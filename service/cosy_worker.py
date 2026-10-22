"""Public reference-conditioned TTS worker; isolated per voice fingerprint."""
import argparse
import hashlib
import io
import json
import math
import os
from pathlib import Path
import sys
import threading
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer


def read_config(path):
    path = Path(path).resolve()
    data = json.loads(path.read_text(encoding="utf-8-sig"))
    for key in ("repo", "model_dir"):
        data[key] = str((path.parent / data[key]).resolve())
    for reference in data["references"].values():
        reference["wav"] = str((path.parent / reference["wav"]).resolve())
        if not Path(reference["wav"]).is_file():
            raise ValueError("Reference audio missing: " + reference["wav"])
    fingerprint = hashlib.sha256(json.dumps(data, sort_keys=True,
        ensure_ascii=False).encode("utf-8")).hexdigest()
    return data, fingerprint


def speech_chunks(text, limit=90):
    """Bound attention memory while retaining every character of the body."""
    pieces = []
    while len(text) > limit:
        candidates = [text.rfind(mark, 0, limit) for mark in "。！？；，、,.!?;\n"]
        end = max(candidates) + 1
        if end < limit // 3:
            end = limit
        pieces.append(text[:end])
        text = text[end:]
    if text:
        pieces.append(text)
    return pieces


class Synthesizer:
    def __init__(self, config):
        sys.path.insert(0, config["repo"])
        sys.path.insert(0, str(Path(config["repo"]) / "third_party" / "Matcha-TTS"))
        import torch
        from cosyvoice.cli.cosyvoice import AutoModel
        self.torch = torch
        torch.set_num_threads(4)
        self.config = config
        # Load checkpoints on CPU and convert before uploading. The original
        # loader briefly held fp32 checkpoints and fp32 models together on GPU,
        # exceeding the local GPU budget. Final weights use the same half precision.
        from cosyvoice.cli.model import CosyVoiceModel
        from unittest.mock import patch
        def load_bounded(model, llm_path, flow_path, hift_path):
            for name, path in (('llm', llm_path), ('flow', flow_path), ('hift', hift_path)):
                print('Loading CosyVoice ' + name, flush=True)
                weights = torch.load(path, map_location='cpu', weights_only=True)
                if name == 'hift':
                    weights = {key.replace('generator.', ''): value for key, value in weights.items()}
                module = getattr(model, name)
                module.load_state_dict(weights, strict=True)
                del weights
                if name != 'hift' and config.get('half_weights') and torch.cuda.is_available():
                    module.half()
                module.to(model.device).eval()
                print('Loaded CosyVoice ' + name, flush=True)
        with patch.object(CosyVoiceModel, 'load', load_bounded):
            self.model = AutoModel(model_dir=config["model_dir"], fp16=True,
                                  load_vllm=False, load_trt=False)
        if config.get("half_weights") and torch.cuda.is_available():
            self.model.model.llm.half()
            self.model.model.flow.half()
            torch.cuda.empty_cache()
        self.device = "cuda" if torch.cuda.is_available() else "cpu"
        self.sample_rate = int(self.model.sample_rate)
        self.prompt_cache = set()
        # Prepare reference conditioning before reporting ready. This shifts the
        # one-time speaker embedding cost into service warm-up instead of the
        # user's first spoken reply.
        for style, profile in self.config["profiles"].items():
            reference = self.config["references"][profile["reference"]]
            if profile.get("mode", "zero_shot") == "instruct2":
                prompt = "You are a helpful assistant. " + profile["instruction"] + "<|endofprompt|>"
            else:
                prompt = "You are a helpful assistant.<|endofprompt|>" + reference["text"]
            self.model.add_zero_shot_spk(prompt, reference["wav"], style)
            self.prompt_cache.add(style)

    def synthesize(self, text, style, speed, seed=1986):
        import soundfile as sf
        torch = self.torch
        torch.manual_seed(seed)
        profile = self.config["profiles"][style]
        reference = self.config["references"][profile["reference"]]
        instruct = profile.get("mode", "zero_shot") == "instruct2"
        if instruct:
            prompt = "You are a helpful assistant. " + profile["instruction"] + "<|endofprompt|>"
        else:
            prompt = "You are a helpful assistant.<|endofprompt|>" + reference["text"]
        started = time.monotonic()
        with torch.inference_mode():
            if style not in self.prompt_cache:
                self.model.add_zero_shot_spk(prompt, reference["wav"], style)
                self.prompt_cache.add(style)
            chunks = []
            for part in speech_chunks(text):
                method = self.model.inference_instruct2 if instruct else self.model.inference_zero_shot
                generator = method(part, prompt, reference["wav"], zero_shot_spk_id=style,
                                   stream=False, speed=1.0 + speed / 100.0, text_frontend=False)
                chunks.extend(item["tts_speech"].detach().float().cpu() for item in generator)
        if not chunks:
            raise RuntimeError("The model produced no audio")
        audio = torch.cat(chunks, dim=1).squeeze(0)
        if audio.ndim != 1 or not torch.isfinite(audio).all():
            raise RuntimeError("The model produced invalid audio samples")
        # Only cap excess initial low-energy padding; preserve consonant onset,
        # all internal pauses, and the original sample rate and speaking tempo.
        leading_trim = 0.0
        limit = profile.get("leading_silence_limit_sec")
        if limit is not None:
            window = max(1, int(self.sample_rate * .01))
            if audio.numel() >= window:
                levels = audio.unfold(0, window, window).square().mean(dim=1).sqrt()
                active = torch.nonzero(levels > max(.003, float(levels.max()) * .02)).flatten()
                if active.numel():
                    keep = max(.2, min(1.0, float(limit)))
                    cut = max(0, int(active[0]) * window - int(keep * self.sample_rate))
                    if cut > self.sample_rate * .1:
                        audio = audio[cut:]
                        leading_trim = cut / self.sample_rate
        duration = audio.numel() / self.sample_rate
        from audio_limits import MAX_AUDIO_SECONDS
        if duration < .08 or duration > MAX_AUDIO_SECONDS:
            raise RuntimeError("The model produced an invalid audio duration")
        peak = float(audio.abs().max())
        if peak < .0001:
            raise RuntimeError("The model produced silent audio")
        if peak >= 1.0:
            audio = audio * (.98 / peak)
        output = io.BytesIO()
        sf.write(output, audio.numpy(), self.sample_rate, format="WAV", subtype="PCM_16")
        if torch.cuda.is_available():
            torch.cuda.empty_cache()
        return output.getvalue(), {"synthesis_ms": int((time.monotonic()-started)*1000),
                                  "duration_sec": duration, "peak_before": peak,
                                  "leading_trim_sec": leading_trim}


class LocalServer(ThreadingHTTPServer):
    daemon_threads = True
    def __init__(self, address, config, fingerprint, token):
        super().__init__(address, Handler)
        self.config = config
        self.fingerprint = fingerprint
        self.token = token
        self.actor = None
        self.error = None
        self.lock = threading.Lock()

    def load(self):
        try:
            self.actor = Synthesizer(self.config)
            print("Natural TTS ready", flush=True)
        except Exception as exc:
            self.error = str(exc)
            import traceback
            traceback.print_exc()


def watch_parent(server, parent_pid):
    """An owned helper exits when the manually started parent service exits."""
    if os.name == "nt":
        import ctypes
        kernel = ctypes.WinDLL("kernel32", use_last_error=True)
        kernel.OpenProcess.argtypes = [ctypes.c_ulong, ctypes.c_int, ctypes.c_ulong]
        kernel.OpenProcess.restype = ctypes.c_void_p
        kernel.WaitForSingleObject.argtypes = [ctypes.c_void_p, ctypes.c_ulong]
        kernel.WaitForSingleObject.restype = ctypes.c_ulong
        kernel.CloseHandle.argtypes = [ctypes.c_void_p]
        handle = kernel.OpenProcess(0x00100000, False, parent_pid)
        if not handle:
            server.shutdown();return
        try:
            while kernel.WaitForSingleObject(handle, 1000) == 258:
                pass
        finally:
            kernel.CloseHandle(handle)
    else:
        while os.getppid() == parent_pid:
            time.sleep(1)
    server.shutdown()


class Handler(BaseHTTPRequestHandler):
    def reply(self, status, body, mime="application/json; charset=utf-8"):
        if not isinstance(body, bytes):
            body = json.dumps(body, ensure_ascii=False).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", mime)
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        try:
            self.wfile.write(body)
        except (BrokenPipeError, ConnectionResetError):
            pass

    def do_GET(self):
        if self.path != "/health":
            return self.reply(404, {"ok": False, "error": "Unknown endpoint"})
        self.reply(200, {"ready": self.server.actor is not None,
                        "pid": os.getpid(), "fingerprint": self.server.fingerprint,
                        "device": getattr(self.server.actor, "device", None),
                        "gpu":self.server.actor.torch.cuda.get_device_name(0) if self.server.actor and self.server.actor.torch.cuda.is_available() else None,
                        "sample_rate": getattr(self.server.actor, "sample_rate", None),
                        "error": self.server.error})

    def do_POST(self):
        if self.path not in ("/synthesize", "/shutdown"):
            return self.reply(404, {"ok": False, "error": "Unknown endpoint"})
        try:
            size = int(self.headers.get("Content-Length", "0"))
            if not 0 < size <= 16000:
                raise ValueError("Invalid request size")
            self.connection.settimeout(15)
            data = json.loads(self.rfile.read(size).decode("utf-8"))
            if not isinstance(data, dict):
                raise ValueError("Expected a JSON object")
            if self.path == "/shutdown":
                if data.get("token") != self.server.token or not self.server.token:
                    return self.reply(403, {"ok": False, "error": "Wrong owner token"})
                self.reply(200, {"ok": True})
                threading.Thread(target=self.server.shutdown, daemon=True).start()
                return
            text = data.get("text")
            if not isinstance(text, str) or not text.strip() or len(text) > 4000:
                raise ValueError("Expected non-empty speech text (up to 4000 characters)")
            style = data.get("style")
            if style is None or style == "":
                style = "chat"
            if not isinstance(style, str) or style not in self.server.config["profiles"]:
                raise ValueError("Unknown speech style")
            speed = float(data.get("speed", 0))
            if not math.isfinite(speed) or not -30 <= speed <= 30:
                raise ValueError("Speech speed must be between -30 and 30")
            seed=data.get('seed',1986)
            if not isinstance(seed,int) or isinstance(seed,bool) or not 0<=seed<=2**31-1:
                raise ValueError('Invalid seed')
        except (ValueError, TypeError, UnicodeError) as exc:
            return self.reply(400, {"ok": False, "error": str(exc)})
        if self.server.actor is None:
            return self.reply(503, {"ok": False, "error": self.server.error or "Model is loading"})
        if not self.server.lock.acquire(timeout=1):
            return self.reply(409, {"ok": False, "error": "Natural TTS is busy"})
        try:
            wav, metadata = self.server.actor.synthesize(text, style, speed, seed)
            print(json.dumps({"text_chars": len(text), "style": style, **metadata}, ensure_ascii=False), flush=True)
            self.reply(200, wav, "audio/wav")
        except Exception as exc:
            self.reply(500, {"ok": False, "error": str(exc)})
            import traceback
            traceback.print_exc()
        finally:
            self.server.lock.release()

    def log_message(self, format, *args):
        pass


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--config", required=True)
    parser.add_argument("--port", type=int, default=8809)
    parser.add_argument("--token", default="")
    parser.add_argument("--parent-pid", type=int, default=0)
    args = parser.parse_args()
    config, fingerprint = read_config(args.config)
    server = LocalServer(("127.0.0.1", args.port), config, fingerprint, args.token)
    threading.Thread(target=server.load, daemon=True).start()
    if args.parent_pid:
        threading.Thread(target=watch_parent, args=(server, args.parent_pid), daemon=True).start()
    try:
        server.serve_forever()
    finally:
        server.server_close()


if __name__ == "__main__":
    main()
