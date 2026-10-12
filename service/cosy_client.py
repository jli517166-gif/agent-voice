"""Public worker client; neural TTS ownership is isolated from private services."""
import hashlib
import io
import json
import os
from pathlib import Path
import subprocess
import threading
import time
import urllib.error
import urllib.request
import uuid
import wave


class NaturalTTSClient:
    def __init__(self, settings, process_kwargs=None):
        self.settings = dict(settings)
        self.port = int(settings.get("port", 8821))
        if not 1024 <= self.port <= 65535:
            raise ValueError("本地自然语音端口不合法")
        self.url = "http://127.0.0.1:%d" % self.port
        self.opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
        self.process_kwargs = process_kwargs or {}
        self.process = None
        self.start_lock = threading.Lock()
        self.token = uuid.uuid4().hex
        config_path = Path(settings["config"]).resolve()
        data = json.loads(config_path.read_text(encoding="utf-8-sig"))
        for key in ("repo", "model_dir"):
            data[key] = str((config_path.parent / data[key]).resolve())
        for reference in data["references"].values():
            reference["wav"] = str((config_path.parent / reference["wav"]).resolve())
        self.fingerprint = hashlib.sha256(json.dumps(data, sort_keys=True,
            ensure_ascii=False).encode("utf-8")).hexdigest()

    def health(self):
        try:
            with self.opener.open(self.url + "/health", timeout=2) as response:
                state = json.loads(response.read(16000).decode("utf-8"))
                if not isinstance(state, dict):
                    raise RuntimeError("自然语音端口返回了陌生服务的数据")
                return state
        except (OSError, ValueError):
            return None

    def ensure_ready(self):
        with self.start_lock:
            self._ensure_ready()

    def _ensure_ready(self):
        state = self.health()
        if state is not None and state.get("fingerprint") != self.fingerprint:
            raise RuntimeError("自然语音服务正在使用另一套参考配置，请重启语音服务")
        if state is None:
            for key in ("python", "server", "config"):
                if not os.path.isfile(self.settings[key]):
                    raise RuntimeError("自然语音文件缺失：" + self.settings[key])
            log_path = str(Path(self.settings["config"]).with_name("natural-tts.log"))
            environment = dict(os.environ)
            environment.pop("PYTHONHOME", None)
            environment.pop("PYTHONPATH", None)
            environment["PYTHONUTF8"] = "1"
            environment["TOKENIZERS_PARALLELISM"] = "false"
            environment["HF_HUB_OFFLINE"] = "1"
            environment["TRANSFORMERS_OFFLINE"] = "1"
            environment["MODELSCOPE_CACHE"] = os.path.join(os.path.dirname(self.settings["config"]), "modelscope-cache")
            with open(log_path, "ab") as log:
                self.process = subprocess.Popen([
                    self.settings["python"], "-X", "utf8", self.settings["server"],
                    "--config", self.settings["config"], "--port", str(self.port),
                    "--token", self.token, "--parent-pid", str(os.getpid())],
                    cwd=os.path.dirname(self.settings["server"]),
                    env=environment, stdout=log, stderr=subprocess.STDOUT,
                    stdin=subprocess.DEVNULL, **self.process_kwargs)
        deadline = time.monotonic() + 120
        while time.monotonic() < deadline:
            state = self.health()
            if state is not None:
                if state.get("fingerprint") != self.fingerprint:
                    raise RuntimeError("本地自然语音端口已被另一套配置占用")
                if state.get("ready"):
                    return
                if state.get("error"):
                    raise RuntimeError("自然语音加载失败：" + state["error"])
            if self.process is not None and self.process.poll() is not None:
                raise RuntimeError("自然语音进程已退出，请看 natural-tts.log")
            time.sleep(.3)
        raise RuntimeError("自然语音加载超时，请看 natural-tts.log")

    def synthesize(self, text, out_wav, style="chat", speed=0):
        self.ensure_ready()
        body = json.dumps({"text": text, "style": style or "chat", "speed": speed},
                          ensure_ascii=False).encode("utf-8")
        request = urllib.request.Request(self.url + "/synthesize", data=body,
                                         headers={"Content-Type": "application/json"})
        try:
            with self.opener.open(request, timeout=600) as response:
                if response.headers.get_content_type() != "audio/wav":
                    raise RuntimeError("自然语音服务没有返回 WAV 音频")
                audio = response.read(40 * 1024 * 1024 + 1)
        except urllib.error.HTTPError as exc:
            try:
                message = json.loads(exc.read(16000).decode("utf-8"))["error"]
            except (ValueError, KeyError):
                message = str(exc)
            raise RuntimeError("自然语音合成失败：" + message) from exc
        if len(audio) > 40 * 1024 * 1024:
            raise RuntimeError("自然语音音频过大")
        try:
            with wave.open(io.BytesIO(audio), "rb") as wav:
                if wav.getnchannels() != 1 or wav.getsampwidth() != 2 or wav.getnframes() == 0:
                    raise ValueError("Expected non-empty mono PCM16")
                if not 8000 <= wav.getframerate() <= 48000:
                    raise ValueError("Invalid sample rate")
                frames = wav.getnframes()
                if len(wav.readframes(frames)) != frames * 2:
                    raise ValueError("Truncated PCM audio")
        except (wave.Error, EOFError, ValueError) as exc:
            raise RuntimeError("自然语音返回了损坏音频：" + str(exc)) from exc
        with open(out_wav, "wb") as output:
            output.write(audio)
        return out_wav

    def close(self):
        if self.process is None:
            return
        try:
            if self.process.poll() is None:
                request = urllib.request.Request(self.url + "/shutdown",
                    data=json.dumps({"token": self.token}).encode("utf-8"),
                    headers={"Content-Type": "application/json"})
                try:
                    with self.opener.open(request, timeout=2) as response:
                        response.read(16000)
                except OSError:
                    pass
                try:
                    self.process.wait(timeout=3)
                except subprocess.TimeoutExpired:
                    self.process.terminate()
                    self.process.wait(timeout=3)
        finally:
            if self.process.poll() is not None:
                self.process = None
