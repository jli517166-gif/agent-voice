import copy,json,shutil,socket,sys,tempfile,unittest,wave,zipfile
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'service'))
from state import Store,atomic_json
from tts_bundle import export_model,import_model,REQUIRED


class TtsBundleTests(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory();self.root=Path(self.temp.name)/'app';self.store=Store(self.root)
        cfg=self.store.get()
        with socket.socket() as s:s.bind(('127.0.0.1',0));cfg['port']=s.getsockname()[1]
        atomic_json(self.store.path,cfg);self.store=Store(self.root)
        self.model=self.root/'tts-engines/cosyvoice/models/fixture-model';self.model.mkdir(parents=True)
        source=self.root/'tts-engines/cosyvoice/source/cosyvoice';source.mkdir(parents=True);(source/'version.py').write_text('__version__="fixture"')
        for name in REQUIRED:(self.model/name).write_bytes(('fixture-only-'+name).encode())
        (self.model/'cosyvoice3.yaml').write_text('llm: !new:cosyvoice.llm.llm.CosyVoice3LM {}')
        (self.model/'CosyVoice-BlankEN').mkdir();(self.model/'CosyVoice-BlankEN/config.json').write_text('{}')
        (self.model/'wrong-rvc.pth').write_bytes(b'never-export')
        (self.model/'.cache').mkdir();(self.model/'.cache/secret.bin').write_bytes(b'never-export')
        (self.root/'rvc.pth').write_bytes(b'rvc-choice');(self.root/'rvc.index').write_bytes(b'index-choice')
        with wave.open(str(self.root/'reference.wav'),'wb') as w:w.setparams((1,2,24000,0,'NONE',''));w.writeframes(b'\0\0'*2400)
        self.store.update({'voice':{'model':'rvc.pth','index':'rvc.index','engine':'cosyvoice','baseModel':'tts-engines/cosyvoice/models/fixture-model','reference':'reference.wav','referenceText':'认真说话。','synthesisMode':'base','advancedEnabled':False},'appearance':{'accent':'#112233'},'phrases':['不应改变']})
        self.before=self.store.get();self.package=Path(self.temp.name)/'声音-轻量.cosyvoice.ttsmodel';self.full=Path(self.temp.name)/'声音-完整.cosyvoice.ttsmodel'
        export_model(self.root,self.package,package_mode='lightweight');export_model(self.root,self.full,package_mode='full')
    def tearDown(self):self.temp.cleanup()
    def rewrite(self,source,change):
        with zipfile.ZipFile(source) as a:files={n:a.read(n) for n in a.namelist()}
        manifest=json.loads(files['manifest.json']);change(manifest,files);files['manifest.json']=json.dumps(manifest).encode()
        bad=Path(self.temp.name)/('bad-'+source.name)
        with zipfile.ZipFile(bad,'w') as a:
            for name,data in files.items():a.writestr(name,data)
        return bad
    def rejected(self,path):
        before=self.store.path.read_bytes()
        with self.assertRaises((ValueError,KeyError,zipfile.BadZipFile)):import_model(self.root,path)
        self.assertEqual(self.store.path.read_bytes(),before)
        parent=self.root/'voice-model-packages'/'已导入';self.assertEqual(list(parent.iterdir()) if parent.exists() else [],[])
    def test_lightweight_roundtrip_reuses_exact_engine_model_and_preserves_other_modules(self):
        result=import_model(self.root,self.package);after=Store(self.root).get()
        self.assertTrue(result['ok']);self.assertEqual(result['packageMode'],'lightweight');self.assertEqual(after['voice']['synthesisMode'],'base')
        self.assertFalse(after['voice']['advancedEnabled'])
        for key in self.before:
            if key!='voice':self.assertEqual(after[key],self.before[key],key)
        for key in ('model','index','pitch','formant','processing','indexRate'):self.assertEqual(after['voice'][key],self.before['voice'][key])
        self.assertEqual(self.store.resolve(after['voice']['baseModel']),self.model.resolve())
        self.assertEqual(self.store.resolve(after['voice']['reference']).read_bytes(),(self.root/'reference.wav').read_bytes())
    def test_lightweight_standard_contains_reference_and_fingerprint_but_no_weights(self):
        with zipfile.ZipFile(self.package) as archive:names=archive.namelist();manifest=json.loads(archive.read('manifest.json'))
        self.assertEqual(manifest['version'],2);self.assertEqual(manifest['packageMode'],'lightweight')
        self.assertEqual(manifest['engine']['id'],'cosyvoice');self.assertEqual(len(manifest['engine']['modelFingerprint']),64)
        self.assertTrue(manifest['modelFiles']);self.assertFalse(any(name.startswith('model/') for name in names))
        self.assertFalse(any(name.endswith(('.pth','.index')) or '.cache' in name for name in names))
    def test_full_standard_installs_model_into_dedicated_tts_engine_folder(self):
        target=Path(self.temp.name)/'fresh';other=Store(target);cfg=other.get();(target/'tts-engines/cosyvoice/source/cosyvoice').mkdir(parents=True);shutil.copy2(self.root/'tts-engines/cosyvoice/source/cosyvoice/version.py',target/'tts-engines/cosyvoice/source/cosyvoice/version.py')
        with socket.socket() as s:s.bind(('127.0.0.1',0));cfg['port']=s.getsockname()[1]
        atomic_json(other.path,cfg)
        result=import_model(target,self.full);after=Store(target).get();installed=other.resolve(after['voice']['baseModel'])
        self.assertEqual(result['packageMode'],'full');self.assertEqual(installed.parents[0],target/'tts-engines/cosyvoice/models')
        for name in REQUIRED:self.assertEqual((installed/name).read_bytes(),(self.model/name).read_bytes())
    def test_lightweight_rejects_missing_or_changed_base_model(self):
        (self.model/'llm.pt').write_bytes(b'changed')
        self.rejected(self.package)
    def test_wrong_engine_and_model_type_rejected(self):
        self.rejected(self.rewrite(self.package,lambda m,f:m['engine'].update(id='other-tts')))
        self.rejected(self.rewrite(self.package,lambda m,f:m['engine'].update(modelType='cosyvoice2')))
    def test_source_or_runtime_fingerprint_mismatch_rejected(self):
        self.rejected(self.rewrite(self.package,lambda m,f:m['engine'].update(sourceFingerprint='0'*64)))
        self.rejected(self.rewrite(self.package,lambda m,f:m['engine'].update(runtimeFingerprint='1'*64)))
    def test_corrupted_full_weight_rejected_without_config_changes(self):
        self.rejected(self.rewrite(self.full,lambda m,f:f.update({'model/llm.pt':b'bad'})))
    def test_unregistered_resource_and_traversal_rejected(self):
        self.rejected(self.rewrite(self.package,lambda m,f:f.update({'../escape.pt':b'x'})))
    def test_rvc_parameter_smuggled_into_voice_rejected(self):
        self.rejected(self.rewrite(self.package,lambda m,f:m['voice'].update(model='rvc.pth')))
    def test_engine_suffix_required_for_export_and_import(self):
        with self.assertRaises(ValueError):export_model(self.root,self.package.with_suffix('.avconfig'))
        with self.assertRaises(ValueError):import_model(self.root,self.package.with_suffix('.avconfig'))
        with self.assertRaises(ValueError):export_model(self.root,self.package.with_name('.cosyvoice.ttsmodel'))
    def test_legacy_full_package_remains_compatible(self):
        def legacy(m,files):
            engine=m['engine'];m['version']=1;m['engine']=engine['id'];m['modelType']=engine['modelType'];m.pop('packageMode');m.pop('modelFiles')
        package=self.rewrite(self.full,legacy);result=import_model(self.root,package)
        self.assertEqual(result['packageMode'],'full');self.assertEqual(result['engine'],'cosyvoice')
    def test_named_saved_voice_can_be_exported_without_applying_it(self):
        cfg=self.store.get();voice=copy.deepcopy(cfg['voice']);voice['referenceText']='保存配置自己的台词。';cfg['profiles']=[{'id':'saved-one','name':'保存声音','voice':voice}];atomic_json(self.store.path,cfg)
        package=Path(self.temp.name)/'保存声音-轻量.cosyvoice.ttsmodel';export_model(self.root,package,'saved-one','lightweight')
        with zipfile.ZipFile(package) as archive:manifest=json.loads(archive.read('manifest.json'))
        self.assertEqual(manifest['voice']['referenceText'],'保存配置自己的台词。')
        with self.assertRaises(ValueError):export_model(self.root,package,'missing')
    def test_missing_required_model_resource_rejected(self):
        (self.model/'flow.pt').unlink()
        with self.assertRaises(ValueError):export_model(self.root,self.package)
    def test_renamed_other_model_yaml_is_not_a_cosyvoice3_model(self):
        (self.model/'cosyvoice3.yaml').write_text('llm: !new:cosyvoice.llm.llm.Qwen2LM {}')
        with self.assertRaises(ValueError):export_model(self.root,self.package)


if __name__=='__main__':unittest.main()
