---
module_id: SRC-UI-DASHBOARD
owner: Meridian
status: active
last_verified: 2026-05-21
---

# Meridian UI Dashboard

## Module Purpose
Browser-based operator workstation frontend for active web UI delivery.

## Ownership and Runtime
- Owner: Meridian
- Runtime lane: Meridian UI Dashboard

## Dependencies and Integrations
- Render operator workflows and workstation experiences in the browser.
- Consume UI services and shared read models for trading and operations surfaces.
- Dependency: `src/Meridian.Ui.Services`
- Dependency: `src/Meridian.Ui.Shared`

## Operational Notes
This is the active operator UI lane; keep shared contract parity with retained desktop.
Security Master Governance detail uses the workstation trust snapshot's `scheduleBook` and
`openLotReadModel` projections for cash-flow schedules, factor provenance, and open-lot exposure
review.

<!-- GENERATED:MODULE_OVERVIEW BEGIN -->
Generated overview content is maintained by documentation automation.
<!-- GENERATED:MODULE_OVERVIEW END -->
