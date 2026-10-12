"""Claude Stop stdin / Codex notify argv; never blocks the Harness on audio."""
import json
from pathlib import Path
import sys
import urllib.request

def send(source,event,root):
    settings=json.loads((Path(root)/"data/settings.json").read_text(encoding="utf-8-sig"))
    body=json.dumps(event,ensure_ascii=False).encode("utf-8")
    request=urllib.request.Request("http://127.0.0.1:%d/api/harness/%s"%(settings["port"],source),
        data=body,headers={"Content-Type":"application/json","Authorization":"Bearer "+settings["token"]})
    with urllib.request.build_opener(urllib.request.ProxyHandler({})).open(request,timeout=3) as response:
        return json.loads(response.read())

if __name__=="__main__":
    try:
        source,root=sys.argv[1:3]
        payload=sys.argv[3] if len(sys.argv)>3 else sys.stdin.read(128*1024)
        send(source,json.loads(payload),root)
    except Exception:
        # Service-off is a normal state; hooks must not block assistant completion.
        pass
