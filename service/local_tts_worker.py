"""One persistent GPU model per worker; stdout is JSON protocol only."""
import argparse,contextlib,json,os,sys,traceback
from pathlib import Path

def send(value):print(json.dumps(value,ensure_ascii=False),flush=True)

def main():
    parser=argparse.ArgumentParser();parser.add_argument('--config',required=True);args=parser.parse_args()
    cfg=json.loads(Path(args.config).read_text(encoding='utf-8-sig'))
    os.environ['PATH']=str(Path(cfg['ffmpeg']).parent)+os.pathsep+os.environ.get('PATH','')
    try:
        with contextlib.redirect_stdout(sys.stderr):
            import torch,numpy as np,soundfile as sf
            torch.set_num_threads(4)
            if not torch.cuda.is_available():raise RuntimeError('需要 NVIDIA 显卡与可用的 CUDA 驱动')
            source=Path(cfg['source']);os.chdir(source);sys.path.insert(0,str(source));sys.path.insert(0,str(source/'GPT_SoVITS'))
            from gpt_resources import configure
            configure(source)
            os.environ['PATH']=str(source/'runtime')+os.pathsep+os.environ.get('PATH','')
            from TTS_infer_pack.TTS import TTS,TTS_Config
            import fast_langdetect
            lite=Path(fast_langdetect.__file__).parent/'resources/lid.176.ftz'
            fast_langdetect.infer._default_detector=fast_langdetect.infer.LangDetector(fast_langdetect.infer.LangDetectConfig(custom_model_path=str(lite)))
            home=Path(cfg['model']);variant=cfg['variant'];acoustic='gsv-v5-pretrained/s2Gv5turbo.pth' if variant=='v5turbo' else 'v2Pro/s2Gv2ProPlus.pth'
            # Upstream SV/vocoder defaults point into its source checkout. Resolve
            # these two assets from the selected package as well, without copying
            # weights or changing the user's upstream files.
            import sv
            from unittest.mock import patch
            sv.sv_path=str(home/'sv/pretrained_eres2netv2w24s4ep4.ckpt')
            original_load=torch.load
            def selected_load(file,*args,**kwargs):
                if isinstance(file,(str,Path)) and str(file).replace('\\','/').endswith('/gsv-v5-pretrained/vocoder.pth'):
                    file=home/'gsv-v5-pretrained/vocoder.pth'
                return original_load(file,*args,**kwargs)
            with patch.object(torch,'load',selected_load):
                model=TTS(TTS_Config({'custom':{'device':'cuda','is_half':True,'version':'v5turbo' if variant=='v5turbo' else 'v2ProPlus','t2s_weights_path':str(home/'s1v3.ckpt'),'vits_weights_path':str(home/acoustic),'bert_base_path':str(home/'chinese-roberta-wwm-ext-large'),'cnhuhbert_base_path':str(home/'chinese-hubert-base')}}))
            model.set_ref_audio(cfg['reference'])
        send({'ready':True,'gpu':torch.cuda.get_device_name(0),'engine':cfg['engine'],'variant':variant,'model':str(home.resolve())})
    except Exception as error:traceback.print_exc(file=sys.stderr);send({'ready':False,'error':str(error)});return 1
    for line in sys.stdin:
        try:
            item=json.loads(line);text=item['text'];speed=float(item.get('speed',1))
            if not isinstance(text,str) or not text.strip() or len(text)>4000 or not .7<=speed<=1.3:raise ValueError('文本或语速无效')
            target=Path(item['output']);target.parent.mkdir(parents=True,exist_ok=True)
            with contextlib.redirect_stdout(sys.stderr):
                seed=int(item.get('seed',42));torch.manual_seed(seed)
                chunks=list(model.run({'text':text,'text_lang':'all_zh','ref_audio_path':cfg['reference'],'prompt_text':cfg['referenceText'],'prompt_lang':'all_zh','top_k':15,'top_p':1,'temperature':1,'text_split_method':'cut5','batch_size':1,'speed_factor':speed,'seed':seed,'parallel_infer':True,'use_cuda_graph':False,'use_flash_attention':False,'return_fragment':False,'streaming_mode':False,'sample_steps':4 if cfg['variant']=='v5turbo' else 32}))
                if not chunks:raise RuntimeError('模型没有生成音频')
                rate=chunks[0][0];wav=np.concatenate([x[1] for x in chunks])
                sf.write(target,wav,rate,subtype='PCM_16');torch.cuda.synchronize()
                data,_=sf.read(target,dtype='float32')
                if not np.isfinite(data).all() or not len(data) or float(np.sqrt(np.mean(data*data)))<.0001:raise RuntimeError('模型返回了静音或无效音频')
            send({'ok':True,'sampleRate':rate,'duration':len(data)/rate})
        except Exception as error:traceback.print_exc(file=sys.stderr);send({'ok':False,'error':str(error)})
    return 0

if __name__=='__main__':raise SystemExit(main())
