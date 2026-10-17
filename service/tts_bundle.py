"""Portable and lightweight, engine-labelled TTS voice packages."""
import copy
import hashlib
import importlib.metadata
import json
import os
from pathlib import Path, PurePosixPath
import re
import shutil
import subprocess
import sys
import uuid
import zipfile

from state import Store, validate_voice
from config_bundle import assert_offline, digest, remove_owned
from engine_registry import spec, CATALOG
from operation_lock import offline_operation

ENGINE_MODEL_TYPES={(s['id'],v['packageType']) for s in CATALOG['engines'] for v in s['variants']}

EXTENSION = '.cosyvoice.ttsmodel'
EXTENSIONS = {'cosyvoice':EXTENSION,'gpt-sovits':'.gpt-sovits.ttsmodel'}
FIELDS = ('engine','speed','style','referenceText','instruction','leadingSilence','volume','advancedEnabled','assetScope')
REQUIRED = ('cosyvoice3.yaml','llm.pt','flow.pt','hift.pt','campplus.onnx','speech_tokenizer_v3.onnx')
MODEL_SUFFIXES = {'.pt','.onnx','.yaml','.yml','.json','.safetensors','.bin','.txt','.model','.tiktoken','.md','.vocab'}
GPT_MODEL_SUFFIXES = MODEL_SUFFIXES | {'.pth','.ckpt'}
PACKAGE_MODES = ('lightweight','full','trained','trained-full')
MAX_BYTES = 64 * 1024**3
SOURCE_SUFFIXES = {'.py','.yaml','.yml','.json','.toml','.txt','.md'}
RUNTIME_PACKAGES = ('torch','torchaudio','numpy','hyperpyyaml','onnxruntime-gpu','onnxruntime','transformers','diffusers','soundfile','librosa')


def gpt_sources(directory, variant):
    from local_tts import required_files
    directory=Path(directory).resolve()
    required=required_files({'engine':'gpt-sovits','model':str(directory),'source':'.','variant':variant})[1:]
    for file in required:
        if not file.is_file():raise ValueError('GPT-SoVITS 模型缺少文件：'+str(file))
    files=list(required)
    for name in ('chinese-hubert-base','chinese-roberta-wwm-ext-large'):
        files.extend((directory/name).rglob('*'))
    sources={}
    for file in sorted(set(files)):
        relative=file.relative_to(directory)
        if any(p.startswith('.') for p in relative.parts):continue
        if file.is_symlink() or directory not in file.resolve().parents:raise ValueError('GPT 模型目录含外部链接')
        if file.is_file() and file.suffix.lower() in GPT_MODEL_SUFFIXES:sources[relative.as_posix()]=file
    return dict(sorted(sources.items()))


def model_ready(directory, engine='cosyvoice', variant=None):
    if engine=='gpt-sovits':
        gpt_sources(directory,variant);return
    directory = Path(directory)
    if not directory.is_dir() or not all((directory/name).is_file() for name in REQUIRED) or not (directory/'CosyVoice-BlankEN').is_dir():
        raise ValueError('请选择完整 CosyVoice 3 模型目录，包含权重、yaml、说话人／语音编码器和 CosyVoice-BlankEN 配套资源')
    import yaml
    document = yaml.compose((directory/'cosyvoice3.yaml').read_text(encoding='utf-8-sig'), Loader=yaml.SafeLoader)
    if not isinstance(document, yaml.MappingNode):
        raise ValueError('CosyVoice 模型配置格式无效')
    llm = next((node for key,node in document.value if key.value == 'llm'), None)
    if llm is None or llm.tag != '!new:cosyvoice.llm.llm.CosyVoice3LM':
        raise ValueError('模型配置与 CosyVoice 3 引擎不匹配，不能只改文件名或后缀')


def package_path(path, engine=None):
    path = Path(path).resolve()
    suffix=EXTENSIONS.get(engine) if engine else next((s for s in EXTENSIONS.values() if path.name.lower().endswith(s)),None)
    if not suffix or not path.name.lower().endswith(suffix):raise ValueError('音色包后缀须与引擎匹配：'+str(suffix or ' / '.join(EXTENSIONS.values())))
    if not path.name[:-len(suffix)].strip():raise ValueError('模型包必须填写音色名称')
    return path


def package_name(path):
    suffix=next(s for s in EXTENSIONS.values() if Path(path).name.lower().endswith(s))
    value = Path(path).name[:-len(suffix)].strip()
    if not value or len(value) > 100 or any(ord(char) < 32 for char in value):
        raise ValueError('CosyVoice 模型包名称无效')
    return value


def storage_name(value):
    invalid = '<>:"/\\|?*'
    safe = ''.join('_' if char in invalid or ord(char) < 32 else char for char in value).strip().rstrip('. ')
    return (safe or '未命名音色')[:60]


def safe_entry(value):
    if not isinstance(value, str) or '\\' in value or ':' in value:
        raise ValueError('模型包路径无效')
    path = PurePosixPath(value)
    if path.is_absolute() or '..' in path.parts or str(path) != value or not path.parts:
        raise ValueError('模型包路径越界')
    reserved = {'CON','PRN','AUX','NUL'} | {'COM'+str(i) for i in range(1,10)} | {'LPT'+str(i) for i in range(1,10)}
    if any(p.endswith((' ','.')) or p.split('.')[0].upper() in reserved or any(ord(c) < 32 for c in p) for p in path.parts):
        raise ValueError('模型包包含无效 Windows 文件名')
    return path


def model_sources(directory, engine='cosyvoice', variant=None):
    if engine=='gpt-sovits':return gpt_sources(directory,variant)
    directory = Path(directory).resolve()
    model_ready(directory)
    sources = {}
    for file in sorted(directory.rglob('*')):
        relative = file.relative_to(directory)
        if any(part.startswith('.') or part.endswith('.chunks') for part in relative.parts):
            continue
        if file.is_symlink() or (file.is_file() and directory not in file.resolve().parents):
            raise ValueError('模型目录含外部链接，请使用完整本地文件')
        if file.is_file() and file.suffix.lower() in MODEL_SUFFIXES:
            sources[relative.as_posix()] = file
    return sources


def inventory(sources):
    records = [{'path':name, 'size':file.stat().st_size, 'sha256':digest(file)} for name,file in sources.items()]
    encoded = json.dumps(records, ensure_ascii=True, sort_keys=True, separators=(',',':')).encode('utf-8')
    return records, hashlib.sha256(encoded).hexdigest()


def inventory_fingerprint(records):
    encoded = json.dumps(records, ensure_ascii=True, sort_keys=True, separators=(',',':')).encode('utf-8')
    return hashlib.sha256(encoded).hexdigest()


def source_fingerprint(store, engine, policy='legacy-bytes'):
    directory = store.resolve(store.get()['engines'][spec(engine)['sourceKey']])
    if not directory.is_dir():
        raise ValueError('缺少本地 TTS 引擎源码目录：'+str(directory))
    records=[]
    for file in sorted(directory.rglob('*')):
        if file.is_symlink():
            raise ValueError('TTS 引擎源码目录含外部链接：'+str(file))
        if file.is_file() and file.suffix.lower() in SOURCE_SUFFIXES:
            if policy=='python-ast-v1':
                if file.suffix.lower()=='.md':continue
                content=file.read_bytes()
                if file.suffix.lower()=='.py':
                    import ast,tokenize
                    try:
                        with tokenize.open(file) as stream:content=ast.dump(ast.parse(stream.read()),include_attributes=False).encode()
                    except (SyntaxError,UnicodeError):pass
                records.append({'path':file.relative_to(directory).as_posix(),'sha256':hashlib.sha256(content).hexdigest()})
            else:records.append({'path':file.relative_to(directory).as_posix(),'size':file.stat().st_size,'sha256':digest(file)})
    if not records:raise ValueError('本地 TTS 引擎源码目录为空或无法识别：'+str(directory))
    return inventory_fingerprint(records)


def runtime_descriptor(store=None, engine='cosyvoice'):
    if store is not None:
        from pipeline import hidden_process
        from runtime_paths import repair_bundled_runtime
        repair_bundled_runtime(store.root)
        code="import importlib.metadata as m,json,sys; names="+repr(RUNTIME_PACKAGES)+"; p={}; exec('for n in names:\\n try:p[n]=m.version(n)\\n except m.PackageNotFoundError:pass'); print(json.dumps({'python':'.'.join(map(str,sys.version_info[:3])),'packages':p},sort_keys=True,separators=(',',':')))"
        from engine_registry import spec
        command=[str(store.resolve(store.get()['engines'][spec(engine)['pythonKey']])),'-I','-X','utf8','-c',code]
        result=subprocess.run(command,capture_output=True,text=True,encoding='utf-8',timeout=30,**hidden_process())
        if result.returncode:raise ValueError(engine+' 运行环境无法读取，请检查本地引擎设置：'+result.stderr[-500:])
        return json.loads(result.stdout)
    packages={}
    for name in RUNTIME_PACKAGES:
        try:packages[name]=importlib.metadata.version(name)
        except importlib.metadata.PackageNotFoundError:pass
    descriptor={'python':'.'.join(map(str,sys.version_info[:3])),'packages':packages}
    return descriptor

def runtime_fingerprint(store=None, engine='cosyvoice'):
    return hashlib.sha256(json.dumps(runtime_descriptor(store,engine),sort_keys=True,separators=(',',':')).encode()).hexdigest()


def engine_model_root(store, engine):
    return store.root / 'tts-engines' / storage_name(engine) / 'models'


def matching_model(store, engine, model_type, records, fingerprint):
    if (engine,model_type) not in ENGINE_MODEL_TYPES:
        raise ValueError('当前程序尚未安装适配该 TTS 引擎的模块：' + engine + ' / ' + model_type)
    parent = engine_model_root(store, engine)
    expected = {item['path']:(item['size'],item['sha256']) for item in records}
    config=store.get()
    variant=model_type.removeprefix('gpt-sovits-') if engine=='gpt-sovits' else None
    s=spec(engine);candidates=[store.resolve(value) for value in (config['voice'].get(s['modelField']),config['engines'][s['modelKey']]) if value]
    if parent.is_dir():candidates += [parent]+sorted((p for p in parent.iterdir() if p.is_dir()),key=lambda p:p.name.casefold())
    candidates=list(dict.fromkeys(p.resolve() for p in candidates))
    for directory in candidates:
        try:
            model_ready(directory,engine,variant)
            files = model_sources(directory,engine,variant)
            if set(files) != set(expected):
                continue
            actual = []
            mismatch = False
            for name,file in files.items():
                size,sha = expected[name]
                if file.stat().st_size != size or digest(file) != sha:
                    mismatch = True
                    break
                actual.append({'path':name,'size':size,'sha256':sha})
            if not mismatch and inventory_fingerprint(actual) == fingerprint:
                return directory.resolve()
        except (ValueError, OSError):
            continue
    return None


@offline_operation
def export_model(root, target, profile_id=None, package_mode='lightweight', trained_model_dir=None):
    if package_mode not in PACKAGE_MODES:
        raise ValueError('音色包类型无效')
    store = Store(root); cfg = store.get(); voice = cfg['voice']
    if profile_id:
        matches = [item for item in cfg['profiles'] if item['id'] == profile_id]
        if len(matches) != 1:
            raise ValueError('没有找到要导出的声音配置')
        voice = matches[0]['voice']
    engine=voice['engine'];variant=voice.get('gptVariant') if engine=='gpt-sovits' else None
    entry=spec(engine);model_type=next(v['packageType'] for v in entry['variants'] if v['id']==(variant or 'cosyvoice3'))
    if engine=='gpt-sovits' and package_mode in ('trained','trained-full'):raise ValueError('GPT-SoVITS 使用轻量包或模型完整包；高级训练包仅适用于 CosyVoice')
    base_directory = store.resolve(voice[entry['modelField']] or cfg['engines'][entry['modelKey']])
    base_sources = model_sources(base_directory,engine,variant); base_records, base_fingerprint = inventory(base_sources)
    advanced=package_mode in ('trained','trained-full')
    if advanced:
        if not trained_model_dir:raise ValueError('高级训练包需要选择训练完成的 CosyVoice3 模型目录')
        directory=Path(trained_model_dir).resolve();sources=model_sources(directory);model_records,fingerprint=inventory(sources)
    else:
        directory=base_directory;sources=base_sources;model_records,fingerprint=base_records,base_fingerprint
    target = package_path(target,engine)
    if directory == target or directory in target.parents:
        raise ValueError('导出文件请保存在模型目录之外')
    reference = store.resolve(voice['reference'])
    if not reference or not reference.is_file() or not voice['referenceText'].strip():
        raise ValueError('请先选择参考音频和对应台词')
    ref_entry = 'reference/reference' + reference.suffix.lower()
    if reference.suffix.lower() not in {'.wav','.flac','.mp3','.m4a','.ogg'}:
        raise ValueError('参考音频类型不支持')
    package_sources = {ref_entry:reference}
    delta=[];removed=[]
    if package_mode == 'full' or package_mode == 'trained-full':
        package_sources.update({'model/'+name:file for name,file in sources.items()})
    elif package_mode == 'trained':
        base_by_name={item['path']:item for item in base_records};target_by_name={item['path']:item for item in model_records}
        delta=sorted(name for name,item in target_by_name.items() if name not in base_by_name or item['size']!=base_by_name[name]['size'] or item['sha256']!=base_by_name[name]['sha256'])
        removed=sorted(set(base_by_name)-set(target_by_name))
        if not delta and not removed:raise ValueError('高级训练结果与当前基础模型完全相同，没有可导出的训练权重')
        package_sources.update({'model/'+name:sources[name] for name in delta})
    if len(package_sources) > 10000 or sum(f.stat().st_size for f in package_sources.values()) > MAX_BYTES:
        raise ValueError('模型资源数量或总大小超限')
    records = [{'entry':name,'size':file.stat().st_size,'sha256':digest(file)} for name,file in package_sources.items()]
    manifest = {
        'format':'agent-voice-tts-model','version':3 if advanced else 2,'name':package_name(target),'packageMode':package_mode,
        'engine':{'id':engine,'modelType':model_type,'modelName':directory.name,'modelFingerprint':fingerprint,
                  'sourceFingerprint':source_fingerprint(store,engine,'python-ast-v1'),'runtimeFingerprint':runtime_fingerprint(store,engine)},
        'compatibility':{'sourcePolicy':'python-ast-v1','runtime':runtime_descriptor(store,engine)},
        'modelFiles':model_records,'voice':{key:voice[key] for key in (*FIELDS,'gptVariant') if key in FIELDS or engine=='gpt-sovits'},'reference':ref_entry,'files':records,
    }
    if advanced:manifest['baseModel']={'modelName':base_directory.name,'modelFingerprint':base_fingerprint,'modelFiles':base_records,'deltaFiles':delta,'removedFiles':removed}
    target.parent.mkdir(parents=True,exist_ok=True); temp=target.with_name(target.name+'.'+uuid.uuid4().hex+'.tmp')
    try:
        with zipfile.ZipFile(temp,'w',compression=zipfile.ZIP_STORED,allowZip64=True) as archive:
            archive.writestr('manifest.json',json.dumps(manifest,ensure_ascii=False,indent=2))
            for item in records:
                sha=hashlib.sha256()
                with package_sources[item['entry']].open('rb') as stream,archive.open(item['entry'],'w',force_zip64=True) as output:
                    for chunk in iter(lambda:stream.read(1024*1024),b''):
                        output.write(chunk);sha.update(chunk)
                if sha.hexdigest()!=item['sha256']:
                    raise ValueError('导出期间模型资源改变，请重新导出')
        os.replace(temp,target)
    finally:
        if temp.exists():temp.unlink()
    return {'ok':True,'name':manifest['name'],'engine':engine,'modelType':model_type,'packageMode':package_mode,
            'modelFingerprint':fingerprint,'file':str(target),'assetCount':len(records),'size':target.stat().st_size}


def validate_record(item, archive, key='entry'):
    name = str(safe_entry(item[key])); size=item['size']; sha=item['sha256']
    if isinstance(size,bool) or not isinstance(size,int) or size<0 or not isinstance(sha,str) or not re.fullmatch('[0-9a-f]{64}',sha):
        raise ValueError('资源校验记录错误')
    if key=='entry' and size!=archive.getinfo(name).file_size:
        raise ValueError('资源校验记录错误')
    return name


def normalize_manifest(manifest, fallback_name):
    if manifest.get('format')!='agent-voice-tts-model':
        raise ValueError('不是 Agent 语音 TTS 音色包')
    if manifest.get('version')==1:
        if manifest.get('engine')!='cosyvoice' or manifest.get('modelType')!='cosyvoice3':
            raise ValueError('引擎标识或版本不匹配，当前程序仅接收 CosyVoice 3 模型包')
        model_records=[{'path':str(item['entry'])[6:],'size':item['size'],'sha256':item['sha256']} for item in manifest.get('files',[]) if str(item.get('entry','')).startswith('model/')]
        manifest=copy.deepcopy(manifest);manifest.update(version=2,packageMode='full',engine={
            'id':'cosyvoice','modelType':'cosyvoice3','modelName':fallback_name,'modelFingerprint':inventory_fingerprint(model_records),
            'sourceFingerprint':'legacy-unverified','runtimeFingerprint':'legacy-unverified'},modelFiles=model_records)
    if manifest.get('version') not in (2,3) or manifest.get('packageMode') not in PACKAGE_MODES:
        raise ValueError('TTS 音色包版本或类型不受支持')
    if manifest.get('version')==2 and manifest.get('packageMode') not in ('lightweight','full'):
        raise ValueError('旧版音色包不能声明高级训练类型')
    if manifest.get('version')==3 and manifest.get('packageMode') not in ('trained','trained-full'):
        raise ValueError('高级音色包类型无效')
    engine=manifest.get('engine')
    compatibility=manifest.get('compatibility')
    if compatibility is not None and (not isinstance(compatibility,dict) or compatibility.get('sourcePolicy')!='python-ast-v1' or not isinstance(compatibility.get('runtime'),dict)):
        raise ValueError('音色包的兼容规则无效')
    if not isinstance(engine,dict) or set(engine)!={'id','modelType','modelName','modelFingerprint','sourceFingerprint','runtimeFingerprint'}:
        raise ValueError('TTS 引擎匹配信息不完整')
    if (engine['id'],engine['modelType']) not in ENGINE_MODEL_TYPES or not re.fullmatch('[0-9a-f]{64}',str(engine['modelFingerprint'])):
        raise ValueError('引擎标识、模型类型或模型指纹不匹配')
    if engine['id']=='gpt-sovits' and manifest['packageMode'] not in ('lightweight','full'):raise ValueError('GPT 音色包类型无效')
    if not isinstance(engine['modelName'],str) or not engine['modelName'].strip() or len(engine['modelName'])>100:
        raise ValueError('基础模型名称无效')
    for key in ('sourceFingerprint','runtimeFingerprint'):
        if engine[key]!='legacy-unverified' and not re.fullmatch('[0-9a-f]{64}',str(engine[key])):raise ValueError('TTS 引擎源码或运行环境指纹无效')
    if manifest.get('version')==3:
        base=manifest.get('baseModel')
        if not isinstance(base,dict) or set(base)!={'modelName','modelFingerprint','modelFiles','deltaFiles','removedFiles'}:raise ValueError('高级训练基础模型信息不完整')
        if not isinstance(base['modelName'],str) or not re.fullmatch('[0-9a-f]{64}',str(base['modelFingerprint'])):raise ValueError('高级训练基础模型信息无效')
        if not isinstance(base['modelFiles'],list) or not isinstance(base['deltaFiles'],list) or not isinstance(base['removedFiles'],list):raise ValueError('高级训练差异清单无效')
    return manifest


@offline_operation
def import_model(root, source):
    store=Store(root);assert_offline(store);source=package_path(source);fallback_name=package_name(source)
    voice_parent=store.root/'voice-model-packages'/'已导入';voice_parent.mkdir(parents=True,exist_ok=True)
    ident=uuid.uuid4().hex;voice_stage=voice_parent/('.staging-'+ident);voice_final=voice_parent/(storage_name(fallback_name)+'-'+ident[:8])
    model_stage=None;model_final=None;created_voice=False;created_model=False
    try:
        with zipfile.ZipFile(source) as archive:
            entries=archive.infolist();names=[i.filename for i in entries];total=sum(i.file_size for i in entries)
            if len(entries)>10001 or len({n.casefold() for n in names})!=len(names) or total>MAX_BYTES:
                raise ValueError('模型包重复条目、数量或大小无效')
            info=archive.getinfo('manifest.json')
            if info.file_size>8*1024**2:raise ValueError('模型清单过大')
            manifest=normalize_manifest(json.loads(archive.read(info).decode('utf-8-sig')),fallback_name)
            display_name=manifest.get('name',fallback_name)
            if not isinstance(display_name,str) or not display_name.strip() or len(display_name)>100 or any(ord(char)<32 for char in display_name):raise ValueError('模型包音色名称无效')
            display_name=display_name.strip();voice_final=voice_parent/(storage_name(display_name)+'-'+ident[:8])
            voice=manifest['voice']
            engine=manifest['engine'];is_gpt=engine['id']=='gpt-sovits';variant=engine['modelType'].removeprefix('gpt-sovits-') if is_gpt else None
            allowed_model_suffixes=GPT_MODEL_SUFFIXES if is_gpt else MODEL_SUFFIXES
            package_path(source,engine['id'])
            expected_fields=set(FIELDS)|({'gptVariant'} if is_gpt else set())
            if not isinstance(voice,dict) or set(voice)!=expected_fields or voice['engine']!=engine['id']:raise ValueError('模型包包含非本地声音设置或错误引擎')
            if is_gpt and voice['gptVariant']!=variant:raise ValueError('GPT 版本与模型类型不匹配')
            reference=str(safe_entry(manifest['reference']))
            if not reference.startswith('reference/') or PurePosixPath(reference).suffix.lower() not in {'.wav','.flac','.mp3','.m4a','.ogg'}:raise ValueError('参考录音路径无效')
            model_records=manifest['modelFiles']
            if not isinstance(model_records,list) or not model_records or len(model_records)>10000:raise ValueError('基础模型指纹清单无效')
            model_names=set()
            for item in model_records:
                path=validate_record(item,archive,'path')
                if path in model_names or PurePosixPath(path).suffix.lower() not in allowed_model_suffixes:raise ValueError('基础模型指纹清单重复或包含不支持的资源')
                model_names.add(path)
            if inventory_fingerprint(model_records)!=manifest['engine']['modelFingerprint']:raise ValueError('基础模型指纹清单已损坏')
            records=manifest['files']
            if not isinstance(records,list) or len(records)>10000:raise ValueError('资源清单无效')
            allowed={'manifest.json'}
            for item in records:
                name=validate_record(item,archive)
                if name in allowed or not (name.startswith('model/') or name==reference):raise ValueError('资源清单重复或包含非模型资源')
                if name.startswith('model/') and PurePosixPath(name).suffix.lower() not in allowed_model_suffixes:raise ValueError('模型包含不支持的资源类型')
                allowed.add(name)
            if set(names)!=allowed or reference not in allowed:raise ValueError('模型包包含未登记资源')
            reference_record=next(i for i in records if i['entry']==reference)
            if reference_record['size']>40*1024**2:raise ValueError('参考录音超过 40 MB，不能导入')
            identity=inventory_fingerprint({'engine':engine['id'],'modelType':engine['modelType'],'model':engine['modelFingerprint'],'voice':voice,'referenceSha256':reference_record['sha256']})
            voice_final=voice_parent/(storage_name(engine['id'])+'-'+identity[:20])
            packaged_models={name[6:] for name in allowed if name.startswith('model/')}
            if manifest['packageMode']=='lightweight' and packaged_models:raise ValueError('轻量包不得携带基础模型权重')
            if manifest['packageMode']=='full' and packaged_models!=model_names:raise ValueError('完整便携包的模型资源与指纹清单不一致')
            if manifest['packageMode']=='trained-full' and packaged_models!=model_names:raise ValueError('高级完整模型包的资源与指纹清单不一致')
            base_directory=None
            if manifest['packageMode'] in ('trained','trained-full'):
                base=manifest['baseModel'];base_records=base['modelFiles'];base_names=set()
                if not base_records or len(base_records)>10000:raise ValueError('高级训练基础模型清单无效')
                for item in base_records:
                    path=validate_record(item,archive,'path')
                    if path in base_names or PurePosixPath(path).suffix.lower() not in MODEL_SUFFIXES:raise ValueError('高级训练基础模型清单重复或无效')
                    base_names.add(path)
                if inventory_fingerprint(base_records)!=base['modelFingerprint']:raise ValueError('高级训练基础模型清单已损坏')
                delta=set(base['deltaFiles']);removed=set(base['removedFiles'])
                if (len(delta)!=len(base['deltaFiles']) or len(removed)!=len(base['removedFiles']) or
                    any(str(safe_entry(x))!=x for x in delta|removed) or delta&removed or
                    not delta.issubset(model_names) or not removed.issubset(base_names)):
                    raise ValueError('高级训练差异清单无效')
                if manifest['packageMode']=='trained' and packaged_models!=delta:raise ValueError('高级训练包的增量权重清单不一致')
            engine=manifest['engine']
            policy=manifest.get('compatibility',{}).get('sourcePolicy','legacy-bytes')
            if engine['sourceFingerprint']!='legacy-unverified' and source_fingerprint(store,engine['id'],policy)!=engine['sourceFingerprint']:
                raise ValueError('本机 TTS 引擎源码版本与音色包不一致，请安装匹配版本后重试')
            if engine['runtimeFingerprint']!='legacy-unverified' and runtime_fingerprint(store,engine['id'])!=engine['runtimeFingerprint']:
                expected=manifest.get('compatibility',{}).get('runtime',{})
                actual=runtime_descriptor(store,engine['id'])
                differences=['Python: 包内 %s / 本机 %s'%(expected.get('python','未知'),actual['python'])] if expected.get('python')!=actual['python'] else []
                differences += ['%s: 包内 %s / 本机 %s'%(k,expected.get('packages',{}).get(k,'未安装'),actual['packages'].get(k,'未安装')) for k in sorted(set(expected.get('packages',{}))|set(actual['packages'])) if expected.get('packages',{}).get(k)!=actual['packages'].get(k)]
                raise ValueError('TTS 运行环境不匹配；'+ '；'.join(differences)+'。请安装原环境，或在原环境中重新导出；不会静默替换依赖。')
            model_directory=matching_model(store,engine['id'],engine['modelType'],model_records,engine['modelFingerprint'])
            if model_directory is None and manifest['packageMode']=='lightweight':
                raise ValueError('轻量音色包需要完全一致的本地 TTS 基础模型。请把对应模型安装到 tts-engines/'+engine['id']+'/models 后重试；要求指纹：'+engine['modelFingerprint'])
            if model_directory is None and manifest['packageMode']=='trained':
                base=manifest['baseModel'];base_directory=matching_model(store,engine['id'],engine['modelType'],base['modelFiles'],base['modelFingerprint'])
                if base_directory is None:raise ValueError('高级训练包需要匹配的基础模型，请先安装基础模型或改用高级完整模型包；要求指纹：'+base['modelFingerprint'])
            required_space=sum(item['size'] for item in records)+256*1024**2
            if model_directory is None and manifest['packageMode']=='trained':
                required_space+=sum(item['size'] for item in manifest['baseModel']['modelFiles'])
            if shutil.disk_usage(voice_parent).free<required_space:raise ValueError('磁盘空间不足，请为音色包导入预留空间')
            if model_directory is None:
                model_parent=engine_model_root(store,engine['id']);model_parent.mkdir(parents=True,exist_ok=True)
                model_stage=model_parent/('.staging-'+ident);model_final=model_parent/(storage_name(engine['modelName'])+'-'+engine['modelFingerprint'][:8])
                if model_final.exists():raise ValueError('同名基础模型目录已经存在但指纹不匹配，请先检查：'+str(model_final))
                if manifest['packageMode']=='trained':shutil.copytree(base_directory,model_stage)
                else:model_stage.mkdir()
            voice_stage.mkdir()
            for item in records:
                name=item['entry']
                if name.startswith('model/'):
                    if model_stage is None:
                        sha=hashlib.sha256()
                        with archive.open(name) as incoming:
                            for chunk in iter(lambda:incoming.read(1024*1024),b''):sha.update(chunk)
                        if sha.hexdigest()!=item['sha256']:raise ValueError('模型文件校验失败：'+name)
                        continue
                    target=model_stage/name[6:]
                else:target=voice_stage/name
                target.parent.mkdir(parents=True,exist_ok=True);sha=hashlib.sha256()
                with archive.open(name) as incoming,target.open('wb') as output:
                    for chunk in iter(lambda:incoming.read(1024*1024),b''):output.write(chunk);sha.update(chunk)
                if sha.hexdigest()!=item['sha256']:raise ValueError('模型文件校验失败：'+name)
            if model_stage is not None:
                if manifest['packageMode']=='trained':
                    for name in manifest['baseModel']['removedFiles']:
                        target=model_stage/name
                        if target.is_file():target.unlink()
                model_ready(model_stage,engine['id'],variant);actual_records,actual_fingerprint=inventory(model_sources(model_stage,engine['id'],variant))
                if actual_records!=model_records or actual_fingerprint!=engine['modelFingerprint']:raise ValueError('音色包中的模型指纹不一致')
                os.replace(model_stage,model_final);created_model=True;model_directory=model_final
            assert_offline(store)
            proposed=store.get()['voice'];proposed.update(copy.deepcopy(voice),reference=store.portable(voice_final/reference));proposed['gptModel' if is_gpt else 'baseModel']=store.portable(model_directory);validate_voice(proposed)
            backup=store.root/'data/config-backups'/uuid.uuid4().hex;backup.mkdir(parents=True);shutil.copy2(store.path,backup/'settings.json')
            reused_voice=voice_final.exists()
            if reused_voice:
                old=voice_final/reference
                if not old.is_file() or digest(old)!=reference_record['sha256']:raise ValueError('已导入的同一音色包参考损坏，请先备份并检查目录：'+str(voice_final))
            else:os.replace(voice_stage,voice_final);created_voice=True
            store.update({'voice':proposed})
        return {'ok':True,'name':display_name,'engine':engine['id'],'modelType':engine['modelType'],'packageMode':manifest['packageMode'],'modelFingerprint':engine['modelFingerprint'],'assetCount':len(records),'modelDirectory':str(model_directory),'packageDirectory':str(voice_parent),'referenceReused':reused_voice,'backup':str(backup),'voice':proposed}
    except Exception:
        if created_voice:remove_owned(voice_final,voice_parent)
        if created_model:remove_owned(model_final,engine_model_root(store,engine['id']))
        raise
    finally:
        if voice_stage.exists():remove_owned(voice_stage,voice_parent)
        if model_stage is not None and model_stage.exists():remove_owned(model_stage,model_stage.parent)
