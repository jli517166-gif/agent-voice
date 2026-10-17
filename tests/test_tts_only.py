import json,sys,tempfile,unittest,copy
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'service'))
from state import Store,defaults,atomic_json
from pipeline import effective_voice
from local_tts import worker_config,required_files

class TtsOnlyTests(unittest.TestCase):
    def test_legacy_settings_and_profiles_migrate_with_exact_backup(self):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp).resolve();cfg=defaults();cfg['version']=1
            cfg['voice'].update(model='old.pth',index='old.index',pitch=3,formant=.4,synthesisMode='rvc',processing='accepted',speed=1.12,reference='own.wav',referenceText='原台词')
            cfg['engines'].pop('pythonService');cfg['engines'].update(pythonRvc='custom-python.exe',rvcHome='old-rvc')
            cfg['profiles']=[{'id':'old','name':'旧认可版','voice':copy.deepcopy(cfg['voice'])},{'id':'retired','name':'淘汰测试','voice':dict(cfg['voice'],engine='piper')}];cfg['modelRoots']=['old-models']
            atomic_json(root/'data/settings.json',cfg);actual=Store(root).get()
            self.assertEqual(actual['version'],4);self.assertEqual(actual['engines']['pythonService'],'custom-python.exe')
            self.assertEqual([p['id'] for p in actual['profiles']],['old'])
            for voice in [actual['voice'],actual['profiles'][0]['voice']]:
                self.assertFalse(set(voice)&{'model','index','pitch','formant','synthesisMode'});self.assertEqual(voice['reference'],'own.wav');self.assertEqual(voice['speed'],1.12)
            self.assertEqual(json.loads((root/'data/migrations/settings-before-stability.json').read_text(encoding='utf-8')),cfg)
            self.assertEqual(actual['token'],cfg['token']);self.assertEqual(actual['harness'],cfg['harness'])
    def test_removed_engines_and_processing_rejected_without_disk_changes(self):
        with tempfile.TemporaryDirectory() as temp:
            store=Store(temp);before=store.path.read_bytes()
            for patch in [{'engine':x} for x in ('moss','piper','sapi','voxcpm','chatterbox','qwen','f5')]+[{'synthesisMode':'rvc'},{'pitch':4},{'gptVariant':'unknown'},{'f5Steps':8},{'f5Steps':True}]:
                with self.assertRaises(ValueError):store.update({'voice':patch})
                self.assertEqual(store.path.read_bytes(),before)
    def test_advanced_off_preserves_reference_speed_and_tts_variants(self):
        raw=defaults()['voice'];raw.update(engine='gpt-sovits',gptVariant='v5turbo',reference='own.wav',speed=1.12,instruction='调皮',volume=.4)
        applied=effective_voice(raw)
        self.assertEqual((applied['instruction'],applied['volume']),('',1))
        for key in ('engine','gptVariant','reference','speed'):self.assertEqual(applied[key],raw[key])
        self.assertEqual(raw['volume'],.4)
    def test_gpt_variants_resolve_distinct_acoustic_models(self):
        with tempfile.TemporaryDirectory() as temp:
            store=Store(temp);v=store.get()['voice'];v['engine']='gpt-sovits';v['gptVariant']='proplus';a=required_files(worker_config(store,v));v['gptVariant']='v5turbo';b=required_files(worker_config(store,v))
            self.assertIn('s2Gv2ProPlus.pth',[x.name for x in a]);self.assertIn('s2Gv5turbo.pth',[x.name for x in b]);self.assertNotEqual(a,b)

if __name__=='__main__':unittest.main()
