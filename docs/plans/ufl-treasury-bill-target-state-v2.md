# UFL Treasury Bill Capability Profile

**Owner:** Core Team
**Audience:** Product, architecture, domain, storage, and application contributors
**Last Updated:** 2026-05-28
**Status:** active UFL asset profile

## Summary

Treasury bill is a lifecycle/cashflow-heavy government-security profile. The current Meridian baseline has canonical T-bill terms, mapping, validation, and basic create support. The next work is L2/L3 reference and projection safety for auction metadata, maturity lifecycle, tenor/ladder views, and rebuild checkpoints.

This profile does not include Treasury notes, Treasury bonds, STRIPS, sovereign pricing analytics, or generalized government-auction workflow automation.

## Evidence Boundary

### Implemented

- `SecurityKind.TreasuryBill` and `TreasuryBillTerms` exist in `src/Meridian.FSharp/Domain/SecurityMaster.fs`.
- `SecurityMasterMapping` maps the `"TreasuryBill"` asset class.
- Security Master validation enforces nonnegative discount rate and auction-date ordering relative to maturity.
- `SecurityMasterAssetClassSupportTests` verifies basic create support for treasury bills.

### Partially Implemented

- T-bill canonical terms and basic create support exist, but T-bill-specific lifecycle, ladder, auction, endpoint, and checkpoint projections are not evidenced as delivered in this package.

### Target-State Only

- Government-security lifecycle projection.
- Tenor and ladder projection.
- Auction metadata projection.
- Treasury/reference query APIs.
- T-bill-scoped rebuild checkpointing.

### Explicitly Out of Scope

- Treasury notes and bonds.
- STRIPS.
- Sovereign pricing analytics.
- Generalized government-auction workflow automation.

## UFL Capability Profile

| Capability | Level | Current evidence | Target addition | Tests |
| --- | ---: | --- | --- | --- |
| InstrumentIdentity | L1 | `SecurityKind.TreasuryBill`, terms, mapping, and create tests exist. | canonical T-bill reference profile | F# validation and C# mapping tests |
| IssuerOrCounterparty | L0 | issuer is implicit government-security context. | additive issuer/government authority view | issuer projection tests |
| Lifecycle | L0 | none named as delivered. | active, maturing, matured, inactive projection | lifecycle projection tests |
| CashFlowSchedule | L1 partial | maturity and discount terms exist. | tenor/ladder projection for treasury operations | ladder projection tests |
| ProviderAlias | L0 | CUSIP can be present in terms. | provider and CUSIP alias projection | alias projection tests |
| ProjectionRebuild | L1 partial | shared Security Master rebuild exists. | T-bill-scoped replay and checkpoint metadata | rebuild/checkpoint tests |

## Current Maturity

`L1`: canonical T-bill terms, mapping, validation, and basic create tests exist. L2/L3 maturity requires dedicated reference endpoints, ladder/auction/lifecycle projections, and rebuild evidence.

## UFL Asset Profile

| Layer | Treasury bill profile |
| --- | --- |
| Canonical Core | `SecurityId`, `AssetClass = TreasuryBill`, `CommonTerms`, maturity, auction date, CUSIP, discount rate. |
| Capability Extensions | `ProviderAlias`, `IssuerOrCounterparty`, `Lifecycle`, `CashFlowSchedule`, `ProjectionRebuild`. |
| Projection + Query | T-bill snapshot, lifecycle projection, tenor/ladder projection, auction metadata projection, alias projection. |
| Orchestration | lifecycle sweep, ladder refresh, auction metadata enrichment, asset-class-scoped rebuild, checkpoint persistence. |

## Provider Payload Boundary

Auction or provider payloads may be retained as evidence. Treasury, Accounting, Reporting, and workstation views must consume canonical T-bill terms, auction metadata projections, lifecycle projections, and ladder projections.

## Next Milestone Contract

**Goal:** advance treasury bills to L2/L3 by exposing canonical T-bill reference reads and adding lifecycle, ladder, auction, and checkpoint projections.

**Files likely touched:**

- `src/Meridian.FSharp/Domain/SecurityMaster.fs`
- `src/Meridian.Application/Treasury/`
- `src/Meridian.Contracts/Treasury/`
- `src/Meridian.Ui.Shared/Endpoints/`
- `tests/Meridian.Tests/`

**Acceptance evidence:**

- Security Master mapping and validation tests.
- Treasury reference endpoint tests.
- Lifecycle, auction, and ladder projection tests.
- Rebuild/checkpoint tests proving deterministic replay.

**Exit criteria:** T-bill target-state claims identify exact projection and endpoint tests before moving beyond L1.

## Related Documents

- [UFL Supported Asset Profiles](ufl-supported-assets-index.md)
- [UFL Capability Model](ufl-capability-model.md)
- [UFL Conformance Matrix](ufl-conformance-matrix.md)
- [UFL Projection and Evidence Kernel](ufl-projection-and-evidence-kernel.md)
