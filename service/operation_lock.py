"""Cross-process exclusion between service ownership and offline package updates."""
from pathlib import Path
from functools import wraps
import os

class OperationLock:
    def __init__(self,root):self.path=Path(root).resolve()/'data/engine-operation.lock';self.stream=None
    def __enter__(self):
        self.path.parent.mkdir(parents=True,exist_ok=True);stream=self.path.open('a+b')
        try:
            if not self.path.stat().st_size:stream.write(b'0');stream.flush()
            stream.seek(0)
            if os.name=='nt':
                import msvcrt
                msvcrt.locking(stream.fileno(),msvcrt.LK_NBLCK,1)
            else:
                import fcntl
                fcntl.flock(stream.fileno(),fcntl.LOCK_EX|fcntl.LOCK_NB)
        except OSError as exc:
            stream.close();raise ValueError('语音服务或另一项文件操作正在运行；请关闭服务或等待操作完成后重试') from exc
        self.stream=stream;return self
    def __exit__(self,*exc):
        if self.stream:
            try:
                self.stream.seek(0)
                if os.name=='nt':
                    import msvcrt
                    msvcrt.locking(self.stream.fileno(),msvcrt.LK_UNLCK,1)
                else:
                    import fcntl
                    fcntl.flock(self.stream.fileno(),fcntl.LOCK_UN)
            finally:self.stream.close();self.stream=None

def offline_operation(fn):
    @wraps(fn)
    def wrapped(root,*args,**kwargs):
        with OperationLock(root):return fn(root,*args,**kwargs)
    return wrapped
