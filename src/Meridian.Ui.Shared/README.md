---
id: meridian-ui-shared
title: Meridian UI Shared
layer: ui_shared
state: active
canonical_path: src/Meridian.Ui.Shared
architecture_boundary: shared-ui-read-models-and-contract-shims
roadmap_anchor: W2-paper-trading-cockpit
---

# Meridian UI Shared

## Purpose
Shared UI read models, compatibility shims, and cross-surface data structures.

## Responsibilities
- Define shared operator-facing projection types.
- Support consistent dashboard and retained desktop data contracts.

## Key dependencies
- `src/Meridian.Contracts`
- Consumers in `src/Meridian.Ui.Services`, `src/Meridian.Ui/dashboard`, and `src/Meridian.Wpf`

## Notes
Preserve cross-surface compatibility when evolving shared read models.
