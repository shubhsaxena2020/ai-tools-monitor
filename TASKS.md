# AI Tools Monitor Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a Windows 11 tray utility that monitors Claude Code, Hermes Agent, OpenAI Codex CLI, OpenCode, and Google Antigravity CLI by process detection and displays live CPU/RAM status in a left-click tray popup.

**Architecture:** Native C#/.NET WinForms tray app. A background poller creates immutable status snapshots from Windows process data; the tray host and popup render those snapshots on the UI thread.

**Tech Stack:** C#, .NET 10, WinForms NotifyIcon, System.Management, System.Diagnostics.Process, System.Text.Json, xUnit.

---

## File Structure

- Create `AI.Tools.Monitor.sln`: solution file.
- Create `src/AiToolsMonitor/AiToolsMonitor.csproj`: WinForms app project.
- Create `src/AiToolsMonitor/Program.cs`: single entrypoint.
- Create `src/AiToolsMonitor/Monitoring/*.cs`: process records, detector, aggregator, sampler, poller.
- Create `src/AiToolsMonitor/Tray/*.cs`: tray icon, context menu, click behavior.
- Create `src/AiToolsMonitor/Popup/*.cs`: popup form, status rows, theme service.
- Create `src/AiToolsMonitor/Config/*.cs`: config model and persistence.
- Create `src/AiToolsMonitor/Startup/*.cs`: current-user startup registration.
- Create `src/AiToolsMonitor/Diagnostics/*.cs`: local diagnostics export.
- Create `tests/AiToolsMonitor.Tests/*.cs`: unit tests.
- Create `scripts/install-user.ps1`: copy published files and register startup when requested.
- Create `scripts/uninstall-user.ps1`: remove startup registration and installed files.

## Task 1: Scaffold Solution

- [ ] Create the solution and projects.

```powershell
dotnet new sln -n AI.Tools.Monitor
dotnet new winforms -n AiToolsMonitor -o src\AiToolsMonitor
dotnet new xunit -n AiToolsMonitor.Tests -o tests\AiToolsMonitor.Tests
dotnet sln add src\AiToolsMonitor\AiToolsMonitor.csproj
dotnet sln add tests\AiToolsMonitor.Tests\AiToolsMonitor.Tests.csproj
dotnet add tests\AiToolsMonitor.Tests reference src\AiToolsMonitor\AiToolsMonitor.csproj
dotnet add src\AiToolsMonitor package System.Management
```

- [ ] Enable nullable and Windows-only settings in `src/AiToolsMonitor/AiToolsMonitor.csproj`.

```xml
<PropertyGroup>
  <OutputType>WinExe</OutputType>
  <TargetFramework>net10.0-windows</TargetFramework>
  <UseWindowsForms>true</UseWindowsForms>
  <Nullable>enable</Nullable>
  <ImplicitUsings>enable</ImplicitUsings>
  <ApplicationManifest>app.manifest</ApplicationManifest>
</PropertyGroup>
```

- [ ] Verify scaffold.

```powershell
dotnet build
```

Expected result: solution builds with zero errors.

## Task 2: Add Core Monitoring Models

- [ ] Create `src/AiToolsMonitor/Monitoring/ProcessRecord.cs`.

```csharp
namespace AiToolsMonitor.Monitoring;

public sealed record ProcessRecord(
    int ProcessId,
    int? ParentProcessId,
    string Name,
    string? ExecutablePath,
    string? CommandLine,
    long WorkingSetBytes,
    TimeSpan TotalProcessorTime,
    DateTimeOffset SampledAt);
```

- [ ] Create `src/AiToolsMonitor/Monitoring/StatusSnapshot.cs` using the model shown in `SYSTEM_DESIGN.md`.

- [ ] Add a unit test that constructs a `ToolStatus` and asserts `IsRunning` and memory values are preserved.

- [ ] Run:

```powershell
dotnet test
```

Expected result: model tests pass.

## Task 3: Implement Tool Profiles And Config

- [ ] Create `src/AiToolsMonitor/Config/ToolProfile.cs`.

```csharp
namespace AiToolsMonitor.Config;

public sealed record ToolProfile(
    string Id,
    string DisplayName,
    IReadOnlyList<string> MatchTerms,
    IReadOnlyList<string> ExcludeTerms);
```

- [ ] Create `src/AiToolsMonitor/Config/AppConfig.cs` with defaults for poll interval, active threshold, theme, startup, and the five profiles from `SYSTEM_DESIGN.md`.

- [ ] Create `ConfigStore` that reads and writes `%APPDATA%\AI Tools Monitor\config.json`.

- [ ] Test that a missing config creates exactly five tool profiles.

## Task 4: Implement Tool Detector

- [ ] Create `src/AiToolsMonitor/Monitoring/ToolDetector.cs`.

Required behavior:

- score exact executable basename at 100
- score first command token at 90
- score package/repo term in command line at 80 or 60
- reject any exclude term
- return no match below 60

- [ ] Add tests for these command lines:

```text
C:\Users\dev\AppData\Roaming\npm\claude.cmd
node.exe C:\Users\dev\AppData\Roaming\npm\node_modules\@openai\codex\bin\codex.js
opencode.exe
python.exe C:\dev\hermes-agent\main.py hermes chat
antigravity.exe --workspace C:\repo
code.exe C:\repo\PROJECT_BRIEF.md
```

Expected result: first five match their intended tools; the editor/doc command does not match.

## Task 5: Implement Process Enumeration

- [ ] Create `ProcessEnumerator` that queries `Win32_Process` for process id, parent id, name, executable path, and command line.

- [ ] For each WMI process row, use `Process.GetProcessById(pid)` to read working set and total processor time.

- [ ] Skip inaccessible or exited processes and increment skipped count.

- [ ] Add an integration diagnostics command that prints one current process scan in debug builds.

## Task 6: Implement Process Tree Aggregation

- [ ] Create `ProcessTreeAggregator`.

Required behavior:

- find root process matches
- recursively include children
- prevent one child process from being counted twice for the same tool
- keep process IDs in the resulting tool status

- [ ] Test with fake records where `node.exe` is the root and `cmd.exe` plus `python.exe` are children.

## Task 7: Implement CPU And RAM Sampling

- [ ] Create `ResourceSampler`.

Required behavior:

- store previous `TotalProcessorTime` by PID
- compute CPU percent using logical processor count
- return null CPU for first sample
- sum working set bytes across included processes

- [ ] Test first sample returns null CPU.

- [ ] Test second sample returns a non-null CPU percent.

## Task 8: Implement Background Poller

- [ ] Create `StatusPoller` using `PeriodicTimer`.

- [ ] Publish immutable snapshots through an event or channel.

- [ ] Support manual refresh by triggering one immediate scan.

- [ ] Marshal UI subscribers through `BeginInvoke` from the tray host, not from the poller.

## Task 9: Implement Tray Host

- [ ] Create `TrayHost` that owns one `NotifyIcon`.

- [ ] Left-click toggles `StatusPopup`.

- [ ] Right-click opens `ContextMenuStrip`.

- [ ] Tooltip updates after each snapshot.

- [ ] Add `Open taskbar settings` command using:

```csharp
Process.Start(new ProcessStartInfo("ms-settings:taskbar") { UseShellExecute = true });
```

## Task 10: Implement Popup

- [ ] Create a borderless `StatusPopup` form with five fixed rows.

- [ ] Render rows from the latest `StatusSnapshot`.

- [ ] Implement Escape to close.

- [ ] Implement outside-click close.

- [ ] Apply light/dark tokens from `THEME.md`.

## Task 11: Implement Startup Registration

- [ ] Create `StartupRegistration` using HKCU Run key:

`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`

- [ ] Add context menu checkbox `Start at login`.

- [ ] Test registry path generation without writing registry in unit tests.

## Task 12: Implement Diagnostics Export

- [ ] Create `DiagnosticsExporter`.

- [ ] Export markdown to `%APPDATA%\AI Tools Monitor\diagnostics`.

- [ ] Include current snapshot, skipped process count, scan duration, app version, OS version, runtime version, and config path.

## Task 13: Package And Install Locally

- [ ] Publish framework-dependent single-file build.

```powershell
dotnet publish src\AiToolsMonitor\AiToolsMonitor.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish\AI.Tools.Monitor
```

- [ ] Create `scripts/install-user.ps1` to copy `publish\AI.Tools.Monitor` into `%LOCALAPPDATA%\Programs\AI Tools Monitor`.

- [ ] Create `scripts/uninstall-user.ps1` to remove HKCU startup value and installed files.

## Task 14: Manual Verification Pass

- [ ] Start the app and confirm one tray icon appears or appears in the hidden overflow.
- [ ] Left-click opens popup.
- [ ] Right-click opens menu.
- [ ] Run each target CLI and confirm the matching row changes to running within one polling interval.
- [ ] Exit each target CLI and confirm row returns to idle.
- [ ] Restart Explorer and confirm the tray icon returns.
- [ ] Record monitor CPU and RAM after 10 minutes idle.

## Sources

- Microsoft NotifyIcon docs: https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.notifyicon?view=windowsdesktop-10.0
- Microsoft WinForms tray icon guide: https://learn.microsoft.com/en-us/dotnet/desktop/winforms/controls/app-icons-to-the-taskbar-with-wf-notifyicon
- Microsoft Win32_Process docs: https://learn.microsoft.com/en-us/windows/win32/cimwin32prov/win32-process
- .NET single-file deployment: https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview
- Microsoft Run and RunOnce registry keys: https://learn.microsoft.com/en-us/windows/win32/setupapi/run-and-runonce-registry-keys
