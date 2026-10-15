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

def effective_voice(voice):
    """Bypass tuning without destroying the user's saved values or reference."""
    value=copy.deepcopy(voice)
    if not value.get('advancedEnabled',True):
        value.update(indexRate=0,rmsMix=1,f0Method='rmvpe',protect=.33,
                     instruction='',style='chat',leadingSilence=1,volume=1)
    return value

def hidden_process():
    if os.name != "nt": return {}
    startup=subprocess.STARTUPINFO()
    startup.dwFlags |= subprocess.STARTF_USESHOWWINDOW
    startup.wShowWindow=subprocess.SW_HIDE
    return {"creationflags":subprocess.CREATE_NO_WINDOW,"startupinfo":startup}

class Pipeline:
    def __init__(self,store):
        self.store=store; self.rvc=None; self.rvc_key=None; self.tts=None; self.tts_key=None
        self.piper=None; self.piper_key=None
        self.loaded=False; self.gpu=None; self.playing=False; self.play_lock=threading.RLock();self.prepare_lock=threading.RLock()

    def prepare(self,voice,check):
        with self.prepare_lock:return self._prepare(voice,check)

    def _prepare(self,voice,check):
        missing=self.store.missing(voice)
        if missing: raise ValueError("待配置："+"、".join(missing))
        import torch
        torch.set_num_threads(4)
        if not torch.cuda.is_available():
            raise RuntimeError("首版需要 NVIDIA 显卡和可用的 CUDA 驱动")
        self.gpu=torch.cuda.get_device_name(0)
        voice=effective_voice(voice)
        data=self.store.get(); paths=copy.deepcopy(data["engines"])
        if voice['engine']!='piper' and self.piper:
            self.piper.close();self.piper=None;self.piper_key=None
        paths['cosyModel']=voice.get('baseModel') or paths['cosyModel']
        home=self.store.resolve(paths['rvcHome'])
        if voice.get('synthesisMode','rvc')=='base' and self.rvc is not None:
            self.rvc=None;self.rvc_key=None
            import gc
            gc.collect();torch.cuda.empty_cache()
        if voice.get('synthesisMode','rvc')=='rvc':
            if str(home) not in sys.path: sys.path.insert(0,str(home))
            # rtrvc reads its generic Hubert and RMVPE assets relative to cwd.
            os.chdir(home)
            os.environ["rmvpe_root"]=str(home/"assets/rmvpe")
            os.environ["weight_root"]=str(self.store.root/"models")
            os.environ["index_root"]=str(self.store.root/"models")
            os.environ["outside_index_root"]=str(self.store.root/"models")
            os.environ["PATH"]=str(home)+os.pathsep+os.environ.get("PATH","")
        cfg={"model":str(self.store.resolve(voice["model"])),"index":str(self.store.resolve(voice["index"])) if voice["index"] else "",
             "pitch":voice["pitch"],"formant":voice["formant"],"index_rate":voice["indexRate"],
             "rms_mix_rate":voice["rmsMix"],"f0_method":voice["f0Method"],"device":"cuda:0", "protect":voice["protect"]}
        key=(voice['processing'],cfg["model"],Path(cfg["model"]).stat().st_mtime_ns,cfg["index"],cfg["index_rate"],str(home)) if voice.get('synthesisMode','rvc')=='rvc' else None
        check()
        if key is not None and self.rvc_key != key:
            if voice['processing'] == 'accepted':
                from rvc_accepted import AcceptedEngine
                replacement=AcceptedEngine(cfg)
            else:
                from rvc_tone import ToneEngine
                previous=self.rvc if self.rvc_key and self.rvc_key[0]=='standard' else None
                replacement=ToneEngine(cfg,previous)
            check()
            self.rvc=replacement; self.rvc_key=key
        if voice["engine"]=="cosyvoice":
            reference=self.store.resolve(voice["reference"])
            reference_stat=reference.stat()
            if reference_stat.st_size>40*1024*1024:
                raise ValueError("参考音频过大，请使用清晰的短句录音")
            conditioning={"referenceText":voice["referenceText"],"instruction":voice["instruction"],
                "leadingSilence":voice["leadingSilence"],"style":voice['style'],"engines":{k:paths[k] for k in ('pythonTts','cosyRepo','cosyModel')}}
            if not voice.get('advancedEnabled',True):conditioning['advancedEnabled']=False
            if voice.get('baseModel'):
                directory=self.store.resolve(paths['cosyModel'])
                conditioning['modelRevision']=[(p.name,p.stat().st_size,p.stat().st_mtime_ns) for p in sorted(directory.iterdir()) if p.suffix in ('.pt','.yaml')]
            fingerprint=hashlib.sha256(reference.read_bytes()+json.dumps(conditioning,sort_keys=True).encode()).hexdigest()
            if self.tts_key != fingerprint:
                if self.tts: self.tts.close()
                profile={"repo":str(self.store.resolve(paths["cosyRepo"])),"model_dir":str(self.store.resolve(paths["cosyModel"])),
                    "half_weights":True,"references":{"public":{"wav":str(reference),"text":voice["referenceText"]}},
                    "profiles":{voice['style']:{"reference":"public","mode":"instruct2" if voice["instruction"].strip() else "zero_shot",
                         "instruction":voice["instruction"],"leading_silence_limit_sec":voice["leadingSilence"] if voice.get("advancedEnabled",True) else None}}}
                config=self.store.root/"data/cache"/fingerprint/"profile.json"
                atomic_json(config,profile)
                from cosy_client import NaturalTTSClient
                self.tts=NaturalTTSClient({"port":data["port"]+1,"python":str(self.store.resolve(paths["pythonTts"])),
                     "server":str(HERE/"cosy_worker.py"),"config":str(config)},hidden_process())
                self.tts_key=fingerprint
            self.tts.ensure_ready()
        elif self.tts:
            self.tts.close(); self.tts=None; self.tts_key=None
        if voice['engine']=='piper':
            from piper_client import PiperClient, model_config
            model=self.store.resolve(voice.get('piperModel') or paths['piperModel'])
            companion=model_config(model)
            python=self.store.resolve(paths['pythonPiper'])
            key=(str(python),str(model),model.stat().st_mtime_ns,companion.stat().st_mtime_ns)
            if self.piper_key!=key:
                if self.piper:self.piper.close()
                self.piper=PiperClient(python,model,self.store.root/'data/logs/piper-worker.log',hidden_process())
                self.piper_key=key
            self.piper.ensure_ready(check)
        check(); self.loaded=True
        return cfg

    def synthesize(self,text,voice,target,check):
        voice=effective_voice(voice)
        cfg=self.prepare(voice,check)
        base=target.with_suffix(".base.wav")
        try:
            if voice["engine"]=="cosyvoice":
                self.tts.synthesize(text,base,voice['style'],(voice["speed"]-1)*100)
            elif voice['engine']=='piper':
                self.piper.synthesize(text,base,voice['speed'],check)
            else:
                import tempfile
                with tempfile.TemporaryDirectory() as temp:
                    temp=Path(temp); body=temp/"body.txt"; script=temp/"speak.ps1"
                    body.write_text(text,encoding="utf-8")
                    script.write_text('param([string]$Body,[string]$Output,[int]$Rate)\nAdd-Type -AssemblyName System.Speech\n$s=New-Object System.Speech.Synthesis.SpeechSynthesizer\ntry {$s.Rate=$Rate;$s.SetOutputToWaveFile($Output);$s.Speak([IO.File]::ReadAllText($Body,[Text.Encoding]::UTF8))} finally {$s.Dispose()}\n',encoding="utf-8-sig")
                    result=subprocess.run(["powershell","-NoProfile","-NonInteractive","-ExecutionPolicy","Bypass","-File",str(script),"-Body",str(body),"-Output",str(base),"-Rate",str(round((voice["speed"]-1)*10))],stdout=subprocess.PIPE,stderr=subprocess.PIPE,**hidden_process())
                    if result.returncode: raise RuntimeError("系统底音生成失败")
            check()
            if voice.get('synthesisMode','rvc')=='base':
                import soundfile as sf
                info=sf.info(base)
                os.replace(base,target)
                rate,duration=info.samplerate,info.duration
            else:
                rate,duration=self.rvc.convert(base,cfg,target,check)
            return {"sampleRate":rate,"duration":duration,"synthesisMode":voice.get('synthesisMode','rvc'),"advancedEnabled":voice.get('advancedEnabled',True)}
        finally:
            if base.exists(): base.unlink()

    def concatenate(self,paths,target,check):
        import soundfile as sf
        import numpy as np
        audio=[];rate=None
        for path in paths:
            check();part,part_rate=sf.read(path,dtype="float32",always_2d=False)
            if part.ndim!=1:raise RuntimeError("渐进语音片段必须是单声道")
            if rate is None:rate=part_rate
            if part_rate!=rate:raise RuntimeError("渐进语音片段采样率不一致")
            audio.append(part)
        if not audio:raise RuntimeError("渐进语音没有生成有效片段")
        combined=np.concatenate(audio)
        if len(combined)/rate>420:raise RuntimeError("朗读内容生成后超过 7 分钟，请分次朗读")
        sf.write(target,combined,rate,subtype="PCM_16")
        return rate,len(combined)/rate

    def play(self,path,voice,check):
        voice=effective_voice(voice)
        import numpy as np
        import sounddevice as sd
        import soundfile as sf
        import librosa
        audio,rate=sf.read(path,dtype="float32")
        device_rate=int(sd.query_devices(kind="output")["default_samplerate"])
        if rate!=device_rate:
            audio=librosa.resample(audio,orig_sr=rate,target_sr=device_rate)
        with self.play_lock:
            check()
            sd.play(np.clip(audio*voice["volume"],-1,1),device_rate)
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
        if self.tts: self.tts.close()
        if self.piper: self.piper.close()
