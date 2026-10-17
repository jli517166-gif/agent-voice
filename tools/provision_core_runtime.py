"""Build a CPU-only app runtime from a known working Python, without modifying it."""
import argparse,hashlib,importlib.metadata as md,json,os,shutil,subprocess,sys,uuid
from pathlib import Path
from packaging.requirements import Requirement
from packaging.utils import canonicalize_name

ROOTS=('numpy','soundfile','sounddevice','soxr','PyYAML','Pillow','packaging')
def provision(root):
    root=Path(root).resolve();dest=root/'runtime/core'
    if dest.exists():raise ValueError('核心运行环境已存在，保留现有环境：'+str(dest))
    source=Path(sys.executable).resolve().parent;stage=dest.with_name('.staging-'+uuid.uuid4().hex)
    stage.mkdir(parents=True)
    try:
        for p in source.iterdir():
            if p.is_file() and (p.suffix.lower() in ('.dll','.exe') or p.name=='LICENSE.txt'):shutil.copy2(p,stage/p.name)
        for name in ['DLLs','Lib']:
            shutil.copytree(source/name,stage/name,ignore=shutil.ignore_patterns('site-packages','__pycache__','test','tests','idlelib','turtledemo'))
        site=stage/'Lib/site-packages';site.mkdir()
        selected={};pending=list(ROOTS)
        while pending:
            name=canonicalize_name(pending.pop())
            if name in selected:continue
            dist=md.distribution(name);selected[name]=dist
            for text in dist.requires or []:
                req=Requirement(text)
                if req.marker is None or req.marker.evaluate({'extra':''}):pending.append(req.name)
        records=[]
        for name,dist in sorted(selected.items()):
            base=Path(dist.locate_file('')).resolve()
            for f in dist.files or []:
                p=Path(dist.locate_file(f)).resolve()
                if not p.is_file() or base not in p.parents or '__pycache__' in p.parts:continue
                rel=p.relative_to(base);out=site/rel;out.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(p,out)
            records.append({'name':name,'version':dist.version})
        check="import sys,importlib.util,numpy,soundfile,sounddevice,soxr,yaml,PIL; assert importlib.util.find_spec('torch') is None; print(sys.prefix)"
        probe=subprocess.run([str(stage/'python.exe'),'-X','utf8','-c',check],capture_output=True,text=True,timeout=90,creationflags=getattr(subprocess,'CREATE_NO_WINDOW',0))
        if probe.returncode:raise RuntimeError('核心环境验证失败：'+probe.stderr[-2000:])
        (stage/'runtime-manifest.json').write_text(json.dumps({'role':'core-cpu','python':sys.version.split()[0],'packages':records},ensure_ascii=False,indent=2),encoding='utf-8')
        os.replace(stage,dest)
        return {'ok':True,'python':str(dest/'python.exe'),'packages':len(records),'torchIncluded':False}
    except Exception:
        if stage.exists() and stage.parent.resolve()==(root/'runtime').resolve():shutil.rmtree(stage)
        raise
if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('--root',required=True);a=parser.parse_args();print(json.dumps(provision(a.root),ensure_ascii=False))
