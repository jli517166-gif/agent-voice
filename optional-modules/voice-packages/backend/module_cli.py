"""Only installed by the optional voice-packages feature module."""
import argparse,json,sys
from pathlib import Path

def main():
    p=argparse.ArgumentParser()
    p.add_argument('action',choices=['import','export','register-current','apply'])
    p.add_argument('--root',required=True);p.add_argument('--file')
    p.add_argument('--name',default='导入的声音');p.add_argument('--profile-id')
    p.add_argument('--package-mode',choices=['lightweight','full','trained','trained-full'],default='lightweight')
    p.add_argument('--trained-model-dir')
    args=p.parse_args();root=Path(args.root).resolve()
    if not (root/'service/state.py').is_file():raise ValueError('请选择完整 Agent Voice 安装目录')
    installed=root/'modules/voice-packages'
    if Path(__file__).resolve().parent!=installed/'backend' or not (installed/'module.json').is_file():
        raise ValueError('请先从主程序安装音色包模块，再使用其导入导出功能')
    sys.path.insert(0,str(root/'service'))
    if not (root/'service/voice_rights.py').is_file():raise ValueError('音色包模块 1.1.0 需要已含素材来源记录功能的新版主程序，请先升级本体；音色文件未改动')
    sys.path.insert(0,str(Path(__file__).resolve().parent))
    from tts_bundle import export_model
    from voice_library import import_and_register,register_current,apply_saved
    if args.action in ('import','export') and not args.file:p.error('导入或导出需要 --file')
    if args.action=='import':result=import_and_register(root,args.file)
    elif args.action=='export':result=export_model(root,args.file,package_mode=args.package_mode,trained_model_dir=args.trained_model_dir)
    elif args.action=='register-current':result={'ok':True,'profile':register_current(root,args.name)}
    else:
        if not args.profile_id:p.error('应用声音需要 --profile-id')
        result=apply_saved(root,args.profile_id)
    print(json.dumps(result,ensure_ascii=False))

if __name__=='__main__':main()
