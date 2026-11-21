---
name: meridian-design
description: Use this skill to generate well-branded interfaces and assets for Meridian (trading / market-data / research workstation platform), either for production or throwaway prototypes/mocks/etc. Contains essential design guidelines, colors, type, fonts, assets, and UI kit components for prototyping.
user-invocable: true
---

Read the README.md file within this skill, and explore the other available files.
If creating visual artifacts (slides, mocks, throwaway prototypes, etc), copy assets out and create static HTML files for the user to view. If working on production code, you can copy assets and read the rules here to become an expert in designing with this brand.
If the user invokes this skill without any other guidance, ask them what they want to build or design, ask some questions, and act as an expert designer who outputs HTML artifacts _or_ production code, depending on the need.

Consolidated primitives (prefer these over their older siblings): **`ValueEnvelope`** — one contract per figure, condition · authority · evidence · clocks · materiality, in three disclosure tiers (`guidelines/VALUE_ENVELOPE.md`); **`DataGrid`** — the single tabular primitive, capabilities as props, domain tables as `GRID_SCHEMAS` entries, arithmetic as a `proof` contract (`guidelines/TABULAR_CONTRACT.md`); **`Figure`** — parametric geometries with role-assigned series instead of one component per named chart; **`Registry`** — the one vocabulary carrier (density, severity, time roles, object types) with alias resolution; **`ScreenComposition`** + `defineScreen` — a screen declared as archetype + regions, never forked (reference migration: `templates/close-tie-out/`).

Key files: `readme.md` (guide + index, with the repository map and root manifest), `CONTRIBUTING.md` + `docs/ARCHITECTURE.md` (how the package is organized and extended), `tokens/` (the "Programmed Institutionalism" light palette, type ramp, elevation — lifted from the desktop app `src/Meridian.Wpf/Styles`), `guidelines/` (visual/content/patterns/brand/token/a11y docs + specimen cards), `assets/` (brand marks + 48 line module icons), `components/` (React primitives in seven domains — 224 exports), `templates/` (7 copy-and-edit archetype workstation screens), `templates/` (21 full production workstation screens to copy from).

Meridian is a **light** institutional workstation: paper canvas, white cards, near-black chrome bars, one terracotta accent, desaturated semantic washes, IBM Plex Sans + IBM Plex Mono, hairline structure — no gradients, no glow.
