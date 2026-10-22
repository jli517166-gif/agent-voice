"""File checks and offline guards shared by local configuration operations."""

import hashlib,json,shutil,urllib.request,urllib.error

from pathlib import Path

def digest(path):
    h=hashlib.sha256()
    with Path(path).open('rb') as stream:
        for chunk in iter(lambda:stream.read(1024*1024),b''):h.update(chunk)
    return h.hexdigest()

def assert_offline(store):
    cfg=store.get()
    request=urllib.request.Request('http://127.0.0.1:%d/health'%cfg['port'],headers={'Authorization':'Bearer '+cfg['token']})
    try:
        with urllib.request.build_opener(urllib.request.ProxyHandler({})).open(request,timeout=1) as response:
            if json.loads(response.read()).get('service')=='agent-voice-public':raise ValueError('请先关闭语音服务再导入配置')
    except urllib.error.HTTPError as exc:
        if exc.code==401:raise ValueError('端口被其他连接配置占用，不能导入') from exc
        raise
    except (urllib.error.URLError,TimeoutError,ConnectionError):pass

def remove_owned(path,parent):
    # Only generated direct children of the explicit configuration-assets root.
    path=Path(path);parent=Path(parent).resolve()
    if path.is_symlink() or path.resolve().parent!=parent:raise ValueError('拒绝清理非配置暂存目录')
    if path.exists():shutil.rmtree(path)
