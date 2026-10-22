"""Explicit public asset allowlist; packaging stays gated until acceptance."""
import argparse
import hashlib
import json
from pathlib import Path
import sys
import zipfile

def private_bundle(path,denied):
    """Renaming a configuration archive must not bypass private-asset checks."""
    if not zipfile.is_zipfile(path):return False
    try:
        with zipfile.ZipFile(path) as archive:
            if 'manifest.json' not in archive.namelist():return False
            info=archive.getinfo('manifest.json')
            if info.file_size>8*1024*1024:raise ValueError('归档配置清单过大')
            config=json.loads(archive.read(info).decode('utf-8-sig'))
            if config.get('format')=='agent-voice-tts-model':
                if config.get('voice',{}).get('assetScope')!='public':return True
                entries=[i for i in archive.infolist() if i.filename.startswith('reference/')]
                if sum(i.file_size for i in entries)>40*1024**2:raise ValueError('音色包参考录音过大')
                for entry in entries:
                    h=hashlib.sha256()
                    with archive.open(entry) as incoming:
                        for chunk in iter(lambda:incoming.read(1024*1024),b''):h.update(chunk)
                    if h.hexdigest() in denied:return True
                return False
            if config.get('format')!='agent-voice-config':return False
            if config.get('scope')!='public':return True
            module=config.get('config',{})
            voices=[module.get('voice',{})]+[p.get('voice',{}) for p in module.get('profiles',[])]
            if any(v.get('assetScope')=='private' for v in voices):return True
            if any(a.get('private') for a in config.get('assets',[])):return True
            entries=[item for item in archive.infolist() if item.filename.startswith('assets/')]
            if sum(item.file_size for item in entries)>8*1024**3:raise ValueError('配置归档资源过大')
            for item in entries:
                h=hashlib.sha256()
                with archive.open(item) as stream:
                    for chunk in iter(lambda:stream.read(1024*1024),b''):h.update(chunk)
                if h.hexdigest() in denied:return True
    except zipfile.BadZipFile:
        # Some DLL/model binaries happen to end with bytes that resemble a ZIP
        # footer.  They are ordinary runtime files, not malformed config bundles.
        return False
    return False

def digest(path):
    h=hashlib.sha256()
    with path.open('rb') as stream:
        for data in iter(lambda:stream.read(1024*1024),b''):h.update(data)
    return h.hexdigest()

def audit(root,manifest,private_manifest=None):
    root=Path(root).resolve();data=json.loads(Path(manifest).read_text(encoding='utf-8-sig'))
    denied=set()
    registry=root/'data/private-assets.json'
    if registry.is_file():
        private=json.loads(registry.read_text(encoding='utf-8-sig'))
        denied.update(p['sha256'] for p in private['files'])
    settings=root/'data/settings.json'
    if settings.is_file():
        config=json.loads(settings.read_text(encoding='utf-8-sig'))
        voices=[config.get('voice',{})]+[p.get('voice',{}) for p in config.get('profiles',[])]
        for voice in voices:
            if voice.get('assetScope')!='private':continue
            for key in ('model','index','reference'):
                value=voice.get(key)
                if value:
                    asset=(root/value).resolve()
                    if asset.is_file():denied.add(digest(asset))
    if private_manifest:
        private=json.loads(Path(private_manifest).read_text(encoding='utf-8-sig'))
        denied.update(p['sha256'] for p in private['private_files'])
    entries=[];errors=[]
    for name in data.get('files',[]):
        path=(root/name).resolve()
        if root not in path.parents:errors.append('资源必须在公共发布目录内');continue
        parts=Path(name).parts
        # Only the application's top-level data/acceptance directories contain
        # user state.  Bundled Python libraries legitimately contain their own
        # package data directories and must remain in a portable release.
        if (parts and parts[0].lower() in ('data','acceptance','.git')) or any(p.lower()=='__pycache__' for p in parts):
            errors.append('发布清单包含用户数据或测试产物');continue
        if not path.is_file():errors.append('公共发布文件缺失：'+name);continue
        sha=digest(path)
        if sha in denied:errors.append('公共清单混入受保护私有资产');continue
        try:
            if private_bundle(path,denied):
                errors.append('公共清单混入私有配置包或其中的受保护资产');continue
        except (ValueError,KeyError,TypeError,zipfile.BadZipFile):errors.append('配置归档内容无法校验：'+name);continue
        entries.append({'file':name,'size':path.stat().st_size,'sha256':sha})
    if data.get('publicVoiceProvided'):errors.append('本体不允许附带默认音色')
    if data.get('packageKind')=='environment' and not data.get('runtimeIncluded'):errors.append('完整运行环境尚未进入环境包清单')
    if not data.get('userAccepted'):errors.append('最终程序尚未由用户确认通过，不能打包')
    return {'packageReady':not errors,'files':entries,'errors':errors}

if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('--root',required=True);parser.add_argument('--manifest',required=True);parser.add_argument('--private-manifest')
    args=parser.parse_args();result=audit(args.root,args.manifest,args.private_manifest)
    print(json.dumps(result,ensure_ascii=False,indent=2));sys.exit(0 if result['packageReady'] else 2)
