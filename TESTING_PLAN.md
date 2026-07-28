# AI Tools Monitor Testing Plan

Research snapshot: 2026-07-29

## Test Goals

Testing must prove four things:

- The tray UI behaves correctly on Windows 11.
- Process detection is accurate for real AI CLI launch shapes.
- The monitor itself stays lightweight.
- Packaging and startup work for the current Windows user without admin rights.

## Unit Tests

### Tool Detection

Test exact and launcher-based command lines:

| Scenario | Expected |
| --- | --- |
| `claude.exe` | Claude Code running |
| npm global Claude launcher path containing `@anthropic-ai/claude-code` | Claude Code running |
| `hermes chat` | Hermes Agent running |
| path containing `NousResearch/hermes-agent` | Hermes Agent running |
| `codex.exe` | OpenAI Codex CLI running |
| node command containing `@openai/codex` | OpenAI Codex CLI running |
| `opencode.exe` | OpenCode running |
| npm global path containing `opencode-ai` | OpenCode running |
| `antigravity.exe` | Google Antigravity CLI running |
| command containing `antigravity-cli` | Google Antigravity CLI running |

False-positive tests:

- A markdown file path containing `codex` is not enough to match Codex.
- A browser tab title in a browser command line is not enough to match a tool.
- A VS Code process opened on this documentation folder is not enough to match a tool.

### Process Tree Aggregation

Test:

- matched root includes direct children
- matched root includes recursive children
- child process is not counted twice
- orphaned child is dropped after one scan when root exits

### Resource Sampling

Test:

- first CPU sample returns null or warming-up state
- second CPU sample calculates a normalized CPU percent
- RAM aggregation sums working-set bytes
- process exit between samples does not crash sampler

### Config

Test:

- missing config creates defaults
- invalid config is backed up and replaced
- user-edited match terms survive app restart
- theme values accept only `system`, `light`, and `dark`

## Integration Tests

### Synthetic Process Harness

Create a small test executable or script that runs with controllable command-line arguments and child processes. Use it to simulate:

- `claude --session test`
- `codex exec "sleep"`
- `opencode run`
- `hermes chat`
- `antigravity --workspace C:\repo`

The monitor should detect these through the same matching pipeline used in production.

### Real CLI Smoke Tests

On the developer laptop, run installed versions of each target CLI one at a time. Record:

- detected status
- process IDs
- command line shown in row tooltip
- CPU and RAM shown in popup
- time from CLI launch to popup update
- time from CLI exit to idle state

## UI Tests

Manual Windows 11 matrix:

| Area | Cases |
| --- | --- |
| Theme | light, dark, high contrast |
| Scaling | 100 percent, 125 percent, 150 percent |
| Taskbar | bottom taskbar, auto-hide on, auto-hide off |
| Tray visibility | icon pinned beside clock, icon in hidden overflow |
| Display | laptop only, external monitor, mixed DPI |

Required checks:

- Left-click opens popup.
- Left-click again hides popup.
- Right-click opens context menu.
- Escape closes popup.
- Click outside closes popup.
- Popup stays on screen near the tray icon.
- Text does not overlap at 150 percent scaling.
- Badge remains legible at 16 px tray size.

## Windows 11 Tray Icon Visibility Tests

Because Windows controls which notification-area icons appear beside the clock, test both states:

- First launch with icon in overflow.
- User drags icon from overflow beside clock.
- App restart preserves normal Windows visibility behavior.
- `Open taskbar settings` opens Windows taskbar settings.

Do not treat hidden overflow placement as an app failure. Treat failure to create any tray icon as a release blocker.

## Explorer Restart Test

Steps:

1. Start AI Tools Monitor.
2. Open Task Manager.
3. Restart Windows Explorer.
4. Confirm the tray icon returns within 5 seconds.
5. Confirm left-click still opens the popup.

## Performance Tests

Run for 10 minutes with no target tools active:

- average monitor CPU below 1 percent
- private memory below 80 MB
- scan duration p95 below 100 ms

Run for 10 minutes with all five tools active:

- average monitor CPU below 2 percent
- private memory below 100 MB
- popup remains responsive
- no unhandled exceptions from process enumeration

## Packaging Tests

Framework-dependent build:

```powershell
dotnet publish src\AiToolsMonitor\AiToolsMonitor.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
```

Self-contained build:

```powershell
dotnet publish src\AiToolsMonitor\AiToolsMonitor.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

Checks:

- app launches from publish folder
- no console window appears
- config path is created under `%APPDATA%`
- diagnostics export writes a markdown file
- install script copies to `%LOCALAPPDATA%\Programs\AI Tools Monitor`
- uninstall script removes startup registration

## Release Blockers

- Left-click does not open popup.
- More than one tray icon appears.
- Tray icon disappears permanently after Explorer restart.
- Any target CLI cannot be detected through configurable match terms.
- Idle monitor CPU averages 1 percent or higher.
- App requires admin rights.
- Startup registration writes to HKLM instead of HKCU.

## Sources

- pystray behavior reference for prototype bug: https://pystray.readthedocs.io/en/latest/usage.html
- Microsoft NotifyIcon docs: https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.notifyicon?view=windowsdesktop-10.0
- Windows taskbar notification-area customization: https://support.microsoft.com/en-us/windows/experience/personalization/customize-the-taskbar-in-windows
- psutil process iteration docs: https://psutil.readthedocs.io/
- .NET single-file deployment: https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview

