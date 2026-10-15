---
name: agent-voice-control
description: Open the Agent Voice reading panel, read requested text, stop speech, start or stop voice service, inspect or switch saved voices, or change automatic reading.
---

# Agent Voice Control

Use the `agent_voice` MCP tools for the requested voice operation. Report the returned state in concise Chinese.

- Read status or list profiles directly.
- When the user asks for the reading panel, call `open_voice_player`. Pass only the requested text, or leave it empty for the user to fill in. Opening the panel does not start speech.
- When the user explicitly asks to read text, call `read_text` with that exact text. The service can start as needed. Never substitute URLs, code, tool logs, or an unrelated reply.
- The panel renders only in hosts that support MCP Apps. A successful tool response, `clientUi.declaration`, or plugin installation does not prove a visible working UI. If only text appears, report that limitation and use the native skill plus MCP controls.
- Do not claim to replace, intercept, or configure the client's built-in Read Aloud button. No built-in player integration has been verified.
- For start, stop, speech interruption, profile switching, or automatic-reading changes, perform only the operation the user requested.
- If a requested profile is named rather than identified, call `list_voice_profiles`, match the exact or closest unambiguous name, then pass its ID to `apply_voice_profile`.
- If the service must be running for an operation, call `start_voice_service` first only when the user asked for that operation and starting the service is required to complete it.
- Surface MCP error text as an actionable desktop-app instruction; do not edit Agent Voice files or settings directly.
