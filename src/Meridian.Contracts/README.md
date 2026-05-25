---
module_id: SRC-CONTRACTS
owner: Meridian
status: active
last_verified: 2026-05-21
---

# Meridian Contracts

## Module Purpose
Shared contracts and DTOs used across host, services, desktop, and dashboard surfaces.

## Ownership and Runtime
- Owner: Meridian
- Runtime lane: Meridian Contracts

## Dependencies and Integrations
- Define stable transport payloads and shared schema objects.
- Provide compatibility-safe contracts for inter-module integration.
- Dependency: `Consumers in host, UI services, shared UI read models, and clients.`
- Dependency: `Serialization/runtime libraries required for DTO transport.`

## Operational Notes
Treat additive and breaking changes as cross-module compatibility work.
Operations Continuity workflow DTOs publish the shared broker intake, Security Master, ledger
posting, reconciliation, approval, close, and audit vocabulary consumed by both browser and WPF
workstation clients. Keep returned workflow blocker codes in
`OperationsWorkflowContractMatrix.BlockerCodes`, including ledger journal context-validation
failures, so clients can handle command failures without parsing messages.

<!-- GENERATED:MODULE_OVERVIEW BEGIN -->
Generated overview content is maintained by documentation automation.
<!-- GENERATED:MODULE_OVERVIEW END -->
