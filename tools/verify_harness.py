"""Launch a real non-interactive Harness turn for end-to-end voice verification."""
import argparse,json,os,shutil,subprocess,sys
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'service'))
from owned_process import terminate

NAMES={
    'codex':('codex.exe','codex.cmd','codex.ps1','codex'),
    'deepseek':('dsh.exe','dsh.cmd','dsh.ps1','dsh'),
}

def is_account_error(detail):
    """The desktop account session is not available to a headless CLI run."""
    text=str(detail or '').lower()
    return any(marker in text for marker in ('account_sign_in_required','missing_credential','no api key','sign in to deepseek'))

def manual_challenge(prompt):
    lines=[line.strip() for line in str(prompt or '').splitlines() if line.strip()]
    return lines[-1] if lines else ''

def manual_result(source,executable,prompt,detail):
    challenge=manual_challenge(prompt)
    text=('DeepSeek Harness 的账号登录只对桌面端有效，headless 无法自动完成验证。\r\n\r\n'
          '验证短句已复制到剪贴板：\r\n'+challenge+'\r\n\r\n'
          '请打开桌面 Harness，把它粘贴到聊天框发送；程序会继续等待连接器和播放回执。\r\n\r\n'
          '如果希望全自动，请在 Harness 的 Models 页面配置 API Key 后重新选择目录接入。')
    if not os.environ.get('AGENT_VOICE_NO_UI'):
        try:subprocess.run(['cmd','/c','clip'],input=challenge,text=True,encoding='utf-8',creationflags=getattr(subprocess,'CREATE_NO_WINDOW',0),timeout=15)
        except (OSError,subprocess.SubprocessError):pass
        try:
            import ctypes
            ctypes.windll.user32.MessageBoxW(0,text,'Agent 语音 · 需要手动完成验证',0x40)
        except Exception:pass
    return {'ok':False,'manualRequired':True,'source':source,'executable':str(executable),'exitCode':None,'output':detail,'challenge':challenge,'message':text}

def candidates(source,location):
    location=Path(location).resolve();seen=set()
    for name in NAMES.get(source,()):
        for path in (location/name,location/'bin'/name,location/'resources'/name,location/'resources'/'app'/name):
            if path.is_file() and str(path).lower() not in seen:seen.add(str(path).lower());yield path
    for name in NAMES.get(source,()):
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
    with subprocess.Popen(command,cwd=str(location),env=environment,stdin=subprocess.PIPE if source=='deepseek' else subprocess.DEVNULL,stdout=subprocess.PIPE,stderr=subprocess.PIPE,text=True,encoding='utf-8',errors='replace',creationflags=getattr(subprocess,'CREATE_NO_WINDOW',0)) as process:
        try:out,err=process.communicate(prompt if source=='deepseek' else None,timeout=timeout)
        except subprocess.TimeoutExpired as exc:
            terminate(process);process.communicate()
            raise ValueError('Harness 在 %d 秒内没有完成验证回复；请检查登录状态、网络或权限弹窗'%timeout) from exc
        detail=(err or out or '').strip()[-3000:]
        if process.returncode!=0:
            if source=='deepseek' and is_account_error(detail):return manual_result(source,executable,prompt,detail)
            raise ValueError('Harness 自动验证命令退出码 %d：%s'%(process.returncode,detail or '没有错误输出'))
        return {'ok':True,'source':source,'executable':str(executable),'exitCode':process.returncode,'output':detail}

def probe(source,location):
    executable=next(candidates(source,location),None)
    if executable is None:
        expected='、'.join(NAMES.get(source,('对应命令行程序',)))
        raise ValueError('连接器可以安装，但没有找到真实验证所需的 Harness 命令（'+expected+'）。请安装命令行组件，或选择包含该命令的安装目录')
    return {'ok':True,'stage':'cli','source':source,'executable':str(executable)}

def main():
    parser=argparse.ArgumentParser();parser.add_argument('--source',required=True);parser.add_argument('--location',required=True);parser.add_argument('--prompt');parser.add_argument('--probe',action='store_true');parser.add_argument('--timeout',type=int,default=180)
    args=parser.parse_args()
    try:
        if args.probe:result=probe(args.source,args.location)
        elif args.prompt:result=launch(args.source,args.location,args.prompt,max(30,min(args.timeout,600)))
        else:raise ValueError('缺少 --prompt；只检查命令行组件时请使用 --probe')
        print(json.dumps(result,ensure_ascii=False))
    except (ValueError,OSError) as error:print('Harness 自动验证失败：'+str(error),file=sys.stderr);raise SystemExit(1)

if __name__=='__main__':main()
