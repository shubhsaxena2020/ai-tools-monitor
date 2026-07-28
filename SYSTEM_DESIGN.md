# AI Tools Monitor System Design

Research snapshot: 2026-07-29

## Design Goal

Detect five local AI CLI tools by inspecting Windows processes and command lines, then publish a thread-safe status snapshot to the tray UI every 2.5 seconds.

There is no dependency on tool-specific local APIs. The app uses process observation only.

## Data Flow

1. `StatusPoller` fires every 2.5 seconds.
2. `ProcessEnumerator` captures process id, parent process id, executable name, command line, working set, and cumulative CPU time.
3. `ToolDetector` scores each process against the five configured tool profiles.
4. `ProcessTreeAggregator` includes child processes under a matched root.
5. `ResourceSampler` computes CPU deltas from the previous snapshot and sums working-set RAM.
6. The poller publishes an immutable `StatusSnapshot`.
7. `TrayHost` updates icon state and tooltip on the UI thread.
8. `StatusPopup` redraws if visible.

## Detection Strategy

The prototype uses the right approach conceptually: psutil-style process iteration plus command-line matching. psutil recommends `process_iter()` over raw PID iteration because it is safer against race conditions when processes exit during enumeration. The production C# version should preserve that behavior with Windows APIs and defensive exception handling.

Production process attributes:

- `pid`
- `parentPid`
- `name`
- `exePath`, when available
- `commandLine`
- `workingSetBytes`
- `totalProcessorTime`
- `sampleTimeUtc`

## Tool Profiles

Default profiles are stored in code and copied into user config on first launch.

```json
{
  "tools": [
    {
      "id": "claude-code",
      "displayName": "Claude Code",
      "matchTerms": ["claude", "claude.exe", "@anthropic-ai/claude-code"],
      "excludeTerms": ["claude-code-docs", "PROJECT_BRIEF.md"]
    },
    {
      "id": "hermes-agent",
      "displayName": "Hermes Agent",
      "matchTerms": ["hermes", "hermes.exe", "hermes-agent", "NousResearch/hermes-agent"],
      "excludeTerms": ["hermes-agent-docs"]
    },
    {
      "id": "codex-cli",
      "displayName": "OpenAI Codex CLI",
      "matchTerms": ["codex", "codex.exe", "@openai/codex"],
      "excludeTerms": ["codex-docs"]
    },
    {
      "id": "opencode",
      "displayName": "OpenCode",
      "matchTerms": ["opencode", "opencode.exe", "opencode-ai"],
      "excludeTerms": ["opencode-docs"]
    },
    {
      "id": "antigravity-cli",
      "displayName": "Google Antigravity CLI",
      "matchTerms": ["antigravity", "antigravity.exe", "antigravity-cli"],
      "excludeTerms": ["antigravity-docs"]
    }
  ]
}
```

## Matching Rules

Use a scoring approach instead of naive substring matching.

| Evidence | Score |
| --- | ---: |
| Executable base name exactly equals command, such as `codex.exe` | 100 |
| First command-line token equals command, such as `codex` | 90 |
| Command line contains known npm package path, such as `@openai/codex` | 80 |
| Command line contains known repo/package identifier, such as `hermes-agent` | 60 |
| Command line only contains a display word in an unrelated path | 10 |
| Command line contains an exclude term | reject |

A process is considered a root match at score 60 or higher. Child processes of a root match are included in the tool's aggregate even if they do not independently match the tool terms.

## Process Tree Handling

Many AI CLIs launch workers, shells, node processes, Python processes, or editor-integrated commands. The app should aggregate:

- root matched process
- direct children
- recursive children while the root remains alive

If a child process is shared or reparented after the root exits, it should only remain attributed for one extra scan. This prevents stale child attribution.

## CPU Calculation

Do not call a blocking CPU measurement per process. Store cumulative CPU time per PID and compute deltas:

```text
cpuPercent = 100 * (currentTotalProcessorTime - previousTotalProcessorTime)
                 / (wallClockDelta * logicalProcessorCount)
```

The first sample has no previous data, so CPU is displayed as `warming up` or `0.0%` with a stale flag cleared on the next scan.

## Memory Calculation

Use working set bytes for display because it maps to physical memory pressure better than virtual address size.

Display formatting:

- Below 1024 MB: `123 MB`
- 1024 MB and above: `1.2 GB`

## Thread Safety

The UI never mutates poller state. The poller publishes a complete immutable snapshot.

Rules:

- `StatusPoller` owns sampling state and previous CPU counters.
- `ToolDetector` is stateless except for compiled match profiles.
- `TrayHost` receives snapshots through a thread-safe event queue.
- UI updates are marshalled to the WinForms UI thread with `BeginInvoke`.
- The popup reads the latest snapshot reference atomically and renders from that copy.

Recommended snapshot shape:

```csharp
public sealed record ToolStatus(
    string ToolId,
    string DisplayName,
    bool IsRunning,
    bool IsActive,
    double? CpuPercent,
    long WorkingSetBytes,
    int ProcessCount,
    string? PrimaryCommandLine,
    IReadOnlyList<int> ProcessIds);

public sealed record StatusSnapshot(
    DateTimeOffset SampledAt,
    IReadOnlyList<ToolStatus> Tools,
    double MonitorCpuPercent,
    long MonitorWorkingSetBytes,
    int SkippedProcessCount);
```

## Polling Failures

Expected failures:

- process exits during enumeration
- command line unavailable due to permissions
- WMI/CIM query returns a process with null command line
- process CPU time unavailable after exit

Handling:

- Skip the failing process for that scan.
- Increment `SkippedProcessCount`.
- Keep previous tool state for no more than one interval if the scan itself fails completely.
- Never show a modal error for routine enumeration failures.

## Production C# Enumeration

Use a Windows-specific enumerator:

- `System.Management` query for `Win32_Process` to retrieve `ProcessId`, `ParentProcessId`, `Name`, `ExecutablePath`, and `CommandLine`.
- `System.Diagnostics.Process.GetProcessById(pid)` for current working set and total processor time.
- Cache command lines for short-lived processes only within the current scan.

WMI command-line access is acceptable at 2.5-second cadence for a single-user utility. If polling exceeds 100 ms consistently, optimize by prefiltering process names and only retrieving full command lines for candidates.

## Sources

- psutil process iteration docs: https://psutil.readthedocs.io/
- Microsoft Win32_Process WMI class: https://learn.microsoft.com/en-us/windows/win32/cimwin32prov/win32-process
- Microsoft Process class: https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.process?view=net-10.0
- Claude Code setup docs: https://code.claude.com/docs/en/setup
- OpenAI Codex CLI GitHub: https://github.com/openai/codex
- OpenCode docs: https://opencode.ai/docs/
- Hermes Agent CLI docs: https://hermes-agent.nousresearch.com/docs/user-guide/cli
- Google Antigravity CLI docs: https://antigravity.google/docs/cli/getting-started

