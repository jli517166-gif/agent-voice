"""Local MCP discovery receipts, scoped to a live process and installation."""
import json
import os
from pathlib import Path
import time
import uuid
from state import atomic_json


def process_stamp(pid):
    if os.name == 'nt':
        import ctypes
        from ctypes import wintypes
        kernel = ctypes.WinDLL('kernel32', use_last_error=True)
        kernel.OpenProcess.argtypes = [wintypes.DWORD, wintypes.BOOL, wintypes.DWORD]
        kernel.OpenProcess.restype = wintypes.HANDLE
        kernel.GetExitCodeProcess.argtypes = [wintypes.HANDLE, ctypes.POINTER(wintypes.DWORD)]
        kernel.GetExitCodeProcess.restype = wintypes.BOOL
        kernel.GetProcessTimes.argtypes = [wintypes.HANDLE] + [ctypes.POINTER(ctypes.c_ulonglong)] * 4
        kernel.CloseHandle.argtypes = [wintypes.HANDLE]
        handle = kernel.OpenProcess(0x1000, False, pid)
        if not handle:
            return None
        try:
            exit_code = wintypes.DWORD()
            if not kernel.GetExitCodeProcess(handle, ctypes.byref(exit_code)):
                return None
            if exit_code.value != 259:  # STILL_ACTIVE
                return None
            values = [ctypes.c_ulonglong() for _ in range(4)]
            if not kernel.GetProcessTimes(handle, *(ctypes.byref(v) for v in values)):
                return None
            return str(values[0].value)
        finally:
            kernel.CloseHandle(handle)
    try:
        return Path('/proc/' + str(pid) + '/stat').read_text().rsplit(')', 1)[1].split()[19]
    except OSError:
        return None


class McpPresence:
    def __init__(self, root, host):
        self.root = Path(root).resolve()
        self.file = self.root / 'data/mcp-clients' / (host + '-' + uuid.uuid4().hex + '.json')
        self.record = {'root': str(self.root), 'host': host, 'pid': os.getpid(),
                       'processStamp': process_stamp(os.getpid()), 'toolCount': 0, 'clientName': ''}

    def note(self, method, params=None, tool_count=0):
        if method == 'initialize':
            self.record['clientName'] = str((params or {}).get('clientInfo', {}).get('name', ''))[:128]
        if method == 'tools/list':
            self.record['toolCount'] = tool_count
        self.record.update(lastMethod=method, updatedAt=time.time())
        atomic_json(self.file, self.record)

    def close(self):
        self.file.unlink(missing_ok=True)


def connected_clients(root, host, client_name):
    root = Path(root).resolve()
    result = []
    for path in (root / 'data/mcp-clients').glob(host + '-*.json'):
        try:
            if path.stat().st_size > 16384:
                continue
            value = json.loads(path.read_text(encoding='utf-8'))
            if (Path(value['root']).resolve() == root and value['host'] == host
                    and (value['clientName'] == client_name if client_name else value['clientName'] != 'agent-voice-selfcheck') and value['processStamp']
                    and process_stamp(int(value['pid'])) == value['processStamp']):
                result.append(value)
        except (OSError, ValueError, KeyError, TypeError):
            continue
    return result
