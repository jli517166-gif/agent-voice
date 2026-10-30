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
 def test_markdown_emphasis_and_closing_marks_cannot_form_empty_speech_segments(self):
  from text import sanitize
  for raw in ['**暂不打包，等你确认。**','__先完成项目内长文测试。__','这段文章名为“荷塘月色。”','太好了！！！继续测试。','**这是标题。**\n下面是完整的正文。']:
   cleaned=sanitize(raw);chunks=generation_chunks(cleaned)
   self.assertEqual(''.join(chunks),cleaned)
   import re
   self.assertTrue(all(re.search(r'[^\W_]',c) for c in chunks),chunks)
  self.assertEqual(sanitize('**暂不打包，等你确认。**'),'暂不打包，等你确认。')
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
   response=io.BytesIO(b'corrupt');response.status=200;response.headers={'Content-Length':'7'}
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
   self.assertEqual(len(commands),3);self.assertIn('setuptools==75.6.0',commands[0]);self.assertIn('--force-reinstall',commands[-2]);self.assertIn('--no-deps',commands[-2]);self.assertNotIn('--force-reinstall',commands[-1])
 def test_repair_success_requires_real_synthesis(self):
  with tempfile.TemporaryDirectory() as temp:
   helper=Repair(temp)
   with patch.object(helper,'inspect',return_value={'ok':True,'selected':['cosyvoice'],'items':[]}),patch.object(helper,'system',return_value={}),patch('diagnostics.inspect_installation',return_value={'checks':[{'name':'CUDA 与推理依赖','section':'cosyvoice/cosyvoice3','ok':True}]}),patch.object(helper,'smoke',return_value=[{'id':'cosyvoice/cosyvoice3','ok':False,'error':'worker failed','guidance':'retry'}]):
    result=helper.repair('cosyvoice')
   self.assertFalse(result['ok']);self.assertFalse(result['verifiedUsable']);self.assertEqual(result['synthesisChecks'][0]['error'],'worker failed')
 def test_smoke_failure_has_actionable_guidance(self):
  self.assertIn('CUDA 12.8',Repair.guidance('no kernel image is available for execution'))
  self.assertIn('Visual C++',Repair.guidance('DLL load failed while importing native module'))
  self.assertIn('显存不足',Repair.guidance('CUDA out of memory'))
 def test_compatibility_torch_is_officially_pinned(self):
  profile=CATALOG['cuda128Compatibility']
  self.assertEqual(profile['torchIndex'],'https://download.pytorch.org/whl/cu128')
  self.assertEqual(profile['torch'],['torch==2.7.1','torchaudio==2.7.1'])
 def test_compatibility_runtime_includes_torchvision_and_mirrors(self):
  profile=CATALOG['cuda128Compatibility']
  self.assertEqual(profile['torchvision'],'torchvision==0.22.1')
  self.assertGreaterEqual(profile['timeoutSeconds'],3600)
  self.assertTrue(any('pytorch-wheels' in link for link in profile['torchFindLinks']))
  self.assertIn('https://pypi.org/simple',CATALOG['pipIndexes'])
  self.assertGreaterEqual(len(CATALOG['pipIndexes']),2)
 def test_local_wheels_match_pinned_versions(self):
  with tempfile.TemporaryDirectory() as temp:
   helper=Repair(temp);directory=helper.work/'wheels';directory.mkdir(parents=True)
   names=['torch-2.7.1+cu128-cp310-cp310-win_amd64.whl','torchaudio-2.7.1+cu128-cp310-cp310-win_amd64.whl','torchvision-0.22.1+cu128-cp310-cp310-win_amd64.whl']
   for name in names:(directory/name).write_bytes(b'x')
   (directory/'torch-2.7.1+cu128-cp311-cp311-win_amd64.whl').write_bytes(b'x'*100)
   selected=helper.local_wheels(['torch==2.7.1','torchaudio==2.7.1','torchvision==0.22.1'])
   self.assertEqual({path.name for path in selected},set(names))
   self.assertEqual(helper.local_wheels(['torch==2.6.0']),[])
 def test_pip_falls_back_to_next_index(self):
  with tempfile.TemporaryDirectory() as temp:
   helper=Repair(temp);helper.download=Mock(return_value=Path(temp).resolve()/'pip.whl');calls=[]
   def run(command,timeout=1800,**kwargs):
    calls.append(command)
    if 'https://first.invalid/simple' in command:raise RuntimeError('boom')
   helper.run=run
   with patch.dict(CATALOG,{'pipIndexes':['https://first.invalid/simple','https://second.invalid/simple']}):
    helper.pip(Path(temp).resolve()/'python.exe',['Pillow==12.3.0'])
   self.assertTrue(any('https://second.invalid/simple' in command for command in calls))
 def test_compat_install_replaces_shadow_torchvision(self):
  with tempfile.TemporaryDirectory() as temp:
   root=Path(temp).resolve();helper=Repair(root)
   runtime=root/'tts-engines'/'gpt-sovits'/'runtime';python=runtime/'Scripts'/'python.exe';python.parent.mkdir(parents=True);python.write_bytes(b'')
   shadow=runtime/'gpu-packages';(shadow/'torchvision').mkdir(parents=True);(shadow/'torchvision-0.21.0+cu124.dist-info').mkdir()
   calls=[]
   helper.pip=lambda py,requirements,**kwargs:calls.append((list(requirements),kwargs))
   helper.verify_compat_runtime=Mock()
   helper.install_compat_torch(python)
   flattened=[item for requirement,_ in calls for item in requirement]
   self.assertIn('torchvision==0.22.1',flattened)
   self.assertFalse((shadow/'torchvision').exists())
   self.assertTrue(any(kwargs.get('target')==shadow for _,kwargs in calls))
 def test_cuda128_requires_a_supporting_driver(self):
  self.assertEqual(Repair.driver_version({'nvidiaSmi':'NVIDIA GeForce RTX 5070 Laptop GPU, 596.13, 8151 MiB'}),'596.13')
  self.assertTrue(Repair.driver_supports_cuda128('596.13'))
  self.assertFalse(Repair.driver_supports_cuda128('566.36'))
  self.assertIsNone(Repair.driver_supports_cuda128(''))
 def test_old_driver_is_reported_before_downloading_cu128(self):
  with tempfile.TemporaryDirectory() as temp:
   helper=Repair(temp);helper.pip=Mock()
   with patch.object(helper,'system',return_value={'nvidiaSmi':'NVIDIA GeForce RTX 5070 Laptop GPU, 566.36, 8151 MiB'}):
    with self.assertRaisesRegex(RuntimeError,'570.26'):helper.install_compat_torch(Path(temp).resolve()/'python.exe')
   helper.pip.assert_not_called()
 def test_rtx_50_series_requires_cuda128_before_deep_diagnostics(self):
  self.assertTrue(Repair.system_requires_cuda128({'nvidiaSmi':'NVIDIA GeForce RTX 5070 Laptop GPU, 596.13, 8151 MiB'}))
  self.assertTrue(Repair.system_requires_cuda128({'nvidiaSmi':'NVIDIA GeForce RTX 5090, 572.83, 32759 MiB'}))
  self.assertFalse(Repair.system_requires_cuda128({'nvidiaSmi':'NVIDIA GeForce RTX 4050 Laptop GPU, 596.13, 6141 MiB'}))
 def test_rtx_50_series_installs_compatibility_before_deep_diagnostics(self):
  with tempfile.TemporaryDirectory() as temp:
   helper=Repair(temp);events=[]
   fake_check={'name':'CUDA 与推理依赖','section':'cosyvoice/cosyvoice3','ok':True}
   system={'nvidiaSmi':'NVIDIA GeForce RTX 5070 Laptop GPU, 596.13, 8151 MiB'}
   def install(_):events.append('compat')
   def diagnose(*args,**kwargs):events.append('diagnostics');return {'checks':[fake_check]}
   with patch.object(helper,'inspect',return_value={'ok':True,'selected':['cosyvoice'],'items':[]}),patch.object(helper,'system',return_value=system),patch.object(helper,'compat_runtime_ready',return_value=False),patch.object(helper,'install_compat_torch',side_effect=install),patch('diagnostics.inspect_installation',side_effect=diagnose),patch.object(helper,'smoke',return_value=[{'id':'cosyvoice/cosyvoice3','ok':True}]):
    result=helper.repair('cosyvoice')
   self.assertTrue(result['verifiedUsable']);self.assertEqual(events,['compat','diagnostics'])
   self.assertTrue(result['compatibilityRuntimeInstalled'])
 def test_setup_prepare_only_installs_rtx50_compatibility_before_claiming_prepared(self):
  with tempfile.TemporaryDirectory() as temp:
   helper=Repair(temp)
   system={'nvidiaSmi':'NVIDIA GeForce RTX 5070 Laptop GPU, 596.13, 8151 MiB'}
   with patch.object(helper,'inspect',return_value={'ok':True,'selected':['cosyvoice'],'items':[]}),patch.object(helper,'system',return_value=system),patch.object(helper,'compat_runtime_ready',return_value=False),patch.object(helper,'install_compat_torch') as install:
    result=helper.repair('cosyvoice',verify=False,dependencies_only=True)
   install.assert_called_once();self.assertTrue(result['compatibilityRuntimeInstalled'])
   self.assertFalse(result['verifiedUsable'])
 def test_strict_offline_compatibility_never_falls_back_to_network(self):
  with tempfile.TemporaryDirectory() as temp:
   helper=Repair(temp,offline=True);helper.pip=Mock()
   with patch.object(helper,'system',return_value={'nvidiaSmi':'NVIDIA GeForce RTX 5070 Laptop GPU, 596.13, 8151 MiB'}):
    with self.assertRaisesRegex(RuntimeError,'CUDA 12.8.*离线'):helper.install_compat_torch(Path(temp).resolve()/'python.exe')
   helper.pip.assert_not_called()
 def test_unsupported_gpu_runtime_is_repaired_and_synthesized_again(self):
  with tempfile.TemporaryDirectory() as temp:
   helper=Repair(temp);failed=[{'id':'cosyvoice/cosyvoice3','ok':False,'error':'no kernel image is available'}];passed=[{'id':'cosyvoice/cosyvoice3','ok':True}]
   fake_check={'name':'CUDA 与推理依赖','section':'cosyvoice/cosyvoice3','ok':True}
   with patch.object(helper,'inspect',return_value={'ok':True,'selected':['cosyvoice'],'items':[]}),patch.object(helper,'system',return_value={}),patch('diagnostics.inspect_installation',return_value={'checks':[fake_check]}),patch.object(helper,'smoke',side_effect=[failed,passed]) as smoke,patch.object(helper,'install_compat_torch') as install:
    result=helper.repair('cosyvoice')
   self.assertTrue(result['ok']);self.assertTrue(result['verifiedUsable']);self.assertTrue(result['compatibilityRuntimeInstalled'])
   self.assertEqual(smoke.call_count,2);install.assert_called_once()

if __name__=='__main__':unittest.main()
