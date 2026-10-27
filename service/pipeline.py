"""Isolated public synthesis; no fallback to the accepted private profile."""
import copy
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
import threading
import uuid
from state import atomic_json

HERE=Path(__file__).resolve().parent

# Only these values affect the loaded TTS worker/model.  Runtime controls such
# as speed and volume are copied into each queued job and must not tear down a
# healthy model or cancel work that is already in progress.
MODEL_VOICE_FIELDS=(
    'engine','baseModel','gptModel','gptVariant','reference','referenceText',
    'instruction','leadingSilence','style','advancedEnabled',
)

def model_voice(voice):
    value=effective_voice(voice)
    return {key:copy.deepcopy(value.get(key)) for key in MODEL_VOICE_FIELDS}

def requires_reload(previous,current):
    return model_voice(previous)!=model_voice(current)

def effective_voice(voice):
    """Bypass tuning without destroying the user's saved values or reference."""
    value=copy.deepcopy(voice)
    if not value.get('advancedEnabled',True):
        value.update(instruction='',style='chat',leadingSilence=1,volume=1)
    return value

def hidden_process():
    if os.name != "nt": return {}
    startup=subprocess.STARTUPINFO()
    startup.dwFlags |= subprocess.STARTF_USESHOWWINDOW
    startup.wShowWindow=subprocess.SW_HIDE
    return {"creationflags":subprocess.CREATE_NO_WINDOW,"startupinfo":startup}

class Pipeline:
    def __init__(self,store):
        self.store=store;self.adapter=None;self.engine=None;self.loaded_key=None;self.last_play_started=None
        self.loaded=False;self.gpu=None;self.state='not_loaded';self.error=None;self.playing=False
        self.play_lock=threading.RLock();self.prepare_lock=threading.RLock()

    def signature(self,voice):
        voice=effective_voice(voice)
        from engine_registry import worker_config,gpt_required,model_directory
        cfg=self.store.get();reference=self.store.resolve(voice['reference']);model=model_directory(self.store,voice)
        files=([reference]+gpt_required(worker_config(self.store,voice))[1:]) if voice['engine']=='gpt-sovits' else [reference]+[model/n for n in ['llm.pt','flow.pt','hift.pt','cosyvoice3.yaml']]
        stats=[]
        for p in files:
            if not p:continue
            try:s=p.stat();stats.append((str(p),s.st_size,s.st_mtime_ns))
            except FileNotFoundError:pass
        return json.dumps([model_voice(voice),cfg['engines'],stats],sort_keys=True)

    def snapshot(self,voice):
        adapter=self.adapter;alive=adapter is not None and adapter.alive()
        ready=self.loaded and alive and self.loaded_key==self.signature(voice)
        error=self.error
        if self.loaded and not alive:error=error or 'TTS 工作进程已退出，请点击恢复声音'
        state='error' if error else 'ready' if ready else 'loading' if self.state=='loading' else 'not_loaded'
        return {'state':state,'ready':ready,'error':error,'gpu':self.gpu}

    def invalidate(self):self.loaded=False;self.state='not_loaded';self.error=None

    def prepare(self,voice,check):
        while not self.prepare_lock.acquire(timeout=.1):check()
        try:
            check();missing=self.store.missing(voice)
            if missing:raise ValueError('待配置：'+'、'.join(missing))
            from engine_registry import create_adapter
            voice=effective_voice(voice);signature=self.signature(voice)
            if self.loaded and self.loaded_key==signature and self.adapter and self.adapter.alive():return
            self.state='loading';self.loaded=False;self.error=None
            if self.engine!=voice['engine']:
                if self.adapter:self.adapter.close()
                self.adapter=create_adapter(self.store,voice['engine']);self.engine=voice['engine']
            self.adapter.prepare(voice,check);check()
            self.gpu=self.adapter.gpu;self.loaded_key=signature;self.loaded=True;self.state='ready'
        except Exception as exc:
            from jobs import Cancelled
            self.loaded=False;self.state='not_loaded' if isinstance(exc,Cancelled) else 'error';self.error=None if isinstance(exc,Cancelled) else str(exc);raise
        finally:self.prepare_lock.release()

    def synthesize(self,text,voice,target,check):
        voice=effective_voice(voice);base=target.with_suffix('.base.wav')
        while not self.prepare_lock.acquire(timeout=.1):check()
        try:
            from synthesis_recovery import retryable,validate_audio
            recovered=[]
            for attempt in range(3):
                check()
                try:
                    self.prepare(voice,check)
                    attempt_voice=copy.deepcopy(voice);attempt_voice['_retryAttempt']=attempt
                    self.adapter.synthesize(text,attempt_voice,base,check);check()
                    rate,duration=validate_audio(base)
                    os.replace(base,target)
                    from audio_policy import mark_synthesized_wav
                    mark_synthesized_wav(target)
                    return {'sampleRate':rate,'duration':duration,'synthesisMode':'base','engine':voice['engine'],'variant':voice['gptVariant'] if voice['engine']=='gpt-sovits' else None,'advancedEnabled':voice.get('advancedEnabled',False),'attempts':attempt+1,'recoveredErrors':recovered}
                except Exception as exc:
                    check()
                    if attempt==2 or not retryable(exc):raise
                    recovered.append(str(exc)[:300]);base.unlink(missing_ok=True)
                    # Release the failed worker and GPU allocation before retrying
                    # the same engine/voice with a different sampling seed.
                    if self.adapter:self.adapter.close()
                    self.invalidate()
        except Exception as exc:
            from jobs import Cancelled
            self.loaded=False;self.state='not_loaded' if isinstance(exc,Cancelled) else 'error';self.error=None if isinstance(exc,Cancelled) else str(exc);raise
        finally:
            base.unlink(missing_ok=True);self.prepare_lock.release()

    def concatenate(self,paths,target,check):
        import soundfile as sf
        import numpy as np
        from audio_limits import MAX_AUDIO_SECONDS
        if not paths:raise RuntimeError("渐进语音没有生成有效片段")
        rate=sf.info(paths[0]).samplerate;frames=0
        temporary=target.with_suffix('.joining.wav')
        try:
            with sf.SoundFile(temporary,'w',samplerate=rate,channels=1,subtype='PCM_16') as output:
                for path in paths:
                    check()
                    with sf.SoundFile(path) as incoming:
                        if incoming.channels!=1:raise RuntimeError("渐进语音片段必须是单声道")
                        if incoming.samplerate!=rate:raise RuntimeError("渐进语音片段采样率不一致")
                        for block in incoming.blocks(blocksize=65536,dtype='float32'):
                            check();frames+=len(block)
                            if frames/rate>MAX_AUDIO_SECONDS:raise RuntimeError("朗读内容生成后超过 30 分钟，请分次朗读")
                            if not np.isfinite(block).all():raise RuntimeError("语音片段含无效数值")
                            output.write(block)
            if not frames:raise RuntimeError("渐进语音没有生成有效片段")
            os.replace(temporary,target)
            from audio_policy import mark_joined_wav
            mark_joined_wav(target,paths)
            return rate,frames/rate
        finally:temporary.unlink(missing_ok=True)

    def play(self,path,voice,check):
        voice=effective_voice(voice)
        self.last_play_started=None
        import numpy as np
        import sounddevice as sd
        import soundfile as sf
        from audio_output import resample
        audio,rate=sf.read(path,dtype="float32")
        device_rate=int(sd.query_devices(kind="output")["default_samplerate"])
        if rate!=device_rate:
            audio=resample(audio,rate,device_rate)
        with self.play_lock:
            check()
            sd.play(np.clip(audio*voice["volume"],-1,1),device_rate)
            import time
            self.last_play_started=time.monotonic()
            self.playing=True
        try:
            while sd.get_stream().active:
                check()
                import time
                time.sleep(.05)
            return device_rate
        finally:
            self.playing=False

    def stop(self):
        with self.play_lock:
            self.playing=False
            if "sounddevice" in sys.modules:
                sys.modules["sounddevice"].stop()

    def close(self):
        self.stop()
        if not self.prepare_lock.acquire(timeout=8):raise RuntimeError('模型仍在退出，请稍后重试恢复或关闭服务')
        try:
            self.invalidate()
            if self.adapter:self.adapter.close()
            self.adapter=None;self.engine=None;self.loaded_key=None;self.invalidate()
        finally:self.prepare_lock.release()
