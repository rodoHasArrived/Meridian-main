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
and `data-theme-density` (terminal / compact / spacious) switches. This is the **only** layer a
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
| Secondary | `--text-secondary` | `#4D5967` · 7.1:1 | Content that is *still primary reading* but subordinate — row labels, field labels, secondary values, body copy in a side panel. The user is meant to read it. |
| Muted | `--text-muted` | `#59636F` · 6.1:1 | *Metadata about* content — captions, hints, timestamps, unit suffixes, column sublabels, "last synced" lines. Glanceable, not primary reading. |

Rule of thumb: if the user reads it to do the task → **secondary**; if it annotates or timestamps
the thing they read → **muted**. (Both pass AA on every surface; choosing correctly is about
hierarchy, not contrast.)

### `--accent-hover` vs `--accent-dim`
`--accent-hover` (#3B82A6, lighter) is the **pointer-hover** state; `--accent-dim` (#255B75,
darker) is the **pressed/active** state. Lighter on hover, darker on press — never swap them.
`--accent-dim` also doubles as **emphasized accent text** (Toast action, role badges).
**Dark-mode exception (2026-07):** in dark, `--accent-dim` is `#609BC9` — *lighter* than the
accent, sitting between accent and hover — because a darker pressed fill left the dark button
ink at 3.11:1 and failed as text on dark panels. Semantics are unchanged (still "pressed" +
"accent text"); only the direction of the shift flips in dark, per standard dark-UI convention.

### `--bg-hover` vs `--bg-active`
`--bg-hover` (#EAEEF3) is a flat neutral shift for transient hover. `--bg-active` (#D7E5F1) is a
cool blue wash for *selected/engaged* state (selected row, active nav). Active is a state that
persists; hover is not.

### `--border` vs `--border-strong` vs `--border-divider`
`--border` is the default load-bearing hairline. `--border-strong` is for emphasis — totals rows,
table headers, section frames, the heavy rule under a title. `--border-divider` is the lightest,
for splitting sections *inside* a surface where a full border would be too much.

---

## Spacing, radius, elevation — the Concrete constraints

**Spacing** is a 6-step scale: `--space-xs 3px · sm 6px · md 12px · lg 16px · xl 24px · 2xl 32px`.
The density switch rescales the *theme* spacing (`--theme-spacing-*`) and `--theme-row-height`
(terminal 26px · default/compact 32px · spacious 48px). Use the scale tokens; don't type pixel padding.

**Radius is intentionally tiny.** Everything is `2px` (`--radius-chip` / `-button` / `-card` all
2px); the named scale tops out at `--radius-xl 6px` for large sheets only. *Structure comes from
borders, not rounding.* If you find yourself wanting a 12px corner, you're off-system — Meridian is
"Concrete," not "friendly SaaS."

**Elevation is flat by mandate.** `--shadow-card`, `--shadow-panel`, `--shadow-soft`,
`--shadow-workstation` are all `none` — the workstation plane is flat and **borders carry
structure**. The *only* real shadow is `--shadow-menu` (`0 2px 6px rgba(0,0,0,.18)`), for genuinely
detached overlays (menus, popovers) — aliased as `--shadow-float`. Don't reach for a shadow to
separate two panels; use a border or a background step (`--card-surface-raised`).

---

## Motion

Two durations and one curve — that's the whole budget:

- `--motion-fast` **100ms** — color/border/background state changes (hover, focus, press).
- `--motion-base` **150ms** — slightly larger transitions (accordion height, drawer slide).
- `--ease-standard` `cubic-bezier(.4,0,.2,1)` — the only easing curve. Use it for everything.

No entrance choreography, no parallax, no animated number rolls (live data updates silently). Any
continuous motion you add must gate on `prefers-reduced-motion` — see `ACCESSIBILITY.md` §4.

---

## Typography tokens

Three families — `--font-display` (Segoe UI Variable Display, headings), `--font-body` (Segoe UI
Variable Text, UI copy), `--font-data` (Cascadia/JetBrains Mono — **all numbers, IDs, codes, and
tabular data**). Numeric data is *always* mono; this is load-bearing for column alignment.

Ramp (size/line-height pairs): `--type-page-title 24/32` · `--type-section 16/24` ·
`--type-card-title 13/20` · `--type-body 13/20` · `--type-metric 28` (mono) ·
`--type-data-value 16` (mono) · `--type-label 9` (small-caps) · `--type-caption 11/16`. Weights
400/500/600/700 via `--weight-*`. Don't hand-size text — pull from the ramp so density and
hierarchy stay coherent.

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
on `#171A1F` — deliberately lighter than the panel semantics `--green/--orange/--red`). Anything that
sits ON the chrome uses these; anything on paper uses the normal panel tokens. Defined in
`tokens/colors.css` under "Chrome interior details"; no dark override needed.

---

## Anti-patterns

- ❌ Hardcoding a hex (`color:#2F6F8F`) — breaks white-label + dark. Use `var(--accent)`.
- ❌ Authoring against Tier 3 (`var(--green-dim)` for a non-semantic surface) or `--ws-*` names.
- ❌ Typed pixel padding/margins instead of `--space-*`; typed corners instead of `--radius-*`.
- ❌ A shadow to separate two coplanar panels — use a border or surface step.
- ❌ A new easing curve or duration — there are exactly two durations and one curve.
- ❌ Placeholder as a label; color as the only signal (see `ACCESSIBILITY.md`).

---

**See also:** `guidelines/VISUAL_FOUNDATIONS.md` (the "why" behind Concrete) ·
`guidelines/ACCESSIBILITY.md` (measured contrast for every token) · `PATTERNS.md` (composition).
