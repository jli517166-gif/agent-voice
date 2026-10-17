import json,sys,tempfile,unittest,zipfile
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'tools'))
from package_portable import archive,files,release_files

class PortablePackageTests(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory();self.parent=Path(self.temp.name).resolve();self.root=self.parent/'app';self.root.mkdir()
    def tearDown(self):self.temp.cleanup()
    def put(self,name,content=b'fixture'):
        path=self.root/name;path.parent.mkdir(parents=True,exist_ok=True);path.write_bytes(content);return path
    def test_clean_archive_ignores_validation_state_and_caches(self):
        for name in ('agent语音.exe','runtime/core/python.exe','tts-engines/cosyvoice/runtime/tools/python.exe','tts-engines/gpt-sovits/runtime/Scripts/python.exe'):self.put(name)
        self.put('assets/bundled-voice.json',b'{}');self.put('release-info.json',b'{"commit":"fixture"}')
        self.put('data/settings.json',b'{"token":"never-publish"}');self.put('data/secret.txt');self.put('acceptance/session.json');self.put('service/__pycache__/fixture.pyc');self.put('runtime/core/Lib/library/data/needed.txt')
        output=self.parent/'release.zip';result=archive(self.root,output,True)
        self.assertTrue(result['packageReady']);self.assertEqual(len(result['archiveSha256']),64)
        with zipfile.ZipFile(output) as z:
            names=z.namelist();self.assertFalse(any(n.startswith('Agent语音/data/') or '/__pycache__/' in n or '/acceptance/' in n for n in names));self.assertIn('Agent语音/runtime/core/Lib/library/data/needed.txt',names)
    def test_unaccepted_or_existing_output_is_rejected(self):
        target=self.parent/'release.zip'
        with self.assertRaisesRegex(ValueError,'确认'):archive(self.root,target,False)
        target.write_bytes(b'keep')
        with self.assertRaisesRegex(ValueError,'存在'):archive(self.root,target,True)
        self.assertEqual(target.read_bytes(),b'keep')
    def test_unknown_directory_or_excluded_voice_fails_closed(self):
        self.put('private/reference.wav')
        with self.assertRaisesRegex(ValueError,'未批准'):list(release_files(self.root))
        self.root.joinpath('private/reference.wav').unlink();self.root.joinpath('private').rmdir()
        self.put('voice-model-packages/小挽昼/model.bin')
        with self.assertRaisesRegex(ValueError,'排除'):list(release_files(self.root))
    def test_link_is_not_followed_to_other_files(self):
        if sys.platform=='win32':
            import os,subprocess
            outside=self.parent/'other';outside.mkdir();(outside/'secret.txt').write_text('keep')
            environment=dict(os.environ,AGENT_PACKAGE_LINK=str(self.root/'link'),AGENT_PACKAGE_TARGET=str(outside))
            result=subprocess.run(['powershell','-NoProfile','-Command','New-Item -ItemType Junction -Path $env:AGENT_PACKAGE_LINK -Target $env:AGENT_PACKAGE_TARGET -ErrorAction Stop | Out-Null'],capture_output=True,env=environment)
            if result.returncode:self.skipTest('junction creation unavailable')
        else:
            outside=self.parent/'other';outside.mkdir();(self.root/'link').symlink_to(outside,target_is_directory=True)
        with self.assertRaisesRegex(ValueError,'链接'):list(files(self.root))

if __name__=='__main__':unittest.main()
