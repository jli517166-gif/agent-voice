"""Read-only MCP protocol checks. Never synthesize, play or prompt an LLM."""
import json
import subprocess
from pathlib import Path

TOOLS = {'voice_service_status', 'start_voice_service', 'stop_voice_service',
         'read_text', 'stop_speech', 'list_voice_profiles', 'apply_voice_profile', 'set_auto_reading',
         'list_singing_engines', 'open_singing_app', 'select_singing_engine', 'open_singing_engine', 'generate_song',
         'singing_job_status', 'set_singing_output_directory'}


def probe(store, source, python, server):
    messages = [
        {'jsonrpc': '2.0', 'id': 1, 'method': 'initialize', 'params': {
            'protocolVersion': '2025-06-18', 'capabilities': {},
            'clientInfo': {'name': 'agent-voice-selfcheck', 'version': '1'}}},
        {'jsonrpc': '2.0', 'method': 'notifications/initialized'},
        {'jsonrpc': '2.0', 'id': 2, 'method': 'tools/list', 'params': {}},
        {'jsonrpc': '2.0', 'id': 3, 'method': 'tools/call', 'params': {
            'name': 'voice_service_status', 'arguments': {}}}]
    result = subprocess.run([str(python), '-I', '-X', 'utf8', str(server),
                             '--root', str(store.root), '--host', source],
                            input=''.join(json.dumps(m) + '\n' for m in messages),
                            stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True,
                            encoding='utf-8', errors='strict', timeout=20, cwd=store.root,
                            creationflags=getattr(subprocess, 'CREATE_NO_WINDOW', 0))
    if result.returncode:
        raise ValueError('MCP 启动失败：' + result.stderr[-1800:])
    replies = {r.get('id'): r for r in map(json.loads, result.stdout.splitlines())}
    for ident in (1, 2, 3):
        if ident not in replies or 'error' in replies[ident]:
            raise ValueError('MCP 协议检查失败：' + str(replies.get(ident, '缺少回执')))
    init = replies[1]['result']
    if init.get('serverInfo', {}).get('name') != 'agent-voice-control' or not init.get('capabilities', {}).get('tools'):
        raise ValueError('MCP initialize 返回了不匹配的服务器或能力')
    names = {t['name'] for t in replies[2]['result'].get('tools', [])}
    if names != TOOLS:
        raise ValueError('MCP 工具列表不完整：' + ', '.join(sorted(names)))
    status = replies[3]['result']
    if status.get('isError') or status.get('structuredContent', {}).get('host') != source:
        raise ValueError('MCP 状态工具调用失败或宿主不匹配')
    return {'protocolPassed': True, 'toolCount': len(names), 'readOnlyStatusPassed': True,
            'audioTested': False, 'harnessSessionTested': False}


def codex_configuration(store, python, server, codex, run):
    item = json.loads(run([codex, 'mcp', 'get', 'agent_voice', '--json']))
    transport = item.get('transport', {})
    if item.get('enabled') is not True or item.get('disabled_reason'):
        raise ValueError('Codex MCP 未启用，请在客户端启用 agent_voice 后重试')
    expected = ['-I', '-X', 'utf8', str(server), '--root', str(store.root), '--host', 'codex']
    if (transport.get('type') != 'stdio' or transport.get('command') != str(python)
            or transport.get('args') != expected or transport.get('cwd') not in (None, str(store.root))):
        raise ValueError('Codex MCP 路径与当前软件不一致，请重新安装 MCP 后重载客户端')
    enabled, disabled = item.get('enabled_tools'), item.get('disabled_tools')
    if (enabled is not None and not TOOLS.issubset(set(enabled))) or TOOLS.intersection(disabled or []):
        raise ValueError('Codex 禁用了部分语音或唱歌工具，请在 MCP 设置恢复全部工具')
    return item
