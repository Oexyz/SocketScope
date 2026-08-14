# Design system

SocketScope uses OeXYZ's recurring clarity, restrained dark surfaces, direct language,
and transparent limitations without copying a game or payment-client interface.

## Visual tokens

| Purpose | Value |
|---|---|
| Application background | `#080B10` |
| Sidebar | `#0B0F16` |
| Surface / raised surface | `#101620` / `#171F2C` |
| Border / strong border | `#29384B` / `#42546B` |
| Primary text | `#F0F6FC` |
| Muted text | `#9DAABD` |
| Accent / hover | `#0B6FD3` / `#33A3FF` |
| Success | `#43D7A0` |
| Warning | `#FFBD2E` |
| Danger | `#FF6961` |

Inter is packaged locally under the SIL Open Font License. The base size is 14 logical
pixels, section titles are 18, page titles are 26, and eyebrow labels are 11 with
modest tracking. No font, icon, or illustration is loaded from the network.

## Layout and components

- Eight-to-ten-pixel radii and one-pixel borders separate surfaces without heavy
  shadows or glow.
- Primary spacing follows a practical 4/8/12/18/24/32 logical-pixel rhythm.
- Buttons and inputs are at least 36 logical pixels high and have visible focus.
- Status is never conveyed by color alone; every state has text and, where useful, an
  evidence explanation.
- Large tables use stable keyed rows, horizontal separators, persistent selection,
  deterministic sorting, localized draggable/resizable/resettable headers, explicit
  copy actions, and an adjacent detail panel. Compact windows retain readable minimum
  widths and expose horizontal scrolling.
- The Help/Diagnostics section explains high-impact terms in responsive cards and
  separates metadata-only support status from observed endpoint evidence.
- Animation is not required for understanding and is omitted from the 1.0 shell.

The tone is factual. UI copy says “not tested”, “partial”, “permission denied”, or
“unknown” when evidence is absent. It does not use marketing claims or automatic risk
scores.

## Responsive and accessible behavior

The supported minimum window is 760 by 520 logical pixels. At narrower content widths,
summary cards and the detail/table arrangement reflow instead of clipping primary
actions. Text may expand in German without fixed-height containers. Keyboard order
follows the visual order, focus indicators use the high-contrast accent border, and
interactive controls expose meaningful automation names through their visible text.
`F1` opens the local help view, `F5` refreshes, `Ctrl+F` focuses endpoint search, and
`Ctrl+C` copies selected table rows with localized headings.

Release review covers 100, 150, 200, and 300 percent scale, Windows high contrast,
keyboard-only navigation, Windows Narrator/NVDA, and Linux Orca/AT-SPI where a suitable
desktop test host is available. Automated headless checks are supporting evidence,
not a substitute for those manual checks.

Automated gates change the headless render scale at all four values, exercise compact
and wide reflow, drive Tab focus, and calculate WCAG contrast for normal primary,
muted, action, success, warning, and error text. The primary action blue is deliberately
darker than the informational hover/focus blue so white button labels remain above
4.5:1.
