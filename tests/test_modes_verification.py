import copy
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import Mock
sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'service'))
from pipeline import effective_voice
from state import defaults,Store,validate
from verification import Verification
import test_core

class ModeTests(unittest.TestCase):



    def test_verification_never_accepts_heartbeat_wrong_source_failed_or_local_airi(self):
        jobs=Mock();clock=[0.0];checker=Verification(jobs,lambda:clock[0]);one=checker.start('deepseek')
        self.assertRegex(one['text'],r'^agent-voice-verify-[0-9a-f]{6}$')
        self.assertTrue(one['prompt'].endswith(one['text']))
        checker.receipt('codex',one['text'],{'jobId':'wrong'})
        checker.receipt('deepseek','别的正文',{'jobId':'wrong'})
        self.assertEqual(checker.snapshot('deepseek')['state'],'waiting')
        checker.receipt('deepseek',one['text'],{'jobId':'correct'})
        jobs.get.return_value={'state':'completed','result':{'played':True,'output':'local'}}
        self.assertEqual(checker.snapshot('deepseek')['state'],'verified')
        airi=checker.start('airi');checker.receipt('airi',airi['text'],{'jobId':'airi'})
        self.assertTrue(airi['text'].startswith('语音接入验证 '))
        self.assertEqual(checker.snapshot('airi')['state'],'failed')
        clock[0]=901;self.assertEqual(checker.snapshot('deepseek')['state'],'expired')

class VerificationApiTests(unittest.TestCase):
    setUp=test_core.CoreTests.setUp
    tearDown=test_core.CoreTests.tearDown
    request=test_core.CoreTests.request
    wait=test_core.CoreTests.wait
    # Inherit fixture and protocol checks; additional checks exercise live HTTP.
    def test_verify_requires_source_reply_and_completed_playback(self):
        self.store.update({'harness':{'deepseek':True}})
        challenge=self.request('/api/verify/deepseek',{})
        self.request('/api/harness/deepseek/heartbeat',{})
        self.assertEqual(self.request('/api/verify/deepseek')['state'],'waiting')
        response=self.request('/api/harness/deepseek',{'type':'turn/end','text':challenge['text'],'session_id':'verify'})
        self.wait(response['jobId'])
        self.assertEqual(self.request('/api/verify/deepseek')['state'],'verified')

    def test_unconfigured_challenge_reports_failure_instead_of_waiting_forever(self):
        self.store.update({'harness':{'codex':True}})
        self.store.update({'voice':{'reference':'missing.wav'}})
        challenge=self.request('/api/verify/codex',{})
        self.request('/api/harness/codex',{'type':'agent-turn-complete','last-assistant-message':challenge['text']})
        state=self.request('/api/verify/codex')
        self.assertEqual(state['state'],'failed');self.assertIn('参考录音',state['error'])

    def test_concurrent_health_and_verification_reads_do_not_deadlock(self):
        self.store.update({'harness':{'codex':True}})
        from concurrent.futures import ThreadPoolExecutor
        challenge=self.request('/api/verify/codex',{})
        result=self.request('/api/harness/codex',{'type':'agent-turn-complete','last-assistant-message':challenge['text']})
        with ThreadPoolExecutor(max_workers=8) as pool:
            states=list(pool.map(lambda i:self.request('/health' if i%2 else '/api/verify/codex'),range(32)))
        self.assertEqual(len(states),32);self.wait(result['jobId'])

    def test_airi_disabled_or_absent_plays_locally(self):
        for enabled in (False,True):
            self.store.update({'airi':{'enabled':enabled,'output':'airi'}})
            result=self.request('/speak',{'text':'本机仍能播放','play':True})
            job=self.wait(result['jobId'])
            self.assertEqual(job['state'],'completed');self.assertEqual(job['result']['output'],'local')

    def test_airi_verification_requires_native_ack_and_target_output(self):
        self.store.update({'airi':{'enabled':True,'output':'local'}})
        self.request('/api/airi/poll',{'client':'desktop'})
        challenge=self.request('/api/verify/airi',{})
        import time
        task=None
        for _ in range(100):
            task=self.request('/api/airi/poll',{'client':'desktop'})['task']
            if task:break
            time.sleep(.01)
        self.assertIsNotNone(task)
        self.assertEqual(self.request('/api/verify/airi')['state'],'playing')
        self.request('/api/airi/ack',{'client':'desktop','id':task['id']})
        self.wait(challenge['jobId'])
        self.assertEqual(self.request('/api/verify/airi')['state'],'verified')

if __name__=='__main__':unittest.main()
