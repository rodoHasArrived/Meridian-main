# Meridian Design System

An operator workstation design system for **Meridian** — a trading, portfolio, accounting,
reporting, strategy, data, and settings platform. The visual language is a deep navy cockpit with
a sky-cyan "signal" primary, shaped for dense institutional workflows without copying third-party
terminal products.

## Products covered

Meridian ships two distinct UI surfaces, both represented here:

1. **Web Dashboard** (`src/Meridian.Ui/dashboard/`) — the active browser-based operator UI lane. It owns new workstation delivery across `Trading`, `Portfolio`, `Accounting`, `Reporting`, `Strategy`, `Data`, and `Settings`.
2. **Retained WPF Desktop App** (`src/Meridian.Wpf/`) — .NET 9 desktop operator shell retained for shared contracts, regression fixes, and desktop support. It still shares brand assets and icon vocabulary.

## Sources

All visual design was reconstructed from the repository **rodoHasArrived/Meridian-main** on branch `main`:

- Design tokens: `src/Meridian.Ui/dashboard/src/styles/index.css`
- Components: `src/Meridian.Ui/dashboard/src/components/meridian/*.tsx`
- Screens: `src/Meridian.Ui/dashboard/src/screens/*.tsx` and workstation routes, with historical
  screen names treated as compatibility context rather than current top-level navigation.
- Brand assets: `src/Meridian.Wpf/Assets/Brand/*.svg`
- Icon library: `src/Meridian.Wpf/Assets/Icons/*.svg` (47 icons, 24×24, stroke-based, currentColor)
- Live screenshots: `docs/screenshots/*.png`

You don't need access to the repo to use this design system — everything is copied into `assets/`.

---

## Index

- **`colors_and_type.css`** — all CSS variables (HSL + resolved hex), typography classes, surface primitives
- **`assets/brand/`** — Meridian logo marks, wordmark, tile
- **`assets/icons/`** — 47 custom 24×24 stroke icons
- **`preview/`** — Design System review cards
- **`preview/component-state-matrix.html`** — component states across operator state semantics
- **`preview/screen-recipes.html`** — seven-workspace screen recipes for implementation handoff
- **`preview/chart-table-standards.html`** — chart, dense-table, status-window, and row-detail standards
- **`ui_kits/dashboard/`** — React component kit for the web dashboard
- **`scripts/check_design_system_governance.py`** — local governance checks for links, tokens, workspace names, radii, gradients, and table numerics
- **`tests/test_design_system_governance.py`** — unittest coverage for the governance script
- **`SKILL.md`** — skill manifest for using this as a Claude Skill
- **`INSPIRATION_BRIEF.md`** — image-derived guidance from uploaded workstation, custody, reporting, charting, and security-master references
- **`CONTENT_FUNDAMENTALS.md`** — voice, tone, copy rules
- **`VISUAL_FOUNDATIONS.md`** — visual motifs, layout rules, motion
- **`ICONOGRAPHY.md`** — icon system, usage

---

## Content Fundamentals

See `CONTENT_FUNDAMENTALS.md` for the full rules. In short:

- **Voice:** operator‑to‑operator. Terse, technical, observational. No marketing fluff.
- **Casing:** Sentence case for UI labels and titles. ALL CAPS reserved for eyebrow labels and environment badges (`PAPER`, `LIVE`, `RESEARCH`).
- **Pronouns:** product speaks in third person about the system ("Working and partial orders stay visible"); addresses the operator as "you" sparingly.
- **Numbers:** tabular, signed where meaningful (`+$100K`, `+0`, `0%`). Monospace for every identifier, symbol, and timestamp.
- **No emoji.** The WPF app originally used emoji; all of them have been replaced with line icons. See `assets/icons/README.md`.

---

## Visual Foundations

See `VISUAL_FOUNDATIONS.md`. In short:

- **Mood:** dark cockpit. Deep navy `#08101A` base with restrained cyan/blue ambient light. The uploaded references support a monitor-like workstation frame, not decorative glow-heavy pages.
- **Primary:** `#2AB2D4` cyan (the "signal" color — used for active nav, primary buttons, focus rings, the brand mark).
- **Accent:** `#D69E38` amber (warning, paper environment, caution).
- **Semantic:** `#26BF86` success · `#D69E38` warning · `#DE5878` danger · `#60A5FA` paper · `#2AB2D4` live.
- **Corners:** tight — 10px maximum for major panels, 8px cards, 6px controls, 4px chips, 3px tags.
- **Borders:** precise — `#1F344C` on navy, `#2A4566` for selected rows, active frames, and high-contrast separators.
- **Shadows:** 1-2px workstation shadows with inset highlights. Large glow and elevation theatre are out.
- **Layout:** masthead + left rail + dense content workbench. Uploaded references favor compact filters, selected-row detail panes, KPI-to-evidence flow, and horizontal status windows.
- **Motion:** 200ms ease transitions on hover/press. No bouncing. No parallax. Spinners only for async refresh.
- **Typography:** three families working together — IBM Plex Sans (UI), IBM Plex Mono (data), Space Grotesk (display headings). Numbers, identifiers, timestamps, prices, and row counts are always mono.

---

## Image-inspired refinement

The local `uploads/` folder is the source reference board for this package. It is intentionally
ignored by Git, so distributable HTML must use tracked files under `assets/` and treat
`INSPIRATION_BRIEF.md` as the durable summary of the image-derived guidance. The local board
contains:

- a target Meridian workstation render with KPI cards, chart/table pairing, and persistent masthead/rail structure;
- custody, portfolio-reporting, and trade-manager screens that demonstrate table density, filter bars, selected-row details, and status windows;
- Beta One product-guide captures that show annotated task flows for upload, pricing, profiles, and send-to-desk actions;
- charting and security-master examples that support split workbenches and canonical identifier panels.

Use those images to refine structure and density. Do not copy third-party branding, labels, marks,
or product-specific layouts. Meridian keeps its own navy/cyan semantic system and operator copy.

---

## Governance checks

Run these before promoting design-system changes:

```bash
python "Meridian Design System (3)/scripts/check_design_system_governance.py"
python -m unittest "Meridian Design System (3)/tests/test_design_system_governance.py"
```

The checker intentionally baselines older preview exceptions so new files must use tokens, current
workspace names, tight radii, approved gradients, valid links, and mono/right-aligned numeric table
cells.

---

## Iconography

See `ICONOGRAPHY.md`. In short:

- **Line icons, 24×24**, 1.5pt stroke, rounded caps/joins, `currentColor` fills. Copied into `assets/icons/`.
- **React:** the live app uses `lucide-react` for most icons; custom ones live alongside. Both vocabularies look identical.
- **WPF:** uses **Segoe MDL2 Assets** glyph codes. SVG set is the cross‑platform source of truth.
- **No emoji.** Unicode characters (`·`, `—`, `/`) are used as separators between metadata fields.
- **Brand mark** (`assets/brand/meridian-mark.svg`) — a meridian line with a rising signal path, capped by a mint glow. This is the app icon, logo, and login illustration all at once.

---

## Getting started

Drop `colors_and_type.css` into a page, wrap your shell in a `bg-app` body, and compose with `.panel-surface` / `.panel-surface-strong` / `.glass-card` primitives. For React, use the kit in `ui_kits/dashboard/`.
