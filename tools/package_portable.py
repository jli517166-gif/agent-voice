"""Compatibility entry point for a voice-free application package.

Never copies installation data/settings, custom appearance or voice collections.
Archive membership is audited; validation data created in staging is excluded.
"""
import argparse,copy,json,os,shutil,stat,sys,time,zipfile
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'service'))
from release_audit import audit,digest
import runtime_paths

SKIP_DIRS={'.git','__pycache__','.cache','.pytest_cache','.mypy_cache'}
ROOTS={'service','integrations','tools','assets','.agents','docs'}
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

def stage(*args,**kwargs):
    raise ValueError('旧的一体式声音打包已停用，请使用 module_packages.py 分别构建本体、引擎、环境和功能模块')


def release_files(root):
    root=Path(root).resolve()
    for relative,path in files(root,skip={'data','acceptance','voice-model-packages/已导入'}):
        if len(relative.parts)>1 and relative.parts[0] not in ROOTS:raise ValueError('未批准的发布目录：'+str(relative))
        if len(relative.parts)==1 and relative.suffix.lower() not in {'.exe','.md','.json'}:raise ValueError('未批准的发布文件：'+str(relative))
        if relative.parts[0] in {'voices','voice-model-packages','modules'}:raise ValueError('本体禁止附带音色和功能模块')
        if path.suffix.lower()=='.index' or any(p.casefold() in {'小挽昼','星见雅','rvc','rvc20240604nvidia'} for p in relative.parts):raise ValueError('发布包包含排除的音色或变声资源')
        yield relative.as_posix(),path

def archive(root,output,accepted):
    if not accepted:raise ValueError('必须在用户确认发布后使用 --accepted')
    output=Path(output).resolve()
    if output.exists():raise ValueError('目标文件已存在，禁止覆盖')
    from module_packages import application_package
    result=application_package(root,output)
    result.update(packageReady=True,archive=str(output),archiveSha256=result['sha256'],defaultVoice=None)
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
