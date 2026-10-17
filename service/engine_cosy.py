import copy,hashlib,json
from pathlib import Path
from state import atomic_json
from pipeline import hidden_process
HERE=Path(__file__).resolve().parent

class Adapter:
    def __init__(self,store):self.store=store;self.client=None;self.key=None
    def prepare(self,voice,check):
        data=self.store.get();paths=copy.deepcopy(data['engines'])
        paths['cosyModel']=voice.get('baseModel') or paths['cosyModel']
        reference=self.store.resolve(voice["reference"])
        reference_stat=reference.stat()
        if reference_stat.st_size>40*1024*1024:
            raise ValueError("参考音频过大，请使用清晰的短句录音")
        conditioning={"referenceText":voice["referenceText"],"instruction":voice["instruction"],
            "leadingSilence":voice["leadingSilence"],"style":voice['style'],"engines":{k:paths[k] for k in ('pythonTts','cosyRepo','cosyModel')}}
        if not voice.get('advancedEnabled',True):conditioning['advancedEnabled']=False
        directory=self.store.resolve(paths['cosyModel'])
        conditioning['modelRevision']=[(p.name,p.stat().st_size,p.stat().st_mtime_ns) for p in sorted(directory.iterdir()) if p.suffix in ('.pt','.yaml')]
        fingerprint=hashlib.sha256(reference.read_bytes()+json.dumps(conditioning,sort_keys=True).encode()).hexdigest()
        if self.key != fingerprint:
            if self.client: self.client.close()
            profile={"repo":str(self.store.resolve(paths["cosyRepo"])),"model_dir":str(self.store.resolve(paths["cosyModel"])),
                "half_weights":True,"references":{"public":{"wav":str(reference),"text":voice["referenceText"]}},
                "profiles":{voice['style']:{"reference":"public","mode":"instruct2" if voice["instruction"].strip() else "zero_shot",
                     "instruction":voice["instruction"],"leading_silence_limit_sec":voice["leadingSilence"] if voice.get("advancedEnabled",True) else None}}}
            config=self.store.root/"data/cache"/fingerprint/"profile.json"
            atomic_json(config,profile)
            from cosy_client import NaturalTTSClient
            self.client=NaturalTTSClient({"port":data["port"]+1,"python":str(self.store.resolve(paths["pythonTts"])),
                 "server":str(HERE/"cosy_worker.py"),"config":str(config)},hidden_process())
            self.key=fingerprint
        self.client.ensure_ready(check)

    def synthesize(self,text,voice,target,check):
        return self.client.synthesize(text,target,voice['style'],(voice['speed']-1)*100,check,seed=1986+voice.get('_retryAttempt',0))
    @property
    def gpu(self):return getattr(self.client,'gpu',None)
    def alive(self):
        process=getattr(self.client,'process',None);return process is not None and process.poll() is None
    def close(self):
        if self.client:self.client.close()
        self.client=None;self.key=None
