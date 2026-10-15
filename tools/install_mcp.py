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
        run([codex,"mcp","add",SERVER_NAME,"--",python,server,"--root",store.root,"--host","codex"])
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
    return {"ok":True,"source":"codex","message":"Codex MCP 与语音控制技能已安装。重载插件或新建会话后选择语音控制，输入‘打开朗读面板’测试插件界面；若客户端只显示文字，请继续通过技能调用。内置朗读按钮尚未接管，安装成功不等于界面验证通过。","verification":listing,"chatEntry":plugin.get("installedPath"),"backup":str(backup)}

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
    import yaml
    cli,profile=find_deepseek(store);version=run([cli,"--version"]).splitlines()[-1].strip()
    # The composer UI lives in a separate browser bundle. Installing only the
    # MCP transport leaves the old client.js active even after a full restart.
    from harness_setup import configure
    registry=json.loads((store.root/"data/harness-installations.json").read_text(encoding="utf-8-sig"))
    connector=configure("deepseek",store.root,registry["deepseek"]["directory"])
    package="@deepseek-ai/dsh-mcp-client@"+version
    # Pin the first-party MCP client to the same version as the Harness runtime.
    run([cli,"plugin","--profile","desktop","add",package],timeout=300)
    patch=profile/"cordis.patch.yml";entries=yaml.safe_load(patch.read_text(encoding="utf-8-sig")) if patch.exists() else []
    if entries is None:entries=[]
    if not isinstance(entries,list):raise ValueError("DeepSeek cordis.patch.yml 不是受支持的列表格式")
    backup=store.root/"data/connection-backups"/("mcp-"+uuid.uuid4().hex);backup.mkdir(parents=True)
    if patch.exists():shutil.copy2(patch,backup/"cordis.patch.yml")
    for entry in entries:
        if isinstance(entry,dict) and isinstance(entry.get("insert"),list):entry["insert"]=[item for item in entry["insert"] if item.get("id")!="agent-voice-mcp"]
    entries=[entry for entry in entries if not (isinstance(entry,dict) and set(entry)=={"insert"} and not entry["insert"])]
    entries.append({"insert":[{"id":"agent-voice-mcp","name":"@deepseek-ai/dsh-mcp-client","config":{"serverName":SERVER_NAME,"transport":"stdio","command":str(python),"args":[str(server),"--root",str(store.root),"--host","deepseek"],"cwd":str(store.root),"failOnStartupError":False}}]})
    temp=patch.with_name(patch.name+".agent-voice.tmp");temp.write_text(yaml.safe_dump(entries,allow_unicode=True,sort_keys=False),encoding="utf-8");os.replace(temp,patch)
    import hashlib
    client=profile/"node_modules/dsh-agent-voice-public/client.js"
    expected=store.root/"integrations/deepseek-control-client.js"
    if not client.is_file() or client.read_text(encoding="utf-8-sig")!=expected.read_text(encoding="utf-8-sig"):raise ValueError("聊天栏前端更新校验失败，请重新安装快捷控制")
    return {"ok":True,"source":"deepseek","message":"DeepSeek Harness MCP 与聊天栏界面已更新并校验，请重启 Harness。","profile":str(profile),"backup":str(backup),"clientSha256":hashlib.sha256(client.read_bytes()).hexdigest(),"connector":connector}

def install(source,root):
    store=Store(root);cfg=store.get();python=store.resolve(cfg["engines"]["pythonRvc"]);server=store.root/"integrations/agent_voice_mcp.py"
    if not python.is_file():raise ValueError("主服务 Python 不存在："+str(python))
    if not server.is_file():raise ValueError("MCP 服务文件缺失："+str(server))
    return install_codex(store,python,server) if source=="codex" else install_deepseek(store,python,server)

if __name__=="__main__":
    parser=argparse.ArgumentParser();parser.add_argument("--source",required=True,choices=("codex","deepseek"));parser.add_argument("--root",required=True);args=parser.parse_args()
    try:print(json.dumps(install(args.source,args.root),ensure_ascii=False))
    except (ValueError,OSError,subprocess.SubprocessError) as error:print("MCP 安装失败："+str(error),file=sys.stderr);raise SystemExit(1)
