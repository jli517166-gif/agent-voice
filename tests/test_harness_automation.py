import sys,tempfile,unittest
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'tools'))
from verify_harness import command_for,candidates

class HarnessAutomationTests(unittest.TestCase):
    def test_commands_use_real_noninteractive_turns(self):
        root=Path(r'C:\Harness');exe=root/'tool.exe';prompt='只回复验证短句'
        self.assertEqual(command_for('codex',exe,prompt,root)[1:5],['exec','--skip-git-repo-check','-C',str(root)])
        self.assertEqual(command_for('claude',exe,prompt,root)[1:],['-p',prompt])
        self.assertEqual(command_for('pi',exe,prompt,root)[1:],['-p',prompt])
        self.assertEqual(command_for('opencode',exe,prompt,root)[1:],['run',prompt])
        self.assertEqual(command_for('deepseek',exe,prompt,root)[1:],['headless','-'])
    def test_selected_directory_executable_has_priority(self):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp);(root/'codex.exe').write_bytes(b'fixture')
            self.assertEqual(next(candidates('codex',root)),root/'codex.exe')

if __name__=='__main__':unittest.main()
