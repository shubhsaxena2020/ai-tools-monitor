# AI Tools Monitor Theme

Research snapshot: 2026-07-29

## Theme Direction

Use a native Windows 11 utility look: calm surfaces, restrained status color, Segoe typography, and density suitable for repeated scanning.

The app follows the OS theme by default. User override values are `system`, `light`, and `dark`.

## Typography

Use:

- Primary font: `Segoe UI Variable`
- Fallback: `Segoe UI`
- Title: 14 px, semibold
- Subtitle and timestamp: 12 px
- Row label: 13 px
- Metrics: 13 px, tabular numeric style where available
- Footer: 12 px

Do not use display-sized headings in the popup. This is a compact utility.

## Color Tokens

### Light Theme

| Token | Value | Usage |
| --- | --- | --- |
| `surface` | `#FFFFFF` | Popup background |
| `surfaceAlt` | `#F7F7F7` | Hover row background |
| `border` | `#E5E5E5` | Popup and row dividers |
| `textPrimary` | `#1A1A1A` | Tool names and metrics |
| `textSecondary` | `#5F5F5F` | Timestamp, footer |
| `accent` | `#2563EB` | Running tray icon and active controls |
| `idle` | `#8A8A8A` | Idle dot |
| `quiet` | `#D97706` | Running but low CPU |
| `active` | `#16A34A` | Running and active |
| `warning` | `#DC2626` | Diagnostics warnings only |

### Dark Theme

| Token | Value | Usage |
| --- | --- | --- |
| `surface` | `#202020` | Popup background |
| `surfaceAlt` | `#2A2A2A` | Hover row background |
| `border` | `#3A3A3A` | Popup and row dividers |
| `textPrimary` | `#F3F3F3` | Tool names and metrics |
| `textSecondary` | `#B8B8B8` | Timestamp, footer |
| `accent` | `#60A5FA` | Running tray icon and active controls |
| `idle` | `#8C8C8C` | Idle dot |
| `quiet` | `#F59E0B` | Running but low CPU |
| `active` | `#22C55E` | Running and active |
| `warning` | `#F87171` | Diagnostics warnings only |

The palette intentionally avoids a single-hue look. Blue is used only for app accent, green for active, amber for quiet, gray for idle.

## Tray Icon

Use one icon with two base states:

- Idle: gray outline monitor glyph with a small hollow dot.
- Running: accent monitor glyph with a filled status dot.

Add a badge only for running count:

- `1` through `5`
- Badge appears at bottom-right
- Badge is hidden for idle

Do not create per-tool mini-icons. At 16 px, five different symbols would be unreadable.

Icon assets:

- `Assets/icon-idle.ico`
- `Assets/icon-running-1.ico`
- `Assets/icon-running-2.ico`
- `Assets/icon-running-3.ico`
- `Assets/icon-running-4.ico`
- `Assets/icon-running-5.ico`

Each `.ico` must contain 16, 20, 24, 32, 48, and 256 px sizes for DPI scaling.

## Popup Styling

- Background uses `surface`.
- Border uses `border`.
- Shadow uses native form shadow where available; otherwise use a 1 px border only.
- Row hover uses `surfaceAlt`.
- No gradients.
- No decorative imagery.
- No nested cards.

## Status Dots

| Status | Dot | Text |
| --- | --- | --- |
| Idle | gray hollow circle | `Idle` |
| Quiet | amber filled circle | `Quiet` |
| Active | green filled circle | `Active` |

This preserves meaning in screenshots and in color-impaired contexts because the text is always present.

## Theme Detection

Read current Windows app theme preference from:

`HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize\AppsUseLightTheme`

Expected values:

- `1`: light
- `0`: dark

If the value is unavailable, default to light and keep the user override available.

## Sources

- Fluent 2 typography: https://fluent2.microsoft.design/typography
- Windows typography guidance: https://learn.microsoft.com/en-us/windows/apps/design/signature-experiences/typography
- Windows color guidance: https://learn.microsoft.com/en-us/windows/apps/design/signature-experiences/color
- Windows app theming guidance: https://learn.microsoft.com/en-us/windows/apps/develop/ui/theming
- Segoe Fluent Icons guidance: https://learn.microsoft.com/en-us/windows/apps/design/iconography/segoe-fluent-icons-font

