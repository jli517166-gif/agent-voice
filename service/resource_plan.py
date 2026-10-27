"""Offline file-level acquisition plan; never imports a TTS runtime."""
import json
from pathlib import Path
from engine_registry import spec, gpt_required
from model_integrity import REQUIRED

CATALOG = json.loads(Path(__file__).with_name('environment-catalog.json').read_text(encoding='utf-8'))


def present(path):
    path = Path(path)
    return path.is_file() and path.stat().st_size > 0


def local_target(store, configured, fallback):
    path = store.resolve(configured)
    return path if store.root in path.resolve().parents else store.root / fallback


def resource_plan(store, engine):
    cfg = store.get()
    selected = ['cosyvoice', 'gpt-sovits'] if engine == 'all' else [] if engine == 'core' else [cfg['voice']['engine'] if engine == 'current' else engine]
    if any(name not in ('cosyvoice', 'gpt-sovits') for name in selected):
        raise ValueError('未知引擎')
    rows = []
    for name in selected:
        data, catalog = spec(name), CATALOG[name]
        source = store.resolve(cfg['engines'][data['sourceKey']])
        model = store.resolve(cfg['engines'][data['modelKey']])
        source_file = source / ('cosyvoice/cli/cosyvoice.py' if name == 'cosyvoice' else 'GPT_SoVITS/TTS_infer_pack/TTS.py')
        required = list(REQUIRED) + ['CosyVoice-BlankEN/' + f for f in ('config.json', 'tokenizer_config.json', 'merges.txt', 'vocab.json', 'generation_config.json')] if name == 'cosyvoice' else sorted(set(
            p.relative_to(model).as_posix() for variant in ('proplus', 'v5turbo')
            for p in gpt_required({'source': str(source), 'model': str(model), 'variant': variant})[1:]))
        missing = [path for path in required if not present(model / path)]
        if name == 'cosyvoice' and not any(present(p) for p in (model / 'CosyVoice-BlankEN').glob('*.safetensors')):
            missing.append('CosyVoice-BlankEN/*.safetensors')
        matcha_missing = name == 'cosyvoice' and not present(source / 'third_party/Matcha-TTS/matcha/utils/audio.py')
        source_target = local_target(store, cfg['engines'][data['sourceKey']], 'tts-engines/' + name + '/source')
        model_target = local_target(store, cfg['engines'][data['modelKey']], 'tts-engines/' + name + '/models/' + ('Fun-CosyVoice3-0.5B' if name == 'cosyvoice' else 'GPT-SoVITS'))
        active = store.resolve(cfg['voice'].get(data['modelField']) or cfg['engines'][data['modelKey']]) if cfg['voice']['engine'] == name else model
        rows.append({'id': name, 'source': str(source_target), 'model': str(model_target),
                     'configuredSource': str(source), 'configuredModel': str(model),
                     'activeModel': str(active), 'customModelSelected': active != model,
                     'sourceMissing': not present(source_file), 'matchaMissing': matcha_missing,
                     'missingModelFiles': sorted(set(missing)),
                     'sourceUrl': 'https://codeload.github.com/' + catalog['repo'] + '/zip/' + catalog['commit'],
                     'sourceRevision': catalog['commit'], 'modelRepository': catalog['modelRepo'],
                     'modelRevision': catalog['modelRevision'],
                     'modelUrl': 'https://huggingface.co/' + catalog['modelRepo'] + '/resolve/' + catalog['modelRevision'] + '/',
                     'note': 'GPT 两个版本共用一次基础文件。' if name == 'gpt-sovits' else '仅匹配 CosyVoice 3；不会覆盖个人训练权重。'})
    return {'ok': True, 'resourcesReady': all(not (r['sourceMissing'] or r['matchaMissing'] or r['missingModelFiles']) for r in rows),
            'items': rows, 'networkUsed': False, 'runtimeTested': False,
            'cache': str(store.root / 'data/environment-repair'),
            'notice': '按当前安装清单匹配固定版本；下载到上述位置。文件就绪不等于环境或语音验证通过。外部资源缺失时只在软件目录内补齐。'}
