"""Prepare a source-built desktop candidate and release download instructions.

This does not upload, sign, approve redistribution, or read installed user data.
"""
import argparse,hashlib,json,re,subprocess,zipfile
from pathlib import Path
from urllib.parse import quote
from module_packages import application_package

ROOT=Path(__file__).resolve().parents[1]

def prepare(build,output,version):
    if not re.fullmatch(r'v\d+\.\d+\.\d+(?:-[A-Za-z0-9.]+)?',version):raise ValueError('版本标签无效')
    build=Path(build).resolve();output=Path(output).resolve();output.mkdir(parents=True,exist_ok=True)
    metadata=json.loads((build/'app-version.json').read_text(encoding='utf-8-sig'))
    if metadata.get('format')!='agent-voice-app' or metadata.get('edition')!='public' or metadata.get('platform')!='windows-x64' or metadata.get('version')!=version.removeprefix('v'):
        raise ValueError('公开发行的版本、平台或类型清单不匹配')
    package=output/('AgentVoice-Windows-x64-'+version+'.zip')
    result=application_package(build,package)
    with zipfile.ZipFile(package) as archive:
        names=archive.namelist()
        forbidden={'data','runtime','tts-engines','voices','models','modules','voice-model-packages','trained-voices'}
        for name in names:
            parts=Path(name).parts
            if not name.startswith('Agent语音/') or any(p in forbidden for p in parts):raise ValueError('软件包混入本机数据／环境：'+name)
            if name.endswith(('.pth','.pt','.onnx','.ckpt','.safetensors','.ttsmodel')):raise ValueError('软件包混入权重')
            if name.endswith('.wav') and not re.fullmatch(r'Agent语音/assets/sfx/step-[0-8]\.wav',name):raise ValueError('软件包混入录音')
            if name.endswith(('.py','.js','.html','.css','.json','.md','.yml','.yaml')):
                text=archive.read(name).decode('utf-8-sig')
                if any(word in text for word in ('25908','小挽昼','星见雅','Fairy','PRIVATE_USE=True','Agent语音-私用版')):raise ValueError('软件包混入私人记录：'+name)
        if any(n.endswith('/edition.py') for n in names):raise ValueError('不得使用私用安装制作公开发行')
        if archive.testzip():raise ValueError('归档校验失败')
    guide=ROOT/'下载与首次使用.md'
    text=guide.read_text(encoding='utf-8-sig')
    for local in ('docs/guides/安装手册.md','THIRD_PARTY_NOTICES.md'):
        text=text.replace(']('+local+')','](https://github.com/jli517166-gif/agent-voice/blob/'+version+'/'+quote(local)+')')
    # GitHub normalizes non-ASCII upload basenames to an opaque "default.md".
    published=output/'DOWNLOADS.md';published.write_text(text,encoding='utf-8')
    files=[package,published]
    sums=output/'SHA256SUMS.txt'
    sums.write_text(''.join(hashlib.sha256(p.read_bytes()).hexdigest()+'  '+p.name+'\n' for p in files),encoding='utf-8')
    commit=subprocess.run(['git','rev-parse','HEAD'],cwd=ROOT,capture_output=True,text=True,check=True).stdout.strip()
    return dict(result,version=version,sourceCommit=commit,attachments=[p.name for p in [*files,sums]],
        scope='application and resource download instructions; no engines, runtimes, weights or user recordings',
        signed=False,redistributionReviewRequired=True)

if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('--build',required=True);parser.add_argument('--output',required=True);parser.add_argument('--version',required=True)
    args=parser.parse_args();print(json.dumps(prepare(args.build,args.output,args.version),ensure_ascii=False))
