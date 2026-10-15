"""Isolated Piper runtime with one cached model and cancellable requests."""
import json
import os
from pathlib import Path
import queue
import subprocess
import threading
import time


def model_config(model):
    model = Path(model)
    if model.suffix.lower() != '.onnx' or not model.is_file():
        raise ValueError('请选择 Piper 的 .onnx 模型文件')
    config = Path(str(model) + '.json')
    if not config.is_file() or config.stat().st_size > 1024 * 1024:
        raise ValueError('Piper 模型旁需要同名 .onnx.json 配置')
    value = json.loads(config.read_text(encoding='utf-8-sig'))
    if not isinstance(value, dict) or not isinstance(value.get('audio'), dict):
        raise ValueError('Piper 模型配置格式无效')
    rate = value.get('audio', {}).get('sample_rate')
    if isinstance(rate, bool) or not isinstance(rate, int) or not 8000 <= rate <= 48000 or not isinstance(value.get('phoneme_id_map'), dict):
        raise ValueError('Piper 模型配置格式无效')
    return config


class PiperClient:
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
                raise RuntimeError('Piper 进程已退出，请查看 data/logs/piper-worker.log')
            if isinstance(value, Exception):
                raise RuntimeError('Piper 返回了无效协议消息') from value
            return value
        raise RuntimeError('Piper 推理超时，请查看 data/logs/piper-worker.log')

    def ensure_ready(self, check):
        if self.process is not None and self.process.poll() is None:
            return
        self.close()
        model_config(self.model)
        if not self.python.is_file():
            raise ValueError('Piper Python 运行时不存在')
        self.log.parent.mkdir(parents=True, exist_ok=True)
        environment = dict(os.environ)
        environment.pop('PYTHONHOME', None)
        environment.pop('PYTHONPATH', None)
        environment['PYTHONUTF8'] = '1'
        with self.log.open('ab') as log:
            self.process = subprocess.Popen(
                [str(self.python), '-I', '-X', 'utf8', str(Path(__file__).with_name('piper_worker.py')), '--model', str(self.model)],
                stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=log,
                encoding='utf-8', env=environment, cwd=str(self.model.parent), **self.process_kwargs)
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

        self.reader = threading.Thread(target=read, name='piper-response-reader', daemon=True)
        self.reader.start()
        try:
            value = self._receive(check, 60)
            if value.get('ready') is not True:
                raise RuntimeError('Piper 模型加载失败：' + str(value.get('error', '未知错误')))
        except Exception:
            self.close()
            raise

    def synthesize(self, text, target, speed, check):
        self.ensure_ready(check)
        try:
            check()
            self.process.stdin.write(json.dumps({'text': text, 'output': str(target), 'speed': speed}, ensure_ascii=False) + '\n')
            self.process.stdin.flush()
            value = self._receive(check, 120)
            if value.get('ok') is not True:
                raise RuntimeError('Piper 合成失败：' + str(value.get('error', '未知错误')))
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
                process.terminate()
                process.wait(timeout=3)
        finally:
            if self.reader: self.reader.join(timeout=1)
            self.reader = None

