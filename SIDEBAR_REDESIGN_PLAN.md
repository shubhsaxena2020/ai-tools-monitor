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

## Status: DONE (2026-07-30) — live-verified, pushed

Both workstreams built, merged, and live-verified by actually launching the rebuilt app and
clicking through every page (not just a green build). Three real bugs were found this way and
fixed, plus one visual non-issue ruled out:

1. **EdgeSidebarTab never appeared at all** — the form was fully built but `Show()` was never
   called anywhere. Fixed directly; the code review had missed this because a hidden-but-real
   Win32 window vs. no window at all look identical from a code read.
2. **MainShellForm never became visible on tray click** — `ShowPage()` correctly called
   `Show()`, but the constructor's own premature `NavigateTo(ShellPage.Dashboard)` (populating
   the Dashboard page before the form's own handle existed) left the form in a state where
   `Show()` silently no-opped. Root-caused and fixed by Antigravity in an isolated worktree,
   with a new Win32-level regression test (`PopupTests.cs`) that asserts both `.Visible` and
   the real `IsWindowVisible` Win32 call — a plain `.Visible` assertion would not have caught
   the original bug.
3. **Crash on Analysis / Cost Report / Usage History pages** — `DataGridView.GridColor` was
   set to a semi-transparent palette color (`palette.CardBorder`) as part of the glassmorphism
   theme; WinForms throws `ArgumentException` for any non-opaque `GridColor`. Fixed by forcing
   alpha=255 only for that one property.
4. Not a bug: a page title that looked like "Settinas" instead of "Settings" in a screenshot
   turned out to be a font-rendering artifact at small size — the source string was already
   correct.

**Process note on my own mistake:** the first bugfix (`EdgeSidebarTab.Show()`) was made directly
in the main repo's working tree and not committed before two new bugfix worktrees were branched
from `HEAD` — both worktrees inherited the still-broken code and had to re-discover the same
missing-`Show()` bug independently before finding their own real (different) bugs. Lesson: commit
a hotfix before branching new work from the same commit, even mid-session.

All fixes independently rebuilt/retested by me (not just trusted from the delegate's report),
then live-clicked through every one of the 6 sidebar pages after the final merge. Final state:
90/90 tests passing, pushed to `github.com/shubhsaxena2020/ai-tools-monitor` at commit `16d1475`.

**Known minor follow-up, not blocking:** the edge sidebar's auto-retract-on-mouse-leave felt
noticeably slower than the documented 400ms grace period in real testing (it did eventually
retract, just not as snappy as designed) — worth a closer look later but not worth delaying
this delivery over.

## Follow-up (2026-07-30, same day): app was still not discoverable

Even with the redesign done, the app had never been registered with Windows at all — no Start
Menu entry, so Windows Search couldn't find it and there was no way to launch it except running
the dev build's exe directly or digging into the hidden tray-icon overflow. Fixed by:

- `dotnet publish -c Release -o publish` — a stable, non-Debug build path that survives future
  `dotnet build` runs without needing to be re-pointed.
- A real Start Menu shortcut (`AI Tools Monitor.lnk` in the user's Start Menu Programs folder)
  targeting that published exe.
- Verified via `Get-StartApps` (the same index Windows Search reads from) that the app now
  resolves under the name "AI Tools Monitor" — confirmed registered, not just assumed from
  creating the .lnk file.
