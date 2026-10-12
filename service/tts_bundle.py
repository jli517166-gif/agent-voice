"""Engine-labelled TTS model packages, isolated from whole-app/RVC configuration."""
import copy
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import shutil
import uuid
import zipfile
from state import Store,atomic_json,validate_voice
from config_bundle import assert_offline,digest,remove_owned

EXTENSION='.cosyvoice.ttsmodel'
FIELDS=('engine','speed','style','referenceText','instruction','leadingSilence','volume','advancedEnabled','assetScope')
REQUIRED=('cosyvoice3.yaml','llm.pt','flow.pt','hift.pt','campplus.onnx','speech_tokenizer_v3.onnx')
MODEL_SUFFIXES={'.pt','.onnx','.yaml','.yml','.json','.safetensors','.bin','.txt','.model','.tiktoken','.md','.vocab'}
MAX_BYTES=64*1024**3

def model_ready(directory):
    directory=Path(directory)
    if not directory.is_dir() or not all((directory/name).is_file() for name in REQUIRED) or not (directory/'CosyVoice-BlankEN').is_dir():
        raise ValueError('请选择完整 CosyVoice 3 模型目录，包含权重、yaml、说话人／语音编码器和 CosyVoice-BlankEN 配套资源')
    import yaml
    # Inspect tags without constructing/executing HyperPyYAML objects.
    document=yaml.compose((directory/'cosyvoice3.yaml').read_text(encoding='utf-8-sig'),Loader=yaml.SafeLoader)
    if not isinstance(document,yaml.MappingNode):raise ValueError('CosyVoice 模型配置格式无效')
    llm=next((node for key,node in document.value if key.value=='llm'),None)
    if llm is None or llm.tag!='!new:cosyvoice.llm.llm.CosyVoice3LM':raise ValueError('模型配置与 CosyVoice 3 引擎不匹配，不能只改文件名或后缀')

def package_path(path):
    path=Path(path).resolve()
    if not path.name.lower().endswith(EXTENSION):raise ValueError('CosyVoice 模型包后缀必须为 '+EXTENSION)
    if not path.name[:-len(EXTENSION)].strip():raise ValueError('CosyVoice 模型包必须填写音色名称')
    return path

def package_name(path):
    value=Path(path).name[:-len(EXTENSION)].strip()
    if not value or len(value)>100 or any(ord(char)<32 for char in value):raise ValueError('CosyVoice 模型包名称无效')
    return value

def storage_name(value):
    invalid='<>:"/\\|?*'
    safe=''.join('_' if char in invalid or ord(char)<32 else char for char in value).strip().rstrip('. ')
    return (safe or '未命名音色')[:60]

def safe_entry(value):
    if not isinstance(value,str) or '\\' in value or ':' in value:raise ValueError('模型包路径无效')
    path=PurePosixPath(value)
    if path.is_absolute() or '..' in path.parts or str(path)!=value or not path.parts:raise ValueError('模型包路径越界')
    reserved={'CON','PRN','AUX','NUL'}|{'COM'+str(i) for i in range(1,10)}|{'LPT'+str(i) for i in range(1,10)}
    if any(p.endswith((' ','.')) or p.split('.')[0].upper() in reserved or any(ord(c)<32 for c in p) for p in path.parts):raise ValueError('模型包包含无效 Windows 文件名')
    return path

def export_model(root,target,profile_id=None):
    store=Store(root);cfg=store.get();voice=cfg['voice']
    if profile_id:
        matches=[item for item in cfg['profiles'] if item['id']==profile_id]
        if len(matches)!=1:raise ValueError('没有找到要导出的声音配置')
        voice=matches[0]['voice']
    if voice['engine']!='cosyvoice':raise ValueError('当前引擎没有可导出的本地训练模型；请选择 CosyVoice')
    directory=store.resolve(voice['baseModel'] or cfg['engines']['cosyModel']);model_ready(directory)
    target=package_path(target)
    if directory==target or directory in target.parents:raise ValueError('导出文件请保存在模型目录之外')
    sources={}
    for file in sorted(directory.rglob('*')):
        relative=file.relative_to(directory)
        if any(part.startswith('.') or part.endswith('.chunks') for part in relative.parts):continue
        if file.is_symlink() or (file.is_file() and directory not in file.resolve().parents):raise ValueError('模型目录含外部链接，请使用完整本地文件')
        if file.is_file() and file.suffix.lower() in MODEL_SUFFIXES:sources['model/'+relative.as_posix()]=file
    reference=store.resolve(voice['reference'])
    if not reference or not reference.is_file() or not voice['referenceText'].strip():raise ValueError('请先选择参考音频和对应台词')
    ref_entry='reference/reference'+reference.suffix.lower()
    if reference.suffix.lower() not in {'.wav','.flac','.mp3','.m4a','.ogg'}:raise ValueError('参考音频类型不支持')
    sources[ref_entry]=reference
    if not sources or len(sources)>10000 or sum(f.stat().st_size for f in sources.values())>MAX_BYTES:raise ValueError('模型资源数量或总大小超限')
    records=[{'entry':name,'size':file.stat().st_size,'sha256':digest(file)} for name,file in sources.items()]
    manifest={'format':'agent-voice-tts-model','version':1,'name':package_name(target),'engine':'cosyvoice','modelType':'cosyvoice3',
              'voice':{key:voice[key] for key in FIELDS},'reference':ref_entry,'files':records}
    target.parent.mkdir(parents=True,exist_ok=True);temp=target.with_name(target.name+'.'+uuid.uuid4().hex+'.tmp')
    try:
        with zipfile.ZipFile(temp,'w',compression=zipfile.ZIP_STORED,allowZip64=True) as archive:
            archive.writestr('manifest.json',json.dumps(manifest,ensure_ascii=False,indent=2))
            for item in records:
                sha=hashlib.sha256()
                with sources[item['entry']].open('rb') as stream,archive.open(item['entry'],'w',force_zip64=True) as output:
                    for chunk in iter(lambda:stream.read(1024*1024),b''):output.write(chunk);sha.update(chunk)
                if sha.hexdigest()!=item['sha256']:raise ValueError('导出期间模型资源改变，请重新导出')
        os.replace(temp,target)
    finally:
        if temp.exists():temp.unlink()
    return {'ok':True,'name':manifest['name'],'engine':'cosyvoice','modelType':'cosyvoice3','file':str(target),'assetCount':len(records),'size':target.stat().st_size}

def import_model(root,source):
    store=Store(root);assert_offline(store);source=package_path(source)
    fallback_name=package_name(source)
    parent=store.root/'voice-model-packages'/'已导入';parent.mkdir(parents=True,exist_ok=True)
    ident=uuid.uuid4().hex;stage=parent/('.staging-'+ident);final=parent/(storage_name(fallback_name)+'-'+ident[:8]);created=False
    try:
        with zipfile.ZipFile(source) as archive:
            entries=archive.infolist();names=[i.filename for i in entries]
            total=sum(i.file_size for i in entries)
            if len(entries)>10001 or len({n.casefold() for n in names})!=len(names) or total>MAX_BYTES:raise ValueError('模型包重复条目、数量或大小无效')
            if shutil.disk_usage(parent).free<total+256*1024**2:raise ValueError('磁盘空间不足，请为完整本地模型预留空间')
            info=archive.getinfo('manifest.json')
            if info.file_size>8*1024**2:raise ValueError('模型清单过大')
            manifest=json.loads(archive.read(info).decode('utf-8-sig'))
            if manifest.get('format')!='agent-voice-tts-model' or manifest.get('version')!=1 or manifest.get('engine')!='cosyvoice' or manifest.get('modelType')!='cosyvoice3':raise ValueError('引擎标识或版本不匹配，当前程序仅接收 CosyVoice 3 模型包')
            display_name=manifest.get('name',fallback_name)
            if not isinstance(display_name,str) or not display_name.strip() or len(display_name)>100 or any(ord(char)<32 for char in display_name):raise ValueError('模型包音色名称无效')
            display_name=display_name.strip();final=parent/(storage_name(display_name)+'-'+ident[:8])
            voice=manifest['voice']
            if not isinstance(voice,dict) or set(voice)!=set(FIELDS) or voice['engine']!='cosyvoice':raise ValueError('模型包包含非本地声音设置或错误引擎')
            reference=str(safe_entry(manifest['reference']))
            if not reference.startswith('reference/') or PurePosixPath(reference).suffix.lower() not in {'.wav','.flac','.mp3','.m4a','.ogg'}:raise ValueError('参考录音路径无效')
            records=manifest['files']
            if not isinstance(records,list) or len(records)>10000:raise ValueError('资源清单无效')
            allowed={'manifest.json'}
            for item in records:
                name=str(safe_entry(item['entry']))
                if name in allowed or not (name.startswith('model/') or name==reference):raise ValueError('资源清单重复或包含非模型资源')
                if name.startswith('model/') and PurePosixPath(name).suffix.lower() not in MODEL_SUFFIXES:raise ValueError('模型包含不支持的资源类型')
                size=item['size'];sha=item['sha256']
                import re
                if isinstance(size,bool) or not isinstance(size,int) or size<0 or size!=archive.getinfo(name).file_size or not isinstance(sha,str) or not re.fullmatch('[0-9a-f]{64}',sha):raise ValueError('资源校验记录错误')
                allowed.add(name)
            if set(names)!=allowed or reference not in allowed:raise ValueError('模型包包含未登记资源')
            proposed=store.get()['voice'];proposed.update(copy.deepcopy(voice),synthesisMode='base',baseModel=store.portable(final/'model'),reference=store.portable(final/reference))
            validate_voice(proposed)
            stage.mkdir()
            for item in records:
                target=stage/item['entry'];target.parent.mkdir(parents=True,exist_ok=True);sha=hashlib.sha256()
                with archive.open(item['entry']) as incoming,target.open('wb') as output:
                    for chunk in iter(lambda:incoming.read(1024*1024),b''):output.write(chunk);sha.update(chunk)
                if sha.hexdigest()!=item['sha256']:raise ValueError('模型文件校验失败：'+item['entry'])
            model_ready(stage/'model');assert_offline(store)
            backup=store.root/'data/config-backups'/uuid.uuid4().hex;backup.mkdir(parents=True);shutil.copy2(store.path,backup/'settings.json')
            os.replace(stage,final);created=True
            store.update({'voice':proposed})
        return {'ok':True,'name':display_name,'engine':'cosyvoice','assetCount':len(records),'modelDirectory':str(final/'model'),'packageDirectory':str(parent),'backup':str(backup),'voice':proposed}
    except Exception:
        if created:remove_owned(final,parent)
        raise
    finally:
        if stage.exists():remove_owned(stage,parent)
