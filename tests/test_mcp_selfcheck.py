import copy
import json
import shutil
import socket
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

REPO = Path(__file__).resolve().parents[1]
sys.path[:0] = [str(REPO / 'service'), str(REPO)]
from state import Store, atomic_json
import mcp_selfcheck
from mcp_presence import McpPresence, connected_clients
from tools import install_mcp


class McpSelfcheckTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.store = Store(Path(self.temp.name) / 'voice')
        with socket.socket() as listener:
            listener.bind(('127.0.0.1', 0))
            port = listener.getsockname()[1]
        cfg=self.store.get();cfg['port']=port;cfg['engines']['pythonService']=sys.executable
        atomic_json(self.store.path,cfg)
        self.store=Store(self.store.root)
        self.python = Path(sys.executable).resolve()
        self.server = self.store.root / 'integrations/agent_voice_mcp.py'
        self.server.parent.mkdir()
        shutil.copyfile(REPO / 'integrations/agent_voice_mcp.py', self.server)
        shutil.copytree(REPO / 'service', self.store.root / 'service', ignore=shutil.ignore_patterns('__pycache__'))

    def codex(self):
        return {'enabled': True, 'transport': {'type': 'stdio', 'command': str(self.python),
            'args': ['-I', '-X', 'utf8', str(self.server), '--root', str(self.store.root), '--host', 'codex']}}

    def test_real_protocol_without_tts_or_voice_leaves_settings_unchanged(self):
        before = self.store.path.read_bytes()
        for host in ('codex', 'deepseek'):
            result = mcp_selfcheck.probe(self.store, host, self.python, self.server)
            self.assertTrue(result['protocolPassed'])
            self.assertEqual(result['toolCount'], 8)
            self.assertFalse(result['audioTested'])
            self.assertFalse(connected_clients(self.store.root, host, None))
        self.assertEqual(self.store.path.read_bytes(), before)
        self.assertFalse((self.store.root / 'data/service.pid').exists())
        self.assertFalse(list(self.store.root.rglob('*.wav')))

    def test_probe_sends_only_initialize_list_and_read_only_status(self):
        with patch.object(mcp_selfcheck.subprocess, 'run', return_value=subprocess.CompletedProcess([], 0, '', '')) as run:
            with self.assertRaises(ValueError):
                mcp_selfcheck.probe(self.store, 'codex', self.python, self.server)
        messages = [json.loads(line) for line in run.call_args.kwargs['input'].splitlines()]
        self.assertEqual([m['params']['name'] for m in messages if m['method'] == 'tools/call'], ['voice_service_status'])
        self.assertNotIn('read_text', run.call_args.kwargs['input'])
        self.assertEqual(run.call_args.kwargs['timeout'], 20)

    def test_codex_checks_enable_paths_and_tool_filters(self):
        valid = self.codex()
        self.assertEqual(mcp_selfcheck.codex_configuration(self.store, self.python, self.server, 'codex', lambda _: json.dumps(valid)), valid)
        cases = []
        disabled = copy.deepcopy(valid);disabled['enabled'] = False;cases.append(disabled)
        stale = copy.deepcopy(valid);stale['transport']['args'][5] = 'old-root';cases.append(stale)
        filtered = copy.deepcopy(valid);filtered['disabled_tools'] = ['read_text'];cases.append(filtered)
        limited = copy.deepcopy(valid);limited['enabled_tools'] = ['voice_service_status'];cases.append(limited)
        for case in cases:
            with self.subTest(case=case), self.assertRaises(ValueError):
                mcp_selfcheck.codex_configuration(self.store, self.python, self.server, 'codex', lambda _: json.dumps(case))

    def test_self_probe_does_not_count_as_host_receipt(self):
        receipt = McpPresence(self.store.root, 'codex')
        self.addCleanup(receipt.close)
        receipt.note('initialize', {'clientInfo': {'name': 'agent-voice-selfcheck'}})
        receipt.note('tools/list', tool_count=8)
        self.assertFalse(connected_clients(self.store.root, 'codex', None))

    def test_pending_host_never_passes_even_with_valid_probe(self):
        with patch.object(install_mcp, 'find_codex', return_value='codex'), patch.object(mcp_selfcheck, 'codex_configuration'), \
             patch('mcp_presence.connected_clients', return_value=[]), patch.object(mcp_selfcheck, 'probe', return_value={'protocolPassed': True}):
            result = install_mcp.inspect('codex', self.store.root, self_test=True)
        self.assertTrue(result['enabled'])
        self.assertFalse(result['passed'])
        self.assertEqual(result['state'], 'awaiting-client')

    def test_loaded_host_passes_after_protocol_check(self):
        with patch.object(install_mcp, 'find_codex', return_value='codex'), patch.object(mcp_selfcheck, 'codex_configuration'), \
             patch('mcp_presence.connected_clients', return_value=[{'toolCount': 8}]), patch.object(mcp_selfcheck, 'probe', return_value={'protocolPassed': True}):
            result = install_mcp.inspect('codex', self.store.root, self_test=True)
        self.assertTrue(result['passed'])
        self.assertFalse(result['audioTested'])

    def test_loaded_host_reports_connected_on_normal_refresh(self):
        with patch.object(install_mcp, 'find_codex', return_value='codex'), patch.object(mcp_selfcheck, 'codex_configuration'), \
             patch('mcp_presence.connected_clients', return_value=[{'toolCount': 8}]):
            result = install_mcp.inspect('codex', self.store.root)
        self.assertEqual(result['state'], 'tools-read')
        self.assertTrue(result['clientLoaded'])
        self.assertTrue(result['installationPassed'])
        self.assertNotIn('protocolPassed', result)

    def test_waiting_for_client_is_pending_not_installation_error(self):
        with patch.object(install_mcp, 'find_codex', return_value='codex'), patch.object(mcp_selfcheck, 'codex_configuration'), \
             patch('mcp_presence.connected_clients', return_value=[]):
            result = install_mcp.inspect('codex', self.store.root)
        self.assertTrue(result['ok'])
        self.assertTrue(result['installationPassed'])
        self.assertEqual(result['state'], 'awaiting-client')
        self.assertFalse(result['clientLoaded'])


if __name__ == '__main__':
    unittest.main()
