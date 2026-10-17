"""Pinned references and model integrity records for named voice configurations."""
import copy,hashlib,json,os,shutil,uuid
from pathlib import Path
from state import atomic_json,validate_voice
from config_bundle import digest

def manifest_path(store,ident):
    if not isinstance(ident,str) or not ident or any(c not in 'abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_' for c in ident):raise ValueError('声音方案 ID 无效')
    return store.root/'data/voice-profile-assets'/(ident+'.json')

def model_inventory(store,voice):
    from engine_registry import model_directory,variant
    from tts_bundle import model_sources,inventory_fingerprint
    sources=model_sources(model_directory(store,voice),voice['engine'],variant(voice))
    cache_path=store.root/'data/model-digests.json'
    cache=json.loads(cache_path.read_text(encoding='utf-8')) if cache_path.exists() else {}
    records=[]
    for name,p in sources.items():
        st=p.stat();key=str(p.resolve());stamp=[st.st_size,st.st_mtime_ns,st.st_ctime_ns]
        old=cache.get(key)
        sha=old['sha256'] if old and old['stamp']==stamp else digest(p)
        cache[key]={'stamp':stamp,'sha256':sha};records.append({'path':name,'size':st.st_size,'sha256':sha})
    atomic_json(cache_path,cache)
    return records,inventory_fingerprint(records)

def pin_reference(store,voice):
    src=store.resolve(voice['reference'])
    if not src or not src.is_file() or src.stat().st_size>40*1024**2:raise ValueError('参考录音缺失或超过 40 MB，不能固定保存')
    if src.suffix.lower() not in ('.wav','.flac','.mp3','.m4a','.ogg'):raise ValueError('参考录音格式不支持')
    sha=digest(src);parent=store.root/'data/voice-assets';parent.mkdir(parents=True,exist_ok=True);dest=parent/(sha+src.suffix.lower())
    if dest.exists():
        if digest(dest)!=sha:raise ValueError('已固定的参考录音损坏，请从备份或音色包恢复')
    else:
        temp=dest.with_name(dest.name+'.'+uuid.uuid4().hex+'.tmp')
        try:
            shutil.copyfile(src,temp)
            if digest(temp)!=sha:raise ValueError('保存期间参考录音发生变化，请重试')
            os.replace(temp,dest)
        finally:temp.unlink(missing_ok=True)
    return dest,sha

def save_profile(store,name,voice,ident=None):
    if not isinstance(name,str) or not 0<len(name.strip())<=80:raise ValueError('声音方案名称必须为 1～80 字')
    voice=copy.deepcopy(voice);validate_voice(voice)
    if not voice['referenceText'].strip():raise ValueError('请先填写参考录音逐字台词')
    registry=store.root/'data/private-assets.json';registry_prior=registry.read_bytes() if registry.exists() else None
    private=json.loads(registry_prior.decode('utf-8-sig')) if registry_prior else {'files':[]}
    if not isinstance(private,dict) or not isinstance(private.get('files'),list):raise ValueError('私有资产记录损坏，请先从备份恢复')
    records,fp=model_inventory(store,voice);ref,sha=pin_reference(store,voice)
    ident=ident or uuid.uuid4().hex;path=manifest_path(store,ident)
    existing=next((p for p in store.get()['profiles'] if p['id']==ident),None)
    if existing and existing['name']!=name:raise ValueError('不能覆盖另一名称的声音方案')
    voice['reference']=store.portable(ref);profile={'id':ident,'name':name.strip(),'voice':voice}
    record={'version':1,'reference':{'sha256':sha,'file':voice['reference']},'engine':voice['engine'],
            'variant':voice.get('gptVariant') if voice['engine']=='gpt-sovits' else 'cosyvoice3',
            'modelFingerprint':fp,'modelFiles':records,'scope':voice['assetScope']}
    prior=path.read_bytes() if path.exists() else None
    atomic_json(path,record)
    try:
        if voice['assetScope']=='private' and not any(p['sha256']==sha and p.get('file')==str(ref) for p in private['files']):
            private['files'].append({'file':str(ref),'sha256':sha,'size':ref.stat().st_size});atomic_json(registry,private)
        profiles=store.get()['profiles'];profiles=[p for p in profiles if p['id']!=ident]+[profile]
        store.update({'profiles':profiles})
    except Exception:
        if prior is None:path.unlink(missing_ok=True)
        else:atomic_json(path,json.loads(prior))
        if registry_prior is None:registry.unlink(missing_ok=True)
        else:atomic_json(registry,json.loads(registry_prior.decode('utf-8-sig')))
        raise
    return {'ok':True,'profile':profile,'referencePinned':True,'modelFingerprint':fp,'config':store.get()}

def apply_profile(store,ident):
    profile=next((p for p in store.get()['profiles'] if p['id']==ident),None)
    if not profile:raise ValueError('声音方案不存在')
    path=manifest_path(store,ident)
    if path.exists():
        record=json.loads(path.read_text(encoding='utf-8'))
        if digest(store.resolve(profile['voice']['reference']))!=record['reference']['sha256']:raise ValueError('声音方案的固定参考录音损坏，请从音色包恢复')
        _,fp=model_inventory(store,profile['voice'])
        if fp!=record['modelFingerprint']:raise ValueError('模型文件已被替换，与保存时不一致；请恢复原模型，或确认新声音后另存方案')
    return {'ok':True,'config':store.update({'voice':profile['voice']}),'referencePinned':path.exists()}

def seal_profile(store,ident):
    p=next((p for p in store.get()['profiles'] if p['id']==ident),None)
    if not p:raise ValueError('声音方案不存在')
    return save_profile(store,p['name'],p['voice'],ident)
