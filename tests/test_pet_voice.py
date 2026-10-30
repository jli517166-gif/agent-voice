import json
import sys
import tempfile
import unittest
import wave
from pathlib import Path
from unittest.mock import Mock,patch

sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'service'))
from engine_pet import Adapter
from pet_endpoint import validate_endpoint
from pipeline import Pipeline
from state import Store
from voice_profiles import save_profile,apply_profile


class Response:
    def __init__(self,body,kind='application/json'):
        self.body=body;self.status=200;self.headers={'Content-Type':kind}
    def __enter__(self):return self
    def __exit__(self,*args):pass
    def read(self,limit):return self.body[:limit]


class PetVoiceTests(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory();self.root=Path(self.temp.name)
        for file in ('runtime/core/python.exe','tools/ffmpeg.exe'):
            path=self.root/file;path.parent.mkdir(parents=True,exist_ok=True);path.write_bytes(b'fixture')
        self.store=Store(self.root)
        self.voice=self.store.get()['voice'];self.voice.update(engine='dsh-pet',petEndpoint='http://127.0.0.1:3080/dsh-pet-7340',petVoice='sample')
        self.store.update({'voice':self.voice})
    def tearDown(self):self.temp.cleanup()

    def test_endpoint_stays_on_loopback(self):
        self.assertEqual(validate_endpoint(self.voice['petEndpoint']),self.voice['petEndpoint'])
        for value in ('http://example.com:3080/dsh-pet-7340','http://127.0.0.1:3080/admin',
                      'http://127.0.0.1:3080@evil.test/dsh-pet-7340','https://127.0.0.1:3080/dsh-pet-7340'):
            with self.subTest(value=value),self.assertRaises(ValueError):validate_endpoint(value)

    def test_pet_is_valid_without_local_model_or_reference(self):
        self.assertEqual(self.store.missing(self.voice),[])
        self.assertEqual(self.store.get()['voice']['engine'],'dsh-pet')
        saved=save_profile(self.store,'桌宠声音',self.voice)
        self.assertFalse(saved['referencePinned'])
        self.assertEqual(apply_profile(self.store,saved['profile']['id'])['config']['voice']['engine'],'dsh-pet')

    def test_prepare_and_generate_use_documented_http_contract(self):
        calls=[]
        def urlopen(request,timeout):
            calls.append((request.full_url,request.data,timeout))
            if request.full_url.endswith('/voice/voices'):
                return Response(json.dumps({'ok':True,'voices':[{'id':'sample'}]}).encode())
            return Response(b'ID3'+b'fake-mp3','audio/mpeg')
        target=self.root/'data/output/pet.wav'
        def convert(command,input,stdout,stderr,timeout,check):
            self.assertEqual(command[0],str(self.root/'tools/ffmpeg.exe'))
            self.assertEqual(input,b'ID3fake-mp3')
            with wave.open(str(target),'wb') as output:
                output.setnchannels(1);output.setsampwidth(2);output.setframerate(24000);output.writeframes(b'\x00\x00'*24000)
            return Mock(returncode=0,stderr=b'')
        with patch('engine_pet.urllib.request.urlopen',side_effect=urlopen),patch('engine_pet.subprocess.run',side_effect=convert):
            adapter=Adapter(self.store);adapter.prepare(self.voice,lambda:None)
            adapter.synthesize('你好',self.voice,target,lambda:None)
        self.assertTrue(target.is_file())
        self.assertEqual(json.loads(calls[-1][1]),{'text':'你好','rate':1,'voice':'sample'})

    def test_unknown_voice_and_json_failure_are_visible(self):
        with patch('engine_pet.urllib.request.urlopen',return_value=Response(b'{"ok":true,"voices":[]}')):
            with self.assertRaisesRegex(ValueError,'音色不存在'):Adapter(self.store).prepare(self.voice,lambda:None)
        adapter=Adapter(self.store);adapter.endpoint=self.voice['petEndpoint']
        with patch('engine_pet.urllib.request.urlopen',return_value=Response(b'{"ok":false,"reason":"generate-error"}')):
            with self.assertRaisesRegex(RuntimeError,'generate-error'):
                adapter.synthesize('你好',self.voice,self.root/'out.wav',lambda:None)


if __name__=='__main__':unittest.main()
