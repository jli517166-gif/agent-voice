"""One serial queue, cancellable generations, explicit AIRI delivery lease."""
import copy
from collections import OrderedDict,Counter
import queue
import threading
import time
import uuid
from state import atomic_json
from text import sanitize,playback_chunks,generation_chunks

class Cancelled(Exception): pass

class Jobs:
    def __init__(self,store,pipeline):
        self.store=store; self.pipeline=pipeline; self.lock=threading.RLock()
        self.queue=queue.Queue(maxsize=64); self.jobs=OrderedDict(); self.seen=OrderedDict()
        self.generation=0; self.current=None; self.closed=False; self.last_error=None;self.switching=False
        self.protected=Counter(); self.airi_client=None; self.airi_seen=0; self.airi_tasks=OrderedDict()
        self.output=store.root/"data/output"; self.output.mkdir(parents=True,exist_ok=True)
        self.thread=threading.Thread(target=self.run,name="voice-serial-worker",daemon=True)
        self.thread.start()

    def submit(self,text,source="manual",play=False,event_id=None,voice=None,existing=None,output=None):
        if not isinstance(text,str) or len(text)>4000: raise ValueError("正文最长 4000 字")
        text=sanitize(text)
        if not text: return {"ok":True,"skipped":True,"reason":"仅链接或代码，跳过朗读"}
        with self.lock:
            if self.closed: raise RuntimeError("服务正在停止")
            if event_id and event_id in self.seen:
                return {"ok":True,"duplicate":True,"jobId":self.seen[event_id]}
            if self.queue.full(): raise RuntimeError("朗读队列已满")
            config=self.store.get(); voice=copy.deepcopy(voice or config["voice"])
            missing=self.store.missing(voice) if existing is None else []
            if missing: raise ValueError("待配置："+"、".join(missing))
            ident=uuid.uuid4().hex
            job={"id":ident,"text":text,"source":source,"voice":voice,"play":bool(play),
                 "output":output or (config["airi"]["output"] if config['airi']['enabled'] else 'local'),"generation":self.generation,"state":"queued",
                 "created":time.time(),"submittedAt":time.monotonic(),"done":threading.Event(),"result":None,"error":None,"existing":copy.deepcopy(existing)}
            if existing:
                path=self.output/existing["file"]
                if not path.is_file():raise ValueError("这条音频已被清理，请重新生成")
                self.protect(path)
            self.jobs[ident]=job; self.queue.put_nowait(job)
            if event_id:
                self.seen[event_id]=ident
                while len(self.seen)>2000:self.seen.popitem(last=False)
            while len(self.jobs)>256:
                candidate=next(iter(self.jobs))
                if not self.jobs[candidate]["done"].is_set(): break
                self.jobs.pop(candidate)
            return {"ok":True,"jobId":ident,"state":"queued"}

    def check(self,job):
        if self.closed or job["generation"]!=self.generation: raise Cancelled("已停止")

    def protect(self,path):self.protected[path]+=1
    def release(self,path):
        self.protected[path]-=1
        if self.protected[path]<=0:del self.protected[path]

    def public(self,job):
        return {key:job.get(key) for key in ("id","text","source","state","created","result","error","segmentsCompleted","totalSegments")}

    def get(self,ident):
        with self.lock:
            if ident not in self.jobs: raise KeyError("任务不存在")
            return self.public(self.jobs[ident])

    def run(self):
        while True:
            job=self.queue.get()
            if job is None: self.queue.task_done(); break
            target=self.output/(job["existing"]["file"] if job["existing"] else job["id"]+".wav")
            try:
                while self.switching:
                    self.check(job);time.sleep(.05)
                with self.lock:
                    self.check(job); self.current=job; job["state"]="generating"
                    if not job["existing"]:self.protect(target)
                from bounded_logs import message
                message("朗读正文："+job["text"])
                chunks=(generation_chunks(job['text']) if not job['play'] else [piece for sentence in playback_chunks(job['text']) for piece in generation_chunks(sentence)]) if not job['existing'] else [job['text']]
                progressive=len(chunks)>1
                result=self.progressive(job,target,chunks) if progressive else (copy.deepcopy(job["existing"]) if job["existing"] else self.pipeline.synthesize(job["text"],job["voice"],target,lambda:self.check(job)))
                self.check(job)
                result.update({"file":target.name,"text":job["text"],"audioUrl":"/audio/"+target.name})
                job["result"]=result
                if job["play"] and not progressive:
                    job["state"]="playing"
                    if job["output"]=="airi" and time.monotonic()-self.airi_seen<8:
                        self.deliver_airi(job)
                        result['output']='airi'
                    else:
                        result["deviceSampleRate"]=self.pipeline.play(target,job["voice"],lambda:self.check(job))
                        self.playback_timing(job,result)
                        result['output']='local'
                        if job['output']=='airi':result['outputNotice']='AIRI 未连接，已由本机播放'
                    result["played"]=True
                self.check(job)
                if not job["existing"]:atomic_json(target.with_suffix(".json"),result)
                job["state"]="completed"; self.last_error=None
            except Cancelled as exc:
                job["state"]="cancelled"; job["error"]=str(exc)
                self.pipeline.stop()
            except Exception as exc:
                job["state"]="failed"; job["error"]=str(exc); self.last_error=str(exc)
                import traceback
                traceback.print_exc()
            finally:
                with self.lock:
                    self.release(target); self.airi_tasks.pop(job["id"],None); self.current=None
                    job["done"].set()
                self.queue.task_done()

    def playback_timing(self,job,result):
        first=getattr(self.pipeline,'last_play_started',None)
        if first is not None:
            result['firstAudioDeviceMs']=round((first-job['submittedAt'])*1000,1)
            result['firstAudioTiming']='device-clock-estimate'

    def progressive(self,job,target,chunks):
        """Generate ahead while a separate consumer plays completed segments."""
        segment_queue=queue.Queue(maxsize=3);parts=[];consumer_error=[];playback={};finished=threading.Event();attempts=[];recovered=[]
        use_airi=job['play'] and job["output"]=="airi" and time.monotonic()-self.airi_seen<8
        job.update(totalSegments=len(chunks),segmentsCompleted=0)
        started=time.monotonic();first_ready=None
        def check_segment():
            self.check(job)
            if consumer_error:raise consumer_error[0]
        def offer(item):
            while True:
                check_segment()
                try:segment_queue.put(item,timeout=.1);return
                except queue.Full:continue
        def consume():
            try:
                while True:
                    if finished.is_set():break
                    self.check(job)
                    try:item=segment_queue.get(timeout=.1)
                    except queue.Empty:continue
                    if item is None:break
                    path,part_text,metadata,index=item
                    if not job['play']:continue
                    if use_airi:self.deliver_airi_segment(job,path,part_text,metadata,index)
                    else:
                        playback["deviceSampleRate"]=self.pipeline.play(path,job["voice"],lambda:self.check(job))
                        if index==0:self.playback_timing(job,playback)
            except Exception as exc:
                consumer_error.append(exc);self.pipeline.stop()
        consumer=threading.Thread(target=consume,name="voice-progressive-playback",daemon=True);consumer.start()
        try:
            for index,part_text in enumerate(chunks):
                check_segment()
                path=self.output/(uuid.uuid4().hex+".wav")
                parts.append(path)
                metadata=self.pipeline.synthesize(part_text,job["voice"],path,lambda:self.check(job))
                job['segmentsCompleted']=index+1
                attempts.append(metadata.get('attempts',1));recovered.extend(metadata.get('recoveredErrors',[]))
                with self.lock:self.protect(path)
                if first_ready is None:
                    first_ready=time.monotonic();job["state"]="playing" if job['play'] else 'generating'
                offer((path,part_text,metadata,index))
            offer(None)
            while consumer.is_alive():
                check_segment();consumer.join(timeout=.1)
            check_segment()
            rate,duration=self.pipeline.concatenate(parts,target,lambda:self.check(job))
            result={"sampleRate":rate,"duration":duration,"synthesisMode":"base",
                    "engine":job['voice']['engine'],"variant":job['voice'].get('gptVariant') if job['voice']['engine']=='gpt-sovits' else None,
                    "advancedEnabled":job["voice"].get("advancedEnabled",True),"progressive":True,
                    "segmentCount":len(chunks),"firstSegmentReadyMs":int(((first_ready or time.monotonic())-started)*1000),
                    "played":bool(job['play']),"output":("airi" if use_airi else "local") if job['play'] else None,"attempts":max(attempts,default=1),"generationAttempts":sum(attempts),"recoveredErrors":recovered}
            if job['play'] and not use_airi and job["output"]=="airi":result["outputNotice"]="AIRI 未连接，已由本机渐进播放"
            result.update(playback);return result
        finally:
            finished.set()
            if consumer.is_alive():
                try:segment_queue.put_nowait(None)
                except queue.Full:pass
                self.pipeline.stop();consumer.join(timeout=3)
            for path in parts:
                with self.lock:
                    if path in self.protected:self.release(path)
                try:path.unlink()
                except FileNotFoundError:pass

    def deliver_airi_segment(self,job,path,text,metadata,index):
        ident=path.stem
        with self.lock:
            if not self.store.get()["airi"]["enabled"] or time.monotonic()-self.airi_seen>8:
                raise RuntimeError("AIRI 播放连接已断开，请切换本机输出后重播")
            self.airi_tasks[ident]={"id":ident,"audioUrl":"/audio/"+path.name,"text":text,
                "generation":job["generation"],"volume":job["voice"]["volume"] if job['voice'].get('advancedEnabled',True) else 1,
                "leased":False,"client":None,"done":threading.Event(),"error":None,"segment":index+1}
            task=self.airi_tasks[ident]
        try:
            deadline=time.monotonic()+metadata["duration"]+120
            while not task["done"].wait(.1):
                self.check(job)
                if time.monotonic()-self.airi_seen>8:raise RuntimeError("AIRI 播放连接已断开，请切换本机输出后重播")
                if time.monotonic()>deadline:raise RuntimeError("AIRI 渐进播放确认超时")
            if task["error"]:raise RuntimeError(task["error"])
        finally:
            with self.lock:self.airi_tasks.pop(ident,None)

    def deliver_airi(self,job):
        with self.lock:
            if not self.store.get()["airi"]["enabled"] or time.monotonic()-self.airi_seen>8:
                raise RuntimeError("AIRI 桌宠未连接，请连接或切回本地播放")
            self.airi_tasks[job["id"]]={"id":job["id"],"audioUrl":job["result"]["audioUrl"],"text":job["text"],
                "generation":job["generation"],"volume":job["voice"]["volume"] if job['voice'].get('advancedEnabled',True) else 1,"leased":False,
                "client":None,"done":threading.Event(),"error":None}
            task=self.airi_tasks[job["id"]]
        deadline=time.monotonic()+job["result"]["duration"]+600
        while not task["done"].wait(.1):
            self.check(job)
            if time.monotonic()-self.airi_seen>8:
                raise RuntimeError('AIRI 播放连接已断开，请切换本机输出后重播')
            if time.monotonic()>deadline: raise RuntimeError("AIRI 播放确认超时")
        self.check(job)
        if task["error"]: raise RuntimeError(task["error"])

    def airi_poll(self,client):
        with self.lock:
            now=time.monotonic()
            if self.airi_client and self.airi_client!=client and now-self.airi_seen<8:
                return {"generation":self.generation,"task":None,"otherClient":True}
            self.airi_client=client; self.airi_seen=now
            for task in self.airi_tasks.values():
                if not task["leased"]:
                    task["leased"]=True; task["client"]=client
                    return {"generation":self.generation,"task":{k:task[k] for k in ("id","audioUrl","text","generation","volume")}}
            return {"generation":self.generation,"task":None}

    def airi_ack(self,client,ident,error=None):
        with self.lock:
            task=self.airi_tasks.get(ident)
            if not task or task["client"]!=client: raise ValueError("桌宠播放任务已失效")
            task["error"]=str(error)[:500] if error else None; task["done"].set()

    def stop(self):
        with self.lock:
            self.generation+=1
            for task in self.airi_tasks.values(): task["error"]="已停止"; task["done"].set()
            while True:
                try:pending=self.queue.get_nowait()
                except queue.Empty:break
                if pending is not None:
                    pending['state']='cancelled';pending['error']='已停止';pending['done'].set()
                    if pending.get('existing'):self.release(self.output/pending['existing']['file'])
                self.queue.task_done()
        self.pipeline.stop()

    def cleanup(self,all_files=False):
        with self.lock:
            policy=self.store.get()["cleanup"]
            files=sorted(self.output.glob("*.wav"),key=lambda p:p.stat().st_mtime,reverse=True)
            total=sum(p.stat().st_size for p in files); removed=0
            retained=set(files[:int(policy["keepLatest"])])
            for path in reversed(files):
                if path in self.protected: continue
                if all_files or (path not in retained and (time.time()-path.stat().st_mtime>policy["maxAgeMinutes"]*60 or total>policy["maxTotalMb"]*1024**2)):
                    total-=path.stat().st_size; path.unlink(); path.with_suffix(".json").unlink(missing_ok=True); removed+=1
            return {"ok":True,"removed":removed,"bytes":total,"directory":str(self.output)}

    def close(self):
        self.closed=True; self.stop()
        self.queue.put_nowait(None)
        self.thread.join(timeout=5)
        self.pipeline.close()
