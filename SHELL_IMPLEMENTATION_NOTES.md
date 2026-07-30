# Main Shell Implementation Notes

## What was built

- Added `src/AiToolsMonitor/Shell/MainShellForm.cs`, a normal resizable application
  window with a persistent left sidebar for Dashboard, Analysis, Cost Report,
  Usage History, Budget, and Settings.
- Sidebar navigation swaps one `DockStyle.Fill` content page inside the shell.
  Analysis, Cost Report, Usage History, and Budget reuse their existing Forms as
  non-top-level child controls; their engines, database queries, events, and save
  behavior were not rewritten.
- Added a lightweight Settings page with a shell/dashboard dark-theme override,
  a Windows color-settings shortcut, and the current soft/hard budget cap summary.
- Extracted the existing theme reading and exact light/dark/high-contrast palette
  into `Popup/ThemeSettings.cs`. The shell and neutral presentation surfaces in
  embedded pages use those shared colors. Semantic status, warning, and grade
  colors remain unchanged.
- Updated `TrayHost` so tray left-click opens/focuses the shell Dashboard. Open,
  Edit budget, Analysis, Cost report, and Usage history context-menu commands now
  navigate to the matching shell page. Refresh, Quick launch, Recent Projects,
  Export, taskbar settings, and Exit retain their existing behavior.
- The shell hides instead of being destroyed on user close, restores from a
  minimized state, and disposes all cached embedded pages before the shared
  history database is disposed at application exit.

## StatusPopup decision

`StatusPopup` was repurposed as the Dashboard content rather than duplicating or
extracting its large live-status layout. `TrayHost` creates it with
`embedded: true`; this disables its top-level/tool-window, always-on-top,
deactivate-to-hide, non-client, and acrylic-window behavior while retaining its
existing cards, quota rendering, summary display, and polling updates.

This keeps one live `StatusSnapshot` rendering path. `TrayHost.Poll()` still uses
`ProcessEnumerator`, `ToolDetector`, and the existing quota clients exactly as it
did before, then renders the resulting snapshot into the embedded Dashboard.

## Verification

Run from `C:\Users\shubh\Projects\ai-tools-monitor-wt-shell`.

### Build

Command:

```powershell
dotnet build .\AI.Tools.Monitor.sln
```

Final output summary:

```text
Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:00.96
```

Exit code: `0`.

A non-incremental compile earlier in the same verification pass also succeeded
with the 16 pre-existing `CS8602` warnings in `UsageHistoryForm.cs` and no errors.

### Tests

Command:

```powershell
dotnet test .\AI.Tools.Monitor.sln
```

Final output summary:

```text
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:    88, Skipped:     0, Total:    88, Duration: 16 s - AiToolsMonitor.Tests.dll (net9.0)
```

Exit code: `0`.

### Additional checks

- `git diff --check`: passed; only Git's existing LF-to-CRLF working-copy notices
  were printed.
- Independent code review: no remaining Critical or Important issues after
  cached-page disposal and embedded-theme coverage were corrected.
- A live automated click-through was attempted, but the installed Windows
  automation runtime could not connect to its native control pipe. No
  test-only launch behavior was added to the production app as a workaround.
