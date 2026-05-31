# UFL Bond Capability Profile

**Owner:** Core Team
**Audience:** Product, architecture, domain, storage, and application contributors
**Last Updated:** 2026-05-28
**Status:** active UFL asset profile

## Summary

Bond is the reference pattern for UFL asset profiles. It uses the shared architecture from [UFL Capability Model](ufl-capability-model.md): canonical core, capability extensions, projection/query, and orchestration. The current Meridian baseline has canonical bond terms and mapping. The next work is L3 projection safety for lifecycle, accrual convention, issuer, maturity ladder, and rebuild evidence.

This profile does not introduce fixed-income pricing, yield analytics, MBS/ABS modeling, portfolio state, PnL, or execution workflows.

## Evidence Boundary

### Implemented

- `SecurityKind.Bond` and `BondTerms` exist in `src/Meridian.FSharp/Domain/SecurityMaster.fs`.
- `SecurityMasterMapping` maps the `"Bond"` asset class.
- Current validation rejects negative coupon values when a coupon is provided.
- Shared reference-data surfaces can consume canonical Security Master projection rows.

### Partially Implemented

- Bond reference data has canonical terms and mapping, but bond-specific lifecycle, accrual, issuer, and maturity-ladder projections are not evidenced as delivered in this package.
- Rebuild infrastructure exists through the shared Security Master pipeline, but bond-scoped replay/checkpoint behavior remains target-state.

### Target-State Only

- Bond lifecycle projection.
- Bond accrual-convention projection.
- Issuer and maturity-ladder read models.
- Bond-specific rebuild checkpoints and fixed-income reference APIs.
- Extensible subclass model for treasury, corporate, municipal, and securitized variants.

### Explicitly Out of Scope

- Fixed-income pricing/yield analytics.
- Callable/puttable schedule engines beyond extension hooks.
- MBS/ABS/structured-product modeling in this profile.
- Portfolio positions, PnL, or execution workflows.

## UFL Capability Profile

| Capability | Level | Current evidence | Target addition | Tests |
| --- | ---: | --- | --- | --- |
| InstrumentIdentity | L1 | `SecurityKind.Bond`, `BondTerms`, and `"Bond"` mapping exist. | canonical bond reference profile with subclass-aware common terms | F# validation and C# mapping tests |
| IssuerOrCounterparty | L0 | issuer name is present in terms only. | issuer lineage and normalized issuer projection | issuer mapping/projection tests |
| Lifecycle | L0 | none named as delivered. | `Issued`, `Active`, `Matured`, `Inactive` projection | lifecycle projection tests |
| CashFlowSchedule | L1 partial | maturity and coupon terms exist. | coupon/maturity schedule profile for downstream reads | fixed-income schedule tests |
| AccrualConvention | L1 partial | coupon/day-count values exist in bond terms. | accrual-convention read model and endpoint | fixed-income projection tests |
| ProjectionRebuild | L1 partial | shared Security Master rebuild exists. | bond-scoped replay, checkpoint, and evidence metadata | rebuild/checkpoint tests |
| AccountingImpact | L0 | ledger consumers can read reference data. | future accrual/accounting preview integration | accounting preview tests when pursued |

## Current Maturity

`L1/L2 partial`: canonical bond terms and mapping exist, and shared reference surfaces can expose canonical records. Bond-specific L3 projection safety is still target-state until lifecycle, accrual, issuer, maturity-ladder, and checkpoint tests land.

## UFL Asset Profile

| Layer | Bond profile |
| --- | --- |
| Canonical Core | `SecurityId`, `AssetClass = Bond`, `CommonTerms`, maturity, coupon, day count, callable flag, issuer name, seniority, subclass. |
| Capability Extensions | `IssuerOrCounterparty`, `Lifecycle`, `CashFlowSchedule`, `AccrualConvention`, `ProjectionRebuild`; optional future `AccountingImpact`. |
| Projection + Query | bond snapshot, lifecycle projection, accrual-convention projection, issuer projection, maturity-ladder projection. |
| Orchestration | asset-class-scoped rebuild, checkpoint persistence, issuer enrichment, lifecycle sweep, outbox dispatch when workflow events are added. |

## Provider Payload Boundary

Provider or issuer payloads may be retained as evidence for enrichment and troubleshooting. Ledger, Reporting, Accounting, and workstation consumers must read canonical bond identities, terms, issuer links, and projections, not provider payloads directly.

## Next Milestone Contract

**Goal:** advance bonds to L3 by adding deterministic bond lifecycle, accrual-convention, issuer, and maturity-ladder projections over canonical Security Master terms.

**Files likely touched:**

- `src/Meridian.FSharp/Domain/SecurityMaster.fs`
- `src/Meridian.Application/SecurityMaster/`
- `src/Meridian.Contracts/SecurityMaster/`
- `src/Meridian.Storage/SecurityMaster/`
- `src/Meridian.Ui.Shared/Endpoints/`
- `tests/Meridian.Tests/`

**Acceptance evidence:**

- F# validation tests for bond terms and subclass invariants.
- C# mapping tests for canonical bond payloads.
- Projection tests for lifecycle, accrual, issuer, and maturity ladder.
- Rebuild/checkpoint tests proving deterministic replay.
- Endpoint contract tests for fixed-income reference reads.

**Exit criteria:** no bond L3 claim is marked delivered until projection rows carry lineage and rebuild evidence.

## Related Documents

- [UFL Supported Asset Profiles](ufl-supported-assets-index.md)
- [UFL Capability Model](ufl-capability-model.md)
- [UFL Conformance Matrix](ufl-conformance-matrix.md)
- [UFL Projection and Evidence Kernel](ufl-projection-and-evidence-kernel.md)
