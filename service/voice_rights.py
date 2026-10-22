"""Optional local declarations; never a rights certificate or synthesis control."""
import copy
import hashlib
import json
from pathlib import Path
from state import atomic_json

USES=('training','generation','privateUse','publicSharing','commercialUse','referenceRedistribution','weightRedistribution')


def material_hash(path):
    if not path:raise ValueError('请先选择有效的参考录音，再记录素材来源')
    path=Path(path)
    if not path.is_file() or path.stat().st_size>40*1024**2:raise ValueError('请先选择不超过 40 MB 的有效参考录音')
    result=hashlib.sha256()
    with path.open('rb') as stream:
        for block in iter(lambda:stream.read(1024*1024),b''):result.update(block)
    return result.hexdigest()


def public_summary(store,reference):
    sha=material_hash(reference)
    registry=store.root/'data/voice-rights.json'
    records=json.loads(registry.read_text(encoding='utf-8-sig')) if registry.is_file() else {}
    record=records.get(sha,{})
    if not isinstance(record,dict):raise ValueError('本地素材来源记录损坏')
    imported=store.root/'data/imported-license-summaries'/(sha+'.json')
    if not record and imported.is_file():
        value=normalize_summary(json.loads(imported.read_text(encoding='utf-8-sig')))
        if value['materialSha256']!=sha:raise ValueError('导入许可摘要与当前素材不一致')
        value['status']='imported-unverified';return value
    return normalize_summary({'materialSha256':sha,'status':'user-recorded' if record else 'not-recorded',
        'permittedUses':record.get('permittedUses',[]),'licenseSummary':record.get('licenseSummary',''),
        'validUntil':record.get('validUntil',''),'territory':record.get('territory','')})


def normalize_summary(value):
    if not isinstance(value,dict):raise ValueError('许可摘要无效')
    result={key:copy.deepcopy(value.get(key,'')) for key in ('materialSha256','status','licenseSummary','validUntil','territory')}
    for key in result:
        if not isinstance(result[key],str) or len(result[key])>2000:raise ValueError('许可摘要字段无效')
    import re
    if not re.fullmatch('[a-f0-9]{64}',result['materialSha256']):raise ValueError('许可摘要未绑定参考文件')
    if result['status'] not in ('user-recorded','not-recorded','imported-unverified'):raise ValueError('许可摘要不能声明软件已认证授权')
    uses=value.get('permittedUses',[])
    if not isinstance(uses,list) or any(u not in USES for u in uses):raise ValueError('许可用途无效')
    result['permittedUses']=list(dict.fromkeys(uses))
    result['notice']='使用者记录，软件未核验真实授权；不包含合同、身份和私有证据路径。'
    return result


def save_imported_summary(store,reference,summary):
    value=normalize_summary(summary)
    if value['materialSha256']!=material_hash(reference):raise ValueError('许可摘要与参考文件不一致')
    value['status']='imported-unverified'
    # Keep imported claims separate from the user's own local declaration.
    atomic_json(store.root/'data/imported-license-summaries'/(value['materialSha256']+'.json'),value)
    return value


def read_record(store,reference):
    sha=material_hash(store.resolve(reference))
    path=store.root/'data/voice-rights.json'
    records=json.loads(path.read_text(encoding='utf-8-sig')) if path.exists() else {}
    return {'materialSha256':sha,'record':records.get(sha,{}),'summary':public_summary(store,store.resolve(reference))}


def save_record(store,reference,value):
    if not isinstance(value,dict):raise ValueError('来源记录必须为对象')
    allowed=('source','holder','evidenceRef','notes','licenseSummary','validUntil','territory')
    if set(value)-set(allowed)-{'permittedUses'}:raise ValueError('未知来源记录字段')
    record={key:value.get(key,'') for key in allowed}
    if any(not isinstance(text,str) or len(text)>2000 for text in record.values()):raise ValueError('来源记录字段过长或无效')
    sha=material_hash(store.resolve(reference))
    summary=normalize_summary(dict(record,materialSha256=sha,status='user-recorded',permittedUses=value.get('permittedUses',[])))
    record['permittedUses']=summary['permittedUses']
    path=store.root/'data/voice-rights.json'
    records=json.loads(path.read_text(encoding='utf-8-sig')) if path.exists() else {}
    records[sha]=record;atomic_json(path,records)
    return {'ok':True,'summary':summary}
