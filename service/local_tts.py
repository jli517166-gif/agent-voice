"""Validated GPT-SoVITS variants in an isolated, persistent worker."""
from pathlib import Path
import json
from tts_process import TtsProcess

from engine_registry import ENGINE_IDS,worker_config,gpt_required as required_files

def validate_config(cfg):
    for p in [Path(cfg['python']),*required_files(cfg)]:
        if not p.is_file():raise ValueError('TTS 文件缺失：'+str(p))
    reference=Path(cfg['reference'])
    if not reference.is_file() or reference.stat().st_size>40*1024**2:raise ValueError('请选择小于 40 MB 的参考录音')
    if not cfg['referenceText'].strip():raise ValueError('请填写参考录音逐字台词')
    from reference_audio import validate_gpt_reference
    validate_gpt_reference(reference,cfg.get('ffmpeg'))

class LocalTtsClient(TtsProcess):
    ready_timeout=180
    synthesis_timeout=180
    def __init__(self,config,config_file,log,process_kwargs=None):
        self.config=dict(config);self.config_file=Path(config_file);self.label=config['engine']
        super().__init__(config['python'],config['model'],log,process_kwargs)
    def validate_model(self):validate_config(self.config)
    def ensure_ready(self,check):
        super().ensure_ready(check)
        actual=getattr(self,'ready_info',{})
        if actual.get('engine')!=self.config['engine'] or actual.get('variant')!=self.config['variant'] or Path(actual.get('model','')).resolve()!=self.model.resolve():
            self.close();raise ValueError('TTS 工作进程实际加载的引擎、版本或模型目录与当前选择不一致')
    def command(self):return [str(self.python),'-X','utf8',str(Path(__file__).with_name('local_tts_worker.py')),'--config',str(self.config_file)]
    def request(self,text,target,speed):return {'text':text,'output':str(target),'speed':speed}
