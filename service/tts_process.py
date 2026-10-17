"""Persistent JSON-line workers shared by isolated local TTS engines."""
import json
import os
from pathlib import Path
import queue
import subprocess
import threading
import time


class TtsProcess:
    label = "TTS"
    ready_timeout = 60
    synthesis_timeout = 120

    def __init__(self, python, model, log, process_kwargs=None):
        self.python, self.model, self.log = Path(python), Path(model), Path(log)
        self.process_kwargs = process_kwargs or {}
        self.process = None
        self.messages = None
        self.reader = None

    def _receive(self, check, timeout):
        deadline = time.monotonic() + timeout
        while time.monotonic() < deadline:
            check()
            try:
                value = self.messages.get(timeout=.1)
            except queue.Empty:
                continue
            if value is None:
                raise RuntimeError(self.label+' 进程已退出，请查看 '+str(self.log))
            if isinstance(value, Exception):
                raise RuntimeError(self.label+' 返回了无效协议消息') from value
            if not isinstance(value,dict):raise RuntimeError(self.label+" 返回了无效协议消息")
            return value
        raise RuntimeError(self.label+' 推理超时，请查看 '+str(self.log))

    def ensure_ready(self, check):
        if self.process is not None and self.process.poll() is None:
            return
        self.close()
        self.validate_model()
        if not self.python.is_file():
            raise ValueError(self.label+' Python 运行时不存在')
        self.log.parent.mkdir(parents=True, exist_ok=True)
        environment = dict(os.environ)
        environment.pop('PYTHONHOME', None)
        environment.pop('PYTHONPATH', None)
        environment['PYTHONUTF8'] = '1'
        environment['PYTHONNOUSERSITE'] = '1'
        self.process = subprocess.Popen(
                self.command(),
                stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                encoding='utf-8', env=environment, cwd=str(self.model.parent), **self.process_kwargs)
        from bounded_logs import pump
        try:self.log_reader=pump(self.process.stderr,self.log)
        except Exception:self.close();raise
        messages = self.messages = queue.Queue()
        stream = self.process.stdout

        def read():
            try:
                for line in stream:
                    try: messages.put(json.loads(line))
                    except (ValueError, TypeError) as error: messages.put(error)
            finally:
                messages.put(None)
                stream.close()

        self.reader = threading.Thread(target=read, name=self.label+'-response-reader', daemon=True)
        self.reader.start()
        try:
            value = self._receive(check, self.ready_timeout)
            if value.get('ready') is not True:
                raise RuntimeError(self.label+' 模型加载失败：' + str(value.get('error', '未知错误')))
            self.gpu=value.get('gpu')
            self.ready_info=value
        except Exception:
            self.close()
            raise

    def synthesize(self, text, target, speed, check, seed=None):
        self.ensure_ready(check)
        try:
            check()
            request=self.request(text,target,speed)
            if seed is not None:request['seed']=seed
            self.process.stdin.write(json.dumps(request, ensure_ascii=False) + '\n')
            self.process.stdin.flush()
            value = self._receive(check, self.synthesis_timeout)
            if value.get('ok') is not True:
                raise RuntimeError(self.label+' 合成失败：' + str(value.get('error', '未知错误')))
            check()
        except Exception:
            # Cancellation must not leave a late response for the next request.
            self.close()
            raise

    def close(self):
        process, self.process = self.process, None
        if process is None:
            return
        try:
            if process.stdin:
                try: process.stdin.close()
                except OSError: pass
            try: process.wait(timeout=2)
            except subprocess.TimeoutExpired:
                from owned_process import terminate
                terminate(process)
        finally:
            if self.reader: self.reader.join(timeout=1)
            self.reader = None
            if getattr(self,'log_reader',None):self.log_reader.join(timeout=1)

