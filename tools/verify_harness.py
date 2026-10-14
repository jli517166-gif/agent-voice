"""Launch a real non-interactive Harness turn for end-to-end voice verification."""
import argparse,json,os,shutil,subprocess,sys
from pathlib import Path

NAMES={
    'codex':('codex.exe','codex.cmd','codex'),
    'claude':('claude.exe','claude.cmd','claude'),
    'pi':('pi.exe','pi.cmd','pi'),
    'opencode':('opencode.exe','opencode.cmd','opencode'),
    'deepseek':('dsh.exe','dsh.cmd','dsh'),
}

def candidates(source,location):
    location=Path(location).resolve();seen=set()
    for name in NAMES.get(source,()):
        for path in (location/name,location/'bin'/name,location/'resources'/name,location/'resources'/'app'/name):
            if path.is_file() and str(path).lower() not in seen:seen.add(str(path).lower());yield path
        found=shutil.which(name)
        if found and found.lower() not in seen:seen.add(found.lower());yield Path(found)
    if source=='codex':
        local=Path(os.environ.get('LOCALAPPDATA',''))/'OpenAI/Codex/bin'
        if local.is_dir():
            for path in sorted(local.glob('*/codex.exe'),key=lambda p:p.stat().st_mtime,reverse=True):
                if str(path).lower() not in seen:seen.add(str(path).lower());yield path
    if source=='deepseek':
        for path in (location/'resources/runtime/cli/bin/dsh.cmd',location/'resources/runtime/cli/bin/dsh.exe'):
            if path.is_file() and str(path).lower() not in seen:seen.add(str(path).lower());yield path

def command_for(source,executable,prompt,location):
    if source=='codex':args=['exec','--skip-git-repo-check','-C',str(location),prompt]
    elif source=='claude':args=['-p',prompt]
    elif source=='pi':args=['-p',prompt]
    elif source=='opencode':args=['run',prompt]
    elif source=='deepseek':args=['headless','-']
    else:raise ValueError('尚未配置该 Harness 的自动验证命令')
    executable=Path(executable)
    if executable.suffix.lower() in ('.cmd','.bat'):
        return [os.environ.get('COMSPEC','cmd.exe'),'/d','/c','call',str(executable)]+args
    if executable.suffix.lower()=='.ps1':
        return ['powershell.exe','-NoProfile','-ExecutionPolicy','Bypass','-File',str(executable)]+args
    return [str(executable)]+args

def launch(source,location,prompt,timeout):
    executable=next(candidates(source,location),None)
    if executable is None:
        expected='、'.join(NAMES.get(source,('对应命令行程序',)))
        raise ValueError('没有找到可自动调用的 Harness 命令（'+expected+'）。请确认已安装命令行组件并加入 PATH')
    command=command_for(source,executable,prompt,location)
    environment=os.environ.copy();environment['NO_COLOR']='1';environment['TERM']='dumb'
    try:
        completed=subprocess.run(command,cwd=str(location),env=environment,input=prompt if source=='deepseek' else None,stdin=None if source=='deepseek' else subprocess.DEVNULL,stdout=subprocess.PIPE,stderr=subprocess.PIPE,text=True,encoding='utf-8',errors='replace',timeout=timeout,creationflags=getattr(subprocess,'CREATE_NO_WINDOW',0))
    except subprocess.TimeoutExpired as exc:
        raise ValueError('Harness 在 %d 秒内没有完成验证回复；请检查登录状态、网络或权限弹窗'%timeout) from exc
    detail=(completed.stderr or completed.stdout or '').strip()[-3000:]
    if completed.returncode!=0:raise ValueError('Harness 自动验证命令退出码 %d：%s'%(completed.returncode,detail or '没有错误输出'))
    return {'ok':True,'source':source,'executable':str(executable),'exitCode':completed.returncode,'output':detail}

def main():
    parser=argparse.ArgumentParser();parser.add_argument('--source',required=True);parser.add_argument('--location',required=True);parser.add_argument('--prompt',required=True);parser.add_argument('--timeout',type=int,default=180)
    args=parser.parse_args()
    try:print(json.dumps(launch(args.source,args.location,args.prompt,max(30,min(args.timeout,600))),ensure_ascii=False))
    except (ValueError,OSError) as error:print('Harness 自动验证失败：'+str(error),file=sys.stderr);raise SystemExit(1)

if __name__=='__main__':main()
