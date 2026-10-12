"""Hidden service launcher; diagnostics always go into the public app's logs."""
import argparse
import hashlib
import os
from pathlib import Path
import sys

parser=argparse.ArgumentParser();parser.add_argument('--root',required=True);args=parser.parse_args()
root=Path(args.root).resolve();mutex=None
if os.name=='nt':
    import ctypes
    kernel=ctypes.WinDLL('kernel32',use_last_error=True)
    kernel.CreateMutexW.argtypes=(ctypes.c_void_p,ctypes.c_int,ctypes.c_wchar_p);kernel.CreateMutexW.restype=ctypes.c_void_p
    kernel.CloseHandle.argtypes=(ctypes.c_void_p,);kernel.CloseHandle.restype=ctypes.c_int
    name='Local\\AgentVoiceService-'+hashlib.sha256(str(root).casefold().encode('utf-8')).hexdigest()[:24]
    mutex=kernel.CreateMutexW(None,False,name)
    if not mutex:raise OSError(ctypes.get_last_error(),'无法创建语音服务单实例锁')
    if ctypes.get_last_error()==183:
        kernel.CloseHandle(mutex);raise SystemExit(0)
logs=root/'data/logs';logs.mkdir(parents=True,exist_ok=True)
stream=(logs/'service.log').open('a',encoding='utf-8',buffering=1)
sys.stdout=stream;sys.stderr=stream
sys.dont_write_bytecode=True
sys.path.insert(0,str(Path(__file__).resolve().parent))
os.environ['PYTHONDONTWRITEBYTECODE']='1'
os.environ['HF_HUB_OFFLINE']='1';os.environ['TRANSFORMERS_OFFLINE']='1'
sys.argv=[str(Path(__file__).with_name('server.py')),'--root',str(root)]
from server import main
try:main()
finally:
    if mutex:kernel.CloseHandle(mutex)
