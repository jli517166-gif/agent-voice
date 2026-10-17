import sys,unittest,tempfile,zipfile,json
from pathlib import Path
from unittest.mock import patch,Mock
sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'service'))
from text import generation_chunks
from pipeline import Pipeline
from state import Store
from environment_repair import Repair,inside,extract,CATALOG
import test_core

class LongTextTests(unittest.TestCase):
 def test_all_content_preserved_with_and_without_punctuation(self):
  for text in [('长文本测试。'*600)[:4000], '连续文字'*1000, '这是一句很长的文字，'*350]:
   chunks=generation_chunks(text)
   self.assertEqual(''.join(chunks),text)
   self.assertTrue(all(0<len(c)<=220 for c in chunks))
 def test_nonplaying_long_request_generates_segments_and_never_plays(self):
  f=test_core.CoreTests('test_authentication_and_unknown_interfaces');f.setUp();self.addCleanup(f.tearDown)
  text='请检查这一段长篇文字是否完整。'*25
  job=f.request('/speak',{'text':text,'play':False})
  self.assertTrue(f.server.jobs.jobs[job['jobId']]['done'].wait(10))
  result=f.request('/api/jobs/'+job['jobId'])
  self.assertEqual(result['state'],'completed');self.assertTrue(result['result']['progressive']);self.assertFalse(result['result']['played'])
  self.assertEqual(''.join(c[0] for c in f.actor.calls),text)
  self.assertEqual(result['segmentsCompleted'],result['totalSegments'])
  self.assertFalse(f.actor.plays)
 def test_concat_streams_and_preserves_samples_with_atomic_failure(self):
  import soundfile as sf,numpy as np
  with tempfile.TemporaryDirectory() as temp:
   root=Path(temp).resolve();p=Pipeline(Store(root));paths=[]
   for i in range(3):
    path=root/(str(i)+'.wav');sf.write(path,np.sin(np.arange(180000)*.1)*.1,24000);paths.append(path)
   target=root/'out.wav';rate,duration=p.concatenate(paths,target,lambda:None)
   self.assertEqual(rate,24000);self.assertEqual(duration,22.5)
   self.assertEqual(sf.info(target).frames,540000)
   original=target.read_bytes()
   with patch('audio_limits.MAX_AUDIO_SECONDS',1),self.assertRaisesRegex(RuntimeError,'30 分钟'):p.concatenate(paths,target,lambda:None)
   self.assertEqual(target.read_bytes(),original);self.assertFalse(target.with_suffix('.joining.wav').exists())

class ConsoleLoggingTests(unittest.TestCase):
 def test_english_console_and_broken_pipe_cannot_fail_a_job(self):
  import io
  from bounded_logs import message
  raw=io.BytesIO();console=io.TextIOWrapper(raw,encoding='cp1252',errors='strict')
  with patch('sys.stdout',console):message('中文任务正文')
  self.assertIn(b'\\u4e2d',raw.getvalue())
  broken=Mock();broken.encoding='utf-8';broken.write.side_effect=BrokenPipeError('closed')
  with patch('sys.stdout',broken):message('日志关闭')

class EnvironmentRepairTests(unittest.TestCase):
 def test_paths_and_archive_cannot_escape(self):
  with tempfile.TemporaryDirectory() as temp:
   root=Path(temp).resolve()
   for path in [root,root/'../outside']:
    with self.assertRaises(ValueError):inside(root,path)
   archive=root/'unsafe.zip'
   with zipfile.ZipFile(archive,'w') as z:z.writestr('../escaped','bad')
   with self.assertRaises(ValueError):extract(archive,root/'stage')
   self.assertFalse((root/'escaped').exists())
 def test_install_environment_and_cache_stay_local(self):
  with tempfile.TemporaryDirectory() as temp:
   helper=Repair(temp)
   with patch.dict('os.environ',{'PIP_TARGET':'outside','PYTHONPATH':'outside','HF_HUB_OFFLINE':'1'}):env=helper.env()
   self.assertNotIn('PIP_TARGET',env);self.assertNotIn('PYTHONPATH',env);self.assertNotIn('HF_HUB_OFFLINE',env)
   for key in ['PIP_CACHE_DIR','TMP','TEMP','HF_HOME','TORCH_HOME','MODELSCOPE_CACHE','NLTK_DATA']:
    self.assertIn(Path(temp).resolve(),Path(env[key]).resolve().parents)
 def test_external_interpreter_cannot_be_mutated(self):
  with tempfile.TemporaryDirectory() as temp:
   with self.assertRaises(ValueError):Repair(temp).pip(sys.executable,['numpy'])
 def test_download_hash_failure_is_not_installed(self):
  import io
  with tempfile.TemporaryDirectory() as temp:
   response=io.BytesIO(b'corrupt');response.headers={'Content-Length':'7'}
   with patch('environment_repair.urllib.request.urlopen',return_value=response),self.assertRaisesRegex(ValueError,'校验'):Repair(temp).download('https://example.invalid/file','x.zip','0'*64)
   self.assertFalse((Path(temp).resolve()/'data/environment-repair/downloads/x.zip').exists())
   self.assertFalse(list(Path(temp).resolve().rglob('*.part')))
 def test_pip_pins_have_explicit_versions_and_sources(self):
  for engine in ['core','cosyvoice','gpt-sovits']:
   self.assertTrue(all('==' in value for value in CATALOG[engine]['packages'].values()))
  self.assertEqual(len(CATALOG['python']['sha256']),64)
  self.assertEqual(len(CATALOG['ffmpeg']['sha256']),64)
  for engine in ['cosyvoice','gpt-sovits']:self.assertEqual(len(CATALOG[engine]['modelRevision']),40)
 def test_repair_reinstalls_failed_root_without_forcing_transitive_upgrades(self):
  with tempfile.TemporaryDirectory() as temp:
   helper=Repair(temp);helper.run=Mock();helper.download=Mock(return_value=Path(temp).resolve()/'pip.whl')
   helper.pip(Path(temp).resolve()/'python.exe',['Pillow==12.3.0'])
   commands=[call.args[0] for call in helper.run.call_args_list]
   self.assertEqual(len(commands),2);self.assertIn('--force-reinstall',commands[0]);self.assertIn('--no-deps',commands[0]);self.assertNotIn('--force-reinstall',commands[1])

if __name__=='__main__':unittest.main()
