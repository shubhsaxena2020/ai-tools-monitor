# AI Tools Monitor UI/UX Spec

Research snapshot: 2026-07-29

## UX Positioning

AI Tools Monitor is a compact operational dashboard, not a landing page and not a full desktop app. It should feel like Docker Desktop's tray access pattern or 1Password's Quick Access pattern: always nearby, fast to open, and easy to dismiss.

## Primary Interaction

Left-click the tray icon to open the status popup. Right-click opens the command menu.

This corrects the prototype confusion. A user should not need to discover that the real app is hidden behind right-click. The right-click menu is for secondary commands, not the main dashboard.

## Popup Behavior

- Opens above the taskbar near the tray icon.
- Stays within the current monitor work area.
- Closes on Escape.
- Closes when the user clicks outside it.
- Does not steal focus permanently.
- Reopens in the same position unless the taskbar or monitor layout changed.
- Does not show in Alt+Tab.
- Does not create a taskbar button.

## Popup Layout

Recommended dimensions:

- Width: 420 px
- Minimum height: 292 px
- Maximum height: 360 px
- Corner radius: 8 px
- Outer padding: 14 px
- Row height: 42 px

Layout:

```text
AI Tools Monitor                      [refresh] [settings]
3 running                             Updated 14:32:08

[dot] Claude Code             Active     18.4%    1.3 GB
[dot] Hermes Agent            Idle        -       -
[dot] OpenAI Codex CLI        Quiet       0.6%    422 MB
[dot] OpenCode                Idle        -       -
[dot] Google Antigravity CLI  Active      7.2%    788 MB

Monitor: 0.3% CPU, 54 MB RAM
```

Do not include instructions inside the popup. The popup is a status surface.

## Row Behavior

Each tool row shows:

- status dot
- display name
- status text
- CPU percent
- RAM usage

Hovering a row shows a tooltip with:

- process count
- process IDs
- primary command line
- last matched term

Rows are not clickable in the first version. Opening terminals, killing processes, or attaching debuggers are future features and should not be implied.

## Tray Tooltip

Idle tooltip:

`AI Tools Monitor: idle`

Running tooltip examples:

- `AI Tools Monitor: 1 running - Claude Code`
- `AI Tools Monitor: 3 running - Claude Code, Codex, Antigravity`
- `AI Tools Monitor: 5 running - all tools active`

Keep the tooltip short because notification-area tooltips are constrained.

## Context Menu

Right-click menu:

```text
Open
Refresh now
Open config
Open diagnostics folder
Open taskbar settings
Start at login  [checked/unchecked]
Exit
```

Destructive actions are excluded. The monitor should not terminate an AI agent in the MVP.

## First-Run Tray Visibility

Windows 11 can hide new tray icons in the overflow area. The first run must show a notification:

`AI Tools Monitor is running. If you do not see the icon, open the hidden icons arrow and drag it beside the clock.`

The context menu's `Open taskbar settings` command opens `ms-settings:taskbar`.

## Accessibility

- All text uses Segoe UI Variable or Segoe UI.
- Minimum body text size is 12 px.
- Status is not color-only: each row includes text.
- Tooltips expose process detail for keyboard and mouse users.
- The popup supports Escape to dismiss.
- High contrast mode falls back to system colors where possible.

## Empty State

When all tools are idle, the popup still shows all five rows. Do not replace the table with an empty-state message. The user wants at-a-glance confirmation that no agents are running.

## Prior Art Used

- Docker Desktop uses a tray icon for quick access to Docker status and commands.
- 1Password Quick Access is a compact popup-like access surface and supports selecting the tray icon to open it.
- TrafficMonitor and Tray Monitor show that lightweight Windows resource utilities often live directly in the tray/taskbar and favor dense live metrics over large windows.

## Sources

- Docker Desktop tray menu docs: https://docs.docker.com/desktop/use-desktop/
- 1Password Quick Access docs: https://support.1password.com/quick-access/
- Microsoft notification area overview: https://learn.microsoft.com/en-us/windows/win32/shell/notification-area
- Windows 11 design principles: https://learn.microsoft.com/en-us/windows/apps/design/design-principles
- Windows typography guidance: https://learn.microsoft.com/en-us/windows/apps/design/signature-experiences/typography
- TrafficMonitor GitHub: https://github.com/zhongyang219/TrafficMonitor/blob/master/README_en-us.md
- Tray Monitor GitHub: https://github.com/strayge/tray-monitor

