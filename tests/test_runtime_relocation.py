"""Portable GPT runtimes must relocate before environment inspection/repair."""
import json,sys,tempfile,unittest
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'tools'))
sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'service'))
from environment_repair import Repair
from package_portable import stage
from state import defaults


class RuntimeRelocationTests(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory();self.addCleanup(self.temp.cleanup)
        self.parent=Path(self.temp.name).resolve()
        self.root=self.parent/'全新目录 (2)'/'Agent语音'

    def put(self,root,name,content=b'fixture'):
        path=root/name;path.parent.mkdir(parents=True,exist_ok=True)
        path.write_bytes(content);return path

    def bundled_runtime(self,root):
        self.put(root,'runtime/core/python.exe')
        self.put(root,'tts-engines/gpt-sovits/runtime/Scripts/python.exe')
        runtime=root/'tts-engines/gpt-sovits/runtime'
        self.put(root,'tts-engines/gpt-sovits/runtime/gpu-packages/torchvision/__init__.py')
        cfg=self.put(root,'tts-engines/gpt-sovits/runtime/pyvenv.cfg',
                     b'home = X:/packaging-machine/runtime/core\nexecutable = X:/packaging-machine/runtime/core/python.exe\ninclude-system-site-packages = true\nversion = 3.10.11\n')
        pth=self.put(root,'tts-engines/gpt-sovits/runtime/Lib/site-packages/000_candidate_gpu.pth',
                     b'import sys; sys.path.insert(0,"X:/packaging-machine/gpu-packages")\n')
        return cfg,pth

    def assert_relocated(self,root):
        runtime=root/'tts-engines/gpt-sovits/runtime'
        cfg=dict(line.split(' = ',1) for line in (runtime/'pyvenv.cfg').read_text(encoding='utf-8').splitlines())
        self.assertEqual(cfg,{'home':str(root/'runtime/core'),
                             'executable':str(root/'runtime/core/python.exe'),
                             'include-system-site-packages':'false','version':'3.10.11'})
        pth=(runtime/'Lib/site-packages/000_candidate_gpu.pth').read_text(encoding='utf-8')
        self.assertIn('sys.prefix',pth);self.assertNotIn('packaging-machine',pth)
        self.assertNotIn(str(root),pth)

    def test_first_repair_relocates_runtime_without_launcher_or_settings(self):
        cfg,pth=self.bundled_runtime(self.root)
        self.assertFalse((self.root/'data/settings.json').exists())
        helper=Repair(self.root)
        self.assertEqual(helper.root,self.root)
        self.assert_relocated(self.root)
        before=(cfg.read_bytes(),pth.read_bytes(),cfg.stat().st_mtime_ns,pth.stat().st_mtime_ns)
        Repair(self.root)
        self.assertEqual((cfg.read_bytes(),pth.read_bytes(),cfg.stat().st_mtime_ns,pth.stat().st_mtime_ns),before)

    def test_repair_preserves_custom_environment_and_settings(self):
        cfg=self.put(self.root,'tts-engines/gpt-sovits/runtime/pyvenv.cfg',
                     b'home = C:/custom-python\ninclude-system-site-packages = true\n')
        self.put(self.root,'runtime/core/python.exe')
        helper=Repair(self.root);helper.store.update({'voice':{'speed':1.1},'engines':{'pythonGpt':'C:/custom-python/python.exe'}})
        before=(cfg.read_bytes(),helper.store.path.read_bytes())
        Repair(self.root)
        self.assertEqual((cfg.read_bytes(),helper.store.path.read_bytes()),before)

    def test_stage_relocates_copy_then_repair_handles_another_move(self):
        build=self.parent/'build';installation=self.root;target=self.parent/'暂存 (3)'
        self.put(build,'agent语音.exe');self.put(build,'tools/fixture.py')
        cfg,pth=self.bundled_runtime(installation);original=(cfg.read_bytes(),pth.read_bytes())
        for name in ('tts-engines/cosyvoice/source/fixture.py','tts-engines/cosyvoice/runtime/tools/python.exe',
                     'tts-engines/gpt-sovits/source/GPT_SoVITS/text/G2PWModel/config.json',
                     'tts-engines/gpt-sovits/models/GPT-SoVITS/fixture.bin',
                     'tts-engines/gpt-sovits/nltk_data/corpora/cmudict/fixture',
                     'tools/ffmpeg.exe','tools/ffprobe.exe'):
            self.put(installation,name)
        model=self.parent/'public-model';self.put(model,'llm.pt')
        reference=self.put(self.parent,'reference.wav')
        voice=defaults()['voice'];voice['referenceText']='公共参考台词。'
        spec=self.parent/'voice.json';spec.write_text(json.dumps({'version':1,'name':'Fairy','voice':voice}),encoding='utf-8')
        with self.assertRaisesRegex(ValueError,'停用'):stage(build,installation,model,reference,spec,target,'fixture')
        self.assertFalse(target.exists())
        import shutil
        shutil.copytree(installation,target)
        from runtime_paths import repair_bundled_runtime
        repair_bundled_runtime(target)
        self.assert_relocated(target)
        self.assertEqual((cfg.read_bytes(),pth.read_bytes()),original)
        self.assertFalse((target/'voices').exists())
        moved=self.parent/'目标机 (4)';target.rename(moved)
        Repair(moved)
        self.assert_relocated(moved)
        self.assertEqual((cfg.read_bytes(),pth.read_bytes()),original)


if __name__=='__main__':unittest.main()
