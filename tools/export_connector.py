"""Export project-scoped connection entries after an explicit UI action."""
import argparse
import json
from pathlib import Path
import sys

def write_new(path,body):
    path.parent.mkdir(parents=True,exist_ok=True)
    if path.exists() and path.read_text(encoding='utf-8-sig')!=body:
        raise ValueError('已有同名接入文件，请保留或先改名：'+str(path))
    path.write_text(body,encoding='utf-8')

def export(source,root,project):
    root=Path(root).resolve();project=Path(project).resolve()
    if not project.is_dir():raise ValueError('项目目录不存在')
    if source=='claude':
        data=json.loads((root/'data/settings.json').read_text(encoding='utf-8-sig'))
        python=(root/data['engines']['pythonRvc']).resolve()
        quote=lambda p:'"'+str(p).replace('\\','/')+'"'
        command=' '.join([quote(python),quote(root/'integrations/send_event.py'),'claude',quote(root)])
        path=project/'.claude/settings.json'
        settings=json.loads(path.read_text(encoding='utf-8-sig')) if path.exists() else {}
        stop=settings.setdefault('hooks',{}).setdefault('Stop',[])
        if not any(h.get('command')==command for entry in stop for h in entry.get('hooks',[])):
            stop.append({'hooks':[{'type':'command','command':command,'async':True,'timeout':5}]})
        from importlib.util import spec_from_file_location,module_from_spec
        spec=spec_from_file_location('public_state',root/'service/state.py');module=module_from_spec(spec);spec.loader.exec_module(module)
        module.atomic_json(path,settings)
    elif source=='pi':
        path=project/'.pi/extensions/agent-voice.ts'
        write_new(path,'export { default } from '+json.dumps((root/'integrations/pi-extension.ts').as_uri())+';\n')
    elif source=='opencode':
        path=project/'.opencode/plugins/agent-voice.js'
        write_new(path,'export { AgentVoicePlugin } from '+json.dumps((root/'integrations/opencode-plugin.mjs').as_uri())+';\n')
    elif source=='deepseek':
        folder=project/'node_modules/dsh-agent-voice-public';path=folder/'index.mjs'
        write_new(path,'export { default, apply, inject, name } from '+json.dumps((root/'integrations/deepseek-plugin.mjs').as_uri())+';\n')
        write_new(folder/'package.json',json.dumps({'name':'dsh-agent-voice-public','version':'0.1.0','type':'module','main':'index.mjs'},indent=2)+'\n')
        write_new(project/'agent-voice-deepseek接入说明.txt','已导出插件 dsh-agent-voice-public。请在该项目的 Cordis 插件配置中启用它，并停用旧语音插件以避免重复。原账号、模型及插件配置未改动。\n')
    else:raise ValueError('Codex 使用内置连接，无需导出')
    print(json.dumps({'ok':True,'file':str(path),'requiresHarnessReload':True},ensure_ascii=False))

if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('--source',required=True);parser.add_argument('--root',required=True);parser.add_argument('--project',required=True)
    args=parser.parse_args();export(args.source,args.root,args.project)
