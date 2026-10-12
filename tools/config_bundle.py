import argparse
import json
from pathlib import Path
import sys
sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'service'))
from config_bundle import export_bundle,import_bundle

if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('mode',choices=['export','import']);parser.add_argument('--root',required=True);parser.add_argument('--file',required=True);parser.add_argument('--without-assets',action='store_true')
    args=parser.parse_args()
    try:
        result=export_bundle(args.root,args.file,not args.without_assets) if args.mode=='export' else import_bundle(args.root,args.file)
        print(json.dumps(result,ensure_ascii=False))
    except Exception as exc:print(str(exc),file=sys.stderr);sys.exit(1)
