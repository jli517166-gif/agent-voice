"""Rights boundaries, privacy defaults and real container/sample preservation."""
import copy,io,json,os,struct,subprocess,sys,tempfile,time,unittest,wave,zipfile
from pathlib import Path
from unittest.mock import Mock,patch
ROOT=Path(__file__).resolve().parents[1]
sys.path[:0]=[str(ROOT/'service'),str(ROOT/'tools')]
from aigc import metadata,label_wav,label_wav_bytes,read_wav,read_wav_bytes,label_mp3,syncsafe,unsyncsafe
from state import Store,defaults,atomic_json,validate
from privacy import reading_allowed,clear_output_records,rotate_token,clean_logs,diagnostic_summary
from voice_rights import read_record,save_record,public_summary
from release_compliance import inspect
from operation_lock import OperationLock
import test_core,optional_voice_module,test_tts_bundle


def wav_bytes():
    stream=io.BytesIO()
    with wave.open(stream,'wb') as wav:
        wav.setparams((1,2,24000,0,'NONE',''));wav.writeframes(struct.pack('<'+'h'*4800,*[(i%99-49)*120 for i in range(4800)]))
    return stream.getvalue()


def pcm(audio):
    with wave.open(io.BytesIO(audio),'rb') as wav:return wav.getparams(),wav.readframes(wav.getnframes())


class ContainerTests(unittest.TestCase):
    def test_many_short_segments_keep_all_parent_labels_without_metadata_overflow(self):
        import zlib,base64
        from aigc import metadata_for_join,payload
        originals=[metadata('segment-'+str(i)) for i in range(1000)]
        with patch('aigc.read_wav',side_effect=originals):result=metadata_for_join(range(1000))
        saved=json.loads(result['ReservedCode1']);self.assertEqual(saved['encoding'],'zlib-base64-json');self.assertEqual(json.loads(zlib.decompress(base64.b64decode(saved['data'])))['sourceMetadata'],originals);self.assertLess(len(payload(result)),65536)
    def test_wav_metadata_keeps_exact_pcm_and_existing_provenance(self):
        original=wav_bytes();tag=metadata('test-id');marked=label_wav_bytes(original,tag)
        self.assertEqual(pcm(original),pcm(marked));self.assertEqual(read_wav_bytes(marked),tag)
        self.assertEqual(label_wav_bytes(marked,metadata('replacement')),marked)
        self.assertNotIn(b'token',marked);self.assertNotIn(b'reference',marked)
        with tempfile.TemporaryDirectory() as temp:
            file=Path(temp)/'real.wav';file.write_bytes(original);self.assertEqual(label_wav(file,tag),tag);self.assertEqual(file.read_bytes(),marked)
    def test_wav_rejects_truncated_and_out_of_riff_data(self):
        for audio in (b'RIFF',wav_bytes()[:-1],b'RIFF'+struct.pack('<I',0)+b'WAVE'):
            with self.assertRaises(ValueError):label_wav_bytes(audio)
        with self.assertRaises(ValueError):label_wav_bytes(wav_bytes()+b'private trailer')
    def test_mp3_id3_versions_keep_encoded_audio_and_other_tags(self):
        for version in (3,4):
            body=b'\x03preserved title';length=syncsafe(len(body)) if version==4 else len(body).to_bytes(4,'big')
            title=b'TIT2'+length+b'\0\0'+body;original=b'ID3'+bytes((version,0,0))+syncsafe(len(title))+title+b'ENCODED-AUDIO'
            tagged=label_mp3(original,metadata('known'));self.assertIn(title,tagged);self.assertTrue(tagged.endswith(b'ENCODED-AUDIO'))
            self.assertEqual(label_mp3(tagged,metadata('replacement')),tagged)
            with self.assertRaises(ValueError):label_mp3(b'ID3\x04\0\x80'+syncsafe(0),metadata())
    def test_real_ffmpeg_mp3_preserves_encoded_payload_and_decodes(self):
        ffmpeg=Path(os.environ.get('AGENT_VOICE_TEST_FFMPEG',r'E:\App\agent 语音服务\Agent语音\tools\ffmpeg.exe'))
        if not ffmpeg.exists():self.skipTest('Real FFmpeg not installed in this test environment')
        raw=subprocess.run([str(ffmpeg),'-v','error','-i','pipe:0','-f','mp3','pipe:1'],input=label_wav_bytes(wav_bytes()),capture_output=True,check=True).stdout
        marked=label_mp3(raw,metadata('known'));offset=10+unsyncsafe(marked[6:10]);old_offset=10+unsyncsafe(raw[6:10]) if raw.startswith(b'ID3') else 0
        self.assertEqual(marked[offset:],raw[old_offset:])
        decoded=subprocess.run([str(ffmpeg),'-v','error','-i','pipe:0','-f','s16le','pipe:1'],input=marked,capture_output=True,check=True)
        self.assertGreater(len(decoded.stdout),1000)
    def test_production_concatenate_preserves_pcm_and_labels_output(self):
        from pipeline import Pipeline
        with tempfile.TemporaryDirectory() as temp:
            store=Store(temp);a=Path(temp)/'a.wav';b=Path(temp)/'b.wav';target=Path(temp)/'joined.wav';a.write_bytes(wav_bytes());b.write_bytes(label_wav_bytes(wav_bytes()))
            pipeline=Pipeline(store);rate,duration=pipeline.concatenate([a,b],target,lambda:None)
            self.assertEqual(rate,24000);self.assertEqual(duration,.4);self.assertEqual(pcm(target.read_bytes())[1],pcm(a.read_bytes())[1]+pcm(b.read_bytes())[1]);label=read_wav(target);self.assertIsNotNone(label);self.assertEqual(json.loads(label['ReservedCode1'])['sourceMetadata'],[read_wav(b)])


class PrivacyTests(unittest.TestCase):
    def test_new_install_off_migration_preserves_explicit_reading_and_retention(self):
        self.assertEqual(defaults()['harness'],{'codex':False,'deepseek':False});self.assertEqual(defaults()['cleanup']['keepLatest'],0)
        with tempfile.TemporaryDirectory() as temp:
            s=Store(temp);old=s.get();old.pop('privacy');old['cleanup'].pop('keepLatestBeyondAge');old['cleanup']['keepLatest']=3;old['harness']['codex']=True;voice=copy.deepcopy(old['voice']);atomic_json(s.path,old)
            loaded=Store(temp).get();self.assertTrue(loaded['harness']['codex']);self.assertTrue(loaded['cleanup']['keepLatestBeyondAge']);self.assertEqual(loaded['voice'],voice)
    def test_scope_boundary_never_accepts_missing_or_sibling_project(self):
        cfg=defaults();cfg['privacy'].update(harnessScope='project',projectRoot=str(ROOT))
        self.assertTrue(reading_allowed(cfg,{'cwd':str(ROOT/'ui')}));self.assertFalse(reading_allowed(cfg,{}));self.assertFalse(reading_allowed(cfg,{'cwd':str(ROOT)+'-other'}))
        cfg['privacy'].update(harnessScope='session',sessionId='current');self.assertTrue(reading_allowed(cfg,{'sessionId':'current'}));self.assertFalse(reading_allowed(cfg,{'sessionId':'other'}));self.assertFalse(reading_allowed(cfg,{}))
    def test_rotate_changes_token_and_disables_external_playback_without_voice_changes(self):
        with tempfile.TemporaryDirectory() as temp:
            s=Store(temp);s.update({'harness':{'codex':True},'airi':{'enabled':True,'output':'airi'}});before=s.get()
            with OperationLock(temp):rotate_token(s)
            after=Store(temp).get();self.assertNotEqual(before['token'],after['token']);self.assertEqual(before['voice'],after['voice']);self.assertEqual(before['profiles'],after['profiles']);self.assertFalse(any(after['harness'].values()));self.assertFalse(after['airi']['enabled'])
    def test_clear_text_is_narrow_and_log_cleanup_respects_age(self):
        with tempfile.TemporaryDirectory() as temp:
            s=Store(temp);out=s.root/'data/output';out.mkdir();file=out/('a'*32+'.json');atomic_json(file,{'text':'secret','recoveredErrors':['private path'],'file':'audio.wav','duration':1});atomic_json(out/'user.json',{'text':'user authored'})
            self.assertEqual(clear_output_records(s)['removed'],1);self.assertNotIn('text',json.loads(file.read_text()));self.assertIn('user authored',(out/'user.json').read_text())
            logs=s.root/'data/logs';logs.mkdir();old=logs/'old.log';old.write_text('private');os.utime(old,(0,0));(logs/'current.log').write_text('current');(logs/'model.bin').write_text('keep');(logs/'backups').mkdir();(logs/'backups/old.log').write_text('keep')
            self.assertEqual(clean_logs(s.root,7)['removed'],1);self.assertTrue((logs/'model.bin').exists());self.assertTrue((logs/'backups/old.log').exists())
    def test_diagnostic_allowlist_excludes_paths_text_and_tokens(self):
        with tempfile.TemporaryDirectory() as temp:
            s=Store(temp)
            with patch('diagnostics.inspect_installation',return_value={'ok':False,'activeEngine':'cosyvoice','token':'secret','checks':[{'name':'Python','ok':False,'section':'core','warning':False,'detail':'C:/private secret recording.wav'}]}):result=diagnostic_summary(s)
            text=json.dumps(result);self.assertNotIn('C:/',text);self.assertNotIn('secret',text);self.assertNotIn('detail',text)
    def test_disabled_codex_does_not_open_transcripts_or_replay_after_enable(self):
        from harness import CodexTail
        with tempfile.TemporaryDirectory() as temp:
            folder=Path(temp)/'sessions/2026/10/22';folder.mkdir(parents=True);file=folder/'s.jsonl'
            line={'type':'response_item','payload':{'type':'message','role':'assistant','phase':'commentary','content':[{'type':'output_text','text':'old'}]}}
            file.write_text(json.dumps(line)+'\n');enabled=[False];received=[];tail=CodexTail(lambda s,e:received.append(e),lambda:enabled[0],root=temp)
            original=Path.open
            def guarded(path,*a,**kw):
                if str(path)==str(file) and not enabled[0]:raise AssertionError('Disabled reader opened a transcript')
                return original(path,*a,**kw)
            with patch.object(Path,'open',guarded):
                tail.start()
                try:time.sleep(.6);enabled[0]=True;time.sleep(.7);self.assertEqual(received,[])
                finally:tail.close()


class ApiPrivacyTests(unittest.TestCase):
    setUp=test_core.CoreTests.setUp
    tearDown=test_core.CoreTests.tearDown
    request=test_core.CoreTests.request
    wait=test_core.CoreTests.wait
    def test_live_api_scope_blocks_wrong_project_and_allows_manual_mcp(self):
        self.request('/api/config',{'harness':{'codex':True},'privacy':{'harnessScope':'project','projectRoot':str(self.store.root)}})
        event={'type':'agent-turn-complete','thread-id':'session','turn-id':'wrong','last-assistant-message':'范围之外'}
        self.assertTrue(self.request('/api/harness/codex',event)['skipped'])
        event.update(cwd=str(self.store.root),**{'turn-id':'allowed'});job=self.request('/api/harness/codex',event);self.assertEqual(self.wait(job['jobId'])['state'],'completed')
        manual=self.request('/speak',{'text':'用户手动请求','source':'mcp-read-aloud'});self.assertEqual(self.wait(manual['jobId'])['state'],'completed')
    def test_default_no_persisted_text_download_labeled_and_share_preserves_body(self):
        job=self.request('/speak',{'text':'原始正文','play':False});original=self.wait(job['jobId']);file=self.store.root/'data/output'/original['result']['file']
        self.assertNotIn('text',json.loads(file.with_suffix('.json').read_text(encoding='utf-8')))
        with self.opener.open(__import__('urllib.request',fromlist=['']).Request(self.base+original['result']['audioUrl'],headers={'Authorization':'Bearer '+self.store.get()['token']})) as response:self.assertIsNotNone(read_wav_bytes(response.read()))
        shared=self.request('/api/export/share',{'jobId':job['jobId']});done=self.wait(shared['jobId']);self.assertEqual(done['state'],'completed');self.assertTrue(done['result']['explicitAiNotice'])
        result=self.store.root/'data/output'/done['result']['file'];self.assertTrue(pcm(result.read_bytes())[1].endswith(pcm(file.read_bytes())[1]));self.assertEqual(self.actor.calls[-1][0],'以下音频由人工智能合成。');self.assertFalse(self.server.jobs.protected)
    def test_clear_records_and_rotate_rejected_while_live(self):
        import urllib.error
        with self.assertRaises(urllib.error.HTTPError) as error:self.request('/api/privacy/rotate',{})
        self.assertEqual(error.exception.code,400);self.assertIn('checks',self.request('/api/privacy/diagnostic',{}))
    def test_airi_leased_download_contains_container_label_before_ack(self):
        self.store.update({'airi':{'enabled':True,'output':'airi'}});self.request('/api/airi/poll',{'client':'test-airi'})
        job=self.request('/speak',{'text':'桌宠合成提示','play':True});task=None
        for _ in range(80):
            task=self.request('/api/airi/poll',{'client':'test-airi'})['task']
            if task:break
            time.sleep(.02)
        self.assertIsNotNone(task);audio,mime=self.request(task['audioUrl'],raw=True);self.assertEqual(mime,'audio/wav');self.assertIsNotNone(read_wav_bytes(audio))
        self.request('/api/airi/ack',{'client':'test-airi','id':task['id']});done=self.wait(job['jobId']);self.assertEqual(done['result']['output'],'airi')
    def test_api_mp3_has_id3_provenance(self):
        ffmpeg=Path(os.environ.get('AGENT_VOICE_TEST_FFMPEG',r'E:\App\agent 语音服务\Agent语音\tools\ffmpeg.exe'))
        if not ffmpeg.exists():self.skipTest('Real FFmpeg is not installed')
        self.store.update({'engines':{'ffmpeg':str(ffmpeg)}})
        audio,mime=self.request('/v1/audio/speech',{'model':'agent-voice-tts','input':'语音标识测试','response_format':'mp3'},raw=True)
        self.assertEqual(mime,'audio/mpeg');self.assertTrue(audio.startswith(b'ID3'));self.assertIn(b'AIGC',audio);self.assertIn(b'AgentVoice.local',audio)


class RightsAndPackageTests(unittest.TestCase):
    setUp=test_tts_bundle.TtsBundleTests.setUp
    tearDown=test_tts_bundle.TtsBundleTests.tearDown
    rewrite=test_tts_bundle.TtsBundleTests.rewrite
    def test_public_summary_excludes_private_evidence_and_import_remains_unverified(self):
        from tts_bundle import export_model,import_model
        declaration={'source':'PRIVATE_SOURCE','holder':'PRIVATE_PERSON','evidenceRef':'C:/PRIVATE_CONTRACT.pdf','notes':'PRIVATE_NOTES','licenseSummary':'Example granted scope','permittedUses':['generation','privateUse']}
        save_record(self.store,'reference.wav',declaration);self.assertEqual(read_record(self.store,'reference.wav')['record']['source'],'PRIVATE_SOURCE')
        export_model(self.root,self.package,package_mode='lightweight')
        with zipfile.ZipFile(self.package) as archive:manifest=json.loads(archive.read('manifest.json'))
        text=json.dumps(manifest);self.assertNotIn('PRIVATE_',text);self.assertEqual(manifest['licenseSummary']['status'],'user-recorded')
        result=import_model(self.root,self.package);self.assertIn('未核验',result['licenseStatus'])
        imported=next((self.root/'data/imported-license-summaries').glob('*.json'));self.assertEqual(json.loads(imported.read_text(encoding='utf-8'))['status'],'imported-unverified')
    def test_legacy_without_rights_allowed_and_summary_write_failure_rolls_back_config(self):
        from tts_bundle import import_model
        package=self.rewrite(self.package,lambda m,f:m.pop('licenseSummary'));result=import_model(self.root,package);self.assertIn('旧包',result['licenseStatus'])
        before=Store(self.root).path.read_bytes()
        with patch('voice_rights.save_imported_summary',side_effect=OSError('disk full')):
            with self.assertRaises(OSError):import_model(self.root,self.package)
        self.assertEqual(Store(self.root).path.read_bytes(),before)
    def test_scope_not_certificate_and_forged_hash_rejected_before_config(self):
        from tts_bundle import import_model
        from voice_rights import material_hash
        with self.assertRaisesRegex(ValueError,'先选择'):material_hash(None)
        package=self.rewrite(self.package,lambda m,f:m['licenseSummary'].update(materialSha256='a'*64))
        before=self.store.path.read_bytes()
        with self.assertRaises(ValueError):import_model(self.root,package)
        self.assertEqual(self.store.path.read_bytes(),before)


class ReleaseGateTests(unittest.TestCase):
    def test_evidence_bound_to_each_file_and_artifact_not_generic_permission(self):
        with tempfile.TemporaryDirectory() as temp:
            file=Path(temp)/'app.zip'
            with zipfile.ZipFile(file,'w') as archive:archive.writestr('LICENSE','MIT sample');archive.writestr('app.py','# sample')
            result=inspect(file);self.assertFalse(result['publicReleaseEligible'])
            review={'artifactSha256':result['artifactSha256'],'files':{f['path']:dict(f,decision='redistribution-reviewed',license='MIT',source='test fixture',evidence='fixture only',reviewer='fixture',reviewedAt='2026-10-22') for f in result['files']}}
            self.assertTrue(inspect(file,review=review)['publicReleaseEligible']);review['files']['app.py']['sha256']='0'*64;self.assertFalse(inspect(file,review=review)['publicReleaseEligible'])
    def test_residue_and_missing_matcha_notice_are_reported(self):
        with tempfile.TemporaryDirectory() as temp:
            file=Path(temp)/'engine.ttsengine'
            with zipfile.ZipFile(file,'w') as archive:archive.writestr('payload/source/third_party/Matcha-TTS/matcha/file.py','source');archive.writestr('payload/source/download.range','download');archive.writestr('payload/data/settings.json','private')
            result=inspect(file);text=json.dumps(result['findings'],ensure_ascii=False);self.assertIn('Matcha',text);self.assertIn('残留',text);self.assertIn('用户设置',text)
    def test_engine_package_adds_matcha_license_without_mutating_source(self):
        from module_packages import engine_package
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp);source=root/'tts-engines/cosyvoice/source/third_party/Matcha-TTS/matcha';source.mkdir(parents=True);(source/'__init__.py').write_text('');(source/'bad.range').write_text('remove');model=root/'tts-engines/cosyvoice/models/Fun-CosyVoice3-0.5B';model.mkdir(parents=True);(model/'llm.pt').write_bytes(b'fixture')
            file=root/'engine.ttsengine';engine_package(root,'cosyvoice',file)
            with zipfile.ZipFile(file) as archive:
                license_path='payload/tts-engines/cosyvoice/source/third_party/Matcha-TTS/LICENSE';self.assertIn(b'Shivam',archive.read(license_path));self.assertFalse(any(n.endswith('.range') for n in archive.namelist()))
            self.assertFalse((source.parent/'LICENSE').exists())


if __name__=='__main__':unittest.main()
