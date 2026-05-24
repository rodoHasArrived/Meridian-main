---
module_id: SRC-UI-SHARED
owner: Meridian
status: active
last_verified: 2026-05-22
---

# Meridian UI Shared

## Module Purpose
Shared UI read models, compatibility shims, and cross-surface data structures.

## Ownership and Runtime
- Owner: Meridian
- Runtime lane: Meridian UI Shared

## Dependencies and Integrations
- Define shared operator-facing projection types.
- Support consistent dashboard and retained desktop data contracts.
- Dependency: `src/Meridian.Contracts`
- Dependency: `Consumers in src/Meridian.Ui.Services, src/Meridian.Ui/dashboard, and src/Meridian.Wpf`

## Operational Notes
Preserve cross-surface compatibility when evolving shared read models.
Chief of Staff orchestration endpoints and services are additive and must keep ledger/reconciliation source-of-truth services authoritative.
Workstation endpoint registration is split by domain through `WorkstationEndpoints.*.cs` partial files.
Keep the root `WorkstationEndpoints.cs` file as the coordinator, route new domain-specific endpoint edits to the matching partial file, and avoid concurrent branches that both modify the root coordinator or the shared `WorkstationEndpointsTests.cs` test body.
For operations-continuity and reconciliation endpoint changes, start with focused `MapWorkstationEndpoints_OperationsContinuity` / `MapWorkstationEndpoints_Reconciliation` filters before broad workstation endpoint validation.

<!-- GENERATED:MODULE_OVERVIEW BEGIN -->
Generated overview content is maintained by documentation automation.
<!-- GENERATED:MODULE_OVERVIEW END -->
