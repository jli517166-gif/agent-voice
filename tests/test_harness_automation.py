import sys,tempfile,unittest
from pathlib import Path
from unittest.mock import patch
sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'tools'))
from verify_harness import command_for,candidates,probe,is_account_error,manual_challenge,manual_result

class HarnessAutomationTests(unittest.TestCase):
    def test_probe_accepts_powershell_shim(self):
        with tempfile.TemporaryDirectory() as temporary:
            root=Path(temporary).resolve();shim=root/'codex.ps1';shim.write_text('exit 0',encoding='utf8')
            result=probe('codex',root)
            self.assertEqual(result['stage'],'cli')
            self.assertEqual(Path(result['executable']),shim)

    def test_commands_use_real_noninteractive_turns(self):
        root=Path(r'C:\Harness');exe=root/'tool.exe';prompt='只回复验证短句'
        self.assertEqual(command_for('codex',exe,prompt,root)[1:5],['exec','--skip-git-repo-check','-C',str(root)])
        self.assertEqual(command_for('deepseek',exe,prompt,root)[1:],['headless','-'])
    def test_selected_directory_executable_has_priority(self):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp).resolve();(root/'codex.exe').write_bytes(b'fixture')
            self.assertEqual(next(candidates('codex',root)),root/'codex.exe')

    def test_account_error_becomes_manual_step_instead_of_failure(self):
        detail='dsh: ACCOUNT_SIGN_IN_REQUIRED: Sign in to DeepSeek to use the account provider.'
        self.assertTrue(is_account_error(detail))
        self.assertTrue(is_account_error('MISSING_CREDENTIAL: no API key'))
        self.assertFalse(is_account_error('network timeout'))
        prompt='Reply with exactly this line and nothing else:\nagent-voice-verify-abc123'
        self.assertEqual(manual_challenge(prompt),'agent-voice-verify-abc123')
        with patch.dict('os.environ',{'AGENT_VOICE_NO_UI':'1'}):
            result=manual_result('deepseek',Path('dsh.cmd'),prompt,detail)
        self.assertTrue(result['manualRequired'])
        self.assertEqual(result['challenge'],'agent-voice-verify-abc123')
        self.assertIn('桌面',result['message'])

if __name__=='__main__':unittest.main()
