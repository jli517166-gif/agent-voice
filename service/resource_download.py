"""Bounded HTTP range downloads with persistent partials and final SHA256."""
import hashlib
import http.client
import json
import os
from pathlib import Path
import re
import time
import urllib.error
import urllib.request
from state import atomic_json


def digest(path):
    result = hashlib.sha256()
    with Path(path).open('rb') as source:
        for block in iter(lambda: source.read(1024*1024), b''):
            result.update(block)
    return result.hexdigest()


def download(url, target, sha256, *, chunk_bytes=8*1024**2, max_bytes=8*1024**3,
             progress=lambda **kw: None, check=lambda: None, backoff=1, timeout=30):
    target = Path(target)
    if not re.fullmatch('[a-f0-9]{64}', sha256):
        raise ValueError('大文件下载必须提供完整 SHA256')
    if target.is_file() and digest(target) == sha256:
        return target
    target.parent.mkdir(parents=True, exist_ok=True)
    partial = target.with_name(target.name+'.part')
    metadata = partial.with_name(partial.name+'.json')
    identity = {'url': url, 'sha256': sha256}
    try:
        previous = json.loads(metadata.read_text(encoding='utf-8'))
    except (OSError, ValueError):
        previous = None
    if previous != identity:
        partial.unlink(missing_ok=True)
    atomic_json(metadata, identity)
    failures = 0
    total = None
    started = time.monotonic()
    initial = partial.stat().st_size if partial.exists() else 0
    last_progress = 0
    while total is None or (partial.stat().st_size if partial.exists() else 0) < total:
        check()
        offset = partial.stat().st_size if partial.exists() else 0
        if offset > max_bytes:
            raise ValueError('下载分片超过限制')
        end = offset+chunk_bytes-1
        request = urllib.request.Request(url, headers={'User-Agent': 'AgentVoice-Downloader/1',
                                        'Range': f'bytes={offset}-{end}', 'Accept-Encoding': 'identity'})
        try:
            with urllib.request.urlopen(request, timeout=timeout) as response:
                if response.status == 206:
                    match = re.fullmatch(r'bytes (\d+)-(\d+)/(\d+)', response.headers.get('Content-Range', ''))
                    if not match or int(match[1]) != offset or not offset <= int(match[2]) <= end or int(match[2]) >= int(match[3]):
                        raise ValueError('服务器返回的下载范围不匹配')
                    received_total = int(match[3])
                    remaining = int(match[2])-offset+1
                elif response.status == 200:
                    # Range is optional: restart if the server only supports a
                    # normal whole-file response; never append duplicate bytes.
                    offset = 0
                    received_total = int(response.headers.get('Content-Length', '0'))
                    remaining = received_total
                else:
                    raise ValueError('下载服务器响应异常')
                if received_total <= 0 or received_total > max_bytes or (total is not None and total != received_total):
                    raise ValueError('下载文件长度异常或发生变化')
                total = received_total
                mode = 'ab' if response.status == 206 and offset else 'wb'
                with partial.open(mode) as output:
                    while remaining:
                        check()
                        block = response.read(min(64*1024, remaining))
                        if not block:
                            raise OSError('下载连接提前结束')
                        output.write(block)
                        remaining -= len(block)
                        count = output.tell()
                        if time.monotonic()-last_progress >= .5 or count == total:
                            progress(downloadedBytes=count, totalBytes=total,
                                     bytesPerSecond=max(0,count-initial)/max(.001,time.monotonic()-started))
                            last_progress = time.monotonic()
            failures = 0
        except (OSError, http.client.HTTPException) as error:
            failures += 1
            if failures >= 4:
                raise RuntimeError('下载连续中断，已保留断点。请检查网络或系统证书后重试：'+str(error)) from error
            time.sleep(backoff*failures)
    check()
    if digest(partial) != sha256:
        partial.unlink(missing_ok=True)
        raise ValueError('下载文件 SHA256 校验失败，未交给安装器')
    os.replace(partial, target)
    metadata.unlink(missing_ok=True)
    progress(downloadedBytes=total, totalBytes=total, bytesPerSecond=max(0,total-initial)/max(.001,time.monotonic()-started))
    return target
