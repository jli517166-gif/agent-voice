"""Single capability catalog and lifecycle contract for installed engine adapters."""
import importlib
import json
from pathlib import Path

CATALOG=json.loads(Path(__file__).with_name('engine-catalog.json').read_text(encoding='utf-8'))
SPECS={s['id']:s for s in CATALOG['engines']}
ENGINE_IDS=tuple(SPECS)

def spec(engine):
    if engine not in SPECS:raise ValueError('不支持的 TTS 引擎：'+str(engine))
    return SPECS[engine]

def variant(voice):return voice['gptVariant'] if voice['engine']=='gpt-sovits' else 'cosyvoice3'

def model_directory(store,voice):
    s=spec(voice['engine']);return store.resolve(voice.get(s['modelField']) or store.get()['engines'][s['modelKey']])

def create_adapter(store,engine):
    # Module names belong to the shipped catalog, never to imported voice packages.
    adapter=importlib.import_module(spec(engine)['adapter']).Adapter(store)
    for name in ['prepare','synthesize','close']:
        if not callable(getattr(adapter,name,None)):raise ValueError('引擎适配器缺少 '+name)
    return adapter

def gpt_required(cfg):
    home=Path(cfg['model']);v=cfg['variant']
    if v not in [v['id'] for v in spec('gpt-sovits')['variants']]:raise ValueError('GPT 版本无效')
    acoustic='gsv-v5-pretrained/s2Gv5turbo.pth' if v=='v5turbo' else 'v2Pro/s2Gv2ProPlus.pth'
    tokenizer=home/'chinese-roberta-wwm-ext-large/tokenizer.json'
    if not tokenizer.is_file():tokenizer=tokenizer.with_name('vocab.txt')
    return [Path(cfg['source'])/'GPT_SoVITS/TTS_infer_pack/TTS.py',home/'s1v3.ckpt',home/acoustic,home/'chinese-hubert-base/pytorch_model.bin',home/'chinese-hubert-base/config.json',home/'chinese-hubert-base/preprocessor_config.json',home/'chinese-roberta-wwm-ext-large/pytorch_model.bin',home/'chinese-roberta-wwm-ext-large/config.json',tokenizer,home/('gsv-v5-pretrained/vocoder.pth' if v=='v5turbo' else 'sv/pretrained_eres2netv2w24s4ep4.ckpt')]

def worker_config(store,voice):
    s=spec(voice['engine']);paths=store.get()['engines']
    return {'engine':s['id'],'reference':str(store.resolve(voice['reference'])),'referenceText':voice['referenceText'],
            'ffmpeg':str(store.resolve(paths['ffmpeg'])),'python':str(store.resolve(paths[s['pythonKey']])),
            'source':str(store.resolve(paths[s['sourceKey']])),'model':str(model_directory(store,voice)),'variant':variant(voice)}
