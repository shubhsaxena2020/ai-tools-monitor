# AI Tools Monitor Deployment

Research snapshot: 2026-07-29

## Deployment Goal

This is a solo-developer utility for the user's own Windows 11 laptop. Do not build a public installer pipeline for the first version. Use a simple local install script that copies a published app folder into the current user's profile and optionally registers startup at logon.

## Recommended Install Model

Use a zip/script install:

- Publish the app into `publish\AI.Tools.Monitor`.
- Copy files to `%LOCALAPPDATA%\Programs\AI Tools Monitor`.
- Store config in `%APPDATA%\AI Tools Monitor`.
- Register startup under HKCU only when enabled.
- Update by replacing the install folder with a new publish folder.

This avoids admin rights, MSI/MSIX complexity, signing requirements for a private utility, and public update infrastructure.

## Publish Commands

Framework-dependent build for the developer laptop:

```powershell
dotnet publish src\AiToolsMonitor\AiToolsMonitor.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish\AI.Tools.Monitor
```

Self-contained build for a Windows 11 machine without the .NET Desktop Runtime:

```powershell
dotnet publish src\AiToolsMonitor\AiToolsMonitor.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish\AI.Tools.Monitor
```

## Install Location

Install files:

`%LOCALAPPDATA%\Programs\AI Tools Monitor`

Config:

`%APPDATA%\AI Tools Monitor\config.json`

Diagnostics:

`%APPDATA%\AI Tools Monitor\diagnostics`

## Startup Registration

Use the current-user Run key:

`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`

Value name:

`AI Tools Monitor`

Value data:

`"%LOCALAPPDATA%\Programs\AI Tools Monitor\AiToolsMonitor.exe"`

Microsoft documents Run keys as a way to run a program each time the user logs on. Use HKCU, not HKLM, so the app does not require admin rights and affects only the developer's account.

## Install Script Behavior

`scripts/install-user.ps1` should:

1. Stop a running instance politely by sending an app exit command if implemented; otherwise ask the user to exit from the tray menu.
2. Create `%LOCALAPPDATA%\Programs\AI Tools Monitor`.
3. Copy published files into the install folder.
4. Create `%APPDATA%\AI Tools Monitor`.
5. Launch the app.
6. Print the taskbar pinning instruction:

```text
If the icon is hidden, open the hidden icons arrow beside the clock and drag AI Tools Monitor onto the taskbar corner.
```

If called with `-StartAtLogin`, it also writes the HKCU Run value.

## Uninstall Script Behavior

`scripts/uninstall-user.ps1` should:

1. Remove the HKCU Run value if present.
2. Ask the user to exit the app from the tray menu if it is running.
3. Remove `%LOCALAPPDATA%\Programs\AI Tools Monitor`.
4. Leave `%APPDATA%\AI Tools Monitor` in place by default so config and diagnostics are not lost.
5. Support `-RemoveUserData` to remove config and diagnostics.

## Update Strategy

For a solo dev:

1. Build a new release locally.
2. Exit AI Tools Monitor from the tray menu.
3. Run `scripts/install-user.ps1`.
4. Start the app.
5. Export diagnostics if detection behavior changed.

No auto-updater in the MVP. An auto-updater adds background network behavior, signing considerations, and failure modes that are not justified for a private machine utility.

## Versioning

Use semantic versions in assembly metadata:

- `0.1.0`: first working tray monitor
- `0.2.0`: config UI or diagnostics improvements
- `1.0.0`: stable daily-use release after at least one week of clean startup and monitoring on the laptop

Show version in diagnostics export and the right-click `About` entry if that entry is added.

## Tray Visibility Setup

Windows 11 may place the icon in the hidden overflow area. After first launch:

1. Click the hidden icons arrow near the clock.
2. Find AI Tools Monitor.
3. Drag it beside the clock.
4. Left-click it to open the popup.

The app should also include `Open taskbar settings` in the right-click menu.

## Installer Decision

Do not build MSI, MSIX, Chocolatey, or winget packaging for the MVP.

Reconsider an installer only if:

- the app is used on more than one machine,
- code signing is available,
- automatic updates become necessary,
- nontechnical users need installation.

## Sources

- .NET single-file deployment: https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview
- .NET publishing overview: https://learn.microsoft.com/en-us/dotnet/core/deploying/
- Microsoft Run and RunOnce registry keys: https://learn.microsoft.com/en-us/windows/win32/setupapi/run-and-runonce-registry-keys
- Microsoft startup apps settings: https://support.microsoft.com/en-us/windows/experience/startup-boot/configure-startup-applications-in-windows
- Microsoft taskbar notification-area customization: https://support.microsoft.com/en-us/windows/experience/personalization/customize-the-taskbar-in-windows

