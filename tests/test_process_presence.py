import subprocess
import sys
import time
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'service'))
from mcp_presence import process_stamp


class ProcessPresenceTests(unittest.TestCase):
    @unittest.skipUnless(sys.platform == 'win32', 'Windows process handle behavior')
    def test_terminated_process_is_not_reported_live_while_object_is_retained(self):
        process = subprocess.Popen([sys.executable, '-c', 'pass'])
        stamp = process_stamp(process.pid)
        self.assertTrue(stamp)
        process.wait(timeout=10)
        deadline = time.monotonic() + 2
        while time.monotonic() < deadline and process_stamp(process.pid) is not None:
            time.sleep(.05)
        self.assertIsNone(process_stamp(process.pid))


if __name__ == '__main__':
    unittest.main()
