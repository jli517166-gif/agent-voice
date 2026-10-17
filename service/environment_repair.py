"""App-local, explicit environment repair. Never installs into an external interpreter."""
import argparse,hashlib,json,os,shutil,subprocess,sys,time,urllib.request,uuid,zipfile
from pathlib import Path
from state import Store,atomic_json
from operation_lock import OperationLock
from engine_registry import spec,gpt_required,worker_config

CATALOG=json.loads(Path(__file__).with_name('environment-catalog.json').read_text(encoding='utf-8'))
MAX_ARCHIVE_BYTES=512*1024**2

def inside(root,path):
    root=Path(root).resolve();path=Path(path).resolve()
    if path==root or root not in path.parents:raise ValueError('补齐文件必须放在软件目录内部：'+str(path))
    return path

def extract(archive,destination,strip_root=False):
    destination=Path(destination).resolve();destination.mkdir(parents=True,exist_ok=True)
    with zipfile.ZipFile(archive) as source:
        if sum(i.file_size for i in source.infolist())>2*1024**3:raise ValueError('解压后资源过大')
        entries=[]
        for item in source.infolist():
            parts=item.filename.replace('\\','/').split('/')
            if strip_root:parts=parts[1:]
            if not parts or not any(parts):continue
            target=inside(destination,destination.joinpath(*parts))
            if item.external_attr>>16&0o170000==0o120000:raise ValueError('压缩包含外部链接')
            entries.append((item,target))
        for item,target in entries:
            if item.is_dir():target.mkdir(parents=True,exist_ok=True)
            else:
                target.parent.mkdir(parents=True,exist_ok=True)
                with source.open(item) as incoming,target.open('wb') as output:shutil.copyfileobj(incoming,output)

class Repair:
    def __init__(self,root):
        self.store=Store(root);self.root=self.store.root;self.work=self.root/'data/environment-repair';self.work.mkdir(parents=True,exist_ok=True)
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
    def pip(self,python,requirements,index=None):
        python=inside(self.root,python)
        if not requirements:return
        asset=CATALOG['pip'];wheel=self.download(asset['url'],'pip-24.3.1.whl',asset['sha256'])
        # The wheel is run directly, so a broken/missing pip does not block repair.
        code="import sys;sys.path.insert(0,"+repr(str(wheel))+");from pip._internal.cli.main import main;raise SystemExit(main())"
        command=[python,'-I','-X','utf8','-c',code,'install','--disable-pip-version-check','--no-user','--prefer-binary','--cache-dir',str(self.work/'pip-cache'),'--index-url',index or 'https://pypi.org/simple',*requirements]
        # A distribution can still have metadata while its actual module is
        # missing/corrupt. Repair the failed roots, then resolve missing deps.
        # Do not force-upgrade otherwise healthy transitive dependencies.
        self.run(command[:-len(requirements)]+['--force-reinstall','--no-deps',*requirements])
        self.run(command)
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
            from tts_bundle import REQUIRED
            missing_model=[f for f in REQUIRED if not (model/f).is_file()] if name=='cosyvoice' else [str(p.relative_to(model)) for variant in ('proplus','v5turbo') for p in gpt_required(dict(worker_config(self.store,dict(cfg['voice'],engine=name,gptVariant=variant)),model=str(model),source=str(source)))[1:] if not p.is_file()]
            if name=='cosyvoice' and (not (model/'CosyVoice-BlankEN/config.json').is_file() or not list((model/'CosyVoice-BlankEN').glob('*.safetensors'))):missing_model.append('CosyVoice-BlankEN/*')
            resource_ok=True
            if name=='gpt-sovits':resource_ok=all((source.parent/'nltk_data'/section/folder).is_dir() for section,folder in [('corpora','cmudict'),('taggers','averaged_perceptron_tagger'),('taggers','averaged_perceptron_tagger_eng')])
            matcha_ok=name!='cosyvoice' or (source/'third_party/Matcha-TTS/matcha/utils/audio.py').is_file()
            rows.append({'id':name,'ok':not probe['missing'] and source_file.is_file() and not missing_model and resource_ok and matcha_ok,'python':str(python),'source':str(source),'model':str(model),'missingPackages':probe['missing'],'sourceMissing':not source_file.is_file(),'matchaMissing':not matcha_ok,'missingModelFiles':sorted(set(missing_model)),'resourcesMissing':not resource_ok,'probe':probe})
        return {'ok':all(r['ok'] for r in rows),'items':rows,'selected':selected,'systemNotice':'显卡驱动、系统 VC++／.NET 组件不能在软件文件夹里替代安装；最后深度自检会说明系统问题。','progress':str(self.work/'progress.json'),'log':str(self.log)}
    def repair(self,engine):
        with OperationLock(self.root):
            if self.log.is_file() and self.log.stat().st_size>10*1024**2:os.replace(self.log,self.log.with_suffix('.previous.log'))
            before=self.inspect(engine);cfg=self.store.get();updates={}
            atomic_json(self.work/('settings-before-'+time.strftime('%Y%m%d-%H%M%S')+'.json'),cfg)
            for row in before['items']:
                if row['ok']:continue
                name=row['id'];self.progress('正在补齐 '+name)
                if name=='core':
                    candidate=Path(row['python']);python=candidate if candidate.is_file() and self.root in candidate.resolve().parents else self.python(self.root/'runtime/core')
                    missing=self.probe(python,CATALOG['core']['packages'])['missing'];self.pip(python,[CATALOG['core']['packages'][m] for m in missing]);updates['pythonService']=self.store.portable(python)
                    if row.get('ffmpegMissing'):
                        asset=CATALOG['ffmpeg'];binary=self.download(asset['url'],'ffmpeg.exe',asset['sha256']);target=inside(self.root,self.root/'tools/ffmpeg.exe');target.parent.mkdir(exist_ok=True);shutil.copy2(binary,target);updates['ffmpeg']=self.store.portable(target)
                    continue
                data=spec(name);home=self.root/'tts-engines'/name;home.mkdir(parents=True,exist_ok=True)
                python=Path(row['python'])
                if not python.is_file() or self.root not in python.resolve().parents:python=self.python(home/'runtime-managed')
                if row['sourceMissing'] or (row['matchaMissing'] and self.root not in Path(row['source']).resolve().parents):
                    source=inside(self.root,Path(row['source'])) if self.root in Path(row['source']).resolve().parents else home/'source';self.source(name,source)
                else:source=Path(row['source'])
                if name=='cosyvoice' and row['matchaMissing']:
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
                    patterns=row['missingModelFiles'];stage=inside(self.root,self.work/('model-download-'+uuid.uuid4().hex))
                    if str(model)!=row['model']:
                        if name=='cosyvoice':
                            from tts_bundle import REQUIRED
                            patterns=[*REQUIRED,'CosyVoice-BlankEN/*']
                        else:patterns=[str(p.relative_to(model)) for variant in ('proplus','v5turbo') for p in gpt_required({'source':str(source),'model':str(model),'variant':variant})[1:]]
                    if name=='gpt-sovits':patterns=sorted(set(x.split('/')[0]+'/*' if x.startswith(('chinese-hubert-base/','chinese-roberta-wwm-ext-large/')) else x for x in patterns))
                    code='from huggingface_hub import snapshot_download;snapshot_download(repo_id='+repr(CATALOG[name]['modelRepo'])+',revision='+repr(CATALOG[name]['modelRevision'])+',local_dir='+repr(str(stage))+',allow_patterns='+repr(patterns)+')'
                    self.run([python,'-I','-X','utf8','-c',code],timeout=14400)
                    for file in stage.rglob('*'):
                        if file.is_file() and '.cache' not in file.relative_to(stage).parts:
                            target=inside(self.root,model/file.relative_to(stage));target.parent.mkdir(parents=True,exist_ok=True)
                            if not target.exists():shutil.move(str(file),str(target))
                    shutil.rmtree(stage)
                updates.update({data['pythonKey']:self.store.portable(python),data['sourceKey']:self.store.portable(source),data['modelKey']:self.store.portable(model)})
            if updates:self.store.update({'engines':updates})
            result=self.inspect(engine)
            if result['ok'] and result['selected']:
                self.progress('正在验证推理源码与 CUDA，不加载音色权重')
                from diagnostics import inspect_installation
                deep=inspect_installation(self.store,deep=True)
                result['runtimeChecks']=[c for c in deep['checks'] if c['name']=='CUDA 与推理依赖' and c['section'].split('/')[0] in result['selected']]
                result['ok']=all(c['ok'] for c in result['runtimeChecks']) and bool(result['runtimeChecks'])
            atomic_json(self.work/'latest.json',result);self.progress('补齐完成' if result['ok'] else '仍有未通过项目，请查看报告和安装日志','completed' if result['ok'] else 'incomplete')
            return result

def main():
    p=argparse.ArgumentParser();p.add_argument('action',choices=['inspect','repair']);p.add_argument('--root',required=True);p.add_argument('--engine',choices=['core','current','all','cosyvoice','gpt-sovits'],default='current');a=p.parse_args();helper=Repair(a.root)
    try:result=helper.repair(a.engine) if a.action=='repair' else helper.inspect(a.engine)
    except Exception as exc:helper.progress(str(exc),'failed');result={'ok':False,'error':str(exc),'log':str(helper.log)}
    print(json.dumps(result,ensure_ascii=False))

if __name__=='__main__':main()
