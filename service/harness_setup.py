"""Install known connector entries, preserve accounts and back up changed files."""
import json,os,shutil,uuid
from pathlib import Path
from state import Store,atomic_json

HARNESS_NAMES={'codex':'Codex','deepseek':'DeepSeek Harness','claude':'Claude Code','pi':'pi','opencode':'OpenCode'}

def detect(location,user_home=None):
    """Identify a supported Harness from stable files inside the selected directory.

    Weak signals such as a directory name never win on their own.  This keeps an
    arbitrary project named "codex" from being configured as the real client.
    """
    location=Path(location).resolve();home=Path(user_home or Path.home())
    if not location.is_dir():raise ValueError('所选目录不存在或不是文件夹')
    roots=[location]
    # Users commonly select an application root, its config root, or its parent.
    for relative in ('.codex','.claude','.pi/agent','.config/opencode','profiles/desktop'):
        candidate=location/relative
        if candidate.is_dir():roots.append(candidate)
    if location.name.lower()=='.pi' and (location/'agent').is_dir():roots.append(location/'agent')
    if location.name.lower()=='.config' and (location/'opencode').is_dir():roots.append(location/'opencode')
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
    executable=file_named('codex.exe','codex.cmd');
    if executable:hit('codex',7,executable.name)
    if exists('settings.json') and any(root.name.lower()=='.claude' for root in roots):hit('claude',8,'.claude/settings.json')
    executable=file_named('claude.exe','claude.cmd');
    if executable:hit('claude',7,executable.name)
    if exists('extensions') and any(root.name.lower()=='agent' and root.parent.name.lower()=='.pi' for root in roots):hit('pi',8,'.pi/agent/extensions')
    executable=file_named('pi.exe','pi.cmd');
    if executable:hit('pi',7,executable.name)
    if exists('plugins') and any(root.name.lower()=='opencode' for root in roots):hit('opencode',8,'opencode/plugins')
    if exists('opencode.json') or exists('opencode.jsonc'):hit('opencode',5,'OpenCode 配置文件')
    executable=file_named('opencode.exe','opencode.cmd');
    if executable:hit('opencode',7,executable.name)
    for root in roots:
        for name in ('cordis.yml','cordis.patch.yml'):
            if (root/name).is_file():hit('deepseek',8,str(root/name));break
    ranked=sorted(scores.items(),key=lambda item:item[1],reverse=True)
    best,score=ranked[0]
    if score<7:
        raise ValueError('未识别出已适配的 Harness。请选择实际安装目录或用户配置目录；当前支持 Codex、DeepSeek Harness、Claude Code、pi、OpenCode。')
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
            if old!=body and not (old.startswith('export ') and 'integrations/' in old):raise ValueError('已有同名文件，请检查后更换：'+str(path))
        save(path,body)
    reload=True;legacy_disabled=False;record={'directory':str(location),'files':[]}
    if source=='codex':
        choices=[location,location/'.codex',Path(os.environ.get('CODEX_HOME',str(home/'.codex')))]
        code_home=next((p for p in choices if (p/'sessions').is_dir()),None)
        if code_home is None:raise ValueError('未找到 Codex sessions，请先启动 Codex，或选择实际 CODEX_HOME 目录')
        record['codexHome']=str(code_home.resolve());reload=False
    elif source=='claude':
        folder=location if location.name=='.claude' else Path(os.environ.get('CLAUDE_CONFIG_DIR',str(home/'.claude')))
        path=folder/'settings.json';cfg=json.loads(path.read_text(encoding='utf-8-sig')) if path.exists() else {}
        python=store.resolve(store.get()['engines']['pythonRvc'])
        quote=lambda value:'"'+str(value).replace('\\','/')+'"'
        command=' '.join((quote(python),quote(store.root/'integrations/send_event.py'),'claude',quote(store.root)))
        stops=cfg.setdefault('hooks',{}).setdefault('Stop',[])
        # Replace only this program's previous hook, preserving unrelated hooks.
        for entry in stops:
            entry['hooks']=[h for h in entry.get('hooks',[]) if 'integrations/send_event.py' not in h.get('command','')]
        stops[:]=[e for e in stops if e.get('hooks')]
        stops.append({'hooks':[{'type':'command','command':command,'async':True,'timeout':5}]})
        save(path,json.dumps(cfg,ensure_ascii=False,indent=2))
    elif source=='pi':
        folder=Path(os.environ.get('PI_CODING_AGENT_DIR',str(home/'.pi/agent')))
        wrapper(folder/'extensions/agent-voice.ts','export { default } from '+json.dumps((store.root/'integrations/pi-extension.ts').as_uri())+';\n')
    elif source=='opencode':
        folder=Path(os.environ.get('XDG_CONFIG_HOME',str(home/'.config')))/'opencode'
        wrapper(folder/'plugins/agent-voice.js','export { AgentVoicePlugin } from '+json.dumps((store.root/'integrations/opencode-plugin.mjs').as_uri())+';\n')
    else:
        import yaml
        candidates=[location,location/'profiles/desktop',home/'.dsh/profiles/desktop']
        folder=next((p for p in candidates if (p/'cordis.patch.yml').is_file() or (p/'cordis.yml').is_file()),None)
        if folder is None:raise ValueError('未找到 DeepSeek Cordis 配置，请先启动 Harness，或选择含 cordis.yml 的配置目录')
        plugin=folder/'node_modules/dsh-agent-voice-public'
        wrapper(plugin/'index.mjs','export { default, apply, inject, name } from '+json.dumps((store.root/'integrations/deepseek-plugin.mjs').as_uri())+';\n')
        wrapper(plugin/'package.json',json.dumps({'name':'dsh-agent-voice-public','version':'0.1.0','type':'module','main':'index.mjs'},indent=2)+'\n')
        patch=folder/'cordis.patch.yml';entries=yaml.safe_load(patch.read_text(encoding='utf-8-sig')) if patch.exists() else []
        if entries is None:entries=[]
        if not isinstance(entries,list):raise ValueError('该 DeepSeek 配置格式尚未适配，原配置未改动')
        for entry in entries:
            if isinstance(entry,dict) and isinstance(entry.get('insert'),list):
                entry['insert']=[p for p in entry['insert'] if p.get('name')!='dsh-agent-voice-public']
                for plugin_entry in entry['insert']:
                    if plugin_entry.get('name')=='dsh-tts-autoread':
                        plugin_entry.setdefault('config',{}).update(enabled=False,autostart=False);legacy_disabled=True
            if isinstance(entry,dict) and entry.get('name')=='dsh-tts-autoread':
                entry.setdefault('config',{}).update(enabled=False,autostart=False);legacy_disabled=True
        entries=[e for e in entries if not isinstance(e,dict) or (e.get('name')!='dsh-agent-voice-public' and (set(e)!={'insert'} or e['insert']))]
        entries.append({'insert':[{'id':'agent-voice-tts','name':'dsh-agent-voice-public','config':{}}]})
        save(patch,yaml.safe_dump(entries,allow_unicode=True,sort_keys=False))
    record['files']=changed
    registry=store.root/'data/harness-installations.json';records=json.loads(registry.read_text(encoding='utf8')) if registry.exists() else {}
    records[source]=record;atomic_json(registry,records)
    result={'ok':True,'installed':True,'verified':False,'source':source,'name':HARNESS_NAMES[source],'requiresHarnessReload':reload,'disabledLegacyVoice':legacy_disabled,'files':changed,'backup':str(backup)}
    if detected:result.update(detectedSource=source,detectedName=detected['name'],detectionEvidence=detected['evidence'])
    return result
