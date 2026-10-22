"""In-file local synthesis provenance without changing PCM or MP3 audio samples.

Container layout follows TC260-PG-202510A (WAV AIGC chunk / ID3 TXXX AIGC).
AgentVoice.local is a local software identity, NOT a registered public-service
provider code. Metadata alone is not full public-service compliance or proof of
voice rights. Existing valid provenance is retained instead of relabelled.
"""
import json
import io
from pathlib import Path
import struct
import uuid

FIELDS = ('Label','ContentProducer','ProduceID','ReservedCode1','ContentPropagator','PropagateID','ReservedCode2')


def metadata(ident=None):
    ident = ident or uuid.uuid4().hex
    return dict(zip(FIELDS, ('1','AgentVoice.local',ident,'','AgentVoice.local',ident,'')))


def payload(value):
    if not isinstance(value,dict) or set(value)!=set(FIELDS) or any(not isinstance(v,str) or len(v)>60000 for v in value.values()):
        raise ValueError('AI 音频标识字段无效')
    if value['Label'] not in ('1','2','3'):
        raise ValueError('AI 音频标签无效')
    encoded=json.dumps(value,ensure_ascii=False,separators=(',',':')).encode('utf-8')
    if len(encoded)>65536:raise ValueError('AI 音频标识溯源超过 64 KB，请分次导出')
    return encoded


def metadata_for_join(paths):
    parents=[]
    for path in paths:
        prior=read_wav(path)
        if prior is not None and prior not in parents:parents.append(prior)
    result=metadata()
    if parents:
        encoded=json.dumps({'sourceMetadata':parents},ensure_ascii=False,separators=(',',':'))
        # Thousands of short sentences must not fail just because provenance
        # repeats the same field names. Reserved data retains every parent.
        if len(encoded.encode('utf-8'))>24000:
            import base64,zlib
            encoded=json.dumps({'encoding':'zlib-base64-json','data':base64.b64encode(zlib.compress(encoded.encode('utf-8'))).decode('ascii')},separators=(',',':'))
        result['ReservedCode1']=encoded
    payload(result)
    return result


def read_wav(path):
    with Path(path).open('rb') as stream:
        return _read_wav(stream,Path(path).stat().st_size)


def read_wav_bytes(audio):
    return _read_wav(io.BytesIO(audio),len(audio))


def label_wav_bytes(audio,value=None):
    if read_wav_bytes(audio) is not None:return audio
    if struct.unpack('<I',audio[4:8])[0]+8!=len(audio):raise ValueError('WAV 尾部数据超出 RIFF 范围')
    data=payload(value or metadata());chunk=b'AIGC'+struct.pack('<I',len(data))+data+(b'\0' if len(data)&1 else b'')
    joined=audio+(b'\0' if len(audio)&1 else b'')+chunk
    return joined[:4]+struct.pack('<I',len(joined)-8)+joined[8:]


def _read_wav(stream,total):
        header=stream.read(12)
        if len(header)!=12 or header[:4]!=b'RIFF' or header[8:]!=b'WAVE':
            raise ValueError('只支持标准 RIFF WAV 标识')
        end=struct.unpack('<I',header[4:8])[0]+8
        if end<12 or end>total:raise ValueError('WAV 文件不完整')
        while stream.tell()+8<=end:
            chunk=stream.read(8);size=struct.unpack('<I',chunk[4:])[0]
            if stream.tell()+size>end:raise ValueError('WAV 数据块不完整')
            if chunk[:4]==b'AIGC':
                if size>65536:raise ValueError('AI 标识过大')
                value=json.loads(stream.read(size).rstrip(b'\0').decode('utf-8'))
                if not isinstance(value,dict):raise ValueError('AI 音频标识必须为对象')
                value=value.get('AIGC',value)
                payload(value)
                return value
            stream.seek(size+(size&1),1)
        return None


def label_wav(path,value=None):
    prior=read_wav(path)
    if prior is not None:return prior
    value=value or metadata();data=payload(value)
    with Path(path).open('r+b') as stream:
        stream.seek(0,2);end=stream.tell()
        stream.seek(4);declared=struct.unpack('<I',stream.read(4))[0]+8
        if declared!=end:raise ValueError('WAV 尾部数据超出 RIFF 范围')
        stream.seek(end)
        if end&1:stream.write(b'\0')
        stream.write(b'AIGC'+struct.pack('<I',len(data))+data+(b'\0' if len(data)&1 else b''))
        size=stream.tell()-8
        if size>0xffffffff:raise ValueError('WAV 超出 RIFF 长度上限')
        stream.seek(4);stream.write(struct.pack('<I',size));stream.flush()
    return value


def syncsafe(value):
    if not 0<=value<1<<28:raise ValueError('ID3 标签过大')
    return bytes(((value>>21)&127,(value>>14)&127,(value>>7)&127,value&127))


def unsyncsafe(data):
    if len(data)!=4 or any(x&128 for x in data):raise ValueError('ID3 长度无效')
    return (data[0]<<21)|(data[1]<<14)|(data[2]<<7)|data[3]


def label_mp3(audio,value):
    text=payload(value).decode('utf-8')
    version=4;frames=b'';offset=0
    if audio.startswith(b'ID3'):
        if len(audio)<10 or audio[3] not in (3,4) or audio[5]!=0:
            raise ValueError('当前 MP3 元数据结构不支持无损合并，未移除原标签')
        version=audio[3];size=unsyncsafe(audio[6:10]);offset=10+size
        if offset>len(audio):raise ValueError('MP3 标签不完整')
        prior=audio[10:offset];position=0
        while position+10<=len(prior) and prior[position:position+4]!=b'\0'*4:
            length=unsyncsafe(prior[position+4:position+8]) if version==4 else int.from_bytes(prior[position+4:position+8],'big')
            end=position+10+length
            if end>len(prior):raise ValueError('MP3 标签帧不完整')
            # Do not overwrite existing AIGC provenance, including unknown
            # producers. A later propagation layer needs its own policy.
            data=prior[position+10:end]
            if prior[position:position+4]==b'TXXX':
                encoding={0:'latin-1',1:'utf-16',2:'utf-16-be',3:'utf-8'}.get(data[0]) if data else None
                if encoding:
                    decoded=data[1:].decode(encoding)
                    description,separator,content=decoded.partition('\0')
                    if description=='AIGC' and separator:
                        existing=json.loads(content.lstrip('\ufeff'));payload(existing.get('AIGC',existing));return audio
            frames+=prior[position:end];position=end
    data=(b'\x03AIGC\0'+text.encode('utf-8')) if version==4 else (b'\x01\xff\xfe'+ 'AIGC'.encode('utf-16-le')+b'\0\0\xff\xfe'+text.encode('utf-16-le'))
    length=syncsafe(len(data)) if version==4 else len(data).to_bytes(4,'big')
    frames+=b'TXXX'+length+b'\0\0'+data
    return b'ID3'+bytes((version,0,0))+syncsafe(len(frames))+frames+audio[offset:]
