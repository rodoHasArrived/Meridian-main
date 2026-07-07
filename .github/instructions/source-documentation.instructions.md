---
applyTo: "src/**"
---

# Source Documentation Rules

Before editing source files:

1. Read the nearest source README.
2. Preserve boundaries in `docs/architecture/module-map.md`.
3. Keep operator UI work across the two active co-equal lanes — `src/Meridian.Ui/dashboard/` (browser) and `src/Meridian.Wpf/` (desktop) — both over shared contracts.
4. Put common product behavior behind shared contracts, API endpoints, or shared read models before composing it into the browser or retained WPF compatibility clients.
5. Link roadmap-affecting work to `docs/roadmap/data/roadmap-items.yml`.
6. Update source READMEs and source registries when public behavior, validation, diagrams, or TODOs change.
7. Do not hand-edit generated docs under `docs/roadmap/generated/` or `docs/source/generated/`.
