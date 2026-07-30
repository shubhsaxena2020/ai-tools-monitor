# Edge Sidebar Implementation Notes

Date: 2026-07-30
Branch: feature-edge-sidebar

## What Was Built

### New file: `src/AiToolsMonitor/Shell/EdgeSidebarTab.cs`

A single-file edge-docked peek sidebar (~830 lines) comprising:

1. **EdgeSidebarTab** (sealed Form) — the main control:
   - Borderless, always-on-top, no taskbar entry (`WsExToolWindow | WsExTopmost | WsExNoActivate`).
   - Docked to the right edge of the primary screen, vertically centered.
   - Collapsed state: 28×120px tab with a left-pointing chevron (◀) and optional green
     status-dot when tools are running.
   - Expanded state: 350px-wide panel showing the same tool-status content as StatusPopup.
   - Animated slide-out/slide-in via a 16ms-interval Timer with cubic easing.
   - Retracts on Deactivate, Escape key, or mouse-leave (400ms grace period).

2. **GlassSidePanel** (internal Panel) — glass-morphism card for each tool row,
   matching StatusPopup's existing card style.

3. **MiniProgressBar** (internal Control) — compact quota progress bar, matching
   the existing QuotaProgressBar's color logic and freshness states.

### Modified file: `src/AiToolsMonitor/Tray/TrayHost.cs`

- Added `using AiToolsMonitor.Shell;`
- Added `EdgeSidebarTab _edgeSidebar` field, initialized in constructor.
- In `Poll()`: `_edgeSidebar.Render(snapshot)` and `_edgeSidebar.UpdateTodaySummary(...)` called
  alongside the existing `_popup.Render(...)` / `_popup.UpdateTodaySummary(...)` — same snapshot,
  zero duplication of quota-fetching logic.
- In `Dispose()`: `_edgeSidebar.Dispose()` called to clean up timers.

## Key Interaction-Design Decisions

### 1. Click trigger, not hover (tied to research)

Research found that hover-to-reveal works well for macOS edge-stash tools (SideBar on
GitHub) but causes accidental activation on Windows desktop where cursors sweep freely.
Windows 11 Widgets uses click. Bartender 6 offers both but defaults to click.

**Decision:** Click the tab to expand. Hover provides visual feedback only (opacity change +
width increase from 28→32px + chevron color change to pink accent). This matches the existing
StatusPopup click-to-open pattern and avoids accidental activations.

Source: EDGE_SIDEBAR_RESEARCH.md §1.1 (SideBar), §1.3 (Windows 11 Widgets), §1.2 (Bartender).

### 2. Lose-focus retract with 400ms mouse-leave grace

Research from SideBar (macOS) shows the "safe region" pattern: collapse when mouse leaves
the panel bounds plus a small margin. StatusPopup already uses Deactivate for its retract.

**Decision:** Dual retract triggers:
- `Deactivate` event → immediate collapse (user clicked elsewhere).
- Mouse-leave from panel → 400ms grace timer, then collapse if mouse doesn't return.
  The grace period lets the cursor cross from the tab to the panel content without
  triggering a retract.

Source: EDGE_SIDEBAR_RESEARCH.md §1.1 (SideBar safe-region), §3.

### 3. Animation: 200ms expand, 150ms collapse, cubic easing

Research shows Bartender uses "instant" reveal while SideBar uses smooth transitions.
For a utility panel, fast but visible animation is better than instant (gives spatial
orientation) or slow (feels sluggish).

**Decision:** 200ms cubic ease-out for expand (feels snappy), 150ms cubic ease-in for
collapse (feels dismissive). 16ms timer interval (~60fps). Right edge stays pinned to
screen edge during animation — the panel grows leftward and downward.

Source: EDGE_SIDEBAR_RESEARCH.md §3.

### 4. Single-form architecture (not two overlapping forms)

Evaluated two approaches:
- Two forms (tab + panel) — complex coordination, Z-order issues, focus conflicts.
- Single form with animated Size/Location — right edge pinned, grows/shrinks leftward.

**Decision:** Single form. The form is always the full expanded size when open; the right
edge is always pinned to `Screen.PrimaryScreen.WorkingArea.Right`. During animation, both
Width/Height/Location interpolate simultaneously. This avoids multi-form coordination
complexity.

### 5. Non-activating form (WsExNoActivate)

Following SideBar's pattern of "invisible non-activating edge indicators to detect hover
intent without stealing focus," the sidebar tab uses `WsExNoActivate` to avoid disrupting
the user's active window when hovering the tab.

Source: EDGE_SIDEBAR_RESEARCH.md §1.1.

### 6. Theme: reuse StatusPopup's color system exactly

The sidebar reads the same registry keys (`AppsUseLightTheme`, `EnableTransparency`) and
applies the same light/dark/high-contrast color palettes as StatusPopup. The pink
glassmorphism accent colors (`#EB4B82` light, `#F56EA0` dark) and card styling are
identical.

Source: THEME.md, StatusPopup.cs `ThemeSettings` / `ApplyThemeColors()`.

## Files Changed

| File | Change |
|------|--------|
| `src/AiToolsMonitor/Shell/EdgeSidebarTab.cs` | NEW — entire sidebar implementation |
| `src/AiToolsMonitor/Tray/TrayHost.cs` | Modified — added using, field, init, render calls, dispose |

## Build & Test Result

```
Build:    0 errors, 0 new warnings (17 pre-existing warnings in UsageHistoryForm.cs)
Tests:    88 passed, 0 failed, 0 skipped
Duration: 2.15s build, 16s tests
```

## What Was NOT Changed

- No modifications to `HistoryDatabase.cs` or any `Monitoring/*` quota clients.
- No modifications to `StatusPopup.cs` — it remains the tray-click popup as before.
- No new NuGet packages added.
- No files outside `src/AiToolsMonitor/` were touched.
