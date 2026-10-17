import copy,sys,tempfile,threading,time,unittest
from pathlib import Path
from unittest.mock import Mock,patch
sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'service'))
from pipeline import Pipeline
from state import Store
from jobs import Cancelled
from synthesis_recovery import retryable
import test_core

class SynthesisRecoveryTests(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory();self.addCleanup(self.temp.cleanup)
        self.pipeline=Pipeline(Store(self.temp.name));self.pipeline.prepare=Mock()
        self.adapter=Mock();self.pipeline.adapter=self.adapter
        self.target=Path(self.temp.name)/'result.wav';self.voice=self.pipeline.store.get()['voice']
    def run_audio(self,errors):
        self.calls=[]
        def synth(text,voice,path,check):
            import numpy as np,soundfile as sf
            self.calls.append(copy.deepcopy(voice))
            failure=errors.pop(0)
            if failure:raise failure
            sf.write(path,np.sin(np.arange(2400)*.1)*.1,24000)
        self.adapter.synthesize.side_effect=synth
        return self.pipeline.synthesize('完整句子。',self.voice,self.target,lambda:None)
    def test_transient_retries_same_voice_with_new_seed_and_validates_output(self):
        result=self.run_audio([RuntimeError('The model produced silent audio'),None])
        self.assertEqual(result['attempts'],2);self.assertTrue(self.target.is_file())
        self.assertEqual([v['_retryAttempt'] for v in self.calls],[0,1])
        self.assertTrue(all(v['engine']==self.voice['engine'] and v['reference']==self.voice['reference'] for v in self.calls))
        self.adapter.close.assert_called_once()
    def test_missing_dependency_not_retried(self):
        with self.assertRaisesRegex(RuntimeError,'cmudict'):self.run_audio([RuntimeError("Resource cmudict not found")])
        self.assertEqual(len(self.calls),1);self.assertFalse(self.target.exists())
    def test_retry_exhausted_is_failure(self):
        with self.assertRaisesRegex(RuntimeError,'out of memory'):self.run_audio([RuntimeError('CUDA out of memory') for _ in range(3)])
        self.assertEqual(len(self.calls),3);self.assertFalse(self.target.exists())
    def test_cancel_never_retries(self):
        with self.assertRaises(Cancelled):self.run_audio([Cancelled('已停止')])
        self.assertEqual(len(self.calls),1)
    def test_cancel_during_recovery_prevents_second_attempt(self):
        self.adapter.synthesize.side_effect=RuntimeError('silent audio');calls=[0]
        def check():
            calls[0]+=1
            if calls[0]>=2:raise Cancelled('已停止')
        with self.assertRaises(Cancelled):self.pipeline.synthesize('句子',self.voice,self.target,check)
        self.adapter.close.assert_not_called()
    def test_silence_is_rejected_even_when_worker_claims_success(self):
        import numpy as np,soundfile as sf
        self.adapter.synthesize.side_effect=lambda text,voice,path,check:sf.write(path,np.zeros(2400),24000)
        with self.assertRaisesRegex(RuntimeError,'silent audio'):self.pipeline.synthesize('句子',self.voice,self.target,lambda:None)
        self.assertEqual(self.adapter.synthesize.call_count,3);self.assertFalse(self.target.exists())
    def test_static_configuration_and_permissions_are_not_transient(self):
        for e in [ValueError('bad input'),PermissionError('denied'),RuntimeError('no module named ffmpeg'),RuntimeError('参考录音不存在')]:self.assertFalse(retryable(e))

class EngineSwitchTests(unittest.TestCase):
    def setUp(self):
        self.fixture=test_core.CoreTests('test_authentication_and_unknown_interfaces');self.fixture.setUp();self.addCleanup(self.fixture.tearDown)
    def test_switch_cancels_old_queue_closes_old_model_and_prewarms_selected_voice(self):
        f=self.fixture;entered=threading.Event();release=threading.Event();voices=[]
        def prepare(voice,check):
            entered.set()
            while not release.wait(.01):check()
            check();voices.append(voice['speed']);f.actor.loaded=True
        f.actor.prepare=prepare;f.actor.close=Mock()
        old=[f.request('/speak',{'text':'旧声音排队任务'})['jobId'] for _ in range(4)]
        f.request('/api/config',{'voice':{'speed':1.1}})
        self.assertTrue(entered.wait(2));health=f.request('/health');self.assertFalse(health['ready']);self.assertTrue(health['switching'])
        queued=f.request('/speak',{'text':'切换中排队使用新模型'})['jobId']
        self.assertEqual(f.server.jobs.jobs[queued]['state'],'queued')
        release.set();deadline=time.monotonic()+3
        while f.server.jobs.switching and time.monotonic()<deadline:time.sleep(.01)
        self.assertEqual(voices,[1.1]);f.actor.close.assert_called_once()
        self.assertTrue(all(f.server.jobs.jobs[i]['state']=='cancelled' for i in old))
        self.assertEqual(f.wait(queued)['state'],'completed');self.assertEqual(f.actor.calls[-1][1]['speed'],1.1)
        self.assertTrue(f.request('/health')['ready'])
    def test_failed_activation_reports_error_and_does_not_keep_old_model_ready(self):
        f=self.fixture;f.actor.prepare=Mock(side_effect=RuntimeError('missing runtime resource'))
        f.request('/api/config',{'voice':{'speed':1.1}});deadline=time.monotonic()+2
        while f.server.jobs.switching and time.monotonic()<deadline:time.sleep(.01)
        state=f.request('/health');self.assertFalse(state['ready']);self.assertEqual(state['serviceState'],'error');self.assertIn('新声音加载失败',state['error'])
    def test_rapid_changes_only_finish_latest_selection(self):
        f=self.fixture;entered=threading.Event();release=threading.Event();completed=[]
        def prepare(voice,check):
            entered.set()
            while not release.wait(.01):check()
            check();completed.append(voice['speed']);f.actor.loaded=True
        f.actor.prepare=prepare
        f.request('/api/config',{'voice':{'speed':1.1}});self.assertTrue(entered.wait(2))
        f.request('/api/config',{'voice':{'speed':1.2}});release.set();deadline=time.monotonic()+3
        while f.server.jobs.switching and time.monotonic()<deadline:time.sleep(.01)
        self.assertEqual(completed,[1.2]);self.assertIsNone(f.server.jobs.last_error)

class WorkerIdentityTests(unittest.TestCase):
    def test_worker_model_and_variant_must_match_actual_selection(self):
        from local_tts import LocalTtsClient
        from tts_process import TtsProcess
        with tempfile.TemporaryDirectory() as temp:
            client=LocalTtsClient.__new__(LocalTtsClient);client.model=Path(temp);client.config={'engine':'gpt-sovits','variant':'proplus'};client.close=Mock()
            client.ready_info={'engine':'gpt-sovits','variant':'v5turbo','model':temp}
            with patch.object(TtsProcess,'ensure_ready'),self.assertRaisesRegex(ValueError,'不一致'):client.ensure_ready(lambda:None)
            client.close.assert_called_once()
            client.ready_info['variant']='proplus'
            with patch.object(TtsProcess,'ensure_ready'):client.ensure_ready(lambda:None)
    def test_slow_health_does_not_spawn_another_owned_cosy_worker(self):
        from cosy_client import NaturalTTSClient
        client=NaturalTTSClient.__new__(NaturalTTSClient);client.process=Mock();client.process.poll.return_value=None;client.fingerprint='selected'
        client.health=Mock(side_effect=[None,{'fingerprint':'selected','ready':True,'gpu':'test GPU'}])
        with patch('cosy_client.subprocess.Popen') as popen:client._ensure_ready(lambda:None)
        popen.assert_not_called();self.assertEqual(client.gpu,'test GPU')

class ReferencePreflightTests(unittest.TestCase):
    def test_short_long_silent_or_nonfinite_reference_fails_before_gpu_loading(self):
        import numpy as np,soundfile as sf
        from reference_audio import validate_gpt_reference
        with tempfile.TemporaryDirectory() as temp:
            path=Path(temp)/'ref.wav'
            for seconds in (2,11):
                sf.write(path,np.ones(16000*seconds)*.1,16000)
                with self.assertRaisesRegex(ValueError,'3～10'):validate_gpt_reference(path)
            sf.write(path,np.zeros(16000*4),16000)
            with self.assertRaisesRegex(ValueError,'静音'):validate_gpt_reference(path)
            sf.write(path,np.sin(np.arange(16000*4)*.1)*.1,16000)
            validate_gpt_reference(path)

class DisconnectedReplyTests(unittest.TestCase):
    def test_aborted_client_during_headers_or_body_does_not_retry_a_500_response(self):
        from server import Handler
        for stage in ('end_headers','write'):
            with self.subTest(stage=stage):
                handler=Mock();getattr(handler if stage=='end_headers' else handler.wfile,stage).side_effect=ConnectionAbortedError(10053,'Client disconnected')
                Handler.reply(handler,200,{'ok':True})
                handler.send_response.assert_called_once_with(200)
    def test_unrelated_reply_errors_remain_visible(self):
        from server import Handler
        handler=Mock();handler.send_response.side_effect=ValueError('Invalid response')
        with self.assertRaises(ValueError):Handler.reply(handler,200,{'ok':True})

if __name__=='__main__':unittest.main()
