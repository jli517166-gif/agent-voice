"""Offline privacy operations; live service owns the same exclusive lock."""
import argparse,json,sys
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'service'))
from state import Store
from operation_lock import OperationLock


def perform(store,action,data):
    from privacy import clean_logs,clear_output_records,diagnostic_summary,rotate_token
    from voice_rights import read_record,save_record
    if action=='diagnostic':return diagnostic_summary(store)
    if action=='rights-read':return read_record(store,data.get('reference',''))
    if action=='rights-save':return save_record(store,data.get('reference',''),data.get('record'))
    if action=='clear-records':return clear_output_records(store)
    if action=='clear-logs':return clean_logs(store.root,1/86400)
    if action=='rotate':return rotate_token(store)
    raise ValueError('未知隐私操作')


def main():
    p=argparse.ArgumentParser();p.add_argument('--root',required=True);p.add_argument('--action',required=True,choices=['diagnostic','rights-read','rights-save','clear-records','clear-logs','rotate']);p.add_argument('--input')
    args=p.parse_args();data=json.loads(Path(args.input).read_text(encoding='utf-8-sig')) if args.input else {}
    with OperationLock(args.root):result=perform(Store(args.root),args.action,data)
    print(json.dumps(result,ensure_ascii=False))


if __name__=='__main__':
    try:main()
    except Exception as error:print(str(error),file=sys.stderr);sys.exit(1)
