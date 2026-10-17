import argparse,json,sys
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'service'))
from state import Store
from diagnostics import inspect_installation
p=argparse.ArgumentParser();p.add_argument('--root',required=True);p.add_argument('--deep',action='store_true');a=p.parse_args()
print(json.dumps(inspect_installation(Store(a.root),a.deep),ensure_ascii=False))
