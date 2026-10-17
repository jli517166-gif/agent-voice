import copy,hashlib,importlib.util,json,queue,sys,tempfile,threading,time,unittest,zipfile,os,subprocess
from pathlib import Path
from unittest.mock import Mock,patch
sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'service'))
from state import Store,defaults,atomic_json
from jobs import Jobs,Cancelled
from pipeline import Pipeline
from test_core import FakePipeline
import test_tts_bundle as fixtures
from voice_profiles import save_profile,apply_profile,seal_profile
from tts_bundle import export_model,import_model,source_fingerprint

class QueueFaultTests(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory();self.store=Store(self.temp.name);self.store.missing=lambda voice=None:[]
    def tearDown(self):self.temp.cleanup()
    def actor(self,fail):
        class Actor(FakePipeline):
            def __init__(self):super().__init__();self.produced=threading.Event();self.fail=fail
            def synthesize(self,*args):
                r=super().synthesize(*args)
                if len(self.calls)>=5:self.produced.set()
                return r
            def play(self,path,voice,check):
                if not self.fail:return super().play(path,voice,check)
                if not self.produced.wait(3):raise RuntimeError('test producer did not fill buffer')
                if self.fail=='device':raise RuntimeError('output device disconnected')
                while True:check();time.sleep(.01)
        return Actor()
    def test_full_buffer_playback_failure_exits_and_next_job_runs(self):
        actor=self.actor('device');jobs=Jobs(self.store,actor)
        try:
            with patch('jobs.playback_chunks',return_value=['完整句子。']*12):
                r=jobs.submit('长文',play=True);job=jobs.jobs[r['jobId']]
                self.assertTrue(job['done'].wait(5),'producer blocked after consumer died')
            self.assertEqual(job['state'],'failed');self.assertIn('disconnected',job['error'])
            self.assertFalse(list(jobs.output.glob('*.wav')));self.assertFalse(jobs.protected)
            actor.fail=False;r=jobs.submit('恢复后的正常任务',play=True);nextjob=jobs.jobs[r['jobId']]
            self.assertTrue(nextjob['done'].wait(3));self.assertEqual(nextjob['state'],'completed');self.assertIsNone(jobs.last_error)
        finally:jobs.close()
    def test_full_buffer_stop_drains_pending_jobs_and_recovers(self):
        actor=self.actor('block');jobs=Jobs(self.store,actor)
        try:
            with patch('jobs.playback_chunks',return_value=['完整句子。']*12):
                first=jobs.submit('长文',play=True);self.assertTrue(actor.produced.wait(3))
                pending=[jobs.submit('待播放') for _ in range(15)];started=time.monotonic();jobs.stop()
                self.assertEqual(jobs.queue.qsize(),0)
                self.assertTrue(jobs.jobs[first['jobId']]['done'].wait(2));self.assertLess(time.monotonic()-started,2)
            self.assertTrue(all(jobs.jobs[x['jobId']]['state']=='cancelled' for x in pending));self.assertFalse(jobs.protected)
            actor.fail=False;r=jobs.submit('新任务',play=True);self.assertTrue(jobs.jobs[r['jobId']]['done'].wait(3));self.assertEqual(jobs.jobs[r['jobId']]['state'],'completed')
        finally:jobs.close()
    def test_partial_synthesis_file_cleaned_when_producer_fails(self):
        actor=FakePipeline()
        def broken(text,voice,target,check):target.write_bytes(b'partial');raise RuntimeError('CUDA out of memory')
        actor.synthesize=broken;jobs=Jobs(self.store,actor)
        try:
            with patch('jobs.playback_chunks',return_value=['一。','二。']):
                r=jobs.submit('测试',play=True);job=jobs.jobs[r['jobId']];self.assertTrue(job['done'].wait(3));self.assertEqual(job['state'],'failed')
            self.assertFalse(list(jobs.output.iterdir()));self.assertFalse(jobs.protected)
        finally:jobs.close()
    def test_close_with_full_pending_queue_finishes(self):
        actor=self.actor('block');jobs=Jobs(self.store,actor)
        with patch('jobs.playback_chunks',return_value=['句子。']*12):
            jobs.submit('长文',play=True);self.assertTrue(actor.produced.wait(3))
            for i in range(64):jobs.submit('待播放')
            t=time.monotonic();jobs.close();self.assertFalse(jobs.thread.is_alive());self.assertLess(time.monotonic()-t,4)
    def test_cosy_cancel_during_inference_aborts_owned_worker(self):
        from cosy_client import NaturalTTSClient
        c=NaturalTTSClient.__new__(NaturalTTSClient);c.ensure_ready=Mock();c.abort=Mock();c.token='token';c.url='http://127.0.0.1';c.opener=Mock()
        release=threading.Event()
        def wait(*a,**kw):release.wait(3);raise OSError('cancelled connection')
        c.opener.open.side_effect=wait
        try:
            def check():raise Cancelled('已停止')
            with self.assertRaises(Cancelled):c.synthesize('正文',Path(self.temp.name).resolve()/'test.wav','chat',0,check)
            c.abort.assert_called_once()
        finally:release.set()

class ProfileAndPackageTests(unittest.TestCase):
    def setUp(self):fixtures.TtsBundleTests.setUp(self)
    def tearDown(self):fixtures.TtsBundleTests.tearDown(self)
    def test_profile_save_failure_rolls_back_asset_records(self):
        before=self.store.path.read_bytes();voice=dict(self.store.get()['voice'],assetScope='private')
        with patch.object(self.store,'update',side_effect=RuntimeError('disk error')):
            with self.assertRaises(RuntimeError):save_profile(self.store,'回滚',voice)
        self.assertEqual(self.store.path.read_bytes(),before);self.assertFalse((self.root/'data/private-assets.json').exists());self.assertEqual(list((self.root/'data/voice-profile-assets').glob('*.json')),[])
    def test_corrupt_private_registry_cannot_half_save_profile(self):
        registry=self.root/'data/private-assets.json';registry.write_text('{broken');before=self.store.path.read_bytes()
        with self.assertRaises(ValueError):save_profile(self.store,'不能半写入',dict(self.store.get()['voice'],assetScope='private'))
        self.assertEqual(self.store.path.read_bytes(),before);self.assertEqual(registry.read_text(),'{broken')
    def test_saved_profile_survives_original_reference_removed(self):
        before=self.store.get()['voice'];r=save_profile(self.store,'固定参考',before);p=r['profile']
        self.assertEqual(self.store.get()['voice'],before)
        self.assertEqual(self.store.resolve(p['voice']['reference']).read_bytes(),(self.root/'reference.wav').read_bytes())
        (self.root/'reference.wav').unlink();apply_profile(self.store,p['id']);self.assertEqual(self.store.get()['voice'],p['voice'])
    def test_repeated_save_deduplicates_reference(self):
        a=save_profile(self.store,'甲',self.store.get()['voice']);b=save_profile(self.store,'乙',self.store.get()['voice'])
        self.assertEqual(a['profile']['voice']['reference'],b['profile']['voice']['reference']);self.assertEqual(len(list((self.root/'data/voice-assets').iterdir())),1)
    def test_changed_model_rejected_without_applying(self):
        r=save_profile(self.store,'固定',self.store.get()['voice']);(self.model/'llm.pt').write_bytes(b'new weights')
        before=self.store.path.read_bytes()
        with self.assertRaisesRegex(ValueError,'模型文件已被替换'):apply_profile(self.store,r['profile']['id'])
        self.assertEqual(self.store.path.read_bytes(),before)
    def test_corrupt_pinned_reference_rejected(self):
        r=save_profile(self.store,'固定',self.store.get()['voice']);self.store.resolve(r['profile']['voice']['reference']).write_bytes(b'bad')
        with self.assertRaisesRegex(ValueError,'固定参考录音损坏'):apply_profile(self.store,r['profile']['id'])
    def test_legacy_profile_can_be_sealed_without_changing_current_voice(self):
        voice=self.store.get()['voice'];self.store.update({'profiles':[{'id':'old','name':'旧方案','voice':voice}]});r=seal_profile(self.store,'old')
        self.assertEqual(r['profile']['id'],'old');self.assertEqual(self.store.get()['voice'],voice)
    def test_comments_and_readme_do_not_break_new_packages(self):
        source=self.root/'tts-engines/cosyvoice/source/cosyvoice/version.py';source.write_text('# harmless comment\n__version__ = "fixture"\n')
        (source.parent/'README.md').write_text('new documentation')
        self.assertTrue(import_model(self.root,self.package)['ok'])
    def test_semantic_source_change_still_rejected(self):
        (self.root/'tts-engines/cosyvoice/source/cosyvoice/version.py').write_text('__version__="incompatible"')
        with self.assertRaisesRegex(ValueError,'源码版本'):import_model(self.root,self.package)
    def test_private_tts_archive_cannot_be_released_under_different_suffix(self):
        modulepath=Path(__file__).resolve().parents[1]/'tools/release_audit.py';spec=importlib.util.spec_from_file_location('audit_test',modulepath);audit=importlib.util.module_from_spec(spec);spec.loader.exec_module(audit)
        self.store.update({'voice':{'assetScope':'private'}});export_model(self.root,self.package)
        other=self.package.with_suffix('.zip');other.write_bytes(self.package.read_bytes());self.assertTrue(audit.private_bundle(other,set()))
    def test_private_reference_hash_detected_even_when_scope_public(self):
        modulepath=Path(__file__).resolve().parents[1]/'tools/release_audit.py';spec=importlib.util.spec_from_file_location('audit_test',modulepath);audit=importlib.util.module_from_spec(spec);spec.loader.exec_module(audit)
        sha=hashlib.sha256((self.root/'reference.wav').read_bytes()).hexdigest();self.assertTrue(audit.private_bundle(self.package,{sha}))

class RuntimeAndStateTests(unittest.TestCase):
    def test_custom_gpt_venv_is_not_repointed_to_core(self):
        from runtime_paths import repair_bundled_runtime
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp).resolve();core=root/'runtime/core';core.mkdir(parents=True);(core/'python.exe').write_bytes(b'test')
            runtime=root/'tts-engines/gpt-sovits/runtime';runtime.mkdir(parents=True);cfg=runtime/'pyvenv.cfg';cfg.write_text('home = C:/user-python\nversion = 3.10.11\n')
            before=cfg.read_bytes();self.assertEqual(repair_bundled_runtime(root),[]);self.assertEqual(cfg.read_bytes(),before)
    @unittest.skipUnless(os.name=='nt','Windows worker tree')
    def test_cancel_kills_owned_worker_redirector_and_child(self):
        from owned_process import terminate
        import ctypes
        with tempfile.TemporaryDirectory() as temp:
            pidfile=Path(temp).resolve()/'child.pid'
            code="import subprocess,sys,time,pathlib; p=subprocess.Popen([sys.executable,'-c','import time; time.sleep(30)']); pathlib.Path("+repr(str(pidfile))+").write_text(str(p.pid)); time.sleep(30)"
            process=subprocess.Popen([sys.executable,'-c',code],creationflags=subprocess.CREATE_NO_WINDOW)
            try:
                deadline=time.monotonic()+5
                while not pidfile.exists() and time.monotonic()<deadline:time.sleep(.02)
                child=int(pidfile.read_text());terminate(process);self.assertIsNotNone(process.poll())
                kernel=ctypes.WinDLL('kernel32',use_last_error=True);kernel.OpenProcess.restype=ctypes.c_void_p;kernel.CloseHandle.argtypes=[ctypes.c_void_p]
                handle=kernel.OpenProcess(0x1000,False,child)
                if handle:
                    status=ctypes.c_ulong();kernel.GetExitCodeProcess.argtypes=[ctypes.c_void_p,ctypes.POINTER(ctypes.c_ulong)]
                    try:kernel.GetExitCodeProcess(handle,ctypes.byref(status));self.assertNotEqual(status.value,259,'orphan inference child survived cancellation')
                    finally:kernel.CloseHandle(handle)
            finally:terminate(process)
    def test_cancelled_prepare_is_not_reported_as_model_failure(self):
        with tempfile.TemporaryDirectory() as temp:
            p=Pipeline(Store(temp))
            def cancelled():raise Cancelled('已停止')
            with self.assertRaises(Cancelled):p.prepare(p.store.get()['voice'],cancelled)
            self.assertEqual(p.state,'not_loaded');self.assertIsNone(p.error)
    def test_diagnostics_low_disk_and_device_failure_are_visible(self):
        from diagnostics import inspect_installation
        import sounddevice
        with tempfile.TemporaryDirectory() as temp,patch('diagnostics.shutil.disk_usage',return_value=type('Usage',(),{'free':0})()),patch.object(sounddevice,'query_devices',side_effect=RuntimeError('no output device')):
            r=inspect_installation(Store(temp),True);bad=[c['name'] for c in r['checks'] if not c['ok']]
            self.assertIn('缓存空间',bad);self.assertIn('播放设备',bad);self.assertFalse(r['ok'])
    def test_v3_migration_preserves_voice_and_uses_core_python(self):
        with tempfile.TemporaryDirectory() as temp:
            cfg=defaults();cfg['version']=3;cfg['engines']['pythonService']='tts-engines/cosyvoice/runtime/tools/python.exe';cfg['voice']['speed']=1.12
            atomic_json(Path(temp).resolve()/'data/settings.json',cfg);store=Store(temp);after=store.get()
            self.assertEqual(after['version'],4);self.assertEqual(after['voice'],cfg['voice']);self.assertEqual(after['engines']['pythonService'],'runtime/core/python.exe')
            self.assertEqual(json.loads((Path(temp).resolve()/'data/migrations/settings-before-stability.json').read_text(encoding='utf-8')),cfg)
    def test_snapshot_distinguishes_unloaded_ready_and_dead_worker(self):
        with tempfile.TemporaryDirectory() as temp:
            p=Pipeline(Store(temp));voice=p.store.get()['voice'];self.assertEqual(p.snapshot(voice)['state'],'not_loaded')
            p.adapter=Mock();p.adapter.alive.return_value=True;p.loaded=True;p.loaded_key=p.signature(voice)
            self.assertEqual(p.snapshot(voice)['state'],'ready');p.adapter.alive.return_value=False;self.assertEqual(p.snapshot(voice)['state'],'error');p.invalidate();self.assertFalse(p.snapshot(voice)['ready'])
    def test_new_runtime_relocates_without_cosy_or_candidate(self):
        from runtime_paths import repair_bundled_runtime
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp).resolve();base=root/'runtime/core';base.mkdir(parents=True);(base/'python.exe').write_bytes(b'test')
            runtime=root/'tts-engines/gpt-sovits/runtime';(runtime/'Lib/site-packages').mkdir(parents=True);(runtime/'gpu-packages').mkdir();(runtime/'pyvenv.cfg').write_text('home = obsolete\nexecutable = obsolete\n');pth=runtime/'Lib/site-packages/000_candidate_gpu.pth';pth.write_text('obsolete')
            repair_bundled_runtime(root);self.assertIn(str(base),(runtime/'pyvenv.cfg').read_text());self.assertIn("/'gpu-packages'",pth.read_text());self.assertNotIn('cosyvoice',pth.read_text())
    def test_diagnostics_missing_engine_is_failure_not_success(self):
        from diagnostics import inspect_installation
        with tempfile.TemporaryDirectory() as temp:
            r=inspect_installation(Store(temp));self.assertFalse(r['ok']);self.assertTrue(Path(r['report']).is_file());self.assertIn('不代表',r['scope'])

class RecoveryApiTests(unittest.TestCase):
    def setUp(self):
        import test_core
        self.fixture=test_core.CoreTests('test_authentication_and_unknown_interfaces');self.fixture.setUp();self.addCleanup(self.fixture.tearDown)
    def test_catalog_and_recovery_clear_error_and_reload(self):
        f=self.fixture;f.actor.prepare=Mock();f.server.jobs.last_error='device lost'
        self.assertEqual(f.request('/health')['serviceState'],'error')
        ids=[e['id'] for e in f.request('/api/engines')['engines']];self.assertEqual(ids,['cosyvoice','gpt-sovits'])
        r=f.request('/api/recover',{});self.assertEqual(r['state'],'loading')
        deadline=time.monotonic()+2
        while not f.actor.prepare.called and time.monotonic()<deadline:time.sleep(.01)
        f.actor.prepare.assert_called_once();self.assertIsNone(f.server.jobs.last_error);self.assertEqual(f.request('/health')['serviceState'],'ready')
    def test_profile_and_recovery_endpoints_require_authentication(self):
        import urllib.error
        for endpoint in ['/api/recover','/api/diagnostics','/api/profiles/save','/api/profiles/apply']:
            with self.assertRaises(urllib.error.HTTPError) as c:self.fixture.request(endpoint,{},auth=False)
            self.assertEqual(c.exception.code,401)

if __name__=='__main__':unittest.main()
