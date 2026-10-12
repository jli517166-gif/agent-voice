import copy
import io
import json
from pathlib import Path
import socket
import sys
import tempfile
import threading
import time
import unittest
import urllib.error
import urllib.request
import wave

sys.path.insert(0,str(Path(__file__).resolve().parents[1]/"service"))
from state import Store, defaults, validate, atomic_json
from text import sanitize,playback_chunks
from harness import normalize,CodexTail,HarnessPresence
from server import Server

class FakePipeline:
    """Protocol-only test audio, explicitly not a speech-quality fixture."""
    loaded=True;gpu="protocol-test-double"
    def __init__(self):self.calls=[];self.plays=[];self.stopped=0
    def synthesize(self,text,voice,path,check):
        self.calls.append((text,copy.deepcopy(voice)))
        for _ in range(8):time.sleep(.008);check()
        with wave.open(str(path),"wb") as wav:
            wav.setparams((1,2,24000,0,"NONE",""));wav.writeframes(b"\0\0"*2400)
        return {"sampleRate":24000,"duration":.1}
    def play(self,path,voice,check):self.plays.append(path);check();time.sleep(.02);check();return 48000
    def concatenate(self,paths,target,check):
        frames=[]
        for path in paths:
            check()
            with wave.open(str(path),'rb') as wav:frames.append(wav.readframes(wav.getnframes()))
        with wave.open(str(target),'wb') as wav:wav.setparams((1,2,24000,0,'NONE',''));wav.writeframes(b''.join(frames))
        return 24000,sum(len(part) for part in frames)/2/24000
    def stop(self):self.stopped+=1
    def close(self):pass

class CoreTests(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory();self.root=Path(self.temp.name);self.store=Store(self.root)
        with socket.socket() as sock:sock.bind(('127.0.0.1',0));port=sock.getsockname()[1]
        self.store.data['port']=port
        model=self.root/'fixture.pth';model.write_bytes(b'test-only')
        self.store.update({'voice':{'model':'fixture.pth','engine':'sapi','synthesisMode':'rvc','advancedEnabled':True},'engines':{'pythonRvc':str(sys.executable),'rvcHome':str(self.root)}})
        self.actor=FakePipeline();self.server=Server(self.store,self.actor)
        self.thread=threading.Thread(target=self.server.serve_forever,daemon=True);self.thread.start()
        self.base='http://127.0.0.1:'+str(port);self.token=self.store.get()['token']
        self.opener=urllib.request.build_opener(urllib.request.ProxyHandler({}))
    def tearDown(self):
        self.server.shutdown_owned();self.server.jobs.close();self.server.server_close();self.thread.join(2);self.temp.cleanup()
    def request(self,path,data=None,auth=True,raw=False):
        headers={'Content-Type':'application/json'}
        if auth:headers['Authorization']='Bearer '+self.token
        req=urllib.request.Request(self.base+path,data=None if data is None else json.dumps(data).encode(),headers=headers)
        with self.opener.open(req,timeout=3) as response:
            body=response.read();return (body,response.headers.get_content_type()) if raw else json.loads(body)
    def wait(self,ident):
        deadline=time.monotonic()+3
        while time.monotonic()<deadline:
            result=self.request('/api/jobs/'+ident)
            if result['state'] in ('completed','cancelled','failed'):return result
            time.sleep(.01)
        self.fail('job did not finish')
    def test_initial_public_config_contains_no_private_voice(self):
        cfg=defaults();self.assertEqual(cfg['voice']['model'],'');self.assertEqual(cfg['voice']['reference'],'');self.assertEqual([k for k,v in cfg['harness'].items() if v],['codex'])
        self.assertEqual(cfg['voice']['synthesisMode'],'base');self.assertFalse(cfg['voice']['advancedEnabled'])
    def test_sanitizer_retains_body_skips_code_links_and_paths(self):
        self.assertEqual(sanitize('工作好久啦，起来动一动、喝口水吧～'),'工作好久啦，起来动一动、喝口水吧～')
        self.assertEqual(sanitize('```py\nprint(1)\n``` https://example.com E:\\secret\\x.pth'),'')
        self.assertEqual(sanitize('[喝水](https://example.com) 然后休息。'),'喝水 然后休息。')
        self.assertNotIn('localhost',sanitize('休息一下。 localhost:1234 fooBarBaz()'))
    def test_progressive_chunks_preserve_complete_sentences(self):
        text='第一句虽然有逗号，但必须完整生成后再播放。第二句和第三句会在播放时继续准备，确保完整内容一个字也不少。最后一句用于确认顺序。'
        chunks=playback_chunks(text)
        self.assertEqual(len(chunks),3);self.assertTrue(all(chunk.endswith('。') for chunk in chunks));self.assertEqual(''.join(chunks),text)
        self.assertEqual(playback_chunks('这是一整句，里面有很多逗号，仍然不能从逗号中间切开'),['这是一整句，里面有很多逗号，仍然不能从逗号中间切开'])
        paragraph='第一句。\n第二句。';self.assertEqual(playback_chunks(paragraph),['第一句。\n','第二句。']);self.assertEqual(''.join(playback_chunks(paragraph)),paragraph)
    def test_config_failure_does_not_overwrite_original(self):
        before=self.store.path.read_bytes()
        with self.assertRaises(ValueError):self.store.update({'voice':{'formant':float('nan')}})
        self.assertEqual(before,self.store.path.read_bytes())
    def test_corrupt_storage_never_resets(self):
        self.store.path.write_text('{broken',encoding='utf-8')
        with self.assertRaises(ValueError):Store(self.root)
        self.assertEqual(self.store.path.read_text(),'{broken')
    def test_full_profile_save_restore_delete_reopen(self):
        original=self.store.get()['voice'];original.update(pitch=4,formant=.7,speed=1.12,instruction='温柔地说')
        self.store.update({'profiles':[{'id':'one','name':'声音一','voice':original}]})
        self.store.update({'voice':{'pitch':-2,'formant':-.8}})
        reopened=Store(self.root);reopened.update({'voice':reopened.get()['profiles'][0]['voice']})
        self.assertEqual(reopened.get()['voice'],original)
        reopened.update({'profiles':[]});self.assertEqual(Store(self.root).get()['profiles'],[]);self.assertTrue((self.root/'fixture.pth').exists())
    def test_authentication_and_unknown_interfaces(self):
        with self.assertRaises(urllib.error.HTTPError) as err:self.request('/api/state',auth=False)
        self.assertEqual(err.exception.code,401)
        with self.assertRaises(urllib.error.HTTPError) as err:self.request('/nothing')
        self.assertEqual(err.exception.code,404)
    def test_connector_heartbeat_auth_enabled_and_expiry(self):
        self.assertEqual(self.request('/health')['harnessConnections']['pi']['state'],'disabled')
        self.request('/api/harness/pi/heartbeat',{})
        self.store.update({'harness':{'pi':True}})
        self.assertEqual(self.request('/health')['harnessConnections']['pi']['state'],'waiting')
        with self.assertRaises(urllib.error.HTTPError) as err:self.request('/api/harness/pi/heartbeat',{},auth=False)
        self.assertEqual(err.exception.code,401)
        self.request('/api/harness/pi/heartbeat',{})
        self.assertEqual(self.request('/health')['harnessConnections']['pi']['state'],'connected')
        now=[0.0];presence=HarnessPresence(clock=lambda:now[0]);presence.heartbeat('pi')
        now[0]=91
        self.assertEqual(presence.snapshot({'pi':True})['pi']['state'],'waiting')
        self.assertEqual(presence.snapshot({'codex':True},True)['codex']['state'],'monitoring')
    def test_received_event_is_not_a_persistent_connection(self):
        self.store.update({'harness':{'claude':True}})
        self.request('/api/harness/claude',{'hook_event_name':'Other','last_assistant_message':'忽略'})
        self.assertEqual(self.request('/health')['harnessConnections']['claude']['state'],'waiting')
        self.request('/api/harness/claude',{'hook_event_name':'Stop','last_assistant_message':'最终回复'})
        state=self.request('/health')['harnessConnections']['claude']
        self.assertEqual(state['state'],'recent_event');self.assertIsNotNone(state['lastReceived'])
        now=[0.0];presence=HarnessPresence(clock=lambda:now[0]);presence.receipt('claude');now[0]=301
        self.assertEqual(presence.snapshot({'claude':True})['claude']['state'],'waiting')
        self.assertEqual(presence.snapshot({'claude':False})['claude']['state'],'disabled')
    def test_five_harness_formats_select_final_text(self):
        events={'codex':{'type':'agent-turn-complete','last-assistant-message':'最终回答'},'claude':{'hook_event_name':'Stop','last_assistant_message':'最终回答'},'deepseek':{'type':'turn/end','text':'最终回答'},'pi':{'type':'agent_end','messages':[{'role':'assistant','content':[{'type':'thinking','text':'不要读'},{'type':'text','text':'最终回答'}]}]},'opencode':{'type':'session.idle','messages':[{'info':{'role':'assistant','id':'a','time':{'completed':1}},'parts':[{'type':'reasoning','text':'不要读'},{'type':'text','text':'最终回答'}]}]}}
        for source,event in events.items():self.assertEqual(normalize(source,event)['text'],'最终回答')
        self.assertIsNone(normalize('claude',{'hook_event_name':'StopFailure','last_assistant_message':'错误'}))
    def test_harness_enabling_and_deduplication(self):
        event={'type':'agent-turn-complete','thread-id':'a','turn-id':'b','last-assistant-message':'喝水吧'}
        first=self.request('/api/harness/codex',event);second=self.request('/api/harness/codex',event)
        self.assertEqual(first['jobId'],second['jobId']);self.assertTrue(second['duplicate']);self.assertEqual(self.wait(first['jobId'])['state'],'completed')
        self.assertTrue(self.request('/api/harness/claude',{'hook_event_name':'Stop','last_assistant_message':'不会念'})['skipped'])
    def test_serial_queue_and_snapshot_isolation(self):
        one=self.request('/speak',{'text':'第一句','play':True})
        self.store.update({'voice':{'pitch':7}})
        two=self.request('/speak',{'text':'第二句','play':True})
        self.wait(one['jobId']);self.wait(two['jobId']);self.assertEqual([c[0] for c in self.actor.calls],['第一句','第二句']);self.assertEqual([c[1]['pitch'] for c in self.actor.calls],[0,7]);self.assertEqual(len(self.actor.plays),2)
    def test_long_spoken_reply_generates_and_plays_progressively(self):
        text='第一句生成完成后就应该开始播放。第二句继续生成，第三句也会完整保留，这样长回复不用全部生成完才出声。'
        item=self.request('/speak',{'text':text,'play':True});result=self.wait(item['jobId'])
        self.assertEqual(result['state'],'completed');self.assertTrue(result['result']['progressive'])
        self.assertGreater(result['result']['segmentCount'],1);self.assertEqual(''.join(call[0] for call in self.actor.calls),text)
        self.assertEqual(len(self.actor.calls),len(self.actor.plays))
    def test_openai_returns_decodable_audio_and_models(self):
        models=self.request('/v1/models');self.assertEqual(models['data'][0]['id'],'agent-voice-tts')
        audio,mime=self.request('/v1/audio/speech',{'model':'agent-voice-tts','input':'喝口水','voice':'current'},raw=True)
        self.assertEqual(mime,'audio/wav')
        with wave.open(io.BytesIO(audio)) as wav:self.assertGreater(wav.getnframes(),0)
        self.assertEqual(len(self.actor.plays),0)
    def test_stop_cancels_pending_and_suppresses_late_playback(self):
        one=self.request('/speak',{'text':'慢一点','play':True});two=self.request('/speak',{'text':'下一句','play':True});self.request('/stop',{})
        self.assertEqual(self.wait(one['jobId'])['state'],'cancelled');self.assertEqual(self.wait(two['jobId'])['state'],'cancelled');self.assertEqual(self.actor.plays,[])
    def test_play_last_reuses_original_audio(self):
        first=self.request('/speak',{'text':'第一份音频','play':False});self.wait(first['jobId']);self.store.update({'voice':{'pitch':6}})
        play=self.request('/api/play',{'jobId':first['jobId']});self.assertEqual(self.wait(play['jobId'])['state'],'completed');self.assertEqual(len(self.actor.calls),1);self.assertEqual(len(self.actor.plays),1)
    def test_cleanup_keeps_active_delivery_and_pair(self):
        self.store.update({'airi':{'enabled':True,'output':'airi'}});self.request('/api/airi/poll',{'client':'desktop'})
        item=self.request('/speak',{'text':'桌宠说话','play':True})
        deadline=time.monotonic()+2;task=None
        while not task and time.monotonic()<deadline:task=self.request('/api/airi/poll',{'client':'desktop'})['task'];time.sleep(.01)
        self.assertIsNotNone(task);self.assertEqual(self.request('/api/cleanup',{'all':True})['removed'],0)
        self.request('/api/airi/ack',{'client':'desktop','id':task['id']});self.assertEqual(self.wait(item['jobId'])['state'],'completed')
        self.assertEqual(self.request('/api/cleanup',{'all':True})['removed'],1)
    def test_airi_lease_excludes_duplicate_desktop_clients(self):
        self.request('/api/airi/poll',{'client':'one'});self.assertTrue(self.request('/api/airi/poll',{'client':'two'})['otherClient'])
    def test_airi_long_reply_receives_ordered_progressive_segments(self):
        self.store.update({'airi':{'enabled':True,'output':'airi'}});self.request('/api/airi/poll',{'client':'desktop'})
        text='桌宠应该先收到第一句并开始播放。第二句在后台继续生成，最后一句仍按原顺序送达。'
        item=self.request('/speak',{'text':text,'play':True});received=[];deadline=time.monotonic()+4
        while time.monotonic()<deadline:
            task=self.request('/api/airi/poll',{'client':'desktop'})['task']
            if task:
                received.append(task['text']);self.request('/api/airi/ack',{'client':'desktop','id':task['id']})
            state=self.request('/api/jobs/'+item['jobId'])
            if state['state'] in ('completed','failed','cancelled'):break
            time.sleep(.01)
        self.assertEqual(state['state'],'completed');self.assertGreater(len(received),1);self.assertEqual(''.join(received),text)
        self.assertEqual(state['result']['output'],'airi');self.assertTrue(state['result']['progressive'])
    def test_audio_path_traversal_rejected(self):
        with self.assertRaises(urllib.error.HTTPError) as err:self.request('/audio/../../data/settings.json')
        self.assertEqual(err.exception.code,400)
    def test_codex_final_only_reader(self):
        captured=[];tail=CodexTail(lambda s,e:captured.append(e),lambda:True,root=self.root)
        path=self.root/'rollout.jsonl'
        tail.consume(path,{'type':'response_item','payload':{'type':'message','role':'assistant','phase':'commentary','content':[{'type':'output_text','text':'进度'}]}})
        tail.consume(path,{'type':'response_item','payload':{'type':'message','role':'assistant','phase':'final_answer','content':[{'type':'output_text','text':'完成啦'}]}})
        tail.consume(path,{'type':'event_msg','payload':{'type':'task_complete','turn_id':'a'}})
        self.assertEqual(len(captured),1);self.assertEqual(captured[0]['last-assistant-message'],'完成啦')
    def test_codex_subagent_replies_are_not_spoken(self):
        captured=[];tail=CodexTail(lambda s,e:captured.append(e),lambda:True,root=self.root);path=self.root/'child.jsonl'
        tail.consume(path,{'type':'session_meta','payload':{'source':{'subagent':{'thread_spawn':{'parent_thread_id':'parent'}}}}})
        tail.consume(path,{'type':'event_msg','payload':{'type':'task_complete','last_agent_message':'内部检查已完成'}})
        self.assertEqual(captured,[])
    def test_appearance_update_does_not_mark_loaded_voice_unready(self):
        self.actor.loaded=True
        self.request('/api/config',{'appearance':{'accent':'#123456'}})
        self.assertTrue(self.actor.loaded)

if __name__=='__main__':unittest.main(verbosity=2)
