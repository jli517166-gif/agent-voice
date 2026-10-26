"""Edition-scoped application updates. User data, engines and modules are excluded."""
import argparse
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import re
import shutil
import time
import urllib.error
import urllib.request
import uuid
import zipfile
from state import atomic_json
from operation_lock import OperationLock

REPOSITORY = 'jli517166-gif/agent-voice'
MAX_BYTES = 256 * 1024 * 1024


def digest(path):
    result = hashlib.sha256()
    with Path(path).open('rb') as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b''):
            result.update(block)
    return result.hexdigest()


def allowed(name):
    parts = PurePosixPath(name).parts
    if (not parts or '\\' in name or ':' in name or name.startswith('/')
            or any(p in ('.', '..') or p.endswith(('.', ' ')) for p in name.split('/'))):
        return False
    if parts[0] in ('service', 'integrations', 'tools', 'docs', 'assets', '.agents'):
        return Path(name).suffix.lower() in {'.py', '.mjs', '.js', '.ts', '.html', '.css', '.json', '.yaml', '.yml', '.md', '.txt', '.ps1', '.toml', '.dll', '.png', '.webp', '.gif', '.svg', '.ico', '.wav'}
    return len(parts) == 1 and (name in ('agent语音.exe', 'LICENSE', 'THIRD_PARTY_NOTICES.md') or name.endswith('.md') or name in ('Microsoft.Web.WebView2.Core.dll', 'Microsoft.Web.WebView2.WinForms.dll', 'WebView2Loader.dll'))


def inside(root, name):
    root = Path(root).resolve()
    path = root / name
    for candidate in (path, *path.parents):
        if candidate == root:
            break
        if candidate.is_symlink() or (candidate.exists() and getattr(candidate.stat(), 'st_file_attributes', 0) & 0x400):
            raise ValueError('更新路径包含链接：' + name)
    resolved = path.resolve()
    if root not in resolved.parents:
        raise ValueError('更新路径超出程序目录')
    return path


def version_info(root):
    value = json.loads((Path(root) / 'app-version.json').read_text(encoding='utf-8-sig'))
    if value.get('format') != 'agent-voice-app' or value.get('edition') not in ('public', 'private') or value.get('platform') != 'windows-x64':
        raise ValueError('更新清单的版本类型或平台不匹配')
    if not re.fullmatch(r'\d+\.\d+\.\d+(?:-[A-Za-z0-9.-]+)?', value.get('version', '')):
        raise ValueError('软件版本无效')
    return value


def write_manifest(root, edition, version):
    root = Path(root).resolve()
    files = []
    for path in sorted(root.rglob('*')):
        if path.is_file() and allowed(path.relative_to(root).as_posix()):
            relative = path.relative_to(root).as_posix()
            if edition == 'public' and relative.casefold() == 'service/edition.py':
                raise ValueError('公开版不能包含私用策略组件')
            inside(root, relative)
            files.append({'path': relative, 'size': path.stat().st_size, 'sha256': digest(path)})
    if not any(f['path'] == 'agent语音.exe' for f in files):
        raise ValueError('更新包缺少主程序')
    atomic_json(root / 'app-version.json', {'format': 'agent-voice-app', 'version': version, 'edition': edition,
                'platform': 'windows-x64', 'repository': REPOSITORY if edition == 'public' else '', 'files': files})
    return version_info(root)


def fetch_json(url):
    request = urllib.request.Request(url, headers={'User-Agent': 'AgentVoice-Updater/1', 'Accept': 'application/vnd.github+json'})
    with urllib.request.urlopen(request, timeout=20) as response:
        return json.loads(response.read(2 * 1024 * 1024))


def version_key(text):
    match = re.fullmatch(r'v?(\d+)\.(\d+)\.(\d+)(?:-([A-Za-z0-9.-]+))?', text)
    if not match:
        raise ValueError('无效发布版本')
    return tuple(map(int, match.group(1, 2, 3))) + (match.group(4) is None, tuple((0, int(x)) if x.isdigit() else (1, x) for x in (match.group(4) or '').split('.')))


def check(root):
    current = version_info(root)
    if current['edition'] == 'private':
        return {'ok': True, 'state': 'local-only', 'current': current['version'], 'message': '私用版仅从本地更新包更新，不连接 GitHub，不接收公开版覆盖。'}
    try:
        releases = fetch_json('https://api.github.com/repos/' + REPOSITORY + '/releases?per_page=30')
        candidates = [r for r in releases if not r.get('draft') and re.fullmatch(r'v?\d+\.\d+\.\d+(?:-[A-Za-z0-9.-]+)?', r.get('tag_name', ''))]
        if not candidates:
            return {'ok': False, 'state': 'unavailable', 'message': '没有可用的软件发行版；不能据此判断为最新版本。'}
        release = max(candidates, key=lambda r: version_key(r['tag_name']))
        newer = version_key(release['tag_name']) > version_key(current['version'])
        assets = [a for a in release.get('assets', []) if re.fullmatch(r'AgentVoice-Windows-x64-v[^/]+\.zip', a.get('name', ''))]
        asset = assets[0] if len(assets) == 1 else None
        valid = asset and re.fullmatch('sha256:[a-f0-9]{64}', asset.get('digest') or '')
        return {'ok': True, 'state': 'available' if newer and valid else 'manual' if newer else 'current',
                'current': current['version'], 'latest': release['tag_name'],
                'downloadUrl': asset['browser_download_url'] if valid else '', 'sha256': asset['digest'][7:] if valid else '',
                'message': ('发现新版，可下载并校验。' if valid else '发现新版，但没有可校验的安装附件，请手动查看发布页。') if newer else '当前已是可用的最新版本。',
                'releaseUrl': 'https://github.com/' + REPOSITORY + '/releases'}
    except (OSError, ValueError, TypeError, KeyError) as error:
        return {'ok': False, 'state': 'unavailable', 'message': '无法检查更新：仓库可能仍为私有、网络不可达或访问受限。可从有权限的账户下载更新包后本地安装。详情：' + str(error)}


def download(root):
    report = check(root)
    if report.get('state') != 'available':
        raise ValueError(report['message'])
    url = report['downloadUrl']
    if not url.startswith('https://github.com/' + REPOSITORY + '/releases/download/'):
        raise ValueError('更新来源不匹配')
    target = inside(root, 'data/updates/download.zip')
    target.parent.mkdir(parents=True, exist_ok=True)
    partial = target.with_suffix('.part')
    try:
        with urllib.request.urlopen(urllib.request.Request(url, headers={'User-Agent': 'AgentVoice-Updater/1'}), timeout=60) as source, partial.open('wb') as output:
            count = 0
            while block := source.read(1024 * 1024):
                count += len(block)
                if count > MAX_BYTES:
                    raise ValueError('更新包超过大小限制')
                output.write(block)
        if digest(partial) != report['sha256']:
            raise ValueError('更新附件 SHA256 校验失败')
        os.replace(partial, target)
        return stage(root, target)
    finally:
        partial.unlink(missing_ok=True)


def stage(root, package):
    root = Path(root).resolve()
    current = version_info(root)
    work = inside(root, 'data/updates')
    work.mkdir(parents=True, exist_ok=True)
    staged = work / ('stage-' + uuid.uuid4().hex)
    try:
        with zipfile.ZipFile(package) as archive:
            infos = [i for i in archive.infolist() if not i.is_dir()]
            if len(infos) > 5000 or sum(i.file_size for i in infos) > MAX_BYTES:
                raise ValueError('更新包超过大小限制')
            names = [i.filename for i in infos]
            manifests = [n for n in names if n.endswith('/app-version.json') and n.count('/') == 1]
            if len(manifests) != 1:
                raise ValueError('更新包缺少独立版本清单；旧版压缩包不能作为增量更新包')
            prefix = manifests[0].split('/')[0] + '/'
            if archive.getinfo(manifests[0]).file_size > 2 * 1024 * 1024:
                raise ValueError('更新清单过大')
            manifest = json.loads(archive.read(manifests[0]).decode('utf-8-sig'))
            if any(manifest.get(k) != current[k] for k in ('format', 'edition', 'platform')):
                raise ValueError('更新包的版本类型不匹配；公开版与私用版禁止互相覆盖')
            version_key(manifest.get('version', ''))
            records = manifest.get('files', [])
            if not records or len(records) > 5000:
                raise ValueError('更新文件清单为空或过大')
            expected = {}
            for row in records:
                name = row['path']
                if current['edition'] == 'public' and str(name).casefold() == 'service/edition.py':
                    raise ValueError('公开版不能包含私用策略组件')
                if not allowed(name) or name.casefold() in expected or not isinstance(row.get('size'), int) or row['size'] < 0 or not re.fullmatch('[a-f0-9]{64}', row.get('sha256', '')):
                    raise ValueError('更新文件范围或校验清单不允许：' + str(name))
                inside(root, name)
                expected[name.casefold()] = row
            if 'agent语音.exe' not in expected:
                raise ValueError('更新包缺少主程序')
            if len({n.casefold() for n in names}) != len(names) or set(names) != {prefix + r['path'] for r in records} | {manifests[0]}:
                raise ValueError('更新归档含未登记、重复或越界文件')
            staged.mkdir()
            for name in names:
                info = archive.getinfo(name)
                if info.external_attr >> 16 & 0o170000 == 0o120000:
                    raise ValueError('更新包包含链接')
                relative = name[len(prefix):]
                if relative != 'app-version.json':
                    row = expected[relative.casefold()]
                    if info.file_size != row['size']:
                        raise ValueError('更新文件大小校验失败：' + relative)
                target = inside(staged, relative)
                target.parent.mkdir(parents=True, exist_ok=True)
                with archive.open(name) as source, target.open('wb') as output:
                    shutil.copyfileobj(source, output)
                if relative != 'app-version.json' and digest(target) != row['sha256']:
                    raise ValueError('更新文件校验失败：' + relative)
        atomic_json(work / 'pending.json', {'stage': staged.name, 'version': manifest['version'], 'manifestSha256': digest(staged / 'app-version.json')})
        return {'ok': True, 'state': 'staged', 'version': manifest['version'], 'message': '更新包校验通过。停止语音服务后点击“退出并安装更新”；现有设置、引擎、功能模块与个人文件保留。'}
    except Exception:
        if staged.exists():
            shutil.rmtree(staged)
        raise


def replace_file(source, destination):
    destination.parent.mkdir(parents=True, exist_ok=True)
    temporary = destination.with_name(destination.name + '.update-' + uuid.uuid4().hex)
    try:
        shutil.copy2(source, temporary)
        os.replace(temporary, destination)
    finally:
        temporary.unlink(missing_ok=True)


def rollback(root, backup):
    journal = json.loads((backup / 'journal.json').read_text(encoding='utf-8-sig'))
    for row in reversed(journal['files']):
        name = row['path']
        if name != 'app-version.json' and not allowed(name):
            raise ValueError('回退清单越界')
        target = inside(root, name)
        if row['existed']:
            saved = inside(backup, name)
            if digest(saved) != row['sha256']:
                raise ValueError('回退备份校验失败：' + name)
            if not target.is_file() or digest(target) != row['sha256']:
                replace_file(saved, target)
        else:
            target.unlink(missing_ok=True)
    atomic_json(backup / 'result.json', {'state': 'rolled-back'})
    (root / 'data/updates/installing.json').unlink(missing_ok=True)


def recover(root):
    root = Path(root).resolve()
    marker = root / 'data/updates/installing.json'
    with OperationLock(root):
        if not marker.is_file():
            return {'ok': True, 'state': 'no-pending-recovery'}
        record = json.loads(marker.read_text(encoding='utf-8-sig'))
        if not re.fullmatch('[a-f0-9]{32}', record['backup']):
            raise ValueError('回退备份路径无效')
        backup = inside(root, 'data/updates/backups/' + record['backup'])
        rollback(root, backup)
        report = {'ok': True, 'state': 'rolled-back', 'message': '上次更新被中断，已恢复原程序。可重新选择更新包。', 'backup': str(backup)}
        atomic_json(root / 'data/updates/latest.json', report)
        return report


def apply(root):
    root = Path(root).resolve()
    work = inside(root, 'data/updates')
    with OperationLock(root):
        if (work / 'installing.json').exists():
            raise ValueError('上次更新尚未恢复，请先运行更新恢复')
        pending = json.loads((work / 'pending.json').read_text(encoding='utf-8-sig'))
        if not re.fullmatch('stage-[a-f0-9]{32}', pending['stage']):
            raise ValueError('待更新路径无效')
        staged = inside(root, 'data/updates/' + pending['stage'])
        if digest(staged / 'app-version.json') != pending['manifestSha256']:
            raise ValueError('待更新清单校验失败')
        manifest = version_info(staged)
        current = version_info(root)
        if manifest['edition'] != current['edition']:
            raise ValueError('版本类型不匹配')
        for row in manifest['files']:
            if not allowed(row['path']) or digest(inside(staged, row['path'])) != row['sha256']:
                raise ValueError('待更新文件校验失败')
        paths = [r['path'] for r in manifest['files']] + ['app-version.json']
        backup = inside(root, 'data/updates/backups/' + uuid.uuid4().hex)
        backup.mkdir(parents=True)
        journal = []
        # Backup all targets before changing any installed program file.
        for name in paths:
            target = inside(root, name)
            existed = target.is_file()
            if existed:
                saved = inside(backup, name)
                saved.parent.mkdir(parents=True, exist_ok=True)
                shutil.copy2(target, saved)
            journal.append({'path': name, 'existed': existed, 'sha256': digest(target) if existed else None})
        atomic_json(backup / 'journal.json', {'state': 'prepared', 'files': journal})
        atomic_json(work / 'installing.json', {'backup': backup.name})
        try:
            for row in journal:
                replace_file(staged / row['path'], inside(root, row['path']))
        except Exception:
            rollback(root, backup)
            raise
        (work / 'pending.json').unlink()
        (work / 'installing.json').unlink()
        report = {'ok': True, 'state': 'installed', 'version': manifest['version'], 'backup': str(backup), 'message': '更新完成；设置、引擎和个人数据保留。'}
        atomic_json(work / 'latest.json', report)
        return report


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('action', choices=['check', 'download', 'stage', 'apply', 'manifest', 'recover'])
    parser.add_argument('--root', required=True)
    parser.add_argument('--package')
    parser.add_argument('--edition', choices=['public', 'private'])
    parser.add_argument('--version')
    parser.add_argument('--wait-pid', type=int)
    parser.add_argument('--restart', action='store_true')
    args = parser.parse_args()
    try:
        if args.wait_pid:
            from mcp_presence import process_stamp
            stamp = process_stamp(args.wait_pid)
            deadline = time.monotonic() + 60
            while stamp and process_stamp(args.wait_pid) == stamp:
                if time.monotonic() > deadline:
                    raise ValueError('主程序尚未退出，未替换文件')
                time.sleep(.2)
        if args.action == 'manifest':
            result = write_manifest(args.root, args.edition, args.version)
        elif args.action == 'stage':
            result = stage(args.root, args.package)
        else:
            result = {'check': check, 'download': download, 'apply': apply, 'recover': recover}[args.action](args.root)
        print(json.dumps(result, ensure_ascii=False))
        if args.action == 'apply' and args.restart:
            import subprocess
            subprocess.Popen([str(Path(args.root) / 'agent语音.exe')], cwd=args.root)
    except Exception as error:
        result = {'ok': False, 'state': 'failed', 'error': str(error)}
        if args.action == 'apply':
            atomic_json(Path(args.root) / 'data/updates/latest.json', result)
        print(json.dumps(result, ensure_ascii=False))
        return 1
    return 0
