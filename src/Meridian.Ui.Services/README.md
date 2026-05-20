---
id: meridian-ui-services
title: Meridian UI Services
layer: ui_services
state: active
canonical_path: src/Meridian.Ui.Services
architecture_boundary: api-services-and-workstation-endpoints
roadmap_anchor: W2-paper-trading-cockpit
---

# Meridian UI Services

## Purpose
Backend UI service endpoints and orchestrators for workstation-facing operator workflows.

## Responsibilities
- Serve workstation API endpoints and read-model projections.
- Aggregate readiness, workflow, and operations data for UI consumption.

## Key dependencies
- `src/Meridian.Application`
- `src/Meridian.Ui.Shared`

## Notes
Keep endpoint contracts aligned with shared UI models and compatibility gates.
