---
name: meridian-design-system
description: Meridian is a trading/research/data-ops operator workstation. Dark navy cockpit (#0A1524), sky‑cyan primary (#2AB2D4), amber accent (#D69E38). Three fonts — Space Grotesk (display), IBM Plex Sans (UI), IBM Plex Mono (data). Sentence case, no emoji, monospace for every identifier/number/timestamp. Use `colors_and_type.css` for tokens and the components in `ui_kits/dashboard/`.
---

# Meridian — skill guide

Use this skill when a user asks to design, extend, or mock UI for **Meridian** or references its workspaces (Overview, Research, Trading, Data Operations, Governance).

## Start here
1. `colors_and_type.css` — drop into the page first. All tokens + typography classes live here.
2. `VISUAL_FOUNDATIONS.md` — rules for surfaces, borders, shadows, motion.
3. `CONTENT_FUNDAMENTALS.md` — voice, casing, numerals, banned phrases.
4. `ICONOGRAPHY.md` — line‑icon rules. Use `assets/icons/*.svg` (47 glyphs, currentColor).

## Fast rules (operator cockpit)
- Background: `bg-app`. Never a flat color — the ambient glow is part of the brand.
- Surfaces stack: `bg-card` → `bg-panel` → `bg-soft` → `bg-raise`. Darker is deeper.
- One primary CTA per screen (`#2AB2D4`). One accent (`#D69E38`) at most alongside.
- Data is monospace (`var(--font-mono)`), prose is sans. Never mix.
- Titles use `--font-display` (Space Grotesk), 700, negative letter‑spacing.
- Numbers are tabular, signed when directional: `+$100K`, `−2.41%`, `0%`.
- Generous radii: 20 / 14 / 10 / 6 / pill.
- Borders subtle: `border: 1px solid hsl(var(--border) / .7)`.
- No emoji. No exclamation marks. No marketing copy.

## Components available
- `preview/components-*.html` — live, styled reference cards for buttons, badges, inputs, metrics, table, nav, banners.
- `ui_kits/dashboard/` — React components if building the web dashboard.

## Copy tone (paste‑safe examples)
- "Working and partially filled orders remain visible in real time."
- "Paper thresholds, drawdown limits, and buying‑power constraints are evaluated on every order submission."
- "No paper sessions active. Create one above to start tracking execution."

## When in doubt
- Match the real dashboard: see `/projects/<meridian-repo>/src/Meridian.Ui/dashboard/src/screens/*.tsx` if the user has it linked.
- Use line icons from `assets/icons/`, not lucide knockoffs.
- If you need a new color, express it as `hsl(var(--primary) / 0.12)` — never invent a hex.
