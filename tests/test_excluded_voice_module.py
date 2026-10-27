"""The public repository and generic module builder must not include voice transfer."""
import sys,tempfile,unittest
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT/'tools'))
from module_packages import feature_package
class ExcludedVoiceModuleTests(unittest.TestCase):
 def test_public_source_does_not_ship_voice_transfer_implementation(self):
  self.assertFalse((ROOT/'optional-modules/voice-packages').exists())
 def test_feature_builder_is_generic_without_a_voice_module_default(self):
  with tempfile.TemporaryDirectory() as temp:
   root=Path(temp);path=root/'modules/extension/module.exe';path.parent.mkdir(parents=True);path.write_bytes(b'fixture')
   result=feature_package(root,root/'extension.avmodule')
   self.assertTrue(result['ok'])
