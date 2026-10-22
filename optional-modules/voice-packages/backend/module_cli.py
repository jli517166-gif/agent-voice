"""Only installed by the optional voice-packages feature module."""
import argparse,json,sys
from pathlib import Path

def main():
    p=argparse.ArgumentParser()
    p.add_argument('action',choices=['import','export'])
    p.add_argument('--root',required=True);p.add_argument('--file',required=True)
    p.add_argument('--package-mode',choices=['lightweight','full','trained','trained-full'],default='lightweight')
    p.add_argument('--trained-model-dir')
    args=p.parse_args();root=Path(args.root).resolve()
    if not (root/'service/state.py').is_file():raise ValueError('请选择完整 Agent Voice 安装目录')
    installed=root/'modules/voice-packages'
    if Path(__file__).resolve().parent!=installed/'backend' or not (installed/'module.json').is_file():
        raise ValueError('请先从主程序安装音色包模块，再使用其导入导出功能')
    sys.path.insert(0,str(root/'service'))
    sys.path.insert(0,str(Path(__file__).resolve().parent))
    from tts_bundle import export_model,import_model
    if args.action=='import':result=import_model(root,args.file)
    else:result=export_model(root,args.file,package_mode=args.package_mode,trained_model_dir=args.trained_model_dir)
    print(json.dumps(result,ensure_ascii=False))

if __name__=='__main__':main()
