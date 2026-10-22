import json
import os
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch

sys.path[:0] = [str(Path(__file__).resolve().parents[1] / 'service'),
                str(Path(__file__).resolve().parents[1])]
from state import Store
from tools import install_mcp
import harness_setup
import mcp_configuration as config
from mcp_presence import McpPresence, connected_clients


class McpConfigurationTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.base = Path(self.temp.name).resolve()
        self.root = self.base / 'voice'
        self.store = Store(self.root)
        self.store.update({'engines': {'pythonService': sys.executable}})
        self.home = self.base / 'home'
        self.profile = self.home / '.dsh/profiles/desktop'
        self.headless = self.home / '.dsh/profiles/headless'
        for p in (self.profile, self.headless):
            p.mkdir(parents=True)
            (p / 'cordis.patch.yml').write_text('[]', encoding='utf-8')
            (p / 'cordis.yml').write_text('original: true\n', encoding='utf-8')
        self.location = self.base / 'harness'
        self.cli = self.location / 'resources/runtime/cli/bin/dsh.cmd'
        self.cli.parent.mkdir(parents=True)
        self.cli.write_text('fixture')
        self.server = self.root / 'integrations/agent_voice_mcp.py'
        self.server.parent.mkdir()
        self.server.write_text('# fixture')
        (self.server.parent / 'deepseek-control-client.js').write_text('// dsh-agent-voice-public\n', encoding='utf-8')
        (self.server.parent / 'deepseek-plugin.mjs').write_text('export const name="test";', encoding='utf-8')
        self.registry = self.root / 'data/harness-installations.json'
        self.registry.write_text(json.dumps({'deepseek': {'directory': str(self.location),
            'deepseekProfiles': [str(self.profile), str(self.headless)]}}), encoding='utf-8')
        self.entry = config.expected_entry(self.store, Path(sys.executable), self.server)

    def save(self, entry=None):
        config.write_patch(self.profile / 'cordis.patch.yml', [
            {'name': 'unrelated', 'config': {'preserve': True}},
            {'insert': [entry or self.entry]}])

    def install(self, run=None):
        with patch.object(install_mcp, 'run', side_effect=run or (lambda *a, **k: '0.2.0-rc.2')):
            return install_mcp.install_deepseek(self.store, Path(sys.executable), self.server)

    def snapshot(self):
        return {p: p.read_bytes() if p.is_file() else None
                for p in config.managed_files(self.store, self.profile)}

    def assert_restored(self, before):
        self.assertEqual(self.snapshot(), before)

    def test_success_validates_four_paths_and_retains_other_plugins(self):
        self.save()
        result = self.install()
        entry = config.validate_patch(self.profile / 'cordis.patch.yml', self.store, Path(sys.executable), self.server)
        self.assertTrue(entry['config']['failOnStartupError'])
        self.assertTrue(result['requiresHarnessReload'])
        self.assertFalse(result['harnessToolsVerified'])
        self.assertIn({'name': 'unrelated', 'config': {'preserve': True}}, config.read_patch(self.profile / 'cordis.patch.yml'))

    def test_disabled_entry_does_not_pass_configuration_check(self):
        entry=json.loads(json.dumps(self.entry));entry['disabled']=True;self.save(entry)
        with self.assertRaisesRegex(ValueError,'禁用'):
            config.validate_patch(self.profile/'cordis.patch.yml',self.store,Path(sys.executable),self.server)

    def test_passive_repair_preserves_explicitly_disabled_mcp(self):
        entry=json.loads(json.dumps(self.entry));entry['disabled']=True;self.save(entry)
        path=self.profile/'cordis.patch.yml';before=path.read_bytes()
        result=config.check_connection(self.store,self.profile,Path(sys.executable),self.server,repair=True)
        self.assertEqual(result['state'],'disabled');self.assertFalse(result['ok'])
        self.assertEqual(path.read_bytes(),before)

    def test_plugin_add_failure_restores_profiles_wrappers_and_registry(self):
        before = self.snapshot()
        def run(command, **kwargs):
            if '--version' in command:
                return '0.2.0-rc.2'
            (self.profile / 'cordis.yml').write_text('changed: true')
            raise ValueError('模拟 plugin add 异常')
        with self.assertRaisesRegex(ValueError, '已回滚'):
            self.install(run)
        self.assert_restored(before)

    def test_matching_installed_plugin_is_reused_without_network_install(self):
        package = self.profile / 'node_modules/@deepseek-ai/dsh-mcp-client'
        (package / 'lib').mkdir(parents=True)
        (package / 'lib/index.js').write_text('// fixture')
        (package / 'package.json').write_text(json.dumps({'name': '@deepseek-ai/dsh-mcp-client', 'version': '0.2.0-rc.2'}))
        def run(command, **kwargs):
            self.assertIn('--version', command, 'matching plugin must not invoke plugin add')
            return '0.2.0-rc.2'
        self.assertTrue(self.install(run)['ok'])

    def test_frontend_text_verification_accepts_bom_and_windows_newlines(self):
        source = self.server.parent / 'deepseek-control-client.js'
        source.write_bytes(b'\xef\xbb\xbf// dsh-agent-voice-public\r\nconst version=1;\r\n')
        self.assertTrue(self.install()['ok'])
        installed = self.profile / 'node_modules/dsh-agent-voice-public/client.js'
        self.assertEqual(installed.read_text(encoding='utf-8-sig'), source.read_text(encoding='utf-8-sig'))

    def test_configure_partial_failure_also_rolls_back(self):
        before = self.snapshot()
        real = harness_setup.configure
        def broken(*args, **kwargs):
            real(*args, **kwargs)
            raise OSError('模拟复制连接器后失败')
        with patch.object(harness_setup, 'configure', side_effect=broken):
            with self.assertRaisesRegex(ValueError, '已回滚'):
                self.install()
        self.assert_restored(before)

    def test_corrupt_write_is_detected_by_readback_and_rolled_back(self):
        self.save()
        before = self.snapshot()
        write = config.write_patch
        def corrupt(path, entries):
            entries[-1]['insert'][0]['config']['args'][5] = str(self.base / 'deleted')
            write(path, entries)
        with patch.object(config, 'write_patch', side_effect=corrupt):
            with self.assertRaisesRegex(ValueError, 'args'):
                self.install()
        self.assert_restored(before)

    def test_missing_launch_file_rejected_before_side_effects(self):
        before = self.snapshot()
        with patch.object(harness_setup, 'configure') as configure:
            with self.assertRaisesRegex(ValueError, '不存在'):
                install_mcp.install_deepseek(self.store, self.base / 'missing.exe', self.server)
        configure.assert_not_called()
        self.assert_restored(before)

    def test_relocation_repairs_missing_old_root_without_cli_or_voice_changes(self):
        old = self.base / 'deleted-installation'
        moved = json.loads(json.dumps(self.entry))
        moved['config'].update(command=str(old / 'python.exe'), cwd=str(old))
        moved['config']['args'][3] = str(old / 'integrations/agent_voice_mcp.py')
        moved['config']['args'][5] = str(old)
        self.save(moved)
        before = self.store.path.read_bytes()
        with patch.object(install_mcp, 'run') as run:
            result = install_mcp.inspect_deepseek(self.root, repair=True)
        run.assert_not_called()
        self.assertTrue(result['repaired'])
        self.assertEqual(result['state'], 'awaiting-client')
        config.validate_patch(self.profile / 'cordis.patch.yml', self.store, Path(sys.executable), self.server)
        self.assertEqual(self.store.path.read_bytes(), before)

    def test_live_foreign_installation_is_reported_not_stolen(self):
        foreign = self.base / 'another-live-app'
        foreign.mkdir()
        moved = json.loads(json.dumps(self.entry));moved['config']['cwd'] = str(foreign)
        self.save(moved);before = self.snapshot()
        result = install_mcp.inspect_deepseek(self.root, repair=True)
        self.assertEqual(result['state'], 'other-installation')
        self.assert_restored(before)

    def test_uninstalled_mcp_is_not_automatically_installed(self):
        before = self.snapshot()
        result = install_mcp.inspect_deepseek(self.root, repair=True)
        self.assertEqual(result['state'], 'not-installed')
        self.assert_restored(before)

    def test_false_silent_error_flag_is_repaired(self):
        entry = json.loads(json.dumps(self.entry));entry['config']['failOnStartupError'] = False
        self.save(entry)
        result = install_mcp.inspect_deepseek(self.root, repair=True)
        self.assertTrue(result['repaired'])
        config.validate_patch(self.profile / 'cordis.patch.yml', self.store, Path(sys.executable), self.server)

    def test_duplicate_entries_cannot_pass_readback(self):
        config.write_patch(self.profile / 'cordis.patch.yml', [{'insert': [self.entry, self.entry]}])
        with self.assertRaisesRegex(ValueError, '重复'):
            config.validate_patch(self.profile / 'cordis.patch.yml', self.store, Path(sys.executable), self.server)

    def test_invalid_yaml_returns_actionable_error_without_replacing_user_file(self):
        target = self.profile / 'cordis.patch.yml'
        target.write_text('- insert: [broken', encoding='utf-8')
        before = target.read_bytes()
        with self.assertRaisesRegex(ValueError, 'YAML 语法错误'):
            install_mcp.inspect_deepseek(self.root, repair=True)
        self.assertEqual(target.read_bytes(), before)

    def test_profile_lock_rejects_concurrent_modification(self):
        with config.profile_lock(self.profile):
            with self.assertRaisesRegex(ValueError, '正在进行'):
                with config.profile_lock(self.profile):
                    self.fail('concurrent lock acquired')

    def test_receipt_requires_actual_client_identity_live_process_and_matching_root(self):
        self.save()
        presence = McpPresence(self.root, 'deepseek')
        self.addCleanup(presence.close)
        presence.note('initialize', {'clientInfo': {'name': 'probe'}})
        presence.note('tools/list', tool_count=8)
        self.assertEqual(connected_clients(self.root, 'deepseek', 'dsh-mcp-client'), [])
        presence.note('initialize', {'clientInfo': {'name': 'dsh-mcp-client'}})
        self.assertEqual(install_mcp.inspect_deepseek(self.root)['state'], 'tools-read')
        record = json.loads(presence.file.read_text(encoding='utf-8'));record['processStamp'] = 'stale'
        presence.file.write_text(json.dumps(record), encoding='utf-8')
        self.assertEqual(install_mcp.inspect_deepseek(self.root)['state'], 'awaiting-client')
        presence.close()
        self.assertEqual(connected_clients(self.root, 'deepseek', 'dsh-mcp-client'), [])


if __name__ == '__main__':
    unittest.main()
