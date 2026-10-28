"""Local model validation and identity; no archive import or export."""

import hashlib,json,re

from pathlib import Path

from file_integrity import digest

REQUIRED = ('cosyvoice3.yaml','llm.pt','flow.pt','hift.pt','campplus.onnx','speech_tokenizer_v3.onnx')

MODEL_SUFFIXES = {'.pt','.onnx','.yaml','.yml','.json','.safetensors','.bin','.txt','.model','.tiktoken','.md','.vocab'}

GPT_MODEL_SUFFIXES = MODEL_SUFFIXES | {'.pth','.ckpt'}

def gpt_sources(directory, variant):
    from local_tts import required_files
    directory=Path(directory).resolve()
    required=required_files({'engine':'gpt-sovits','model':str(directory),'source':'.','variant':variant})[1:]
    for file in required:
        if not file.is_file():raise ValueError('GPT-SoVITS 模型缺少文件：'+str(file))
    files=list(required)
    for name in ('chinese-hubert-base','chinese-roberta-wwm-ext-large'):
        files.extend((directory/name).rglob('*'))
    sources={}
    for file in sorted(set(files)):
        relative=file.relative_to(directory)
        if any(p.startswith('.') for p in relative.parts):continue
        if file.is_symlink() or directory not in file.resolve().parents:raise ValueError('GPT 模型目录含外部链接')
        if file.is_file() and file.suffix.lower() in GPT_MODEL_SUFFIXES:sources[relative.as_posix()]=file
    return dict(sorted(sources.items()))

def model_ready(directory, engine='cosyvoice', variant=None):
    if engine=='gpt-sovits':
        gpt_sources(directory,variant);return
    directory = Path(directory)
    if not directory.is_dir() or not all((directory/name).is_file() for name in REQUIRED) or not (directory/'CosyVoice-BlankEN').is_dir():
        raise ValueError('请选择完整 CosyVoice 3 模型目录，包含权重、yaml、说话人／语音编码器和 CosyVoice-BlankEN 配套资源')
    # This validation is used before the engine environment exists, so it must
    # not depend on PyYAML already being installed. Only accept an exact,
    # top-level CosyVoice 3 LLM constructor tag; the engine performs the full
    # YAML parse later inside its isolated runtime.
    text=(directory/'cosyvoice3.yaml').read_text(encoding='utf-8-sig')
    tags=re.findall(r'(?m)^llm\s*:\s*(![^\s#]+)',text)
    if tags != ['!new:cosyvoice.llm.llm.CosyVoice3LM']:
        raise ValueError('模型配置与 CosyVoice 3 引擎不匹配，不能只改文件名或后缀')

def model_sources(directory, engine='cosyvoice', variant=None):
    if engine=='gpt-sovits':return gpt_sources(directory,variant)
    directory = Path(directory).resolve()
    model_ready(directory)
    sources = {}
    for file in sorted(directory.rglob('*')):
        relative = file.relative_to(directory)
        if any(part.startswith('.') or part.endswith('.chunks') for part in relative.parts):
            continue
        if file.is_symlink() or (file.is_file() and directory not in file.resolve().parents):
            raise ValueError('模型目录含外部链接，请使用完整本地文件')
        if file.is_file() and file.suffix.lower() in MODEL_SUFFIXES:
            sources[relative.as_posix()] = file
    return sources

def inventory_fingerprint(records):
    encoded = json.dumps(records, ensure_ascii=True, sort_keys=True, separators=(',',':')).encode('utf-8')
    return hashlib.sha256(encoded).hexdigest()
