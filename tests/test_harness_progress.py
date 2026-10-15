import json
from pathlib import Path
import sys
import tempfile
import time
import unittest

sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'service'))
from harness import CodexTail

class HarnessProgressTests(unittest.TestCase):
    def test_new_session_progress_is_read_without_replaying_startup_history(self):
        with tempfile.TemporaryDirectory() as temporary:
            home=Path(temporary);folder=home/'sessions/2026/10/15';folder.mkdir(parents=True)
            progress={'type':'response_item','payload':{'type':'message','role':'assistant','phase':'commentary','content':[{'type':'output_text','text':'正在检查。'}]}}
            old=folder/'old.jsonl';old.write_text(json.dumps(progress)+'\n',encoding='utf8')
            received=[];tail=CodexTail(lambda source,event:received.append(event),lambda:True,root=home);tail.start()
            try:
                deadline=time.monotonic()+3
                while not tail.monitor_ready and time.monotonic()<deadline:time.sleep(.02)
                self.assertTrue(tail.monitor_ready);self.assertEqual(received,[])
                new=folder/'new.jsonl';new.write_text(json.dumps(progress)+'\n',encoding='utf8')
                deadline=time.monotonic()+3
                while not received and time.monotonic()<deadline:time.sleep(.02)
                self.assertEqual(len(received),1);self.assertEqual(received[0]['thread-id'],'new')
                self.assertEqual(received[0]['last-assistant-message'],'正在检查。')
            finally:tail.close()

    def test_internal_records_are_never_read(self):
        events=[];tail=CodexTail(lambda source,event:events.append(event),lambda:True);path=Path('test.jsonl')
        for payload in ({'type':'reasoning','summary':[{'text':'内部推理'}]},
                        {'type':'message','role':'user','phase':'commentary','content':'用户输入'},
                        {'type':'function_call_output','output':'工具结果'}):
            tail.consume(path,{'type':'response_item','payload':payload})
        self.assertEqual(events,[])
