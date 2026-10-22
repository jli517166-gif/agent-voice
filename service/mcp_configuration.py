"""Validated, recoverable updates of an existing DeepSeek MCP connection."""
import copy
import json
import os
from pathlib import Path
import shutil
import uuid
from contextlib import contextmanager
from state import atomic_json

ENTRY_ID = 'agent-voice-mcp'


@contextmanager
def profile_lock(profile):
    stream = (profile / '.agent-voice-mcp.lock').open('a+b')
    locked = False
    try:
        if stream.seek(0, 2) == 0:
            stream.write(b'0');stream.flush()
        stream.seek(0)
        try:
            if os.name == 'nt':
                import msvcrt
                msvcrt.locking(stream.fileno(), msvcrt.LK_NBLCK, 1)
            else:
                import fcntl
                fcntl.flock(stream.fileno(), fcntl.LOCK_EX | fcntl.LOCK_NB)
            locked = True
        except OSError as error:
            raise ValueError('另一项 MCP 安装或修复正在进行，请等待完成') from error
        yield
    finally:
        if locked:
            stream.seek(0)
            if os.name == 'nt':
                import msvcrt
                msvcrt.locking(stream.fileno(), msvcrt.LK_UNLCK, 1)
            else:
                import fcntl
                fcntl.flock(stream.fileno(), fcntl.LOCK_UN)
        stream.close()


def read_patch(path):
    import yaml
    try:
        entries = yaml.safe_load(path.read_text(encoding='utf-8-sig')) if path.exists() else []
    except yaml.YAMLError as error:
        mark = getattr(error, 'problem_mark', None)
        location = ('第 %d 行，第 %d 列' % (mark.line + 1, mark.column + 1)) if mark else '未知位置'
        raise ValueError('DeepSeek MCP 配置 YAML 语法错误（' + location + '），未自动覆盖：' + str(path)) from error
    if entries is None:
        entries = []
    if not isinstance(entries, list):
        raise ValueError('DeepSeek cordis.patch.yml 不是受支持的列表格式：' + str(path))
    return entries


def owned_entries(entries):
    found = []
    for entry in entries:
        if not isinstance(entry, dict):
            continue
        if entry.get('id') == ENTRY_ID:
            found.append(entry)
        if isinstance(entry.get('insert'), list):
            found.extend(p for p in entry['insert'] if isinstance(p, dict) and p.get('id') == ENTRY_ID)
    return found


def expected_entry(store, python, server):
    python, server = Path(python).resolve(), Path(server).resolve()
    for path, kind in ((python, '主服务 Python'), (server, 'MCP 服务文件')):
        if not path.is_file():
            raise ValueError(kind + '不存在：' + str(path))
    if server != store.root / 'integrations/agent_voice_mcp.py':
        raise ValueError('MCP 服务文件不属于当前程序目录')
    return {'id': ENTRY_ID, 'name': '@deepseek-ai/dsh-mcp-client', 'config': {
        'serverName': 'agent_voice', 'transport': 'stdio', 'command': str(python),
        'args': ['-I', '-X', 'utf8', str(server), '--root', str(store.root), '--host', 'deepseek'],
        'cwd': str(store.root), 'failOnStartupError': True}}


def replace_entry(entries, entry):
    result = []
    for item in copy.deepcopy(entries):
        if isinstance(item, dict):
            if item.get('id') == ENTRY_ID:
                continue
            if isinstance(item.get('insert'), list):
                item['insert'] = [p for p in item['insert']
                                  if not isinstance(p, dict) or p.get('id') != ENTRY_ID]
                if set(item) == {'insert'} and not item['insert']:
                    continue
        result.append(item)
    result.append({'insert': [entry]})
    return result


def write_patch(path, entries):
    import yaml
    temporary = path.with_name(path.name + '.' + uuid.uuid4().hex + '.tmp')
    try:
        temporary.write_text(yaml.safe_dump(entries, allow_unicode=True, sort_keys=False), encoding='utf-8')
        os.replace(temporary, path)
    finally:
        temporary.unlink(missing_ok=True)


def validate_patch(path, store, python, server):
    expected = expected_entry(store, python, server)
    entries = owned_entries(read_patch(path))
    if len(entries) != 1:
        raise ValueError('MCP 配置缺失或重复，需要恰好一个 agent-voice-mcp 条目')
    actual = entries[0]
    config, target = actual.get('config', {}), expected['config']
    if actual.get('disabled') or actual.get('enabled') is False or config.get('enabled') is False:
        raise ValueError('DeepSeek MCP 条目已禁用，请重新安装或在客户端启用插件')
    if actual.get('name') != expected['name']:
        raise ValueError('MCP 客户端插件名称错误')
    for key in ('serverName', 'transport', 'command', 'args', 'cwd', 'failOnStartupError'):
        if config.get(key) != target[key]:
            raise ValueError('MCP 配置 ' + key + ' 与当前程序目录不一致，请修复连接并重启 Harness')
    # These comparisons check ownership as well as existence: another working
    # installation is still the wrong target for this installation's controls.
    for value in (config['command'], config['args'][3]):
        if not Path(value).is_file():
            raise ValueError('MCP 启动文件不存在：' + value)
    for value in (config['args'][5], config['cwd']):
        if Path(value).resolve() != store.root or not Path(value).is_dir():
            raise ValueError('MCP 工作目录无效：' + value)
    return actual


class ConnectionTransaction:
    """Back up active configuration before any connector or plugin command.

    Package-manager download caches are deliberately retained on failure;
    restored configuration prevents an uncommitted plugin from activating.
    """
    def __init__(self, store, files):
        self.backup = store.root / 'data/connection-backups' / ('mcp-' + uuid.uuid4().hex)
        self.backup.mkdir(parents=True)
        self.files = {}
        for path in dict.fromkeys(Path(p) for p in files):
            existed = path.is_file()
            target = self.backup / str(len(self.files))
            if existed:
                shutil.copy2(path, target)
            self.files[path] = target if existed else None
        atomic_json(self.backup / 'journal.json', {'state': 'prepared', 'files': [
            {'path': str(p), 'backup': str(b) if b else None} for p, b in self.files.items()]})

    def rollback(self):
        failures = []
        for path, backup in reversed(list(self.files.items())):
            try:
                if backup is None:
                    path.unlink(missing_ok=True)
                else:
                    path.parent.mkdir(parents=True, exist_ok=True)
                    temporary = path.with_name(path.name + '.' + uuid.uuid4().hex + '.rollback')
                    try:
                        shutil.copy2(backup, temporary)
                        os.replace(temporary, path)
                    finally:
                        temporary.unlink(missing_ok=True)
            except OSError as error:
                failures.append(str(path) + ': ' + str(error))
        atomic_json(self.backup / 'result.json', {'state': 'rollback-failed' if failures else 'rolled-back', 'errors': failures})
        if failures:
            raise ValueError('回滚未完成，请使用备份 ' + str(self.backup) + ' 恢复：' + '; '.join(failures))

    def commit(self):
        atomic_json(self.backup / 'result.json', {'state': 'committed'})


def managed_files(store, profile):
    profiles = [profile]
    sibling = profile.parent / 'headless'
    if sibling.is_dir() and sibling != profile:
        profiles.append(sibling)
    paths = [store.root / 'data/harness-installations.json']
    for folder in profiles:
        paths += [folder / name for name in ('cordis.patch.yml', 'cordis.yml', 'package.json',
                                            'package-lock.json', 'pnpm-lock.yaml', 'yarn.lock')]
        paths += [folder / 'node_modules/dsh-agent-voice-public' / name
                  for name in ('index.mjs', 'client.js', 'package.json')]
        paths += [folder / 'node_modules/dsh-local-trusted-ca' / name for name in ('index.js', 'package.json')]
    return paths


def check_connection(store, profile, python, server, repair=False):
    if repair:
        with profile_lock(profile):
            return _check_connection(store, profile, python, server, repair)
    return _check_connection(store, profile, python, server, repair)


def _check_connection(store, profile, python, server, repair):
    from mcp_presence import connected_clients
    path = profile / 'cordis.patch.yml'
    entries = read_patch(path)
    owned = owned_entries(entries)
    if not owned:
        return {'ok': True, 'state': 'not-installed', 'message': '未安装 MCP 控制；自动朗读连接与 MCP 分别配置。'}
    changed = False
    try:
        validate_patch(path, store, python, server)
    except ValueError as error:
        if not repair:
            return {'ok': False, 'state': 'invalid', 'message': str(error)}
        # Never silently steal a live second installation. A missing old root
        # is a relocation; a live foreign root requires explicit reinstall.
        old = owned[0].get('config', {}).get('cwd')
        if old and Path(old).resolve() != store.root and Path(old).is_dir():
            return {'ok': False, 'state': 'other-installation', 'message': 'MCP 指向另一套仍存在的程序。请点击“安装到 DeepSeek”明确切换目标。'}
        replacement = expected_entry(store, python, server)
        transaction = ConnectionTransaction(store, managed_files(store, profile))
        try:
            write_patch(path, replace_entry(entries, replacement))
            validate_patch(path, store, python, server)
            wrapper = profile / 'node_modules/dsh-agent-voice-public/index.mjs'
            if wrapper.is_file() and 'integrations/' in wrapper.read_text(encoding='utf-8-sig'):
                wrapper.write_text('export { default, apply, inject, name } from ' +
                                   json.dumps((store.root / 'integrations/deepseek-plugin.mjs').as_uri()) + ';\n', encoding='utf-8')
            transaction.commit()
            changed = True
        except Exception:
            transaction.rollback()
            raise
    clients = connected_clients(store.root, 'deepseek', 'dsh-mcp-client')
    listed = [c for c in clients if c.get('toolCount') == 8]
    # A tools/list receipt proves discovery, not registration in the model or
    # audible playback. The interface must retain that distinction.
    return {'ok': True, 'state': 'tools-read' if listed else 'awaiting-client', 'repaired': changed,
            'toolCount': 8 if listed else 0, 'harnessRegistrationVerified': False,
            'message': ('已修复 MCP 启动配置；请重启 Harness。' if changed else '') +
                       ('客户端已读取 8 个工具；连接自检不测试语音生成或播放。' if listed else
                        '配置正确，尚未确认客户端读取工具；请重启 Harness 后检查。')}
