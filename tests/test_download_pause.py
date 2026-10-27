import hashlib,importlib.util,json,sys,tempfile,threading,time,unittest
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'service'))

class DownloadPauseTests(unittest.TestCase):
 def gate(self,root):
  self.assertIsNotNone(importlib.util.find_spec('download_control'),'Download pause control is not implemented')
  from download_control import DownloadGate
  return DownloadGate(root,'a'*32)
 def wait(self,condition):
  end=time.monotonic()+3
  while not condition():
   if time.monotonic()>end:self.fail('download did not reach expected state')
   time.sleep(.01)
 def test_pause_blocks_work_until_resume_and_preserves_progress(self):
  with tempfile.TemporaryDirectory() as tmp:
   gate=self.gate(tmp);gate.pause_file.parent.mkdir(parents=True,exist_ok=True);gate.report('receiving',downloadedBytes=65536,totalBytes=131072)
   gate.pause_file.touch();finished=threading.Event()
   thread=threading.Thread(target=lambda:(gate.check(),finished.set()));thread.start()
   try:
    self.wait(lambda:gate.status().get('state')=='paused');self.assertFalse(finished.wait(.15))
    self.assertEqual(gate.status()['downloadedBytes'],65536)
    gate.pause_file.unlink();self.assertTrue(finished.wait(2))
   finally:
    gate.pause_file.unlink(missing_ok=True);thread.join(2)
 def test_cancel_while_paused_unblocks_without_reporting_success(self):
  with tempfile.TemporaryDirectory() as tmp:
   gate=self.gate(tmp);gate.pause_file.parent.mkdir(parents=True,exist_ok=True);gate.pause_file.touch();errors=[]
   def work():
    try:gate.check()
    except Exception as error:errors.append(error)
   thread=threading.Thread(target=work);thread.start()
   try:
    self.wait(lambda:gate.status().get('state')=='paused');gate.cancel_file.touch();thread.join(2)
    self.assertFalse(thread.is_alive());self.assertEqual(type(errors[0]).__name__,'DownloadCancelled')
   finally:gate.pause_file.unlink(missing_ok=True);thread.join(2)
 def test_stale_other_session_does_not_pause_new_job(self):
  with tempfile.TemporaryDirectory() as tmp:
   gate=self.gate(tmp);gate.pause_file.parent.mkdir(parents=True,exist_ok=True);gate.pause_file.touch()
   from download_control import DownloadGate
   other=DownloadGate(tmp,'b'*32);other.check();self.assertNotEqual(gate.pause_file,other.pause_file)
 def test_invalid_token_cannot_escape_control_directory(self):
  with tempfile.TemporaryDirectory() as tmp:
   self.gate(tmp)
   from download_control import DownloadGate
   with self.assertRaises(ValueError):DownloadGate(tmp,'../../outside')
 def test_http_download_pause_resume_keeps_validated_bytes(self):
  from test_download_repair import DownloadTests
  from resource_download import download
  DownloadTests.setUpClass()
  try:
   with tempfile.TemporaryDirectory() as tmp:
    gate=self.gate(tmp);target=Path(tmp)/'payload';failures=[];armed=threading.Event()
    def progress(**row):
     gate.report('receiving',**row)
     if not armed.is_set():gate.pause_file.touch();armed.set()
    def work():
     try:download('http://127.0.0.1:'+str(DownloadTests.http.server_address[1])+'/file',target,hashlib.sha256(DownloadTests.payload).hexdigest(),chunk_bytes=32768,check=gate.check,progress=progress)
     except Exception as error:failures.append(error)
    thread=threading.Thread(target=work);thread.start()
    try:
     self.wait(lambda:gate.status().get('state')=='paused');partial=target.with_name('payload.part');size=partial.stat().st_size
     time.sleep(.15);self.assertEqual(partial.stat().st_size,size);self.assertFalse(target.exists())
     gate.pause_file.unlink();thread.join(3);self.assertFalse(thread.is_alive());self.assertFalse(failures)
     self.assertEqual(target.read_bytes(),DownloadTests.payload)
    finally:gate.pause_file.unlink(missing_ok=True);thread.join(3)
  finally:DownloadTests.tearDownClass()

 def test_paused_time_does_not_exhaust_child_install_timeout(self):
  import os
  from unittest.mock import patch
  from environment_repair import Repair
  with tempfile.TemporaryDirectory() as tmp,patch.dict(os.environ,{'AGENT_VOICE_DOWNLOAD_TOKEN':'a'*32}):
   helper=Repair(tmp);gate=helper.gate
   code="from pathlib import Path;import time;p=Path("+repr(str(gate.pause_file))+");p.parent.mkdir(parents=True,exist_ok=True);time.sleep(.1);p.touch();time.sleep(.5);p.unlink();time.sleep(.1)"
   helper.run([sys.executable,'-c',code],timeout=.35)
 def test_requests_hook_stops_stream_iteration_at_checkpoint(self):
  import types
  from download_control import hook_requests
  class Response:
   def iter_content(self,chunk_size=1,decode_unicode=False):
    yield b'first';yield b'second'
  with tempfile.TemporaryDirectory() as tmp:
   gate=self.gate(tmp);module=types.SimpleNamespace(Response=Response);hook_requests(module,gate)
   stream=Response().iter_content(10*1024*1024);self.assertEqual(next(stream),b'first')
   gate.pause_file.parent.mkdir(parents=True,exist_ok=True);gate.pause_file.touch();received=[]
   thread=threading.Thread(target=lambda:received.append(next(stream)));thread.start()
   try:
    self.wait(lambda:gate.status().get('state')=='paused');time.sleep(.1);self.assertEqual(received,[])
    gate.pause_file.unlink();thread.join(2);self.assertEqual(received,[b'second'])
   finally:gate.pause_file.unlink(missing_ok=True);thread.join(2)
