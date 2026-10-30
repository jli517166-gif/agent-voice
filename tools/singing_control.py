import argparse,json,sys,time
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'service'))
from singing import SingingManager

def main():
    parser=argparse.ArgumentParser();parser.add_argument('action',choices=['list','select','output','open','generate','job']);parser.add_argument('--root',required=True);parser.add_argument('--engine');parser.add_argument('--path');parser.add_argument('--lyrics');parser.add_argument('--prompt',default='');parser.add_argument('--duration',type=int,default=30);parser.add_argument('--job');parser.add_argument('--stdin',action='store_true')
    args=parser.parse_args();payload=json.loads(sys.stdin.read() or '{}') if args.stdin else {};manager=SingingManager(args.root)
    if args.action=='list':result=manager.list_engines()
    elif args.action=='select':result=manager.select_engine(payload.get('engine',args.engine))
    elif args.action=='output':result=manager.set_output_directory(payload.get('path',args.path))
    elif args.action=='open':result=manager.open_engine()
    elif args.action=='generate':result=manager.submit(payload.get('lyrics',args.lyrics),payload.get('prompt',args.prompt),payload.get('duration',args.duration))
    else:result=manager.job(payload.get('job',args.job))
    print(json.dumps(result,ensure_ascii=False))

if __name__=='__main__':main()
