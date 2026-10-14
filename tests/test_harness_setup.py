import json,os,sys,tempfile,unittest
from pathlib import Path
from unittest.mock import patch
sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'service'))
from state import Store
from harness_setup import configure,detect

class SetupTests(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory();self.base=Path(self.temp.name);self.root=self.base/'voice';Store(self.root)
        self.home=self.base/'home';self.home.mkdir();self.install=self.base/'installed';self.install.mkdir()
    def tearDown(self):self.temp.cleanup()
    def test_claude_global_hook_preserves_existing_model_and_unrelated_hooks(self):
        path=self.home/'.claude/settings.json';path.parent.mkdir();path.write_text(json.dumps({'model':'existing','hooks':{'Stop':[{'hooks':[{'type':'command','command':'existing-hook'}]}]}}))
        with patch.dict(os.environ,{'CLAUDE_CONFIG_DIR':str(path.parent)}):
            configure('claude',self.root,self.install,self.home);result=configure('claude',self.root,self.install,self.home)
        cfg=json.loads(path.read_text());self.assertEqual(cfg['model'],'existing');self.assertEqual(len(cfg['hooks']['Stop']),2)
        self.assertFalse(result['verified']);self.assertTrue(Path(result['backup']).is_dir())
    def test_pi_and_opencode_install_global_wrappers_not_install_project(self):
        with patch.dict(os.environ,{'PI_CODING_AGENT_DIR':str(self.home/'.pi/agent'),'XDG_CONFIG_HOME':str(self.home/'.config')}):
            configure('pi',self.root,self.install,self.home);configure('opencode',self.root,self.install,self.home)
        self.assertTrue((self.home/'.pi/agent/extensions/agent-voice.ts').is_file())
        self.assertTrue((self.home/'.config/opencode/plugins/agent-voice.js').is_file())
        self.assertFalse((self.install/'.opencode').exists())
    def test_codex_records_actual_sessions_directory(self):
        (self.install/'sessions').mkdir();result=configure('codex',self.root,self.install,self.home)
        record=json.loads((self.root/'data/harness-installations.json').read_text())['codex']
        self.assertEqual(record['codexHome'],str(self.install.resolve()));self.assertFalse(result['verified'])
    def test_deepseek_installs_and_enables_once_preserving_original_entries(self):
        import yaml
        profile=self.home/'.dsh/profiles/desktop';profile.mkdir(parents=True)
        headless=self.home/'.dsh/profiles/headless';headless.mkdir(parents=True);(headless/'cordis.patch.yml').write_text('[]')
        path=profile/'cordis.patch.yml';path.write_text(yaml.safe_dump([{'id':'existing','name':'existing-plugin','config':{'model':'keep'}},{'id':'agent-default-model','name':'@deepseek-ai/dsh-agent-default-model','config':{'provider':'deepseek-account','model':'deepseek-flash'}}]))
        configure('deepseek',self.root,self.install,self.home);configure('deepseek',self.root,self.install,self.home)
        entries=yaml.safe_load(path.read_text());self.assertEqual(entries[0]['config']['model'],'keep')
        inserted=[p for e in entries for p in e.get('insert',[]) if p['name']=='dsh-agent-voice-public']
        self.assertEqual(len(inserted),1);self.assertTrue((profile/'node_modules/dsh-agent-voice-public/index.mjs').is_file())
        self.assertTrue((headless/'node_modules/dsh-agent-voice-public/index.mjs').is_file())
        self.assertEqual(len([p for e in yaml.safe_load((headless/'cordis.patch.yml').read_text()) for p in e.get('insert',[]) if p['name']=='dsh-agent-voice-public']),1)
        headless_entries=yaml.safe_load((headless/'cordis.patch.yml').read_text());self.assertEqual(next(e for e in headless_entries if e.get('id')=='agent-default-model')['config']['provider'],'deepseek-account')
    def test_unknown_or_missing_location_is_not_success(self):
        with self.assertRaises(ValueError):configure('unknown',self.root,self.install,self.home)
        with self.assertRaises(ValueError):configure('pi',self.root,self.base/'missing',self.home)
    def test_deepseek_handoff_disables_only_known_old_voice(self):
        import yaml
        profile=self.home/'.dsh/profiles/desktop';profile.mkdir(parents=True);path=profile/'cordis.patch.yml'
        path.write_text(yaml.safe_dump([{'insert':[{'id':'old','name':'dsh-tts-autoread','config':{'endpoint':'http://127.0.0.1:8799/speak'}},{'id':'other','name':'other-plugin','config':{'enabled':True}}]}]))
        result=configure('deepseek',self.root,self.install,self.home);entries=yaml.safe_load(path.read_text())
        old=entries[0]['insert'][0];other=entries[0]['insert'][1]
        self.assertFalse(old['config']['enabled']);self.assertFalse(old['config']['autostart']);self.assertTrue(other['config']['enabled']);self.assertTrue(result['disabledLegacyVoice'])
    def test_detects_every_supported_harness_from_strong_signature(self):
        fixtures={
            'codex':lambda root:(root/'sessions').mkdir(),
            'deepseek':lambda root:(root/'cordis.yml').write_text('plugins: []'),
            'claude':lambda root:(root/'claude.exe').write_bytes(b''),
            'pi':lambda root:(root/'pi.cmd').write_text(''),
            'opencode':lambda root:(root/'opencode.exe').write_bytes(b'')}
        for expected,build in fixtures.items():
            folder=self.base/('detect-'+expected);folder.mkdir();build(folder)
            self.assertEqual(detect(folder,self.home)['source'],expected)
    def test_auto_configure_reports_detected_source_and_evidence(self):
        (self.install/'sessions').mkdir()
        result=configure('auto',self.root,self.install,self.home)
        self.assertEqual(result['detectedSource'],'codex');self.assertTrue(result['detectionEvidence'])
    def test_deepseek_install_directory_detects_then_uses_user_profile(self):
        import yaml
        (self.install/'DeepSeek Harness.exe').write_bytes(b'')
        cli=self.install/'resources/runtime/cli/bin';cli.mkdir(parents=True);(cli/'dsh.cmd').write_text('@echo off')
        profile=self.home/'.dsh/profiles/desktop';profile.mkdir(parents=True);(profile/'cordis.patch.yml').write_text('[]')
        result=configure('auto',self.root,self.install,self.home)
        self.assertEqual(result['detectedSource'],'deepseek')
        self.assertIn('DeepSeek Harness.exe',result['detectionEvidence'][0])
        entries=yaml.safe_load((profile/'cordis.patch.yml').read_text())
        self.assertTrue(any(p.get('name')=='dsh-agent-voice-public' for entry in entries for p in entry.get('insert',[])))
    def test_detection_rejects_unknown_and_ambiguous_directories(self):
        unknown=self.base/'unknown';unknown.mkdir()
        with self.assertRaisesRegex(ValueError,'未识别'):detect(unknown,self.home)
        mixed=self.base/'mixed';mixed.mkdir();(mixed/'sessions').mkdir();(mixed/'claude.exe').write_bytes(b'')
        with self.assertRaisesRegex(ValueError,'同时匹配'):detect(mixed,self.home)

if __name__=='__main__':unittest.main()
