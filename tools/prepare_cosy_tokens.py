"""Prepare CosyVoice3 attention-adaptation tokens from an explicit labelled CSV.

CPU feature extraction; no ASR guessing, no training, no base model modification.
"""
import argparse,hashlib,json,os,sys,uuid
from pathlib import Path

def prepare(source,base,dataset,output):
    from prepare_training import validate_dataset
    result=validate_dataset(dataset)
    if not result['ok']:raise ValueError('; '.join(result['errors']))
    target=Path(output).resolve()
    if target.exists():raise ValueError('Token output must be a new JSONL file')
    sys.path[:0]=[str(Path(source).resolve()),str(Path(source).resolve()/'third_party/Matcha-TTS')]
    os.environ.update(HF_HUB_OFFLINE='1',TRANSFORMERS_OFFLINE='1',TOKENIZERS_PARALLELISM='false')
    import numpy as np,soundfile as sf,torch,whisper,onnxruntime as ort
    from scipy.signal import resample_poly
    from math import gcd
    from cosyvoice.tokenizer.tokenizer import get_qwen_tokenizer
    torch.set_num_threads(2);base=Path(base).resolve();tokenizer=get_qwen_tokenizer(str(base/'CosyVoice-BlankEN'),True,'cosyvoice3')
    options=ort.SessionOptions();options.intra_op_num_threads=4
    session=ort.InferenceSession(str(base/'speech_tokenizer_v3.onnx'),sess_options=options,providers=['CPUExecutionProvider'])
    target.parent.mkdir(parents=True,exist_ok=True);stage=target.with_name(target.name+'.'+uuid.uuid4().hex+'.tmp');seen=set();count=0;splits={}
    try:
        with stage.open('w',encoding='utf-8') as stream:
            for item in result['items']:
                if not 1.5<=item['duration']<=10:raise ValueError('This small-GPU tool requires 1.5–10 second clips: '+item['file'])
                wave,rate=sf.read(item['file'],dtype='float32');factor=gcd(rate,16000);wave=resample_poly(wave,16000//factor,rate//factor).astype(np.float32)
                digest=hashlib.sha256(wave.tobytes()).hexdigest()
                if digest in seen:raise ValueError('Duplicate decoded/resampled audio; remove duplicates before splitting: '+item['file'])
                seen.add(digest);features=whisper.log_mel_spectrogram(torch.from_numpy(wave).unsqueeze(0),n_mels=128)
                speech=session.run(None,{session.get_inputs()[0].name:features.numpy(),session.get_inputs()[1].name:np.array([features.shape[2]],dtype=np.int32)})[0].flatten().tolist()
                text=item['text'];row={'sha256':digest,'source':item['file'],'text':text,'split':item['split'],'duration':len(wave)/16000,'text_token':tokenizer.encode(text,allowed_special='all'),'speech_token':speech,'instruct_token':tokenizer.encode('You are a helpful assistant.<|endofprompt|>',allowed_special='all')}
                stream.write(json.dumps(row,ensure_ascii=False)+'\n');count+=1;splits[item['split']]=splits.get(item['split'],0)+1
                print(json.dumps({'prepared':count,'total':len(result['items'])}),flush=True)
        os.replace(stage,target)
    finally:stage.unlink(missing_ok=True)
    report={'ok':True,'count':count,'splits':splits,'output':str(target),'speechTokenizerSha256':hashlib.sha256((base/'speech_tokenizer_v3.onnx').read_bytes()).hexdigest(),'scope':'CSV labels are supplied by the user; this tool does not verify spoken text or train a model'}
    target.with_suffix('.report.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),'utf-8');return report

def main():
    p=argparse.ArgumentParser();p.add_argument('--source',required=True);p.add_argument('--base',required=True);p.add_argument('--dataset',required=True);p.add_argument('--output',required=True);a=p.parse_args()
    print(json.dumps(prepare(a.source,a.base,a.dataset,a.output),ensure_ascii=False))

if __name__=='__main__':main()
