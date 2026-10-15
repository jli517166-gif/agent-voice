"""Local stdio MCP server for controlling Agent Voice from supported Harnesses.

Stdout is reserved for newline-delimited JSON-RPC.  The server deliberately
reads the local bearer token from the selected Agent Voice installation so it
never appears in a Harness configuration file.
"""
import argparse
import json
import os
from pathlib import Path
import subprocess
import sys
import time
import base64
import urllib.error
import urllib.request

TOOLS = [
    {"name":"voice_service_status","description":"查看 Agent Voice 服务、播放队列、当前声音方案和本 Harness 自动朗读开关。","inputSchema":{"type":"object","properties":{},"additionalProperties":False},"annotations":{"readOnlyHint":True,"destructiveHint":False,"idempotentHint":True}},
    {"name":"start_voice_service","description":"启动本机 Agent Voice 后台语音服务。","inputSchema":{"type":"object","properties":{},"additionalProperties":False},"annotations":{"readOnlyHint":False,"destructiveHint":False,"idempotentHint":True}},
    {"name":"stop_voice_service","description":"关闭本机 Agent Voice 后台语音服务。","inputSchema":{"type":"object","properties":{},"additionalProperties":False},"annotations":{"readOnlyHint":False,"destructiveHint":False,"idempotentHint":True}},
    {"name":"stop_speech","description":"立即停止当前朗读并清空尚未播放的语音队列。","inputSchema":{"type":"object","properties":{},"additionalProperties":False},"annotations":{"readOnlyHint":False,"destructiveHint":False,"idempotentHint":True}},
    {"name":"list_voice_profiles","description":"列出已保存的声音方案及当前使用的声音。","inputSchema":{"type":"object","properties":{},"additionalProperties":False},"annotations":{"readOnlyHint":True,"destructiveHint":False,"idempotentHint":True}},
    {"name":"apply_voice_profile","description":"按声音方案 ID 切换当前声音配置。","inputSchema":{"type":"object","properties":{"profile_id":{"type":"string","description":"list_voice_profiles 返回的声音方案 ID"}},"required":["profile_id"],"additionalProperties":False},"annotations":{"readOnlyHint":False,"destructiveHint":False,"idempotentHint":True}},
    {"name":"set_auto_reading","description":"启用或关闭当前 Harness 最终回复的自动朗读。","inputSchema":{"type":"object","properties":{"enabled":{"type":"boolean"}},"required":["enabled"],"additionalProperties":False},"annotations":{"readOnlyHint":False,"destructiveHint":False,"idempotentHint":True}},
]

class AgentVoice:
    def __init__(self,root,host):
        self.root=Path(root).resolve();self.host=host
        if host not in ("codex","deepseek"):raise ValueError("MCP 当前仅支持 codex 或 deepseek")

    def settings(self):
        path=self.root/"data/settings.json"
        if not path.is_file():raise ValueError("未找到 Agent Voice 设置："+str(path))
        return json.loads(path.read_text(encoding="utf-8-sig"))

    def request(self,path,data=None,timeout=5):
        cfg=self.settings();body=None if data is None else json.dumps(data,ensure_ascii=False).encode("utf-8")
        request=urllib.request.Request("http://127.0.0.1:%d%s"%(cfg["port"],path),data=body,
            headers={"Authorization":"Bearer "+cfg["token"],"Content-Type":"application/json; charset=utf-8"})
        try:
            with urllib.request.urlopen(request,timeout=timeout) as response:return json.loads(response.read().decode("utf-8"))
        except urllib.error.HTTPError as error:
            try:message=json.loads(error.read().decode("utf-8"))["error"]["message"]
            except Exception:message=str(error)
            raise ValueError(message) from error

    def running(self):
        try:return self.request("/health",timeout=1)
        except (OSError,ValueError,urllib.error.URLError):return None

    def start(self):
        current=self.running()
        if current:return {"ok":True,"alreadyRunning":True,"status":self.public_status(current)}
        cfg=self.settings();value=cfg["engines"]["pythonRvc"]
        python=(self.root/value).resolve() if not Path(value).is_absolute() else Path(value)
        launcher=self.root/"service/launcher.py"
        if not python.is_file():raise ValueError("主服务 Python 不存在："+str(python))
        environment=os.environ.copy();environment.pop("PYTHONHOME",None);environment.pop("PYTHONPATH",None)
        environment.update(PYTHONDONTWRITEBYTECODE="1",PYTHONUTF8="1")
        command=[str(python),"-X","utf8","-u",str(launcher),"--root",str(self.root)]
        if os.name=="nt":
            # Codex and some Electron Harnesses put MCP servers in a Windows
            # job object that kills every child when the stdio session ends.
            # WMI owns the created process, so the voice service keeps running
            # after this short-lived MCP process exits.
            def ps_quote(value):return "'"+str(value).replace("'","''")+"'"
            line=subprocess.list2cmdline(command)
            script=("$startup=New-CimInstance -ClassName Win32_ProcessStartup -ClientOnly -Property @{ShowWindow=[uint16]0};"
                    "$r=Invoke-CimMethod -ClassName Win32_Process -MethodName Create -Arguments "
                    "@{CommandLine="+ps_quote(line)+";CurrentDirectory="+ps_quote(self.root)+";ProcessStartupInformation=$startup};"
                    "if($r.ReturnValue -ne 0){exit [int]$r.ReturnValue}")
            encoded=base64.b64encode(script.encode("utf-16le")).decode("ascii")
            completed=subprocess.run(["powershell.exe","-NoLogo","-NoProfile","-NonInteractive","-EncodedCommand",encoded],
                cwd=str(self.root),env=environment,stdin=subprocess.DEVNULL,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL,
                creationflags=getattr(subprocess,"CREATE_NO_WINDOW",0),timeout=15)
            if completed.returncode:raise ValueError("无法创建常驻语音服务进程，Windows 返回："+str(completed.returncode))
        else:
            subprocess.Popen(command,cwd=str(self.root),env=environment,stdin=subprocess.DEVNULL,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL,start_new_session=True,close_fds=True)
        for _ in range(80):
            time.sleep(.15);current=self.running()
            if current:return {"ok":True,"started":True,"status":self.public_status(current)}
        raise ValueError("服务启动超时，请查看 data/logs/service.log")

    def public_status(self,health):
        cfg=self.settings();profiles=cfg.get("profiles",[]);current=cfg.get("voice",{})
        match=next((item for item in profiles if item.get("voice")==current),None)
        return {"running":True,"ready":health.get("ready"),"busy":health.get("busy"),"queued":health.get("queued"),
            "currentProfile":{"id":match.get("id"),"name":match.get("name")} if match else None,
            "autoReading":bool(cfg.get("harness",{}).get(self.host)),"host":self.host,"error":health.get("error")}

    def call(self,name,args):
        args=args or {}
        if name=="voice_service_status":
            health=self.running()
            if not health:
                cfg=self.settings();return {"running":False,"host":self.host,"autoReading":bool(cfg.get("harness",{}).get(self.host))}
            return self.public_status(health)
        if name=="start_voice_service":return self.start()
        if name=="stop_voice_service":
            if not self.running():return {"ok":True,"alreadyStopped":True}
            return self.request("/api/shutdown",{})
        if name=="stop_speech":
            if not self.running():raise ValueError("语音服务尚未启动")
            return self.request("/stop",{})
        if name=="list_voice_profiles":
            cfg=self.settings();current=cfg.get("voice",{})
            return {"profiles":[{"id":item["id"],"name":item["name"],"mode":item.get("voice",{}).get("synthesisMode"),"active":item.get("voice")==current} for item in cfg.get("profiles",[])]}
        if name=="apply_voice_profile":
            ident=args.get("profile_id");cfg=self.settings();item=next((p for p in cfg.get("profiles",[]) if p.get("id")==ident),None)
            if item is None:raise ValueError("声音方案不存在，请先调用 list_voice_profiles")
            if not self.running():raise ValueError("请先启动语音服务，再切换声音方案")
            self.request("/api/config",{"voice":item["voice"]},timeout=30)
            return {"ok":True,"profile":{"id":item["id"],"name":item["name"]}}
        if name=="set_auto_reading":
            if not isinstance(args.get("enabled"),bool):raise ValueError("enabled 必须是布尔值")
            if not self.running():raise ValueError("请先启动语音服务，再修改自动朗读")
            cfg=self.settings();harness=dict(cfg.get("harness",{}));harness[self.host]=args["enabled"]
            self.request("/api/config",{"harness":harness})
            if self.host=="codex":self.request("/api/connections/reload",{})
            return {"ok":True,"host":self.host,"enabled":args["enabled"]}
        raise ValueError("未知工具："+str(name))

def response(ident,result=None,error=None):
    body={"jsonrpc":"2.0","id":ident}
    if error is not None:body["error"]={"code":-32000,"message":str(error)}
    else:body["result"]=result
    sys.stdout.write(json.dumps(body,ensure_ascii=False,separators=(",",":"))+"\n");sys.stdout.flush()

def main():
    if hasattr(sys.stdin,"reconfigure"):sys.stdin.reconfigure(encoding="utf-8",errors="strict")
    if hasattr(sys.stdout,"reconfigure"):sys.stdout.reconfigure(encoding="utf-8",errors="strict",newline="\n")
    parser=argparse.ArgumentParser();parser.add_argument("--root",required=True);parser.add_argument("--host",required=True,choices=("codex","deepseek"));args=parser.parse_args()
    voice=AgentVoice(args.root,args.host)
    for line in sys.stdin:
        try:
            message=json.loads(line);ident=message.get("id");method=message.get("method")
            if ident is None:continue
            if method=="initialize":
                requested=message.get("params",{}).get("protocolVersion","2025-06-18")
                response(ident,{"protocolVersion":requested,"capabilities":{"tools":{"listChanged":False}},"serverInfo":{"name":"agent-voice-control","version":"1.0.0"},"instructions":"控制本机 Agent Voice。执行切换或停止等有状态操作前，应确认用户意图。"})
            elif method=="ping":response(ident,{})
            elif method=="tools/list":response(ident,{"tools":TOOLS})
            elif method=="tools/call":
                params=message.get("params",{});result=voice.call(params.get("name"),params.get("arguments",{}))
                response(ident,{"content":[{"type":"text","text":json.dumps(result,ensure_ascii=False)}],"structuredContent":result,"isError":False})
            else:response(ident,error="不支持的 MCP 方法："+str(method))
        except Exception as error:
            if 'ident' in locals() and ident is not None:response(ident,{"content":[{"type":"text","text":str(error)}],"isError":True})
            else:print("agent-voice MCP: "+str(error),file=sys.stderr,flush=True)

if __name__=="__main__":main()
