import argparse,json,sys
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'service'))
from state import Store
from voice_profiles import save_profile,apply_profile,seal_profile
from operation_lock import OperationLock
p=argparse.ArgumentParser();p.add_argument('mode',choices=['save','apply','seal']);p.add_argument('--root',required=True);p.add_argument('--file');p.add_argument('--id');a=p.parse_args()
with OperationLock(a.root):
 store=Store(a.root)
 if a.mode=='save':
  d=json.loads(Path(a.file).read_text(encoding='utf-8-sig'));r=save_profile(store,d['name'],d['voice'])
 elif a.mode=='apply':r=apply_profile(store,a.id)
 else:r=seal_profile(store,a.id)
print(json.dumps(r,ensure_ascii=False))
