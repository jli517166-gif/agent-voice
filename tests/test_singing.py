import json,sys,tempfile,unittest
from pathlib import Path
from unittest.mock import patch,Mock

sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'service'))
from singing import SingingManager


class SingingManagerTests(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory();self.root=Path(self.temp.name).resolve()
        catalog={'version':1,'engines':[{'id':'ace-step','name':'ACE-Step 1.5','adapter':'ace-step','roots':['singing-engines/ace-step','tts-engines/ace-step'],'launcher':'启动歌声引擎.cmd','port':7860}]}
        (self.root/'service').mkdir();(self.root/'service/singing-engine-catalog.json').write_text(json.dumps(catalog),encoding='utf-8')
        self.manager=SingingManager(self.root)
    def tearDown(self):self.temp.cleanup()
    def install(self,relative='singing-engines/ace-step'):
        engine=self.root/relative;(engine/'portable').mkdir(parents=True);(engine/'portable/python.exe').write_bytes(b'python');(engine/'启动歌声引擎.cmd').write_text('@echo off',encoding='utf-8');return engine
    def test_catalog_and_legacy_install_are_discovered_without_tts_coupling(self):
        self.install('tts-engines/ace-step')
        listed=self.manager.list_engines()
        self.assertEqual(listed['activeEngine'],'ace-step');self.assertTrue(listed['engines'][0]['installed'])
        self.assertIn('tts-engines/ace-step',listed['engines'][0]['root'].replace('\\','/'))
    def test_engine_switch_and_output_preference_are_persisted(self):
        target=self.root/'my-songs';self.manager.set_output_directory(target)
        self.manager.select_engine('ace-step')
        again=SingingManager(self.root)
        self.assertEqual(Path(again.settings()['outputDirectory']),target)
        self.assertEqual(again.settings()['activeEngine'],'ace-step')
    def test_unknown_engine_and_empty_generation_are_rejected(self):
        with self.assertRaisesRegex(ValueError,'唱歌引擎'):self.manager.select_engine('unknown')
        with self.assertRaisesRegex(ValueError,'歌词'):self.manager.submit('',prompt='pop')
    def test_submit_creates_background_job_with_explicit_storage(self):
        engine=self.install();worker=self.root/'service/singing_job.py';worker.write_text('# fixture',encoding='utf-8')
        fake=Mock(pid=4321)
        with patch('singing.subprocess.Popen',return_value=fake) as launch:
            job=self.manager.submit('[Verse]\n你好',prompt='dream pop',duration=15)
        record=json.loads(Path(job['jobFile']).read_text(encoding='utf-8'))
        self.assertEqual(record['state'],'queued');self.assertEqual(record['pid'],4321)
        command=launch.call_args.args[0]
        self.assertEqual(Path(command[0]),engine/'portable/python.exe');self.assertIn(str(worker),command)
        self.assertEqual(record['outputDirectory'],str(self.root/'data/singing/output'))
        self.assertNotIn('[Verse]',command)
    def test_output_directory_cannot_be_a_file(self):
        target=self.root/'not-a-folder';target.write_text('x')
        with self.assertRaisesRegex(ValueError,'目录'):self.manager.set_output_directory(target)


if __name__=='__main__':unittest.main()
