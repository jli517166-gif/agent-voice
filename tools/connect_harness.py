import argparse,json,sys
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'service'))
from harness_setup import configure
parser=argparse.ArgumentParser();parser.add_argument('--source',default='auto',choices=['auto','codex','deepseek','claude','pi','opencode']);parser.add_argument('--root',required=True);parser.add_argument('--location',required=True)
args=parser.parse_args()
try:print(json.dumps(configure(args.source,args.root,args.location),ensure_ascii=False))
except (ValueError,KeyError,OSError) as error:
    print('接入配置失败：'+str(error),file=sys.stderr);raise SystemExit(1)
