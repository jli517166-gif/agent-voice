import copy,hashlib,json
from pathlib import Path
from state import atomic_json
from pipeline import hidden_process
HERE=Path(__file__).resolve().parent

class Adapter:
    def __init__(self,store):self.store=store;self.client=None;self.key=None
    def prepare(self,voice,check):
        from local_tts import worker_config,LocalTtsClient,required_files
        cfg=worker_config(self.store,voice)
        files=[Path(cfg['reference']),*required_files(cfg)]
        key=json.dumps(cfg,sort_keys=True)+str([(str(p),p.stat().st_size,p.stat().st_mtime_ns) for p in files])
        if self.key!=key:
            if self.client:self.client.close()
            config=self.store.root/'data/cache/local-tts'/('config-'+hashlib.sha256(key.encode()).hexdigest()+'.json')
            atomic_json(config,cfg)
            self.client=LocalTtsClient(cfg,config,self.store.root/('data/logs/'+voice['engine']+'-worker.log'),hidden_process());self.key=key
        self.client.ensure_ready(check)

    def synthesize(self,text,voice,target,check):
        return self.client.synthesize(text,target,voice['speed'],check,seed=42+voice.get('_retryAttempt',0))
    @property
    def gpu(self):return getattr(self.client,'gpu',None)
    def alive(self):
        process=getattr(self.client,'process',None);return process is not None and process.poll() is None
    def close(self):
        if self.client:self.client.close()
        self.client=None;self.key=None
