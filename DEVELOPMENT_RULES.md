# AI Tools Monitor Development Rules

## Product Rules

- The app observes processes. It does not stop, restart, or mutate AI agent sessions in the MVP.
- One tray icon only.
- Left-click opens the dashboard popup.
- Right-click opens the command menu.
- The app must run without admin rights.
- The app must not send telemetry or make network calls.
- The app must stay useful when all tools are idle.

## Code Rules

- Keep classes small and named by responsibility.
- Put monitoring logic under `Monitoring`.
- Put all tray-specific code under `Tray`.
- Put popup drawing and theme handling under `Popup`.
- Keep process detection pure enough to test with fake process records.
- Never bind UI directly to mutable poller state.
- Use immutable records for snapshots.
- Use cancellation tokens for background loops.
- Catch expected process-enumeration exceptions at the smallest useful boundary.
- Log skipped process counts to diagnostics, not modal popups.

## C# Conventions

- Enable nullable reference types.
- Use file-scoped namespaces.
- Prefer `record` or `record struct` for data transfer shapes.
- Prefer explicit names over abbreviations.
- Use `DateTimeOffset` for sample timestamps.
- Use bytes internally for memory and format only at the UI boundary.
- Use percent as `double`, rounded only for display.
- Keep public methods small enough to test through behavior.

## UI Rules

- Use system theme by default.
- Keep popup body text at 12 px or larger.
- Do not use color as the only status signal.
- Do not add marketing text to the popup.
- Do not add large hero-style headings.
- Do not put cards inside cards.
- Do not show configuration text in the main popup.

## Monitoring Rules

- Default poll interval is 2500 ms.
- CPU is calculated from cumulative CPU deltas, not blocking per-process samples.
- The first CPU interval is treated as warming up.
- Match a tool only when the score reaches 60 or higher.
- Include child processes under matched roots.
- Skip inaccessible or exited processes quietly.
- Keep scan duration visible in diagnostics.

## Configuration Rules

- User config lives under `%APPDATA%\AI Tools Monitor\config.json`.
- App install files live under `%LOCALAPPDATA%\Programs\AI Tools Monitor`.
- Generated diagnostics live under `%APPDATA%\AI Tools Monitor\diagnostics`.
- Config load failure creates a backup of the invalid file and recreates defaults.
- The app never overwrites user-edited match terms unless the user deletes the config file.

## Test Rules

- Every detection rule gets a unit test with representative Windows command lines.
- Every false-positive exclusion gets a unit test.
- Aggregation tests must include parent-child process trees.
- UI click behavior must be manually verified on Windows 11 before declaring a release build ready.
- Performance tests must report monitor CPU and RAM, not only target tool CPU and RAM.

## Source Control Rules

- Commit docs separately from implementation.
- Commit scaffolding separately from monitoring logic.
- Commit UI behavior separately from packaging.
- Do not mix unrelated formatting changes into feature commits.

## Sources

- Microsoft NotifyIcon docs: https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.notifyicon?view=windowsdesktop-10.0
- psutil process iteration docs used for detector philosophy: https://psutil.readthedocs.io/
- Windows design principles: https://learn.microsoft.com/en-us/windows/apps/design/design-principles
- Microsoft Run and RunOnce registry keys: https://learn.microsoft.com/en-us/windows/win32/setupapi/run-and-runonce-registry-keys
