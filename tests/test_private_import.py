import contextlib
import hashlib
import io
import json
from pathlib import Path
import sys
import tempfile
import unittest

BASE=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(BASE/'service'))
sys.path.insert(0,str(BASE/'tools'))
from state import Store, defaults, atomic_json
from release_audit import audit
from export_connector import export

class IsolationTests(unittest.TestCase):
    def test_private_hash_is_rejected_even_without_external_manifest(self):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp);Store(root)
            payload=b'private-test-fixture';(root/'renamed.pth').write_bytes(payload)
            atomic_json(root/'data/private-assets.json',{'files':[{'sha256':hashlib.sha256(payload).hexdigest()}]})
            atomic_json(root/'release.json',{'files':['renamed.pth','../escape.wav','data/settings.json'],
                        'publicVoiceProvided':True,'runtimeIncluded':True,'userAccepted':True})
            result=audit(root,root/'release.json')
            self.assertFalse(result['packageReady']);self.assertEqual(len(result['errors']),3);self.assertEqual(result['files'],[])
    def test_accepted_settings_and_profile_survive_reopening(self):
        with tempfile.TemporaryDirectory() as temp:
            store=Store(temp);voice=defaults()['voice'];voice.update(processing='accepted',assetScope='private',protect=.33,style='gentle')
            store.update({'voice':voice,'profiles':[{'id':'accepted','name':'私有测试','voice':voice}]})
            self.assertEqual(Store(temp).get()['profiles'][0]['voice'],voice)
            before=store.path.read_bytes()
            with self.assertRaises(ValueError):store.update({'voice':{'formant':.5}})
            self.assertEqual(before,store.path.read_bytes())
    def test_old_saved_profile_is_upgraded_with_processing_defaults(self):
        with tempfile.TemporaryDirectory() as temp:
            store=Store(temp);config=store.get();old=dict(config['voice'])
            for key in ('processing','assetScope','protect'):old.pop(key)
            config['profiles']=[{'id':'old','name':'旧配置','voice':old}];atomic_json(store.path,config)
            reopened=Store(temp).get()['profiles'][0]['voice']
            self.assertEqual(reopened['processing'],'standard');self.assertEqual(reopened['assetScope'],'public')
    def test_connector_exports_preserve_existing_configuration_and_are_idempotent(self):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp)/'app';project=Path(temp)/'project';project.mkdir();Store(root)
            (root/'service').mkdir();(root/'service/state.py').write_bytes((BASE/'service/state.py').read_bytes())
            with contextlib.redirect_stdout(io.StringIO()):
                export('deepseek',root,project);export('deepseek',root,project)
            self.assertTrue((project/'node_modules/dsh-agent-voice-public/index.mjs').is_file())
            with self.assertRaises(ValueError):export('claude',root,project)

if __name__=='__main__':unittest.main(verbosity=2)
