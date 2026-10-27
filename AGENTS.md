# Agent Voice maintenance

Maintain one public codebase on main. Package only the application; engines, runtimes, models, recordings, optional modules and user settings remain separate.

- First-run setup is optional. Keep usage notices, TTS licensing and scope information available under settings without acknowledgement gates.
- Preserve API authentication, path and archive validation, model compatibility checks, cancellation and recovery.
- Preserve existing audio provenance. Do not silently rewrite user settings or source recordings.
- Use the tested release builder and verify the actual packaged bytes, including normalized manuals.
- Never commit generated audio, user credentials, local audit files or machine-specific migration records.
- Run Python, Node and relevant native checks before publishing.

- Exclude the voice archive transfer extension from this public source tree. Keep generic feature modules and core saved-voice settings independent of it.
