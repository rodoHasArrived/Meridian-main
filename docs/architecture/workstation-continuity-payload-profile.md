# Workstation Continuity Payload Profile (Source of Truth)

This profile is the canonical payload contract for **Strategy**, **Trading**, **Accounting**, and
governance-readiness consumers.

## Canonical DTO surfaces

- `FundLedgerSummary` (`FundLedgerDtos.cs`)
- `ReconciliationRunSummary` (`ReconciliationDtos.cs`)
- `StrategyRunContinuityDto` (`StrategyRunReadModels.cs`)

These payloads are shared and must not fork per UI surface (web dashboard vs retained workstation shell).

## Compatibility guard

`LedgerReconciliationContractCompatibility` is the additive-only and required-field guard for
readiness, accounting, and governance-control paths:

- Required identity/presence fields are enforced for fund ledger and reconciliation summaries.
- Continuity payload must include `Run`, `Lineage`, and `ContinuityStatus`.
- Contract evolution policy: additive-only member changes on canonical DTO records.

## Serialization profile

All parity tests use `JsonSerializerDefaults.Web` to validate shared JSON shape, including camelCase member names expected by both consumers.

## Change policy

When changing these contracts:

1. Add new members only (non-breaking additive change).
2. Update compatibility tests in `tests/Meridian.Tests/Contracts/LedgerReconciliationContractCompatibilityTests.cs`.
3. Preserve required-field invariants used by readiness, accounting, and governance-control flows.
4. Avoid per-surface DTO variants unless a deliberate, reviewed versioning strategy is introduced.
