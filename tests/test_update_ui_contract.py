from pathlib import Path
import unittest


class UpdateUiContractTests(unittest.TestCase):
    def test_installer_handoff_terminates_gui_process_immediately(self):
        source = (Path(__file__).resolve().parents[1] / 'ui' / 'SoftwareUpdatePage.cs').read_text(encoding='utf-8-sig')
        handoff = source.split('Process.Start(info);', 1)[1]
        self.assertIn('Environment.Exit(0);', handoff)

    def test_installer_uses_the_verified_staged_updater(self):
        source = (Path(__file__).resolve().parents[1] / 'ui' / 'SoftwareUpdatePage.cs').read_text(encoding='utf-8-sig')
        self.assertIn('Json.Text(pending,"stage")', source)
        self.assertIn('Path.Combine(updates,stage,"tools","software_update.py")', source)


if __name__ == '__main__':
    unittest.main()
