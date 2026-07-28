# AI Tools Monitor Architecture

Research snapshot: 2026-07-29

## Architecture Decision

Use a native Windows app:

- C# on .NET 10
- WinForms `NotifyIcon` for tray integration
- A borderless WinForms popup window for the dashboard
- Native Windows user-level startup registration
- Built-in .NET process sampling and Windows command-line inspection

The Python plus pystray prototype should be kept only as a proof of detection logic. The production app should not use pystray.

## Why Switch Away From Python + pystray

pystray is useful for a fast prototype, but the core user complaint is exactly where pystray is weakest for this product: Windows tray interaction. Its documentation says the menu is displayed when the right-hand button is pressed on Windows, while primary-button activation can trigger a default menu item. That can be wired to open a window, but the app still lacks first-class control over native tray messages, popup positioning, Explorer restart recovery, and Windows-specific lifecycle behavior.

Python also adds distribution friction for a solo Windows tray utility: Python runtime packaging, pyinstaller antivirus noise, and a larger idle memory footprint than a small native .NET utility.

## Selected Runtime Shape

The app is a tray-only WinForms process. It creates no main window at startup.

Core runtime components:

- `Program`: single-instance startup, config load, tray host startup.
- `TrayHost`: owns `NotifyIcon`, context menu, left-click handling, icon state, Explorer restart recovery.
- `StatusPoller`: background timer that samples process data and emits immutable snapshots.
- `ToolDetector`: matches configured tool profiles against process command lines.
- `ResourceSampler`: computes CPU deltas and RAM totals.
- `StatusPopup`: small borderless dashboard opened from tray left-click.
- `StartupRegistration`: manages current-user autostart.
- `DiagnosticsExporter`: writes local troubleshooting reports.

## Alternatives Considered

| Option | Strengths | Weaknesses | Decision |
| --- | --- | --- | --- |
| Python + pystray + psutil | Fast prototype, excellent process scanning library, simple iteration | Right-click menu default on Windows, packaging friction, higher runtime overhead, less native control | Reject for production |
| Python + pywin32 Shell_NotifyIcon | Keeps psutil, exposes lower-level tray messages | More Win32 plumbing in Python, still Python packaging/runtime overhead | Reject unless .NET is blocked |
| C# WinForms NotifyIcon | Native Windows control, low complexity, direct click/menu events, easy user-level install | UI is less modern by default than WinUI/WPF | Select |
| C# WPF + NotifyIcon library | Better XAML styling, richer popup layout | WPF has more overhead and trimming limitations; tray support is not built into WPF itself | Defer |
| WinUI 3 / Windows App SDK | Modern Fluent controls | Heavier deployment/runtime setup for a tiny solo utility | Reject for MVP |
| Tauri | Good tray support and smaller than Electron for web UI apps | Rust plus web frontend is more stack than this app needs | Reject for MVP |
| Electron | Mature tray APIs and easy UI | Multi-process Chromium architecture is too heavy for a resource monitor | Reject |

## Reliability Notes

### Tray Icon Creation

WinForms `NotifyIcon` wraps the Windows notification-area pattern and supports icon, tooltip, context menu, click events, and visibility. It is the highest-leverage native API for this utility.

### Explorer Restart

Windows Explorer can restart and clear tray icons. The app should register the `TaskbarCreated` window message and recreate the `NotifyIcon` when Explorer broadcasts it.

### Windows 11 Hidden Overflow

No architecture can guarantee that Windows 11 permanently displays a new tray icon beside the clock. Microsoft documents that users choose which icons appear in the system tray. Treat this as an onboarding and documentation issue:

- First launch notification explains the hidden icons arrow.
- Context menu includes `Open taskbar settings`.
- Deployment docs include a pinning step.

### Resource Footprint

The design avoids webview runtimes, background servers, and always-open windows. Polling runs on a timer, snapshots are immutable, and the popup only redraws when visible.

Target budgets:

- Idle app CPU: below 1 percent average.
- Private memory: below 80 MB.
- Poll duration: below 100 ms on the developer laptop for a normal process table.

## Packaging Architecture

For a solo developer install, use a simple zip layout under:

`%LOCALAPPDATA%\Programs\AI Tools Monitor`

Preferred publish mode:

- Framework-dependent single-file for the development machine if .NET Desktop Runtime is installed.
- Self-contained single-file when moving to another Windows 11 laptop.

This avoids MSIX overhead while staying easy to update by replacing one folder.

## Sources

- Microsoft NotifyIcon class: https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.notifyicon?view=windowsdesktop-10.0
- Microsoft Shell_NotifyIcon: https://learn.microsoft.com/en-us/windows/win32/api/shellapi/nf-shellapi-shell_notifyicona
- Microsoft notification area overview: https://learn.microsoft.com/en-us/windows/win32/shell/notification-area
- pystray usage docs: https://pystray.readthedocs.io/en/latest/usage.html
- Electron process model: https://electronjs.org/docs/latest/tutorial/process-model
- Electron Tray API: https://electronjs.org/docs/latest/api/tray
- Tauri overview: https://v2.tauri.app/
- .NET single-file deployment: https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview
- .NET WPF trimming limitations: https://learn.microsoft.com/en-us/dotnet/core/deploying/trimming/incompatibilities

