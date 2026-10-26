"""App-local environment repair with a real end-to-end synthesis check."""
import argparse,copy,hashlib,json,os,platform,shutil,subprocess,sys,time,urllib.request,uuid,zipfile
from pathlib import Path
import runtime_paths
from state import Store,atomic_json
from operation_lock import OperationLock
from engine_registry import spec,gpt_required,worker_config

CATALOG=json.loads(Path(__file__).with_name('environment-catalog.json').read_text(encoding='utf-8'))
MAX_ARCHIVE_BYTES=512*1024**2

def inside(root,path):
    root=Path(root).resolve();path=Path(path).resolve()
    if path==root or root not in path.parents:raise ValueError('补齐文件必须放在软件目录内部：'+str(path))
    return path

from source_archive import extract

class Repair:
    def __init__(self,root,offline=False):
        self.offline=offline;self.store=Store(root);self.root=self.store.root
        runtime_paths.repair_bundled_runtime(self.root)
        self.work=self.root/'data/environment-repair';self.work.mkdir(parents=True,exist_ok=True)
        self.log=self.work/'install.log'
    def progress(self,message,stage='working',**extra):
        atomic_json(self.work/'progress.json',dict(stage=stage,message=message,time=time.time(),**extra))
    def env(self):
        env=dict(os.environ)
        for key in list(env):
            if key.upper().startswith('PIP_'):env.pop(key,None)
        for key in ('PYTHONHOME','PYTHONPATH','PIP_TARGET','PIP_PREFIX','PIP_USER','VIRTUAL_ENV','HF_HUB_OFFLINE','TRANSFORMERS_OFFLINE'):env.pop(key,None)
        env.update(PYTHONNOUSERSITE='1',PYTHONUTF8='1',PIP_CONFIG_FILE=os.devnull,PIP_REQUIRE_VIRTUALENV='false',PIP_CACHE_DIR=str(self.work/'pip-cache'),TMP=str(self.work/'tmp'),TEMP=str(self.work/'tmp'),HF_HOME=str(self.work/'hf-cache'),HF_HUB_DISABLE_TELEMETRY='1',MODELSCOPE_CACHE=str(self.work/'modelscope-cache'),TORCH_HOME=str(self.work/'torch-cache'),XDG_CACHE_HOME=str(self.work/'cache'),MPLCONFIGDIR=str(self.work/'matplotlib'),NLTK_DATA=str(self.root/'tts-engines/gpt-sovits/nltk_data'))
        (self.work/'tmp').mkdir(exist_ok=True)
        env['PATH']=str(self.root/'tools')+os.pathsep+env.get('PATH','')
        if self.offline:env.update(HF_HUB_OFFLINE='1',TRANSFORMERS_OFFLINE='1',PIP_NO_INDEX='1')
        env.update(HF_HUB_DISABLE_XET='1',HF_HUB_DOWNLOAD_TIMEOUT='60',HF_HUB_ETAG_TIMEOUT='30')
        return env
    def run(self,command,timeout=1800):
        from pipeline import hidden_process
        with self.log.open('a',encoding='utf-8') as log:
            log.write('\n'+json.dumps([str(x) for x in command],ensure_ascii=False)+'\n');log.flush()
            process=subprocess.Popen([str(x) for x in command],stdout=log,stderr=log,cwd=self.root,env=self.env(),**hidden_process())
            try:code=process.wait(timeout=timeout)
            except subprocess.TimeoutExpired:
                from owned_process import terminate
                terminate(process);raise RuntimeError('安装步骤超时，已停止本次进程；详见 '+str(self.log))
        if code:raise RuntimeError('安装步骤失败（退出码 %d），可修复后重试；详见 %s'%(code,self.log))
    def download(self,url,name,sha256=None):
        target=inside(self.root,self.work/'downloads'/name);target.parent.mkdir(parents=True,exist_ok=True)
        if target.is_file() and sha256 and hashlib.sha256(target.read_bytes()).hexdigest()==sha256:return target
        if self.offline:
            if target.is_file() and not sha256:return target
            raise ValueError('离线资源缺少或校验失败：'+name+'；请导入完整环境包后重试，不会连接网络')
        temporary=target.with_name(target.name+'.part');last=0
        try:
            with urllib.request.urlopen(urllib.request.Request(url,headers={'User-Agent':'AgentVoice-EnvironmentRepair/1'}),timeout=60) as incoming,temporary.open('wb') as output:
                total=int(incoming.headers.get('Content-Length',0));count=0
                while True:
                    block=incoming.read(1024*1024)
                    if not block:break
                    count+=len(block)
                    if count>MAX_ARCHIVE_BYTES:raise ValueError('下载文件超过限制')
                    output.write(block)
                    if time.monotonic()-last>1:self.progress('正在下载 '+name,downloadedBytes=count,totalBytes=total);last=time.monotonic()
            if sha256 and hashlib.sha256(temporary.read_bytes()).hexdigest()!=sha256:raise ValueError('下载校验不匹配：'+name)
            os.replace(temporary,target);return target
        finally:temporary.unlink(missing_ok=True)
    def python(self,directory):
        directory=inside(self.root,directory);entry=directory/'python.exe'
        if entry.is_file():return entry
        asset=CATALOG['python'];archive=self.download(asset['url'],'python-'+asset['version']+'.zip',asset['sha256'])
        stage=directory.with_name('.python-stage-'+uuid.uuid4().hex)
        try:
            extract(archive,stage)
            tag=''.join(asset['version'].split('.')[:2]);pth=stage/('python'+tag+'._pth');pth.write_text('python'+tag+'.zip\n.\nLib/site-packages\nimport site\n',encoding='utf-8')
            if directory.exists():
                for path in stage.iterdir():
                    if (directory/path.name).exists():raise ValueError('环境目录含冲突文件，请手动保留后清理：'+str(directory))
                for path in stage.iterdir():shutil.move(str(path),str(directory/path.name))
                stage.rmdir()
            else:os.replace(stage,directory)
        finally:
            if stage.exists():shutil.rmtree(inside(self.root,stage))
        return entry
    def probe(self,python,modules):
        if not Path(python).is_file():return {'missing':list(modules),'error':'Python 不存在'}
        # g2p_en's module import downloads NLTK data as a side effect. Inspect its
        # presence here; the explicit offline source check exercises its resources.
        script="import importlib,importlib.util,json\nr={}\nfor name in "+repr(list(modules))+":\n try:\n  if name=='g2p_en':\n   if importlib.util.find_spec(name) is None:raise ImportError(name)\n  else:importlib.import_module(name)\n  r[name]=True\n except Exception as e:r[name]=str(e)\nprint(json.dumps(r))"
        from pipeline import hidden_process
        try:
            p=subprocess.run([str(python),'-I','-X','utf8','-c',script],capture_output=True,text=True,encoding='utf-8',env=self.env(),timeout=90,**hidden_process())
            if p.returncode:return {'missing':list(modules),'error':p.stderr[-1200:]}
            result=json.loads(p.stdout.strip().splitlines()[-1]);return {'missing':[k for k,v in result.items() if v is not True],'details':result}
        except (ValueError,OSError,subprocess.TimeoutExpired) as exc:return {'missing':list(modules),'error':str(exc)}
    def local_wheels(self,requirements):
        """Use operator-provided wheels before any network source."""
        directory=self.work/'wheels'
        if not directory.is_dir():return []
        available=list(directory.glob('*.whl'));selected=[]
        for requirement in requirements:
            name,_,version=requirement.partition('==')
            canonical=name.lower().replace('_','-')
            matches=[]
            for wheel in available:
                parts=wheel.stem.lower().replace('_','-').split('-')
                if len(parts)<2 or parts[0]!=canonical:continue
                if version and not parts[1].startswith(version):continue
                matches.append(wheel)
            if not matches:return []
            selected.append(max(matches,key=lambda path:path.stat().st_size))
        return selected
    def pip(self,python,requirements,index=None,target=None,find_links=None,no_deps=False,timeout=1800):
        python=inside(self.root,python)
        if not requirements:return
        # Prefetch large official wheels with visible byte progress and a full
        # publisher hash. Pip then installs local verified files instead of
        # waiting silently on a multi-GB response with no recoverable partial.
        if not self.offline and index and index.startswith('https://download.pytorch.org/whl/cu') and not find_links:
            try:
                requirements=self.torch_wheels(python,requirements,index)
            except (OSError,ValueError) as error:
                self.progress('独立下载解析未完成，尝试原官方 pip 通道：'+str(error))
        asset=CATALOG['pip'];wheel=self.download(asset['url'],'pip-24.3.1.whl',asset['sha256'])
        # The wheel is run directly, so a broken/missing pip does not block repair.
        code="import sys;sys.path.insert(0,"+repr(str(wheel))+");from pip._internal.cli.main import main;raise SystemExit(main())"
        base=[python,'-I','-X','utf8','-c',code,'install','--disable-pip-version-check','--no-user','--prefer-binary','--cache-dir',str(self.work/'pip-cache')]
        if target:base+=['--target',str(inside(self.root,target))]
        sources=[]
        if self.offline:
            sources=[(str(self.work/'wheels'),None)]
        elif find_links:
            sources=[(link,None) for link in find_links]
        else:
            candidates=[index] if index else list(CATALOG.get('pipIndexes') or ['https://pypi.org/simple'])
            sources=[(None,candidate) for candidate in candidates]
        failure=None
        for link,candidate in sources:
            command=list(base)
            if link:command+=['--find-links',link,'--no-index']
            else:command+=['--index-url',candidate]
            try:
                # A distribution can still have metadata while its actual module is
                # missing/corrupt. Repair the failed roots, then resolve missing deps.
                # Do not force-upgrade otherwise healthy transitive dependencies.
                self.run(command+['--force-reinstall','--no-deps',*requirements],timeout=timeout)
                if not no_deps and not target:self.run(command+[*requirements],timeout=timeout)
                return
            except RuntimeError as exc:
                failure=exc;self.progress('依赖源不可用，尝试下一个：'+str(link or candidate))
        raise RuntimeError('依赖安装失败：'+str(failure))
    def torch_wheels(self,python,requirements,index):
        from torch_download import wheel
        from resource_download import download
        tag=subprocess.run([str(python),'-I','-c','import sys;print("cp"+str(sys.version_info[0])+str(sys.version_info[1]))'],capture_output=True,text=True,timeout=15,**__import__('pipeline').hidden_process()).stdout.strip()
        result=[]
        for requirement in requirements:
            self.progress('正在匹配 '+requirement+' · '+tag+' · Windows x64')
            selected=wheel(index,requirement,tag)
            target=inside(self.root,self.work/'wheels'/selected['filename'])
            def report(**row):
                self.progress('正在下载 '+selected['filename']+' · %.1f / %.1f MB · %.2f MB/s'%(row['downloadedBytes']/1e6,row['totalBytes']/1e6,row['bytesPerSecond']/1e6),**row)
            download(selected['url'],target,selected['sha256'],progress=report)
            result.append(str(target))
        return result
    def system(self):
        """Collect actionable host facts without depending on the TTS runtime."""
        result={'windows':sys.platform=='win32','architecture':platform.machine(),'pythonBits':__import__('struct').calcsize('P')*8}
        if sys.platform=='win32':
            result['windowsVersion']=platform.platform()
            try:
                probe=subprocess.run(['nvidia-smi','--query-gpu=name,driver_version,memory.total','--format=csv,noheader'],capture_output=True,text=True,encoding='utf-8',timeout=15,**__import__('pipeline').hidden_process())
                result['nvidiaSmi']=probe.stdout.strip() if probe.returncode==0 else ''
                if probe.returncode:result['nvidiaError']=(probe.stderr or probe.stdout).strip()[-1000:]
            except (OSError,subprocess.TimeoutExpired) as exc:result['nvidiaError']=str(exc)
        return result
    @staticmethod
    def guidance(error):
        text=str(error);low=text.lower()
        if 'no kernel image' in low or 'not compatible with the current pytorch' in low or 'sm_' in low and 'not compatible' in low:
            return '当前 PyTorch 不支持这张显卡架构。请再次运行补齐；程序会改用 CUDA 12.8 兼容运行时。若仍失败，请先更新 NVIDIA 驱动。'
        if 'cuda out of memory' in low or 'out of memory' in low:
            return '显存不足。关闭占用显卡的软件后重试；不要同时加载多个 TTS 引擎。'
        if 'cuda driver' in low or 'torch.cuda.is_available' in low or '需要 nvidia' in text.lower() or 'cuda' in low and ('false' in low or 'not found' in low):
            return '没有可用的 NVIDIA CUDA 驱动。请安装或更新 NVIDIA 官方显卡驱动，重启电脑后再补齐。'
        if 'dll load failed' in low or 'winerror 126' in low or 'vcruntime' in low or 'msvcp' in low:
            return 'Windows 原生运行库加载失败。请安装 Microsoft Visual C++ 2015–2022 x64 运行库并重启。'
        if '参考' in text or 'reference' in low:
            return '参考音频或逐字台词不可用。请在声音设置中重新选择自己的参考音频并核对台词。'
        if 'timed out' in low or '超时' in text:
            return '模型加载超时。关闭占用显卡的软件后重试，并检查杀毒软件是否正在扫描模型文件。'
        return '打开安装日志和工作进程日志查看原始错误；修复后再次运行补齐。'
    def smoke(self,selected):
        """Load the selected model(s) and create a validated WAV.

        Import-only checks used to produce false success on a new PC.  This is
        deliberately part of repair completion, not an optional diagnostic.
        """
        cfg=self.store.get();base=copy.deepcopy(cfg['voice']);results=[]
        directory=inside(self.root,self.work/'smoke');directory.mkdir(parents=True,exist_ok=True)
        plans=[]
        for engine in selected:
            if engine=='cosyvoice':plans.append(('cosyvoice','cosyvoice3'))
            else:plans.extend(('gpt-sovits',variant) for variant in ('v5turbo','proplus'))
        for engine,variant in plans:
            label=engine+'/'+variant;target=directory/(engine+'-'+variant+'.wav')
            target.unlink(missing_ok=True);voice=copy.deepcopy(base);voice.update(engine=engine,gptVariant=variant,advancedEnabled=False)
            started=time.monotonic();pipeline=None
            self.progress('正在实际加载并生成验证音频：'+label,engine=engine,variant=variant)
            try:
                missing=self.store.missing(voice)
                if missing:raise RuntimeError('真实合成仍缺少：'+'、'.join(missing))
                from pipeline import Pipeline
                pipeline=Pipeline(self.store);details=pipeline.synthesize('环境补齐验证成功。',voice,target,lambda:None)
                from synthesis_recovery import validate_audio
                rate,duration=validate_audio(target)
                results.append({'id':label,'ok':True,'audio':str(target),'sampleRate':rate,'duration':duration,'seconds':round(time.monotonic()-started,2),'gpu':pipeline.gpu,'attempts':details.get('attempts',1)})
            except Exception as exc:
                results.append({'id':label,'ok':False,'error':str(exc),'guidance':self.guidance(exc),'seconds':round(time.monotonic()-started,2),'logs':[str(self.log),str(self.root/'data/logs'),str(self.root/'data/cache')]})
            finally:
                if pipeline and pipeline.adapter:pipeline.adapter.close()
        return results
    @staticmethod
    def driver_version(system):
        text=str((system or {}).get('nvidiaSmi') or '')
        parts=[part.strip() for part in text.split(',')]
        return parts[1] if len(parts)>1 else ''
    @staticmethod
    def driver_supports_cuda128(version):
        try:numbers=tuple(int(part) for part in str(version).split('.')[:2])
        except (TypeError,ValueError):return None
        return len(numbers)==2 and numbers>=(570,26)
    def install_compat_torch(self,python):
        profile=CATALOG.get('cuda128Compatibility') or {}
        requirements=list(profile.get('torch') or [])
        if not requirements:raise RuntimeError('没有可用的显卡兼容运行时配置')
        driver=self.driver_version(self.system())
        supported=self.driver_supports_cuda128(driver)
        if supported is False:
            raise RuntimeError('当前 NVIDIA 驱动 %s 不支持 CUDA 12.8（需要 570.26 或更高）。请先更新显卡驱动后重试；本次没有下载 3.2 GB 兼容运行时。'%driver)
        timeout=int(profile.get('timeoutSeconds',14400))
        runtime_root=Path(python).resolve().parent.parent
        shadow=runtime_root/'gpu-packages'
        torchvision=profile.get('torchvision')
        # A bundled GPT environment keeps torchvision in gpu-packages and that
        # directory precedes site-packages. Install the matching torchvision
        # whenever the engine imports it, or torch 2.7 sees torchvision 0.21
        # and transformers fails with "torchvision::nms does not exist".
        needs_torchvision=bool((shadow/'torchvision').is_dir()) or 'gpt-sovits' in str(python).lower()
        if needs_torchvision and torchvision:requirements.append(torchvision)
        local=self.local_wheels(requirements)
        self.progress('正在安装新显卡兼容的 CUDA 12.8 推理运行时'+('（使用本地安装包）' if local else ''))
        if local:
            self.pip(python,[str(path) for path in local],no_deps=True,timeout=timeout)
        else:
            links=list(profile.get('torchFindLinks') or [])
            if links:self.pip(python,requirements,find_links=links,no_deps=True,timeout=timeout)
            else:self.pip(python,requirements,index=profile.get('torchIndex'),no_deps=True,timeout=timeout)
        if (shadow/'torchvision').is_dir() and torchvision:
            backup=shadow/('torchvision-backup-'+uuid.uuid4().hex[:8]);backup.mkdir()
            os.replace(shadow/'torchvision',backup/'torchvision')
            for item in list(shadow.glob('torchvision-*.dist-info')):
                os.replace(item,backup/item.name)
            selected=[path for path in local if path.name.lower().startswith('torchvision-')]
            if selected:self.pip(python,[str(path) for path in selected],target=shadow,no_deps=True,timeout=timeout)
            elif links:self.pip(python,[torchvision],find_links=links,target=shadow,no_deps=True,timeout=timeout)
            else:self.pip(python,[torchvision],index=profile.get('torchIndex'),target=shadow,no_deps=True,timeout=timeout)
        self.verify_compat_runtime(python,requirements)
    def verify_compat_runtime(self,python,requirements):
        names=[requirement.partition('==')[0] for requirement in requirements]
        code="import json\nresult={}\nfor name in "+repr(names)+":\n try:\n  module=__import__(name)\n  result[name]=getattr(module,'__version__','')\n except Exception as exc:result[name]='ERROR: '+str(exc)\nprint(json.dumps(result))"
        from pipeline import hidden_process
        run=subprocess.run([str(python),'-I','-X','utf8','-c',code],capture_output=True,text=True,encoding='utf-8',timeout=180,env=self.env(),**hidden_process())
        if run.returncode:raise RuntimeError('兼容运行时安装后无法启动：'+run.stderr[-800:])
        versions=json.loads(run.stdout.strip().splitlines()[-1])
        problems=[name+'='+value for name,value in versions.items() if str(value).startswith('ERROR')]
        for requirement in requirements:
            name,_,wanted=requirement.partition('==')
            actual=str(versions.get(name,''))
            if wanted and not actual.startswith(wanted):problems.append(name+': 期望 '+wanted+'，实际 '+actual)
        if problems:raise RuntimeError('兼容运行时校验失败：'+'；'.join(problems))
    def source(self,engine,destination):
        data=CATALOG[engine];archive=self.download('https://codeload.github.com/'+data['repo']+'/zip/'+data['commit'],engine+'-'+data['commit']+'.zip')
        stage=inside(self.root,destination.with_name('.source-stage-'+uuid.uuid4().hex))
        try:
            extract(archive,stage,True)
            if destination.exists():
                backup=inside(self.root,self.work/'backups'/(destination.parent.name+'-source-'+uuid.uuid4().hex));backup.parent.mkdir(exist_ok=True);os.replace(destination,backup)
            os.replace(stage,destination)
        finally:
            if stage.exists():shutil.rmtree(stage)
    def inspect(self,engine):
        self.progress('正在检查环境依赖，不下载文件')
        cfg=self.store.get();selected=['cosyvoice','gpt-sovits'] if engine=='all' else [cfg['voice']['engine'] if engine=='current' else engine] if engine!='core' else []
        if any(e not in CATALOG for e in selected):raise ValueError('未知引擎')
        rows=[]
        core=self.store.resolve(cfg['engines']['pythonService']);core_probe=self.probe(core,CATALOG['core']['packages'])
        ffmpeg=self.store.resolve(cfg['engines']['ffmpeg']);ffmpeg_missing=bool(selected) and not ffmpeg.is_file()
        rows.append({'id':'core','ok':not core_probe['missing'] and not ffmpeg_missing,'python':str(core),'missingPackages':core_probe['missing'],'probe':core_probe,'ffmpegMissing':ffmpeg_missing})
        for name in selected:
            data=spec(name);python=self.store.resolve(cfg['engines'][data['pythonKey']]);source=self.store.resolve(cfg['engines'][data['sourceKey']]);model=self.store.resolve(cfg['engines'][data['modelKey']]);probe=self.probe(python,['torch','torchaudio',*CATALOG[name]['packages']])
            source_file=source/('cosyvoice/cli/cosyvoice.py' if name=='cosyvoice' else 'GPT_SoVITS/TTS_infer_pack/TTS.py')
            from model_integrity import REQUIRED
            missing_model=[f for f in REQUIRED if not (model/f).is_file()] if name=='cosyvoice' else [str(p.relative_to(model)) for variant in ('proplus','v5turbo') for p in gpt_required(dict(worker_config(self.store,dict(cfg['voice'],engine=name,gptVariant=variant)),model=str(model),source=str(source)))[1:] if not p.is_file()]
            if name=='cosyvoice' and (not all((model/'CosyVoice-BlankEN'/f).is_file() for f in ('config.json','tokenizer_config.json','vocab.json','merges.txt','generation_config.json')) or not list((model/'CosyVoice-BlankEN').glob('*.safetensors'))):missing_model.append('CosyVoice-BlankEN/*')
            resource_ok=True
            if name=='gpt-sovits':resource_ok=all((source.parent/'nltk_data'/section/folder).is_dir() for section,folder in [('corpora','cmudict'),('taggers','averaged_perceptron_tagger'),('taggers','averaged_perceptron_tagger_eng')])
            matcha_ok=name!='cosyvoice' or (source/'third_party/Matcha-TTS/matcha/utils/audio.py').is_file()
            rows.append({'id':name,'ok':not probe['missing'] and source_file.is_file() and not missing_model and resource_ok and matcha_ok,'python':str(python),'source':str(source),'model':str(model),'missingPackages':probe['missing'],'sourceMissing':not source_file.is_file(),'matchaMissing':not matcha_ok,'missingModelFiles':sorted(set(missing_model)),'resourcesMissing':not resource_ok,'probe':probe})
        return {'ok':all(r['ok'] for r in rows),'items':rows,'selected':selected,'systemNotice':'显卡驱动、系统 VC++／.NET 组件不能在软件文件夹里替代安装；最后深度自检会说明系统问题。','progress':str(self.work/'progress.json'),'log':str(self.log)}
    @staticmethod
    def model_patterns(name,patterns):
        patterns=[p.replace('\\','/') for p in patterns]+['LICENSE*','NOTICE*','COPYING*','README*','**/LICENSE*','**/NOTICE*','**/README*']
        folders=('chinese-hubert-base/','chinese-roberta-wwm-ext-large/') if name=='gpt-sovits' else ('CosyVoice-BlankEN/',)
        return sorted(set(x.split('/')[0]+'/*' if x.startswith(folders) else x for x in patterns))

    def download_model(self,name,python,model,patterns):
        inside(self.root,model)
        identity=json.dumps([name,CATALOG[name]['modelRevision'],str(model)],ensure_ascii=False)
        stage=inside(self.root,self.work/('model-download-'+hashlib.sha256(identity.encode()).hexdigest()[:24]))
        patterns=self.model_patterns(name,patterns)
        self.progress('正在下载 '+name+' 基础模型；只补缺失文件，可能需要数 GB')
        pip=self.download(CATALOG['pip']['url'],'pip-24.3.1.whl',CATALOG['pip']['sha256'])
        # Use the Windows trust store in this downloader process only. TLS
        # verification remains enabled, including for enterprise proxy roots.
        prefix='import sys;sys.path.insert(0,'+repr(str(pip))+');\nif sys.platform=="win32":\n from pip._vendor import truststore;truststore.inject_into_ssl()\n'
        code=prefix+'from huggingface_hub import snapshot_download;snapshot_download(repo_id='+repr(CATALOG[name]['modelRepo'])+',revision='+repr(CATALOG[name]['modelRevision'])+',local_dir='+repr(str(stage))+',allow_patterns='+repr(patterns)+',max_workers=3)'
        for attempt in range(3):
            try:self.run([python,'-I','-X','utf8','-c',code],timeout=14400);break
            except RuntimeError:
                if attempt==2:raise RuntimeError('模型下载连续失败，断点保留。请检查系统证书、网络和日志：'+str(self.log))
                self.progress('模型下载中断，保留断点重试（%d/3）'%(attempt+2));time.sleep(attempt+1)
        copied=[]
        for file in stage.rglob('*'):
            if file.is_file() and '.cache' not in file.relative_to(stage).parts:
                relative=file.relative_to(stage)
                # Keep new legal texts outside the model identity namespace.
                # A new README must not invalidate an approved voice fingerprint.
                notice=file.name.upper().startswith(('LICENSE','NOTICE','COPYING','README'))
                target=inside(self.root,model/('.agent-voice-licenses' if notice else '')/relative);target.parent.mkdir(parents=True,exist_ok=True)
                if not target.exists() or (target.is_file() and target.stat().st_size==0):
                    shutil.move(str(file),str(target));copied.append(relative.as_posix())
        from state import atomic_json
        atomic_json(model/'.agent-voice-licenses'/('download-'+uuid.uuid4().hex+'.json'),
            {'repo':CATALOG[name]['modelRepo'],'revision':CATALOG[name]['modelRevision'],'downloadedFiles':copied,
             'scope':'Only this download, not provenance or permission for all existing model files'})
        shutil.rmtree(stage)

    def prepare_resources(self,engine):
        """First-run acquisition only: source + model, never a Torch install."""
        if self.offline:raise ValueError('严格离线已开启，请导入本地引擎包或定位已有引擎')
        with OperationLock(self.root):
            before=self.inspect(engine);updates={};rows=[]
            for row in before['items']:
                name=row['id']
                if name=='core':continue
                data=spec(name);home=self.root/'tts-engines'/name;source=Path(row['source']);model=Path(row['model']);source_replaced=False
                if row['sourceMissing'] or (row['matchaMissing'] and self.root not in source.resolve().parents):
                    source=inside(self.root,source) if self.root in source.resolve().parents else home/'source'
                    self.progress('正在下载 '+name+' 引擎源码');self.source(name,source);source_replaced=True
                if name=='cosyvoice' and (row['matchaMissing'] or (source_replaced and not (source/'third_party/Matcha-TTS/matcha/utils/audio.py').is_file())):
                    inside(self.root,source);entry=CATALOG[name]
                    archive=self.download('https://codeload.github.com/'+entry['matchaRepo']+'/zip/'+entry['matchaCommit'],'matcha-'+entry['matchaCommit']+'.zip')
                    destination=source/'third_party/Matcha-TTS'
                    if destination.exists():
                        backup=self.work/'backups'/('matcha-'+uuid.uuid4().hex);backup.parent.mkdir(exist_ok=True);os.replace(destination,backup)
                    extract(archive,destination,True)
                if row['missingModelFiles']:
                    patterns=row['missingModelFiles']
                    if self.root not in model.resolve().parents:
                        model=home/'models'/('Fun-CosyVoice3-0.5B' if name=='cosyvoice' else 'GPT-SoVITS')
                        if name=='cosyvoice':
                            from model_integrity import REQUIRED
                            patterns=[*REQUIRED,'CosyVoice-BlankEN/*']
                        else:patterns=[str(p.relative_to(model)) for v in ('proplus','v5turbo') for p in gpt_required({'source':str(source),'model':str(model),'variant':v})[1:]]
                    python=self.store.resolve(self.store.get()['engines']['pythonService'])
                    if not python.is_file() or self.root not in python.resolve().parents:python=self.python(self.root/'runtime/core')
                    if self.probe(python,['huggingface_hub'])['missing']:self.pip(python,[CATALOG['gpt-sovits']['packages']['huggingface_hub']])
                    self.download_model(name,python,model,patterns)
                updates.update({data['sourceKey']:self.store.portable(source),data['modelKey']:self.store.portable(model)})
                rows.append({'engine':name,'source':str(source),'model':str(model)})
            if updates:self.store.update({'engines':updates})
            after=self.inspect(engine)
            for row in after['items']:
                if row['id']!='core' and (row['sourceMissing'] or row['matchaMissing'] or row['missingModelFiles']):raise RuntimeError('引擎文件仍有缺项，请重试下载或导入本地引擎包：'+row['id'])
            return {'ok':True,'verifiedUsable':False,'resources':rows,'message':'引擎文件已准备，下一步检查运行环境；仍需实际试听'}

    def repair(self,engine,verify=True,dependencies_only=False):
        with OperationLock(self.root):
            if self.log.is_file() and self.log.stat().st_size>10*1024**2:os.replace(self.log,self.log.with_suffix('.previous.log'))
            before=self.inspect(engine);cfg=self.store.get();updates={}
            if dependencies_only and any(r['id']!='core' and (r['sourceMissing'] or r['matchaMissing'] or r['missingModelFiles']) for r in before['items']):raise ValueError('引擎源码或模型尚未完整，请先返回上一步下载引擎或导入本地引擎包')
            atomic_json(self.work/('settings-before-'+time.strftime('%Y%m%d-%H%M%S')+'.json'),cfg)
            for row in before['items']:
                if row['ok']:continue
                name=row['id'];self.progress('正在补齐 '+name)
                if self.offline and name!='core' and any(row.get(field) for field in ('sourceMissing','matchaMissing','missingModelFiles','resourcesMissing')):
                    raise ValueError('离线模式缺少 '+name+' 引擎源码、基础模型或发音资源，请先导入对应 TTS 包')
                if name=='core':
                    candidate=Path(row['python']);python=candidate if candidate.is_file() and self.root in candidate.resolve().parents else self.python(self.root/'runtime/core')
                    missing=self.probe(python,CATALOG['core']['packages'])['missing'];self.pip(python,[CATALOG['core']['packages'][m] for m in missing]);updates['pythonService']=self.store.portable(python)
                    if row.get('ffmpegMissing'):
                        asset=CATALOG['ffmpeg'];binary=self.download(asset['url'],'ffmpeg.exe',asset['sha256']);target=inside(self.root,self.root/'tools/ffmpeg.exe');target.parent.mkdir(exist_ok=True);shutil.copy2(binary,target);updates['ffmpeg']=self.store.portable(target)
                    continue
                data=spec(name);home=self.root/'tts-engines'/name;home.mkdir(parents=True,exist_ok=True)
                python=Path(row['python'])
                if not python.is_file() or self.root not in python.resolve().parents:python=self.python(home/'runtime-managed')
                source_replaced=False
                if row['sourceMissing'] or (row['matchaMissing'] and self.root not in Path(row['source']).resolve().parents):
                    source=inside(self.root,Path(row['source'])) if self.root in Path(row['source']).resolve().parents else home/'source';self.source(name,source);source_replaced=True
                else:source=Path(row['source'])
                if name=='cosyvoice' and (row['matchaMissing'] or (source_replaced and not (source/'third_party/Matcha-TTS/matcha/utils/audio.py').is_file())):
                    inside(self.root,source);entry=CATALOG[name];archive=self.download('https://codeload.github.com/'+entry['matchaRepo']+'/zip/'+entry['matchaCommit'],'matcha-'+entry['matchaCommit']+'.zip')
                    destination=source/'third_party/Matcha-TTS'
                    if destination.exists():
                        backup=self.work/'backups'/('matcha-'+uuid.uuid4().hex);backup.parent.mkdir(exist_ok=True);os.replace(destination,backup)
                    extract(archive,destination,True)
                probe=self.probe(python,['torch','torchaudio',*CATALOG[name]['packages']])
                if any(m in probe['missing'] for m in ('torch','torchaudio')):self.pip(python,CATALOG[name]['torch'],CATALOG[name]['torchIndex'])
                self.pip(python,[CATALOG[name]['packages'][m] for m in probe['missing'] if m in CATALOG[name]['packages']])
                if name=='gpt-sovits' and row['resourcesMissing']:
                    inside(self.root,source.parent/'nltk_data');self.run([python,'-X','utf8',self.root/'tools/prepare_gpt_resources.py','--source',source])
                model=Path(row['model'])
                if row['missingModelFiles']:
                    if self.root not in model.resolve().parents:model=home/'models'/('Fun-CosyVoice3-0.5B' if name=='cosyvoice' else 'GPT-SoVITS')
                    inside(self.root,model);self.progress('正在下载缺少的 '+name+' 基础模型（可能需要数 GB）')
                    patterns=row['missingModelFiles']
                    if str(model)!=row['model']:
                        if name=='cosyvoice':
                            from model_integrity import REQUIRED
                            patterns=[*REQUIRED,'CosyVoice-BlankEN/*']
                        else:patterns=[str(p.relative_to(model)) for variant in ('proplus','v5turbo') for p in gpt_required({'source':str(source),'model':str(model),'variant':variant})[1:]]
                    self.download_model(name,python,model,patterns)
                updates.update({data['pythonKey']:self.store.portable(python),data['sourceKey']:self.store.portable(source),data['modelKey']:self.store.portable(model)})
            if updates:self.store.update({'engines':updates})
            result=self.inspect(engine)
            result['system']=self.system()
            if verify and result['ok'] and result['selected']:
                self.progress('正在验证推理源码与 CUDA')
                from diagnostics import inspect_installation
                deep=inspect_installation(self.store,deep=True)
                result['runtimeChecks']=[c for c in deep['checks'] if c['name']=='CUDA 与推理依赖' and c['section'].split('/')[0] in result['selected']]
                result['ok']=all(c['ok'] for c in result['runtimeChecks']) and bool(result['runtimeChecks'])
            if verify and result['ok'] and result['selected']:
                result['synthesisChecks']=self.smoke(result['selected'])
                unsupported=[x for x in result['synthesisChecks'] if not x['ok'] and any(marker in x.get('error','').lower() for marker in ('no kernel image','not compatible with the current pytorch','torchvision::nms','operator torchvision'))]
                if unsupported:
                    # Blackwell-class cards can pass torch.cuda.is_available()
                    # while older bundled kernels still fail at first inference.
                    for name in result['selected']:
                        data=spec(name);self.install_compat_torch(self.store.resolve(self.store.get()['engines'][data['pythonKey']]))
                    result['synthesisChecks']=self.smoke(result['selected'])
                    result['compatibilityRuntimeInstalled']=True
                result['ok']=all(x['ok'] for x in result['synthesisChecks'])
            result['verifiedUsable']=bool(result.get('synthesisChecks')) and result['ok']
            atomic_json(self.work/'latest.json',result);self.progress('补齐完成' if result['ok'] else '仍有未通过项目，请查看报告和安装日志','completed' if result['ok'] else 'incomplete')
            return result

def main():
    p=argparse.ArgumentParser();p.add_argument('action',choices=['inspect','repair','resources']);p.add_argument('--root',required=True);p.add_argument('--engine',choices=['core','current','all','cosyvoice','gpt-sovits'],default='current');p.add_argument('--offline',action='store_true');p.add_argument('--prepare-only',action='store_true');p.add_argument('--dependencies-only',action='store_true');a=p.parse_args();helper=Repair(a.root,offline=a.offline)
    try:result=helper.prepare_resources(a.engine) if a.action=='resources' else helper.repair(a.engine,verify=not a.prepare_only,dependencies_only=a.dependencies_only) if a.action=='repair' else helper.inspect(a.engine)
    except Exception as exc:helper.progress(str(exc),'failed');result={'ok':False,'error':str(exc),'log':str(helper.log)}
    print(json.dumps(result,ensure_ascii=False))

if __name__=='__main__':main()
