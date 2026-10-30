"""Installation checks never claim that a model or speaker has actually spoken."""
import copy,json,subprocess,sys,shutil,os
from pathlib import Path
from state import atomic_json
from engine_registry import CATALOG,worker_config,gpt_required,model_directory

def inspect_installation(store,deep=False):
    cfg=store.get();checks=[];runtime_cache={}
    def item(name,ok,detail,section='core',warning=False):checks.append({'section':section,'name':name,'ok':bool(ok),'warning':warning,'detail':detail})
    core=store.resolve(cfg['engines']['pythonService'])
    item('核心运行环境',core.is_file(),str(core))
    free=shutil.disk_usage(store.root).free
    item('缓存空间',free>=256*1024**2,'剩余 %.2f GB；建议至少保留 0.25 GB'%(free/1024**3))
    if deep:
        try:
            import sounddevice as sd
            d=sd.query_devices(kind='output');item('播放设备',d.get('max_output_channels',0)>0,str(d['name']))
        except Exception as exc:item('播放设备',False,str(exc))
    for spec in CATALOG['engines']:
        if spec['id']=='dsh-pet':
            voice=copy.deepcopy(cfg['voice']);voice['engine']='dsh-pet'
            section='dsh-pet/dsh-pet'
            missing=store.missing(voice)
            item('桌宠语音桥',not missing,'本机接口 '+voice['petEndpoint'] if not missing else '缺少：'+'；'.join(missing),section)
            if deep and not missing:
                try:
                    from engine_pet import Adapter
                    Adapter(store).prepare(voice,lambda:None)
                    item('桌宠音色接口',True,'音色列表已返回；仍需实际生成和试听',section)
                except Exception as exc:item('桌宠音色接口',False,str(exc),section)
            continue
        python=store.resolve(cfg['engines'][spec['pythonKey']])
        if deep and python.is_file():
            source=store.resolve(cfg['engines'][spec['sourceKey']])
            imports=("from TTS_infer_pack.TTS import TTS,TTS_Config\nimport ffmpeg,matplotlib,resampy" if spec['id']=='gpt-sovits' else "from cosyvoice.cli.cosyvoice import AutoModel")
            extra=source/'GPT_SoVITS' if spec['id']=='gpt-sovits' else source/'third_party/Matcha-TTS'
            setup="import os,sys,torch,librosa,soundfile\nos.chdir("+repr(str(source))+")\nsys.path[:0]="+repr([str(source),str(extra)])+"\n"+imports
            if spec['id']=='gpt-sovits':
                setup="import sys\nsys.path.insert(0,"+repr(str(Path(__file__).parent))+ ")\nfrom gpt_resources import configure\nconfigure("+repr(str(source))+")\n"+setup
            code="import contextlib,sys,json\nwith contextlib.redirect_stdout(sys.stderr):exec("+repr(setup)+")\nprint(json.dumps({'sourceImport':True,'cuda':torch.cuda.is_available(),'gpu':torch.cuda.get_device_name(0) if torch.cuda.is_available() else None,'torch':torch.__version__}))"
            try:
                from pipeline import hidden_process
                environment=os.environ.copy();environment.pop('PYTHONHOME',None);environment.pop('PYTHONPATH',None);environment.update(HF_HUB_OFFLINE='1',TRANSFORMERS_OFFLINE='1',PYTHONNOUSERSITE='1')
                r=subprocess.run([str(python),'-I','-X','utf8','-c',code],capture_output=True,text=True,encoding='utf-8',timeout=60,env=environment,**hidden_process())
                if r.returncode:raise RuntimeError(r.stderr[-1500:])
                runtime_cache[spec['id']]=json.loads(r.stdout)
            except Exception as exc:runtime_cache[spec['id']]={'cuda':False,'error':str(exc)}
        for variant in spec['variants']:
            section=spec['id']+'/'+variant['id'];voice=copy.deepcopy(cfg['voice']);voice.update(engine=spec['id'])
            if spec['id']=='gpt-sovits':voice['gptVariant']=variant['id']
            missing=store.missing(voice)
            model=model_directory(store,voice)
            if spec['id']=='cosyvoice':
                from model_integrity import REQUIRED
                missing += [str(model/n) for n in REQUIRED if not (model/n).is_file()]
                if not (model/'CosyVoice-BlankEN').is_dir():missing.append('CosyVoice-BlankEN')
                source=store.resolve(cfg['engines'][spec['sourceKey']])
                if not (source/'cosyvoice/cli/cosyvoice.py').is_file():missing.append('CosyVoice 源码不完整')
            item(variant['label']+' 文件',not missing,'文件齐全' if not missing else '缺少：'+'；'.join(dict.fromkeys(missing)),section)
            if deep:
                runtime=runtime_cache.get(spec['id'],{})
                item('CUDA 与推理依赖',runtime.get('cuda',False),runtime.get('error') or json.dumps(runtime,ensure_ascii=False) if runtime else 'Python 入口缺失',section)
    ref=store.resolve(cfg['voice']['reference'])
    if ref and ref.is_file():
        try:
            import soundfile as sf
            info=sf.info(ref);seconds=info.duration;valid=seconds>0
            if cfg['voice']['engine']=='gpt-sovits':valid=3<=seconds<=10
            item('当前参考录音',valid,'%.2f 秒，%d 声道；GPT 要求 3～10 秒完整短句'%(seconds,info.channels))
            if store.root not in ref.resolve().parents:item('参考资源位置',True,'参考录音在程序目录之外；保存声音方案，并在换机时自行迁移相关文件',warning=True)
        except Exception as exc:item('当前参考录音',False,str(exc))
    active=cfg['voice']['engine']+'/'+(cfg['voice']['gptVariant'] if cfg['voice']['engine']=='gpt-sovits' else 'dsh-pet' if cfg['voice']['engine']=='dsh-pet' else 'cosyvoice3')
    ok=all(x['ok'] for x in checks if x['section'] in ('core',active) and not x['warning'])
    result={'ok':ok,'deep':deep,'checks':checks,'activeEngine':active,'scope':'文件／环境检查，不代表模型已经生成或实际播放成功'}
    report=store.root/'data/diagnostics/latest.json';atomic_json(report,result);result['report']=str(report)
    return result
