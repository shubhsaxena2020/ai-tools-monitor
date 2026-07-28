# AI Tools Monitor Tech Stack

Research snapshot: 2026-07-29

## Final Recommendation

Build AI Tools Monitor with:

- Language: C#
- Runtime: .NET 10
- UI: WinForms
- Tray: `System.Windows.Forms.NotifyIcon`
- Popup: custom borderless WinForms `Form`
- Process command-line inspection: `System.Management` / WMI `Win32_Process`
- Resource sampling: `System.Diagnostics.Process`
- Config: JSON via `System.Text.Json`
- Tests: xUnit
- Packaging: `dotnet publish` single-file for `win-x64`

## Why This Stack

The app is Windows-only, tray-first, and must stay low overhead. WinForms is the simplest native stack that gives direct access to the notification area without introducing a browser runtime or a heavy XAML application shell.

Use WinForms even though the popup is custom-styled. The UI requirement is five status rows, two icon buttons, and a context menu. WPF, WinUI, Tauri, and Electron are more capable than needed.

## Dependencies

Keep dependencies small:

| Dependency | Purpose | Required |
| --- | --- | --- |
| `System.Management` | Query process command lines through WMI | Yes |
| `xunit` | Unit tests | Yes |
| `FluentAssertions` | Readable test assertions | Optional |

Avoid:

- Electron
- Tauri
- WinUI 3
- background web servers
- local databases
- telemetry libraries
- UI component suites

## Project Layout

```text
AI.Tools.Monitor.sln
src/
  AiToolsMonitor/
    Program.cs
    App/
      AppController.cs
      SingleInstanceGuard.cs
    Tray/
      TrayHost.cs
      TrayIconRenderer.cs
      TrayContextMenu.cs
    Popup/
      StatusPopup.cs
      StatusRowControl.cs
      Theme.cs
      ThemeService.cs
    Monitoring/
      ProcessEnumerator.cs
      ToolDetector.cs
      ProcessTreeAggregator.cs
      ResourceSampler.cs
      StatusPoller.cs
      StatusSnapshot.cs
    Config/
      AppConfig.cs
      ConfigStore.cs
      ToolProfile.cs
    Startup/
      StartupRegistration.cs
    Diagnostics/
      DiagnosticsExporter.cs
    Assets/
      icon-idle.ico
      icon-running-1.ico
      icon-running-2.ico
      icon-running-3.ico
      icon-running-4.ico
      icon-running-5.ico
tests/
  AiToolsMonitor.Tests/
    ToolDetectorTests.cs
    ProcessTreeAggregatorTests.cs
    ResourceSamplerTests.cs
    ConfigStoreTests.cs
```

## Build Commands

Create solution:

```powershell
dotnet new sln -n AI.Tools.Monitor
dotnet new winforms -n AiToolsMonitor -o src\AiToolsMonitor
dotnet new xunit -n AiToolsMonitor.Tests -o tests\AiToolsMonitor.Tests
dotnet sln add src\AiToolsMonitor\AiToolsMonitor.csproj
dotnet sln add tests\AiToolsMonitor.Tests\AiToolsMonitor.Tests.csproj
dotnet add tests\AiToolsMonitor.Tests reference src\AiToolsMonitor\AiToolsMonitor.csproj
dotnet add src\AiToolsMonitor package System.Management
```

Publish:

```powershell
dotnet publish src\AiToolsMonitor\AiToolsMonitor.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
```

Self-contained publish for a machine without .NET Desktop Runtime:

```powershell
dotnet publish src\AiToolsMonitor\AiToolsMonitor.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

## Stack Risks

| Risk | Mitigation |
| --- | --- |
| WMI command-line queries are slower than simple process enumeration | Poll every 2.5 seconds and optimize only if scan time exceeds 100 ms |
| WinForms default controls can look dated | Use owner-drawn compact popup and explicit theme tokens |
| Windows 11 can hide tray icons | Include first-run notification, taskbar settings shortcut, and deployment pinning step |
| .NET publish size may be larger when self-contained | Use framework-dependent mode on the developer's own machine |

## Sources

- Microsoft NotifyIcon docs: https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.notifyicon?view=windowsdesktop-10.0
- Microsoft WinForms tray icon guide: https://learn.microsoft.com/en-us/dotnet/desktop/winforms/controls/app-icons-to-the-taskbar-with-wf-notifyicon
- Microsoft Win32_Process docs: https://learn.microsoft.com/en-us/windows/win32/cimwin32prov/win32-process
- .NET single-file deployment: https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview
- .NET publishing overview: https://learn.microsoft.com/en-us/dotnet/core/deploying/
- Electron process model: https://electronjs.org/docs/latest/tutorial/process-model
- Tauri overview: https://v2.tauri.app/

