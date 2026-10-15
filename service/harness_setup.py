"""Install known connector entries, preserve accounts and back up changed files."""
import json,os,shutil,uuid
from pathlib import Path
from state import Store,atomic_json

HARNESS_NAMES={'codex':'Codex','deepseek':'DeepSeek Harness'}

def detect(location,user_home=None):
    """Identify a supported Harness from stable files inside the selected directory.

    Weak signals such as a directory name never win on their own.  This keeps an
    arbitrary project named "codex" from being configured as the real client.
    """
    location=Path(location).resolve();home=Path(user_home or Path.home())
    if not location.is_dir():raise ValueError('所选目录不存在或不是文件夹')
    roots=[location]
    # Users commonly select an application root, its config root, or its parent.
    for relative in ('.codex','profiles/desktop'):
        candidate=location/relative
        if candidate.is_dir():roots.append(candidate)
    scores={key:0 for key in HARNESS_NAMES};evidence={key:[] for key in HARNESS_NAMES}
    def hit(source,points,label):scores[source]+=points;evidence[source].append(label)
    def exists(*parts):return any((root.joinpath(*parts)).exists() for root in roots)
    def file_named(*names):
        wanted={name.lower() for name in names}
        for root in roots:
            try:
                checked=0
                for folder,dirs,files in os.walk(root):
                    relative=Path(folder).relative_to(root)
                    if len(relative.parts)>=2:dirs[:]=[]
                    dirs[:]=[name for name in dirs if name.lower() not in ('node_modules','.git','models','cache')]
                    for name in files:
                        checked+=1
                        if name.lower() in wanted:return Path(folder)/name
                        if checked>=2000:dirs[:]=[];break
            except OSError:pass
        return None
    if exists('sessions'):
        hit('codex',8,'sessions 目录')
        if exists('config.toml'):hit('codex',2,'config.toml')
    executable=file_named('codex.exe','codex.cmd','codex.ps1');
    if executable:hit('codex',7,executable.name)
    # DeepSeek users normally select the desktop installation directory.  Its
    # Cordis profiles live under the Windows user directory, so the installer
    # root itself must be a strong, version-independent detection signature.
    if exists('DeepSeek Harness.exe') and exists('resources','runtime','cli','bin','dsh.cmd'):
        hit('deepseek',10,'DeepSeek Harness.exe + resources/runtime/cli/bin/dsh.cmd')
    elif exists('resources','runtime','cli','bin','dsh.cmd'):
        hit('deepseek',8,'resources/runtime/cli/bin/dsh.cmd')
    for root in roots:
        for name in ('cordis.yml','cordis.patch.yml'):
            if (root/name).is_file():hit('deepseek',8,str(root/name));break
    ranked=sorted(scores.items(),key=lambda item:item[1],reverse=True)
    best,score=ranked[0]
    if score<7:
        raise ValueError('未识别出已实测支持的 Harness。请选择 Codex 的 CODEX_HOME，或 DeepSeek Harness 的实际安装目录。其他 Harness 请按二次开发指南新增适配器。')
    conflicts=[key for key,value in ranked if value>=score-1 and value>=7]
    if len(conflicts)>1:
        names='、'.join(HARNESS_NAMES[key] for key in conflicts)
        raise ValueError('目录同时匹配多个 Harness（'+names+'），请改选更具体的安装目录或配置目录。')
    return {'source':best,'name':HARNESS_NAMES[best],'score':score,'evidence':evidence[best]}

def configure(source,root,location,user_home=None):
    store=Store(root);location=Path(location).resolve();home=Path(user_home or Path.home());detected=None
    if source=='auto':detected=detect(location,home);source=detected['source']
    if source not in store.get()['harness'] or not location.is_dir():raise ValueError('Harness 类型或安装目录无效')
    backup=store.root/'data/connection-backups'/uuid.uuid4().hex;backup.mkdir(parents=True)
    changed=[]
    def save(path,body):
        path=Path(path);path.parent.mkdir(parents=True,exist_ok=True)
        if path.exists():shutil.copy2(path,backup/(str(len(changed))+'-'+path.name))
        temp=path.with_name(path.name+'.agent-voice.tmp');temp.write_text(body,encoding='utf8');os.replace(temp,path);changed.append(str(path))
    def wrapper(path,body):
        if path.exists():
            old=path.read_text(encoding='utf-8-sig')
            owned=(old.startswith('export ') and 'integrations/' in old) or 'dsh-agent-voice-public' in old or 'dsh-agent-voice-control' in old
            if old!=body and not owned:raise ValueError('已有同名文件，请检查后更换：'+str(path))
        save(path,body)
    reload=True;legacy_disabled=False;record={'directory':str(location),'files':[]}
    if source=='codex':
        choices=[location,location/'.codex',Path(os.environ.get('CODEX_HOME',str(home/'.codex')))]
        code_home=next((p for p in choices if (p/'sessions').is_dir()),None)
        if code_home is None:raise ValueError('未找到 Codex sessions，请先启动 Codex，或选择实际 CODEX_HOME 目录')
        record['codexHome']=str(code_home.resolve());reload=False
    else:
        import yaml
        candidates=[location,location/'profiles/desktop',home/'.dsh/profiles/desktop']
        folder=next((p for p in candidates if (p/'cordis.patch.yml').is_file() or (p/'cordis.yml').is_file()),None)
        if folder is None:raise ValueError('未找到 DeepSeek Cordis 配置，请先启动 Harness，或选择含 cordis.yml 的配置目录')
        profiles=[folder];preferred_model=None;trusted_ca=None
        headless=folder.parent/'headless'
        if headless!=folder and ((headless/'cordis.patch.yml').is_file() or (headless/'cordis.yml').is_file()):profiles.append(headless)
        for profile in profiles:
            plugin=profile/'node_modules/dsh-agent-voice-public'
            wrapper(plugin/'index.mjs','export { default, apply, inject, name } from '+json.dumps((store.root/'integrations/deepseek-plugin.mjs').as_uri())+';\n')
            wrapper(plugin/'client.js',(store.root/'integrations/deepseek-control-client.js').read_text(encoding='utf-8-sig'))
            manifest={'name':'dsh-agent-voice-public','version':'0.2.0','type':'module','main':'index.mjs',
                'exports':{'.':'./index.mjs','./client':'./client.js','./package.json':'./package.json'},
                'dsh':{'client':{'inject':['@deepseek-ai/dsh-client-runtime'],'platform':'web'}},
                'peerDependencies':{'react':'^18.2.0','@deepseek-ai/dsh-client-runtime':'*'}}
            wrapper(plugin/'package.json',json.dumps(manifest,ensure_ascii=False,indent=2)+'\n')
            patch=profile/'cordis.patch.yml';entries=yaml.safe_load(patch.read_text(encoding='utf-8-sig')) if patch.exists() else []
            if entries is None:entries=[]
            if not isinstance(entries,list):raise ValueError('该 DeepSeek 配置格式尚未适配，原配置未改动：'+str(patch))
            for entry in entries:
                if profile==folder and isinstance(entry,dict) and entry.get('id')=='agent-default-model' and isinstance(entry.get('config'),dict):preferred_model=dict(entry['config'])
                if isinstance(entry,dict) and isinstance(entry.get('insert'),list):
                    entry['insert']=[p for p in entry['insert'] if p.get('name')!='dsh-agent-voice-public']
                    if profile==folder:
                        for plugin_entry in entry['insert']:
                            if plugin_entry.get('name')=='dsh-local-trusted-ca':trusted_ca=json.loads(json.dumps(plugin_entry))
                            if plugin_entry.get('name')=='dsh-tts-autoread':
                                plugin_entry.setdefault('config',{}).update(enabled=False,autostart=False);legacy_disabled=True
                if profile==folder and isinstance(entry,dict) and entry.get('name')=='dsh-tts-autoread':
                    entry.setdefault('config',{}).update(enabled=False,autostart=False);legacy_disabled=True
            entries=[e for e in entries if not isinstance(e,dict) or (e.get('name')!='dsh-agent-voice-public' and (set(e)!={'insert'} or e['insert']))]
            if profile!=folder and preferred_model:
                model_entry=next((e for e in entries if isinstance(e,dict) and e.get('id')=='agent-default-model'),None)
                if model_entry is None:entries.append({'id':'agent-default-model','name':'@deepseek-ai/dsh-agent-default-model','config':preferred_model})
                else:model_entry['config'].update(preferred_model)
            if profile!=folder and trusted_ca:
                entries=[e for e in entries if not (isinstance(e,dict) and isinstance(e.get('insert'),list) and any(p.get('name')=='dsh-local-trusted-ca' for p in e['insert']))]
                source_plugin=folder/'node_modules/dsh-local-trusted-ca';target_plugin=profile/'node_modules/dsh-local-trusted-ca'
                if (source_plugin/'index.js').is_file() and (source_plugin/'package.json').is_file():
                    save(target_plugin/'index.js',(source_plugin/'index.js').read_text(encoding='utf-8-sig'))
                    save(target_plugin/'package.json',(source_plugin/'package.json').read_text(encoding='utf-8-sig'))
                    entries.append({'insert':[trusted_ca]})
            entries.append({'insert':[{'id':'agent-voice-tts','name':'dsh-agent-voice-public','config':{}}]})
            save(patch,yaml.safe_dump(entries,allow_unicode=True,sort_keys=False))
        record['deepseekProfiles']=[str(profile) for profile in profiles]
    record['files']=changed
    registry=store.root/'data/harness-installations.json';records=json.loads(registry.read_text(encoding='utf8')) if registry.exists() else {}
    records[source]=record;atomic_json(registry,records)
    result={'ok':True,'installed':True,'verified':False,'source':source,'name':HARNESS_NAMES[source],'requiresHarnessReload':reload,'disabledLegacyVoice':legacy_disabled,'files':changed,'backup':str(backup)}
    if detected:result.update(detectedSource=source,detectedName=detected['name'],detectionEvidence=detected['evidence'])
    return result
