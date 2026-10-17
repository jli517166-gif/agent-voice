import copy,json,sys,tempfile,unittest
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'service'))
from state import Store,defaults

class BundledVoiceTests(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory();self.root=Path(self.temp.name).resolve()
        (self.root/'assets').mkdir()
        voice=defaults()['voice'];voice.update(baseModel='tts-engines/cosyvoice/models/Fairy',reference='voices/Fairy/reference.wav',referenceText='测试参考台词。')
        self.preset={'version':1,'name':'Fairy','voice':voice}
        self.write()
    def tearDown(self):self.temp.cleanup()
    def write(self): (self.root/'assets/bundled-voice.json').write_text(json.dumps(self.preset),'utf-8')
    def test_clean_first_start_has_voice_and_unique_credentials(self):
        result=Store(self.root).get();self.assertEqual(result['voice'],self.preset['voice']);self.assertEqual(result['engines']['cosyModel'],result['voice']['baseModel']);self.assertEqual(result['profiles'][0]['name'],'Fairy');self.assertFalse(result['airi']['enabled']);self.assertEqual(result['appearance'],defaults()['appearance']);self.assertEqual(result['harness'],{'codex':True,'deepseek':False})
        (self.root/'data/settings.json').unlink();other=Store(self.root).get();self.assertNotEqual(result['token'],other['token'])
    def test_existing_user_configuration_is_never_overwritten(self):
        store=Store(self.root);store.update({'voice':{'speed':1.1}})
        self.preset['voice']['assetScope']='private';self.write()
        self.assertEqual(Store(self.root).get()['voice']['speed'],1.1)
    def test_external_or_parent_paths_are_rejected_before_writing_settings(self):
        for key,value in [('baseModel','C:/private/model'),('baseModel','tts-engines/cosyvoice/models/../../../../private'),('reference','voices/../../private.wav'),('reference','voices\\private.wav')]:
            with self.subTest(key=key,value=value):
                preset=copy.deepcopy(self.preset);preset['voice'][key]=value
                (self.root/'assets/bundled-voice.json').write_text(json.dumps(preset),'utf-8')
                with self.assertRaises(ValueError):Store(self.root)
                self.assertFalse((self.root/'data/settings.json').exists())
    def test_private_assets_or_connection_settings_are_rejected(self):
        self.preset['voice']['assetScope']='private';self.write()
        with self.assertRaisesRegex(ValueError,'公共'):Store(self.root)
        self.preset['voice']['assetScope']='public';self.preset['token']='machine-secret';self.write()
        with self.assertRaisesRegex(ValueError,'清单'):Store(self.root)

if __name__=='__main__':unittest.main()
