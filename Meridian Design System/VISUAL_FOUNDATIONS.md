# Visual Foundations

## Mood & atmosphere

Meridian is a **dark cockpit**. Not a dark mode — a cockpit. The baseline is a deep desaturated navy (`#0A1524`) overlaid with an ambient glow:

```css
background-image:
  radial-gradient(ellipse at 6% 0%, rgba(42, 178, 212, 0.08) 0%, transparent 40%),
  radial-gradient(ellipse at 96% 96%, rgba(96, 165, 250, 0.05) 0%, transparent 38%),
  linear-gradient(180deg, rgba(8,16,26,1) 0%, rgba(8,16,26,1) 100%);
```

The uploaded workstation render reinforces a monitor-like shell: dark perimeter, compact masthead,
persistent left rail, and cyan used as a signal, not decoration. Ambient light stays faint and static.
**This is the only decorative gradient allowed.** Everything else that looks like a gradient is a
brand mark or a chart encoding.

## Reference-derived structure

The uploaded custody, reporting, charting, product-guide, and security-master images point to one
consistent workstation model:

- **Masthead:** global search, alerts, tasks, settings, and session/user controls in a thin top bar.
- **Left rail:** persistent workspace navigation with compact labels and optional status metadata.
- **Toolbar strip:** one-line filters, date scope, columns, export, refresh, and view controls.
- **Evidence first:** KPI rows sit above chart/table evidence; they do not become standalone dashboards.
- **Split workbench:** canonical records use list/detail or chart/detail layouts with identifiers and next actions visible.
- **Status window:** bottom or side panels show run status, audit trails, exceptions, or selected-row details.

Use these as Meridian patterns. Do not copy JPMorgan, Goldman Sachs, Beta One, VPR, or other
third-party brand elements.

## Color system

### Palette (resolved hex)

| Token | Hex | Use |
|---|---|---|
| `--bg` | `#08101A` | App base. Only full-bleed element. |
| `--card-bg` | `#0D1722` | Default card surface. |
| `--panel-strong-bg` | `#101C2B` | Header and sidebar. |
| `--panel-soft-bg` | `#162334` | Inline data grids and subtle lift. |
| `--surface-raise-bg` | `#1B2A3C` | Hover states, selected rows. |
| `--border-color` | `#1F344C` | Default stroke. |
| `--border-hi` | `#2A4566` | High-contrast separators, active states, selected frames. |
| `--fg` | `#DEE6EF` | Primary text. |
| `--fg-muted` | `#7C8A9B` | Secondary/supporting text, icons. |
| `--primary` | `#2AB2D4` | Signal cyan — CTAs, active nav, focus rings. |
| `--accent` | `#D69E38` | Amber — warnings, paper environment, caution. |
| `--success` | `#26BF86` | Healthy, completed, positive P&L. |
| `--warning` | `#D69E38` | Same as accent — observe state. |
| `--danger` | `#DE5878` | Offline, rejected, negative P&L, destructive. |
| `--paper` | `#60A5FA` | Paper environment badge. |
| `--live` | `#2AB2D4` | Live environment badge — same as primary for a reason. |

### Rules
- **Never invent a new color**. If you need a tint, use `color / 0.1`, `/ 0.2`, `/ 0.3` on an existing token.
- **Semantic tone is paired with a matching border and background** at consistent opacity: `border-success/30 bg-success/10 text-success`. This is a pattern, not a one‑off.
- **The primary is a scalpel**, not a paintbrush. Used on one CTA per screen, the active nav item, focus rings, and the brand mark.

## Typography

Three families, each with a job:

| Family | Role | Example |
|---|---|---|
| **Space Grotesk** (500, 600, 700) | Display — hero titles, big metrics | `"Trading Workstation"`, `"+$100K"` |
| **IBM Plex Sans** (400, 500, 600, 700) | UI — body, labels, buttons, descriptions | Everything normal |
| **IBM Plex Mono** (400, 500, 600) | Data — identifiers, timestamps, prices, code | `ord-a8f2c`, `$1,204.32`, `14:32:07` |

### Scale

| Class | Size | Weight | Family | Use |
|---|---|---|---|---|
| `h1` | 36px / 2.25rem | 700 | Display | Workspace title |
| `h2` | 24px / 1.5rem | 700 | Display | Section titles |
| `h3` | 18px / 1.125rem | 600 | Sans | Card titles |
| `body` | 14px / 0.875rem | 400 | Sans | Default text |
| `small` | 12px / 0.75rem | 400 | Sans | Supporting text |
| `eyebrow` | 10px | 600 | Sans, `UPPER`, `0.24em` | Above titles |
| `mono` | 13px / 0.8125rem | 400 | Mono | All data |
| `metric` | 32px / 2rem | 700 | Display, tabular | KPI values |

## Spacing & layout

- **Outer shell:** `max-w-[1720px]`, gutter `p-4 lg:p-6`.
- **Sidebar:** fixed 320px.
- **Panels:** inner padding `p-5 lg:p-6`, gap between sections `space-y-8`.
- **Cards:** inner padding `p-4 lg:p-5`, gap `space-y-4`.
- **Grids:** mostly `gap-3` or `gap-4`. Tables use `px-3 py-2`.
- **Unit:** everything snaps to a 4px grid. `0.25rem` increments; no `13px` anywhere.

## Corners (radii)

| Token | Value | Use |
|---|---|---|
| `--radius-xl` | 0.625rem / 10px | Major panels |
| `--radius-lg` | 0.5rem / 8px | Cards |
| `--radius-md` | 0.375rem / 6px | Inputs, buttons |
| `--radius-sm` | 0.25rem / 4px | Chips and badges |
| `--radius-xs` | 0.1875rem / 3px | Tags and price labels |

Meridian uses **tight radii** to match institutional density. The uploaded custody and charting
references read best when rows, filters, and detail panes stay crisp. Avoid soft pillows and
rounded marketing cards.

## Borders

- Default: `1px solid hsl(var(--border))` — `#22405A`.
- Typically softened: `border-border/70` or `/80` so it reads as drawn rather than carved.
- Tonal borders for semantic states: `border-success/30`, `border-warning/30`, `border-danger/30`.
- **No double borders.** Card inside a panel uses inner shadow, not a second stroke.

## Shadows & elevation

```css
--shadow-workstation:
  0 1px 0 rgba(255,255,255,0.02) inset,
  0 1px 2px rgba(0,0,0,0.30);

--shadow-panel:
  0 1px 0 rgba(255,255,255,0.02) inset,
  0 1px 1px rgba(0,0,0,0.25);
```

Every surface pairs an **inner highlight** with a shallow workstation shadow. Floating dialogs may
use `--shadow-float`; tables, nav, and workbench panels should not look like marketing cards.

## Backgrounds & imagery

- Use uploaded reference images in documentation and preview galleries only.
- Never use third-party screenshots as production UI imagery.
- Never use photography inside the workstation shell. The only product assets are brand marks,
  icons, charts, generated screenshots, and operator evidence.
- No repeating patterns, no textures, no noise.
- The ambient gradient on `body` is the only decoration.
- **Full‑bleed** is used once — the login screen — using the hero SVG.

## Motion

- **Transitions** 200ms `ease` — `transition-all duration-200` on nav items, buttons, and hover states.
- **Hover** — lighter background (`bg-secondary/55`), border appears where there wasn't one. Never a scale change.
- **Press** — briefly darker (`bg-primary/90` for primary buttons). No shrink, no squish.
- **Focus** — `ring-2 ring-primary/40`, `ring-offset-0`. Always visible for keyboard users.
- **Async** — `animate-spin` on `RefreshCcw` and similar icons. Never on whole components.
- **No bouncing, no parallax, no scroll‑jacking.** This is an operator tool.

## Transparency & blur

- Used sparingly for layering. `backdrop-blur-sm` on floating panels, dialogs, command palette.
- Semi‑transparent fills (`bg-secondary/60`, `bg-background/80`) let the ambient glow seep through decorative frames but never through data.

## Data density

Meridian is **dense** by consumer‑UI standards but **breathing** by Bloomberg standards:
- Tables: `px-3 py-2` (not `px-2 py-1`).
- KPI grids: 2 cols mobile, 4 cols desktop, never more than 6.
- Never collapse labels into icons. Icons accompany labels; they never replace them.
- Filters and toolbar controls should fit in one row before wrapping.
- Selected rows should reveal a detail pane or status window, not just a color highlight.
- Long-running workflows should expose status, owner/action, and last-updated evidence.

## The cockpit rules

1. **Information first, chrome second.** If a border or shadow isn't helping the operator parse the scene, it goes.
2. **Status is color‑coded everywhere.** Never guess — always `success/warning/danger`.
3. **Data is monospace, prose is sans.** No exceptions.
4. **The primary is for one thing per screen.** If two things are primary, one is actually secondary.
5. **Darker is deeper.** Background < card < panel < surface‑raise. The hierarchy is always visible.
