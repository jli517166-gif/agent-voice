"""Build a clean portable release from explicit code, runtimes and one public voice.

Never copies installation data/settings, custom appearance or voice collections.
Archive membership is audited; validation data created in staging is excluded.
"""
import argparse,copy,json,os,shutil,stat,sys,time,zipfile
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'service'))
from release_audit import audit,digest

SKIP_DIRS={'.git','__pycache__','.cache','.pytest_cache','.mypy_cache'}
ROOTS={'service','integrations','tools','assets','.agents','docs','runtime','tts-engines','voices','voice-model-packages'}
GPT_LINKS={f'GPT_SoVITS/pretrained_models/{name}' for name in ('chinese-hubert-base','chinese-roberta-wwm-ext-large','gsv-v5-pretrained','sv','v2Pro')}

def files(root,skip=()):
    """Do not follow Windows junctions or other reparse points implicitly."""
    root=Path(root).resolve();pending=[root]
    while pending:
        directory=pending.pop()
        for entry in sorted(os.scandir(directory),key=lambda x:x.name):
            relative=Path(entry.path).relative_to(root)
            if relative.as_posix() in skip:continue
            info=entry.stat(follow_symlinks=False)
            if getattr(info,'st_file_attributes',0)&getattr(stat,'FILE_ATTRIBUTE_REPARSE_POINT',0x400) or entry.is_symlink():
                raise ValueError('未登记的链接不能进入便携包：'+str(relative))
            if entry.is_dir(follow_symlinks=False):
                if entry.name not in SKIP_DIRS:pending.append(Path(entry.path))
            elif entry.name not in {'Thumbs.db','.DS_Store'} and Path(entry.name).suffix.lower() not in {'.pyc','.pyo','.tmp','.log'}:
                yield relative,Path(entry.path)

def copy_tree(source,target,skip=()):
    count=size=0
    for relative,path in files(source,skip):
        dest=Path(target)/relative;dest.parent.mkdir(parents=True,exist_ok=True)
        shutil.copy2(path,dest);count+=1;size+=path.stat().st_size
    print(json.dumps({'phase':'copied','folder':str(target),'files':count,'bytes':size},ensure_ascii=False),flush=True)

def stage(build,installation,model,reference,spec,target,commit):
    build=Path(build).resolve();installation=Path(installation).resolve();target=Path(target).resolve()
    if target.exists():raise ValueError('暂存目录必须不存在，不能覆盖旧安装或已有文件')
    if not (build/'agent语音.exe').is_file():raise ValueError('请先构建程序')
    preset=json.loads(Path(spec).read_text('utf-8-sig'))
    if set(preset)!={'version','name','voice'} or preset['name']!='Fairy':raise ValueError('本次发布只允许 Fairy 公共音色')
    from state import validate_voice
    validate_voice(preset['voice'])
    if preset['voice']['assetScope']!='public' or preset['voice']['engine']!='cosyvoice':raise ValueError('禁止发布私有音色')
    target.mkdir(parents=True)
    copy_tree(build,target,skip={'data','acceptance','voice-model-packages','voices','tts-engines'})
    copy_tree(installation/'runtime/core',target/'runtime/core')
    for engine in ('cosyvoice','gpt-sovits'):
        for folder in ('source','runtime'):
            skip=GPT_LINKS|{'GPT_SoVITS/text/G2PWModel'} if engine=='gpt-sovits' and folder=='source' else set()
            copy_tree(installation/'tts-engines'/engine/folder,target/'tts-engines'/engine/folder,skip)
    copy_tree(installation/'tts-engines/gpt-sovits/models/GPT-SoVITS',target/'tts-engines/gpt-sovits/models/GPT-SoVITS')
    copy_tree(installation/'tts-engines/gpt-sovits/nltk_data',target/'tts-engines/gpt-sovits/nltk_data')
    # G2PW must exist at upstream's lookup path. Materialize this one known link,
    # checking its target stays inside the selected installation first.
    g2pw=(installation/'tts-engines/gpt-sovits/source/GPT_SoVITS/text/G2PWModel').resolve()
    if installation not in g2pw.parents:raise ValueError('G2PW 链接指向软件外部')
    copy_tree(g2pw,target/'tts-engines/gpt-sovits/source/GPT_SoVITS/text/G2PWModel')
    copy_tree(model,target/'tts-engines/cosyvoice/models/Fairy-CosyVoice3')
    ref=target/'voices/Fairy/reference.wav';ref.parent.mkdir(parents=True,exist_ok=True)
    if Path(reference).suffix.lower()!='.wav':raise ValueError('默认参考必须是 WAV')
    shutil.copy2(reference,ref)
    voice=copy.deepcopy(preset['voice']);voice.update(baseModel='tts-engines/cosyvoice/models/Fairy-CosyVoice3',reference='voices/Fairy/reference.wav',gptModel='')
    (target/'assets').mkdir(exist_ok=True)
    (target/'assets/bundled-voice.json').write_text(json.dumps({'version':1,'name':'Fairy','voice':voice},ensure_ascii=False,indent=2),'utf-8')
    for name in ('ffmpeg.exe','ffprobe.exe'):
        shutil.copy2(installation/'tools'/name,target/'tools'/name)
    (target/'release-info.json').write_text(json.dumps({'format':'agent-voice-portable','version':1,'commit':commit,'defaultVoice':'Fairy','includedEngines':['cosyvoice','gpt-sovits-v5turbo','gpt-sovits-proplus'],'userDataIncluded':False},ensure_ascii=False,indent=2),'utf-8')
    print(json.dumps({'phase':'staged','directory':str(target)},ensure_ascii=False),flush=True)

def release_files(root):
    root=Path(root).resolve()
    for relative,path in files(root,skip={'data','acceptance','voice-model-packages/已导入'}):
        if len(relative.parts)>1 and relative.parts[0] not in ROOTS:raise ValueError('未批准的发布目录：'+str(relative))
        if len(relative.parts)==1 and relative.suffix.lower() not in {'.exe','.md','.json'}:raise ValueError('未批准的发布文件：'+str(relative))
        if relative.parts[0] in {'voices','voice-model-packages'} and (len(relative.parts)<3 or relative.parts[1]!='Fairy'):raise ValueError('发布包只允许 Fairy 声音资源')
        if path.suffix.lower()=='.index' or any(p.casefold() in {'小挽昼','星见雅','rvc','rvc20240604nvidia'} for p in relative.parts):raise ValueError('发布包包含排除的音色或变声资源')
        yield relative.as_posix(),path

def archive(root,output,accepted):
    root=Path(root).resolve();output=Path(output).resolve()
    if not accepted:raise ValueError('必须在用户确认发布后使用 --accepted')
    if output.exists() or output.with_suffix(output.suffix+'.partial').exists():raise ValueError('目标文件已存在，禁止覆盖')
    entries=list(release_files(root))
    if not entries:raise ValueError('发布目录为空')
    manifest={'publicVoiceProvided':(root/'assets/bundled-voice.json').is_file(),'runtimeIncluded':all((root/p).is_file() for p in ('runtime/core/python.exe','tts-engines/cosyvoice/runtime/tools/python.exe','tts-engines/gpt-sovits/runtime/Scripts/python.exe')),'userAccepted':accepted,'files':[name for name,_ in entries]}
    manifest_path=output.with_suffix('.manifest.json');manifest_path.write_text(json.dumps(manifest,ensure_ascii=False,indent=2),'utf-8')
    print(json.dumps({'phase':'audit','files':len(entries)},ensure_ascii=False),flush=True)
    result=audit(root,manifest_path)
    if not result['packageReady']:raise ValueError('发布审计失败：'+repr(result['errors']))
    partial=output.with_suffix(output.suffix+'.partial');last=time.monotonic()
    with zipfile.ZipFile(partial,'x',compression=zipfile.ZIP_DEFLATED,compresslevel=1,allowZip64=True) as archive:
        for index,(name,path) in enumerate(entries):
            archive.write(path,'Agent语音/'+name)
            if time.monotonic()-last>8:
                print(json.dumps({'phase':'compress','done':index+1,'total':len(entries)},ensure_ascii=False),flush=True);last=time.monotonic()
    print(json.dumps({'phase':'verify-crc'},ensure_ascii=False),flush=True)
    with zipfile.ZipFile(partial) as archive:
        names=archive.namelist()
        if len(names)!=len(set(n.casefold() for n in names)) or len(names)!=len(entries):raise ValueError('归档条目不一致')
        bad=archive.testzip()
        if bad:raise ValueError('归档 CRC 校验失败：'+bad)
    os.replace(partial,output)
    result.update(archive=str(output),archiveBytes=output.stat().st_size,archiveSha256=digest(output),codeCommit=json.loads((root/'release-info.json').read_text('utf-8'))['commit'],defaultVoice='Fairy')
    output.with_suffix('.audit.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),'utf-8')
    print(json.dumps({k:v for k,v in result.items() if k!='files'},ensure_ascii=False),flush=True)
    return result

def main():
    parser=argparse.ArgumentParser();commands=parser.add_subparsers(dest='mode',required=True)
    p=commands.add_parser('stage')
    for name in ('build','installation','model','reference','spec','target','commit'):p.add_argument('--'+name,required=True)
    p=commands.add_parser('archive');p.add_argument('--root',required=True);p.add_argument('--output',required=True);p.add_argument('--accepted',action='store_true')
    args=parser.parse_args();values=vars(args);mode=values.pop('mode')
    if mode=='stage':stage(**values)
    else:archive(**values)

if __name__=='__main__':main()
