# Embedded Setup Implementation Plan

> **For agentic workers:** Use executing-plans to implement this plan task-by-task. User has authorized implementation; continue without another approval gate.

**Goal:** Ship a real, offline-capable embedded web configuration flow with the existing desktop app.

**Architecture:** A local WebView2 frontend calls a narrow C# setup controller. The controller reuses SetupWorkflow and ConnectionSetup. A native fallback remains available when the UI runtime is missing.

**Tech Stack:** .NET Framework / WinForms, Microsoft.Web.WebView2 1.0.3537.50, local HTML/CSS/JS, Node and native regression runners.

**Spec:** docs/配置渲染路线评估-20261020.md

## Global Constraints

- Ten independent setup pages; retain skip, defer, resume, sound and reduced motion.
- Never treat sample progress as real installation or playback evidence.
- No private voices, secrets, downloaded binaries or test media in Git.
- Preserve the formal installation; update only the WIP after validation.
- Local Git and GitHub integration branch remain the handoff record.

## Review Focus

- Missing UI runtime and strict offline mode must retain a usable native exit/fallback.
- Malformed or remote web messages cannot invoke arbitrary paths or commands.
- Repeated clicks and window close during installation must not run duplicate work or abandon a write.
- Engine selection only applies on Next; skip must not apply an unconfirmed choice.
- Moved software packages must include frontend, SDK loader and dependency diagnostics, without user data.

### Task 1: Build and package UI dependencies

**Files:** tools/restore_webview2.ps1, build.ps1, tools/module_packages.py, tests/test_embedded_setup_package.py, .gitignore.
**Interfaces:** pinned SDK restore produces managed Core/WinForms assemblies and x64 WebView2Loader; software package explicitly includes these and assets/setup.

- [ ] Write packaging tests for required UI files and absent private data; run RED.
- [ ] Restore verified NuGet package into ignored .build-deps; update build and software/environment package inclusion.
- [ ] Run packaging tests GREEN, build offline from cache, commit.

### Task 2: Real controller and native WebView host

**Files:** ui/EmbeddedSetupController.cs, ui/EmbeddedSetupForm.cs, ui/SetupEntry.cs, ui/WebRuntime.cs, ui/Program.cs, ui/SetupPage.cs, ui/EmbeddedSetupChecks.cs.
**Interfaces:** controller returns sanitized setup state and dispatches a fixed action set; WebView messages use {id,action,args}. Progress and result messages carry the same request id. Native file dialogs own all browsing. Completion/defer return DialogResult.OK.

- [ ] Add native controller regressions for engine Next/skip, reference validation, unknown commands, preferences and completion; run RED.
- [ ] Implement fixed command dispatch, real operations, missing-runtime panel and entry routing.
- [ ] Run controller and real WebView loading regressions, commit.

### Task 3: Local frontend and integrated acceptance

**Files:** assets/setup/index.html, setup.css, setup.js, bridge.js; tests/test_setup_frontend.mjs; docs and manuals.
**Interfaces:** frontend consumes sanitized state, request result and progress events; only current-page motion lives. Native runtime diagnostics are available without loading the frontend.

- [ ] Write frontend contract tests for lifecycle, safe rendering and operation state; run RED.
- [ ] Implement ten pages, independent artwork, title-first motion, real operation feedback, paths/voice drawers and inline failures.
- [ ] Validate real loading, resize/maximize/restore, all ten pages, native fallback, full UI/welcome and packaging tests. Keep original wizard checks as fallback coverage.
- [ ] Update WIP with binary, SDK files and frontend; back up prior files. Document limits, commit and push.

## Execution record

Windows environment: skill Bash ledger helpers are replaced by this checklist and evidence under acceptance/. Existing integration worktree is reused. Native implementation proceeds in this chat; independent review follows integration.
