"""Engine-neutral state and background jobs for the separate singing workbench."""
import json,os,socket,subprocess,sys,time,uuid
from pathlib import Path


def _atomic(path,value):
    path=Path(path);path.parent.mkdir(parents=True,exist_ok=True)
    temporary=path.with_name(path.name+'.tmp-'+uuid.uuid4().hex)
    temporary.write_text(json.dumps(value,ensure_ascii=False,indent=2),encoding='utf-8')
    os.replace(temporary,path)


class SingingManager:
    def __init__(self,root):
        self.root=Path(root).resolve();self.data=self.root/'data/singing';self.data.mkdir(parents=True,exist_ok=True)
        catalog_path=self.root/'service/singing-engine-catalog.json'
        if not catalog_path.is_file():raise ValueError('缺少唱歌引擎目录：'+str(catalog_path))
        self.catalog=json.loads(catalog_path.read_text(encoding='utf-8-sig'))
        if self.catalog.get('version')!=1:raise ValueError('不支持的唱歌引擎目录版本')
        self.by_id={item['id']:item for item in self.catalog.get('engines',[])}
        if not self.by_id:raise ValueError('唱歌引擎目录为空')
        self.settings_file=self.data/'settings.json'

    def settings(self):
        default={'version':1,'activeEngine':next(iter(self.by_id)),'outputDirectory':str(self.root/'data/singing/output')}
        if self.settings_file.is_file():
            try:default.update(json.loads(self.settings_file.read_text(encoding='utf-8-sig')))
            except (OSError,ValueError):pass
        return default

    def save(self,value):_atomic(self.settings_file,value)

    def engine_root(self,item):
        for relative in item.get('roots',[]):
            candidate=(self.root/relative).resolve()
            if candidate.is_dir():return candidate
        roots=item.get('roots',[])
        return (self.root/(roots[0] if roots else 'singing-engines/'+item['id'])).resolve()

    @staticmethod
    def _online(port):
        try:
            with socket.create_connection(('127.0.0.1',int(port)),timeout=.35):return True
        except OSError:return False

    def list_engines(self):
        settings=self.settings();rows=[]
        for item in self.by_id.values():
            root=self.engine_root(item);python=self.engine_python(item,root);launcher=root/item.get('launcher','')
            rows.append({'id':item['id'],'name':item['name'],'active':settings['activeEngine']==item['id'],'installed':python.is_file() and launcher.is_file(),'online':self._online(item.get('port',0)),'root':str(root),'capabilities':item.get('capabilities',[])})
        return {'activeEngine':settings['activeEngine'],'outputDirectory':settings['outputDirectory'],'engines':rows}

    def select_engine(self,ident):
        if ident not in self.by_id:raise ValueError('未知唱歌引擎：'+str(ident))
        value=self.settings();value['activeEngine']=ident;self.save(value)
        return {'ok':True,'activeEngine':ident}

    def set_output_directory(self,path):
        target=Path(path).expanduser().resolve()
        if target.exists() and not target.is_dir():raise ValueError('输出位置必须是目录：'+str(target))
        target.mkdir(parents=True,exist_ok=True)
        value=self.settings();value['outputDirectory']=str(target);self.save(value)
        return {'ok':True,'outputDirectory':str(target)}

    def active(self):
        ident=self.settings().get('activeEngine');item=self.by_id.get(ident)
        if item is None:raise ValueError('当前唱歌引擎配置无效，请重新选择')
        root=self.engine_root(item);python=self.engine_python(item,root);launcher=root/item.get('launcher','')
        if not python.is_file() or not launcher.is_file():raise ValueError('唱歌引擎未完整安装：'+str(root))
        return item,root,python,launcher

    @staticmethod
    def engine_python(item,root):return root/item.get('python','portable/python.exe')

    def open_engine(self):
        item,root,python,launcher=self.active()
        flags=getattr(subprocess,'CREATE_NEW_PROCESS_GROUP',0)|getattr(subprocess,'DETACHED_PROCESS',0)
        subprocess.Popen(['cmd.exe','/c','start','',str(launcher)],cwd=str(root),stdin=subprocess.DEVNULL,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL,creationflags=flags)
        return {'ok':True,'engine':item['id'],'online':self._online(item.get('port',0)),'message':'已启动引擎；首次加载通常需要 20–90 秒'}

    def submit(self,lyrics,prompt='',duration=30):
        if not isinstance(lyrics,str) or not lyrics.strip():raise ValueError('请先填写歌词')
        if not isinstance(prompt,str) or len(prompt)>1000:raise ValueError('风格描述最长 1000 字')
        try:duration=int(duration)
        except (TypeError,ValueError):raise ValueError('时长必须是整数秒')
        if duration<5 or duration>300:raise ValueError('时长范围为 5–300 秒')
        item,engine_root,python,launcher=self.active();settings=self.settings();output=Path(settings['outputDirectory']).resolve();output.mkdir(parents=True,exist_ok=True)
        ident=uuid.uuid4().hex;jobs=self.data/'jobs';jobs.mkdir(exist_ok=True);job_file=jobs/(ident+'.json')
        record={'id':ident,'state':'queued','engine':item['id'],'engineRoot':str(engine_root),'outputDirectory':str(output),'lyrics':lyrics.strip(),'prompt':prompt.strip(),'duration':duration,'createdAt':time.time(),'pid':None}
        _atomic(job_file,record)
        worker=self.root/'service/singing_job.py'
        if not worker.is_file():raise ValueError('缺少唱歌任务执行器：'+str(worker))
        flags=getattr(subprocess,'CREATE_NO_WINDOW',0)|getattr(subprocess,'CREATE_NEW_PROCESS_GROUP',0)
        process=subprocess.Popen([str(python),'-I','-X','utf8',str(worker),'--root',str(self.root),'--job',ident],cwd=str(self.root),stdin=subprocess.DEVNULL,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL,creationflags=flags)
        record['pid']=process.pid;_atomic(job_file,record)
        return {'ok':True,'jobId':ident,'state':'queued','jobFile':str(job_file),'outputDirectory':str(output)}

    def job(self,ident):
        if not isinstance(ident,str) or not ident.isalnum():raise ValueError('任务 ID 无效')
        path=self.data/'jobs'/(ident+'.json')
        if not path.is_file():raise ValueError('唱歌任务不存在：'+ident)
        value=json.loads(path.read_text(encoding='utf-8-sig'))
        return {key:value.get(key) for key in ('id','state','engine','output','error','createdAt','startedAt','finishedAt','pid')}
