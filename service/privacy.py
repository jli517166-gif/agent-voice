"""Local privacy boundaries: scoped automatic reading and narrow log cleanup."""
import os
from pathlib import Path
import time
import stat


def linked(path):
    return path.is_symlink() or bool(getattr(path.lstat(),'st_file_attributes',0)&getattr(stat,'FILE_ATTRIBUTE_REPARSE_POINT',0x400))


def reading_allowed(config, message):
    privacy = config['privacy']
    if privacy['harnessScope'] == 'all':
        return True
    if privacy['harnessScope'] == 'session':
        return bool(message.get('sessionId')) and message['sessionId'] == privacy['sessionId'].strip()
    actual = message.get('cwd')
    expected = privacy['projectRoot'].strip()
    if not actual or not expected:
        return False  # A host without scope evidence is not silently trusted.
    try:
        actual = os.path.normcase(os.path.realpath(actual))
        expected = os.path.normcase(os.path.realpath(expected))
        return os.path.commonpath([actual, expected]) == expected
    except (ValueError, OSError, TypeError):
        return False


def clean_logs(root, days, now=None):
    """Only old regular files immediately inside data/logs; never models/backups."""
    if not days:
        return {'removed': 0, 'skipped': 0}
    directory = Path(root).resolve() / 'data/logs'
    if not directory.is_dir() or linked(directory) or linked(directory.parent):
        return {'removed': 0, 'skipped': 0}
    cutoff = (time.time() if now is None else now) - days * 86400
    removed = skipped = 0
    import re
    for path in directory.iterdir():
        if linked(path) or not path.is_file() or not (re.fullmatch(r'.+\.log(?:\.\d+)?',path.name) or path.suffix == '.txt'):
            continue
        try:
            if path.stat().st_mtime < cutoff:
                path.unlink(); removed += 1
        except (PermissionError, OSError):
            skipped += 1  # A live Windows log must not abort synthesis.
    return {'removed': removed, 'skipped': skipped}


def clear_output_records(store):
    """Only persisted generation metadata, not user references, profiles or backups."""
    import json,re
    from state import atomic_json
    directory=store.root/'data/output';removed=0
    if not directory.is_dir() or linked(directory) or linked(directory.parent):return {'removed':0}
    for path in directory.glob('*.json'):
        if not re.fullmatch('[a-f0-9]{32}\\.json',path.name) or linked(path):continue
        value=json.loads(path.read_text(encoding='utf-8-sig'))
        if not isinstance(value,dict):continue
        changed=False
        for key in ('text','recoveredErrors'):
            if key in value:value.pop(key);changed=True
        if changed:atomic_json(path,value);removed+=1
    return {'removed':removed}


def rotate_token(store):
    """Call only while holding the exclusive offline operation lock."""
    import secrets
    from state import atomic_json,validate
    config=store.get();config['token']=secrets.token_hex(32)
    config['harness']={key:False for key in config['harness']}
    config['airi']['enabled']=False;config['airi']['output']='local'
    validate(config);atomic_json(store.path,config)
    receipts=store.root/'data/mcp-clients'
    skipped=0
    if receipts.is_dir() and not linked(receipts) and not linked(receipts.parent):
        for path in receipts.glob('*.json'):
            if path.is_file() and not linked(path):
                try:path.unlink()
                except OSError:skipped+=1
    return {'ok':True,'receiptsSkipped':skipped,'message':'连接密钥已更换，自动朗读与 AIRI 已关闭。重新启动服务后重载 MCP；AIRI 请重新选择原目录接入并验证。账号登录与声音设置保留。'+(' 部分旧回执正被占用，请重新验证。' if skipped else '')}


def diagnostic_summary(store):
    """Allow-list a shareable preview; no raw errors, hardware IDs, text or paths."""
    from diagnostics import inspect_installation
    result = inspect_installation(store, deep=False)
    return {'format': 'agent-voice-redacted-diagnostic', 'version': 1,
        'activeEngine': result.get('activeEngine'), 'ok': result.get('ok'),
        'checks': [{key: item[key] for key in ('name', 'ok', 'section', 'warning') if key in item}
                   for item in result.get('checks', [])],
        'notice': '仅含检查名称与布尔结果；不含路径、连接密钥、台词、录音和模型。'}
