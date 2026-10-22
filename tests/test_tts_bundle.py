import optional_voice_module
import copy,json,shutil,socket,sys,tempfile,time,unittest,wave,zipfile
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'service'))
from state import Store,atomic_json
from tts_bundle import export_model,import_model,REQUIRED,runtime_compatibility


class TtsBundleTests(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory();self.root=Path(self.temp.name).resolve()/'app';self.store=Store(self.root)
        cfg=self.store.get();cfg['engines']['pythonTts']=sys.executable
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
        self.store.update({'voice':{'engine':'cosyvoice','baseModel':'tts-engines/cosyvoice/models/fixture-model','reference':'reference.wav','referenceText':'认真说话。','advancedEnabled':False},'appearance':{'accent':'#112233'},'phrases':['不应改变']})
        self.before=self.store.get();self.package=Path(self.temp.name).resolve()/'声音-轻量.cosyvoice.ttsmodel';self.full=Path(self.temp.name).resolve()/'声音-完整.cosyvoice.ttsmodel'
        export_model(self.root,self.package,package_mode='lightweight');export_model(self.root,self.full,package_mode='full')
    def tearDown(self):
        # Windows file scanners can briefly recreate or retain an entry after
        # rmtree has enumerated the directory (WinError 145).  Retry cleanup;
        # persistent leaks still fail the test instead of being hidden.
        for attempt in range(5):
            try:self.temp.cleanup();return
            except OSError:
                if attempt==4:raise
                time.sleep(.1*(attempt+1))
    def rewrite(self,source,change):
        with zipfile.ZipFile(source) as a:files={n:a.read(n) for n in a.namelist()}
        manifest=json.loads(files['manifest.json']);change(manifest,files);files['manifest.json']=json.dumps(manifest).encode()
        bad=Path(self.temp.name).resolve()/('bad-'+source.name)
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
        self.assertTrue(result['ok']);self.assertEqual(result['packageMode'],'lightweight');self.assertNotIn('synthesisMode',after['voice'])
        self.assertFalse(after['voice']['advancedEnabled'])
        for key in self.before:
            if key!='voice':self.assertEqual(after[key],self.before[key],key)
        for key in ('gptModel','gptVariant'):self.assertEqual(after['voice'][key],self.before['voice'][key])
        self.assertEqual(self.store.resolve(after['voice']['baseModel']),self.model.resolve())
        self.assertEqual(self.store.resolve(after['voice']['reference']).read_bytes(),(self.root/'reference.wav').read_bytes())
    def test_lightweight_package_honors_custom_source_and_model_directories(self):
        external=Path(self.temp.name).resolve()/'custom-model';shutil.move(str(self.model),str(external))
        source=Path(self.temp.name).resolve()/'custom-source';shutil.move(str(self.root/'tts-engines/cosyvoice/source'),str(source))
        self.store.update({'engines':{'cosyRepo':str(source),'cosyModel':str(external)},'voice':{'baseModel':str(external)}})
        export_model(self.root,self.package,package_mode='lightweight')
        result=import_model(self.root,self.package)
        self.assertEqual(Path(result['modelDirectory']),external.resolve())
        self.assertEqual(list((self.root/'tts-engines/cosyvoice/models').iterdir()),[])
    def test_lightweight_standard_contains_reference_and_fingerprint_but_no_weights(self):
        with zipfile.ZipFile(self.package) as archive:names=archive.namelist();manifest=json.loads(archive.read('manifest.json'))
        self.assertEqual(manifest['version'],2);self.assertEqual(manifest['packageMode'],'lightweight')
        self.assertEqual(manifest['engine']['id'],'cosyvoice');self.assertEqual(len(manifest['engine']['modelFingerprint']),64)
        self.assertTrue(manifest['modelFiles']);self.assertFalse(any(name.startswith('model/') for name in names))
        self.assertFalse(any(name.endswith(('.pth','.index')) or '.cache' in name for name in names))
    def test_full_standard_installs_model_into_dedicated_tts_engine_folder(self):
        target=Path(self.temp.name).resolve()/'fresh';other=Store(target);cfg=other.get();cfg['engines']['pythonTts']=sys.executable;(target/'tts-engines/cosyvoice/source/cosyvoice').mkdir(parents=True);shutil.copy2(self.root/'tts-engines/cosyvoice/source/cosyvoice/version.py',target/'tts-engines/cosyvoice/source/cosyvoice/version.py')
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
        package=Path(self.temp.name).resolve()/'保存声音-轻量.cosyvoice.ttsmodel';export_model(self.root,package,'saved-one','lightweight')
        with zipfile.ZipFile(package) as archive:manifest=json.loads(archive.read('manifest.json'))
        self.assertEqual(manifest['voice']['referenceText'],'保存配置自己的台词。')
        with self.assertRaises(ValueError):export_model(self.root,package,'missing')
    def test_missing_required_model_resource_rejected(self):
        (self.model/'flow.pt').unlink()
        with self.assertRaises(ValueError):export_model(self.root,self.package)
    def test_renamed_other_model_yaml_is_not_a_cosyvoice3_model(self):
        (self.model/'cosyvoice3.yaml').write_text('llm: !new:cosyvoice.llm.llm.Qwen2LM {}')
        with self.assertRaises(ValueError):export_model(self.root,self.package)

    def trained_model(self):
        trained=Path(self.temp.name).resolve()/'trained-result';shutil.copytree(self.model,trained)
        (trained/'llm.pt').write_bytes(b'fine-tuned-llm-weights')
        return trained

    def fresh_root(self,with_base):
        target=Path(self.temp.name).resolve()/('fresh-with-base' if with_base else 'fresh-without-base');store=Store(target);cfg=store.get();cfg['engines']['pythonTts']=sys.executable
        source=target/'tts-engines/cosyvoice/source/cosyvoice';source.mkdir(parents=True)
        shutil.copy2(self.root/'tts-engines/cosyvoice/source/cosyvoice/version.py',source/'version.py')
        if with_base:shutil.copytree(self.model,target/'tts-engines/cosyvoice/models/fixture-model')
        with socket.socket() as s:s.bind(('127.0.0.1',0));cfg['port']=s.getsockname()[1]
        atomic_json(store.path,cfg);return target,Store(target)

    def test_advanced_trained_delta_roundtrip_requires_and_rebuilds_exact_base(self):
        trained=self.trained_model();package=Path(self.temp.name).resolve()/'高级训练.cosyvoice.ttsmodel'
        export_model(self.root,package,package_mode='trained',trained_model_dir=trained)
        with zipfile.ZipFile(package) as archive:
            manifest=json.loads(archive.read('manifest.json'));models={n for n in archive.namelist() if n.startswith('model/')}
        self.assertEqual(manifest['version'],3);self.assertEqual(manifest['packageMode'],'trained')
        self.assertEqual(manifest['baseModel']['deltaFiles'],['llm.pt']);self.assertEqual(models,{'model/llm.pt'})
        target,store=self.fresh_root(True);result=import_model(target,package);installed=store.resolve(Store(target).get()['voice']['baseModel'])
        self.assertEqual(result['packageMode'],'trained');self.assertEqual((installed/'llm.pt').read_bytes(),b'fine-tuned-llm-weights')
        self.assertEqual((installed/'flow.pt').read_bytes(),(self.model/'flow.pt').read_bytes())

    def test_advanced_trained_delta_rejects_when_base_is_missing(self):
        trained=self.trained_model();package=Path(self.temp.name).resolve()/'高级训练缺基础.cosyvoice.ttsmodel'
        export_model(self.root,package,package_mode='trained',trained_model_dir=trained)
        target,_=self.fresh_root(False)
        with self.assertRaisesRegex(ValueError,'需要匹配的基础模型'):import_model(target,package)

    def test_advanced_full_roundtrip_does_not_require_base_model(self):
        trained=self.trained_model();package=Path(self.temp.name).resolve()/'高级完整.cosyvoice.ttsmodel'
        export_model(self.root,package,package_mode='trained-full',trained_model_dir=trained)
        target,store=self.fresh_root(False);result=import_model(target,package);installed=store.resolve(Store(target).get()['voice']['baseModel'])
        self.assertEqual(result['packageMode'],'trained-full');self.assertEqual((installed/'llm.pt').read_bytes(),b'fine-tuned-llm-weights')

    def test_advanced_delta_manifest_tampering_is_rejected(self):
        trained=self.trained_model();package=Path(self.temp.name).resolve()/'高级篡改.cosyvoice.ttsmodel'
        export_model(self.root,package,package_mode='trained',trained_model_dir=trained)
        bad=self.rewrite(package,lambda m,f:m['baseModel'].update(deltaFiles=['../outside.pt']))
        self.rejected(bad)


class RuntimeCompatibilityTests(unittest.TestCase):
    def test_torch_family_upgrade_is_allowed_with_warning(self):
        expected={'python':'3.10.11','packages':{'torch':'2.3.1+cu121','torchaudio':'2.3.1+cu121','numpy':'1.26.4'}}
        actual={'python':'3.10.11','packages':{'torch':'2.7.1+cu128','torchaudio':'2.7.1+cu128','torchvision':'0.22.1+cu128','numpy':'1.26.4'}}
        ok,warnings,errors=runtime_compatibility(expected,actual)
        self.assertTrue(ok);self.assertEqual(errors,[])
        self.assertTrue(any('torch' in item for item in warnings));self.assertTrue(any('torchvision' in item for item in warnings))
    def test_python_minor_mismatch_is_rejected(self):
        ok,warnings,errors=runtime_compatibility({'python':'3.10.11','packages':{}},{'python':'3.11.9','packages':{}})
        self.assertFalse(ok);self.assertTrue(any('Python' in item for item in errors))
    def test_non_torch_package_mismatch_is_rejected(self):
        ok,warnings,errors=runtime_compatibility({'python':'3.10.11','packages':{'numpy':'1.26.4'}},{'python':'3.10.11','packages':{'numpy':'2.0.0'}})
        self.assertFalse(ok);self.assertTrue(any('numpy' in item for item in errors))
    def test_missing_torch_is_rejected(self):
        ok,warnings,errors=runtime_compatibility({'python':'3.10.11','packages':{'torch':'2.3.1+cu121'}},{'python':'3.10.11','packages':{}})
        self.assertFalse(ok);self.assertTrue(any('torch' in item for item in errors))
    def test_new_torchvision_only_is_allowed(self):
        ok,warnings,errors=runtime_compatibility({'python':'3.10.11','packages':{'torch':'2.7.1+cu128'}},{'python':'3.10.11','packages':{'torch':'2.7.1+cu128','torchvision':'0.22.1+cu128'}})
        self.assertTrue(ok);self.assertEqual(errors,[]);self.assertTrue(warnings)


if __name__=='__main__':unittest.main()
