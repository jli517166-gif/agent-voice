"""Inventory an exact archive and require artifact-bound evidence for public release.

Local installation is unaffected. This deliberately refuses to turn notice counts,
successful synthesis or a user checkbox into redistribution clearance.
"""
import argparse
import hashlib
import json
from pathlib import Path, PurePosixPath
import re
import sys
import zipfile

ROOT = Path(__file__).resolve().parents[1]


def sha256(stream):
    digest = hashlib.sha256()
    for block in iter(lambda: stream.read(1024 * 1024), b''):
        digest.update(block)
    return digest.hexdigest()


def inspect(package, registry=None, review=None):
    registry = registry or json.loads((ROOT / 'docs/legal-components.json').read_text(encoding='utf-8-sig'))
    with Path(package).open('rb') as stream:
        artifact_hash = sha256(stream)
    findings = []
    inventory = []
    with zipfile.ZipFile(package) as archive:
        names = archive.namelist()
        if len(names) != len({name.casefold() for name in names}):
            findings.append('归档含重复或大小写冲突的路径')
        for info in archive.infolist():
            path = PurePosixPath(info.filename)
            if path.is_absolute() or '..' in path.parts or '\\' in info.filename:
                findings.append('归档路径无效：' + info.filename)
            if info.is_dir():
                continue
            with archive.open(info) as stream:
                digest = sha256(stream)
            inventory.append({'path': info.filename, 'bytes': info.file_size, 'sha256': digest})
        lower = [name.lower() for name in names]
        if not any(name.endswith(('/license', '/license.txt')) or name in ('license', 'license.txt') for name in lower):
            findings.append('缺少项目／模块适用的根许可文件')
        if any('/matcha-tts/matcha/' in name for name in lower) and not any(name.endswith('/matcha-tts/license') for name in lower):
            findings.append('Matcha-TTS 主项目版权许可遗漏')
        if any(name.endswith(('.range', '.headers', '.pyc', '.log')) for name in lower):
            findings.append('存在下载分片、头信息、字节码或日志残留')
        if any(re.search(r'(^|/)(settings\.json|welcome\.json|bundled-voice\.json|private-assets\.json)$', name) for name in lower):
            findings.append('包含用户设置或退役默认声音清单')
    # A public review must be tied to this exact artifact and every file, not a
    # generic flag applied to a different build or only a top-level engine.
    records = review.get('files', {}) if isinstance(review, dict) else {}
    evidence_ok = isinstance(review, dict) and review.get('artifactSha256') == artifact_hash
    if not evidence_ok:
        findings.append('缺少绑定当前归档 SHA-256 的逐文件公开分发审查记录')
    missing = []
    for item in inventory:
        decision = records.get(item['path'], {}) if isinstance(records, dict) else {}
        if (decision.get('sha256') != item['sha256'] or decision.get('decision') != 'redistribution-reviewed'
                or str(decision.get('license','')).upper() in ('NOASSERTION','UNKNOWN','PENDING','NONE')
                or not all(isinstance(decision.get(key), str) and decision[key].strip()
                           for key in ('license', 'source', 'evidence', 'reviewer', 'reviewedAt'))):
            missing.append(item['path'])
    if missing:
        findings.append('部分文件缺少匹配哈希、来源、许可及有责任人的审查证据')
    # Report all outstanding program-level work, but do not make unrelated
    # runtime work block a source-only inventory or local use of this package.
    pending = [{'id': c['id'], 'status': c['status'], 'requiredEvidence': c.get('requiredEvidence', [])}
               for c in registry['components'] if c['status'] not in ('owner-approved', 'notice-confirmed')]
    return {'format': 'agent-voice-release-compliance', 'version': 1,
        'artifact': Path(package).name, 'artifactSha256': artifact_hash,
        'publicReleaseEligible': not findings, 'findings': findings,
        'unreviewedFiles': missing, 'files': inventory, 'programPending': pending,
        'limits': 'Engineering evidence check only; does not authenticate a rights holder or replace legal applicability review.'}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--package', required=True, type=Path)
    parser.add_argument('--report', required=True, type=Path)
    parser.add_argument('--review', type=Path)
    parser.add_argument('--public', action='store_true')
    args = parser.parse_args()
    review = json.loads(args.review.read_text(encoding='utf-8-sig')) if args.review else None
    result = inspect(args.package, review=review)
    args.report.parent.mkdir(parents=True, exist_ok=True)
    args.report.write_text(json.dumps(result, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(json.dumps({key: result[key] for key in ('artifactSha256', 'publicReleaseEligible', 'findings')}, ensure_ascii=False))
    return 2 if args.public and not result['publicReleaseEligible'] else 0


if __name__ == '__main__':
    sys.exit(main())
