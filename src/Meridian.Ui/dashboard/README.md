---
id: meridian-ui-dashboard
title: Meridian UI Dashboard
layer: ui_dashboard
state: active
canonical_path: src/Meridian.Ui/dashboard
architecture_boundary: browser-operator-workstation
roadmap_anchor: W2-paper-trading-cockpit
---

# Meridian UI Dashboard

## Purpose
Browser-based operator workstation frontend for active web UI delivery.

## Responsibilities
- Render operator workflows and workstation experiences in the browser.
- Consume UI services and shared read models for trading and operations surfaces.

## Key dependencies
- `src/Meridian.Ui.Services`
- `src/Meridian.Ui.Shared`

## Notes
This is the active operator UI lane; keep shared contract parity with retained desktop.
