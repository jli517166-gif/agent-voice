"""Relocate only the bundled GPT environment; leave custom environments alone."""
from pathlib import Path
import os
import uuid

def repair_bundled_runtime(root):
    root=Path(root).resolve();runtime=root/'tts-engines/gpt-sovits/runtime'
    base=root/'runtime/core';cfg=runtime/'pyvenv.cfg'
    modern=(runtime/'gpu-packages').is_dir()
    legacy=(root/'tts-engines/candidate-runtime/runtime/Lib/site-packages').is_dir()
    if not modern and not legacy:return []
    if not modern:base=root/'tts-engines/cosyvoice/runtime/tools'
    changed=[]
    def replace(path,text):
        if path.read_text(encoding='utf-8')==text:return
        temp=path.with_name(path.name+'.'+uuid.uuid4().hex+'.tmp')
        try:temp.write_text(text,encoding='utf-8');os.replace(temp,path)
        finally:temp.unlink(missing_ok=True)
        changed.append(str(path.relative_to(root)))
    if cfg.is_file() and (base/'python.exe').is_file():
        lines=cfg.read_text(encoding='utf-8').splitlines()
        lines=[('home = '+str(base)) if line.startswith('home =') else
               ('executable = '+str(base/'python.exe')) if line.startswith('executable =') else line for line in lines]
        if (runtime/'gpu-packages').is_dir() and base==(root/'runtime/core'):
            lines=['include-system-site-packages = false' if line.startswith('include-system-site-packages =') else line for line in lines]
        replace(cfg,'\n'.join(lines)+'\n')
    shared=runtime/'gpu-packages'
    pth=runtime/'Lib/site-packages/000_candidate_gpu.pth'
    if pth.is_file():
        if shared.is_dir():replace(pth,"import sys,pathlib; sys.path.insert(0,str(pathlib.Path(sys.prefix)/'gpu-packages'))\n")
        elif (root/'tts-engines/candidate-runtime/runtime/Lib/site-packages').is_dir():
            replace(pth,"import sys,pathlib; sys.path.insert(0,str(pathlib.Path(sys.prefix).parents[1]/'candidate-runtime'/'runtime'/'Lib'/'site-packages'))\n")
    return changed
