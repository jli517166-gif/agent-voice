"""Harness-specific final message adapters and an opt-in Codex tail reader."""
import hashlib
import json
import os
from pathlib import Path
import threading
import time

class HarnessPresence:
    """Local evidence of connector liveness, separate from an enable checkbox."""
    def __init__(self,clock=time.monotonic):
        self.clock=clock; self.lock=threading.Lock(); self.seen={}; self.received={}

    def heartbeat(self,source):
        with self.lock:self.seen[source]=self.clock()

    def receipt(self,source):
        with self.lock:self.received[source]=(self.clock(),time.time())

    def snapshot(self,enabled,codex_ready=False):
        now=self.clock()
        with self.lock:
            result={}
            for source,on in enabled.items():
                receipt=self.received.get(source)
                if not on:state='disabled'
                elif source in self.seen and now-self.seen[source]<90:state='connected'
                elif receipt and now-receipt[0]<300:state='recent_event'
                elif source=='codex' and codex_ready:state='monitoring'
                else:state='waiting'
                result[source]={'state':state,'lastReceived':receipt[1] if receipt else None}
            return result

def blocks(content):
    if isinstance(content,str):
        return content
    if not isinstance(content,list):
        return ""
    return "\n".join(str(p.get("text","")) for p in content
                     if isinstance(p,dict) and p.get("type") in ("text","output_text"))

def normalize(source,event):
    if not isinstance(event,dict):
        raise ValueError("Harness 事件必须为对象")
    text=""; session=str(event.get("session_id",event.get("thread-id",event.get("sessionId","unknown"))))
    ident=str(event.get("turn-id",event.get("turn",event.get("messageId",""))))
    if source == "codex":
        if event.get("type") != "agent-turn-complete": return None
        text=event.get("last-assistant-message","")
    elif source == "claude":
        if event.get("hook_event_name") != "Stop": return None
        text=event.get("last_assistant_message","")
    elif source == "deepseek":
        if event.get("type") != "turn/end": return None
        text=event.get("text","")
    elif source == "pi":
        if event.get("type") != "agent_end": return None
        messages=[m for m in event.get("messages",[]) if isinstance(m,dict) and m.get("role")=="assistant" and m.get("stopReason") not in ("toolUse","error","aborted")]
        if messages:
            text=blocks(messages[-1].get("content")); ident=str(messages[-1].get("timestamp",ident))
    elif source == "opencode":
        if event.get("type") != "session.idle": return None
        messages=[m for m in event.get("messages",[]) if isinstance(m,dict) and m.get("info",{}).get("role")=="assistant" and m.get("info",{}).get("time",{}).get("completed") and m.get("info",{}).get("finish") not in ("tool-calls","error")]
        if messages:
            latest=max(messages,key=lambda m:m["info"].get("time",{}).get("completed",0))
            text=blocks(latest.get("parts")); ident=str(latest["info"].get("id",ident))
    else:
        raise ValueError("未知 Harness")
    if not isinstance(text,str) or not text.strip(): return None
    if not ident:
        ident=hashlib.sha256(text.encode()).hexdigest()
    return {"text":text,"source":source,"eventId":source+":"+session+":"+ident}

class CodexTail:
    def __init__(self,on_event,enabled,root=None):
        self.root=Path(root or os.environ.get("CODEX_HOME",str(Path.home()/".codex"))) / "sessions"
        self.on_event=on_event; self.enabled=enabled; self.offsets={}; self.pending={}; self.ignored=set(); self.stopped=threading.Event()
        self.monitor_ready=False
        self.thread=threading.Thread(target=self.run,name="codex-final-messages",daemon=True)

    def run(self):
        initialized=False; was_enabled=False
        while not self.stopped.is_set():
            try:
                files=sorted(self.root.glob("*/*/*/*.jsonl"),key=lambda p:p.stat().st_mtime,reverse=True)[:24]
                enabled=self.enabled()
                for path in files:
                    size=path.stat().st_size
                    if path not in self.offsets or not initialized or not enabled or not was_enabled:
                        with path.open('rb') as stream:
                            try:self.consume(path,json.loads(stream.readline()))
                            except ValueError:pass
                        self.offsets[path]=size; continue
                    offset=self.offsets[path]
                    if size<offset: offset=0
                    if size==offset: continue
                    with path.open("rb") as stream:
                        stream.seek(offset)
                        while True:
                            line=stream.readline()
                            if not line: break
                            if not line.endswith(b"\n"): break
                            offset=stream.tell()
                            try: record=json.loads(line)
                            except ValueError: continue
                            self.consume(path,record)
                    self.offsets[path]=offset
                self.offsets={p:o for p,o in self.offsets.items() if p in files}
                self.pending={p:t for p,t in self.pending.items() if Path(p) in files}
                self.ignored.intersection_update(map(str,files))
                initialized=True; was_enabled=enabled; self.monitor_ready=self.root.is_dir()
            except (OSError,ValueError):
                self.monitor_ready=False
            self.stopped.wait(2)

    def consume(self,path,record):
        payload=record.get("payload",{}); kind=record.get("type")
        if kind=="session_meta":
            source=payload.get('source')
            if isinstance(source,dict) and 'subagent' in source or isinstance(source,str) and source.startswith('subagent'):
                self.ignored.add(str(path))
            return
        if str(path) in self.ignored:return
        if kind=="event_msg" and payload.get("type")=="task_started":
            self.pending.pop(str(path),None)
        if kind=="response_item" and payload.get("role")=="assistant" and payload.get("type")=="message" and payload.get("phase") in ("final_answer","final"):
            self.pending[str(path)]=blocks(payload.get("content"))
        if kind=="event_msg" and payload.get("type")=="task_complete":
            text=payload.get("last_agent_message") or self.pending.pop(str(path),"")
            if text:
                self.on_event("codex",{"type":"agent-turn-complete","thread-id":path.stem,
                    "turn-id":payload.get("turn_id",record.get("timestamp","")),"last-assistant-message":text})

    def start(self): self.thread.start()
    def close(self): self.stopped.set(); self.thread.join(timeout=3); self.monitor_ready=False
