"""Visible assistant message adapters and an opt-in Codex tail reader."""
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
        if event.get("type") not in ("agent-turn-complete","agent-message-complete"): return None
        text=event.get("last-assistant-message","")
        if event.get("type")=="agent-message-complete":ident="message:"+ident+":"+str(event.get("message-id") or hashlib.sha256(str(text).encode()).hexdigest())
    elif source == "deepseek":
        if event.get("type") not in ("turn/end","assistant/message"): return None
        text=event.get("text","")
        if event.get("type")=="assistant/message":ident="message:"+str(event.get("messageId") or hashlib.sha256(str(text).encode()).hexdigest())
    else:
        raise ValueError("未知 Harness")
    if not isinstance(text,str) or not text.strip(): return None
    if not ident:
        ident=hashlib.sha256(text.encode()).hexdigest()
    return {"text":text,"source":source,"eventId":source+":"+session+":"+ident}

class CodexTail:
    def __init__(self,on_event,enabled,root=None):
        self.root=Path(root or os.environ.get("CODEX_HOME",str(Path.home()/".codex"))) / "sessions"
        self.on_event=on_event; self.enabled=enabled; self.offsets={}; self.pending={}; self.turns={}; self.progress={}; self.ignored=set(); self.stopped=threading.Event()
        self.monitor_ready=False
        self.thread=threading.Thread(target=self.run,name="codex-visible-messages",daemon=True)

    def run(self):
        initialized=False; was_enabled=False
        while not self.stopped.is_set():
            try:
                files=sorted(self.root.glob("*/*/*/*.jsonl"),key=lambda p:p.stat().st_mtime,reverse=True)[:24]
                enabled=self.enabled()
                for path in files:
                    size=path.stat().st_size
                    if not initialized or not enabled or not was_enabled:
                        with path.open('rb') as stream:
                            try:
                                metadata=json.loads(stream.readline())
                                if metadata.get('type')=='session_meta':self.consume(path,metadata)
                            except ValueError:pass
                        self.offsets[path]=size; continue
                    if path not in self.offsets:self.offsets[path]=0
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
                if len(self.offsets)>1024:self.offsets={p:o for p,o in self.offsets.items() if p in files}
                self.pending={p:t for p,t in self.pending.items() if Path(p) in files}
                self.turns={p:t for p,t in self.turns.items() if Path(p) in files}
                self.progress={p:t for p,t in self.progress.items() if Path(p) in files}
                if len(self.ignored)>1024:self.ignored.intersection_update(map(str,files))
                initialized=True; was_enabled=enabled; self.monitor_ready=self.root.is_dir()
            except (OSError,ValueError):
                self.monitor_ready=False
            self.stopped.wait(.5)

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
            self.progress.pop(str(path),None)
            self.turns[str(path)]=payload.get("turn_id",record.get("timestamp",""))
        if kind=="response_item" and payload.get("role")=="assistant" and payload.get("type")=="message" and payload.get("phase")=="commentary":
            text=blocks(payload.get("content"))
            if text.strip():
                self.progress[str(path)]=text
                self.on_event("codex",{"type":"agent-message-complete","thread-id":path.stem,
                    "turn-id":self.turns.get(str(path),""),"message-id":payload.get("id") or hashlib.sha256(text.encode()).hexdigest(),
                    "last-assistant-message":text})
        if kind=="response_item" and payload.get("role")=="assistant" and payload.get("type")=="message" and payload.get("phase") in ("final_answer","final"):
            self.pending[str(path)]=blocks(payload.get("content"))
        if kind=="event_msg" and payload.get("type")=="task_complete":
            final=self.pending.pop(str(path),"")
            text=payload.get("last_agent_message") or final
            if text and (final or text!=self.progress.get(str(path))):
                self.on_event("codex",{"type":"agent-turn-complete","thread-id":path.stem,
                    "turn-id":payload.get("turn_id",record.get("timestamp","")),"last-assistant-message":text})

    def start(self): self.thread.start()
    def close(self): self.stopped.set(); self.thread.join(timeout=3); self.monitor_ready=False
