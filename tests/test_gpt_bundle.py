import copy,json,shutil,socket,sys,tempfile,unittest,wave,zipfile
from pathlib import Path
from unittest.mock import patch
sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'service'))
from state import Store,atomic_json,defaults
from tts_bundle import export_model,import_model,gpt_sources
from runtime_paths import repair_bundled_runtime

class GptBundleTests(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory();self.root=Path(self.temp.name).resolve()/'app';self.store=self.make_store(self.root)
        self.fp=patch('tts_bundle.runtime_fingerprint',return_value='a'*64);self.fp.start();self.addCleanup(self.fp.stop)
        self.desc=patch('tts_bundle.runtime_descriptor',return_value={'python':'fixture','packages':{}});self.desc.start();self.addCleanup(self.desc.stop)
        self.model=self.root/'tts-engines/gpt-sovits/models/GPT-SoVITS'
        for name in ['s1v3.ckpt','v2Pro/s2Gv2ProPlus.pth','sv/pretrained_eres2netv2w24s4ep4.ckpt',
                     'gsv-v5-pretrained/s2Gv5turbo.pth','gsv-v5-pretrained/vocoder.pth',
                     'chinese-hubert-base/config.json','chinese-hubert-base/pytorch_model.bin','chinese-hubert-base/preprocessor_config.json',
                     'chinese-roberta-wwm-ext-large/config.json','chinese-roberta-wwm-ext-large/tokenizer.json',
                     'chinese-roberta-wwm-ext-large/pytorch_model.bin']:
            p=self.model/name;p.parent.mkdir(parents=True,exist_ok=True);p.write_bytes(name.encode())
        with wave.open(str(self.root/'ref.wav'),'wb') as w:w.setparams((1,2,16000,0,'NONE',''));w.writeframes(b'\0\0'*1600)
        self.store.update({'voice':{'engine':'gpt-sovits','gptVariant':'v5turbo','reference':'ref.wav','referenceText':'原台词','speed':1.1},'appearance':{'accent':'#123456'},'phrases':['不修改']})
        self.package=Path(self.temp.name).resolve()/'角色-轻量.gpt-sovits.ttsmodel'
    def tearDown(self):self.temp.cleanup()
    def make_store(self,root):
        store=Store(root);cfg=store.get()
        with socket.socket() as s:s.bind(('127.0.0.1',0));cfg['port']=s.getsockname()[1]
        atomic_json(store.path,cfg)
        p=root/'tts-engines/gpt-sovits/source/GPT_SoVITS/TTS_infer_pack/TTS.py';p.parent.mkdir(parents=True);p.write_text('# source fixture')
        return Store(root)
    def test_both_variants_lightweight_roundtrip_restore_version_and_reference(self):
        for variant in ['proplus','v5turbo']:
            self.store=Store(self.root);self.store.update({'voice':{'engine':'gpt-sovits','gptVariant':variant}});before=self.store.get()
            export_model(self.root,self.package)
            with zipfile.ZipFile(self.package) as z:
                m=json.loads(z.read('manifest.json'));self.assertFalse(any(n.startswith('model/') for n in z.namelist()))
                self.assertEqual(m['engine']['modelType'],'gpt-sovits-'+variant)
            self.store.update({'voice':{'engine':'cosyvoice','gptVariant':'proplus' if variant=='v5turbo' else 'v5turbo','referenceText':'修改过'}})
            result=import_model(self.root,self.package);after=Store(self.root).get()
            self.assertEqual((after['voice']['engine'],after['voice']['gptVariant']),('gpt-sovits',variant))
            self.assertEqual(after['voice']['referenceText'],'原台词');self.assertEqual(after['voice']['speed'],1.1)
            self.assertEqual(Path(result['modelDirectory']),self.model)
            self.assertEqual(self.store.resolve(after['voice']['reference']).read_bytes(),(self.root/'ref.wav').read_bytes())
            for key in before:
                if key!='voice':self.assertEqual(before[key],after[key])
    def test_full_package_installs_only_selected_variant_and_reuses_on_repeat(self):
        full=self.package.with_name('角色-完整.gpt-sovits.ttsmodel');export_model(self.root,full,package_mode='full')
        with zipfile.ZipFile(full) as z:
            self.assertIn('model/gsv-v5-pretrained/s2Gv5turbo.pth',z.namelist())
            self.assertNotIn('model/v2Pro/s2Gv2ProPlus.pth',z.namelist())
        other=self.make_store(Path(self.temp.name).resolve()/'fresh')
        first=import_model(other.root,full);second=import_model(other.root,full)
        self.assertEqual(first['modelDirectory'],second['modelDirectory'])
        installed=Path(first['modelDirectory']);self.assertEqual(installed.parent,other.root/'tts-engines/gpt-sovits/models')
        self.assertEqual((installed/'gsv-v5-pretrained/vocoder.pth').read_bytes(),(self.model/'gsv-v5-pretrained/vocoder.pth').read_bytes())
    def test_version_tamper_and_engine_suffix_mismatch_do_not_change_settings(self):
        export_model(self.root,self.package);before=self.store.path.read_bytes()
        with zipfile.ZipFile(self.package) as z:files={n:z.read(n) for n in z.namelist()}
        m=json.loads(files['manifest.json']);m['voice']['gptVariant']='proplus';files['manifest.json']=json.dumps(m).encode()
        bad=self.package.with_name('篡改.gpt-sovits.ttsmodel')
        with zipfile.ZipFile(bad,'w') as z:
            for n,b in files.items():z.writestr(n,b)
        with self.assertRaisesRegex(ValueError,'版本'):import_model(self.root,bad)
        disguised=self.package.with_name('伪装.cosyvoice.ttsmodel');shutil.copy2(self.package,disguised)
        with self.assertRaisesRegex(ValueError,'后缀'):import_model(self.root,disguised)
        self.assertEqual(self.store.path.read_bytes(),before)
    def test_incomplete_model_and_unsupported_training_export_rejected(self):
        with self.assertRaisesRegex(ValueError,'高级训练'):export_model(self.root,self.package,package_mode='trained')
        (self.model/'gsv-v5-pretrained/vocoder.pth').unlink()
        with self.assertRaisesRegex(ValueError,'缺少文件'):gpt_sources(self.model,'v5turbo')
    def test_v2_migration_removes_f5_and_preserves_approved_cosy_parameters(self):
        cfg=self.store.get();cfg['version']=2;cfg['voice'].update(engine='cosyvoice',f5Steps=32,f5Model='old.pt')
        cfg['engines'].update(pythonF5='old.exe',f5Model='old.pt',f5Vocoder='vocos')
        cfg['profiles']=[{'id':'old-f5','name':'F5','voice':dict(cfg['voice'],engine='f5')},{'id':'approved','name':'认可版','voice':copy.deepcopy(cfg['voice'])}]
        atomic_json(self.store.path,cfg);migrated=Store(self.root).get()
        self.assertEqual(migrated['version'],4);self.assertEqual([p['id'] for p in migrated['profiles']],['approved'])
        self.assertEqual({k:v for k,v in cfg['voice'].items() if not k.startswith('f5')},migrated['voice'])
        self.assertEqual(json.loads((self.root/'data/migrations/settings-before-stability.json').read_text(encoding='utf8')),cfg)
    def test_bundled_runtime_relocation_is_idempotent(self):
        runtime=self.root/'tts-engines/gpt-sovits/runtime';runtime.mkdir(parents=True)
        cfg=runtime/'pyvenv.cfg';cfg.write_text('home = X:\\old\\python\nexecutable = X:\\old\\python.exe\ninclude-system-site-packages = false\n')
        base=self.root/'tts-engines/cosyvoice/runtime/tools';base.mkdir(parents=True);(base/'python.exe').write_bytes(b'fixture')
        shared=self.root/'tts-engines/candidate-runtime/runtime/Lib/site-packages';shared.mkdir(parents=True)
        pth=runtime/'Lib/site-packages/000_candidate_gpu.pth';pth.parent.mkdir(parents=True);pth.write_text('old absolute path')
        self.assertEqual(len(repair_bundled_runtime(self.root)),2)
        self.assertIn(str(base),cfg.read_text());self.assertIn('sys.prefix',pth.read_text())
        self.assertNotIn(str(self.root),pth.read_text());self.assertEqual(repair_bundled_runtime(self.root),[])

if __name__=='__main__':unittest.main()
