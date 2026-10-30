"""Optional Agent Voice provider for dsh-pet-indesktop's source build.

This file is an integration for the upstream provider seam, not a copy of the
pet application. It deliberately does not cache: Agent Voice's current voice
can change independently of the pet's configuration.
"""
from __future__ import annotations

import json
import urllib.error
import urllib.request
from pathlib import Path

from .base import TtsField, TtsProvider, TtsUnavailable, register


class AgentVoiceProvider(TtsProvider):
    id = "agentvoice"
    label = "Agent Voice（本机当前声音）"
    is_fallback = False
    cacheable = False
    availability_code = "agent-voice-unavailable"
    unavailable_message = "请启动 Agent Voice，并在桌宠语音设置中选择其程序目录。"
    fields = (
        TtsField("root", "voice_chime_agent_voice_root", "Agent Voice 目录",
                 "选择 agent语音.exe 所在目录；密钥由本机设置读取，不填入桌宠。",
                 kind="text", default="", max_length=500),
        TtsField("profile", "voice_chime_agent_voice_profile", "声音方案 ID",
                 "current 跟随 Agent Voice 当前声音；也可填写已保存的方案 ID。",
                 kind="text", default="current", max_length=100),
        TtsField("speed", "voice_chime_agent_voice_speed", "语速",
                 "100 为原速。", kind="number", default=100,
                 minimum=70, maximum=130, suffix=" %"),
    )

    def plan(self, values: dict) -> dict:
        return {"provider": self.id, "root": str(values.get("root") or "").strip(),
                "profile": str(values.get("profile") or "current").strip(),
                "speed": max(70, min(130, int(values.get("speed") or 100))),
                "ext": "mp3"}

    def flavor(self, values: dict) -> str:
        return f"agentvoice|{values.get('root','')}|{values.get('profile','current')}|{values.get('speed',100)}"

    def availability(self, values: dict) -> str:
        return "" if str(values.get("root") or "").strip() else self.availability_code

    def synth(self, text: str, plan: dict, out_path) -> None:
        root = Path(plan.get("root") or "")
        settings = root / "data" / "settings.json"
        try:
            config = json.loads(settings.read_text(encoding="utf-8-sig"))
            port = int(config["port"])
            token = config["token"]
            if not 1 <= port <= 65535 or not isinstance(token, str) or not token:
                raise ValueError("invalid local service configuration")
            if config.get("voice", {}).get("engine") == "dsh-pet":
                raise TtsUnavailable(self.availability_code,
                                     "Agent Voice 当前使用桌宠声音；请先切到本地 TTS，避免循环调用。")
        except TtsUnavailable:
            raise
        except (OSError, ValueError, TypeError, KeyError):
            raise TtsUnavailable(self.availability_code,
                                 "Agent Voice 目录或本机设置无效，请重新选择程序目录。") from None

        payload = json.dumps({"model": "agent-voice-tts", "input": text,
                              "voice": plan.get("profile") or "current",
                              "speed": plan.get("speed", 100) / 100,
                              "response_format": "mp3"}, ensure_ascii=False).encode("utf-8")
        request = urllib.request.Request(f"http://127.0.0.1:{port}/v1/audio/speech",
                                         data=payload, headers={"Content-Type": "application/json",
                                                                "Authorization": "Bearer " + token,
                                                                "X-Agent-Voice-Pet": "standalone",
                                                                "Accept": "audio/mpeg"}, method="POST")
        try:
            # An explicit empty proxy map keeps the authenticated loopback call local.
            # Local models can take substantially longer on a cold start than
            # the pet's built-in online providers.  A short socket timeout
            # aborts a healthy generation before Agent Voice returns audio.
            with urllib.request.build_opener(urllib.request.ProxyHandler({})).open(request, timeout=120) as response:
                if response.status != 200 or "audio/mpeg" not in response.headers.get("Content-Type", ""):
                    raise ValueError("unexpected audio response")
                audio = response.read(20 * 1024 * 1024 + 1)
        except urllib.error.HTTPError as exc:
            raise TtsUnavailable(self.availability_code,
                                 f"Agent Voice 合成失败（HTTP {exc.code}）；请检查声音方案和服务状态。") from None
        except (urllib.error.URLError, TimeoutError, OSError):
            raise TtsUnavailable(self.availability_code,
                                 "Agent Voice 服务未响应或合成超时，请先在 Agent Voice 中试听。") from None
        if not 0 < len(audio) <= 20 * 1024 * 1024 or not audio.startswith((b"ID3", b"\xff\xfb", b"\xff\xf3", b"\xff\xf2")):
            raise TtsUnavailable(self.availability_code, "Agent Voice 未返回有效 MP3 音频。")
        target = Path(out_path)
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(audio)


PROVIDER = register(AgentVoiceProvider())
