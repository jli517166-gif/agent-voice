"""Install known connector entries, preserve accounts and back up changed files."""
import json,os,shutil,uuid
from pathlib import Path
from state import Store,atomic_json

def configure(source,root,location,user_home=None):
    store=Store(root);location=Path(location).resolve();home=Path(user_home or Path.home())
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
    return {'ok':True,'installed':True,'verified':False,'source':source,'requiresHarnessReload':reload,'disabledLegacyVoice':legacy_disabled,'files':changed,'backup':str(backup)}
