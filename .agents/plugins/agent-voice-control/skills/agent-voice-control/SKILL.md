---
name: agent-voice-control
description: Control Agent Voice and its optional independent singing workbench with language commands: read text, stop speech, manage the voice service, switch saved voices, change automatic reading, or generate songs.
---

# Agent Voice Control

Use the `agent_voice` MCP tools for the requested voice operation. Report the returned state in concise Chinese.

- Read status or list profiles directly.
- When the user explicitly asks to read text, call `read_text` with that exact text. The service can start as needed. Never substitute URLs, code, tool logs, or an unrelated reply.
- A queued reading request is not completed playback. Report its returned state accurately.
- Codex uses language commands through MCP. Do not open a custom reading panel, floating window, or promise a permanent composer button; this integration does not implement those interfaces.
- The client's built-in Read Aloud has no verified local TTS integration. Do not claim to replace or intercept it. For reading, stopping, service control, voices and automatic reading, use the MCP tools directly.
- For start, stop, speech interruption, profile switching, or automatic-reading changes, perform only the operation the user requested.
- If a requested profile is named rather than identified, call `list_voice_profiles`, match the exact or closest unambiguous name, then pass its ID to `apply_voice_profile`.
- If the service must be running for an operation, call `start_voice_service` first only when the user asked for that operation and starting the service is required to complete it.
- Surface MCP error text as an actionable desktop-app instruction; do not edit Agent Voice files or settings directly.
- Singing is a separate application and engine family. Use `open_singing_app` to show it, `list_singing_engines` before selecting an engine, and `generate_song` only when the user explicitly supplies or requests lyrics. A returned job ID means queued, not completed; use `singing_job_status` to check it.

## Common commands

- 查看语音状态 → `voice_service_status`
- 开启语音服务／关闭语音服务 → `start_voice_service` / `stop_voice_service`
- 停止朗读 → `stop_speech` (also clears the waiting queue)
- 列出声音方案 → `list_voice_profiles`
- 切换到某声音 → resolve the name, then `apply_voice_profile`
- 开启／关闭自动朗读 → `set_auto_reading`
- 朗读这段文字 → `read_text`
- 打开唱歌软件 → `open_singing_app`
- 查看／切换唱歌引擎 → `list_singing_engines` / `select_singing_engine`
- 按歌词生成歌曲 → `generate_song`, then `singing_job_status`
