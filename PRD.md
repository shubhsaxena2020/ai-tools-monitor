# AI Tools Monitor PRD

Research snapshot: 2026-07-29

## Objective

Ship a Windows 11 tray utility that shows whether the developer's local AI CLI tools are idle or running, plus their current CPU and RAM usage, without opening Task Manager or switching terminals.

## User Story

As a solo developer running several local coding agents, I want a tray icon that changes state and opens a small status popup on left-click, so I can immediately see which agents are still consuming resources.

## Functional Requirements

### FR-1: Monitored Tools

The app must monitor exactly these default tool profiles:

| Tool | Primary commands and identifiers |
| --- | --- |
| Claude Code | `claude`, `claude.exe`, `@anthropic-ai/claude-code` |
| Hermes Agent | `hermes`, `hermes.exe`, `hermes-agent`, `NousResearch/hermes-agent` |
| OpenAI Codex CLI | `codex`, `codex.exe`, `@openai/codex` |
| OpenCode | `opencode`, `opencode.exe`, `opencode-ai` |
| Google Antigravity CLI | `antigravity`, `antigravity.exe`, `antigravity-cli` |

The first production version should allow these patterns to be edited in a local JSON config file, because CLI packaging can change and npm launchers may execute through `node.exe`.

### FR-2: Status Model

Each tool has one required status:

- `Idle`: no matching process or process tree was detected in the latest scan.
- `Running`: one or more matching process trees were detected.

Each running tool also has an activity hint:

- `Active`: aggregate CPU is at least 3 percent for two consecutive samples.
- `Quiet`: process exists but aggregate CPU is below the active threshold.

The tray icon only uses `Idle` and `Running`. The popup can show `Active` or `Quiet` as row detail.

### FR-3: Tray Icon States

The app uses one tray icon, not one icon per tool.

| State | Condition | Icon behavior |
| --- | --- | --- |
| Idle | Zero monitored tools running | Neutral outline icon, tooltip says `AI Tools Monitor: idle` |
| Running | One or more monitored tools running | Accent icon with a numeric badge from 1 to 5, tooltip summarizes running tools |

Do not use five separate tray icons. Multiple icons make Windows 11 overflow behavior worse and create extra visual noise.

### FR-4: Click Behavior

Left-click must toggle the status popup. Right-click must open a compact context menu.

This is a deliberate deviation from pystray's Windows default. pystray's documented menu behavior is right-click on Windows, with primary-button activation limited to a default menu item. Modern tray utilities often treat the tray icon as a direct launcher for their primary surface: Docker Desktop documents selecting the whale icon to open its tray menu, and 1Password lets users configure selecting the tray icon to show Quick Access.

Required interactions:

- Single left-click: show popup if hidden, hide popup if visible.
- Right-click: show context menu with `Open`, `Refresh now`, `Open taskbar settings`, `Open config`, and `Exit`.
- Double-click: same as left-click. It must not exit or perform destructive actions.
- Escape while popup focused: hide popup.
- Click outside popup: hide popup.

### FR-5: Popup Content

The popup displays a dense five-row dashboard:

| Column | Content |
| --- | --- |
| Tool | Tool name and small status dot |
| Status | `Idle`, `Quiet`, or `Active` |
| CPU | Aggregate current CPU percent across matched process tree |
| RAM | Aggregate working set in MB |
| Processes | Count of matched processes |

Below the rows, show:

- `Last updated HH:mm:ss`
- `Monitor CPU/RAM` for the monitor app itself
- A small `Refresh` icon button
- A settings icon button that opens the config file location

### FR-6: Refresh Cadence

Use a background poller with these intervals:

- Default tray polling: every 2.5 seconds.
- Popup visible: keep 2.5 seconds, but redraw immediately when a new sample arrives.
- Manual refresh: run one scan immediately, then reset the timer.
- Suspended or locked session: continue polling at 10 seconds until unlock.

The first CPU sample after launch is allowed to show `warming up` for one interval because CPU percentages require a delta between two samples.

### FR-7: Icon Visibility Onboarding

Windows 11 may put newly created tray icons in the hidden icon menu. The app cannot rely on code to force permanent taskbar visibility. On first launch, it must show a non-intrusive notification and an `Open taskbar settings` menu item.

First-run text:

`AI Tools Monitor is running. If you do not see the icon, open the hidden icons arrow and drag it beside the clock.`

The context menu command opens:

`ms-settings:taskbar`

### FR-8: Configuration

Store user config at:

`%APPDATA%\AI Tools Monitor\config.json`

Required config fields:

- `pollIntervalMs`: default `2500`
- `activeCpuThresholdPercent`: default `3.0`
- `tools`: array of five tool profiles with `displayName`, `matchTerms`, and `excludeTerms`
- `startAtLogin`: default `false`
- `theme`: `system`, `light`, or `dark`; default `system`

### FR-9: Error Handling

If a process cannot be inspected due to access denial or exit-race timing, skip it for that scan and record the skipped count in diagnostics. Do not show an error popup during normal monitoring.

### FR-10: Diagnostics

The app must provide a diagnostics export command that writes a timestamped markdown file under:

`%APPDATA%\AI Tools Monitor\diagnostics`

The export includes matched process rows, skipped process count, monitor resource usage, config path, app version, Windows build, and .NET runtime version.

## Acceptance Criteria

- Launching the app creates one notification-area icon.
- Left-click opens a popup within 150 ms when the process snapshot is already available.
- Right-click opens a menu without blocking polling.
- Starting `claude`, `codex`, `opencode`, `hermes`, or `antigravity` changes that row from `Idle` to `Running` within one polling interval.
- Exiting the CLI changes the row to `Idle` within two polling intervals.
- The app survives short-lived processes exiting during enumeration.
- The app keeps monitoring after Explorer restarts and recreates its tray icon.

## Sources

- pystray usage docs: https://pystray.readthedocs.io/en/latest/usage.html
- Microsoft NotifyIcon Click event: https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.notifyicon.click?view=windowsdesktop-10.0
- Microsoft NotifyIcon ContextMenuStrip property: https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.notifyicon.contextmenustrip?view=windowsdesktop-10.0
- Docker Desktop tray menu docs: https://docs.docker.com/desktop/use-desktop/
- 1Password Quick Access tray docs: https://support.1password.com/quick-access/
- Windows taskbar notification-area settings: https://support.microsoft.com/en-us/windows/experience/personalization/customize-the-taskbar-in-windows

