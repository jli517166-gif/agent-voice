"""Materialize validated archive-internal links without creating Windows symlinks."""
from pathlib import Path
import posixpath
import shutil
import zipfile

LIMIT = 2 * 1024**3


def extract(archive, destination, strip_root=False):
    destination = Path(destination).resolve()
    if destination.exists() and any(destination.iterdir()):
        raise ValueError('解压目标必须为空目录')
    entries = {}
    links = {}
    with zipfile.ZipFile(archive) as source:
        wrapper = None
        total = sum(i.file_size for i in source.infolist())
        if total > LIMIT or len(source.infolist()) > 50000:
            raise ValueError('解压后资源过大')
        seen = set()
        for item in source.infolist():
            raw = item.filename.replace('\\', '/')
            parts = raw.rstrip('/').split('/')
            if raw.startswith('/') or any(p in ('', '.', '..') or ':' in p or p.endswith((' ', '.')) for p in parts):
                raise ValueError('归档路径越界或无效')
            if strip_root:
                if wrapper is None:
                    wrapper = parts[0]
                if parts[0] != wrapper:
                    raise ValueError('源码归档包含多个根目录')
                parts = parts[1:]
            if not parts:
                continue
            name = '/'.join(parts)
            if name.casefold() in seen:
                raise ValueError('归档包含重复或大小写冲突的路径')
            seen.add(name.casefold())
            entries[name] = item
            if item.external_attr >> 16 & 0o170000 == 0o120000:
                if item.file_size > 4096:
                    raise ValueError('链接内容过长')
                target = source.read(item).decode('utf-8').replace('\\', '/')
                relative = posixpath.normpath(posixpath.join(posixpath.dirname(name), target))
                if target.startswith('/') or ':' in target or relative == '..' or relative.startswith('../'):
                    raise ValueError('压缩包含外部链接')
                links[name] = relative
        for name in entries:
            for link in links:
                if name.startswith(link + '/'):
                    raise ValueError('链接与其他归档路径冲突')
        destination.mkdir(parents=True, exist_ok=True)
        def path(name):
            value = destination.joinpath(*name.split('/'))
            if destination not in value.resolve().parents and value.resolve() != destination:
                raise ValueError('归档路径越界')
            # No pre-existing links are permitted in a staging directory.
            for ancestor in (value, *value.parents):
                if ancestor == destination:
                    break
                if ancestor.is_symlink():
                    raise ValueError('解压目录包含链接')
            return value
        for name, item in entries.items():
            if name in links:
                continue
            target = path(name)
            if item.is_dir():
                target.mkdir(parents=True, exist_ok=True)
            else:
                target.parent.mkdir(parents=True, exist_ok=True)
                with source.open(item) as incoming, target.open('wb') as output:
                    shutil.copyfileobj(incoming, output)
        completed = set()
        def materialize(name, trail=()):
            nonlocal total
            if name in completed:
                return
            if name in trail:
                raise ValueError('归档链接循环')
            target_name = links[name]
            if target_name in links:
                materialize(target_name, (*trail, name))
            # Resolve linked children before copying a directory. This also
            # rejects a directory link that would recursively include itself.
            for child in links:
                if target_name == '.' or child.startswith(target_name + '/'):
                    materialize(child, (*trail, name))
            target, value = path(name), path(target_name)
            if not value.exists() or value == target:
                raise ValueError('归档链接目标缺失或循环')
            total += sum(p.stat().st_size for p in value.rglob('*') if p.is_file()) if value.is_dir() else value.stat().st_size
            if total > LIMIT:
                raise ValueError('链接展开后资源过大')
            target.parent.mkdir(parents=True, exist_ok=True)
            if value.is_dir():
                shutil.copytree(value, target)
            else:
                shutil.copy2(value, target)
            completed.add(name)
        for name in links:
            materialize(name)
