# Edge-Docked Peek-Panel Sidebar: Research & Design Recommendations

Research snapshot: 2026-07-30

## 1. Real-World Implementations Studied

### 1.1 SideBar (macOS — oidd/SideBar, GitHub)
**Source:** https://github.com/oidd/SideBar (72 stars)

The most directly relevant open-source implementation. SideBar brings edge-snap
and hover-reveal behavior to third-party macOS windows.

Key interaction model:
1. Drag a window to the left or right edge of the screen.
2. SideBar captures it into a snapped state and moves it just outside the visible area.
3. **Hover the screen edge to reveal it.**
4. **Leave the safe region to collapse it again.**
5. Dock click or per-app shortcut toggles it explicitly.

Architecture highlights:
- Uses **invisible non-activating edge indicator windows** to detect hover intent
  without stealing focus — critical design detail.
- Per-window state machine (snap → expand → collapse) with consistent transitions.
- Moves windows physically outside screen bounds rather than toggling visibility.
- Explicit focus handoff and geometry recovery.
- **"Treats hover detection, collapse timing, and cleanup as first-class runtime
  concerns rather than UI-only behavior."**

Takeaway: Hover is the primary reveal trigger; collapse on mouse-leave from a
safe region. The tab itself must be non-activating to avoid focus-stealing bugs.

---

### 1.2 Bartender 6 (macOS)
**Source:** https://www.macbartender.com/

A mature menu bar management tool (10+ years, award-winning). Bartender stashes
menu bar items behind a reveal trigger.

Interaction model:
- **Multiple trigger options:** swipe, scroll, click, or hover on the menu bar
  reveal icon.
- Hidden items slide out instantly beneath the menu bar.
- Items auto-hide when focus leaves.
- Supports a "Bartender Bar" — a dedicated stashing zone beneath the menu bar.

Takeaway: Offering multiple triggers (click OR hover) is the mature pattern.
Bartender defaults to click but lets users choose hover for fastest access.
The "instant reveal" timing is important — no perceptible delay.

---

### 1.3 Windows 11 Widgets Panel
**Source:** Microsoft documentation + user reports (Reddit r/Windows11)

Windows 11 Widgets slide in from the taskbar edge (left, later moved to right
near the weather icon).

Behavior:
- Click the Widgets icon on the taskbar → panel slides in from the edge.
- Panel is a full-height flyout that covers part of the desktop.
- Dismissed by clicking outside, pressing Escape, or clicking the icon again.
- Does not use hover-to-reveal (too many accidental activations on desktop).
- The panel is always-on-top but non-activating for other windows.

Takeaway: Click is preferred over hover for desktop panels (hover causes too
many accidental activations in desktop contexts). But a small peek tab with
hover is different from a full panel — the risk of accidental activation is
lower with a narrow tab.

---

### 1.4 macOS Notification Center / Control Center
**Source:** Apple HIG Gestures (https://developer.apple.com/design/human-interface-guidelines/gestures)

Apple's HIG states: "This specific motion [edge swiping] is reserved for
revealing system overlays. Since system overlays always display on top of app
content..."

Behavior:
- On iPhone/iPad: swipe from right edge reveals Notification Center.
- On macOS: swipe from right edge of trackpad reveals Notification Center
  (on older macOS versions) or use the date/time click.
- System overlays always display on top.

Takeaway: Edge-swipe gestures work well for touch/trackpad but are not
applicable to a mouse-driven WinForms desktop app. Click or hover are the
appropriate triggers.

---

### 1.5 Gaming Overlays (NVIDIA GeForce Experience, Xbox Game Bar)
**Source:** NVIDIA support docs, Microsoft Xbox Game Bar docs

Gaming overlays take a fundamentally different approach:
- Triggered by **keyboard shortcut** (Alt+Z for GeForce, Win+G for Xbox).
- Full-screen overlay that dims the background.
- No edge tab — the overlay appears as a centered/modal UI.
- Xbox Game Bar has detachable widgets that can be positioned freely.

Takeaway: Not directly applicable. Gaming overlays are modal/centered and
keyboard-triggered. Our use case is a persistent non-modal glanceable panel.

---

### 1.6 ShelfDesk (Windows, Microsoft Store)
**Source:** apps.microsoft.com/detail/9nfc2dkpqdlj

A Windows dock that uses the AppBar API to reserve screen real estate:
"Through the Windows AppBar API, ShelfDesk reserves screen real estate, so
when you maximise any application, it lays out beside the dock instead of
under it."

Takeaway: The AppBar approach is heavyweight and changes window layout. For
a peek panel, we want **TopMost + borderless** without AppBar — we don't
want to resize other windows.

---

## 2. Interaction Design Analysis

### Trigger: Click vs. Hover

| Factor | Click | Hover |
|--------|-------|-------|
| Accidental activation risk | Low — requires deliberate action | Medium — cursor sweep can trigger |
| Speed of access | Fast (one click) | Faster (no click needed) |
| Dismissal | Click elsewhere or explicit close | Mouse-leave (natural) |
| Desktop context fit | Standard for desktop tray apps | Works well for narrow peek tabs |
| Precedent in our app | StatusPopup uses click | N/A |

**Recommendation: CLICK is the primary trigger** for the edge tab, with a
**300ms hover pre-highlight** (visual feedback only, no slide-out). This
follows the Windows 11 Widgets pattern and avoids accidental activation on
desktop. The click-to-slide-out pattern is also what the existing StatusPopup
already uses (tray icon click), maintaining consistency.

However, we add a **hover highlight** on the tab itself (opacity/color change)
to signal interactivity, following Bartender's approach of giving hover
feedback before the user commits to clicking.

### Retract / Dismiss Behavior

Based on research:
- **SideBar:** collapse when mouse leaves the "safe region" (expanded panel
  bounds + small margin).
- **Bartender:** auto-hide when focus leaves.
- **Windows 11 Widgets:** click outside or press Escape.

**Recommendation: Lose-focus retract with mouse-leave guard.**
- Retract on `Deactivate` event (same as existing StatusPopup).
- Retract on mouse-leave from the entire panel (tab + expanded panel) with a
  400ms grace period to let the mouse cross from tab to panel content.
- Retract on Escape key.

### Animation

- **Slide-out:** 200ms ease-out, translating from off-screen right to docked
  position. No spring/overshoot — keep it utility-quick.
- **Slide-in (retract):** 150ms ease-in, translating back off-screen.
- WinForms doesn't have built-in animation, so use a `System.Windows.Forms.Timer`
  with ~16ms interval (60fps) to interpolate position.

### Visual Treatment of the Tab

The tab should be:
- **28px wide × 120px tall**, vertically centered on the primary screen's
  right edge.
- Shows a small left-pointing chevron (◀) or vertical "AI" text when collapsed.
- Uses the app's existing pink/white glassmorphism theme (see ThemeSettings
  in StatusPopup.cs).
- Has a subtle **glow/highlight** on hover (opacity change from 0.85 to 1.0
  and slight width increase from 28px to 32px).
- When tools are active, the tab shows a small green status dot in its center
  (same as the tray icon's running indicator).

---

## 3. Concrete Design Specification

### State Machine

```
[Collapsed] --click tab--> [Sliding Out] --animation done--> [Expanded]
[Expanded] --mouse-leave (400ms grace)--> [Sliding In] --done--> [Collapsed]
[Expanded] --Deactivate/focus loss--> [Sliding In] --done--> [Collapsed]
[Expanded] --Escape--> [Sliding In] --done--> [Collapsed]
[Collapsed] --mouse hover tab--> [Hovered] (visual highlight only)
[Hovered] --mouse leave tab--> [Collapsed]
[Hovered] --click tab--> [Sliding Out] --> [Expanded]
```

### Dimensions

| Element | Size |
|---------|------|
| Tab width | 28px (32px on hover) |
| Tab height | 120px |
| Tab position | Right edge, vertically centered |
| Expanded panel width | 350px (same as StatusPopup) |
| Expanded panel height | Full working area height |
| Expanded panel position | Right edge, top of working area |

### Timing

| Parameter | Value |
|-----------|-------|
| Slide-out duration | 200ms |
| Slide-in duration | 150ms |
| Hover pre-highlight delay | 300ms (visual only) |
| Mouse-leave grace period | 400ms |
| Timer interval (animation) | 16ms (~60fps) |

### Theme Colors (from StatusPopup.cs)

The tab and panel reuse StatusPopup's existing ThemeSettings / color system:
- Light: surface #FFF5F8, accent pink #EB4B82, text #2D1423
- Dark: surface #1C141A, accent pink #F56EA0, text #FAEBF2
- High contrast: system colors

### Content

The expanded panel shows the **exact same** content as StatusPopup:
- Header with "AI Tools Monitor" title
- Tool cards with status dots, CPU, RAM, quota progress bars
- Footer with last-updated time and running count
- Today's token/cost summary

It receives the same `StatusSnapshot` object that TrayHost's `Poll()` already
produces — no duplication of quota-fetching logic.

---

## 4. Sources

| Source | URL | Key Insight |
|--------|-----|-------------|
| SideBar (macOS) | https://github.com/oidd/SideBar | Hover reveal with non-activating edge indicators, safe-region collapse |
| Bartender 6 | https://www.macbartender.com/ | Multi-trigger reveal (click/hover/scroll), instant show, auto-hide |
| Windows 11 Widgets | Reddit r/Windows11, Microsoft docs | Click trigger preferred for desktop, edge-slide animation |
| Apple HIG Gestures | https://developer.apple.com/design/human-interface-guidelines/gestures | Edge swipe reserved for system overlays; swipe = reveal pattern |
| ShelfDesk | apps.microsoft.com/detail/9nfc2dkpqdlj | AppBar API reserves space (too heavyweight for peek panel) |
| TrafficMonitor | https://github.com/zhongyang219/TrafficMonitor | Lightweight Windows tray utility precedent |
| NVIDIA/Xbox overlays | nvidia.com, Microsoft docs | Keyboard-triggered modal overlays (different pattern) |
