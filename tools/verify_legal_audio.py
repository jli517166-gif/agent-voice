"""Owner-run regression on installed engines using isolated settings/output.

Never writes the installed settings, reads real Harness transcripts or bundles
reference recordings. The report contains engine timings and label checks only.
"""
import argparse,copy,hashlib,json,sys,time
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'service'))
from state import Store
from pipeline import Pipeline
from jobs import Jobs
from aigc import read_wav


def main():
    parser=argparse.ArgumentParser();parser.add_argument('--installed',required=True,type=Path);parser.add_argument('--output',required=True,type=Path);args=parser.parse_args()
    root=args.installed.resolve();settings=root/'data/settings.json';before=settings.read_bytes();old=json.loads(before.decode('utf-8-sig'))
    isolated=args.output.resolve();isolated.mkdir(parents=True,exist_ok=False)
    store=Store(isolated);engines={key:str((root/value).resolve()) for key,value in old['engines'].items()}
    voice=copy.deepcopy(old['voice'])
    for key in ('reference','baseModel','gptModel'):
        if voice.get(key):voice[key]=str((root/voice[key]).resolve())
    store.update({'engines':engines,'voice':voice,'harness':{'codex':False,'deepseek':False},'airi':{'enabled':False,'output':'local'}})
    cases=[]
    for engine,variant in [('cosyvoice','v5turbo'),('gpt-sovits','v5turbo'),('gpt-sovits','proplus')]:
        selected=copy.deepcopy(voice);selected.update(engine=engine,gptVariant=variant,speed=1)
        if engine=='gpt-sovits':selected['gptModel']=''
        store.update({'voice':selected});pipeline=Pipeline(store);jobs=Jobs(store,pipeline)
        try:
            for speed,text in [(1,'这是一段本地语音回归测试。声音保持原有设置，新增标识只记录在音频文件中。'),(1.2,'窗边的灯慢慢亮起来，桌上的笔记还停留在刚才那一页。今天的工作已经完成了一部分，剩下的事情也可以一步一步来。我们把每个问题写清楚，再认真检查，给下一次对话留出从容的时间。外面的风很轻，远处传来几声鸟鸣。喝一口水，伸展一下肩膀，然后继续做好眼前这一件事。')]:
                selected['speed']=speed;started=time.monotonic();submission=jobs.submit(text,'legal-audio-regression',False,voice=selected);job=jobs.jobs[submission['jobId']]
                if not job['done'].wait(600):raise RuntimeError('Real synthesis did not finish within 600 seconds')
                if job['state']!='completed':raise RuntimeError(job['error'])
                result=job['result'];file=jobs.output/result['file'];label=read_wav(file)
                if label is None:raise RuntimeError('Real synthesized output lost its AIGC chunk')
                saved=json.loads(file.with_suffix('.json').read_text(encoding='utf-8'))
                if 'text' in saved:raise RuntimeError('Default privacy wrote transcript')
                cases.append({'engine':engine,'variant':variant if engine=='gpt-sovits' else None,'speed':speed,'chars':len(text),'seconds':round(time.monotonic()-started,3),'duration':result['duration'],'progressive':result.get('progressive',False),'labeled':True,'persistedTranscript':False,'state':'completed'})
                print(json.dumps(cases[-1],ensure_ascii=False),flush=True)
        finally:jobs.close()
    if settings.read_bytes()!=before:raise RuntimeError('Installed settings changed during isolated regression')
    report={'ok':True,'cases':cases,'installedSettingsUnchanged':True,'settingsSha256':hashlib.sha256(before).hexdigest(),'limits':'These real generation samples validate this machine and revision, not a lifetime success-rate or legal clearance guarantee.'}
    (isolated/'results.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')


if __name__=='__main__':main()
