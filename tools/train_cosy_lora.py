"""Small-GPU CosyVoice3 LLM adaptation; explicit data, validation and merged output.

Run in a separate compatible training environment. Never overwrites a base model.
The official CosyVoice loss is used; flow/vocoder are frozen and remain unchanged.
"""
import argparse,copy,hashlib,json,math,os,random,sys,time,types
from pathlib import Path

def main():
    p=argparse.ArgumentParser();p.add_argument('--source',required=True);p.add_argument('--base',required=True);p.add_argument('--data',required=True);p.add_argument('--output',required=True);p.add_argument('--steps',type=int,default=240);p.add_argument('--accumulation',type=int,default=4);p.add_argument('--rank',type=int,default=8);p.add_argument('--seed',type=int,default=20261017);p.add_argument('--lr',type=float,default=.0001);a=p.parse_args()
    base=Path(a.base).resolve();out=Path(a.output).resolve()
    if out.exists():raise ValueError('Training output must be a new directory')
    if not 1<=a.steps<=100000 or not 1<=a.rank<=64 or not 1<=a.accumulation<=64:raise ValueError('Invalid training limits')
    out.mkdir(parents=True);os.environ.update(HF_HUB_OFFLINE='1',TRANSFORMERS_OFFLINE='1',TOKENIZERS_PARALLELISM='false')
    sys.path[:0]=[a.source,str(Path(a.source)/'third_party/Matcha-TTS')]
    import torch
    from torch import nn
    from cosyvoice.llm.llm import CosyVoice3LM,Qwen2Encoder
    from cosyvoice.utils.common import ras_sampling
    from cosyvoice.utils.mask import make_pad_mask
    torch.set_num_threads(4);random.seed(a.seed);torch.manual_seed(a.seed)
    data=[json.loads(line) for line in Path(a.data).read_text(encoding='utf-8').splitlines()]
    train=[x for x in data if x['split']=='train'];dev=[x for x in data if x['split']=='dev'][:24];test=[x for x in data if x['split']=='test']
    if len(train)<80 or len(dev)<8 or len(test)<8:raise ValueError('Insufficient independent training/dev/test samples')
    if len({x['sha256'] for x in data})!=len(data):raise ValueError('Duplicate audio in data split')
    device='cuda' if torch.cuda.is_available() else 'cpu';dtype=torch.bfloat16 if device=='cuda' and torch.cuda.is_bf16_supported() else torch.float32
    lm=CosyVoice3LM(896,896,6561,Qwen2Encoder(str(base/'CosyVoice-BlankEN')),ras_sampling)
    state=torch.load(base/'llm.pt',map_location='cpu',weights_only=True);lm.load_state_dict(state,strict=True);lm.to(device=device,dtype=dtype)
    for parameter in lm.parameters():parameter.requires_grad_(False)
    class LoRA(nn.Module):
        def __init__(self,linear):
            super().__init__();self.base=linear;self.scale=2.
            self.A=nn.Parameter(torch.randn(a.rank,linear.in_features,device=device,dtype=torch.float32)*.01)
            self.B=nn.Parameter(torch.zeros(linear.out_features,a.rank,device=device,dtype=torch.float32))
        def forward(self,x):return self.base(x)+((x.float()@self.A.t())@self.B.t()).to(x.dtype)*self.scale
    layers={}
    for name,module in list(lm.named_modules()):
        if isinstance(module,nn.Linear) and name.endswith(('.q_proj','.v_proj')):
            parent=lm.get_submodule(name.rsplit('.',1)[0]);layer=LoRA(module);setattr(parent,name.rsplit('.',1)[1],layer);layers[name]=layer
    if not layers:raise ValueError('No compatible attention layers found')
    # Qwen's LM head is unused for speech; avoid allocating full-vocabulary logits.
    def speech_forward(encoder,xs,lens):
        mask=~make_pad_mask(lens,xs.size(1))
        output=encoder.model.model(inputs_embeds=xs,attention_mask=mask,return_dict=True,use_cache=False)
        return output.last_hidden_state,mask.unsqueeze(1)
    lm.llm.forward=types.MethodType(speech_forward,lm.llm)
    lm.llm.model.config.use_cache=False
    lm.llm.model.model.gradient_checkpointing_enable(gradient_checkpointing_kwargs={'use_reentrant':False})
    params=[p for p in lm.parameters() if p.requires_grad];optimizer=torch.optim.AdamW(params,lr=a.lr,weight_decay=.01)
    def batch(row):
        result={}
        for key in ('text_token','speech_token','instruct_token'):
            result[key]=torch.tensor([row[key]],dtype=torch.int64);result[key+'_len']=torch.tensor([len(row[key])],dtype=torch.int32)
        return result
    def evaluate():
        lm.eval();losses=[];random_state=random.getstate();random.seed(a.seed)
        with torch.no_grad():
            for row in dev:losses.append(float(lm(batch(row),torch.device(device))['loss']))
        random.setstate(random_state);lm.train();return sum(losses)/len(losses)
    report={'engine':'cosyvoice','type':'cosyvoice3-attention-lora-merged','seed':a.seed,'base':str(base),'baseLlmSha256':hashlib.sha256((base/'llm.pt').read_bytes()).hexdigest(),'trainingCount':len(train),'devCount':len(dev),'testCount':len(test),'trainableParameters':sum(p.numel() for p in params),'device':device,'dtype':str(dtype),'flowAndVocoderFrozen':True,'history':[]}
    initial=evaluate();best=initial;best_step=0;best_weights=None;report['initialDevLoss']=initial
    print(json.dumps({'initialDevLoss':initial,'trainable':report['trainableParameters'],'train':len(train),'dev':len(dev),'test':len(test)},ensure_ascii=False),flush=True)
    rng=random.Random(a.seed);order=list(train);rng.shuffle(order);offset=0;started=time.monotonic()
    for step in range(1,a.steps+1):
        optimizer.zero_grad(set_to_none=True);loss_sum=0
        for _ in range(a.accumulation):
            if offset==len(order):rng.shuffle(order);offset=0
            row=order[offset];offset+=1;loss=lm(batch(row),torch.device(device))['loss']
            if not torch.isfinite(loss):raise RuntimeError('Non-finite training loss')
            loss_sum+=float(loss.detach());(loss/a.accumulation).backward()
        torch.nn.utils.clip_grad_norm_(params,1.);optimizer.step()
        if step%10==0:print(json.dumps({'step':step,'loss':loss_sum/a.accumulation,'seconds':round(time.monotonic()-started,2),'peakGpuGB':round(torch.cuda.max_memory_allocated()/1024**3,3) if device=='cuda' else None}),flush=True)
        if step%40==0 or step==a.steps:
            value=evaluate();entry={'step':step,'trainLoss':loss_sum/a.accumulation,'devLoss':value};report['history'].append(entry)
            if value<best:
                best=value;best_step=step;best_weights={name:{'A':layer.A.detach().cpu().clone(),'B':layer.B.detach().cpu().clone()} for name,layer in layers.items()}
                torch.save(best_weights,out/'best-lora.pt')
            (out/'training-report.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8');print(json.dumps(entry),flush=True)
    if best_weights is None:raise RuntimeError('Held-out loss did not improve; do not install as a trained voice')
    merged=copy.copy(state)
    for name,layer in layers.items():
        update=(best_weights[name]['B']@best_weights[name]['A'])*layer.scale
        merged[name+'.weight']=(state[name+'.weight'].float()+update).to(state[name+'.weight'].dtype)
    torch.save(merged,out/'llm.pt')
    report.update(completed=True,bestStep=best_step,bestDevLoss=best,weightChanged=any(not torch.equal(state[name+'.weight'],merged[name+'.weight']) for name in layers),elapsedSeconds=round(time.monotonic()-started,2),requiresListeningApproval=True)
    (out/'training-report.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8');print(json.dumps(report,ensure_ascii=False),flush=True)

if __name__=='__main__':main()
