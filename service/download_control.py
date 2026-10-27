"""Cooperative download pause scoped to one application-owned job."""
import json,os,re,threading,time
from pathlib import Path
from state import atomic_json

class DownloadCancelled(RuntimeError):pass

class DownloadGate:
 def __init__(self,root,token=None):
  token=token if token is not None else os.environ.get('AGENT_VOICE_DOWNLOAD_TOKEN','')
  if token and not re.fullmatch('[a-f0-9]{32}',token):raise ValueError('下载任务编号无效')
  self.prefix=Path(root).resolve()/'data/download-control'/token if token else None
  self.pause_file=Path(str(self.prefix)+'.pause') if token else None
  self.cancel_file=Path(str(self.prefix)+'.cancel') if token else None
  self.state_file=Path(str(self.prefix)+'.json') if token else None
  self.lock=threading.RLock();self.last={}
 @property
 def paused(self):return bool(self.prefix and self.pause_file.exists())
 @property
 def cancelled(self):return bool(self.prefix and self.cancel_file.exists())
 def status(self):
  if not self.prefix:return {}
  try:return json.loads(self.state_file.read_text(encoding='utf-8'))
  except (OSError,ValueError):return {}
 def report(self,message,**data):
  if not self.prefix:return
  with self.lock:
   self.last.update(data);self.last.update(message=message,state=data.get('state','running'),time=time.time())
   atomic_json(self.state_file,self.last)
 def check(self):
  if not self.prefix:return
  paused=False
  while self.paused:
   if self.cancelled:raise DownloadCancelled('下载已停止；已完成文件和可续传断点已保留')
   if not paused:self.report('下载已暂停，可继续下载',state='paused');paused=True
   time.sleep(.05)
  if self.cancelled:raise DownloadCancelled('下载已停止；已完成文件和可续传断点已保留')
  if paused:self.report('正在继续下载',state='running')

def hook_requests(module,gate):
 """Bound both Hugging Face and pip HTTP reads to pause checkpoints."""
 original=module.Response.iter_content
 if getattr(original,'_agent_voice_pause',False):return
 def iterate(response,chunk_size=1,decode_unicode=False):
  source=iter(original(response,min(chunk_size or 65536,65536),decode_unicode))
  while True:
   gate.check()
   try:block=next(source)
   except StopIteration:return
   yield block
 iterate._agent_voice_pause=True
 module.Response.iter_content=iterate
