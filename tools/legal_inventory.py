"""Read-only archive inventory for legal review; this is not a compliance verdict.

Uses only the Python standard library. Does not install, run, upload or rewrite
package contents. Large archive hashes remain explicitly unverified unless
--hash-all is used. Output contains component paths, not user configuration.
"""
import argparse
import csv
import email
import hashlib
import io
import json
from pathlib import Path, PurePosixPath
import re
import subprocess
import zipfile


def digest(stream):
    result = hashlib.sha256()
    for chunk in iter(lambda: stream.read(1024 * 1024), b''):
        result.update(chunk)
    return result.hexdigest()


def inspect(release, repository, hash_all=False):
    manifest = json.loads((release / '发行清单.json').read_text(encoding='utf-8-sig'))
    report = {'format': 'agent-voice-legal-inventory', 'version': 1,
              'sourceCommit': manifest['sourceCommit'], 'archives': [],
              'limits': ['Not a complete SBOM, rights clearance or legal opinion.',
                         'License-like filenames and package metadata are evidence, not grants.',
                         'No assessment of unpublished rights contracts or actual users.']}
    distributions = []
    for item in manifest['files']:
        path = release / item['path']
        # Do not let a tampered release manifest select unrelated files.
        if release.resolve() not in path.resolve().parents:
            raise ValueError('Archive path escapes release directory')
        row = {'path': item['path'], 'kind': item['kind'], 'bytes': path.stat().st_size,
               'sha256Declared': item['sha256'], 'hashVerified': False}
        if hash_all or path.stat().st_size < 150_000_000:
            with path.open('rb') as stream:
                row['sha256Actual'] = digest(stream)
            row['hashVerified'] = row['sha256Actual'] == item['sha256']
            if not row['hashVerified']:
                raise ValueError('Archive SHA-256 does not match manifest: ' + item['path'])
        with zipfile.ZipFile(path) as archive:
            names = archive.namelist()
            row.update(entries=len(names),
                legalEntries=[name for name in names if re.search(
                    r'(^|/)[^/]*(?:licen[cs]e|copyright|notice|copying|eula)[^/]*(/|$)', name, re.I)],
                audioEntries=[name for name in names if PurePosixPath(name).suffix.lower()
                              in {'.wav', '.flac', '.mp3', '.m4a', '.ogg'}],
                modelEntries=[name for name in names if PurePosixPath(name).suffix.lower()
                              in {'.pt', '.pth', '.onnx', '.safetensors', '.bin', '.ckpt'}],
                leftoverEntries=[name for name in names if name.endswith(
                    ('.headers', '.range', '.pyc', '.log'))])
            row['ffmpeg'] = []
            for name in names:
                if name.endswith('.dist-info/METADATA'):
                    if archive.getinfo(name).file_size > 8 * 1024 * 1024:
                        raise ValueError('Unexpectedly large package metadata')
                    metadata = email.message_from_bytes(archive.read(name))
                    classifiers = [value for value in metadata.get_all('Classifier', [])
                                   if value.startswith('License ::')]
                    declared = str(metadata.get('License-Expression') or
                                   metadata.get('License') or '')
                    distributions.append({'archive': item['path'], 'entry': name,
                        'name': metadata.get('Name', ''), 'version': metadata.get('Version', ''),
                        'licenseDeclared': ' '.join(declared.split())[:180],
                        'licenseClassifiers': ' | '.join(classifiers),
                        'reviewStatus': 'unreviewed; metadata only'})
                if name.lower().endswith('ffmpeg.exe'):
                    with archive.open(name) as stream:
                        sha = digest(stream)
                    row['ffmpeg'].append({'entry': name, 'sha256': sha})
        report['archives'].append(row)
    def git(*args):
        return subprocess.check_output(['git', '-c', 'core.quotePath=false', *args],
            cwd=repository).decode('utf-8', errors='replace').strip()
    report['repository'] = {'commit': git('rev-parse', 'HEAD'),
        'branch': git('branch', '--show-current'),
        'rootLicensePresent': any((repository / name).is_file()
                                 for name in ('LICENSE', 'LICENSE.md', 'LICENSE.txt', 'COPYING')),
        'visibility': 'not determined by local Git; verify authenticated hosting metadata'}
    binaries = []
    for line in git('rev-list', '--objects', '--all').splitlines():
        sha, _, name = line.partition(' ')
        if PurePosixPath(name).suffix.lower() in {
                '.wav', '.mp3', '.flac', '.ogg', '.m4a', '.pth', '.pt', '.onnx',
                '.safetensors', '.ckpt', '.zip', '.ttsvoice', '.avvoice', '.avmodule',
                '.ttsenvironment', '.ttsengine'}:
            binaries.append({'object': sha, 'path': name})
    report['selectedBinaryObjectsInReachableGitHistory'] = binaries
    report['packageMetadataCount'] = len(distributions)
    return report, distributions


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--release', required=True, type=Path)
    parser.add_argument('--repository', type=Path, default=Path(__file__).resolve().parents[1])
    parser.add_argument('--output', required=True, type=Path)
    parser.add_argument('--hash-all', action='store_true')
    args = parser.parse_args()
    report, rows = inspect(args.release.resolve(), args.repository.resolve(), args.hash_all)
    args.output.mkdir(parents=True, exist_ok=True)
    (args.output / 'inventory.json').write_text(json.dumps(report, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    fields = ['archive', 'entry', 'name', 'version', 'licenseDeclared', 'licenseClassifiers', 'reviewStatus']
    buffer = io.StringIO(newline='')
    writer = csv.DictWriter(buffer, fieldnames=fields, lineterminator='\n')
    writer.writeheader()
    writer.writerows(rows)
    # Avoid translating csv's existing CRLF to CRCRLF on Windows.
    (args.output / 'components.csv').write_bytes(buffer.getvalue().encode('utf-8-sig'))
    print(json.dumps({'archives': len(report['archives']), 'metadataRecords': len(rows),
                      'output': str(args.output), 'legalClearance': False}, ensure_ascii=False))


if __name__ == '__main__':
    main()
