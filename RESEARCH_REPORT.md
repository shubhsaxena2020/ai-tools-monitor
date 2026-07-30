# AI Tools Monitor — Comprehensive Research Report
> Date: 2026-07-30
> Verified from official docs, GitHub repos, and live inspection on this machine (C:\Users\shubh)

---

## PART 1: PER-TOOL USAGE/QUOTA DATA EXPOSURE

---

### 1. CLAUDE CODE (Anthropic)

**Official docs:** https://code.claude.com/docs/en/statusline
**CLI reference:** https://code.claude.com/docs/en/cli-reference

#### The Statusline Mechanism (CORRECTED — your current approach is wrong)

The statusline is NOT a file that Claude Code writes to. It is a **shell command mechanism**:
- Claude Code runs your configured command when a session begins
- It pipes a **JSON object via stdin** to your script
- Your script reads stdin, extracts fields, prints formatted text to stdout
- Claude Code displays stdout in the TUI

**There is NO "statusline capture file" on disk.** Claude Code does not write status data to any file via the statusline mechanism.

**Configuration** in `~/.claude/settings.json`:
```json
{
  "statusLine": {
    "type": "command",
    "command": "powershell -NoProfile -File C:/Users/shubh/.claude/statusline-capture.ps1",
    "refreshInterval": 5
  }
}
```

**Refresh triggers** (event-driven, NOT polling):
1. New assistant message arrives
2. `/compact` finishes
3. Permission mode changes
4. Vim mode toggles
5. `refreshInterval` timer elapses (if set)
- Debounced at 300ms; in-flight script is cancelled if new update arrives

#### ALL Available Data Fields (from stdin JSON)

| Field | Description |
|-------|-------------|
| `model.id`, `model.display_name` | Current model |
| `cost.total_cost_usd` | **Estimated session cost in USD** (resets on /clear) |
| `cost.total_duration_ms` | Total wall-clock time (ms) |
| `cost.total_api_duration_ms` | Total API wait time (ms) |
| `cost.total_lines_added/removed` | Lines of code changed |
| `context_window.total_input_tokens` | Input tokens in context |
| `context_window.total_output_tokens` | Output tokens in context |
| `context_window.context_window_size` | Max context window (200k or 1M) |
| `context_window.used_percentage` | **Pre-calculated % of context used** |
| `context_window.remaining_percentage` | **Pre-calculated % remaining** |
| `rate_limits.five_hour.used_percentage` | **5-hour rate limit usage %** (Pro/Max only) |
| `rate_limits.seven_day.used_percentage` | **7-day rate limit usage %** (Pro/Max only) |
| `rate_limits.five_hour.resets_at` | Unix epoch when 5h window resets |
| `rate_limits.seven_day.resets_at` | Unix epoch when 7d window resets |
| `session_id` | Unique session ID |
| `session_name` | Custom session name |
| `transcript_path` | Path to JSONL transcript file |
| `version` | Claude Code version |
| `fast_mode` | Whether fast mode is on |
| `effort.level` | Reasoning effort (low/medium/high/xhigh/max) |
| `thinking.enabled` | Extended thinking status |
| `pr.number`, `pr.url`, `pr.review_state` | Open PR info |

Source: https://code.claude.com/docs/en/statusline (Available data section)

#### More Direct APIs Beyond Statusline

**A. `claude -p "query" --output-format json` (Print Mode)**
Returns complete usage JSON after a task:
```json
{
  "type": "result",
  "total_cost_usd": 0.0787,
  "duration_ms": 10276,
  "usage": { "input_tokens": 5, "output_tokens": 603 },
  "modelUsage": { "claude-sonnet-4-6": { "costUSD": 0.078 } }
}
```

**B. `claude -p "query" --output-format stream-json` (Streaming)**
Newline-delimited JSON events in real-time during execution.

**C. `claude auth status` (JSON)**
Returns auth status and billing type.

**D. Session transcript files**
JSONL files at `~/.claude/projects/<escaped-cwd>/` contain per-message:
- `message.usage` with `input_tokens`, `output_tokens`, `cache_creation_input_tokens`, `cache_read_input_tokens`
- `message.model` (e.g., "claude-sonnet-5")
- `timestamp` (ISO 8601)
- Session ID, project path, git branch info
- **These persist for 30 days** then get deleted

#### Windows-Specific Considerations

1. On Windows, statusline commands run through **Git Bash** (if installed) or **PowerShell** (if Git Bash is absent)
2. Always use **forward slashes** in command paths — Git Bash treats backslashes as escapes
3. `~` expands to the Windows home directory
4. `tput cols` doesn't work inside statusline scripts — use `COLUMNS` env var (v2.1.153+)
5. `FORCE_HYPERLINK=1` may be needed for OSC 8 links on Windows Terminal

#### Recommended Approach for Your Tray App

**Option A (Real-time, best):** Write a small PowerShell script that:
1. Reads JSON from stdin
2. Writes extracted data to a known file (e.g., `C:\Users\shubh\.claude\statusline-data.json`)
3. Tray app polls this file

**Option B (Simple, post-hoc):** Read the JSONL transcript files directly from `~/.claude/projects/`. Contains full token/cost/model data per message. No statusline needed.

**Option C (Event-driven, advanced):** Named pipe or TCP socket — statusline script pipes data there, tray app reads for live updates.

---

### 2. HERMES AGENT (NousResearch)

**Official docs:** https://hermes-agent.nousresearch.com/docs
**CLI commands ref:** https://hermes-agent.nousresearch.com/docs/reference/cli-commands

#### Usage Data Exposure: YES — Multiple Rich Sources

**A. `state.db` — SQLite Database (BEST SOURCE)**
- Path: `C:\Users\shubh\AppData\Local\hermes\state.db` (9.3 MB on this machine)
- Table `session_model_usage`: Per-session, per-model token counts, API calls, cost
- Table `sessions`: Session-level aggregates (tokens, cost, tool calls, source, model)
- **This is the ideal data source for a tray app** — direct SQLite query for real-time usage

Schema (verified):
```sql
CREATE TABLE session_model_usage (
    session_id TEXT, model TEXT, billing_provider TEXT,
    api_call_count INTEGER, input_tokens INTEGER, output_tokens INTEGER,
    cache_read_tokens INTEGER, cache_write_tokens INTEGER,
    reasoning_tokens INTEGER, estimated_cost_usd REAL,
    first_seen REAL, last_seen REAL
);

CREATE TABLE sessions (
    id TEXT PRIMARY KEY, source TEXT, model TEXT,
    input_tokens INTEGER, output_tokens INTEGER,
    message_count INTEGER, tool_call_count INTEGER,
    estimated_cost_usd REAL, started_at REAL, ended_at REAL
);
```

**B. `hermes insights [--days N]` — CLI Analytics**
Outputs: token counts, cost, model breakdown, platform breakdown, tool usage, activity patterns, notable sessions. Text-formatted (not JSON), would need regex parsing.

Real output example:
```
Sessions: 39  |  Messages: 1,375
Tool calls: 763  |  Input tokens: 916,447  |  Output tokens: 166,179
Total tokens: 7,890,498
```

**C. `--usage-file PATH` — Per-Run JSON Usage Report**
With `--oneshot` mode, writes clean JSON:
```json
{
  "estimated_cost_usd": 0.0, "input_tokens": 0, "output_tokens": 23,
  "api_calls": 1, "model": "posiden/mimo-v2.5", "session_id": "..."
}
```
Only works with `--oneshot`, not ongoing sessions.

**D. `hermes status --all` — Component Health**
Shows: active model/provider, API key status, gateway status, session count. NOT usage data.

**E. Local Web Server**
- `hermes serve` / `hermes dashboard` on `localhost:9119`
- JSON-RPC/WebSocket (serve) or web UI (dashboard)
- Auth required for non-loopback binds
- Not primarily a usage API, but dashboard UI exists

**F. Log Files**
- Path: `C:\Users\shubh\AppData\Local\hermes\logs\`
- `agent.log`, `errors.log`, `desktop.log`, `mcp-stderr.log`
- NO structured token/cost data in logs — that's all in the SQLite DB

**G. Config File**
- Path: `C:\Users\shubh\.hermes\config.yaml`
- Contains providers, models, API keys. NO usage tracking.

#### Recommended Approach for Your Tray App

**Poll `state.db` directly with SQLite queries.** This is the richest, most structured data source. The tray app can run periodic queries like:
```sql
SELECT model, SUM(input_tokens), SUM(output_tokens), SUM(estimated_cost_usd)
FROM session_model_usage
WHERE first_seen > datetime('now', '-1 day')
GROUP BY model;
```

---

### 3. OPENCODE (sst/opencode → anomalyco/opencode)

**GitHub:** https://github.com/anomalyco/opencode (191k stars)
**Docs:** https://opencode.ai/docs/cli/ , https://opencode.ai/docs/server/

#### Usage Data Exposure: YES — Multiple Rich Sources

**A. `opencode stats` Command — Primary Usage Data**
Outputs formatted table with:
- Total sessions, messages, days
- Total cost (USD), cost per day
- Total tokens: input, output, reasoning, cache read, cache write
- Per-model breakdown (messages, tokens, cost)
- Per-tool usage counts
- Flags: `--days N`, `--tools N`, `--models N`, `--project ID`

Source: https://github.com/anomalyco/opencode/blob/dev/packages/opencode/src/cli/cmd/stats.ts

**B. SQLite Database — Raw Usage Data**
- Path: `%LOCALAPPDATA%/opencode/opencode.db` (Windows)
- `session` table has: `cost` (REAL), `tokens_input`, `tokens_output`, `tokens_reasoning`, `tokens_cache_read`, `tokens_cache_write`
- `message` table has per-message token data in JSON `data` column
- Can be queried directly with any SQLite client

**C. Local HTTP Server / API — Programmatic Access**
- `opencode serve` starts headless HTTP server on `127.0.0.1:4096`
- The TUI also starts a background server automatically
- Server registration file: `~/.local/state/opencode/server.json` — contains `{ url, pid, version, id }`
- OpenAPI spec: `http://localhost:4096/doc`
- Key endpoints:
  - `GET /api/session` — list sessions with token/cost data
  - `GET /api/session/:id/messages` — messages with per-message tokens
  - `GET /api/provider` — list providers
  - `GET /api/model` — list models
  - `GET /global/event` — SSE event stream (real-time!)
- Auth: HTTP Basic Auth (username `opencode`, password in `~/.local/state/opencode/password`)

**D. Additional CLI Commands**
- `opencode service status` — check if background server is running
- `opencode session list [--format json]` — list sessions
- `opencode api <operation>` — query running server
- `opencode export [sessionID]` — export session as JSON

**E. No Rate Limit/Quota Data**
OpenCode tracks what you consumed (tokens, cost), NOT what remains. No rate limit headers stored.

#### Recommended Approach for Your Tray App

**Two options, ranked:**
1. **Poll the local HTTP API** (`GET /api/session`) for real-time data when the server is running. Check `~/.local/state/opencode/server.json` for server URL.
2. **Read `opencode.db` directly** with SQLite — same approach as Hermes.
3. **Subscribe to SSE** (`GET /global/event`) for real-time events.

---

### 4. ANTIGRAVITY (agy CLI, Google, uses Gemini)

**GitHub:** https://github.com/google-antigravity/antigravity-cli (1.8k stars)
**Docs:** https://antigravity.google/docs/cli/overview
**Product page:** https://antigravity.google/product/antigravity-cli
**Blog:** https://developers.googleblog.com/an-important-update-transitioning-gemini-cli-to-antigravity-cli/

#### Tool Background
- Replaced Gemini CLI (shut down June 18, 2026)
- Closed-source Go binary (single executable)
- Install: `irm https://antigravity.google/cli/install.ps1 | iex` (Windows)

#### Usage Data Exposure: YES — via Local HTTPS Server

**A. Built-in `/usage` Slash Command (alias `/quota`)**
- Type `/usage` in the agy TUI → interactive panel showing model quotas
- Shows: remaining requests/tokens per model, refresh times
- This is an **interactive TUI panel**, not machine-readable

**B. Local HTTPS Loopback Server (KEY FINDING)**
The `agy` CLI runs an embedded HTTPS localhost server while alive.
- **Primary endpoint:** `POST /exa.language_server_pb.LanguageServerService/RetrieveUserQuotaSummary`
- **Fallback 1:** `POST /exa.language_server_pb.LanguageServerService/GetUserStatus`
- **Fallback 2:** `POST /exa.language_server_pb.LanguageServerService/GetCommandModelConfigs`
- **No CSRF token needed** for `agy` CLI (unlike the desktop app)
- Port discovery: scan listening ports on the `agy` process

Source: https://github.com/steipete/CodexBar/blob/main/docs/antigravity.md

**C. Quota System**
- Quota shared across Antigravity desktop app, CLI, and SDK
- Two windows: 5-hour session limit + weekly limit
- Two model groups: Gemini models + Claude/GPT models
- Subagents burn quota in parallel

**D. Settings File**
- Path: `~/.gemini/antigravity-cli/settings.json`
- Configuration only, not live usage data

**E. Existing Third-Party Tools (proof it works)**

| Tool | Type | Stars | How |
|------|------|-------|-----|
| [CodexBar](https://github.com/steipete/CodexBar) | macOS menu bar | 19.3k | Probes local HTTPS server for `RetrieveUserQuotaSummary` |
| [agy-hud](https://github.com/franksde/agy-hud) | agy plugin | 16 | Reads status-line JSON + quota cache |
| [antigravity-usage](https://github.com/skainguyen1412/antigravity-usage) | npm CLI | 366 | Dual-fetch: local loopback OR Google Cloud API |
| [AntigravityQuota](https://github.com/Henrik-3/AntigravityQuota) | VS Code ext | 237 | Detects process, scans ports, calls `GetUserStatus` |

#### Recommended Approach for Your Tray App

**Option A (Easiest):** Shell out to `antigravity-usage quota --json` and parse JSON output. Handles port discovery, auth automatically.

**Option B (More control):** Directly probe agy's local HTTPS server:
1. Find `agy` process → extract listening ports (use `netstat` or Windows API)
2. `POST` to `RetrieveUserQuotaSummary`
3. No CSRF token needed for CLI

**Windows consideration:** Port discovery uses `netstat` or `Get-NetTCPConnection` instead of macOS `lsof`. The `AntigravityQuota` VS Code extension supports Windows with `wmic`.

---

### 5. PROCESS-LEVEL ACTIVITY AS FALLBACK

**When to use:** For tools where quota/usage API is unavailable or insufficient.
**Source:** https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.process

#### What System.Diagnostics.Process Gives You

| Property | Useful For |
|----------|-----------|
| `StartTime` | Session duration calculation |
| `TotalProcessorTime` | CPU usage delta (already implemented) |
| `WorkingSet64` | RAM usage (already implemented) |
| `MainWindowTitle` | Sometimes shows activity state (often empty for console apps) |
| `HandleCount` | Correlates with activity level |
| `Threads` | Thread count (active workers) |

**CANNOT detect:** Network activity, "waiting for API response" state, quota data.

#### CPU-Based Active vs Idle: Partially Reliable

**Works:** Streaming responses (high CPU), file editing (high CPU), idle at prompt (zero CPU).
**Fails:** Network-bound waits (tool is "active" but CPU is ~0 while waiting for API response).

#### The Gold Standard: Transcript File Mtime (from CodexBar)

CodexBar (19.3k stars) does NOT use CPU for activity detection. It uses **file modification time**:

```swift
// From AgentSession.swift (CodexBar)
func state(lastActivityAt: Date?, now: Date, hasLiveProcess: Bool) -> AgentSession.State {
    guard let lastActivityAt else { return hasLiveProcess ? .active : .idle }
    return now.timeIntervalSince(lastActivityAt) <= self.activeWindow ? .active : .idle
}
```

`lastActivityAt` = most recent transcript file's `modifiedAt` in `~/.claude/projects/`. Active window = 120 seconds.

This is more reliable than CPU because:
1. Captures network-wait activity (tool writes to transcript during streaming)
2. Works regardless of internal processing model
3. Has direct semantic meaning (tool wrote output = active)

#### Actual Windows Process Names (Verified on This Machine)

| Tool | Executable | Process Name | Detection Notes |
|------|-----------|-------------|-----------------|
| Claude Code | `claude.exe` (native) | `claude` | Process name match works |
| Codex | `codex.js` via Node.js | `node` ⚠️ | Must match `codex.js`/`@openai/codex` in command line |
| Hermes | Python venv | `python` ⚠️ | Must match `hermes` in command line |
| OpenCode | `opencode.exe` (native) | `opencode` | Process name match works |
| Antigravity | `agy.exe` (native Go) | `agy` | Process name match works |

#### Recommended Dual-Signal Approach

1. **Primary:** Transcript/session file mtime (newest file modification time)
   - Claude Code: `~/.claude/projects/**/*.jsonl`
   - Codex: `~/.codex/sessions/`
   - Hermes: `state.db` modification time
   - OpenCode: `opencode.db` modification time
   - Antigravity: Check if agy process exists (no local files to scan)
2. **Fallback:** CPU usage delta (your current approach, 3% threshold)

---

## PART 2: FEATURE IDEAS (20 Verified, Ranked by Value)

All features are verified as implemented by real shipping tools.

---

### Category 1: Cost/Spend Tracking

**1. Per-Model Cost Breakdown with Auto-Pricing** [HIGH VALUE, MEDIUM EFFORT]
- Inspired by: CodeBurn (9k stars), TokenTracker (1.1k stars)
- Data: Parse JSONL/SQLite per-message token counts × model pricing from LiteLLM's `model_prices_and_context_window.json` (2200+ models)
- Difficulty: ~2-3 days. System.Text.Json parsing, local pricing dictionary, SQLite aggregation.

**2. Monthly/Weekly/Daily Spend Summaries** [HIGH VALUE, LOW EFFORT]
- Inspired by: CodeBurn (`codeburn overview`), WakaTime
- Data: Same session files, `GROUP BY date()` in SQLite
- Difficulty: ~1-2 days. SQL queries + DataGridView.

**3. Budget Guard with Hard/Soft Caps** [MEDIUM VALUE, MEDIUM EFFORT]
- Inspired by: CodeBurn Guard (`codeburn guard install`), OpenRouter Workspace Budgets
- Data: Accumulated cost in local DB vs. user-configured thresholds
- Difficulty: ~2-3 days. Config JSON + tray balloon notifications.

**4. Cost Forecasting from Trend Data** [MEDIUM VALUE, LOW EFFORT]
- Inspired by: CodeBurn (7-day trend prediction), ai-token-monitor
- Data: Historical daily cost, linear extrapolation
- Difficulty: ~1 day.

**5. Multi-Currency Support** [LOW VALUE, LOW EFFORT]
- Inspired by: CodeBurn (`codeburn currency GBP`, 162 currencies)
- Data: Frankfurter API (free, ECB rates, no API key)
- Difficulty: ~0.5 days. HTTP GET + cache.

### Category 2: Session History/Logging

**6. Session Timeline with Duration and Token Counts** [HIGH VALUE, LOW EFFORT]
- Inspired by: CodeBurn, claude-dashboard (536 stars), toktrack (179 stars)
- Data: JSONL timestamps, session boundaries
- Difficulty: ~1-2 days.

**7. Persistent History Cache (Survives 30-Day Deletion)** [HIGH VALUE, MEDIUM EFFORT]
- Inspired by: toktrack — explicitly built because "Claude Code deletes session data after 30 days"
- Data: Ingest JSONL files into local SQLite on each poll cycle
- Difficulty: ~2-3 days. File watcher + incremental ingestion.

**8. Activity Heatmap (GitHub-Style)** [MEDIUM VALUE, MEDIUM EFFORT]
- Inspired by: TokenTracker (GitHub-style heatmap widget), WakaTime
- Data: Session timestamps, daily token aggregation
- Difficulty: ~2 days. Custom WinForms GDI+ panel.

### Category 3: Alerts/Notifications

**9. Rate Limit Monitoring with Countdown** [HIGH VALUE, HARD]
- Inspired by: claude-dashboard (`rateLimit5h`, `rateLimit7d` widgets)
- Data: Claude Code statusline `rate_limits.five_hour` / `rate_limits.seven_day`
- Difficulty: ~3-4 days. Statusline integration + countdown timer.

**10. Budget Threshold Tray Notifications** [HIGH VALUE, LOW EFFORT]
- Inspired by: CodeBurn Guard, Usage4AI, ai-token-monitor (80% warning)
- Data: Accumulated cost vs. config thresholds
- Difficulty: ~1 day. Windows toast/balloon notification.

**11. Cost Anomaly Detection** [LOW VALUE, MEDIUM EFFORT]
- Inspired by: CodeBurn optimize (detects low-worth expensive sessions)
- Data: Per-session cost, Z-score outlier detection
- Difficulty: ~1-2 days.

### Category 4: Quick-Launch/Actions

**12. Recent Project Switcher** [HIGH VALUE, LOW EFFORT]
- Inspired by: PowerToys Run, Alfred/Raycast
- Data: Session `cwd` field from JSONL files
- Difficulty: ~1 day. MRU list in context menu + Process.Start to open terminal.

**13. Quick Command Palette** [LOW VALUE, LOW EFFORT]
- Inspired by: PowerToys Run, Raycast
- Data: Tool profiles from config
- Difficulty: ~1 day.

### Category 5: Aggregated Timeline

**14. Daily/Weekly Usage Summary by Tool** [HIGH VALUE, LOW EFFORT]
- Inspired by: CodeBurn (`codeburn overview`), WakaTime
- Data: Local SQLite DB aggregation
- Difficulty: ~1-2 days.

**15. Task Category Classification** [MEDIUM VALUE, MEDIUM EFFORT]
- Inspired by: CodeBurn (13 categories from tool usage patterns, no LLM calls)
- Data: Tool usage patterns in session files (Edit, Write, Bash, Grep)
- Difficulty: ~2-3 days.

**16. One-Shot Success Rate** [MEDIUM VALUE, MEDIUM EFFORT]
- Inspired by: CodeBurn ("Coding at 90% means AI got it right first try 9/10 times")
- Data: Edit-A → Bash → Edit-A again = retry pattern detection
- Difficulty: ~2 days.

### Category 6: Cross-Tool Comparison

**17. Per-Tool Cost Efficiency Comparison** [MEDIUM VALUE, MEDIUM EFFORT]
- Inspired by: CodeBurn (`codeburn compare`), Splitrail (`compare_tools`)
- Data: Unified metrics across 5 tool formats
- Difficulty: ~2-3 days.

**18. Model Cost-Effectiveness Rankings** [MEDIUM VALUE, LOW EFFORT]
- Inspired by: CodeBurn (`codeburn models`)
- Data: Per-model usage data, SQL aggregation + sorting
- Difficulty: ~1 day.

### Category 7: Other

**19. CSV/JSON Export** [HIGH VALUE, LOW EFFORT]
- Inspired by: CodeBurn (`codeburn export`), ClaudeUsageTracker
- Data: Local SQLite DB
- Difficulty: ~0.5 days.

**20. Setup Health Grade** [LOW VALUE, HARD]
- Inspired by: CodeBurn optimize (A-F grade, waste pattern detection)
- Data: Config files + session analysis
- Difficulty: ~4-5 days.

---

### Feature Priority Matrix

| Rank | Feature | Effort | Value |
|------|---------|--------|-------|
| 1 | Per-Model Cost Breakdown | Medium | ★★★★★ |
| 2 | Daily/Weekly/Monthly Summaries | Low | ★★★★★ |
| 3 | Persistent History Cache | Medium | ★★★★★ |
| 4 | Budget Threshold Notifications | Low | ★★★★☆ |
| 5 | Session Timeline | Low | ★★★★☆ |
| 6 | Recent Project Switcher | Low | ★★★★☆ |
| 7 | CSV/JSON Export | Low | ★★★★☆ |
| 8 | Aggregated Daily Summary | Low | ★★★★☆ |
| 9 | Cost Forecasting | Low | ★★★☆☆ |
| 10 | Model Cost-Effectiveness | Low | ★★★☆☆ |
| 11 | Activity Heatmap | Medium | ★★★☆☆ |
| 12 | Cross-Tool Comparison | Medium | ★★★☆☆ |
| 13 | Task Classification | Medium | ★★★☆☆ |
| 14 | One-Shot Success Rate | Medium | ★★★☆☆ |
| 15 | Rate Limit Monitoring | Hard | ★★★☆☆ |
| 16 | Budget Guard | Medium | ★★☆☆☆ |
| 17 | Multi-Currency | Low | ★★☆☆☆ |
| 18 | Cost Anomaly Detection | Medium | ★★☆☆☆ |
| 19 | Quick Command Palette | Low | ★★☆☆☆ |
| 20 | Setup Health Grade | Hard | ★★☆☆☆ |

---

## PART 3: EXISTING TOOLS YOU SHOULD STUDY

| Tool | Stars | What It Does | Relevance |
|------|-------|-------------|-----------|
| [CodexBar](https://github.com/steipete/CodexBar) | 19.3k | macOS menu bar AI monitor | Direct competitor/reference. Monitors Codex + Claude Code + 66 providers. |
| [CodeBurn](https://github.com/getagentseal/codeburn) | 9k | Claude Code cost tracker | Per-model costs, task classification, budget guard, optimize, compare |
| [TokenTracker](https://github.com/xiufengsun/TokenTracker) | 1.1k | Multi-tool token tracker | Dashboard, heatmap, daily/weekly views |
| [claude-dashboard](https://github.com/uppinote20/claude-dashboard) | 536 | Claude Code dashboard | Session timeline, rate limits, burn rate |
| [antigravity-usage](https://github.com/skainguyen1412/antigravity-usage) | 366 | Antigravity quota CLI | Machine-readable quota output, port discovery |
| [toktrack](https://github.com/mag123c/toktrack) | 179 | Session cache | Solves 30-day deletion problem |
| [Splitrail](https://github.com/Piebald-AI/splitrail) | 215 | Cross-tool comparison | MCP server for comparing AI coding tools |

---

## PART 4: SUMMARY — DATA SOURCE MATRIX

| Tool | Primary Data Source | Format | Path (Windows) | Has Cost? | Has Rate Limits? |
|------|-------------------|--------|----------------|-----------|-----------------|
| Claude Code | Statusline stdin JSON | JSON (pipe) | N/A (stdin) | Yes (session) | Yes (5h/7d) |
| Claude Code | Session JSONL files | JSONL | `~/.claude/projects/**/*.jsonl` | Yes (tokens, calc cost) | No |
| Hermes Agent | `state.db` SQLite | SQLite | `%LOCALAPPDATA%\hermes\state.db` | Yes (estimated) | No |
| OpenCode | HTTP API | JSON | `localhost:4096/api/session` | Yes (cost field) | No |
| OpenCode | `opencode.db` SQLite | SQLite | `%LOCALAPPDATA%\opencode\opencode.db` | Yes (cost field) | No |
| Antigravity | Local HTTPS server | Protobuf/JSON | Loopback (port varies) | No (quota only) | Yes (quotas) |
| Antigravity | `antigravity-usage` | JSON | CLI output | No (quota only) | Yes |

---

## Sources

- Claude Code statusline: https://code.claude.com/docs/en/statusline
- Claude Code CLI reference: https://code.claude.com/docs/en/cli-reference
- Hermes Agent docs: https://hermes-agent.nousresearch.com/docs
- Hermes CLI commands: https://hermes-agent.nousresearch.com/docs/reference/cli-commands
- OpenCode GitHub: https://github.com/anomalyco/opencode
- OpenCode CLI docs: https://opencode.ai/docs/cli/
- OpenCode server docs: https://opencode.ai/docs/server/
- Antigravity CLI GitHub: https://github.com/google-antigravity/antigravity-cli
- Antigravity docs: https://antigravity.google/docs/cli/overview
- CodexBar docs: https://github.com/steipete/CodexBar/blob/main/docs/antigravity.md
- CodexBar source: https://github.com/steipete/CodexBar/blob/main/Sources/CodexBarCore/AgentSession.swift
- agy-hud: https://github.com/franksde/agy-hud
- antigravity-usage: https://github.com/skainguyen1412/antigravity-usage
- AntigravityQuota: https://github.com/Henrik-3/AntigravityQuota
- CodeBurn: https://github.com/getagentseal/codeburn
- TokenTracker: https://github.com/xiufengsun/TokenTracker
- claude-dashboard: https://github.com/uppinote20/claude-dashboard
- toktrack: https://github.com/mag123c/toktrack
- Splitrail: https://github.com/Piebald-AI/splitrail
- .NET Process class: https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.process
- LiteLLM pricing: https://github.com/BerriAI/litellm/blob/main/model_prices_and_context_window.json
- Frankfurter API: https://frankfurter.app
- WakaTime: https://wakatime.com/features
