"""Security and request-shape checks for the standalone pet source provider."""
import importlib.util
import json
import sys
import tempfile
import types
import unittest
from pathlib import Path
from unittest.mock import patch


PROVIDER = Path(__file__).resolve().parents[1] / 'integrations/dsh-pet-indesktop/agent_voice.py'


def load_provider():
    class TtsField:
        def __init__(self, *args, **kwargs):pass
    class TtsProvider:pass
    class TtsUnavailable(RuntimeError):pass
    base=types.ModuleType('pet.tts.base')
    base.TtsField=TtsField;base.TtsProvider=TtsProvider
    base.TtsUnavailable=TtsUnavailable;base.register=lambda provider:provider
    pet=types.ModuleType('pet');pet.__path__=[]
    tts=types.ModuleType('pet.tts');tts.__path__=[]
    with patch.dict(sys.modules,{'pet':pet,'pet.tts':tts,'pet.tts.base':base}):
        spec=importlib.util.spec_from_file_location('pet.tts.agent_voice',PROVIDER)
        module=importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
    return module


class Response:
    status=200
    headers={'Content-Type':'audio/mpeg'}
    def __enter__(self):return self
    def __exit__(self,*args):pass
    def read(self,limit):return b'ID3' + b'voice'


class PetSourceProviderTests(unittest.TestCase):
    def setUp(self):
        self.module=load_provider()
        self.temp=tempfile.TemporaryDirectory()
        self.root=Path(self.temp.name)
        (self.root/'data').mkdir()
        self.config={'port':8820,'token':'private-token','voice':{'engine':'cosyvoice'}}
        self._save()
        self.provider=self.module.PROVIDER
        self.values={'root':str(self.root),'profile':'current','speed':110}
    def tearDown(self):self.temp.cleanup()
    def _save(self):(self.root/'data/settings.json').write_text(json.dumps(self.config),encoding='utf-8')

    def test_authenticated_local_mp3_and_no_stale_cache(self):
        calls=[]
        class Opener:
            def open(self,request,timeout):calls.append((request,timeout));return Response()
        with patch.object(self.module.urllib.request,'build_opener',return_value=Opener()) as build:
            target=self.root/'audio.part'
            self.provider.synth('桌宠你好',self.provider.plan(self.values),target)
        self.assertEqual(target.read_bytes(),b'ID3voice')
        self.assertFalse(self.provider.cacheable)
        request,timeout=calls[0]
        self.assertEqual(request.full_url,'http://127.0.0.1:8820/v1/audio/speech')
        self.assertEqual(request.get_method(),'POST')
        self.assertEqual(request.get_header('Authorization'),'Bearer private-token')
        self.assertEqual(request.get_header('X-agent-voice-pet'),'standalone')
        self.assertEqual(json.loads(request.data)['speed'],1.1)
        self.assertEqual(json.loads(request.data)['voice'],'current')
        self.assertEqual(timeout,14)
        self.assertIsInstance(build.call_args.args[0],self.module.urllib.request.ProxyHandler)

    def test_recursion_guard_before_http(self):
        self.config['voice']['engine']='dsh-pet';self._save()
        with patch.object(self.module.urllib.request,'build_opener') as build:
            with self.assertRaisesRegex(self.module.TtsUnavailable,'循环调用'):
                self.provider.synth('你好',self.provider.plan(self.values),self.root/'out.mp3')
        build.assert_not_called()

    def test_bad_settings_do_not_expose_token(self):
        self.config['port']='bad';self._save()
        with self.assertRaises(self.module.TtsUnavailable) as caught:
            self.provider.synth('你好',self.provider.plan(self.values),self.root/'out.mp3')
        self.assertNotIn('private-token',str(caught.exception))


if __name__=='__main__':unittest.main()
