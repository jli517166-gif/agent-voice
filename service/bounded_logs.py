"""Bounded application/worker logs without changing the synthesis protocol."""
import codecs,logging,logging.handlers,threading,sys
from pathlib import Path

def message(text):
    """A console locale or closed log pipe must never abort synthesis."""
    try:
        stream=sys.stdout
        encoding=getattr(stream,'encoding',None) or 'utf-8'
        safe=str(text).encode(encoding,errors='backslashreplace').decode(encoding)
        stream.write(safe+'\n');stream.flush()
    except (OSError,ValueError,UnicodeError,AttributeError):pass

class BoundedLog:
    encoding='utf-8'
    def __init__(self,path,max_bytes=10*1024**2,backups=3):
        path=Path(path);path.parent.mkdir(parents=True,exist_ok=True)
        self.handler=logging.handlers.RotatingFileHandler(path,maxBytes=max_bytes,backupCount=backups,encoding='utf-8')
        self.handler.terminator=''
        self.error=None
        # logging's default error handler writes to stderr. The service redirects
        # stderr to this same sink, which recurses if the disk becomes full.
        self.handler.handleError=self.handle_error
    def handle_error(self,record):self.error='日志无法写入，请检查磁盘空间和 data 目录权限'
    def write(self,text):
        if not text:return 0
        self.handler.handle(logging.LogRecord('agent-voice',logging.INFO,'',0,str(text),(),None));return len(text)
    def flush(self):self.handler.flush()
    def isatty(self):return False
    def close(self):self.handler.close()

def pump(stream,path):
    sink=BoundedLog(path)
    def read():
        decoder=codecs.getincrementaldecoder('utf-8')('replace')
        raw=getattr(stream,'buffer',stream)
        try:
            while True:
                block=raw.read1(4096)
                if not block:break
                sink.write(decoder.decode(block))
            sink.write(decoder.decode(b'',final=True))
        except (OSError,ValueError):pass
        finally:
            sink.close()
            try:stream.close()
            except (OSError,ValueError):pass
    thread=threading.Thread(target=read,name='voice-log-writer',daemon=True);thread.start();return thread
