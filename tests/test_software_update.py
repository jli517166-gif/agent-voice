import hashlib
import json
import sys
import tempfile
from support_temp import TemporaryDirectory
import unittest
import zipfile
from pathlib import Path
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'service'))
import software_update as update


class SoftwareUpdateTests(unittest.TestCase):
    def setUp(self):
        self.temp = TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name) / 'app'
        self.root.mkdir()
        self.current = {'format': 'agent-voice-app', 'version': '0.1.0', 'edition': 'private', 'platform': 'windows-x64'}
        (self.root / 'app-version.json').write_text(json.dumps(self.current))
        (self.root / 'agent语音.exe').write_bytes(b'old')
        (self.root / 'data').mkdir()
        (self.root / 'data/settings.json').write_bytes(b'user-secret')

    def package(self, edition='private', extra=None, digest=None):
        files = {'agent语音.exe': b'new', 'service/edition.py': b'PRIVATE_USE=True\n', '.agents/plugins/voice/agents/openai.yaml': b'display_name: Voice\n'}
        files.update(extra or {})
        version = dict(self.current, version='0.2.0', edition=edition)
        manifest = dict(version, files=[{'path': k, 'size': len(v), 'sha256': digest or hashlib.sha256(v).hexdigest()} for k, v in files.items()])
        path = Path(self.temp.name) / 'update.zip'
        with zipfile.ZipFile(path, 'w') as archive:
            for name, content in files.items():
                archive.writestr('Agent语音/' + name, content)
            archive.writestr('Agent语音/app-version.json', json.dumps(manifest))
        return path

    def test_stage_and_apply_preserves_user_settings_and_has_rollback(self):
        report = update.stage(self.root, self.package())
        self.assertTrue(report['ok'])
        self.assertEqual((self.root / 'agent语音.exe').read_bytes(), b'old')
        result = update.apply(self.root)
        self.assertTrue(result['ok'])
        self.assertEqual((self.root / 'agent语音.exe').read_bytes(), b'new')
        self.assertEqual((self.root / 'data/settings.json').read_bytes(), b'user-secret')
        self.assertEqual((Path(result['backup']) / 'agent语音.exe').read_bytes(), b'old')

    def test_cross_edition_and_personal_paths_are_rejected(self):
        with self.assertRaisesRegex(ValueError, '版本类型'):
            update.stage(self.root, self.package('public'))
        for name in ('data/settings.json', 'runtime/python.exe', 'tts-engines/model.pt', 'modules/extra.exe', '../escape', 'service/evil.pth', 'VOICE.wav'):
            with self.subTest(name=name), self.assertRaises(ValueError):
                update.stage(self.root, self.package(extra={name: b'no'}))

    def test_corruption_does_not_change_installed_files(self):
        with self.assertRaisesRegex(ValueError, '校验'):
            update.stage(self.root, self.package(digest='0' * 64))
        self.assertEqual((self.root / 'agent语音.exe').read_bytes(), b'old')

    def test_apply_rolls_back_on_partial_write_failure(self):
        update.stage(self.root, self.package())
        original = update.replace_file
        def fail_on_manifest(source, destination):
            if destination.name == 'app-version.json':
                raise OSError('simulated locked file')
            return original(source, destination)
        with patch.object(update, 'replace_file', side_effect=fail_on_manifest), self.assertRaises(OSError):
            update.apply(self.root)
        self.assertEqual((self.root / 'agent语音.exe').read_bytes(), b'old')
        self.assertFalse((self.root / 'service/edition.py').exists())

    def test_private_check_is_offline_and_public_api_failure_is_not_latest(self):
        with patch.object(update, 'fetch_json', side_effect=AssertionError('private must not use GitHub')):
            self.assertEqual(update.check(self.root)['state'], 'local-only')
        self.current['edition'] = 'public'
        (self.root / 'app-version.json').write_text(json.dumps(self.current))
        with patch.object(update, 'fetch_json', side_effect=OSError('404 private repository')):
            report = update.check(self.root)
        self.assertFalse(report['ok'])
        self.assertEqual(report['state'], 'unavailable')

    def test_public_updates_require_valid_attachment_and_compare_preview_versions(self):
        self.current.update(edition='public', version='0.1.1-preview.1')
        (self.root / 'app-version.json').write_text(json.dumps(self.current))
        release={'tag_name':'v0.1.1-preview.2','assets':[{'name':'AgentVoice-Windows-x64-v0.1.1-preview.2.zip','digest':'sha256:'+'a'*64,'browser_download_url':'https://github.com/'+update.REPOSITORY+'/releases/download/v0.1.1-preview.2/AgentVoice-Windows-x64-v0.1.1-preview.2.zip'}]}
        with patch.object(update, 'fetch_json', return_value=[release]):
            self.assertEqual(update.check(self.root)['state'],'available')
        release['assets'][0]['digest']=None
        with patch.object(update, 'fetch_json', return_value=[release]):
            self.assertEqual(update.check(self.root)['state'],'manual')
        release['tag_name']='v0.1.1-preview.1'
        with patch.object(update, 'fetch_json', return_value=[release]):
            self.assertEqual(update.check(self.root)['state'],'current')

    def test_public_rejects_mislabeled_private_component(self):
        self.current['edition']='public'
        (self.root / 'app-version.json').write_text(json.dumps(self.current))
        with self.assertRaisesRegex(ValueError,'私用策略'):
            update.stage(self.root,self.package('public'))

    def test_public_api_rate_limit_falls_back_to_official_feed(self):
        self.current['edition']='public'
        (self.root / 'app-version.json').write_text(json.dumps(self.current))
        feed={'format':'agent-voice-release','edition':'public','platform':'windows-x64','version':'v0.1.1-preview.2','sha256':'a'*64,'downloadUrl':'https://github.com/'+update.REPOSITORY+'/releases/download/v0.1.1-preview.2/AgentVoice-Windows-x64-v0.1.1-preview.2.zip'}
        with patch.object(update,'fetch_json',side_effect=[OSError('403 rate limit exceeded'),feed]):
            result=update.check(self.root)
        self.assertEqual(result['state'],'available')
        self.assertEqual(result['channel'],'official-static-feed')
        feed['downloadUrl']='https://untrusted.invalid/program.zip'
        with patch.object(update,'fetch_json',side_effect=[OSError('403 rate limit exceeded'),feed]):
            self.assertEqual(update.check(self.root)['state'],'unavailable')

    def test_unmanifested_payload_and_duplicate_paths_are_rejected(self):
        path = self.package()
        with zipfile.ZipFile(path, 'a') as archive:
            archive.writestr('Agent语音/service/extra.py', b'bad')
        with self.assertRaises(ValueError):
            update.stage(self.root, path)

    def test_packaged_manifest_describes_actual_normalized_manual_bytes(self):
        sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'tools'))
        from module_packages import application_package
        build = Path(self.temp.name) / 'build'
        build.mkdir()
        for folder in ('service', 'tools', 'integrations', 'docs', '.agents'):
            (build / folder).mkdir()
        (build / 'agent语音.exe').write_bytes(b'new')
        (build / 'manual.md').write_text('[omitted source](tests/not-shipped.py)', encoding='utf-8')
        update.write_manifest(build, 'private', '0.2.0')
        output = Path(self.temp.name) / 'app.zip'
        application_package(build, output)
        self.assertTrue(update.stage(self.root, output)['ok'])

    def test_changed_stage_is_rejected_before_any_installation_write(self):
        update.stage(self.root, self.package())
        staged = next((self.root / 'data/updates').glob('stage-*'))
        (staged / 'agent语音.exe').write_bytes(b'tampered')
        with self.assertRaisesRegex(ValueError, '校验'):
            update.apply(self.root)
        self.assertEqual((self.root / 'agent语音.exe').read_bytes(), b'old')

    def test_interrupted_application_is_recovered_on_next_start(self):
        update.stage(self.root, self.package())
        original = update.replace_file
        def simulate_power_loss(source, destination):
            if destination.name == 'app-version.json':
                raise KeyboardInterrupt('process terminated')
            return original(source, destination)
        with patch.object(update, 'replace_file', side_effect=simulate_power_loss), self.assertRaises(KeyboardInterrupt):
            update.apply(self.root)
        self.assertTrue((self.root / 'data/updates/installing.json').exists())
        self.assertTrue(update.recover(self.root)['ok'])
        self.assertEqual((self.root / 'agent语音.exe').read_bytes(), b'old')
        self.assertFalse((self.root / 'service/edition.py').exists())
