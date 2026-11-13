# Meridian — Token Reference

**Which token to reach for, and why.** Meridian has a deliberately layered token system: a
white-label control surface on top, a stable semantic vocabulary in the middle, and computed
derivations at the bottom. Knowing which layer you're touching is the difference between a clean
re-brand and a broken one.

> **The one-line rule:** *consume Tier 2, override Tier 1, never hardcode Tier 3 or a hex.*

---

## The three tiers

```
  Tier 1  THEME / WHITE-LABEL     --theme-*        ← override these to re-brand
            │  (tokens/theme.css)   data-brand=…     data-theme-density=…
            ▼
  Tier 2  SEMANTIC (public API)   --accent --green --text-primary --bg-light …
            │  (tokens/colors.css) ← THIS is what you author components against
            ▼
  Tier 3  DERIVED (computed)      --green-dim --green-a10 --severity-* --state-*
               color-mix() off Tier 2 — inherits white-label + dark automatically
```

**Tier 1 — Theme.** `--theme-accent`, `--theme-bg-canvas`, `--theme-spacing-*`, `--theme-row-height`,
the `--theme-font-*` stack, plus the `data-brand` (indigo / emerald / rose / slate / cyan / amber)
and `data-theme-density` (terminal / compact / spacious; unset = standard) switches. This is the **only** layer a
consumer should override to re-skin. Setting `data-brand="indigo"` re-points `--theme-accent`,
which flows down through Tier 2 and Tier 3 with zero component edits.

**Tier 2 — Semantic (the public API).** The names you write components and cards against:
`--accent` / `--accent-hover` / `--accent-dim`, `--green` / `--red` / `--orange` / `--purple`,
`--text-primary/secondary/muted/disabled`, `--bg` / `--bg-light` / `--bg-medium` / `--bg-hover` /
`--bg-active`, `--border` / `--border-strong` / `--border-focus` / `--border-divider`. There's
also a verbose alias set for readability — `--color-success`/`--color-error`/`--color-warning`/
`--color-info`, `--bg-surface`/`--bg-canvas`, `--text-foreground` — pick one vocabulary and stay
consistent. **Author here.**

**Tier 3 — Derived.** Never typed by hand:
- `--{hue}-dim` — the hue mixed 75% toward `--dim-mix` (#000 in light, #FFF in dark). **This is the
  text-legible variant** — chips, badges, status labels use it (see `ACCESSIBILITY.md` §1).
- `--{hue}-a10` / `-a20` — translucent fills for chip/row backgrounds.
- `--severity-*` and `--state-*` triads (`-fg` / `-bd` / `-bg`) — the operations vocabulary
  (blocked / action / review / ready; healthy / warn / danger / paper / strategy / live / pending).
  Each is derived from a Tier-2 hue, so re-branding and dark mode flow through for free.

There is also a `--ws-*` / working-name layer (`--fg`, `--border-color`, `--cyan-primary`…) that
exists **only** so CSS pasted verbatim from the real Meridian workstation resolves. Don't author
new code against `--ws-*`; use Tier 2.

---

## "These sound the same" — disambiguation

### `--text-secondary` vs `--text-muted`
Both are legible body weights; the distinction is **role, not just darkness**.

| | Token | Hex · on panel | Use for |
| --- | --- | --- | --- |
| Secondary | `--text-secondary` | `#535C65` · 7.1:1 | Content that is *still primary reading* but subordinate — row labels, field labels, secondary values, body copy in a side panel. The user is meant to read it. |
| Muted | `--text-muted` | `#606870` · 5.3:1 | *Metadata about* content — captions, hints, timestamps, unit suffixes, column sublabels, "last synced" lines. Glanceable, not primary reading. |

Rule of thumb: if the user reads it to do the task → **secondary**; if it annotates or timestamps
the thing they read → **muted**. (Both pass AA on every surface; choosing correctly is about
hierarchy, not contrast.)

### `--accent-hover` vs `--accent-dim`
`--accent-hover` (`#D2795A`) is the pointer-emphasized signal; `--accent-dim` (`#CB6E4E`)
is the controlled signal variant. Primary actions do not use this family. In dark mode the same
roles use `#E5A088` and `#E19479` so signal labels remain legible without turning into focus or
semantic state. Do not create intermediate shades.

### `--brand-signal-ink` vs signal fills
Use `--brand-signal-ink` for normal-sized text that retains the signal hue: labels, inline
references, highlighted matches and retained table ranks. It derives 75% of the signal toward
`--dim-mix`, producing `#944A32` in the default light theme and `#E2A38C` in dark. Its text
contrast is checked against workstation surfaces and signal washes in every appearance.
`--brand-signal`, `--accent-hover` and `--accent-dim` remain fill, border and graphical roles;
their lighter variants are not text inks. Keep text fully opaque on hover; emphasize it with
an underline instead of fading an otherwise readable label.

### `--bg-hover` vs `--bg-active`
`--bg-hover` (#EDEFF0) is a flat neutral shift for transient hover. `--bg-active` (#E7EDF2) is a
cool blue wash for *selected/engaged* state (selected row, active nav). Active is a state that
persists; hover is not.

### `--border` vs `--border-strong` vs `--border-divider`
`--border` is the default load-bearing hairline. `--border-strong` is for emphasis — totals rows,
table headers, section frames, the heavy rule under a title. `--border-divider` is the lightest,
for splitting sections *inside* a surface where a full border would be too much.

---

## Spacing, radius, elevation — the v2.1 constraints

**Spacing** is a 4px-base, 8-step scale: `--space-xs 4 · sm 8 · md 12 · lg 16 · xl 24 · 2xl 32 ·
3xl 48 · 4xl 64`. The density switch rescales the *theme* spacing (`--theme-spacing-*`) and the
row/control heights (see the density table below). Use the scale tokens; don't type pixel padding.

**Radius is intentionally tiny.** `--radius-chip` / `--radius-analytical` **2px** (chips, badges,
plots, plates) · `--radius-button` / `--radius-card` **4px** (controls, panels) ·
`--radius-overlay` **6px** (popovers, menus, drawers) · `--radius-dialog` **8px** — the ceiling.
`--radius-pill 999px` is for status pills only. *Structure comes from borders, not rounding.*

**Elevation is flat by mandate.** `--shadow-card`, `--shadow-panel`, `--shadow-soft`,
`--shadow-workstation` are all `none` — the workstation plane is flat and **borders carry
structure**. The *only* real shadow is `--shadow-menu` (`0 2px 8px rgba(17,23,29,.16)`), for genuinely
detached overlays (menus, popovers) — aliased as `--shadow-float`. Don't reach for a shadow to
separate two panels; use a border or a background step (`--card-surface-raised`).

**Opacity** has exactly three sanctioned values: `--opacity-disabled .38` ·
`--opacity-muted .62` · `--opacity-series-secondary .74`. Never invent a fourth.

**Grid** — `--grid-columns 12` (large) / `--grid-columns-md 8`, gutters
`--grid-gutter-lg 24 · --grid-gutter 20 · --grid-gutter-sm 16`, margins
`--grid-margin-lg 32 · --grid-margin 24 · --grid-margin-sm 20`.

**Component anatomy** — reference dimensions to alias instead of hard-coding:
`--table-row-height` (tracks density) · `--table-header-height 34` · `--selection-rail 2` ·
`--capsule-header-height 48` / `--capsule-footer-height 32` / `--capsule-h-sm|md|lg|xl`
(144 · 240 · 336 · 432) · `--drawer-width-min 360` / `--drawer-width-max 440` /
`--drawer-action-height 36`. Analytical geometry (plates, inspector, plot padding) lives in
`tokens/visualization.css`.

### Density modes

| Scope | Row | Control | Body | Label | Panel pad |
| --- | --- | --- | --- | --- | --- |
| `terminal` | 26px | 24px | 12px | 10px | 8px |
| `compact` (**dense**) | 28px | 28px | 13px | 10px | 12px |
| default (**standard**) | 36px | 36px | 14px | 11px | 16px |
| `spacious` (**review**) | 44px | 40px | 15px | 12px | 20px |

Read them as `--theme-row-height` · `--theme-control-height` · `--theme-body-size` ·
`--theme-label-size` · `--pad-panel`; never branch on the attribute yourself.

---

## Material hierarchy & the interaction family

Surfaces are named by **material**, and the ladder is deliberate: data surfaces read brightest,
insets recede toward the canvas.

| Material | Token | Light | Use |
| --- | --- | --- | --- |
| Paper | `--bg` / `--surface-paper` | `#F2F0EB` | application canvas |
| Plate | `--bg-light` / `--surface-plate` | `#F8F7F3` | primary workspace, panels |
| Well | `--card-surface-raised` / `--surface-well` | `#FCFBF8` | data surfaces, raised cells |
| Recessed / Rail | `--bg-medium` / `--surface-recessed` | `#EBE8E1` | headers, nav rail, inset wells |
| Overlay | `--surface-overlay` | `#FFFFFF` | dialogs, menus, popovers |
| Signal | `--signal` / `--accent` | `#C56343` | brand, active identity marker, scenario |

**Focus and selection are blue, not brand.** `--focus #2D6FB3` (focus ring, also `--border-focus`) ·
`--selected #496C8F` (selection rail, selected mark) · `--selected-surface #E7EDF2` (selected row,
plate, datum) · `--interaction-hover #DDE5EC`. The terracotta signal marks *identity or an analytical alternate state* (an active brand marker,
the scenario series, the editorial Field); graphite marks primary action, while blue marks *system
feedback* (what you focused and selected). Keeping them apart is why a scenario overlay never reads as an alarm and a focused input
never reads as a brand moment.

**Semantic surfaces** — flat washes for high-priority conditions: `--green-surface #E4EEE8` ·
`--red-surface #F5E5E2` · `--orange-surface #F2EAD8` · `--info-surface #E3ECF2`. Plus the extra
semantics the data-condition model needs: `--pending #7A6B52` · `--neutral #6D757D` ·
`--missing #A34A42` · `--brand-field #5D7286` (the Field motif) · `--brand-axis #29323A` (the Datum
ink — chrome and the primary trace).

---

## Motion

Five durations and two curves — that's the whole budget:

- `--motion-instant` **80ms** — state flips (checkbox, chip, toggle).
- `--motion-fast` **120ms** — color/border/background state changes (hover, focus, press).
- `--motion-base` **180ms** — slightly larger transitions (accordion height, drawer slide).
- `--motion-reflow` **240ms** (= `--motion-slow`) — marks moving after sort, filter, or scenario.
- `--motion-deliberate` **280ms** — trace and reconcile sequences.
- `--ease-standard` `cubic-bezier(.2,0,0,1)` for entrances and moves; `--ease-exit`
  `cubic-bezier(.4,0,1,1)` for dismissals.

No entrance choreography, no parallax, no animated number rolls (live data updates silently). Any
continuous motion you add must gate on `prefers-reduced-motion` — see `ACCESSIBILITY.md` §4, and
the four motion verbs in `VISUALIZATION_GRAMMAR.md` §17.

---

## Typography tokens

Three families — `--font-display` (IBM Plex Sans, headings), `--font-body` (IBM Plex Sans,
UI copy), `--font-data` (IBM Plex Mono — **all numbers, IDs, codes, and
tabular data**). Numeric data is *always* mono; this is load-bearing for column alignment.

Ramp (size/line-height pairs): `--type-display 48/52` (report covers only) · `--type-workspace 28/34` ·
`--type-page-title 22/28` · `--type-data-medium 18/24` · `--type-section 16/22` ·
`--type-card-title 14/20` · `--type-body 14/20` (`--type-body-dense 13/18`, `--type-body-review 15/22`) ·
`--type-metric 28/34` (mono) · `--type-data-value 16` (mono) · `--type-label-lg 11/16` ·
`--type-label` / `--type-micro 10/14` (small-caps) · `--type-caption 12/16`. Weights
400 / **450 (`--weight-data`, mono optical match)** / 500 / 600 / 700 via `--weight-*`. Don't hand-size
text — pull from the ramp so density and hierarchy stay coherent.

---

## White-labeling — the complete entry points

To re-skin Meridian for a consumer, you touch **only Tier 1**:

1. **Pick a brand** — `<html data-brand="indigo">` for one of the six presets, **or** override
   `--theme-accent` / `--theme-accent-dim` / `--theme-accent-hover` directly for a custom hue.
2. **Set density** — `<body data-theme-density="compact">` for trading-desk density, `spacious`
   for review/reading surfaces.
3. **Swap fonts** (optional) — override `--theme-font-display/body/mono`.
4. **Re-run the contrast check** — if your custom accent is lighter than the steel default,
   re-verify white-on-accent and accent-on-panel clear AA (the script in `ACCESSIBILITY.md` §1).

Because Tier 2 aliases Tier 1 and Tier 3 derives from Tier 2, a single `data-brand` attribute
recolors accents, chart primaries, focus rings, active nav indicators, selected-row washes, and
every severity/state chip — automatically, in both light and dark. **Never** fork a component to
change its color; if you can't re-skin it from Tier 1, that's a token bug to fix at the source.

---

## Chrome tokens (masthead + status bar)

The near-black bars stay dark in **both** modes, so their interior details have their own tokens
instead of reusing panel tokens (which flip in dark): `--topbar-text-muted` / `--topbar-text-faint`
(secondary/hint ink on chrome), `--topbar-sep`, `--topbar-field-bg` / `--topbar-field-border`(`-hover`)
(the inset search field and kbd caps), and `--chrome-ok/warn/err` (status dots bright enough to read
on `#29323A` — deliberately lighter than the panel semantics `--green/--orange/--red`). Anything that
sits ON the chrome uses these; anything on paper uses the normal panel tokens. Defined in
`tokens/colors.css` under "Chrome interior details"; no dark override needed.

---

## Anti-patterns

- ❌ Hardcoding a hex (`color:#C56343`) — breaks white-label + dark. Use `var(--accent)`.
- ❌ Authoring against Tier 3 (`var(--green-dim)` for a non-semantic surface) or `--ws-*` names.
- ❌ Typed pixel padding/margins instead of `--space-*`; typed corners instead of `--radius-*`.
- ❌ A shadow to separate two coplanar panels — use a border or surface step.
- ❌ A new easing curve or duration — there are exactly two durations and one curve.
- ❌ Placeholder as a label; color as the only signal (see `ACCESSIBILITY.md`).

---

**See also:** `guidelines/VISUAL_FOUNDATIONS.md` (the "why" behind Concrete) ·
`guidelines/ACCESSIBILITY.md` (measured contrast for every token) · `guidelines/PATTERNS.md` (composition).
