# AI Tools Monitor

A Windows 11 system tray app that shows live status and usage for local AI CLI tools: **Claude Code**, **Codex CLI**, **Hermes Agent**, **OpenCode**, and **Antigravity**.

Built for developers who run several AI agent CLIs at once across terminals and editors, and want to answer "which agents are running, and how much are they using" without opening Task Manager.

## Features

- Native WinForms tray icon with a glassmorphism popup (left-click to open, right-click for the command menu)
- Per-tool live status: idle/active/quiet, process detection, and quota/usage data where a real local source exists
- **Claude Code** — reads its own JSONL session transcripts (`~/.claude/projects/**/*.jsonl`) for real token usage
- **Codex CLI** — reads its local JSON-RPC quota endpoint for 5-hour/weekly rate limits
- **Hermes Agent** — reads `state.db` (SQLite) directly for per-session token/cost usage
- **OpenCode** — reads `opencode.db` (SQLite) directly for per-session token/cost usage
- **Antigravity** — process-based idle/active status (no verified unauthenticated local API exists as of this build; a `401 unauthenticated` was found on its loopback endpoint and not worked around)

Every data source falls back gracefully to `Unavailable`/`--` rather than crashing or showing fake data when its source file/process isn't present.

## Requirements

- Windows 11
- .NET 9 SDK

## Build & run

```
dotnet build -c Release
dotnet run --project src/AiToolsMonitor -c Release
```

## Test

```
dotnet test -c Release
```

## Project docs

See `PROJECT_BRIEF.md`, `PRD.md`, `ARCHITECTURE.md`, `SYSTEM_DESIGN.md`, `TECH_STACK.md`, `TASKS.md`, `TESTING_PLAN.md`, `THEME.md`, and `UI_UX_SPEC.md` for the full design/decision record this project was built from. `RESEARCH_REPORT.md`, `RESEARCH_PROCESS_MONITORING.md`, and `RESEARCH_FEATURES.md` contain the underlying research (quota API feasibility per tool, process-detection patterns, and a longer list of candidate future features).

## Non-goals (v1)

No cloud service, no telemetry, no account system, no process control (observe only), no cross-platform support.
