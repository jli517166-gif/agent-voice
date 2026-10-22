"""Install the local Agent Voice MCP server for verified Harness hosts."""
import argparse
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import uuid
import re

sys.path.insert(0,str(Path(__file__).resolve().parents[1]/"service"))
from state import Store

SERVER_NAME="agent_voice"

def run(command,cwd=None,timeout=180):
    command=[str(item) for item in command]
    suffix=Path(command[0]).suffix.lower()
    if suffix in (".cmd",".bat"):command=[os.environ.get("COMSPEC","cmd.exe"),"/d","/c","call"]+command
    elif suffix==".ps1":command=["powershell.exe","-NoProfile","-ExecutionPolicy","Bypass","-File"]+command
    completed=subprocess.run(command,cwd=str(cwd) if cwd else None,stdout=subprocess.PIPE,stderr=subprocess.PIPE,
        text=True,encoding="utf-8",errors="replace",timeout=timeout,creationflags=getattr(subprocess,"CREATE_NO_WINDOW",0))
    if completed.returncode:
        detail=(completed.stderr or completed.stdout).strip()
        raise ValueError("命令执行失败（%d）：%s"%(completed.returncode,detail[-3000:] or "没有错误输出"))
    return (completed.stdout or completed.stderr).strip()

def find_codex():
    local=Path(os.environ.get("LOCALAPPDATA",""))/"OpenAI/Codex/bin"
    if local.is_dir():
        files=sorted(local.glob("*/codex.exe"),key=lambda item:item.stat().st_mtime,reverse=True)
        if files:return files[0]
    for name in ("codex.exe","codex.cmd","codex"):
        found=shutil.which(name)
        if found:return Path(found)
    raise ValueError("未找到可用的 Codex CLI")

def install_codex(store,python,server):
    codex=find_codex()
    config=Path(os.environ.get("CODEX_HOME",str(Path.home()/".codex")))/"config.toml"
    backup=store.root/"data/connection-backups"/("mcp-"+uuid.uuid4().hex);backup.mkdir(parents=True)
    if config.is_file():shutil.copy2(config,backup/"config.toml")
    existing=subprocess.run([str(codex),"mcp","get",SERVER_NAME],stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL,creationflags=getattr(subprocess,"CREATE_NO_WINDOW",0))
    try:
        if existing.returncode==0:run([codex,"mcp","remove",SERVER_NAME])
        run([codex,"mcp","add",SERVER_NAME,"--",python,"-I","-X","utf8",server,"--root",store.root,"--host","codex"])
        text=config.read_text(encoding="utf-8-sig")
        match=re.search(r"(?m)^\[mcp_servers\.agent_voice\]\s*$",text)
        if not match:raise ValueError("Codex 已添加 MCP，但未找到对应配置段")
        end=re.search(r"(?m)^\[",text[match.end():]);section_end=match.end()+(end.start() if end else len(text)-match.end())
        section=text[match.end():section_end]
        setting='default_tools_approval_mode = "approve"'
        if re.search(r"(?m)^\s*default_tools_approval_mode\s*=",section):section=re.sub(r"(?m)^\s*default_tools_approval_mode\s*=.*$",setting,section)
        else:section="\n"+setting+section
        updated=text[:match.end()]+section+text[section_end:]
        temp=config.with_name(config.name+".agent-voice.tmp");temp.write_text(updated,encoding="utf-8");os.replace(temp,config)
        marketplace=store.root/".agents/plugins/marketplace.json"
        if not marketplace.is_file():raise ValueError("Codex 聊天栏控制入口文件缺失："+str(marketplace))
        # Refresh our dedicated local marketplace so an application update can
        # replace the cached skill and its composer metadata deterministically.
        subprocess.run([str(codex),"plugin","remove","agent-voice-control@agent-voice-local"],stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL,creationflags=getattr(subprocess,"CREATE_NO_WINDOW",0))
        subprocess.run([str(codex),"plugin","marketplace","remove","agent-voice-local"],stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL,creationflags=getattr(subprocess,"CREATE_NO_WINDOW",0))
        market=json.loads(run([codex,"plugin","marketplace","add",store.root,"--json"]))
        plugin=json.loads(run([codex,"plugin","add","agent-voice-control@"+market["marketplaceName"],"--json"]))
    except Exception:
        if (backup/"config.toml").is_file():shutil.copy2(backup/"config.toml",config)
        raise
    listing=run([codex,"mcp","get",SERVER_NAME])
    return {"ok":True,"source":"codex","message":"Codex MCP 语言控制已安装。重载插件或新建会话后选择语音控制，可发送：查看语音状态、开启语音服务、停止朗读、切换声音方案。没有自定义浮窗或朗读面板，也未接管内置朗读。","verification":listing,"chatEntry":plugin.get("installedPath"),"backup":str(backup)}

def find_deepseek(store):
    registry=store.root/"data/harness-installations.json"
    if not registry.is_file():raise ValueError("请先完成 DeepSeek Harness 自动接入验证")
    record=json.loads(registry.read_text(encoding="utf-8-sig")).get("deepseek",{})
    location=Path(record.get("directory",""))
    cli=location/"resources/runtime/cli/bin/dsh.cmd"
    profiles=[Path(item) for item in record.get("deepseekProfiles",[])]
    desktop=next((item for item in profiles if item.name.lower()=="desktop"),Path.home()/".dsh/profiles/desktop")
    if not cli.is_file() or not desktop.is_dir():raise ValueError("DeepSeek 安装记录已失效，请重新执行自动接入")
    return cli,desktop

def install_deepseek(store,python,server):
    from mcp_configuration import (expected_entry,read_patch,replace_entry,write_patch,
        validate_patch,ConnectionTransaction,managed_files,profile_lock)
    from harness_setup import configure
    import hashlib
    entry=expected_entry(store,python,server)
    cli,profile=find_deepseek(store)
    version=run([cli,"--version"]).splitlines()[-1].strip()
    if not re.fullmatch(r"[0-9]+\.[0-9]+\.[0-9]+(?:[-+][0-9A-Za-z.-]+)?",version):
        raise ValueError("无法识别 DeepSeek CLI 版本，未修改配置："+version)
    # Back up BEFORE configure/plugin add; both commands can modify profiles.
    with profile_lock(profile):
        transaction=ConnectionTransaction(store,managed_files(store,profile))
        try:
            registry=json.loads((store.root/"data/harness-installations.json").read_text(encoding="utf-8-sig"))
            connector=configure("deepseek",store.root,registry["deepseek"]["directory"],user_home=profile.parents[2])
            package=profile/"node_modules/@deepseek-ai/dsh-mcp-client"
            try:
                metadata=json.loads((package/"package.json").read_text(encoding="utf-8-sig"))
                installed=(metadata.get("name")=="@deepseek-ai/dsh-mcp-client" and metadata.get("version")==version and (package/"lib/index.js").is_file())
            except (OSError,ValueError):installed=False
            if not installed:
                run([cli,"plugin","--profile","desktop","add","@deepseek-ai/dsh-mcp-client@"+version],timeout=300)
            patch=profile/"cordis.patch.yml"
            write_patch(patch,replace_entry(read_patch(patch),entry))
            validate_patch(patch,store,python,server)
            client=profile/"node_modules/dsh-agent-voice-public/client.js"
            expected=store.root/"integrations/deepseek-control-client.js"
            if not client.is_file() or client.read_text(encoding="utf-8-sig")!=expected.read_text(encoding="utf-8-sig"):
                raise ValueError("聊天栏前端更新校验失败，请重新安装快捷控制")
            transaction.commit()
        except Exception as error:
            try:transaction.rollback()
            except Exception as rollback_error:
                raise ValueError(str(error)+"；"+str(rollback_error)) from error
            raise ValueError("MCP 安装失败，生效配置和连接器已回滚。备份："+str(transaction.backup)+"。原因："+str(error)) from error
    return {"ok":True,"source":"deepseek","message":"DeepSeek MCP 路径、配置与聊天栏文件已回读校验。请重启 Harness，再核对 8 个工具及实际调用；安装不等于已加载。",
            "requiresHarnessReload":True,"harnessToolsVerified":False,"profile":str(profile),"backup":str(transaction.backup),
            "clientSha256":hashlib.sha256(client.read_bytes()).hexdigest(),"connector":connector}


def inspect_deepseek(root,repair=False):
    from mcp_configuration import check_connection
    store=Store(root);cfg=store.get()
    try:_,profile=find_deepseek(store)
    except ValueError as error:
        return {"ok":False,"state":"not-configured","message":str(error)}
    return check_connection(store,profile,store.resolve(cfg["engines"]["pythonService"]),
                            store.root/"integrations/agent_voice_mcp.py",repair)


def install(source,root):
    store=Store(root);cfg=store.get();python=store.resolve(cfg["engines"]["pythonService"]);server=store.root/"integrations/agent_voice_mcp.py"
    if not python.is_file():raise ValueError("主服务 Python 不存在："+str(python))
    if not server.is_file():raise ValueError("MCP 服务文件缺失："+str(server))
    return install_codex(store,python,server) if source=="codex" else install_deepseek(store,python,server)

if __name__=="__main__":
    parser=argparse.ArgumentParser();parser.add_argument("--source",required=True,choices=("codex","deepseek"));parser.add_argument("--root",required=True);actions=parser.add_mutually_exclusive_group();actions.add_argument("--check",action="store_true");actions.add_argument("--repair",action="store_true");args=parser.parse_args()
    try:
        result=inspect_deepseek(args.root,args.repair) if args.check or args.repair else install(args.source,args.root)
        print(json.dumps(result,ensure_ascii=False))
    except (ValueError,OSError,subprocess.SubprocessError) as error:print("MCP 安装失败："+str(error),file=sys.stderr);raise SystemExit(1)
