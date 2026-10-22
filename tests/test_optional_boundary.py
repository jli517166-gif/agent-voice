"""Core must remain usable in a Python process that cannot see optional code."""
import json,subprocess,sys,tempfile,unittest
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]

class OptionalBoundaryTests(unittest.TestCase):
    def test_local_voice_profile_does_not_require_package_module(self):
        code=r'''
import sys,json,importlib.util
from pathlib import Path
sys.path.insert(0,sys.argv[1]);root=Path(sys.argv[2])
assert importlib.util.find_spec('tts_bundle') is None
assert importlib.util.find_spec('config_bundle') is None
import server,diagnostics,environment_repair
from state import Store
from model_integrity import REQUIRED
from voice_profiles import save_profile,apply_profile
store=Store(root);model=root/'models';model.mkdir()
for name in REQUIRED:(model/name).write_bytes(b'fixture weights')
(model/'cosyvoice3.yaml').write_text('llm: !new:cosyvoice.llm.llm.CosyVoice3LM {}',encoding='utf-8')
(model/'CosyVoice-BlankEN').mkdir();(root/'mine.wav').write_bytes(b'fixture reference')
store.update({'voice':{'baseModel':'models','reference':'mine.wav','referenceText':'own recording'}})
result=save_profile(store,'own',store.get()['voice'])
assert apply_profile(store,result['profile']['id'])['ok']
print(json.dumps({'ok':True,'savedAndAppliedWithoutModule':True}))
'''
        with tempfile.TemporaryDirectory() as tmp:
            result=subprocess.run([sys.executable,'-I','-X','utf8','-c',code,str(ROOT/'service'),tmp],capture_output=True,text=True,encoding='utf-8',timeout=25)
        self.assertEqual(result.returncode,0,result.stderr)
        self.assertTrue(json.loads(result.stdout)['savedAndAppliedWithoutModule'])

    def test_optional_code_is_outside_core_and_onboarding(self):
        for folder in ('service','tools'):
            for name in ('tts_bundle.py','config_bundle.py'):self.assertFalse((ROOT/folder/name).exists())
        for name in ('ui/EmbeddedSetupController.cs','ui/EmbeddedSetupForm.cs','ui/SetupWorkflow.cs','assets/setup/setup.js'):
            text=(ROOT/name).read_text(encoding='utf-8-sig');self.assertNotIn('import-voice',text);self.assertNotIn('.ttsmodel',text)
        self.assertFalse((ROOT/'ui/ConfigurationPage.cs').exists())

if __name__=='__main__':unittest.main()
