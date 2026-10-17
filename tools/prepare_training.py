"""Validate a speaker's CSV/audio and prepare deterministic Kaldi splits for CosyVoice."""
import argparse,csv,hashlib,json,os,random,shutil,sys,uuid
from pathlib import Path,PureWindowsPath

def train_repo(path):
    root=Path(path);required=['cosyvoice/bin/train.py','cosyvoice/bin/average_model.py','examples/libritts/cosyvoice3/run.sh','examples/libritts/cosyvoice3/conf/cosyvoice3.yaml','tools/make_parquet_list.py']
    missing=[n for n in required if not (root/n).is_file()]
    return {'ok':not missing,'missing':missing,'message':'训练源码完整；还需单独验证 Linux/WSL、CUDA 和训练依赖' if not missing else '当前目录缺少完整训练脚本；请另行克隆官方完整训练仓库，不要覆盖当前推理环境'}

def validate_dataset(directory):
    import numpy as np,soundfile as sf
    root=Path(directory).resolve();metadata=root/'metadata.csv';errors=[];warnings=[];items=[];seen={}
    if not metadata.is_file():return {'ok':False,'errors':['缺少 metadata.csv；表头为 file,text,speaker,split'],'warnings':[],'items':[]}
    with metadata.open(encoding='utf-8-sig',newline='') as stream:
        reader=csv.DictReader(stream)
        if not {'file','text','speaker'}.issubset(reader.fieldnames or []):return {'ok':False,'errors':['CSV 表头必须包含 file,text,speaker；split 可选'],'warnings':[],'items':[]}
        for rowno,row in enumerate(reader,2):
            if rowno>100001:errors.append('素材超过十万条，请拆分数据集');break
            try:
                if None in row:raise ValueError('CSV 列数过多；含英文逗号的台词请用双引号包裹')
                name=(row.get('file') or '').strip();p=(root/name).resolve();text=(row.get('text') or '').strip();speaker=(row.get('speaker') or '').strip();split=(row.get('split') or '').strip()
                if not name or root not in p.parents or not p.is_file():raise ValueError('录音路径不存在或不在所选素材目录内')
                if p.suffix.lower()!='.wav' or p.stat().st_size>40*1024**2:raise ValueError('请先准备不超过 40 MB 的 WAV 文件')
                if not text or '\n' in text or '\r' in text or len(text)>2000:raise ValueError('逐字台词为空、包含换行或过长')
                if not speaker:raise ValueError('speaker 不能为空；同一角色统一使用同一个名称')
                if split not in ('','train','dev','test'):raise ValueError('split 只允许留空或 train/dev/test')
                info=sf.info(p)
                if info.channels!=1:raise ValueError('训练素材必须为单声道')
                if not 1<=info.duration<=60 or not 16000<=info.samplerate<=48000:raise ValueError('录音需为 1～60 秒，采样率为 16000～48000 Hz')
                audio,_=sf.read(p,dtype='float32');rms=float(np.sqrt(np.mean(audio*audio)))
                if not np.isfinite(audio).all() or rms<.002:raise ValueError('录音为静音、音量过小或包含无效样本')
                if float(np.mean(np.abs(audio)>=.999))>.01:raise ValueError('录音存在明显削波，请重新准备')
                sha=hashlib.sha256(p.read_bytes()).hexdigest()
                if sha in seen:raise ValueError('与第 %s 行音频完全重复，会造成训练/验收数据泄漏'%seen[sha])
                seen[sha]=rowno
                items.append({'id':'utt_%06d'%(rowno-1),'file':str(p),'text':text,'speaker':speaker,'split':split,'sha256':sha,'duration':info.duration})
            except (ValueError,RuntimeError,OSError) as exc:errors.append('第 %d 行：%s'%(rowno,exc))
    if len(items)<10:errors.append('有效素材不足 10 条；先准备至少 10 条不同完整短句，以便分出训练、验证与最终试听集')
    seconds=sum(i['duration'] for i in items)
    if seconds<600:warnings.append('当前共 %.1f 秒，结构检查不代表训练效果；请优先增加干净、准确、多样的真实录音'%seconds)
    if any(i['split'] for i in items):
        if any(not i['split'] for i in items):errors.append('使用自定义 split 时每行都必须填写')
    else:
        for speaker in sorted({i['speaker'] for i in items}):
            group=[i for i in items if i['speaker']==speaker];random.Random(42).shuffle(group)
            if len(group)<3:errors.append('说话人 %s 少于 3 条，无法划分三个集合'%speaker);continue
            held=max(1,int(len(group)*.1))
            for n,i in enumerate(group):i['split']='dev' if n<held else 'test' if n<held*2 else 'train'
    counts={k:sum(i['split']==k for i in items) for k in ['train','dev','test']}
    if not all(counts.values()):errors.append('train/dev/test 三个集合均需至少一条素材')
    for speaker in {i['speaker'] for i in items}:
        if not any(i['speaker']==speaker and i['split']=='train' for i in items):errors.append('说话人 %s 没有训练集素材'%speaker)
    return {'ok':not errors,'errors':errors,'warnings':warnings,'count':len(items),'seconds':seconds,'splits':counts,'items':items,'scope':'检查文件与清单结构；不会自动确认逐字台词正确，也不会开始训练'}

def training_path(path):
    if os.name!='nt':return Path(path).resolve().as_posix()
    p=PureWindowsPath(str(Path(path).resolve()))
    if len(p.drive)!=2 or p.drive[1]!=':':raise ValueError('WSL 清单不支持网络共享路径；请选择本地盘符目录')
    return '/mnt/'+p.drive[0].lower()+'/'+('/'.join(p.parts[1:]))

def prepare(directory,output):
    result=validate_dataset(directory)
    if not result['ok']:return result
    dest=Path(output).resolve()
    if any(c.isspace() for c in str(dest)):raise ValueError('官方 wav.scp 读取器不支持路径含空格；请选择不含空格的新输出目录，例如 E:\\agent_voice_training\\dataset_01')
    if dest.exists():raise ValueError('输出目录已存在，保留原内容；请选择新的目录')
    dest.parent.mkdir(parents=True,exist_ok=True);stage=dest.with_name('.training-'+uuid.uuid4().hex);stage.mkdir()
    try:
        (stage/'audio').mkdir();groups={k:[] for k in ['train','dev','test']}
        for item in result['items']:
            name=item['id']+'.wav';shutil.copy2(item['file'],stage/'audio'/name);groups[item['split']].append(dict(item,file=training_path(dest/'audio'/name),speaker='spk_'+hashlib.sha256(item['speaker'].encode()).hexdigest()[:12]))
        for split,items in groups.items():
            folder=stage/split;folder.mkdir()
            for filename,text in [('wav.scp',''.join(i['id']+' '+i['file']+'\n' for i in items)),('text',''.join(i['id']+' '+i['text']+'\n' for i in items)),('utt2spk',''.join(i['id']+' '+i['speaker']+'\n' for i in items)),('spk2utt',''.join(s+' '+' '.join(i['id'] for i in items if i['speaker']==s)+'\n' for s in sorted({i['speaker'] for i in items})))]:
                (folder/filename).write_text(text,encoding='utf-8')
        report={k:v for k,v in result.items() if k!='items'};report.update(pathStyle='wsl' if os.name=='nt' else 'posix',output=str(dest),trainingData=training_path(dest))
        (stage/'dataset-report.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8');os.replace(stage,dest);return report
    finally:
        if stage.exists() and stage.parent==dest.parent:shutil.rmtree(stage)

def main():
    p=argparse.ArgumentParser();p.add_argument('mode',choices=['validate','prepare','inspect-repo']);p.add_argument('--input',required=True);p.add_argument('--output');p.add_argument('--report-only',action='store_true');a=p.parse_args()
    try:
        if a.mode=='prepare' and not a.output:raise ValueError('生成清单必须指定 --output 新目录')
        result=train_repo(a.input) if a.mode=='inspect-repo' else prepare(a.input,a.output) if a.mode=='prepare' and a.output else validate_dataset(a.input)
        result={k:v for k,v in result.items() if k!='items'};print(json.dumps(result,ensure_ascii=False,indent=2));return 0 if result['ok'] or a.report_only else 2
    except (ValueError,OSError,UnicodeError) as exc:print(json.dumps({'ok':False,'errors':[str(exc)]},ensure_ascii=False));return 2
if __name__=='__main__':raise SystemExit(main())
