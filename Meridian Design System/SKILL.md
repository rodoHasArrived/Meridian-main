---
name: meridian-design
description: Use this skill to generate well-branded interfaces and assets for Meridian (trading / market-data / research workstation platform), either for production or throwaway prototypes/mocks/etc. Contains essential design guidelines, colors, type, fonts, assets, and UI kit components for prototyping.
user-invocable: true
---

Read the README.md file within this skill, and explore the other available files.
If creating visual artifacts (slides, mocks, throwaway prototypes, etc), copy assets out and create static HTML files for the user to view. If working on production code, you can copy assets and read the rules here to become an expert in designing with this brand.
If the user invokes this skill without any other guidance, ask them what they want to build or design, ask some questions, and act as an expert designer who outputs HTML artifacts _or_ production code, depending on the need.

Key files: `README.md` (guide + index), `tokens/` (the "Programmed Institutionalism" light palette, type ramp, elevation — lifted from the desktop app `src/Meridian.Wpf/Styles`), `guidelines/` (brand/content/visual/iconography docs + specimen cards), `assets/` (brand marks + 47 line module icons), `components/` (React primitives), `ui_kits/` (full light workstation screens to copy from).

Meridian is a **light** institutional workstation: warm drafting-stock canvas, architect-white cards, warm-black chrome bars, one muted copper accent (`#A85436`), desaturated forest/brick/ochre/slate-violet semantics, Segoe UI + Cascadia Mono, hairline structure — no gradients, no glow. The superseded steel-blue identity is still selectable as `<html data-brand="steel">`.
