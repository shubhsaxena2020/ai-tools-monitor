# AI Tools Monitor Project Brief

Research snapshot: 2026-07-29

## Summary

AI Tools Monitor is a Windows 11 system tray application for a solo developer who runs multiple local AI CLI tools and wants immediate visibility into which ones are still alive and consuming CPU or RAM.

The app monitors these five tools:

- Claude Code
- Hermes Agent
- OpenAI Codex CLI
- OpenCode
- Google Antigravity CLI

The current prototype proves that process command-line scanning works, but it also exposed two UX problems: pystray opens its normal Windows menu on right-click, and Windows 11 can place new notification-area icons in the hidden overflow area. The production app should be built as a native Windows tray app so the click behavior, popup positioning, lifecycle, and theme handling are under project control.

## Target User

The primary user is a solo Windows developer working inside terminals and editor-integrated shells. They may leave several AI agent CLIs open across projects, worktrees, and terminals. Their pain is not managing the tools; it is answering a simple question without opening Task Manager:

"Which AI agents are currently running, and how much of my laptop are they using?"

## Problem

Modern AI CLI tools often run as terminal user interfaces, spawned binaries, Python processes, Node launchers, or child processes. None of the five target tools exposes a stable local status API suitable for a lightweight tray monitor. The practical signal is the Windows process table: process name, command line, parent-child relationships, CPU deltas, and working-set memory.

The tray app must itself stay small. A monitor that burns noticeable memory or CPU defeats its purpose.

## Product Decision

Build the production app as a native Windows desktop utility using C# and WinForms NotifyIcon, with a small custom popup window. Do not continue the production version with Python plus pystray.

Rationale:

- Microsoft supports notification-area icons through native Windows APIs and the WinForms NotifyIcon wrapper.
- WinForms exposes Click, MouseClick, ContextMenuStrip, and lifecycle behavior directly.
- pystray documents that its popup menu is shown on right-click on Windows, with left-click limited to activating a default item.
- Electron and Tauri can build tray apps, but they add a webview/frontend stack that is unnecessary for a five-row local monitor.
- Windows 11 tray icon visibility is ultimately user controlled; the app must include first-run instructions for pinning the icon from the overflow area.

## Non-Goals

- No public cloud service.
- No telemetry.
- No account system.
- No process control in the first build. The app should observe, not kill or restart agents.
- No attempt to infer the semantic state of an agent conversation. "Running" means a matching local process exists.
- No cross-platform support in the first build.

## Success Criteria

- The tray icon appears in the notification area or hidden overflow within 2 seconds of launch.
- Left-click opens a compact live status popup. Right-click opens a command menu.
- The tray icon has two top-level states: idle when no target tool is running, and running when at least one target tool is running.
- The popup lists all five tools with status, aggregate CPU percent, aggregate RAM, process count, and last detected command.
- Resource sampling adds less than 1 percent CPU on average while idle on the developer laptop.
- App private memory remains below 80 MB during normal idle monitoring.
- Detection handles npm/native binary installs for Claude Code, Codex CLI, OpenCode, Hermes Agent, and Antigravity CLI.
- The app installs for the current Windows user without admin rights and can start at logon.

## Sources

- Microsoft notification area overview: https://learn.microsoft.com/en-us/windows/win32/shell/notification-area
- Microsoft WinForms NotifyIcon docs: https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.notifyicon?view=windowsdesktop-10.0
- pystray tray menu behavior: https://pystray.readthedocs.io/en/latest/usage.html
- Microsoft taskbar notification-area customization: https://support.microsoft.com/en-us/windows/experience/personalization/customize-the-taskbar-in-windows
- Docker Desktop tray menu pattern: https://docs.docker.com/desktop/use-desktop/
- 1Password Quick Access tray behavior: https://support.1password.com/quick-access/

