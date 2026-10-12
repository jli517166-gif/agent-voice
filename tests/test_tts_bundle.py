import copy,json,socket,sys,tempfile,unittest,wave,zipfile
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
        model=self.root/'custom-model';model.mkdir()
        for name in REQUIRED:(model/name).write_bytes(('fixture-only-'+name).encode())
        (model/'cosyvoice3.yaml').write_text('llm: !new:cosyvoice.llm.llm.CosyVoice3LM {}')
        (model/'CosyVoice-BlankEN').mkdir();(model/'CosyVoice-BlankEN/config.json').write_text('{}')
        (model/'wrong-rvc.pth').write_bytes(b'never-export')
        (model/'.cache').mkdir();(model/'.cache/secret.bin').write_bytes(b'never-export')
        (self.root/'rvc.pth').write_bytes(b'rvc-choice');(self.root/'rvc.index').write_bytes(b'index-choice')
        with wave.open(str(self.root/'reference.wav'),'wb') as w:w.setparams((1,2,24000,0,'NONE',''));w.writeframes(b'\0\0'*2400)
        self.store.update({'voice':{'model':'rvc.pth','index':'rvc.index','engine':'cosyvoice','baseModel':'custom-model','reference':'reference.wav','referenceText':'认真说话。','synthesisMode':'base','advancedEnabled':False},'appearance':{'accent':'#112233'},'phrases':['不应改变']})
        self.before=self.store.get();self.package=Path(self.temp.name)/'声音.cosyvoice.ttsmodel'
        export_model(self.root,self.package)
    def tearDown(self):self.temp.cleanup()
    def rewrite(self,change):
        with zipfile.ZipFile(self.package) as a:files={n:a.read(n) for n in a.namelist()}
        manifest=json.loads(files['manifest.json']);change(manifest,files);files['manifest.json']=json.dumps(manifest).encode()
        bad=self.package.with_name('bad.cosyvoice.ttsmodel')
        with zipfile.ZipFile(bad,'w') as a:
            for name,data in files.items():a.writestr(name,data)
        return bad
    def rejected(self,path):
        before=self.store.path.read_bytes()
        with self.assertRaises((ValueError,KeyError,zipfile.BadZipFile)):import_model(self.root,path)
        self.assertEqual(self.store.path.read_bytes(),before)
        parent=self.root/'voice-model-packages'/'已导入';self.assertEqual(list(parent.iterdir()) if parent.exists() else [],[])
    def test_roundtrip_only_changes_local_tts_preserves_other_modules_and_rvc(self):
        result=import_model(self.root,self.package);after=Store(self.root).get()
        self.assertTrue(result['ok']);self.assertEqual(after['voice']['synthesisMode'],'base')
        self.assertFalse(after['voice']['advancedEnabled'])
        for k in self.before:
            if k!='voice':self.assertEqual(after[k],self.before[k],k)
        for key in ('model','index','pitch','formant','processing','indexRate'):self.assertEqual(after['voice'][key],self.before['voice'][key])
        imported=self.store.resolve(after['voice']['baseModel'])
        self.assertEqual(imported.parents[1],self.root/'voice-model-packages'/'已导入')
        for name in REQUIRED:self.assertEqual((imported/name).read_bytes(),(self.root/'custom-model'/name).read_bytes())
        self.assertEqual(self.store.resolve(after['voice']['reference']).read_bytes(),(self.root/'reference.wav').read_bytes())
    def test_package_contains_no_rvc_external_settings_or_cache(self):
        with zipfile.ZipFile(self.package) as a:
            names=a.namelist();m=json.loads(a.read('manifest.json'))
        self.assertFalse(any(n.endswith(('.pth','.index')) or '.cache' in n for n in names))
        self.assertNotIn('harness',m);self.assertNotIn('appearance',m);self.assertNotIn('token',m)
        self.assertNotIn('model',m['voice']);self.assertEqual(m['engine'],'cosyvoice');self.assertEqual(m['name'],'声音')
    def test_wrong_engine_rejected_even_when_renamed(self):
        self.rejected(self.rewrite(lambda m,f:m.update(engine='other-tts')))
    def test_wrong_model_type_rejected(self):
        self.rejected(self.rewrite(lambda m,f:m.update(modelType='cosyvoice2')))
    def test_corrupted_weight_rejected_without_config_changes(self):
        self.rejected(self.rewrite(lambda m,f:f.update({'model/llm.pt':b'bad'})))
    def test_unregistered_resource_and_traversal_rejected(self):
        self.rejected(self.rewrite(lambda m,f:f.update({'../escape.pt':b'x'})))
    def test_rvc_parameter_smuggled_into_voice_rejected(self):
        self.rejected(self.rewrite(lambda m,f:m['voice'].update(model='rvc.pth')))
    def test_engine_suffix_required_for_export_and_import(self):
        with self.assertRaises(ValueError):export_model(self.root,self.package.with_suffix('.avconfig'))
        with self.assertRaises(ValueError):import_model(self.root,self.package.with_suffix('.avconfig'))
        with self.assertRaises(ValueError):export_model(self.root,self.package.with_name('.cosyvoice.ttsmodel'))
    def test_old_package_without_internal_name_remains_compatible(self):
        legacy=self.rewrite(lambda m,f:m.pop('name'))
        result=import_model(self.root,legacy)
        self.assertEqual(result['name'],'bad')
        self.assertIn(str(self.root/'voice-model-packages'/'已导入'),result['modelDirectory'])
    def test_named_saved_voice_can_be_exported_without_applying_it(self):
        cfg=self.store.get();voice=copy.deepcopy(cfg['voice']);voice['referenceText']='保存配置自己的台词。'
        cfg['profiles']=[{'id':'saved-one','name':'保存声音','voice':voice}];atomic_json(self.store.path,cfg)
        package=Path(self.temp.name)/'保存声音.cosyvoice.ttsmodel';export_model(self.root,package,'saved-one')
        with zipfile.ZipFile(package) as archive:manifest=json.loads(archive.read('manifest.json'))
        self.assertEqual(manifest['voice']['referenceText'],'保存配置自己的台词。')
        with self.assertRaises(ValueError):export_model(self.root,package,'missing')
    def test_missing_required_model_resource_rejected(self):
        (self.root/'custom-model/flow.pt').unlink()
        with self.assertRaises(ValueError):export_model(self.root,self.package)
    def test_renamed_other_model_yaml_is_not_a_cosyvoice3_model(self):
        (self.root/'custom-model/cosyvoice3.yaml').write_text('llm: !new:cosyvoice.llm.llm.Qwen2LM {}')
        with self.assertRaises(ValueError):export_model(self.root,self.package)

if __name__=='__main__':unittest.main()
