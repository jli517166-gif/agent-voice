import json
from pathlib import Path
import subprocess
import sys
import tempfile
import time
import unittest
from unittest.mock import patch
import wave

sys.path.insert(0, str(Path(__file__).resolve().parents[1]/'service'))
from piper_client import PiperClient, model_config
from state import Store


class PiperTests(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory();self.root=Path(self.temp.name)
        self.model=self.root/'声音.onnx';self.model.write_bytes(b'protocol-test-only')
        self.config=Path(str(self.model)+'.json')
        self.config.write_text(json.dumps({'audio':{'sample_rate':22050},'phoneme_id_map':{'a':[1]}}))
        self.script=self.root/'fixture.py'
        self.script.write_text('''import sys,json,time,wave
print(json.dumps({'ready':True}),flush=True)
for line in sys.stdin:
    data=json.loads(line)
    if data['text']=='slow':time.sleep(10)
    if data['text']=='error':
        print(json.dumps({'ok':False,'error':'fixture failure'}),flush=True);continue
    with wave.open(data['output'],'wb') as w:
        w.setparams((1,2,22050,0,'NONE',''));w.writeframes(b'\\0\\0'*2205)
    print(json.dumps({'ok':True}),flush=True)
''',encoding='utf-8')
        original=subprocess.Popen
        self.patched=patch('piper_client.subprocess.Popen',lambda command,**kw:original([sys.executable,'-u',str(self.script)],**kw))
        self.patched.start();self.client=PiperClient(sys.executable,self.model,self.root/'worker.log',{'creationflags':getattr(subprocess,'CREATE_NO_WINDOW',0)})

    def tearDown(self):
        self.client.close();self.patched.stop();self.temp.cleanup()

    def test_requires_onnx_and_valid_companion(self):
        self.assertEqual(model_config(self.model),self.config)
        self.config.unlink()
        with self.assertRaisesRegex(ValueError,'同名'):model_config(self.model)
        self.config.write_text('{"audio":{"sample_rate":true},"phoneme_id_map":{}}')
        with self.assertRaisesRegex(ValueError,'格式'):model_config(self.model)
        for value in ([],None,{'audio':None}):
            self.config.write_text(json.dumps(value))
            with self.assertRaisesRegex(ValueError,'格式'):model_config(self.model)

    def test_reuses_worker_and_supports_unicode_output(self):
        target=self.root/'输出.wav'
        self.client.synthesize('first',target,1,lambda:None);pid=self.client.process.pid
        self.client.synthesize('second',target,1.2,lambda:None)
        self.assertEqual(self.client.process.pid,pid)
        with wave.open(str(target)) as audio:self.assertEqual(audio.getnframes(),2205)
        process=self.client.process;self.client.close();self.assertIsNotNone(process.poll())

    def test_worker_error_closes_process_and_next_request_recovers(self):
        with self.assertRaisesRegex(RuntimeError,'fixture failure'):
            self.client.synthesize('error',self.root/'error.wav',1,lambda:None)
        self.assertIsNone(self.client.process)
        self.client.synthesize('recovery',self.root/'recovered.wav',1,lambda:None)
        self.assertTrue((self.root/'recovered.wav').is_file())

    def test_cancel_stops_worker_and_does_not_leak_response(self):
        self.client.ensure_ready(lambda:None);process=self.client.process;start=time.monotonic()
        def cancel():
            if time.monotonic()-start>.2:raise RuntimeError('test cancelled')
        with self.assertRaisesRegex(RuntimeError,'test cancelled'):
            self.client.synthesize('slow',self.root/'cancel.wav',1,cancel)
        self.assertIsNotNone(process.poll());self.assertLess(time.monotonic()-start,5)
        self.client.synthesize('next',self.root/'next.wav',1,lambda:None)

    def test_independent_model_settings_and_saved_profile_survive_reload(self):
        store=Store(self.root);before=store.get()
        voice=dict(before['voice'],engine='piper',piperModel=str(self.model))
        store.update({'voice':voice,'profiles':[{'id':'piper-test','name':'Piper','voice':voice}]})
        again=Store(self.root).get()
        self.assertEqual(again['voice']['piperModel'],str(self.model))
        self.assertEqual(again['engines']['cosyModel'],before['engines']['cosyModel'])
        self.assertEqual(again['profiles'][0]['voice'],voice)

    def test_missing_resources_report_piper_not_cosyvoice(self):
        store=Store(self.root);voice=dict(store.get()['voice'],engine='piper',piperModel=str(self.model))
        missing=store.missing(voice)
        self.assertIn('Piper Python 运行时',missing)
        self.assertNotIn('公共参考录音',missing)
        self.config.unlink();self.assertIn('Piper 同名 ONNX JSON 配置',store.missing(voice))


if __name__=='__main__':unittest.main()
