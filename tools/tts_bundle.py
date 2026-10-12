import argparse,json,sys
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'service'))
from tts_bundle import export_model,import_model
parser=argparse.ArgumentParser();parser.add_argument('mode',choices=('export','import'));parser.add_argument('--root',required=True);parser.add_argument('--file',required=True);parser.add_argument('--profile-id')
args=parser.parse_args()
try:
    result=export_model(args.root,args.file,args.profile_id) if args.mode=='export' else import_model(args.root,args.file)
    print(json.dumps(result,ensure_ascii=False))
except (ValueError,KeyError,OSError) as error:
    print('本地模型操作失败：'+str(error),file=sys.stderr);raise SystemExit(1)
