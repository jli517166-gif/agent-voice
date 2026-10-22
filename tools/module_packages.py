"""Separate application, engine and offline-environment packages.

The manifest is consumed by the native desktop installer, including on machines
without Python. Voice archives retain their existing engine-labelled format.
"""
import argparse, hashlib, json, os, re, stat, sys, zipfile
from pathlib import Path

ENGINES=('cosyvoice','gpt-sovits')
SKIP={'.git','__pycache__','.cache','.pytest_cache','tests','test'}

def digest(path):
    result=hashlib.sha256()
    with Path(path).open('rb') as stream:
        for block in iter(lambda:stream.read(1024*1024),b''):result.update(block)
    return result.hexdigest()

def inventory(root,relative):
    root=Path(root).resolve();start=root/relative
    if not start.exists():raise ValueError('缺少打包资源：'+str(start))
    pending=[start]
    while pending:
        path=pending.pop();info=path.lstat()
        if path.is_symlink() or getattr(info,'st_file_attributes',0)&getattr(stat,'FILE_ATTRIBUTE_REPARSE_POINT',0x400):
            # Only the known G2PW lookup is materialized. Reject all other links.
            key=path.relative_to(root).as_posix()
            if key!='tts-engines/gpt-sovits/source/GPT_SoVITS/text/G2PWModel' or root not in path.resolve().parents:
                if '/GPT_SoVITS/pretrained_models/' in key:continue  # adapter resolves the separate model library
                raise ValueError('未登记或外部链接：'+key)
            for source in path.resolve().rglob('*'):
                if source.is_symlink():raise ValueError('G2PW 内部不能包含链接')
                if source.is_file():yield key+'/'+source.relative_to(path.resolve()).as_posix(),source
            continue
        if path.is_dir():pending.extend(p for p in sorted(path.iterdir(),reverse=True) if p.name not in SKIP)
        elif path.suffix.lower() not in {'.pyc','.pyo','.log','.tmp','.part'}:yield path.relative_to(root).as_posix(),path

def write_package(output,kind,engine,components,paths,defaults,profile='bundled',minimum_driver='',feature=None):
    output=Path(output).resolve()
    suffix='.ttsengine' if kind=='engine' else '.avmodule' if kind=='feature' else '.ttsenvironment'
    if not str(output).endswith(suffix) or output.exists():raise ValueError('输出需要新文件，后缀为 '+suffix)
    paths=list(paths);names=[p[0] for p in paths]
    if not paths or len(names)!=len(set(n.casefold() for n in names)):raise ValueError('模块资源为空或路径重复')
    manifest={'format':'agent-voice-module','version':1,'kind':kind,'name':engine+' '+kind,
              'engine':engine,'platform':'windows-x64','profile':profile,'minimumDriver':minimum_driver,
              'components':components,'defaults':defaults,
              'files':[{'path':name,'size':path.stat().st_size,'sha256':digest(path)} for name,path in paths]}
    if feature:manifest.update(feature)
    output.parent.mkdir(parents=True,exist_ok=True);temporary=output.with_name(output.name+'.partial')
    if temporary.exists():raise ValueError('存在未完成包，请改名或检查旧操作')
    try:
        with zipfile.ZipFile(temporary,'x',compression=zipfile.ZIP_DEFLATED,compresslevel=1,allowZip64=True) as archive:
            archive.writestr('manifest.json',json.dumps(manifest,ensure_ascii=False,separators=(',',':')))
            for name,path in paths:archive.write(path,'payload/'+name)
        with zipfile.ZipFile(temporary) as archive:
            if archive.testzip():raise ValueError('模块归档 CRC 校验失败')
        os.replace(temporary,output)
    finally:temporary.unlink(missing_ok=True)
    result={'ok':True,'kind':kind,'file':str(output),'files':len(paths),'bytes':output.stat().st_size,'sha256':digest(output)}
    output.with_name(output.name+'.sha256').write_text(result['sha256']+'  '+output.name+'\n',encoding='utf-8')
    return result

def engine_package(root,engine,output,model=None):
    if engine not in ENGINES:raise ValueError('未适配引擎')
    root=Path(root).resolve();prefix='tts-engines/'+engine
    model=model or (prefix+'/models/'+('Fun-CosyVoice3-0.5B' if engine=='cosyvoice' else 'GPT-SoVITS'))
    if not re.fullmatch(re.escape(prefix)+r'/models/[^./\\:][^/\\:]*',model):raise ValueError('基础模型必须位于该引擎 models 的独立子目录')
    components=[prefix+'/source',model]
    if engine=='gpt-sovits':components.append(prefix+'/nltk_data')
    key='cosy' if engine=='cosyvoice' else 'gpt'
    defaults={key+'Repo':prefix+'/source',key+'Model':model}
    return write_package(output,'engine',engine,components,(entry for c in components for entry in inventory(root,c)),defaults)

def environment_package(root,engines,output,include_downloads=False,profile='bundled',minimum_driver=''):
    if not set(engines)<=set(ENGINES):raise ValueError('未适配引擎')
    root=Path(root).resolve();components=['runtime/core','tools/ffmpeg.exe','tools/ffprobe.exe']
    defaults={'pythonService':'runtime/core/python.exe','ffmpeg':'tools/ffmpeg.exe'}
    for engine in dict.fromkeys(engines):
        components.append('tts-engines/'+engine+'/runtime')
        defaults['pythonTts' if engine=='cosyvoice' else 'pythonGpt']='tts-engines/'+engine+'/runtime/'+('tools/python.exe' if engine=='cosyvoice' else 'Scripts/python.exe')
    if include_downloads:
        for relative in ['data/environment-repair/wheels','data/environment-repair/downloads']:
            if (root/relative).is_dir():components.append(relative)
    if (root/'tools/system-prerequisites').is_dir():components.append('tools/system-prerequisites')
    return write_package(output,'environment','all',components,(entry for c in components for entry in inventory(root,c)),defaults,profile,minimum_driver)

def application_package(build,output):
    build=Path(build).resolve();output=Path(output).resolve()
    if output.exists():raise ValueError('不能覆盖已有软件包')
    if not (build/'agent语音.exe').is_file():raise ValueError('请先构建软件本体')
    ui_libraries=['Microsoft.Web.WebView2.Core.dll','Microsoft.Web.WebView2.WinForms.dll','WebView2Loader.dll']
    roots=['agent语音.exe','service','tools','integrations','docs','.agents']
    roots.extend(name for name in ui_libraries if (build/name).is_file())
    paths=[entry for relative in roots for entry in inventory(build,relative)]
    paths.extend((p.name,p) for p in build.glob('*.md'))
    forbidden={'service/tts_bundle.py','service/config_bundle.py','tools/tts_bundle.py','tools/config_bundle.py'}
    paths=[(name,path) for name,path in paths if name not in forbidden and '/__pycache__/' not in name]
    # Public stock graphics only; no bundled-voice manifest or user imagery.
    if (build/'assets').is_dir():paths.extend((n,p) for n,p in inventory(build,'assets') if n.startswith('assets/') and (p.suffix.lower() in {'.png','.ico','.svg'} or re.fullmatch(r'assets/sfx/step-[0-8]\.wav',n) or n=='assets/onboarding/layout.json' or (n.startswith('assets/setup/') and p.suffix.lower() in {'.html','.css','.js','.json','.webp'})))
    output.parent.mkdir(parents=True,exist_ok=True)
    temporary=output.with_name(output.name+'.partial')
    if temporary.exists():raise ValueError('存在未完成的软件包，请检查后重试')
    try:
        with zipfile.ZipFile(temporary,'x',compression=zipfile.ZIP_DEFLATED,compresslevel=1) as archive:
            for name,path in paths:
                if path.suffix.lower() in {'.exe','.dll'} and name not in ['agent语音.exe']+ui_libraries:continue
                archive.write(path,'Agent语音/'+name)
        with zipfile.ZipFile(temporary) as archive:
            if archive.testzip():raise ValueError('软件本体 CRC 校验失败')
        os.replace(temporary,output)
    finally:temporary.unlink(missing_ok=True)
    return {'ok':True,'kind':'application','file':str(output),'bytes':output.stat().st_size,'sha256':digest(output)}


def feature_package(root,output,ident='voice-packages',name='音色包导入导出',version='1.0.0'):
    if not re.fullmatch(r'[a-z][a-z0-9-]{0,63}',ident):raise ValueError('功能模块标识无效')
    root=Path(root).resolve();prefix='modules/'+ident
    if not (root/prefix/'module.exe').is_file():raise ValueError('请先单独构建功能模块')
    paths=list(inventory(root,prefix))
    if any(Path(n).suffix.lower() not in {'.exe','.dll','.py','.json','.md','.txt','.png','.svg','.gif'} or n==prefix+'/module.json' for n,p in paths):raise ValueError('功能模块只能包含实现和界面资源，不能携带录音和权重')
    return write_package(output,'feature','',[prefix],paths,{},feature={'id':ident,'name':name,'moduleVersion':version,'apiVersion':1,'entrypoint':prefix+'/module.exe'})

def main():
    parser=argparse.ArgumentParser();parser.add_argument('kind',choices=['application','engine','environment','feature']);parser.add_argument('--root',required=True);parser.add_argument('--output',required=True)
    parser.add_argument('--engine',choices=ENGINES);parser.add_argument('--engines',nargs='*',choices=ENGINES,default=list(ENGINES));parser.add_argument('--model');parser.add_argument('--include-downloads',action='store_true');parser.add_argument('--profile',default='bundled');parser.add_argument('--minimum-driver',default='')
    args=parser.parse_args()
    if args.kind=='application':result=application_package(args.root,args.output)
    elif args.kind=='feature':result=feature_package(args.root,args.output)
    elif args.kind=='engine':result=engine_package(args.root,args.engine,args.output,args.model)
    else:result=environment_package(args.root,args.engines,args.output,args.include_downloads,args.profile,args.minimum_driver)
    print(json.dumps(result,ensure_ascii=False))

if __name__=='__main__':main()
