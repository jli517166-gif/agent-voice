import importlib.util
from pathlib import Path
import tempfile
import unittest
import zipfile

spec = importlib.util.spec_from_file_location('package_setup', Path(__file__).parents[1] / 'tools/module_packages.py')
packages = importlib.util.module_from_spec(spec)
spec.loader.exec_module(packages)

class EmbeddedPackageTests(unittest.TestCase):
    def test_setup_assets_and_sdk_survive_application_export(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder) / 'build'
            for name in ['service', 'tools', 'integrations', 'docs', '.agents']:
                (root / name).mkdir(parents=True, exist_ok=True)
            required = ['agent语音.exe', 'Microsoft.Web.WebView2.Core.dll',
                        'Microsoft.Web.WebView2.WinForms.dll', 'WebView2Loader.dll',
                        'assets/setup/index.html', 'assets/setup/setup.css',
                        'assets/setup/setup.js', 'assets/setup/bridge.js']
            for name in required + ['data/settings.json', 'voices/private.wav', 'tools/unrelated.exe']:
                target = root / name
                target.parent.mkdir(parents=True, exist_ok=True)
                target.write_bytes(b'fixture')
            output = Path(folder) / 'app.zip'
            packages.application_package(root, output)
            with zipfile.ZipFile(output) as archive:
                names = set(archive.namelist())
                for name in required:
                    self.assertIn('Agent语音/' + name, names)
                self.assertNotIn('Agent语音/data/settings.json', names)
                self.assertNotIn('Agent语音/voices/private.wav', names)
                self.assertNotIn('Agent语音/tools/unrelated.exe', names)

if __name__ == '__main__':
    unittest.main()
