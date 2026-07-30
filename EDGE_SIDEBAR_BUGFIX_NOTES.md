# Edge Sidebar Bug-Fix Notes

## Scope

This investigation used the build output from:

`C:\Users\shubh\Projects\ai-tools-monitor-wt-sidebarfix\src\AiToolsMonitor\bin\Debug\net9.0-windows\AiToolsMonitor.exe`

An AiToolsMonitor process from another worktree was running during the
investigation. It was not stopped or modified. A temporary diagnostic mutex
name was used to run this worktree's executable concurrently; that diagnostic
change was removed before the final build, test, and diff audit.

## Repository discrepancy

The worktree started clean at commit `86ef9f4`. Contrary to the task context,
`TrayHost.cs` in this commit constructed `EdgeSidebarTab` but did not call
`Show()`, and no local ref contained `_edgeSidebar.Show();`.

The already-approved visibility fix was therefore restored:

```csharp
_edgeSidebar = new EdgeSidebarTab();
_edgeSidebar.Show();
```

## Investigation and root cause

The reported post-click rectangle was:

`(318,885)-(477,912)`, or `159x27`.

That rectangle cannot be produced by `ApplyAnimationFrame`: the sidebar's
logical dimensions are always between `28x120` and `350x1140` on this display.
The corresponding DPI-virtualized Win32 rectangles are between `22x96` and
`280x912`.

The full `Shell`, `Tray`, and `Popup` source search found:

- No external `Location`, `Size`, `Bounds`, or `SetBounds` write targeting
  `EdgeSidebarTab`.
- `MainShellForm.PrepareEmbeddedForm` is never called with `EdgeSidebarTab`.
- The popup `ThemeSettings` type is an immutable value and has no shared
  mutable form or bounds state.
- No display-settings, user-preference, resize, DPI, or theme event handler
  changes the sidebar bounds.

Temporary `SetBoundsCore`, handle-lifecycle, mouse, and animation tracing then
captured the real source of every bounds request. The sidebar handle remained
`0x3D06B4`; every animated bounds stack originated in
`EdgeSidebarTab.ApplyAnimationFrame` via `OnAnimationTick`. No unexpected
bounds writer or handle recreation occurred.

Before the identity fix, a real click on that verified handle produced:

- Collapsed: `(1514,408)-(1536,504)` (`22x96`)
- Intermediate: `(1374,186)-(1536,726)` (`162x540`)
- Expanded: `(1256,0)-(1536,912)` (`280x912`)

Every sampled frame remained pinned to `Right = 1536`.

The live desktop did contain an unrelated window with the exact reported
`159x27` size:

- Process: `dwm.exe` (PID `12700` at capture time)
- Handle: `0x10040` at capture time
- Class: `Dwm`
- Title: `DWM Notification Window`
- Captured rectangle: `(0,933)-(159,960)` (`159x27`)

The sidebar itself had an empty title, as did several WinForms helper windows.
The evidence therefore identifies the second "jump" as native-window
misidentification, not sidebar geometry: the `159x27` rectangle belongs to a
different top-level window. The claimed rectangle was not reproducible when
PID, title, class, and handle were cross-checked together.

## Fix

`EdgeSidebarTab` now has the stable, non-visible native title:

```csharp
Text = "AI Tools Monitor Edge Sidebar";
```

The form remains borderless and absent from the taskbar, so this does not add
visible chrome. It gives `EnumWindows` diagnostics an unambiguous identity
alongside process ID, class, and handle. A regression test verifies the title.

No speculative animation, main-shell page-swapping, history, monitoring, or
theme changes were made.

## Re-verification

The final worktree build was launched and the sidebar was located by all four
identity fields:

- Process ID: `8852` at capture time
- Handle: `0x405D0`
- Class: `WindowsForms10.Window.8.app.0.1320344_r3_ad1`
- Title: `AI Tools Monitor Edge Sidebar`

Expand and collapse were simulated directly against that verified handle:

- Before click: `(1514,408)-(1536,504)` (`22x96`)
- After expand: `(1256,0)-(1536,912)` (`280x912`)
- After collapse: `(1514,408)-(1536,504)` (`22x96`)

Both final states remained pinned to `Right = 1536`; there was no jump to an
unrelated bottom-screen rectangle.

## Automated verification

The focused identity regression test was observed failing before the title was
added and passing afterward.

Final full-solution results:

- `dotnet build`: passed with 0 errors and 16 pre-existing nullable warnings
  in `UsageHistoryForm.cs`.
- `dotnet test`: passed, 89 total, 89 passed, 0 failed, 0 skipped.
