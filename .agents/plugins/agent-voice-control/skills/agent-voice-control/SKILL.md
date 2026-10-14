---
name: agent-voice-control
description: Control the locally installed Agent Voice service when the user asks to start or stop voice service, stop speech, inspect or switch saved voices, or change automatic reading.
---

# Agent Voice Control

Use the `agent_voice` MCP tools for the requested voice operation. Report the returned state in concise Chinese.

- Read status or list profiles directly.
- For start, stop, speech interruption, profile switching, or automatic-reading changes, perform only the operation the user requested.
- If a requested profile is named rather than identified, call `list_voice_profiles`, match the exact or closest unambiguous name, then pass its ID to `apply_voice_profile`.
- If the service must be running for an operation, call `start_voice_service` first only when the user asked for that operation and starting the service is required to complete it.
- Surface MCP error text as an actionable desktop-app instruction; do not edit Agent Voice files or settings directly.
