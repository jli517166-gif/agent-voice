import hashlib
import http.server
import io
import json
from pathlib import Path
import socketserver
import sys
import tempfile
import threading
import unittest
import zipfile
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'service'))
from environment_repair import extract


class SourceArchiveTests(unittest.TestCase):
    def archive(self, root, entries):
        path = root / 'source.zip'
        with zipfile.ZipFile(path, 'w') as archive:
            for name, value, linked in entries:
                info = zipfile.ZipInfo(name)
                info.create_system = 3
                info.external_attr = (0o120777 if linked else 0o100644) << 16
                archive.writestr(info, value)
        return path

    def test_internal_file_and_directory_links_are_materialized(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            archive = self.archive(root, [('repo/common/a.py', b'value', False),
                                         ('repo/example/local', b'../common', True),
                                         ('repo/example/file.py', b'../common/a.py', True)])
            extract(archive, root / 'out', True)
            self.assertEqual((root / 'out/example/local/a.py').read_bytes(), b'value')
            self.assertEqual((root / 'out/example/file.py').read_bytes(), b'value')
            self.assertFalse((root / 'out/example/local').is_symlink())

    def test_outside_missing_and_cyclic_links_remain_rejected(self):
        for target in ('../../outside', '/etc/passwd', 'missing', 'loop'):
            with self.subTest(target=target), tempfile.TemporaryDirectory() as tmp:
                root = Path(tmp)
                archive = self.archive(root, [('repo/loop', target.encode(), True)])
                with self.assertRaises(ValueError):
                    extract(archive, root / 'out', True)

    def test_directory_links_cannot_recursively_include_themselves(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            archive = self.archive(root, [('repo/a/file', b'valid', False), ('repo/a/recursive', b'.', True)])
            with self.assertRaises(ValueError):
                extract(archive, root / 'out', True)

    def test_duplicate_and_parent_traversal_entries_are_rejected(self):
        for entries in [[('repo/a', b'1', False), ('repo/A', b'2', False)], [('repo/../outside', b'no', False)]]:
            with tempfile.TemporaryDirectory() as tmp:
                root = Path(tmp)
                with self.assertRaises(ValueError):
                    extract(self.archive(root, entries), root / 'out', True)


class DownloadTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.payload = (b'verified-model-or-wheel-' * 10000)
        cls.requests = []
        cls.drop_once = False
        class Handler(http.server.BaseHTTPRequestHandler):
            def log_message(self, *args):
                pass
            def do_GET(self):
                value = cls.payload
                request = self.headers.get('Range', '')
                cls.requests.append(request)
                if self.path == '/ignore-range':
                    self.send_response(200); self.send_header('Content-Length', str(len(value))); self.end_headers(); self.wfile.write(value); return
                start, end = map(int, request[6:].split('-'))
                end = min(end, len(value)-1)
                self.send_response(206)
                self.send_header('Content-Range', f'bytes {start}-{end}/{len(value)}')
                self.send_header('Content-Length', str(end-start+1))
                self.end_headers()
                if cls.drop_once:
                    cls.drop_once = False
                    self.wfile.write(value[start:start+1024]); self.close_connection = True; return
                self.wfile.write(value[start:end+1])
        cls.http = socketserver.TCPServer(('127.0.0.1', 0), Handler)
        cls.thread = threading.Thread(target=cls.http.serve_forever, daemon=True)
        cls.thread.start()

    @classmethod
    def tearDownClass(cls):
        cls.http.shutdown(); cls.http.server_close(); cls.thread.join()

    def test_range_download_recovers_interruption_and_checks_final_hash(self):
        from resource_download import download
        self.__class__.drop_once = True
        self.__class__.requests = []
        with tempfile.TemporaryDirectory() as tmp:
            target = Path(tmp) / 'wheel.whl'
            progress = []
            result = download(f'http://127.0.0.1:{self.http.server_address[1]}/file', target,
                              hashlib.sha256(self.payload).hexdigest(), chunk_bytes=32768,
                              progress=lambda **row: progress.append(row), backoff=0)
            self.assertEqual(result.read_bytes(), self.payload)
            self.assertTrue(any(int(r[6:].split('-')[0]) > 0 for r in self.requests))
            self.assertEqual(progress[-1]['downloadedBytes'], len(self.payload))

    def test_server_without_range_support_can_still_download(self):
        from resource_download import download
        with tempfile.TemporaryDirectory() as tmp:
            result = download(f'http://127.0.0.1:{self.http.server_address[1]}/ignore-range', Path(tmp)/'plain', hashlib.sha256(self.payload).hexdigest())
            self.assertEqual(result.read_bytes(), self.payload)

    def test_corrupt_payload_is_never_promoted(self):
        from resource_download import download
        with tempfile.TemporaryDirectory() as tmp:
            target = Path(tmp) / 'invalid'
            with self.assertRaisesRegex(ValueError, '校验'):
                download(f'http://127.0.0.1:{self.http.server_address[1]}/file', target, '0'*64)
            self.assertFalse(target.exists())

    def test_cancel_preserves_partial_and_next_call_resumes(self):
        from resource_download import download
        with tempfile.TemporaryDirectory() as tmp:
            target = Path(tmp) / 'resume'
            def cancel():
                if target.with_name(target.name+'.part').exists() and target.with_name(target.name+'.part').stat().st_size >= 32768:
                    raise ValueError('已取消')
            with self.assertRaisesRegex(ValueError, '取消'):
                download(f'http://127.0.0.1:{self.http.server_address[1]}/file', target, hashlib.sha256(self.payload).hexdigest(), chunk_bytes=32768, check=cancel)
            partial = target.with_name(target.name+'.part').stat().st_size
            self.__class__.requests=[]
            download(f'http://127.0.0.1:{self.http.server_address[1]}/file', target, hashlib.sha256(self.payload).hexdigest(), chunk_bytes=32768)
            self.assertTrue(self.requests[0].startswith(f'bytes={partial}-'))


class TorchIndexTests(unittest.TestCase):
    def test_wrong_cuda_build_is_rejected(self):
        from torch_download import wheel
        content=('<a href="https://download.pytorch.org/whl/cu124/torch-2.6.0%2Bcu118-cp311-cp311-win_amd64.whl#sha256='+'a'*64+'">wrong CUDA</a>').encode()
        with patch('torch_download.urllib.request.urlopen',return_value=io.BytesIO(content)), self.assertRaises(ValueError):
            wheel('https://download.pytorch.org/whl/cu124','torch==2.6.0','cp311')

    def test_exact_version_platform_and_hash_are_required(self):
        from torch_download import wheel
        sha='a'*64
        content=('<a href="https://download.pytorch.org/whl/cu124/torch-2.6.0%2Bcu124-cp311-cp311-win_amd64.whl#sha256='+sha+'">good</a>'
                 '<a href="https://evil.invalid/torch-2.6.0-cp311-cp311-win_amd64.whl#sha256='+sha+'">wrong host</a>').encode()
        with patch('torch_download.urllib.request.urlopen',return_value=io.BytesIO(content)):
            result=wheel('https://download.pytorch.org/whl/cu124','torch==2.6.0','cp311')
        self.assertEqual(result['sha256'],sha)
        self.assertEqual(result['filename'],'torch-2.6.0+cu124-cp311-cp311-win_amd64.whl')
        with patch('torch_download.urllib.request.urlopen',return_value=io.BytesIO(content)), self.assertRaises(ValueError):
            wheel('https://download.pytorch.org/whl/cu124','torch==2.7.0','cp311')


class PipResilienceContractTests(unittest.TestCase):
    def test_large_dependency_downloads_use_patient_network_retries(self):
        source = (Path(__file__).resolve().parents[1] / 'service' / 'environment_repair.py').read_text(encoding='utf-8')
        self.assertIn("'--retries','10','--timeout','120'", source)

    def test_embedded_runtimes_bootstrap_build_tools_before_sdists(self):
        root = Path(__file__).resolve().parents[1]
        source = (root / 'service' / 'environment_repair.py').read_text(encoding='utf-8')
        catalog = json.loads((root / 'service' / 'environment-catalog.json').read_text(encoding='utf-8'))
        self.assertEqual(catalog['bootstrapPackages'], ['setuptools==75.6.0', 'wheel==0.45.1'])
        self.assertIn("self.probe(python,['setuptools','wheel'])", source)
        self.assertIn("Python 构建工具安装失败", source)

    def test_pip_dependency_activity_is_visible_to_download_ui(self):
        source = (Path(__file__).resolve().parents[1] / 'service' / 'environment_repair.py').read_text(encoding='utf-8')
        self.assertIn("activity=self.work/'tmp'", source)
        self.assertIn("activity_message='正在下载或安装 Python 依赖'", source)

    def test_gpt_windows_runtime_uses_pure_python_jieba_compatibility(self):
        root = Path(__file__).resolve().parents[1]
        source = (root / 'service' / 'environment_repair.py').read_text(encoding='utf-8')
        catalog = json.loads((root / 'service' / 'environment-catalog.json').read_text(encoding='utf-8'))
        self.assertNotIn('jieba_fast', catalog['gpt-sovits']['packages'])
        self.assertEqual(catalog['gpt-sovits']['packages']['jieba'], 'jieba==0.42.1')
        self.assertIn("if name=='gpt-sovits':self.install_jieba_fast_compat(python)", source)
