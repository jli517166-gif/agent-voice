import sys,tempfile,unittest
from pathlib import Path
from unittest.mock import Mock,patch
sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'service'))
from environment_repair import Repair

class SetupResourcesTests(unittest.TestCase):
 def test_new_license_download_does_not_change_existing_model_identity(self):
  from model_integrity import model_sources,inventory_fingerprint
  import test_tts_bundle as fixtures
  fixture=fixtures.TtsBundleTests();fixture.setUp()
  try:
   before={key:file.read_bytes() for key,file in model_sources(fixture.model).items()};helper=Repair(fixture.root)
   def download(command,**kw):
    stage=next(helper.work.glob('model-download-*'));(stage/'README.md').write_text('model card');(stage/'LICENSE').write_text('model terms')
   # The downloader creates its staging path in the child process.
   def run(command,**kw):
    import ast,re
    source=command[-1];stage=Path(ast.literal_eval(re.search(r'local_dir=(.*?),allow_patterns=',source).group(1)));stage.mkdir(parents=True);download(command)
   helper.run=run;helper.download=Mock(return_value=helper.root/'fixture-pip.whl');helper.download_model('cosyvoice',Path(sys.executable),fixture.model,['README.md'])
   self.assertEqual(before,{key:file.read_bytes() for key,file in model_sources(fixture.model).items()});self.assertTrue((fixture.model/'.agent-voice-licenses/README.md').exists());self.assertFalse((fixture.model/'README.md').exists())
  finally:fixture.tearDown()
 def test_windows_model_patterns_expand_complete_tokenizer_and_feature_folders(self):
  patterns=Repair.model_patterns('gpt-sovits',[r'chinese-hubert-base\config.json',r'chinese-roberta-wwm-ext-large\vocab.txt',r'gsv-v5-pretrained\vocoder.pth'])
  self.assertTrue({'chinese-hubert-base/*','chinese-roberta-wwm-ext-large/*','gsv-v5-pretrained/vocoder.pth','LICENSE*','**/LICENSE*','NOTICE*','COPYING*','README*'}<=set(patterns))
 def test_environment_step_does_not_silently_download_missing_engines(self):
  with tempfile.TemporaryDirectory() as temp:
   helper=Repair(temp);helper.inspect=Mock(return_value={'items':[{'id':'cosyvoice','sourceMissing':True,'matchaMissing':False,'missingModelFiles':[]}]});helper.pip=Mock();helper.source=Mock()
   with self.assertRaisesRegex(ValueError,'上一步'):helper.repair('current',verify=False,dependencies_only=True)
   helper.pip.assert_not_called();helper.source.assert_not_called()
 def test_external_runtime_gets_local_downloader_without_mutating_user_interpreter(self):
  with tempfile.TemporaryDirectory() as temp:
   helper=Repair(temp);root=helper.root;c=helper.store.get();c['engines']['pythonService']=sys.executable;helper.store.update({'engines':c['engines']})
   row={'id':'cosyvoice','source':str(root/'tts-engines/cosyvoice/source'),'model':str(root/'tts-engines/cosyvoice/models/base'),'sourceMissing':False,'matchaMissing':False,'missingModelFiles':['llm.pt']}
   helper.inspect=Mock(side_effect=[{'items':[row]},{'items':[dict(row,missingModelFiles=[])]}]);local=root/'runtime/core/python.exe';helper.python=Mock(return_value=local);helper.probe=Mock(return_value={'missing':[]});helper.download_model=Mock();helper.pip=Mock()
   helper.prepare_resources('current');helper.python.assert_called_once_with(root/'runtime/core');self.assertEqual(helper.download_model.call_args.args[1],local);helper.pip.assert_not_called();self.assertEqual(helper.store.get()['engines']['pythonService'],sys.executable)
 def test_external_source_missing_matcha_is_acquired_inside_app(self):
  with tempfile.TemporaryDirectory() as temp,tempfile.TemporaryDirectory() as outside:
   helper=Repair(temp);root=helper.root
   row={'id':'cosyvoice','source':outside,'model':str(root/'tts-engines/cosyvoice/models/base'),'sourceMissing':False,'matchaMissing':True,'missingModelFiles':[]}
   helper.inspect=Mock(side_effect=[{'items':[row]},{'items':[dict(row,source=str(root/'tts-engines/cosyvoice/source'),matchaMissing=False)]}]);helper.source=Mock();helper.download=Mock(return_value=root/'dummy.zip')
   with patch('environment_repair.extract'):helper.prepare_resources('current')
   helper.source.assert_called_once_with('cosyvoice',root/'tts-engines/cosyvoice/source')
 def test_replaced_source_rechecks_matcha_instead_of_using_old_inventory(self):
  with tempfile.TemporaryDirectory() as temp:
   helper=Repair(temp);root=helper.root
   row={'id':'cosyvoice','source':str(root/'tts-engines/cosyvoice/source'),'model':str(root/'tts-engines/cosyvoice/models/base'),'sourceMissing':True,'matchaMissing':False,'missingModelFiles':[]}
   helper.inspect=Mock(side_effect=[{'items':[row]},{'items':[dict(row,sourceMissing=False)]}]);helper.source=Mock();helper.download=Mock(return_value=root/'dummy.zip')
   with patch('environment_repair.extract') as extract:helper.prepare_resources('current')
   extract.assert_called_once();self.assertIn('Matcha-TTS',str(extract.call_args.args[1]))
 def test_offline_download_fails_without_starting_network_or_pip(self):
  with tempfile.TemporaryDirectory() as temp:
   helper=Repair(temp,offline=True);helper.download=Mock();helper.pip=Mock()
   with self.assertRaisesRegex(ValueError,'离线'):helper.prepare_resources('current')
   helper.download.assert_not_called();helper.pip.assert_not_called()
 def test_existing_resources_need_no_torch_environment_or_second_download(self):
  with tempfile.TemporaryDirectory() as temp:
   helper=Repair(temp);root=helper.root
   row={'id':'cosyvoice','source':str(root/'tts-engines/cosyvoice/source'),'model':str(root/'tts-engines/cosyvoice/models/base'),'sourceMissing':False,'matchaMissing':False,'missingModelFiles':[]}
   helper.inspect=Mock(return_value={'items':[row]});helper.source=Mock();helper.pip=Mock();helper.download_model=Mock()
   result=helper.prepare_resources('current')
   self.assertTrue(result['ok']);self.assertFalse(result['verifiedUsable'])
   helper.pip.assert_not_called();helper.source.assert_not_called();helper.download_model.assert_not_called()
 def test_source_failure_preserves_settings_and_returns_real_failure(self):
  with tempfile.TemporaryDirectory() as temp:
   helper=Repair(temp);root=helper.root;before=helper.store.path.read_bytes()
   row={'id':'cosyvoice','source':str(root/'tts-engines/cosyvoice/source'),'model':str(root/'tts-engines/cosyvoice/models/base'),'sourceMissing':True,'matchaMissing':False,'missingModelFiles':[]}
   helper.inspect=Mock(return_value={'items':[row]});helper.source=Mock(side_effect=RuntimeError('network unavailable'))
   with self.assertRaisesRegex(RuntimeError,'network unavailable'):helper.prepare_resources('current')
   self.assertEqual(before,helper.store.path.read_bytes())

if __name__=='__main__':unittest.main()
