"""Install the optional provider into a *source checkout* of dsh-pet-indesktop.

Never touches a running pet executable, user config, or the upstream assets.
Exact markers make upstream changes fail closed instead of corrupting code.
"""
import argparse
import json
import shutil
from pathlib import Path

SOURCE = Path(__file__).resolve().parents[1] / 'integrations/dsh-pet-indesktop/agent_voice.py'


def replace_once(text, old, new):
    if text.count(old) != 1:
        raise ValueError('桌宠源码结构已变化，补丁未安装；请更新适配器：' + old[:65])
    return text.replace(old, new, 1)


def prepare(root):
    root = Path(root).resolve()
    if not (root / 'pet/tts/base.py').is_file() or not (root / 'pet/voice/chime_service.py').is_file():
        raise ValueError('请选择 dsh-pet-indesktop 的源码目录，不能选择已打包的桌宠目录')
    tts = root / 'pet/tts/__init__.py'
    base = root / 'pet/tts/base.py'
    service = root / 'pet/voice/chime_service.py'
    config = root / 'pet/config.py'
    schema_test = root / 'tests/test_config_schema.py'
    target = root / 'pet/tts/agent_voice.py'
    if target.exists():
        raise FileExistsError('检测到已安装的 Agent Voice provider；请先检查现有源码，避免覆盖')
    if not schema_test.is_file():
        raise ValueError('桌宠源码缺少配置 schema 测试；补丁未安装')
    original = {path: path.read_text(encoding='utf-8') for path in (tts, base, service, config, schema_test)}
    updates = {}
    updates[tts] = replace_once(original[tts],
                                'from . import doubao, edge, mimo, sapi',
                                'from . import agent_voice, doubao, edge, mimo, sapi')
    updates[base] = replace_once(original[base],
                                 '    is_fallback: bool = False\n',
                                 '    is_fallback: bool = False\n    cacheable: bool = True\n')
    modified = original[service]
    modified = replace_once(modified,
                            'def _cache_hit(path: Path) -> bool:',
                            'def _cache_hit(path: Path, attempt=None) -> bool:')
    modified = replace_once(modified,
                            '    return _nonempty_file(path)\n\n\ndef _temp_cache_path',
                            '    provider = tts.get(attempt.provider) if attempt is not None else None\n'
                            '    return (provider is None or provider.cacheable) and _nonempty_file(path)\n\n\ndef _temp_cache_path')
    modified = replace_once(modified,
                            'if any(_cache_hit(path) for path in paths):',
                            'if any(_cache_hit(path, attempt) for path, attempt in zip(paths, attempts)):')
    modified = replace_once(modified,
                            'next((str(p) for p in paths if _cache_hit(p)), None)',
                            'next((str(p) for p, attempt in zip(paths, attempts) if _cache_hit(p, attempt)), None)')
    modified = replace_once(modified,
                            '        for out_path in paths:\n            if not _cache_hit(out_path):',
                            '        for out_path, attempt in zip(paths, attempts):\n            if not _cache_hit(out_path, attempt):')
    modified = replace_once(modified,
                            '        sentence = build_chime_sentence(next_at, self._cfg)\n        bubble = build_bubble_sentence',
                            '        if attempts[0].provider == "agentvoice":\n            return  # Live voice: no cache to precompute.\n'
                            '        sentence = build_chime_sentence(next_at, self._cfg)\n        bubble = build_bubble_sentence')
    modified = replace_once(modified,
                            '        paths = [self._cache_path(stripped, attempt) for attempt in attempts]\n',
                            '        if attempts[0].provider == "agentvoice":\n            return False  # Live voice is synthesized when actually requested.\n'
                            '        paths = [self._cache_path(stripped, attempt) for attempt in attempts]\n')
    updates[service] = modified
    modified_config = replace_once(original[config],
                                   '            "voice_chime_tts_fallback": True,  # 主后端失败自动回退 edge\n',
                                   '            "voice_chime_tts_fallback": True,  # 主后端失败自动回退 edge\n'
                                   '            "voice_chime_agent_voice_root": "",\n'
                                   '            "voice_chime_agent_voice_profile": "current",\n'
                                   '            "voice_chime_agent_voice_speed": 100,\n')
    modified_config = replace_once(modified_config,
                                   '            "voice_chime_tts_fallback",\n            "voice_chime_mimo_model",',
                                   '            "voice_chime_tts_fallback",\n'
                                   '            "voice_chime_agent_voice_root",\n'
                                   '            "voice_chime_agent_voice_profile",\n'
                                   '            "voice_chime_agent_voice_speed",\n'
                                   '            "voice_chime_mimo_model",')
    updates[config] = modified_config
    updates[schema_test] = replace_once(original[schema_test],
                                        '        "voice_chime_custom_times",\n        "voice_chime_doubao_pitch",',
                                        '        "voice_chime_custom_times",\n'
                                        '        "voice_chime_agent_voice_profile",\n'
                                        '        "voice_chime_agent_voice_root",\n'
                                        '        "voice_chime_agent_voice_speed",\n'
                                        '        "voice_chime_doubao_pitch",')
    # Validate every marker before writing anything. Backups remain beside source.
    for path in updates:
        backup = path.with_suffix(path.suffix + '.agent-voice.bak')
        if backup.exists():
            raise FileExistsError('已有补丁备份，未修改源码：' + str(backup))
    for path, content in original.items():
        path.with_suffix(path.suffix + '.agent-voice.bak').write_text(content, encoding='utf-8')
    try:
        for path, content in updates.items():
            path.write_text(content, encoding='utf-8')
        shutil.copy2(SOURCE, target)
    except Exception:
        for path, content in original.items():
            path.write_text(content, encoding='utf-8')
        target.unlink(missing_ok=True)
        raise
    return {'ok': True, 'source': str(root), 'provider': str(target),
            'note': '已安装到桌宠源码；需按桌宠上游说明重新构建，现有 exe 未被修改'}


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--pet-source', required=True)
    args = parser.parse_args()
    print(json.dumps(prepare(args.pet_source), ensure_ascii=False))
