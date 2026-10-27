"""Minimal local model fixtures for core profile and environment tests."""
import socket,sys,wave
from pathlib import Path
from support_temp import TemporaryDirectory
sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'service'))
from state import Store,atomic_json
from model_integrity import REQUIRED
class ModelFixture:
 def setUp(self):
  self.temp=TemporaryDirectory();self.root=Path(self.temp.name).resolve()/'app';self.store=Store(self.root)
  cfg=self.store.get();cfg['engines']['pythonTts']=sys.executable
  with socket.socket() as sock:sock.bind(('127.0.0.1',0));cfg['port']=sock.getsockname()[1]
  atomic_json(self.store.path,cfg);self.store=Store(self.root)
  self.model=self.root/'tts-engines/cosyvoice/models/fixture-model';self.model.mkdir(parents=True)
  source=self.root/'tts-engines/cosyvoice/source/cosyvoice';source.mkdir(parents=True);(source/'version.py').write_text('__version__="fixture"')
  for name in REQUIRED:(self.model/name).write_bytes(('fixture-only-'+name).encode())
  (self.model/'cosyvoice3.yaml').write_text('llm: !new:cosyvoice.llm.llm.CosyVoice3LM {}')
  (self.model/'CosyVoice-BlankEN').mkdir();(self.model/'CosyVoice-BlankEN/config.json').write_text('{}')
  with wave.open(str(self.root/'reference.wav'),'wb') as output:output.setparams((1,2,24000,0,'NONE',''));output.writeframes(bytes(4800))
  self.store.update({'voice':{'engine':'cosyvoice','baseModel':'tts-engines/cosyvoice/models/fixture-model','reference':'reference.wav','referenceText':'认真说话。','advancedEnabled':False},'appearance':{'accent':'#112233'},'phrases':['不应改变']})
  self.before=self.store.get()
 def tearDown(self):self.temp.cleanup()
