"""Background ACE-Step adapter. It runs with the selected engine's own Python."""
import argparse,json,os,shutil,time,uuid
from pathlib import Path


def atomic(path,value):
    temporary=path.with_name(path.name+'.tmp-'+uuid.uuid4().hex);temporary.write_text(json.dumps(value,ensure_ascii=False,indent=2),encoding='utf-8');os.replace(temporary,path)


def first_audio(value):
    if isinstance(value,str) and Path(value).suffix.lower() in ('.wav','.mp3','.flac','.m4a') and Path(value).is_file():return Path(value)
    if isinstance(value,dict):
        for key in ('path','value','name'):
            found=first_audio(value.get(key))
            if found:return found
        for child in value.values():
            found=first_audio(child)
            if found:return found
    if isinstance(value,(list,tuple)):
        for child in value:
            found=first_audio(child)
            if found:return found
    return None


def generate(record):
    from gradio_client import Client
    import requests
    server='http://127.0.0.1:7860';api='/generation_wrapper'
    endpoint=requests.get(server+'/gradio_api/info',timeout=15).json()['named_endpoints'][api]
    values=[parameter.get('parameter_default') for parameter in endpoint['parameters']]
    values[0]=record['prompt'] or '清晰自然的人声，旋律完整，编曲平衡'
    values[1]=record['lyrics'];values[2]=108;values[5]='zh';values[8]=False;values[9]=int(record['id'][:8],16)
    values[10]=None;values[11]=record['duration'];values[12]=1;values[13]=None;values[14]='';values[20]='text2music';values[27]='wav';values[29]=False;values[43]=None;values[44]=[]
    result=Client(server).predict(*values,api_name=api);source=first_audio(result)
    if source is None:raise RuntimeError('引擎返回成功，但没有找到生成的音频文件')
    output=Path(record['outputDirectory']);output.mkdir(parents=True,exist_ok=True)
    target=output/('song-'+time.strftime('%Y%m%d-%H%M%S')+'-'+record['id'][:8]+source.suffix.lower())
    shutil.copy2(source,target);return target


def main():
    parser=argparse.ArgumentParser();parser.add_argument('--root',required=True);parser.add_argument('--job',required=True);args=parser.parse_args()
    root=Path(args.root).resolve();job_file=root/'data/singing/jobs'/(args.job+'.json');record=json.loads(job_file.read_text(encoding='utf-8-sig'))
    try:
        record.update(state='generating',startedAt=time.time());atomic(job_file,record)
        target=generate(record);record.update(state='completed',output=str(target),finishedAt=time.time(),error=None)
    except Exception as error:record.update(state='failed',error=str(error),finishedAt=time.time())
    atomic(job_file,record)


if __name__=='__main__':main()
