# AI Tools Monitor — Process-Level Activity Monitoring Research

> Research date: 2026-07-30
> All findings verified from real source code, documentation, and this machine.

---

## 1. System.Diagnostics.Process — Available Properties for Monitoring

Source: [Microsoft .NET Process class](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.process?view=net-10.0)

### Properties directly relevant to activity monitoring:

| Property | Type | What it tells you | Notes |
|----------|------|-------------------|-------|
| `Id` | int | PID | Already used by ProcessEnumerator |
| `ProcessName` | string | e.g. "claude", "node", "agy" | Already used |
| `StartTime` | DateTime | When process was launched | Reliable — comes from OS |
| `TotalProcessorTime` | TimeSpan | Accumulated CPU time across all threads | **Already used** for CPU % delta calculation |
| `WorkingSet64` | long | Current RAM in bytes | Already used as `RamMb` |
| `MainWindowTitle` | string | Window title text | **Not yet used** — could show active/idle state |
| `Responding` | bool | Whether UI thread is responding | Not useful for console apps (always true/false) |
| `HasExited` | bool | Whether process has terminated | Useful for cleanup |
| `ExitTime` | DateTime | When process exited | For logging |
| `MainModule` | ProcessModule | Full path to executable | Useful for precise identification |
| `Threads` | ProcessThreadCollection | Thread list | Could count active threads |
| `HandleCount` | int | Number of open handles | Correlates with activity |
| `SessionId` | int | Windows session ID | Filter to current user |

### What you CAN detect reliably:

- **Running vs not running**: `Process.GetProcesses()` + process name matching ✅
- **CPU usage over time**: `TotalProcessorTime` delta between samples ✅ (already implemented in `ProcessEnumerator.cs`)
- **Memory usage**: `WorkingSet64` ✅ (already implemented)
- **Process start time**: `StartTime` ✅ (not yet exposed in `ToolStatus`)
- **Last activity time**: **CANNOT** get directly from Process class ❌ — must infer from CPU deltas or transcript file timestamps

### What you CANNOT detect from System.Diagnostics.Process:

- **Network activity** (API calls in progress) — no API for this without ETW
- **Whether it's waiting for user input vs processing** — only CPU usage gives a hint
- **Quota/usage data** — must come from tool-specific data sources
- **Window title for console apps** — `MainWindowTitle` is often empty for console processes

---

## 2. Active vs Idle Detection via CPU Usage

### The approach your code already uses (ToolDetector.cs):

```csharp
public const double ActiveCpuThresholdPercent = 3.0;
var state = totalCpu >= ActiveCpuThresholdPercent ? ToolState.Active : ToolState.Quiet;
```

### Does this actually work?

**Partially. It's a useful heuristic but not reliable for all cases.**

**What works:**
- **Claude Code actively streaming a response**: High CPU (Node.js parsing JSON, rendering output) → Detectable ✅
- **Codex actively editing files**: High CPU (file I/O + code generation) → Detectable ✅
- **Tool waiting for user input**: Near-zero CPU → Detectable as idle ✅
- **Tool downloading/generating**: Spiky CPU → Detectable ✅

**What doesn't work reliably:**
- **Network I/O bound wait** (waiting for API response): CPU drops to near-zero even though the tool is "actively working" — it's just blocked on an HTTP request. The tool is NOT idle from the user's perspective, but CPU says it is. ❌
- **Claude Code streaming SSE**: The Node.js event loop handles SSE parsing with low CPU during streaming — the tool is "active" (generating tokens) but CPU may be low. ❌
- **Brief CPU spikes** within a poll interval: A 5-second API call between 10-second polls may show 0% CPU on both samples. ❌

### How CodexBar solves this (the gold standard):

CodexBar does NOT use CPU usage for activity detection. Instead, it uses **transcript file modification time**:

```swift
// From AgentSession.swift (CodexBar, github.com/steipete/CodexBar)
public func state(lastActivityAt: Date?, now: Date, hasLiveProcess: Bool) -> AgentSession.State {
    guard let lastActivityAt else { return hasLiveProcess ? .active : .idle }
    return now.timeIntervalSince(lastActivityAt) <= self.activeWindow ? .active : .idle
}
```

The `lastActivityAt` comes from the **modification timestamp of the most recent transcript JSONL file** in `~/.claude/projects/`. When Claude Code writes a new message to the transcript, the file's mtime updates, signaling "activity." The `activeWindow` defaults to **120 seconds** — if a transcript was modified in the last 2 minutes, the session is "active."

This is far more reliable than CPU because:
1. It captures network-wait activity (the tool writes to transcript even during streaming)
2. It works regardless of how the tool processes internally
3. It has direct semantic meaning (tool wrote output = active)

### Recommendation for your tray app:

**Use a dual-signal approach:**
1. **Primary signal**: Transcript/session file mtime (like CodexBar)
   - For Claude Code: `~/.claude/projects/**/*.jsonl` — newest mtime
   - For Codex: `~/.codex/sessions/` — newest mtime
   - For Hermes: SQLite DB modification time
   - For OpenCode/Antigravity: unknown data locations, fall back to CPU
2. **Fallback signal**: CPU usage delta (your current approach)
   - Use when no transcript data is available
   - Threshold of 3% is reasonable as a heuristic

---

## 3. CodexBar — How It Actually Does Process Monitoring

Source: [github.com/steipete/CodexBar](https://github.com/steipete/CodexBar) (19.3k stars), Swift source code analysis

### CodexBar monitors these tools:
- **Codex** (OpenAI) — process detection + session file scanning
- **Claude Code** (Anthropic) — process detection + transcript file scanning
- **66+ AI providers** — via web scraping, API polling, cookie import (not process monitoring)

### Process detection approach (from AgentSession.swift):

1. **Runs `ps` command** to get all processes with PID, PPID, start time, and command line
2. **Parses output** into `AgentProcessRecord` structs (PID, PPID, startedAt, command)
3. **Filters by executable basename**:
   - `"codex"` → Codex agent processes (excludes `app-server`, `--help`, `--version`)
   - `"claude"` → Claude Code processes (excludes helpers like `claude-code-acp`)
   - `"disclaimer"` → Also Claude (macOS-specific wrapper)
4. **Distinguishes source**: CLI vs desktop app vs IDE plugin, based on command line path
5. **Gets CWD** via `lsof` on macOS (to map process → project directory)

### Activity detection:

CodexBar uses **transcript file mtime** (NOT CPU usage):
- For Claude: Scans `~/.claude/projects/<escaped-cwd>/` for JSONL transcript files
- `lastActivityAt` = most recent transcript file's `modifiedAt`
- `activeWindow` = 120 seconds (configurable)
- Session is "active" if transcript was modified within the window

### What CodexBar does NOT do:
- Does NOT use CPU usage for activity detection
- Does NOT use memory usage for activity detection
- Does NOT monitor process count as a signal
- Does NOT track process start time for activity

### ClaudeWatchdog (Sources/CodexBarClaudeWatchdog/main.swift):

This is NOT a monitoring component. It's a **process tree supervisor**:
- Spawns a Claude Code process as a child
- Monitors for orphaned parent (getppid() == 1)
- On orphan detection: sends SIGTERM → SIGKILL to kill the entire process tree
- On CodexBar exit: propagates termination signal to Claude
- Purpose: Prevents zombie Claude processes when CodexBar crashes

---

## 4. agy-hud — What It Does for Process Monitoring

Source: [github.com/franksde/agy-hud](https://github.com/franksde/agy-hud) (16 stars)

### What agy-hud is:

A **CLI status-line plugin** for the Antigravity (agy) CLI, NOT a system tray app or process monitor. It renders a terminal HUD showing:

```
 3.5 Flash High |  Pro │  agy-hud │  main
Context ░░░░░░░░░░ 0% │ Usage ████████░░ 82% (↻ 1h 52m) |  █░░░░░░░░░ 13% (↻ 4d 21h)
```

### How it gets activity data:

1. **Reads status-line JSON from stdin** — Antigravity CLI pipes its state to the plugin via a `/statusline` hook
2. **`agent_state` field** from stdin: `"Idle"`, `"Thinking"`, `"Auth"` — this comes from the CLI itself, not from process monitoring
3. **Quota data**: From `GetUserStatus` local server endpoint (loopback HTTP), or from official `quota` object in the status-line payload
4. **Local quota cache**: Stores sanitized quota data at `$XDG_CACHE_HOME/agy-hud/quota_cache.json`

### What agy-hud does NOT do:
- Does NOT enumerate system processes
- Does NOT use CPU/RAM for activity detection
- Does NOT monitor other tools — it only shows Antigravity's own state
- Not relevant as an architecture model for your tray app

---

## 5. Real Windows Tray Apps with Per-Process Activity Graphs

### Windows Task Manager (built-in):
- **Per-process CPU history**: Right-click a process → "Graph summary view" or "Resource values" shows a real-time CPU usage graph
- **Implementation**: Uses Windows Performance Data Helper (PDH) or ETW to sample CPU at ~1 second intervals
- **UI**: Mini sparkline graph per process in the Details tab

### Process Lasso (Bitsum, bitsum.com):
- **ProBalance algorithm**: Dynamically adjusts process priority to maintain system responsiveness
- **Real-time process graph**: Main window shows per-process CPU, memory, I/O history
- **Core parking and CPU affinity**: Can bind processes to specific CPU cores
- **Process watchdog**: Monitors for process creation/destruction
- **Logging**: Per-process CPU usage history exported to CSV
- **Not a tray app** per se — has a tray icon but the main UI is a window
- Source: [Wikipedia - Process Lasso](https://en.wikipedia.org/wiki/Process_Lasso)

### Process Hacker (open-source):
- **Per-process graphs**: Real-time CPU, memory, I/O, GPU usage graphs
- **Network monitoring**: Can see per-process network connections
- **Tray icon**: Shows CPU usage as a tray icon graph
- Source: [github.com/processhacker/processhacker](https://github.com/processhacker/processhacker) (now called System Informer)

### GlassWire (network-focused):
- **Network activity graph**: Per-process network usage timeline
- **Not CPU/RAM focused** — monitors network connections

### Key insight for your app:
**No existing tray app combines process detection with AI-tool-specific activity monitoring.** Process Lasso and Task Manager show generic CPU/memory graphs, but none understand "this Claude Code session is actively generating a response" vs "waiting for user input." Your app would be the first to combine:
1. AI-tool-specific process detection (matching by command line)
2. Tool-specific activity signals (transcript file mtime + CPU usage)
3. Tray integration with status visualization

---

## 6. Actual Windows Process Names for Each Tool

Verified on this machine (C:\Users\shubh):

### Claude Code (Anthropic)
| Detail | Value |
|--------|-------|
| npm package | `@anthropic-ai/claude-code` |
| Windows executable | `claude.exe` (native binary) |
| Full path | `%APPDATA%\npm\node_modules\@anthropic-ai\claude-code\bin\claude.exe` |
| Shim | `claude.cmd` → calls `claude.exe` |
| Process name when running | `claude` |
| Command line contains | `claude.exe`, `anthropic-ai/claude-code` |
| Detection match terms | `claude.exe`, `claude`, `anthropic-ai/claude-code` |

### Codex (OpenAI)
| Detail | Value |
|--------|-------|
| npm package | `@openai/codex` |
| Windows executable | `codex.js` (runs via Node.js, NOT native) |
| Shim | `codex.cmd` → `node codex.js` |
| Process name when running | `node` (or `node.exe`) |
| Command line contains | `codex.js`, `@openai/codex` |
| Detection match terms | `codex.exe`, `openai/codex` |
| ⚠️ Important | **Process name is `node`**, not `codex`. Must match by command line argument. |

### Hermes Agent (Nous Research)
| Detail | Value |
|--------|-------|
| Install path | `%LOCALAPPDATA%\hermes\hermes-agent\venv\Scripts\hermes` |
| Runtime | Python venv (`python.exe` running `hermes` module) |
| Process name when running | `python` or `hermes` (depending on how invoked) |
| Command line contains | `hermes`, `hermes-agent` |
| Detection match terms | `hermes.exe`, `hermes-agent.exe`, `hermes` |

### OpenCode
| Detail | Value |
|--------|-------|
| npm package | `opencode-ai` |
| Windows executable | `opencode.exe` (native binary) |
| Full path | `%APPDATA%\npm\node_modules\opencode-ai\bin\opencode.exe` |
| Shim | `opencode.cmd` → calls `opencode.exe` |
| Process name when running | `opencode` |
| Command line contains | `opencode.exe`, `opencode-ai` |
| Detection match terms | `opencode.cmd`, `opencode.exe`, `opencode` |

### Antigravity (agy)
| Detail | Value |
|--------|-------|
| Install path | `%LOCALAPPDATA%\agy\bin\agy.exe` |
| Binary type | PE32+ executable (native x86-64, Go binary) |
| Process name when running | `agy` |
| Command line contains | `agy.exe`, `antigravity` |
| Detection match terms | `agy.exe`, `antigravity` |

### Current ToolProfile.cs match terms (from your code):
```csharp
new("Claude Code", ["claude.exe", "claude", "anthropic-ai/claude-code"]),
new("Hermes Agent", ["hermes.exe", "hermes-agent.exe", "hermes"]),
new("Codex", ["codex.exe", "openai/codex"]),
new("OpenCode", ["opencode.cmd", "opencode.exe", "opencode"]),
new("Antigravity", ["agy.exe", "antigravity"]),
```

### ⚠️ Detection gaps identified:

1. **Codex runs as `node.exe`** — matching on `codex.exe` won't find it. Need to match `codex.js` or `@openai/codex` in the command line. Current match term `"openai/codex"` should work via command line. ✅
2. **Hermes runs as `python.exe`** — matching on `hermes.exe` won't find it if the shim isn't used. Need to match `hermes` in command line. Current match term `"hermes"` should work via command line. ✅
3. **All npm tools may also spawn child `node.exe` processes** — these won't match the parent tool's name. For Claude Code (which is now a native `.exe`), this is fine. For Codex (still JS), child processes may be `node.exe` with different arguments. Consider adding PPID tracking.

---

## 7. Recommendations for Enhanced Process Monitoring

### Priority 1: Expose more Process data in ToolStatus
Add to `ProcessSample` or create a new `ProcessDetail` record:
```csharp
public sealed record ProcessDetail(
    int Pid,
    string ProcessName,
    string CommandLine,
    double CpuPercent,
    double RamMb,
    DateTime? StartTime,        // NEW: from Process.StartTime
    string? MainWindowTitle,    // NEW: from Process.MainWindowTitle
    string? MainModulePath,     // NEW: from Process.MainModule.FileName
    int HandleCount             // NEW: from Process.HandleCount
);
```

### Priority 2: Add transcript file mtime as primary activity signal
For Claude Code:
```csharp
// Scan ~/.claude/projects/**/ for newest JSONL file
var newestTranscript = Directory.GetFiles(
    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), 
    ".claude", "projects"), "*.jsonl", SearchOption.AllDirectories)
    .Select(f => new FileInfo(f).LastWriteTimeUtc)
    .DefaultIfEmpty(DateTime.MinValue)
    .Max();
```

### Priority 3: Track process start time and duration
```csharp
// In ToolStatus, add:
DateTimeOffset? SessionStartedAt;  // When the main process started
TimeSpan? SessionDuration;          // How long it's been running
```

### Priority 4: Window title monitoring (experimental)
```csharp
// MainWindowTitle can show:
// - Claude Code: "Claude Code" or project name
// - Terminal window title when tool is active
// Only works for GUI processes, not pure console apps
```

### Priority 5: Mini CPU usage history for tray tooltip
Keep a rolling buffer of CPU samples (e.g., last 60 samples at 1-second intervals) and render as a sparkline in the tray tooltip:
```csharp
// Circular buffer
private readonly float[] _cpuHistory = new float[60];
private int _cpuHistoryIndex = 0;

// Each poll cycle:
_cpuHistory[_cpuHistoryIndex % 60] = (float)totalCpu;
_cpuHistoryIndex++;
```

---

## Sources

- **Microsoft Process class**: https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.process?view=net-10.0
- **CodexBar source (AgentSession.swift)**: https://github.com/steipete/CodexBar/blob/main/Sources/CodexBarCore/AgentSession.swift
- **CodexBar source (ClaudeWatchdog)**: https://github.com/steipete/CodexBar/blob/main/Sources/CodexBarClaudeWatchdog/main.swift
- **CodexBar source (CodexExecutableResolver.swift)**: https://github.com/steipete/CodexBar/blob/main/Sources/CodexBarCore/CodexExecutableResolver.swift
- **agy-hud README**: https://github.com/franksde/agy-hud/blob/main/README.md
- **Process Lasso**: https://bitsum.com/process-lasso, https://en.wikipedia.org/wiki/Process_Lasso
- **Process Hacker/System Informer**: https://github.com/processhacker/processhacker
- **This machine verification**: Executable paths, shims, and binary types verified at C:\Users\shubh
