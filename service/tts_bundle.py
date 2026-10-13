"""Portable and lightweight, engine-labelled TTS voice packages."""
import copy
import hashlib
import importlib.metadata
import json
import os
from pathlib import Path, PurePosixPath
import re
import shutil
import sys
import uuid
import zipfile

from state import Store, validate_voice
from config_bundle import assert_offline, digest, remove_owned

EXTENSION = '.cosyvoice.ttsmodel'
FIELDS = ('engine','speed','style','referenceText','instruction','leadingSilence','volume','advancedEnabled','assetScope')
REQUIRED = ('cosyvoice3.yaml','llm.pt','flow.pt','hift.pt','campplus.onnx','speech_tokenizer_v3.onnx')
MODEL_SUFFIXES = {'.pt','.onnx','.yaml','.yml','.json','.safetensors','.bin','.txt','.model','.tiktoken','.md','.vocab'}
PACKAGE_MODES = ('lightweight','full')
MAX_BYTES = 64 * 1024**3
SOURCE_SUFFIXES = {'.py','.yaml','.yml','.json','.toml','.txt','.md'}
RUNTIME_PACKAGES = ('torch','torchaudio','numpy','hyperpyyaml','onnxruntime-gpu','onnxruntime','transformers','diffusers','soundfile','librosa')


def model_ready(directory):
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


def package_path(path):
    path = Path(path).resolve()
    if not path.name.lower().endswith(EXTENSION):
        raise ValueError('CosyVoice 模型包后缀必须为 ' + EXTENSION)
    if not path.name[:-len(EXTENSION)].strip():
        raise ValueError('CosyVoice 模型包必须填写音色名称')
    return path


def package_name(path):
    value = Path(path).name[:-len(EXTENSION)].strip()
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


def model_sources(directory):
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


def source_fingerprint(store, engine):
    directory = store.root/'tts-engines'/storage_name(engine)/'source'
    if not directory.is_dir():
        raise ValueError('缺少本地 TTS 引擎源码目录：'+str(directory))
    records=[]
    for file in sorted(directory.rglob('*')):
        if file.is_symlink():
            raise ValueError('TTS 引擎源码目录含外部链接：'+str(file))
        if file.is_file() and file.suffix.lower() in SOURCE_SUFFIXES:
            records.append({'path':file.relative_to(directory).as_posix(),'size':file.stat().st_size,'sha256':digest(file)})
    if not records:raise ValueError('本地 TTS 引擎源码目录为空或无法识别：'+str(directory))
    return inventory_fingerprint(records)


def runtime_fingerprint():
    packages={}
    for name in RUNTIME_PACKAGES:
        try:packages[name]=importlib.metadata.version(name)
        except importlib.metadata.PackageNotFoundError:pass
    descriptor={'python':'.'.join(map(str,sys.version_info[:3])),'packages':packages}
    return hashlib.sha256(json.dumps(descriptor,sort_keys=True,separators=(',',':')).encode()).hexdigest()


def engine_model_root(store, engine):
    return store.root / 'tts-engines' / storage_name(engine) / 'models'


def matching_model(store, engine, model_type, records, fingerprint):
    if engine != 'cosyvoice' or model_type != 'cosyvoice3':
        raise ValueError('当前程序尚未安装适配该 TTS 引擎的模块：' + engine + ' / ' + model_type)
    parent = engine_model_root(store, engine)
    if not parent.is_dir():
        return None
    expected = {item['path']:(item['size'],item['sha256']) for item in records}
    candidates = [parent] + sorted((p for p in parent.iterdir() if p.is_dir()), key=lambda p:p.name.casefold())
    for directory in candidates:
        try:
            model_ready(directory)
            files = model_sources(directory)
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


def export_model(root, target, profile_id=None, package_mode='lightweight'):
    if package_mode not in PACKAGE_MODES:
        raise ValueError('音色包类型必须为 lightweight 或 full')
    store = Store(root); cfg = store.get(); voice = cfg['voice']
    if profile_id:
        matches = [item for item in cfg['profiles'] if item['id'] == profile_id]
        if len(matches) != 1:
            raise ValueError('没有找到要导出的声音配置')
        voice = matches[0]['voice']
    if voice['engine'] != 'cosyvoice':
        raise ValueError('当前引擎没有可导出的本地训练模型；请选择 CosyVoice')
    directory = store.resolve(voice['baseModel'] or cfg['engines']['cosyModel'])
    sources = model_sources(directory); model_records, fingerprint = inventory(sources)
    target = package_path(target)
    if directory == target or directory in target.parents:
        raise ValueError('导出文件请保存在模型目录之外')
    reference = store.resolve(voice['reference'])
    if not reference or not reference.is_file() or not voice['referenceText'].strip():
        raise ValueError('请先选择参考音频和对应台词')
    ref_entry = 'reference/reference' + reference.suffix.lower()
    if reference.suffix.lower() not in {'.wav','.flac','.mp3','.m4a','.ogg'}:
        raise ValueError('参考音频类型不支持')
    package_sources = {ref_entry:reference}
    if package_mode == 'full':
        package_sources.update({'model/'+name:file for name,file in sources.items()})
    if len(package_sources) > 10000 or sum(f.stat().st_size for f in package_sources.values()) > MAX_BYTES:
        raise ValueError('模型资源数量或总大小超限')
    records = [{'entry':name,'size':file.stat().st_size,'sha256':digest(file)} for name,file in package_sources.items()]
    manifest = {
        'format':'agent-voice-tts-model','version':2,'name':package_name(target),'packageMode':package_mode,
        'engine':{'id':'cosyvoice','modelType':'cosyvoice3','modelName':directory.name,'modelFingerprint':fingerprint,
                  'sourceFingerprint':source_fingerprint(store,'cosyvoice'),'runtimeFingerprint':runtime_fingerprint()},
        'modelFiles':model_records,'voice':{key:voice[key] for key in FIELDS},'reference':ref_entry,'files':records,
    }
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
    return {'ok':True,'name':manifest['name'],'engine':'cosyvoice','modelType':'cosyvoice3','packageMode':package_mode,
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
    if manifest.get('version')!=2 or manifest.get('packageMode') not in PACKAGE_MODES:
        raise ValueError('TTS 音色包版本或类型不受支持')
    engine=manifest.get('engine')
    if not isinstance(engine,dict) or set(engine)!={'id','modelType','modelName','modelFingerprint','sourceFingerprint','runtimeFingerprint'}:
        raise ValueError('TTS 引擎匹配信息不完整')
    if engine['id']!='cosyvoice' or engine['modelType']!='cosyvoice3' or not re.fullmatch('[0-9a-f]{64}',str(engine['modelFingerprint'])):
        raise ValueError('引擎标识、模型类型或模型指纹不匹配')
    if not isinstance(engine['modelName'],str) or not engine['modelName'].strip() or len(engine['modelName'])>100:
        raise ValueError('基础模型名称无效')
    for key in ('sourceFingerprint','runtimeFingerprint'):
        if engine[key]!='legacy-unverified' and not re.fullmatch('[0-9a-f]{64}',str(engine[key])):raise ValueError('TTS 引擎源码或运行环境指纹无效')
    return manifest


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
            if not isinstance(voice,dict) or set(voice)!=set(FIELDS) or voice['engine']!='cosyvoice':raise ValueError('模型包包含非本地声音设置或错误引擎')
            reference=str(safe_entry(manifest['reference']))
            if not reference.startswith('reference/') or PurePosixPath(reference).suffix.lower() not in {'.wav','.flac','.mp3','.m4a','.ogg'}:raise ValueError('参考录音路径无效')
            model_records=manifest['modelFiles']
            if not isinstance(model_records,list) or not model_records or len(model_records)>10000:raise ValueError('基础模型指纹清单无效')
            model_names=set()
            for item in model_records:
                path=validate_record(item,archive,'path')
                if path in model_names or PurePosixPath(path).suffix.lower() not in MODEL_SUFFIXES:raise ValueError('基础模型指纹清单重复或包含不支持的资源')
                model_names.add(path)
            if inventory_fingerprint(model_records)!=manifest['engine']['modelFingerprint']:raise ValueError('基础模型指纹清单已损坏')
            records=manifest['files']
            if not isinstance(records,list) or len(records)>10000:raise ValueError('资源清单无效')
            allowed={'manifest.json'}
            for item in records:
                name=validate_record(item,archive)
                if name in allowed or not (name.startswith('model/') or name==reference):raise ValueError('资源清单重复或包含非模型资源')
                if name.startswith('model/') and PurePosixPath(name).suffix.lower() not in MODEL_SUFFIXES:raise ValueError('模型包含不支持的资源类型')
                allowed.add(name)
            if set(names)!=allowed or reference not in allowed:raise ValueError('模型包包含未登记资源')
            packaged_models={name[6:] for name in allowed if name.startswith('model/')}
            if manifest['packageMode']=='lightweight' and packaged_models:raise ValueError('轻量包不得携带基础模型权重')
            if manifest['packageMode']=='full' and packaged_models!=model_names:raise ValueError('完整便携包的模型资源与指纹清单不一致')
            engine=manifest['engine']
            if engine['sourceFingerprint']!='legacy-unverified' and source_fingerprint(store,engine['id'])!=engine['sourceFingerprint']:
                raise ValueError('本机 TTS 引擎源码版本与音色包不一致，请安装匹配版本后重试')
            if engine['runtimeFingerprint']!='legacy-unverified' and runtime_fingerprint()!=engine['runtimeFingerprint']:
                raise ValueError('本机 TTS Python 运行环境与音色包不一致，请按安装手册安装匹配依赖后重试')
            model_directory=matching_model(store,engine['id'],engine['modelType'],model_records,engine['modelFingerprint'])
            if model_directory is None and manifest['packageMode']=='lightweight':
                raise ValueError('轻量音色包需要完全一致的本地 TTS 基础模型。请把对应模型安装到 tts-engines/'+engine['id']+'/models 后重试；要求指纹：'+engine['modelFingerprint'])
            if shutil.disk_usage(voice_parent).free<sum(item['size'] for item in records)+256*1024**2:raise ValueError('磁盘空间不足，请为音色包导入预留空间')
            if model_directory is None:
                model_parent=engine_model_root(store,engine['id']);model_parent.mkdir(parents=True,exist_ok=True)
                model_stage=model_parent/('.staging-'+ident);model_final=model_parent/(storage_name(engine['modelName'])+'-'+engine['modelFingerprint'][:8])
                if model_final.exists():raise ValueError('同名基础模型目录已经存在但指纹不匹配，请先检查：'+str(model_final))
                model_stage.mkdir()
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
                model_ready(model_stage);actual_records,actual_fingerprint=inventory(model_sources(model_stage))
                if actual_records!=model_records or actual_fingerprint!=engine['modelFingerprint']:raise ValueError('完整便携包中的基础模型指纹不一致')
                os.replace(model_stage,model_final);created_model=True;model_directory=model_final
            assert_offline(store)
            proposed=store.get()['voice'];proposed.update(copy.deepcopy(voice),synthesisMode='base',baseModel=store.portable(model_directory),reference=store.portable(voice_final/reference));validate_voice(proposed)
            backup=store.root/'data/config-backups'/uuid.uuid4().hex;backup.mkdir(parents=True);shutil.copy2(store.path,backup/'settings.json')
            os.replace(voice_stage,voice_final);created_voice=True;store.update({'voice':proposed})
        return {'ok':True,'name':display_name,'engine':engine['id'],'modelType':engine['modelType'],'packageMode':manifest['packageMode'],'modelFingerprint':engine['modelFingerprint'],'assetCount':len(records),'modelDirectory':str(model_directory),'packageDirectory':str(voice_parent),'backup':str(backup),'voice':proposed}
    except Exception:
        if created_voice:remove_owned(voice_final,voice_parent)
        if created_model:remove_owned(model_final,engine_model_root(store,'cosyvoice'))
        raise
    finally:
        if voice_stage.exists():remove_owned(voice_stage,voice_parent)
        if model_stage is not None and model_stage.exists():remove_owned(model_stage,model_stage.parent)
