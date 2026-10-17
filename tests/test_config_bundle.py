import copy
import json
from pathlib import Path
import socket
import sys
import tempfile
import shutil
import threading
import unittest
from unittest.mock import patch
import wave
import zipfile

sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'service'))
from state import Store,defaults,atomic_json
from config_bundle import export_bundle,import_bundle,digest
from server import Server
sys.path.insert(1,str(Path(__file__).resolve().parents[1]/'tools'))
from release_audit import audit

class BundleTests(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory();self.base=Path(self.temp.name).resolve();self.root=self.base/'app';self.root.mkdir()
        cfg=defaults()
        with socket.socket() as sock:sock.bind(('127.0.0.1',0));cfg['port']=sock.getsockname()[1]
        cfg['engines']={key:str(self.root) for key in cfg['engines']};cfg['engines']['pythonService']=sys.executable;cfg['engines']['pythonTts']=sys.executable
        cfg['harness']['codex']=False
        atomic_json(self.root/'data/settings.json',cfg);self.store=Store(self.root)
        (self.root/'voice.pth').write_bytes(b'fixture-model');(self.root/'voice.index').write_bytes(b'fixture-index')
        with wave.open(str(self.root/'reference.wav'),'wb') as wav:wav.setparams((1,2,24000,0,'NONE',''));wav.writeframes(b'\0\0'*2400)
        from PIL import Image
        Image.new('RGB',(8,8),(210,90,130)).save(self.root/'background.png')
        Image.new('RGB',(8,8),(80,160,220)).save(self.root/'loading.gif',save_all=True)
        self.store.update({'voice':{'reference':'reference.wav','referenceText':'认真说话。','engine':'cosyvoice','assetScope':'private','style':'gentle'},'appearance':{'background':'background.png','loadingAnimation':'loading.gif'}})
        cfg=self.store.get();self.store.update({'profiles':[{'id':'accepted','name':'认可版','voice':cfg['voice']}],'phrases':['喝水。','休息。']})
        self.original=self.store.get();self.package=self.base/'accepted.avconfig';self.assets=self.base/'private-assets'
        export_bundle(self.root,self.package)
    def tearDown(self):self.temp.cleanup()
    def test_new_synthesis_mode_base_model_and_bypass_roundtrip(self):
        folder=self.root/'custom-base';folder.mkdir()
        self.store.update({'voice':{'baseModel':'custom-base','advancedEnabled':False,'instruction':'参数应保留'}})
        expected=self.store.get()['voice'];self.store.update({'profiles':[{'id':'new','name':'新配置','voice':expected}]})
        export_bundle(self.root,self.package)
        self.store.update({'voice':{'baseModel':'','advancedEnabled':True,'instruction':''},'profiles':[]})
        import_bundle(self.root,self.package,asset_root=self.assets)
        actual=Store(self.root).get()
        for voice in (actual['voice'],actual['profiles'][0]['voice']):
            self.assertEqual(voice['baseModel'],'custom-base')
            self.assertFalse(voice['advancedEnabled']);self.assertEqual(voice['instruction'],'参数应保留')
    def rewrite(self,transform,extra=None):
        with zipfile.ZipFile(self.package) as archive:entries={n:archive.read(n) for n in archive.namelist()}
        manifest=json.loads(entries['manifest.json']);transform(manifest,entries);entries['manifest.json']=json.dumps(manifest).encode()
        if extra:entries.update(extra)
        bad=self.base/'modified.avconfig'
        with zipfile.ZipFile(bad,'w') as archive:
            for name,body in entries.items():archive.writestr(name,body)
        return bad
    def rejected(self,source):
        before=self.store.path.read_bytes()
        with self.assertRaises((ValueError,KeyError,zipfile.BadZipFile,OSError)):import_bundle(self.root,source,asset_root=self.assets)
        self.assertEqual(self.store.path.read_bytes(),before)
        self.assertFalse((self.root/'data/private-assets.json').exists())
        self.assertFalse(any(self.assets.iterdir()) if self.assets.exists() else False)
    def test_complete_modules_assets_roundtrip_and_local_credentials(self):
        self.store.update({'voice':{'speed':1.2},'appearance':{'accent':'#112233'},'phrases':['已改'],'profiles':[]})
        result=import_bundle(self.root,self.package,asset_root=self.assets);actual=Store(self.root).get()
        expected=copy.deepcopy(self.original)
        for old,new in [(expected['voice'],actual['voice']),(expected['profiles'][0]['voice'],actual['profiles'][0]['voice'])]:
            for key in ('reference',):
                self.assertEqual(digest(self.root/old[key]),digest(self.store.resolve(new[key])));old[key]=new[key]
        self.assertEqual(digest(self.root/expected['appearance']['background']),digest(self.store.resolve(actual['appearance']['background'])))
        expected['appearance']['background']=actual['appearance']['background']
        self.assertEqual(digest(self.root/expected['appearance']['loadingAnimation']),digest(self.store.resolve(actual['appearance']['loadingAnimation'])))
        expected['appearance']['loadingAnimation']=actual['appearance']['loadingAnimation']
        self.assertEqual(actual,expected);self.assertEqual(result['assetCount'],3);self.assertEqual(result['scope'],'private')
        self.assertTrue(Path(result['backup'],'settings.json').is_file())
        with zipfile.ZipFile(self.package) as archive:
            manifest=json.loads(archive.read('manifest.json'));self.assertNotIn('token',manifest['config']);self.assertNotIn('port',manifest['config'])
        registry=json.loads((self.root/'data/private-assets.json').read_text());self.assertEqual(len(registry['files']),3)
        again=import_bundle(self.root,self.package,asset_root=self.assets);self.assertEqual(again['assetDirectory'],result['assetDirectory'])
    def test_damaged_asset_does_not_replace_settings(self):
        def change(manifest,entries):entries[manifest['assets'][0]['entry']]=b'X'*manifest['assets'][0]['size']
        self.rejected(self.rewrite(change))
    def test_repeat_export_is_identical_and_does_not_recopy_assets(self):
        another=self.base/'again.avconfig';export_bundle(self.root,another)
        self.assertEqual(digest(another),digest(self.package))
    def test_path_traversal_is_rejected(self):self.rejected(self.rewrite(lambda m,e:None,{'../../evil.txt':b'bad'}));self.assertFalse((self.base/'evil.txt').exists())
    def test_unknown_version_is_rejected(self):self.rejected(self.rewrite(lambda m,e:m.update(version=2)))
    def test_foreign_connection_credentials_are_rejected(self):self.rejected(self.rewrite(lambda m,e:m['config'].update(token='foreign-token')))
    def test_private_scope_cannot_be_downgraded(self):self.rejected(self.rewrite(lambda m,e:m.update(scope='public')))
    def test_missing_asset_is_rejected(self):self.rejected(self.rewrite(lambda m,e:e.pop(m['assets'][0]['entry'])))
    def test_failure_rolls_back_registry_and_extracted_assets(self):
        before=self.store.path.read_bytes()
        def write(path,data):
            if Path(path)==self.store.path:raise OSError('injected settings write failure')
            atomic_json(path,data)
        with patch('state.atomic_json',write):
            with self.assertRaises(OSError):import_bundle(self.root,self.package,asset_root=self.assets)
        self.assertEqual(self.store.path.read_bytes(),before);self.assertFalse((self.root/'data/private-assets.json').exists());self.assertEqual(list(self.assets.iterdir()),[])
    def test_import_rejects_running_service(self):
        class IdlePipeline:
            loaded=False;gpu='fixture'
            def stop(self):pass
            def close(self):pass
        server=Server(self.store,IdlePipeline());worker=threading.Thread(target=server.serve_forever,daemon=True);worker.start()
        try:
            with self.assertRaisesRegex(ValueError,'关闭语音服务'):import_bundle(self.root,self.package,asset_root=self.assets)
        finally:server.shutdown_owned();server.jobs.close();server.server_close();worker.join(2)
    def check_release_rejected(self,package):
        resource=self.root/'renamed-example.bin';shutil.copy2(package,resource)
        manifest=self.base/'release.json';atomic_json(manifest,{'publicVoiceProvided':True,'runtimeIncluded':True,'userAccepted':True,'files':[resource.name]})
        result=audit(self.root,manifest);self.assertFalse(result['packageReady']);self.assertTrue(any('私有' in error for error in result['errors']))
    def test_renamed_private_bundle_is_blocked_from_release(self):self.check_release_rejected(self.package)
    def test_mislabeled_bundle_still_detects_protected_asset_bytes(self):
        def change(manifest,entries):
            manifest['scope']='public'
            for asset in manifest['assets']:asset['private']=False
            manifest['config']['voice']['assetScope']='public'
            for profile in manifest['config']['profiles']:profile['voice']['assetScope']='public'
        self.check_release_rejected(self.rewrite(change))

if __name__=='__main__':unittest.main(verbosity=2)
