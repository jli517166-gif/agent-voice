import json,sys,tempfile,unittest,zipfile
from pathlib import Path
from unittest.mock import Mock,patch
sys.path[:0]=[str(Path(__file__).resolve().parents[1]/'service'),str(Path(__file__).resolve().parents[1]/'tools')]
from module_packages import engine_package,environment_package,application_package,feature_package
from environment_repair import Repair,CATALOG

class ModulePackageTests(unittest.TestCase):
    def setUp(self):self.temp=tempfile.TemporaryDirectory();self.root=Path(self.temp.name).resolve()
    def tearDown(self):self.temp.cleanup()
    def put(self,name,content=b'fixture'):
        path=self.root/name;path.parent.mkdir(parents=True,exist_ok=True);path.write_bytes(content);return path
    def manifest(self,file):
        with zipfile.ZipFile(file) as archive:return json.loads(archive.read('manifest.json')),archive.namelist()
    def test_engine_and_environment_have_disjoint_resources_and_no_voice(self):
        for name in ['tts-engines/cosyvoice/source/cosyvoice/cli/cosyvoice.py','tts-engines/cosyvoice/models/Fun-CosyVoice3-0.5B/llm.pt','tts-engines/cosyvoice/runtime/tools/python.exe','runtime/core/python.exe','tools/ffmpeg.exe','tools/ffprobe.exe','voices/private.wav','data/settings.json']:self.put(name)
        a=self.root/'cosyvoice.ttsengine';b=self.root/'offline.ttsenvironment';engine_package(self.root,'cosyvoice',a);environment_package(self.root,['cosyvoice'],b)
        engine,names=self.manifest(a);environment,other=self.manifest(b)
        self.assertFalse(set(names[1:])&set(other[1:]));self.assertEqual(engine['kind'],'engine');self.assertEqual(environment['kind'],'environment')
        for name in names+other:self.assertNotIn('settings.json',name);self.assertNotIn('private.wav',name)
    def test_unknown_engine_outside_model_and_existing_output_rejected(self):
        with self.assertRaises(ValueError):engine_package(self.root,'unknown',self.root/'x.ttsengine')
        with self.assertRaises(ValueError):engine_package(self.root,'cosyvoice',self.root/'x.ttsengine','../outside')
    def test_strict_offline_never_downloads_or_uses_remote_pip_index(self):
        helper=Repair(self.root,offline=True)
        with patch('environment_repair.urllib.request.urlopen') as remote:
            with self.assertRaisesRegex(ValueError,'离线资源'):helper.download('https://invalid/file','missing.zip','0'*64)
            remote.assert_not_called()
        wheel=self.put('data/environment-repair/downloads/pip-24.3.1.whl')
        helper.download=Mock(return_value=wheel);helper.run=Mock();helper.pip(self.root/'runtime/core/python.exe',['numpy==1.26.4'])
        for call in helper.run.call_args_list:
            command=call.args[0];self.assertIn('--no-index',command);self.assertNotIn('--index-url',command)
        self.assertEqual(helper.env()['HF_HUB_OFFLINE'],'1')
    def test_prepare_only_does_not_claim_real_synthesis(self):
        helper=Repair(self.root)
        with patch.object(helper,'inspect',return_value={'ok':True,'selected':['cosyvoice'],'items':[]}),patch.object(helper,'system',return_value={}),patch.object(helper,'smoke') as smoke:
            result=helper.repair('cosyvoice',verify=False)
        smoke.assert_not_called();self.assertTrue(result['ok']);self.assertFalse(result['verifiedUsable'])
    def test_application_package_omits_runtime_voice_and_bundled_preset(self):
        for name in ['agent语音.exe','service/server.py','tools/tool.py','integrations/connector.mjs','docs/guide.md','.agents/plugin.json','assets/icon.png','assets/sfx/step-0.wav','assets/private.wav','assets/bundled-voice.json','data/settings.json','runtime/core/python.exe','voices/private.wav']:self.put(name)
        output=self.root/'app.zip';application_package(self.root,output)
        with zipfile.ZipFile(output) as archive:names=archive.namelist()
        self.assertIn('Agent语音/agent语音.exe',names)
        self.assertIn('Agent语音/assets/sfx/step-0.wav',names);self.assertNotIn('Agent语音/assets/private.wav',names)
        for denied in ['data/','runtime/','voices/','bundled-voice.json']:self.assertFalse(any(denied in name for name in names))
    def test_stale_core_transfer_files_and_installed_modules_never_enter_application(self):
        for name in ['agent语音.exe','service/server.py','service/tts_bundle.py','service/config_bundle.py','tools/tts_bundle.py','tools/config_bundle.py','modules/voice-packages/module.exe']:self.put(name)
        for folder in ('integrations','docs','.agents'):(self.root/folder).mkdir()
        output=self.root/'core.zip';application_package(self.root,output)
        with zipfile.ZipFile(output) as z:names=z.namelist()
        self.assertIn('Agent语音/service/server.py',names)
        self.assertFalse(any('tts_bundle' in n or 'config_bundle' in n or '/modules/' in n for n in names))
    def test_feature_package_is_independent_and_rejects_recordings(self):
        self.put('modules/voice-packages/module.exe');self.put('modules/voice-packages/backend/module_cli.py')
        output=self.root/'feature.avmodule';feature_package(self.root,output)
        m,names=self.manifest(output);self.assertEqual(m['kind'],'feature');self.assertEqual(m['apiVersion'],1);self.assertEqual(m['defaults'],{})
        self.assertTrue(all(n=='manifest.json' or n.startswith('payload/modules/voice-packages/') for n in names))
        self.put('modules/voice-packages/reference.wav')
        with self.assertRaisesRegex(ValueError,'录音'):feature_package(self.root,self.root/'audio.avmodule')

    def test_private_archive_has_distinct_installation_root_and_no_agent_instructions(self):
        self.put('agent语音.exe');self.put('AGENTS.md',b'local maintenance only')
        for folder in ('service','tools','integrations','docs','.agents'):(self.root/folder).mkdir()
        output=self.root/'private.zip';application_package(self.root,output,'Agent语音-私用版')
        with zipfile.ZipFile(output) as z:
            self.assertIn('Agent语音-私用版/agent语音.exe',z.namelist())
            self.assertFalse(any(n.startswith('Agent语音/') or n.endswith('/AGENTS.md') for n in z.namelist()))
        with self.assertRaisesRegex(ValueError,'解压目录'):application_package(self.root,self.root/'bad.zip','../outside')

    def test_application_keeps_user_manuals_and_separates_development_audits(self):
        self.put('agent语音.exe')
        for folder in ('service','tools','integrations','docs','.agents'):(self.root/folder).mkdir(exist_ok=True)
        for name in ('安装手册.md','高级CosyVoice训练手册.md','docs/licenses/LICENSE.txt','docs/legal-components.json',
                     'docs/维护收尾-20261023.md','docs/Codex语言控制验收.md','私用版使用与维护.md'):
            self.put(name,name.encode('utf-8'))
        self.put('安装手册.md','安装步骤；[验收记录](docs/维护收尾-20261023.md)；[训练](高级CosyVoice训练手册.md)。'.encode('utf-8'))
        report=self.root/'audit'
        output=self.root/'clean.zip';application_package(self.root,output,audit_directory=report)
        with zipfile.ZipFile(output) as z:
            names=z.namelist();manual=z.read('Agent语音/安装手册.md').decode('utf-8')
        self.assertNotIn('](docs/维护收尾-20261023.md)',manual)
        self.assertIn('验收记录',manual)
        self.assertIn('](高级CosyVoice训练手册.md)',manual)
        for name in ('安装手册.md','高级CosyVoice训练手册.md','docs/licenses/LICENSE.txt','docs/legal-components.json'):
            self.assertIn('Agent语音/'+name,names)
        for name in ('docs/维护收尾-20261023.md','docs/Codex语言控制验收.md','私用版使用与维护.md'):
            self.assertNotIn('Agent语音/'+name,names)
            self.assertEqual((report/name).read_bytes(),(self.root/name).read_bytes())

if __name__=='__main__':unittest.main()
