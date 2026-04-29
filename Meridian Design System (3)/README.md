# Meridian Design System

An operator workstation design system for **Meridian** — a trading, research, data‑operations, and governance platform. The visual language is a deep navy cockpit with a sky‑cyan "signal" primary, reading like a Bloomberg terminal re‑skinned for a modern, typography‑led UI.

## Products covered

Meridian ships two distinct UI surfaces, both represented here:

1. **Web Dashboard** (`src/Meridian.Ui/dashboard/`) — React + Tailwind + shadcn‑style components. Workflow‑centric shell with five workspaces: Overview, Research, Trading, Data Operations, Governance. This is the primary source of design tokens.
2. **WPF Desktop App** (`src/Meridian.Wpf/`) — .NET 9 desktop operator shell. Shares the brand assets and Segoe MDL2 icon mapping.

## Sources

All visual design was reconstructed from the repository **rodoHasArrived/Meridian-main** on branch `main`:

- Design tokens: `src/Meridian.Ui/dashboard/src/styles/index.css`
- Components: `src/Meridian.Ui/dashboard/src/components/meridian/*.tsx`
- Screens: `src/Meridian.Ui/dashboard/src/screens/*.tsx` (overview, trading, governance, research, data‑operations)
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
- **`ui_kits/dashboard/`** — React component kit for the web dashboard
- **`SKILL.md`** — skill manifest for using this as a Claude Skill
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

- **Mood:** dark cockpit. Deep navy `#0A1524` base with radial cyan/amber/mint ambient glows in the corners — not a flat color, an atmosphere.
- **Primary:** `#2AB2D4` cyan (the "signal" color — used for active nav, primary buttons, focus rings, the brand mark).
- **Accent:** `#D69E38` amber (warning, paper environment, caution).
- **Semantic:** `#26BF86` success · `#D69E38` warning · `#DE5878` danger · `#60A5FA` paper · `#2AB2D4` live.
- **Corners:** generous — `1.25rem` for panels, `0.875rem` for cards, `0.625rem` for inputs, `0.375rem` for pills.
- **Borders:** subtle — `hsl(208 38% 22%)` on navy, usually at `/70` or `/80` opacity to feel drawn rather than carved.
- **Shadows:** combined inner highlight + deep soft outer. Never harsh.
- **Layout:** `max-w-[1720px]`, 320px left nav, fluid main with `p-6 lg:p-8`. Information density is high but breathing — panels always get `p-5/p-6`, cards get `p-4/p-5`.
- **Motion:** 200ms ease transitions on hover/press. No bouncing. No parallax. Spinners only for async refresh.
- **Typography:** three families working together — IBM Plex Sans (UI), IBM Plex Mono (data), Space Grotesk (display headings and metric numbers).

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
