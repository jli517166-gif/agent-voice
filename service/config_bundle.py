"""Versioned configuration bundles with hashed assets and transactional import."""
import copy
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import urllib.error
import urllib.request
import uuid
import zipfile
from state import Store, atomic_json, defaults, merge, validate

FORMAT='agent-voice-config'
MAX_BYTES=8*1024**3
SUFFIXES={'.pth','.index','.wav','.flac','.mp3','.m4a','.ogg','.png','.jpg','.jpeg','.bmp','.ico'}
MODULES=set(defaults())-{'token','port'}

def digest(path):
    h=hashlib.sha256()
    with Path(path).open('rb') as stream:
        for chunk in iter(lambda:stream.read(1024*1024),b''):h.update(chunk)
    return h.hexdigest()

def slots(config):
    voices=[config['voice']]+[p['voice'] for p in config['profiles']]
    for voice in voices:
        for key in ('model','index','reference'):
            if voice[key]:yield voice,key,voice['assetScope']=='private'
    for key in ('background','icon'):
        if config['appearance'][key]:yield config['appearance'],key,False

def export_bundle(root,target,include_assets=True,config=None):
    store=Store(root);cfg=validate(copy.deepcopy(config or store.get()))
    cfg={key:cfg[key] for key in sorted(MODULES)}
    target=Path(target).resolve();target.parent.mkdir(parents=True,exist_ok=True)
    assets={};sources={};private=any(v['assetScope']=='private' for v in [cfg['voice']]+[p['voice'] for p in cfg['profiles']])
    registry=store.root/'data/private-assets.json'
    protected={f['sha256'] for f in json.loads(registry.read_text(encoding='utf-8-sig')).get('files',[])} if registry.exists() else set()
    for obj,key,is_private in slots(cfg):
        path=store.resolve(obj[key])
        if not path.is_file():raise ValueError('导出资源不存在：'+str(path))
        sha=digest(path);is_private=is_private or sha in protected;private=private or is_private
        if include_assets:
            suffix=path.suffix.lower()
            if suffix not in SUFFIXES:raise ValueError('不支持的配置资源类型：'+suffix)
            record=assets.setdefault(sha,{'id':sha,'entry':'assets/'+sha+suffix,'sha256':sha,'size':path.stat().st_size,'private':False})
            record['private']=record['private'] or is_private
            sources[sha]=path;obj[key]='asset:'+sha
        else:obj[key]=str(path)
    if sum(f['size'] for f in assets.values())>MAX_BYTES:raise ValueError('配置资源超过 8 GB，请分开保存')
    manifest={'format':FORMAT,'version':1,'processingRevision':'accepted-v1','scope':'private' if private else 'public',
              'config':cfg,'assets':list(assets.values()),'includesAssets':include_assets}
    temp=target.with_name(target.name+'.'+uuid.uuid4().hex+'.tmp')
    try:
        with zipfile.ZipFile(temp,'w',compression=zipfile.ZIP_STORED,allowZip64=True) as archive:
            archive.writestr(zipfile.ZipInfo('manifest.json'),json.dumps(manifest,ensure_ascii=False,indent=2,sort_keys=True))
            for sha,record in assets.items():
                info=zipfile.ZipInfo(record['entry']);info.file_size=record['size'];check=hashlib.sha256()
                with sources[sha].open('rb') as stream,archive.open(info,'w',force_zip64=True) as output:
                    for chunk in iter(lambda:stream.read(1024*1024),b''):output.write(chunk);check.update(chunk)
                # Reject a source modified between checksum and archive copy.
                if check.hexdigest()!=sha:raise ValueError('导出期间资源改变，请重试')
        os.replace(temp,target)
    finally:
        if temp.exists():temp.unlink()
    return {'ok':True,'file':str(target),'scope':manifest['scope'],'assetCount':len(assets),'size':target.stat().st_size}

def assert_offline(store):
    cfg=store.get()
    request=urllib.request.Request('http://127.0.0.1:%d/health'%cfg['port'],headers={'Authorization':'Bearer '+cfg['token']})
    try:
        with urllib.request.build_opener(urllib.request.ProxyHandler({})).open(request,timeout=1) as response:
            if json.loads(response.read()).get('service')=='agent-voice-public':raise ValueError('请先关闭语音服务再导入配置')
    except urllib.error.HTTPError as exc:
        if exc.code==401:raise ValueError('端口被其他连接配置占用，不能导入') from exc
        raise
    except (urllib.error.URLError,TimeoutError,ConnectionError):pass

def remove_owned(path,parent):
    # Only generated direct children of the explicit configuration-assets root.
    path=Path(path);parent=Path(parent).resolve()
    if path.is_symlink() or path.resolve().parent!=parent:raise ValueError('拒绝清理非配置暂存目录')
    if path.exists():shutil.rmtree(path)

def preflight(store,cfg,adjustments):
    for key,value in cfg['engines'].items():
        if value and store.resolve(value).exists():continue
        current=store.get()['engines'][key]
        if current and store.resolve(current).exists():cfg['engines'][key]=current;adjustments.append('engines.'+key)
    for obj,key,_ in slots(cfg):
        path=store.resolve(obj[key])
        if not path.is_file():raise ValueError('导入资源不存在：'+str(path))
        if key in ('model','index') and path.suffix.lower()!=('.pth' if key=='model' else '.index'):raise ValueError('模型或索引类型不匹配')
    required=['pythonRvc','rvcHome']+(['pythonTts','cosyRepo','cosyModel'] if cfg['voice']['engine']=='cosyvoice' else [])
    if cfg['voice']['model']:
        for key in required:
            value=cfg['engines'][key]
            if not value or not store.resolve(value).exists():raise ValueError('请先配置本机引擎环境：'+key)
        if cfg['voice']['engine']=='cosyvoice' and (not cfg['voice']['reference'] or not cfg['voice']['referenceText'].strip()):raise ValueError('底音参考录音或台词不完整')
    for key in ('background','icon'):
        if cfg['appearance'][key]:
            from PIL import Image
            with Image.open(store.resolve(cfg['appearance'][key])) as image:image.verify()
    directory=cfg['airi']['directory']
    if directory and not (store.resolve(directory)/'airi.exe').is_file():
        current=store.get()['airi']['directory']
        if current and (store.resolve(current)/'airi.exe').is_file():cfg['airi']['directory']=current
        else:cfg['airi'].update(directory='',enabled=False,output='local')
        adjustments.append('airi.directory')
    roots=[]
    for value in cfg['modelRoots']:
        if value and store.resolve(value).is_dir():roots.append(value)
    if not roots:roots=['models']
    cfg['modelRoots']=roots

def import_bundle(root,source,asset_root=None):
    store=Store(root);assert_offline(store);source=Path(source).resolve()
    if source.stat().st_size>MAX_BYTES+8*1024**2:raise ValueError('配置包过大')
    package_id=digest(source)[:24]
    parent=Path(asset_root).resolve() if asset_root else None
    final=None;stage=None
    created=False;adjustments=[]
    old_registry=None;registry=store.root/'data/private-assets.json';registry_changed=False
    try:
        with zipfile.ZipFile(source) as archive:
            infos=archive.infolist();names=[f.filename for f in infos]
            if len(infos)>1000 or len(names)!=len(set(names)) or sum(f.file_size for f in infos)>MAX_BYTES:raise ValueError('配置包内容大小或条目无效')
            info=archive.getinfo('manifest.json')
            if info.file_size>8*1024**2:raise ValueError('配置清单过大')
            manifest=json.loads(archive.read(info).decode('utf-8-sig'))
            if manifest.get('format')!=FORMAT or manifest.get('version')!=1 or manifest.get('processingRevision')!='accepted-v1':raise ValueError('配置包版本或处理链不兼容')
            if manifest.get('scope') not in ('private','public'):raise ValueError('资产范围无效')
            if parent is None:
                parent=(store.root.with_name(store.root.name+'-私有资产')/'config-imports') if manifest['scope']=='private' else store.root/'data/config-assets'
            parent.mkdir(parents=True,exist_ok=True);final=parent/package_id;stage=parent/('.staging-'+uuid.uuid4().hex)
            modules=manifest['config']
            if not isinstance(modules,dict) or set(modules)-MODULES:raise ValueError('配置包包含未知模块或本机连接密钥')
            modules=copy.deepcopy(modules)
            if 'voice' in modules:
                # Old accepted bundles predate synthesisMode/advancedEnabled.
                # Restore their original defaults, not the target's current mode.
                original=modules['voice']
                modules['voice']=merge(defaults()['voice'],original)
                if 'synthesisMode' not in original:modules['voice']['synthesisMode']='rvc'
                if 'advancedEnabled' not in original:modules['voice']['advancedEnabled']=True
            cfg=validate(merge(store.get(),modules));records=manifest['assets']
            if not isinstance(records,list):raise ValueError('资源清单无效')
            allowed={'manifest.json'};assets={}
            for record in records:
                sha=record['sha256'];entry=record['entry']
                if not isinstance(sha,str) or not re.fullmatch('[0-9a-f]{64}',sha) or record['id']!=sha or sha in assets:raise ValueError('资源校验信息无效')
                suffix=Path(entry).suffix.lower()
                if suffix not in SUFFIXES or entry!='assets/'+sha+suffix:raise ValueError('资源路径无效')
                if not isinstance(record['size'],int) or record['size']<0 or archive.getinfo(entry).file_size!=record['size']:raise ValueError('资源长度不匹配')
                assets[sha]=record;allowed.add(entry)
            if manifest['scope']=='public' and (any(record.get('private') for record in records) or any(v['assetScope']=='private' for v in [cfg['voice']]+[p['voice'] for p in cfg['profiles']])):raise ValueError('私有配置不能标记为公开包')
            if set(names)!=allowed:raise ValueError('配置包包含未登记的文件')
            stage.mkdir()
            for sha,record in assets.items():
                path=stage/Path(record['entry']).name;h=hashlib.sha256()
                with archive.open(record['entry']) as incoming,path.open('wb') as output:
                    for chunk in iter(lambda:incoming.read(1024*1024),b''):output.write(chunk);h.update(chunk)
                if h.hexdigest()!=sha:raise ValueError('配置包资源损坏：'+Path(record['entry']).name)
            for obj,key,_ in slots(cfg):
                if obj[key].startswith('asset:'):
                    sha=obj[key][6:]
                    if sha not in assets:raise ValueError('配置引用未登记的资源')
                    obj[key]=store.portable(stage/Path(assets[sha]['entry']).name)
            preflight(store,cfg,adjustments)
            if final.exists():
                for sha,record in assets.items():
                    path=final/Path(record['entry']).name
                    if not path.is_file() or digest(path)!=sha:raise ValueError('已导入资源被修改，请先检查现用配置资产')
            else:
                os.replace(stage,final);created=True
            for obj,key,_ in slots(cfg):
                path=store.resolve(obj[key])
                if path.parent==stage:obj[key]=store.portable(final/path.name)
            private_files=[]
            private_hashes={sha for sha,record in assets.items() if record.get('private') or manifest['scope']=='private'}
            for voice in [cfg['voice']]+[p['voice'] for p in cfg['profiles']]:
                if any(voice[key] and Path(voice[key]).stem in private_hashes for key in ('model','index','reference')):voice['assetScope']='private'
            for sha in private_hashes:private_files.append({'file':str(final/Path(assets[sha]['entry']).name),'sha256':sha,'size':assets[sha]['size']})
            validate(cfg);assert_offline(store)
            backup=store.root/'data/config-backups'/uuid.uuid4().hex;backup.mkdir(parents=True)
            shutil.copy2(store.path,backup/'settings.json')
            if registry.exists():old_registry=registry.read_bytes();shutil.copy2(registry,backup/'private-assets.json')
            if private_files:
                current=json.loads(old_registry.decode('utf-8-sig')) if old_registry else {'files':[]}
                existing={(f.get('file'),f['sha256']) for f in current['files']}
                current['files'] += [f for f in private_files if (f['file'],f['sha256']) not in existing]
                atomic_json(registry,current);registry_changed=True
            try:store.update({key:cfg[key] for key in MODULES})
            except Exception:
                if registry_changed:
                    if old_registry is None:registry.unlink()
                    else:atomic_json(registry,json.loads(old_registry.decode('utf-8-sig')))
                raise
        return {'ok':True,'scope':manifest['scope'],'assetCount':len(assets),'assetDirectory':str(final),'backup':str(backup),'adjustments':adjustments,'voice':cfg['voice']}
    except Exception:
        if created:remove_owned(final,parent)
        raise
    finally:
        if stage is not None and stage.exists():remove_owned(stage,parent)
