import socket
import sys
import unittest
from pathlib import Path
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'service'))
from cosy_client import available_worker_port


class WorkerPortTests(unittest.TestCase):
    def test_occupied_internal_port_falls_back_without_touching_occupant(self):
        with socket.socket() as owner:
            owner.bind(('127.0.0.1', 0))
            owner.listen()
            preferred = owner.getsockname()[1]
            selected = available_worker_port(preferred)
            self.assertNotEqual(selected, preferred)
            with socket.socket() as probe:
                probe.bind(('127.0.0.1', selected))
            self.assertEqual(owner.getsockname()[1], preferred)

    def test_windows_permission_denied_uses_os_assigned_port(self):
        with patch('cosy_client.socket.socket') as constructor:
            probe = constructor.return_value.__enter__.return_value
            probe.bind.side_effect = [PermissionError(10013, 'reserved port'), None]
            probe.getsockname.return_value = ('127.0.0.1', 61432)
            self.assertEqual(available_worker_port(8821), 61432)
            self.assertEqual(probe.bind.call_args_list[-1].args[0], ('127.0.0.1', 0))


if __name__ == '__main__':
    unittest.main()
