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
    def test_clean_start_ignores_legacy_factory_voice_and_has_unique_credentials(self):
        first=Store(self.root).get()
        self.assertEqual(first['voice'],defaults()['voice']);self.assertEqual(first['profiles'],[])
        self.assertEqual(first['voice']['reference'],'');self.assertFalse(first['airi']['enabled'])
        (self.root/'data/settings.json').unlink();self.assertNotEqual(Store(self.root).get()['token'],first['token'])
    def test_existing_user_voice_is_never_erased(self):
        store=Store(self.root);store.update({'voice':{'speed':1.1,'reference':'mine.wav','referenceText':'own recording'}})
        (self.root/'assets/bundled-voice.json').write_text('invalid','utf-8')
        again=Store(self.root).get();self.assertEqual(again['voice']['reference'],'mine.wav');self.assertEqual(again['voice']['speed'],1.1)
    def test_old_manifest_cannot_inject_paths_or_credentials(self):
        self.preset['token']='other-machine';self.preset['voice']['reference']='C:/private/reference.wav';self.write()
        current=Store(self.root).get();self.assertNotEqual(current['token'],'other-machine');self.assertEqual(current['voice']['reference'],'')
    def test_malformed_factory_manifest_is_completely_ignored(self):
        (self.root/'assets/bundled-voice.json').write_text('invalid','utf-8')
        self.assertEqual(Store(self.root).get()['profiles'],[])

if __name__=='__main__':unittest.main()
