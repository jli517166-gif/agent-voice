"""Public voice API, OpenAI-compatible TTS and desktop delivery endpoint."""
import argparse
import copy
import json
import hashlib
import os
from pathlib import Path
import threading
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import urlsplit
from state import Store, validate_voice
from pipeline import Pipeline
from jobs import Jobs,Cancelled
from harness import CodexTail, HarnessPresence, normalize
from verification import Verification

class Server(ThreadingHTTPServer):
    daemon_threads=True
    request_queue_size=128
    def __init__(self,store,pipeline=None):
        data=store.get()
        super().__init__(("127.0.0.1",int(data["port"])),Handler)
        self.store=store; self.pipeline=pipeline or Pipeline(store); self.jobs=Jobs(store,self.pipeline)
        self.instance_id=hashlib.sha256(str(store.root.resolve()).lower().encode('utf-8')).hexdigest()[:24]
        self.verification=Verification(self.jobs)
        self.last_events={}; self.presence=HarnessPresence(); self.api_lock=threading.RLock(); self.stopping=threading.Event()
        self.voice_change_lock=threading.Lock();self.voice_revision=0
        self.codex=CodexTail(self.ingest,lambda:self.store.get()["harness"]["codex"],root=self.codex_home())
        self.codex.start()
        threading.Thread(target=self.cleanup_loop,daemon=True).start()
        if hasattr(self.pipeline,"prepare"):
            threading.Thread(target=self.prewarm_voice,name="voice-prewarm",daemon=True).start()

    def prewarm_voice(self):
        epoch=self.jobs.generation
        try:
            self.pipeline.prepare(self.store.get()["voice"],lambda:self.jobs.check({'generation':epoch}))
            if epoch==self.jobs.generation and self.jobs.current is None:self.jobs.last_error=None
        except Cancelled:pass
        except Exception as exc:
            if epoch==self.jobs.generation:self.jobs.last_error="声音预热失败："+str(exc)

    def recover_voice(self):
        with self.api_lock:
            self.jobs.stop();deadline=time.monotonic()+10
            while self.jobs.current is not None:
                if time.monotonic()>deadline:raise RuntimeError('当前任务尚未退出，请关闭并重新启动语音服务')
                time.sleep(.05)
            self.pipeline.close();self.jobs.last_error=None;self.verification.clear()
            threading.Thread(target=self.prewarm_voice,name='voice-recovery',daemon=True).start()
            return {'ok':True,'state':'loading','message':'已清空队列并重新加载当前声音，请等待模型就绪'}

    def activate_voice(self):
        """A switch is a cancellation boundary; queued old-voice work must never replay."""
        self.voice_revision+=1;revision=self.voice_revision
        with self.jobs.lock:
            self.jobs.switching=True;self.jobs.stop();self.jobs.last_error=None
            epoch=self.jobs.generation
        if hasattr(self.pipeline,'invalidate'):self.pipeline.invalidate()
        else:self.pipeline.loaded=False
        def activate():
            def check():
                self.jobs.check({'generation':epoch})
                if revision!=self.voice_revision:raise Cancelled('声音已再次切换')
            try:
                with self.voice_change_lock:
                    check();deadline=time.monotonic()+20
                    while self.jobs.current is not None:
                        check()
                        if time.monotonic()>deadline:raise RuntimeError('旧声音任务未能退出，请恢复服务')
                        time.sleep(.05)
                    check();self.pipeline.close();check()
                    if hasattr(self.pipeline,'prepare'):self.pipeline.prepare(self.store.get()['voice'],check)
            except Cancelled:pass
            except Exception as exc:
                if revision==self.voice_revision:self.jobs.last_error='新声音加载失败：'+str(exc)
            finally:
                if revision==self.voice_revision:self.jobs.switching=False
        threading.Thread(target=activate,name='voice-switch',daemon=True).start()

    def codex_home(self):
        path=self.store.root/'data/harness-installations.json'
        if path.is_file():
            records=json.loads(path.read_text(encoding='utf8'))
            home=records.get('codex',{}).get('codexHome')
            if home and (Path(home)/'sessions').is_dir():return home
        return None

    def reload_codex(self):
        self.codex.close()
        self.codex=CodexTail(self.ingest,lambda:self.store.get()['harness']['codex'],root=self.codex_home())
        self.codex.start();self.verification.clear()

    def ingest(self,source,event):
        if not self.store.get()["harness"].get(source):
            return {"ok":True,"skipped":True,"reason":"该 Harness 未启用"}
        message=normalize(source,event)
        if not message:return {"ok":True,"skipped":True,"reason":"不是可朗读的助手正文"}
        self.presence.receipt(source)
        try:
            with self.api_lock:result=self.jobs.submit(message["text"],source,True,message["eventId"])
            self.verification.receipt(source,message['text'],result)
            self.last_events[source]={"time":time.time(),"status":"received","result":result}
            return result
        except (ValueError,RuntimeError) as exc:
            self.verification.receipt(source,message['text'],{'ok':False,'error':str(exc)})
            self.last_events[source]={"time":time.time(),"status":"unconfigured","error":str(exc)}
            return {"ok":False,"error":str(exc)}

    def cleanup_loop(self):
        while not self.stopping.wait(self.store.get()["cleanup"]["intervalSeconds"]):
            try:self.jobs.cleanup()
            except OSError as exc:self.jobs.last_error=str(exc)

    def status(self):
        missing=self.store.missing()
        voice=self.store.get()['voice']
        model=self.pipeline.snapshot(voice) if hasattr(self.pipeline,'snapshot') else {'ready':self.pipeline.loaded,'state':'ready' if self.pipeline.loaded else 'not_loaded','error':None}
        verification=self.verification.snapshot_all()
        with self.jobs.lock:
            current=self.jobs.public(self.jobs.current) if self.jobs.current else None
            job=self.jobs.current
            # Busy also includes synthesis-only work. Animate only playback
            # jobs, and stop immediately when the cancellation epoch changes.
            playback={"state":"idle"}
            if job and job.get("play") and job.get("generation")==self.jobs.generation:
                state=job.get("state")
                if state=='playing' and job.get('output')!='airi' and getattr(self.pipeline,'playing',True) is False:state='generating'
                if state in ("queued","generating","playing"):
                    playback={"state":state,"jobId":job["id"],"source":job["source"]}
            elif not job and self.jobs.queue.qsize():playback={"state":"queued"}
            error=self.jobs.last_error or model.get('error')
            activity=playback['state'] if playback['state'] in ('generating','playing','queued') else 'generating' if job and job.get('generation')==self.jobs.generation and job.get('state')=='generating' else None
            state='unconfigured' if missing else 'loading' if self.jobs.switching else 'error' if error and current is None else activity or model['state']
            return {"service":"agent-voice-public","version":1,"pid":os.getpid(),"instanceId":self.instance_id,
                "ready":bool(model['ready'] and not missing and not self.jobs.switching),"resourcesReady":not missing,"missing":missing,"serviceState":state,"modelState":model['state'],
                "engine":voice['engine'],"variant":voice.get('gptVariant') if voice['engine']=='gpt-sovits' else None,"switching":self.jobs.switching,"voiceRevision":self.voice_revision,
                "busy":current is not None,"current":current,"queued":self.jobs.queue.qsize(),"playback":playback,
                "error":error,"gpu":self.pipeline.gpu,
                "airiConnected":self.store.get()['airi']['enabled'] and time.monotonic()-self.jobs.airi_seen<8,"generation":self.jobs.generation,
                "harnessEvents":copy.deepcopy(self.last_events),
                "verification":verification,
                "harnessConnections":self.presence.snapshot(self.store.get()['harness'],self.codex.monitor_ready and self.codex.thread.is_alive())}

    def shutdown_owned(self):
        self.stopping.set(); self.codex.close(); self.jobs.stop(); self.shutdown()

class Handler(BaseHTTPRequestHandler):
    def log_message(self,*args):pass

    def reply(self,status,body,mime="application/json; charset=utf-8"):
        if not isinstance(body,bytes):body=json.dumps(body,ensure_ascii=False).encode("utf-8")
        try:
            self.send_response(status)
            self.send_header("Content-Type",mime); self.send_header("Content-Length",str(len(body)))
            self.send_header("Access-Control-Allow-Origin","*")
            self.send_header("Access-Control-Allow-Headers","Authorization,Content-Type")
            self.send_header("Access-Control-Allow-Methods","GET,POST,OPTIONS")
            self.send_header("Cache-Control","no-store")
            self.end_headers()
            self.wfile.write(body)
        except (BrokenPipeError,ConnectionResetError,ConnectionAbortedError):pass

    def do_OPTIONS(self):self.reply(204,b"")

    def authorized(self):
        expected="Bearer "+self.server.store.get()["token"]
        import hmac
        if not hmac.compare_digest(self.headers.get("Authorization",""),expected):
            # Drain an ordinary bounded POST body before closing the connection.
            # Windows may otherwise reset TCP before the client receives the 401.
            if self.command=='POST':
                try:
                    length=int(self.headers.get('Content-Length','0'))
                    if 0<length<=128*1024:
                        self.connection.settimeout(2);self.rfile.read(length)
                except (ValueError,OSError):pass
            self.reply(401,{"error":{"message":"本地连接密钥不匹配","type":"authentication_error"}})
            return False
        return True

    def do_GET(self):
        path=urlsplit(self.path).path
        if path=="/health":
            try:
                status=self.server.status()
                import hmac
                if not hmac.compare_digest(self.headers.get('Authorization',''),'Bearer '+self.server.store.get()['token']):
                    # Public discovery must not expose spoken text, verification
                    # challenges or private file paths to arbitrary web pages.
                    status={key:status[key] for key in ('service','version','instanceId','ready','serviceState','busy','queued')}
                return self.reply(200,status)
            except Exception as exc:return self.reply(503,{'error':{'message':'服务状态暂时不可用：'+str(exc)}})
        if not self.authorized():return
        try:
            if path=="/api/state":return self.reply(200,{"config":self.server.store.get(),"status":self.server.status()})
            if path=='/api/engines':
                from engine_registry import CATALOG
                return self.reply(200,CATALOG)
            if path=="/v1/models":return self.reply(200,{"object":"list","data":[{"id":"agent-voice-tts","object":"model","owned_by":"local"}]})
            if path.startswith("/api/jobs/"):return self.reply(200,self.server.jobs.get(path.rsplit("/",1)[1]))
            if path.startswith('/api/verify/'):return self.reply(200,self.server.verification.snapshot(path.rsplit('/',1)[1]))
            if path.startswith("/audio/"):
                name=path[len("/audio/"):]
                import re
                if not re.fullmatch(r"[a-f0-9]{32}\.wav",name):raise ValueError("无效音频文件名")
                target=self.server.jobs.output/name
                with self.server.jobs.lock:
                    self.server.jobs.protect(target)
                    try:audio=target.read_bytes()
                    finally:self.server.jobs.release(target)
                return self.reply(200,audio,"audio/wav")
            return self.reply(404,{"error":{"message":"接口不存在"}})
        except FileNotFoundError:self.reply(404,{"error":{"message":"音频已被清理"}})
        except (ValueError,KeyError) as exc:self.reply(400,{"error":{"message":str(exc)}})

    def do_POST(self):
        if not self.authorized():return
        path=urlsplit(self.path).path
        try:
            self.connection.settimeout(15)
            size=int(self.headers.get("Content-Length","0"))
            if not 0<size<=128*1024:raise ValueError("请求大小无效")
            data=json.loads(self.rfile.read(size).decode("utf-8-sig"))
            if not isinstance(data,dict):raise ValueError("请求必须为对象")
            if path in ('/api/profiles/save','/api/profiles/apply','/api/profiles/seal'):
                from voice_profiles import save_profile,apply_profile,seal_profile
                with self.server.api_lock:
                    if path.endswith('/save'):result=save_profile(self.server.store,data.get('name'),data.get('voice'))
                    elif path.endswith('/seal'):result=seal_profile(self.server.store,data.get('id'))
                    else:
                        result=apply_profile(self.server.store,data.get('id'));self.server.activate_voice()
                        self.server.verification.clear()
                return self.reply(200,result)
            if path=='/api/recover':return self.reply(200,self.server.recover_voice())
            if path=='/api/diagnostics':
                from diagnostics import inspect_installation
                return self.reply(200,inspect_installation(self.server.store,data.get('deep') is True))
            if path=='/api/connections/reload':
                self.server.reload_codex();return self.reply(200,{'ok':True,'verified':False})
            if path.startswith('/api/verify/'):
                source=path.rsplit('/',1)[1];config=self.server.store.get()
                if source not in (*config['harness'],'airi'):raise ValueError('未知接入来源')
                if source=='airi':
                    if not config['airi']['enabled'] or time.monotonic()-self.server.jobs.airi_seen>=8:
                        raise ValueError('请启用联动并打开已登录的 AIRI，等待连接后再验证')
                    item=self.server.verification.start(source)
                    result=self.server.jobs.submit(item['text'],'airi-verification',True,output='airi')
                    self.server.verification.receipt(source,item['text'],result)
                    return self.reply(200,self.server.verification.snapshot(source))
                if not config['harness'][source]:raise ValueError('请先启用并保存该 Harness')
                return self.reply(200,self.server.verification.start(source))
            if path=="/api/config":
                with self.server.api_lock:
                    previous=self.server.store.get()
                    if "voice" in data:
                        from state import merge
                        voice=merge(self.server.store.get()["voice"],data["voice"])
                        validate_voice(voice)
                        if voice['engine']=='cosyvoice' and voice['baseModel']:
                            from tts_bundle import model_ready
                            model_ready(self.server.store.resolve(voice['baseModel']))
                        if voice['engine']!='cosyvoice':
                            from local_tts import worker_config,validate_config
                            validate_config(worker_config(self.server.store,voice))
                        if voice['reference']:
                            file=self.server.store.resolve(voice['reference'])
                            if not file.is_file() or file.stat().st_size>40*1024**2:raise ValueError('参考录音不存在或超过 40 MB')
                    config=self.server.store.update(data)
                    if config['voice']!=previous['voice'] or config['engines']!=previous['engines']:
                        self.server.activate_voice()
                    if any(config[k]!=previous[k] for k in ('voice','engines','harness','airi')):
                        self.server.verification.clear()
                return self.reply(200,{"ok":True,"config":config})
            if path=="/api/cleanup":return self.reply(200,self.server.jobs.cleanup(data.get("all") is True))
            if path=="/stop":self.server.jobs.stop();return self.reply(200,{"ok":True})
            if path=="/api/shutdown":
                self.reply(200,{"ok":True});threading.Thread(target=self.server.shutdown_owned,daemon=True).start();return
            if path.startswith("/api/harness/"):
                if path.endswith('/heartbeat'):
                    source=path[len('/api/harness/'):-len('/heartbeat')]
                    enabled=self.server.store.get()['harness']
                    if source not in enabled:raise ValueError('未知 Harness')
                    if not enabled[source]:return self.reply(200,{'ok':True,'skipped':True})
                    self.server.presence.heartbeat(source)
                    return self.reply(200,{'ok':True})
                return self.reply(200,self.server.ingest(path.rsplit("/",1)[1],data))
            if path=="/api/airi/poll":return self.reply(200,self.server.jobs.airi_poll(str(data["client"])))
            if path=="/api/airi/ack":
                self.server.jobs.airi_ack(str(data["client"]),str(data["id"]),data.get("error"));return self.reply(200,{"ok":True})
            if path=="/api/play":
                ident=data.get("jobId");old=self.server.jobs.get(ident)
                if old["state"]!="completed":raise ValueError("这条语音尚未生成完成")
                # Use the exact text and original saved voice, never the editor's
                # current contents or an unrelated subsequently selected model.
                original=self.server.jobs.jobs[ident]
                return self.reply(200,self.server.jobs.submit(old["text"],"manual-play",True,voice=original["voice"],existing=old["result"]))
            if path in ("/speak","/v1/audio/speech"):
                voice=None
                if path=="/v1/audio/speech":
                    if data.get("model") not in ("agent-voice-tts","tts-1"):raise ValueError("TTS 模型 ID 应为 agent-voice-tts")
                    if data.get("response_format","wav") not in ("wav","mp3"):raise ValueError("支持 WAV 或 MP3 音频")
                    voice_id=data.get("voice","current")
                    if not isinstance(voice_id,str):raise ValueError("音色 ID 无效")
                    current=self.server.store.get()
                    if voice_id not in ("current","alloy"):
                        candidates=[p for p in current["profiles"] if p["id"]==voice_id]
                        if not candidates:raise ValueError("已保存声音配置不存在")
                        voice=copy.deepcopy(candidates[0]["voice"])
                    else:voice=copy.deepcopy(current["voice"])
                    if "speed" in data:
                        from state import number
                        number(data["speed"],.7,1.3,"语速")
                        # AIRI's default 1 means use the saved profile speed.
                        voice["speed"]=max(.7,min(1.3,voice["speed"]*data["speed"]))
                    with self.server.api_lock:result=self.server.jobs.submit(data.get("input"),"airi-chat",False,voice=voice)
                    if result.get("skipped"):
                        import io,wave
                        buffer=io.BytesIO()
                        with wave.open(buffer,"wb") as wav:wav.setparams((1,2,24000,0,"NONE",""));wav.writeframes(b"\0\0"*2400)
                        return self.reply(200,buffer.getvalue(),"audio/wav")
                    job=self.server.jobs.jobs[result["jobId"]]
                    from audio_limits import MAX_JOB_SECONDS
                    if not job["done"].wait(MAX_JOB_SECONDS):raise RuntimeError("语音生成超时")
                    if job["state"]!="completed":raise RuntimeError(job["error"] or "语音生成失败")
                    file=self.server.jobs.output/job["result"]["file"]
                    with self.server.jobs.lock:
                        self.server.jobs.protect(file)
                        try:audio=file.read_bytes()
                        finally:self.server.jobs.release(file)
                    if data.get("response_format")=="mp3":
                        import subprocess
                        from pipeline import hidden_process
                        ffmpeg=self.server.store.resolve(current["engines"]["ffmpeg"])
                        if not ffmpeg.is_file():raise RuntimeError("MP3 编码器不存在，请在本地引擎设置中配置 FFmpeg")
                        command=[str(ffmpeg),"-hide_banner","-loglevel","error","-i","pipe:0","-f","mp3","pipe:1"]
                        conversion=subprocess.run(command,input=audio,stdout=subprocess.PIPE,stderr=subprocess.PIPE,**hidden_process())
                        if conversion.returncode:raise RuntimeError("MP3 编码失败")
                        return self.reply(200,conversion.stdout,"audio/mpeg")
                    return self.reply(200,audio,"audio/wav")
                with self.server.api_lock:result=self.server.jobs.submit(data.get("text"),str(data.get("source","manual")),data.get("play") is True,data.get("eventId"))
                return self.reply(200,result)
            return self.reply(404,{"error":{"message":"接口不存在"}})
        except (ValueError,KeyError,TypeError,json.JSONDecodeError) as exc:self.reply(400,{"error":{"message":str(exc)}})
        except RuntimeError as exc:self.reply(503,{"error":{"message":str(exc)}})
        except Exception as exc:
            import traceback
            traceback.print_exc();self.reply(500,{"error":{"message":str(exc)}})

def main():
    parser=argparse.ArgumentParser();parser.add_argument("--root",required=True);args=parser.parse_args()
    from operation_lock import OperationLock
    with OperationLock(args.root):serve(args.root)

def serve(root):
    store=Store(root);server=Server(store)
    print("Public voice service listening on 127.0.0.1:"+str(store.get()["port"]),flush=True)
    try:server.serve_forever()
    finally:server.stopping.set();server.codex.close();server.jobs.close();server.server_close()

if __name__=="__main__":main()
