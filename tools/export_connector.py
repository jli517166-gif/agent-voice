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
    if source=='deepseek':
        folder=project/'node_modules/dsh-agent-voice-public';path=folder/'index.mjs'
        write_new(path,'export { default, apply, inject, name } from '+json.dumps((root/'integrations/deepseek-plugin.mjs').as_uri())+';\n')
        write_new(folder/'package.json',json.dumps({'name':'dsh-agent-voice-public','version':'0.1.0','type':'module','main':'index.mjs'},indent=2)+'\n')
        write_new(project/'agent-voice-deepseek接入说明.txt','已导出插件 dsh-agent-voice-public。请在该项目的 Cordis 插件配置中启用它，并停用旧语音插件以避免重复。原账号、模型及插件配置未改动。\n')
    else:raise ValueError('正式版仅保留 DeepSeek 项目连接器导出；Codex 使用内置连接。其他 Harness 请按二次开发指南新增适配器。')
    print(json.dumps({'ok':True,'file':str(path),'requiresHarnessReload':True},ensure_ascii=False))

if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('--source',required=True);parser.add_argument('--root',required=True);parser.add_argument('--project',required=True)
    args=parser.parse_args();export(args.source,args.root,args.project)
