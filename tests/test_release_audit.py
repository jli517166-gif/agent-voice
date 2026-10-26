import csv,importlib.util,io,json,os,subprocess,sys,tempfile,time,unittest,wave
from pathlib import Path
from unittest.mock import patch
sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'service'))
sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'tools'))
from operation_lock import OperationLock
from bounded_logs import BoundedLog,pump
from prepare_training import validate_dataset,prepare,train_repo
import test_tts_bundle as fixtures

class LogDirectory(tempfile.TemporaryDirectory):
 def cleanup(self):
  # Windows scanners can briefly retain a deleted rotating-log directory.
  # Retry only ERROR_DIR_NOT_EMPTY; other cleanup errors remain failures.
  for attempt in range(5):
   try:return super().cleanup()
   except OSError as error:
    if getattr(error,'winerror',None)!=145 or attempt==4:raise
    time.sleep(.1*(attempt+1))

class ImportDedupTests(unittest.TestCase):
 def setUp(self):fixtures.TtsBundleTests.setUp(self)
 def tearDown(self):fixtures.TtsBundleTests.tearDown(self)
 def test_repeated_light_and_full_reuse_reference(self):
  from tts_bundle import import_model
  first=import_model(self.root,self.package)
  for _ in range(4):
   result=import_model(self.root,self.package);self.assertTrue(result['referenceReused']);self.assertEqual(result['voice']['reference'],first['voice']['reference'])
  full=import_model(self.root,self.full);self.assertTrue(full['referenceReused']);self.assertEqual(full['voice']['reference'],first['voice']['reference'])
  self.assertEqual(len(list((self.root/'voice-model-packages/已导入').iterdir())),1)
 def test_live_service_excludes_offline_import(self):
  from tts_bundle import import_model
  before=self.store.path.read_bytes()
  with OperationLock(self.root):
   with self.assertRaisesRegex(ValueError,'正在运行'):import_model(self.root,self.package)
  self.assertEqual(before,self.store.path.read_bytes())

class LockAndLogTests(unittest.TestCase):
 def test_log_write_failure_never_recurses_into_redirected_stderr(self):
  with tempfile.TemporaryDirectory() as tmp:
   path=Path(tmp)/'failed.log';sink=BoundedLog(path);sink.handler.stream.close();sink.handler.stream=path.open('r',encoding='utf-8')
   with patch('sys.stderr',sink):sink.write('disk failure')
   self.assertIsNotNone(sink.error);sink.close()
 def test_mcp_distinguishes_hung_foreign_and_unused_ports(self):
  import urllib.error
  from unittest.mock import Mock
  p=Path(__file__).resolve().parents[1]/'integrations/agent_voice_mcp.py';spec=importlib.util.spec_from_file_location('release_mcp',p);mod=importlib.util.module_from_spec(spec);spec.loader.exec_module(mod)
  v=mod.AgentVoice('.','codex');v.settings=Mock(return_value={'port':8820});v.request=Mock(side_effect=urllib.error.URLError(TimeoutError('timeout')))
  with patch.object(mod,'local_listener',return_value=False):self.assertIsNone(v.running())
  with patch.object(mod,'local_listener',return_value=True):
   with self.assertRaisesRegex(RuntimeError,'未响应'):v.running()
  v.request=Mock(return_value={'service':'agent-voice-public','instanceId':'foreign'})
  with self.assertRaisesRegex(RuntimeError,'另一套'):v.running()
 def test_normal_close_does_not_report_crashed_worker(self):
  from pipeline import Pipeline
  from state import Store
  from unittest.mock import Mock
  with tempfile.TemporaryDirectory() as tmp:
   p=Pipeline(Store(tmp));p.loaded=True;p.adapter=Mock();p.adapter.alive.return_value=False
   def during_close():self.assertIsNone(p.snapshot(p.store.get()['voice'])['error'])
   p.adapter.close.side_effect=during_close;p.close()
 def test_lock_excludes_second_process_and_releases(self):
  with tempfile.TemporaryDirectory() as tmp:
   script='import sys;sys.path.insert(0,sys.argv[1]);from operation_lock import OperationLock;g=OperationLock(sys.argv[2]);g.__enter__();g.__exit__()'
   command=[sys.executable,'-c',script,str(Path(__file__).resolve().parents[1]/'service'),tmp]
   with OperationLock(tmp):self.assertNotEqual(subprocess.run(command,capture_output=True).returncode,0)
   self.assertEqual(subprocess.run(command,capture_output=True).returncode,0)
 def test_log_size_and_unicode_pump(self):
  with LogDirectory() as tmp:
   p=Path(tmp)/'service.log';sink=BoundedLog(p,max_bytes=2048,backups=2)
   for _ in range(100):sink.write('中文日志'*80+'\n')
   sink.close();records=[(f.name,f.stat().st_size) for f in Path(tmp).iterdir()];self.assertLessEqual(len(records),3,records);self.assertLess(sum(size for _,size in records),6144,records)
   source=io.BufferedReader(io.BytesIO(('中文跨块🙂'*500).encode()));t=pump(source,Path(tmp)/'worker.log');t.join(3);self.assertFalse(t.is_alive());self.assertEqual((Path(tmp)/'worker.log').read_text(encoding='utf-8'),'中文跨块🙂'*500)

class TrainingDataTests(unittest.TestCase):
 def setUp(self):
  import numpy as np,soundfile as sf
  self.temp=tempfile.TemporaryDirectory();self.root=Path(self.temp.name).resolve()/'audio';self.root.mkdir();self.rows=[]
  for n in range(12):
   name=str(n)+'.wav';sf.write(self.root/name,.1*np.sin(np.arange(24000)*(200+n*31)/24000*2*np.pi),24000);self.rows.append({'file':name,'text':'第%s条素材。'%n,'speaker':'角色','split':''})
  self.write()
 def write(self):
  with (self.root/'metadata.csv').open('w',encoding='utf-8-sig',newline='') as f:w=csv.DictWriter(f,fieldnames=['file','text','speaker','split']);w.writeheader();w.writerows(self.rows)
 def tearDown(self):self.temp.cleanup()
 def test_splits_deterministic_and_prepared_without_mutating_audio(self):
  a=validate_dataset(self.root);b=validate_dataset(self.root);self.assertTrue(a['ok'],a);self.assertEqual(a['items'],b['items'])
  target=Path(self.temp.name).resolve()/'prepared';r=prepare(self.root,target);self.assertTrue(r['ok']);self.assertEqual(sum(len((target/s/'wav.scp').read_text().splitlines()) for s in ('train','dev','test')),12)
  with self.assertRaises(ValueError):prepare(self.root,target)
 def test_duplicate_and_path_escape_rejected(self):
  self.rows[1]['file']='0.wav';self.rows[2]['file']='../escape.wav';self.write();r=validate_dataset(self.root);self.assertFalse(r['ok']);self.assertIn('重复',''.join(r['errors']));self.assertIn('目录',''.join(r['errors']))
 def test_explicit_split_requires_train_per_speaker(self):
  for n,r in enumerate(self.rows):r.update(split='train' if n<10 else 'dev' if n==10 else 'test',speaker='未训练' if n==11 else '训练')
  self.write();r=validate_dataset(self.root);self.assertFalse(r['ok']);self.assertIn('没有训练集',''.join(r['errors']))
 def test_output_path_with_spaces_rejected(self):
  with self.assertRaisesRegex(ValueError,'空格'):prepare(self.root,Path(self.temp.name).resolve()/'invalid output')
 def test_inference_only_source_is_not_training_ready(self):self.assertFalse(train_repo(self.root)['ok'])

import test_core as core_tests
class PublicHealthPrivacyTests(unittest.TestCase):
 setUp=core_tests.CoreTests.setUp
 tearDown=core_tests.CoreTests.tearDown
 request=core_tests.CoreTests.request
 def test_public_health_omits_conversation_and_paths(self):
  state=self.request('/health',auth=False)
  self.assertIn('instanceId',state)
  for key in ('current','verification','harnessEvents','missing','error'):self.assertNotIn(key,state)
  self.assertIn('current',self.request('/health'))

if __name__=='__main__':unittest.main()
