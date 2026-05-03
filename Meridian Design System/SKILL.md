---
name: meridian-design-system
description: Meridian is a trading, portfolio, accounting, reporting, strategy, data, and settings operator workstation. Dark navy cockpit (#08101A), sky-cyan primary (#2AB2D4), amber warning (#D69E38), tight workstation radii, dense data tables, masthead plus left rail. Three fonts: Space Grotesk (display), IBM Plex Sans (UI), IBM Plex Mono (data). Sentence case, no emoji, monospace for every identifier/number/timestamp. Use `colors_and_type.css`, `INSPIRATION_BRIEF.md`, and `ui_kits/dashboard/`.
---

# Meridian — skill guide

Use this skill when a user asks to design, extend, or mock UI for **Meridian** or references its
workspaces (`Trading`, `Portfolio`, `Accounting`, `Reporting`, `Strategy`, `Data`, `Settings`).

## Start here
1. `colors_and_type.css` — drop into the page first. All tokens + typography classes live here.
2. `VISUAL_FOUNDATIONS.md` — rules for surfaces, borders, shadows, motion.
3. `CONTENT_FUNDAMENTALS.md` — voice, casing, numerals, banned phrases.
4. `INSPIRATION_BRIEF.md` — image-derived structure for dense workstation pages.
5. `preview/component-state-matrix.html` — expected state behavior across reusable components.
6. `preview/screen-recipes.html` — workspace-level recipes for implementation handoff.
7. `preview/chart-table-standards.html` — evidence chart, dense-table, and row-detail rules.
8. `ICONOGRAPHY.md` — line-icon rules. Use `assets/icons/*.svg` (47 glyphs, currentColor).

## Fast rules (operator cockpit)
- Background: `bg-app`. Keep ambient light faint and static.
- Surfaces stack: `bg-card` → `bg-panel` → `bg-soft` → `bg-raise`. Darker is deeper.
- One primary CTA per screen (`#2AB2D4`). One accent (`#D69E38`) at most alongside.
- Data is monospace (`var(--font-mono)`), prose is sans. Never mix.
- Titles use `--font-display` (Space Grotesk), 700, negative letter‑spacing.
- Numbers are tabular, signed when directional: `+$100K`, `−2.41%`, `0%`.
- Tight radii: 10 / 8 / 6 / 4 / 3px.
- Borders precise: `#1F344C` default, `#2A4566` for selected and active frames.
- Use masthead + left rail + toolbar strip for workstation pages.
- Pair KPI cards with chart/table evidence and selected-record details.
- No emoji. No exclamation marks. No marketing copy.

## Components available
- `preview/components-*.html` — live, styled reference cards for buttons, badges, inputs, metrics, table, nav, banners.
- `preview/institutional-workstation.html` — image-inspired workstation frame and dense table preview.
- `preview/component-state-matrix.html` — badges, banners, KPI tiles, toolbar chips, nav rows, dense-table rows, inputs, buttons, and entity fields in all supported states.
- `preview/screen-recipes.html` — recipes for Trading, Portfolio, Accounting, Reporting, Strategy, Data, and Settings.
- `preview/chart-table-standards.html` — projection/fan, scatter, order-book, dense-table, status-window, and row-detail standards.
- `ui_kits/dashboard/` — React components if building the web dashboard.
- `scripts/check_design_system_governance.py` — run before finalizing design-system edits.

## Copy tone (paste‑safe examples)
- "Working and partially filled orders remain visible in real time."
- "Paper thresholds, drawdown limits, and buying‑power constraints are evaluated on every order submission."
- "No paper sessions active. Create one above to start tracking execution."

## When in doubt
- Match the real dashboard: see `/projects/<meridian-repo>/src/Meridian.Ui/dashboard/src/screens/*.tsx` if the user has it linked.
- Use uploaded images as structural inspiration only; never copy third-party brands, marks, labels, or proprietary layouts.
- Use line icons from `assets/icons/`, not lucide knockoffs.
- If you need a new color, express it as `hsl(var(--primary) / 0.12)` — never invent a hex.
- Run `python "Meridian Design System (3)/scripts/check_design_system_governance.py"` before handoff.
