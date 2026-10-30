"""Fetch the three official cp310 CUDA 12.8 wheels for RTX 50 offline repair.

Each bounded range is resumable. The completed file is accepted only if its
SHA-256 matches the hash published in the official PyTorch package index.
"""
import argparse,concurrent.futures,hashlib,math,os,sys,time,urllib.request
from pathlib import Path

sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'service'))
from torch_download import wheel

INDEX='https://download.pytorch.org/whl/cu128'
PACKAGES=('torch==2.7.1','torchaudio==2.7.1','torchvision==0.22.1')
CHUNK=32*1024*1024

def sha256(path):
    result=hashlib.sha256()
    with path.open('rb') as stream:
        for block in iter(lambda:stream.read(1024*1024),b''):result.update(block)
    return result.hexdigest()

def length(url):
    request=urllib.request.Request(url,headers={'Range':'bytes=0-0','User-Agent':'AgentVoice-Offline/1'})
    with urllib.request.urlopen(request,timeout=30) as response:
        if response.status!=206:raise RuntimeError('PyTorch CDN 没有提供分段下载')
        content=response.headers.get('Content-Range','')
        return int(content.rsplit('/',1)[1])

def one_range(url,folder,index,size):
    start=index*CHUNK;end=min(size-1,start+CHUNK-1);target=folder/(f'{index:05d}.part')
    if target.is_file() and target.stat().st_size==end-start+1:return target
    temporary=target.with_suffix('.download')
    for attempt in range(24):
        try:
            received=temporary.stat().st_size if temporary.is_file() else 0
            if received>end-start+1:
                temporary.unlink();received=0
            if received==end-start+1:
                os.replace(temporary,target)
                return target
            next_start=start+received
            request=urllib.request.Request(url,headers={'Range':f'bytes={next_start}-{end}','User-Agent':'AgentVoice-Offline/1'})
            with urllib.request.urlopen(request,timeout=120) as response:
                if response.status!=206 or response.headers.get('Content-Range','').split('/')[0]!=f'bytes {next_start}-{end}':
                    raise RuntimeError('下载服务返回的分段范围错误')
                with temporary.open('ab') as out:
                    while True:
                        block=response.read(64*1024)
                        if not block:break
                        out.write(block)
            if temporary.stat().st_size!=end-start+1:raise RuntimeError('下载分段长度不符')
            os.replace(temporary,target)
            return target
        except (OSError,RuntimeError) as error:
            if attempt==23:raise RuntimeError(f'分段 {index+1} 失败，已保留其内部断点：{error}') from error
            time.sleep(min(8,1+attempt))

def fetch(package,destination,workers):
    item=wheel(INDEX,package,'cp310');target=destination/item['filename']
    if target.is_file() and sha256(target)==item['sha256']:
        print('校验通过，已存在：'+target.name,flush=True);return target
    size=length(item['url']);folder=destination/(item['filename']+'.chunks');folder.mkdir(parents=True,exist_ok=True)
    count=math.ceil(size/CHUNK);print(f'{target.name}: {size/1e9:.2f} GB，{count} 段',flush=True)
    completed=0
    with concurrent.futures.ThreadPoolExecutor(max_workers=workers) as pool:
        futures=[pool.submit(one_range,item['url'],folder,i,size) for i in range(count)]
        for future in concurrent.futures.as_completed(futures):
            future.result();completed+=1
            if completed==count or completed%4==0:print(f'{target.name}: {completed}/{count} ({completed/count:.1%})',flush=True)
    temporary=destination/(item['filename']+'.assembling')
    try:
        with temporary.open('wb') as out:
            for i in range(count):
                with (folder/f'{i:05d}.part').open('rb') as part:
                    while True:
                        block=part.read(1024*1024)
                        if not block:break
                        out.write(block)
        if temporary.stat().st_size!=size or sha256(temporary)!=item['sha256']:
            raise RuntimeError('官方 SHA-256 校验失败，保留分段供重试')
        os.replace(temporary,target)
    finally:temporary.unlink(missing_ok=True)
    print('SHA-256 校验通过：'+target.name,flush=True)
    return target

def main():
    parser=argparse.ArgumentParser();parser.add_argument('--root',type=Path,required=True);parser.add_argument('--workers',type=int,default=8)
    args=parser.parse_args();root=args.root.resolve();destination=root/'data/environment-repair/wheels'
    if not (root/'runtime/core/python.exe').is_file():raise ValueError('不是 Agent Voice 安装目录：'+str(root))
    if not 1<=args.workers<=16:raise ValueError('并发数需在 1–16 之间')
    destination.mkdir(parents=True,exist_ok=True)
    for package in PACKAGES:fetch(package,destination,args.workers)

if __name__=='__main__':main()
