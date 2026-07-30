# AI Tools Monitor — Access Model Redesign Plan

> Started: 2026-07-30. Problem: the app is currently reachable only through the hidden-icons
> tray overflow, and every feature (Analysis, Cost report, Usage history, Budget) is buried
> behind a right-click context menu. There is no visible navigation between features once
> you find them. Not discoverable for a new user.

## Goal (from user's own words)

Turn this into something closer to a real app:
1. A main app window containing a sidebar with all dashboards/features, reachable without
   digging through a hidden-icons tray menu.
2. A slim edge-docked arrow/tab that stays on screen; clicking it slides out a quick-glance
   panel (the existing live-status view), without needing the full app window.
3. Both need to look and feel industry-level, not a bolted-on WinForms afterthought.

## Workstreams (delegated, isolated git worktrees)

1. **Main Shell** (`ai-tools-monitor-wt-shell`, branch `feature-main-shell`, delegated to Codex)
   — a `MainShellForm` with a persistent left nav sidebar (Dashboard / Analysis / Cost Report /
   Usage History / Budget / Settings) that swaps an embedded content panel instead of opening
   separate popup windows. Tray left-click now opens this window (or brings it to front);
   tray right-click menu keeps Quick launch / Recent Projects / Exit as fast paths only.

2. **Edge Sidebar** (`ai-tools-monitor-wt-sidebar`, branch `feature-edge-sidebar`, delegated to Hermes)
   — a small always-on-top arrow tab docked to a screen edge (configurable, default right edge,
   vertically centered) that slides out the existing `StatusPopup`-style quick view on click/hover,
   and retracts on focus loss — independent of whether the main shell window is open.

3. **Market/UX research** (delegated to Hermes, real web search) — current best practice for this
   exact pattern (dashboard app + edge-docked slide-out) in shipping desktop tools: Bartender/Ice
   (macOS menu bar), Rewind AI, Stats.app, CleanMyMac X, Windows 11 widgets edge panel, Raycast,
   Arc browser sidebar. Feed findings into workstream 1 and 2's visual/interaction design.

## What does NOT change

- All existing feature logic (HistoryDatabase, SessionAnalysisEngine, BudgetGuard, cost reports)
  is reused as-is inside the new shell — this is a navigation/shell redesign, not a rewrite of
  the underlying features.
- The tray icon and background polling stay exactly as they are.

## Verification

- Build + all existing tests still pass after merge.
- Launch the app for real, screenshot the new shell and the edge sidebar via computer-use,
  click through every sidebar nav item and confirm real data (not placeholders).
- No regression: right-click tray menu items (Quick launch, Recent Projects, Export) still work.
