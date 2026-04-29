# Visual Foundations

## Mood & atmosphere

Meridian is a **dark cockpit**. Not a dark mode — a cockpit. The baseline is a deep desaturated navy (`#0A1524`) overlaid with an ambient glow:

```css
background-image:
  radial-gradient(ellipse at 10% 0%,  rgba(42, 178, 212, 0.20) 0%, transparent 35%),
  radial-gradient(ellipse at 88% 8%,  rgba(96, 165, 250, 0.14) 0%, transparent 30%),
  radial-gradient(ellipse at 92% 88%, rgba(214, 158, 56, 0.12) 0%, transparent 28%),
  radial-gradient(ellipse at 4% 90%,  rgba(52, 211, 153, 0.08) 0%, transparent 24%),
  linear-gradient(175deg, rgba(6,14,24,0.99) 0%, rgba(8,18,30,1) 50%, rgba(6,12,20,1) 100%);
```

Four corner radial glows (cyan top‑left, blue top‑right, amber bottom‑right, mint bottom‑left) warm the shell without drawing attention. **This is the only gradient in the design system that's allowed to be decorative.** Everything else that looks like a gradient is a brand mark.

## Color system

### Palette (resolved hex)

| Token | Hex | Use |
|---|---|---|
| `--bg` | `#0A1524` | App base. Only full‑bleed element. |
| `--card` | `#0F1E30` | Default card surface. |
| `--panel-strong` | `#10243A` | Header & sidebar — slightly brighter. |
| `--panel-soft` | `#17293C` | Inline data grids and subtle lift. |
| `--surface-raise` | `#1D3146` | Hover states, selected rows. |
| `--border` | `#22405A` | Default stroke. Usually at 70–80% opacity. |
| `--fg` | `#E8F1F9` | Primary text. |
| `--fg-muted` | `#8DA0B3` | Secondary/supporting text, icons. |
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
| `--radius-xl` | 1.25rem / 20px | Major panels, nav, header |
| `--radius-lg` | 0.875rem / 14px | Cards |
| `--radius-md` | 0.625rem / 10px | Inputs, buttons |
| `--radius-sm` | 0.375rem / 6px | Badges, tags, chips |
| full | 9999px | Pill badges only |

Meridian leans into **generous radii** — 20px on the outermost frames — to soften the otherwise dense, data‑heavy grid.

## Borders

- Default: `1px solid hsl(var(--border))` — `#22405A`.
- Typically softened: `border-border/70` or `/80` so it reads as drawn rather than carved.
- Tonal borders for semantic states: `border-success/30`, `border-warning/30`, `border-danger/30`.
- **No double borders.** Card inside a panel uses inner shadow, not a second stroke.

## Shadows & elevation

```css
--shadow-workstation:
  0 1px 0 rgba(255,255,255,0.03) inset,   /* inner top highlight */
  0 10px 30px -12px rgba(0,0,0,0.55);     /* deep soft drop */

--shadow-panel:
  0 1px 0 rgba(255,255,255,0.04) inset,
  0 4px 16px -6px rgba(0,0,0,0.45);
```

Every surface pairs an **inner highlight** (to feel lit from above) with a **deep, soft drop** (to feel seated). No harsh black shadows.

## Backgrounds & imagery

- Never use photography. The only image assets are the brand marks.
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

## The cockpit rules

1. **Information first, chrome second.** If a border or shadow isn't helping the operator parse the scene, it goes.
2. **Status is color‑coded everywhere.** Never guess — always `success/warning/danger`.
3. **Data is monospace, prose is sans.** No exceptions.
4. **The primary is for one thing per screen.** If two things are primary, one is actually secondary.
5. **Darker is deeper.** Background < card < panel < surface‑raise. The hierarchy is always visible.
