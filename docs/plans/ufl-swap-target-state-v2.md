# UFL Swap Capability Profile

**Owner:** Core Team
**Audience:** Product, architecture, domain, storage, and application contributors
**Last Updated:** 2026-05-29

## TODO Checklist (Concrete Implementation Items)
- [ ] Define scope boundaries for **ufl swap target state v2** and document explicit in-scope vs out-of-scope items.
- [ ] Break delivery into PR-sized milestones with owner, dependency, and evidence artifact for each milestone.
- [ ] Implement the first milestone in code/config/scripts and link the exact validating test or command output.
- [ ] Add/update operator runbook steps and rollback procedure for the ufl swap target state v2 workflow.
- [ ] Record completion evidence in `docs/status/` (or linked packet) and mark corresponding checklist items done.

**Status:** active
**Reviewed:** 2026-03-26

> **Naming standard:** All new F# types and DTOs in this package must follow the
> [Domain Naming Standard](../ai/claude/CLAUDE.domain-naming.md).
> Swaps: definition record → `SwapDef`; fixed leg rate → `FixedRate: decimal option`; floating index → `FloatIdx: string option`; notional → `NotionalAmt: decimal option`.

## Summary

This document captures the target-state V2 package for `UFL` swap assets inside Meridian's broader security-master, derivatives, valuation, and governance expansion.

It assumes:

- a modular monolith
- canonical swap definitions stored in security master
- leg-level, lifecycle, and valuation-reference views modeled as projections
- replay-safe rebuilds across effective dates, maturity dates, and swap-leg metadata
- downstream governance, reconciliation, and pricing consumers querying canonical projections

This package turns the existing `SwapTerms` and `SwapLeg` support into an implementation-ready plan for swap reference data, lifecycle handling, leg projections, and APIs.

## Evidence Boundary

### Implemented

- `SecurityKind.Swap`, `SwapTerms`, and `SwapLeg` exist in `src/Meridian.FSharp/Domain/SecurityMaster.fs`.
- `SecurityMasterMapping` maps the `"Swap"` asset class and materializes swap legs from JSON.
- Security Master validation enforces ordered effective and maturity dates, at least one leg, and nonblank leg type and currency values.
- `src/Meridian.Application/Derivatives/ISwapReferenceService.cs` and `SwapProjectionService` define swap reference reads.
- `src/Meridian.Ui.Shared/Endpoints/SwapReferenceEndpoints.cs` exposes swap reference-data endpoints.
- Security Master reference lookup tests cover `Swap` subtype derivation.

### Partially Implemented

- Canonical swap terms, legs, and reference surfaces exist, but lifecycle, valuation-reference, collateral, unwind, and rebuild-specific projection evidence remains partial.

### Target-State Only

- Swap lifecycle and leg projections beyond current reference reads.
- Valuation-reference and index exposure views.
- Novation and unwind state overlays.
- Swap-specific accounting and reconciliation handoff.

### Explicitly Out of Scope

- CSA management.
- Collateral optimization.
- XVA.
- Clearing integrations.
- Full pricing-engine implementation.

## UFL Capability Profile

| Capability | Level | Current evidence | Target addition | Tests |
| --- | ---: | --- | --- | --- |
| InstrumentIdentity | L1 | `SecurityKind.Swap`, terms, leg mapping, validation, and reference services exist. | canonical swap profile metadata and stricter validation evidence | F# validation and C# mapping tests |
| IssuerOrCounterparty | L1 partial | counterparty-oriented swap terms can be modeled through the profile. | counterparty linkage and exposure projection | endpoint/projection tests |
| Lifecycle | L0 | target-state only. | active, matured, novated, unwound, and terminated states | lifecycle projection tests |
| CashFlowSchedule | L1 partial | leg terms exist. | leg schedule and reset/reference projections | schedule projection tests |
| CollateralOrMargin | L0 | target-state only. | collateral and margin metadata if required by governed workflows | collateral tests |
| ProjectionRebuild | L1/L2 partial | swap projection service and endpoint shape exist. | swap leg, lifecycle, exposure, valuation, and rebuild metadata projections | rebuild/checkpoint tests |
| AccountingImpact | L0 | target-state only. | controlled accounting/reconciliation handoff for swap lifecycle events | accounting/reconciliation tests |

## Current Maturity

`L1/L2 partial`: canonical swap terms, legs, validation, and reference surfaces exist. L3 maturity requires leg, lifecycle, valuation, exposure, and rebuild metadata tests.

## Next Milestone Contract

**Goal:** advance swaps toward L3 by adding rebuild-safe leg, lifecycle, valuation-reference, counterparty, and exposure projections.

**Files likely touched:**

- `src/Meridian.FSharp/Domain/SecurityMaster.fs`
- `src/Meridian.Application/Derivatives/`
- `src/Meridian.Contracts/Derivatives/`
- `src/Meridian.Ui.Shared/Endpoints/`
- `tests/Meridian.Tests/`

**Acceptance evidence:**

- validation and mapping tests for swap terms and legs.
- endpoint contract tests for swap reference, type, and maturity reads.
- projection/rebuild tests for leg, lifecycle, exposure, valuation, and source-event metadata.
- provider-payload isolation tests for canonical swap projections.

**Exit criteria:** swap consumers can query canonical leg, lifecycle, counterparty, and maturity views with deterministic rebuild evidence.

## Provider Payload Boundary

Provider payloads may be retained as evidence, but swap workflows must consume canonical swap terms, leg projections, counterparty/exposure projections, and rebuild metadata.

## Repo Fit

### Verified Meridian constraints

- Meridian already models `SecurityKind.Swap`, `SwapTerms`, and `SwapLeg` in `src/Meridian.FSharp/Domain/SecurityMaster.fs`.
- `SecurityMasterMapping` already maps the `"Swap"` asset class and materializes swap legs from JSON.
- security-master validation already enforces ordered effective and maturity dates, at least one leg, and nonblank leg type and currency values.
- ledger, reconciliation, and governance planning already make swap lifecycle and leg-level projections valuable downstream.

### Proposed UFL-specific additions

- swap lifecycle and leg projections
- valuation-reference and index exposure views
- novation and unwind state overlays
- swap-specific query contracts and endpoints

### Suggested Meridian mapping if implemented in-place

- F# domain support in `src/Meridian.FSharp/Domain/`
- application services in `src/Meridian.Application/Derivatives/`
- contracts in `src/Meridian.Contracts/Derivatives/`
- storage in `src/Meridian.Storage/SecurityMaster/`
- endpoints in `src/Meridian.Ui.Shared/Endpoints/`

## Scope

**In Scope:** canonical swap identity, leg metadata, effective and maturity dates, lifecycle state, valuation-reference views, replay-safe rebuilds, and derivative/reference APIs.

**Out of Scope:** CSA management, collateral optimization, XVA, clearing integrations, and full pricing-engine implementation.

## Knowledge Graph

```mermaid
flowchart TD
    Swap["Swap Aggregate"] --> SwapEvents["Security Master Events"]
    SwapEvents --> Outbox["Transactional Outbox"]
    SwapEvents --> Snapshot["Swap Snapshot Projection"]

    Snapshot --> Legs["Swap Leg Projection"]
    Snapshot --> Lifecycle["Lifecycle Projection"]
    Snapshot --> Exposure["Index / Currency Exposure Projection"]
    Snapshot --> Valuation["Valuation Reference Projection"]

    Legs --> Pricing["Pricing / Analytics"]
    Lifecycle --> Governance["Accounting / Reporting workstation"]
    Exposure --> Recon["Reconciliation / Risk"]
    Valuation --> APIs["Derivative Reference APIs"]
```

## 1. Architecture Blueprint

### 1.1 System shape

**Write side**

- canonical swap aggregate via security master
- lifecycle and novation overlay boundary
- valuation-reference projection boundary

**Read side**

- current swap snapshot
- swap-leg snapshot
- lifecycle snapshot
- valuation-reference snapshot
- exposure snapshot

**Processing**

- security create/amend/deactivate handlers
- swap-leg projection worker
- lifecycle-state worker
- valuation-reference worker
- rebuild orchestration

### 1.2 Design principles

1. A swap is a canonical derivative identity with immutable leg structure plus additive lifecycle state.
2. Leg-level projections should be first class because downstream consumers reason on legs, not just the top-level contract.
3. Lifecycle state should capture active, novated, unwinding, and matured states without rewriting canonical terms.
4. Valuation-reference consumers should read normalized projections rather than raw source payloads.
5. Future derivative extensions should preserve the base swap shape wherever possible.

## 2. F# Aggregate and Domain Shapes

### 2.1 Shared kernel

```fsharp
type SwapId = SecurityId

type SwapLifecycleState =
    | Pending
    | Active
    | Novated
    | Unwinding
    | Matured
    | Inactive
```

### 2.2 Swap aggregate

The canonical instrument definition remains:

```fsharp
type SwapLeg = {
    LegType: string
    Currency: string
    Index: string option
    FixedRate: decimal option
}

type SwapTerms = {
    EffectiveDate: DateOnly
    MaturityDate: DateOnly
    Legs: SwapLeg list
}
```

Proposed additive projection shapes:

```fsharp
type SwapLifecycleProjection = {
    SecurityId: SecurityId
    State: SwapLifecycleState
    EffectiveDate: DateOnly
    MaturityDate: DateOnly
}

type SwapLegProjection = {
    SecurityId: SecurityId
    LegType: string
    Currency: string
    Index: string option
    FixedRate: decimal option
}
```

### 2.3 Projection lineage model

- security-master events rebuild canonical swap terms
- lifecycle overlays rebuild active, novated, unwinding, and matured views
- leg and valuation projections rebuild exposure and pricing-reference views

## 3. Event Catalog

### 3.1 Domain events

- `SecurityCreated`
- `TermsAmended`
- `SecurityDeactivated`
- `SwapLifecycleStateChanged`
- `SwapLegsProjected`
- `SwapValuationReferenceProjected`

### 3.2 Process events

- `SwapLifecycleSweepCompleted`
- `SwapProjectionRebuildCompleted`
- `SwapValuationRefreshCompleted`

### 3.3 Event naming and versioning policy

- align base derivative-definition events with security master
- version lifecycle and valuation-reference payloads independently from definition payloads
- include source system and effective timestamp on all overlays and projection records

## 4. SQL DDL Design

### 4.1 Core table groups

- `security_master_projection`
- `swap_projection`
- `swap_leg_projection`
- `swap_lifecycle_projection`
- `swap_exposure_projection`
- `swap_valuation_reference_projection`

### 4.2 Implementation notes

- leg projections should index by security ID and leg type
- lifecycle projections should index by effective date, maturity date, and current state
- valuation-reference projections should preserve the source event lineage used for rebuilds

## 5. Service Boundaries

### 5.1 Swap Reference module

- owns canonical swap query APIs

### 5.2 Lifecycle module

- owns pending, active, novated, unwinding, and matured state projections

### 5.3 Leg / Valuation Reference module

- owns leg-level, exposure, and pricing-reference views

### 5.4 Platform module

- owns rebuild orchestration and outbox dispatch

## 6. Core Workflows

### 6.1 Create swap

1. create canonical swap in security master
2. persist `SecurityCreated`
3. rebuild snapshot and leg projections
4. attach lifecycle and valuation-reference views

### 6.2 Amend swap terms

1. amend common or swap-specific terms
2. persist `TermsAmended`
3. rebuild leg, lifecycle, and valuation-reference views

### 6.3 Evaluate lifecycle state

1. compare as-of date to effective and maturity dates
2. apply novation or unwind overlays if present
3. rebuild lifecycle projection and publish outbox event

### 6.4 Refresh leg and valuation views

1. normalize leg metadata
2. rebuild leg and exposure projections
3. refresh valuation-reference views for downstream consumers

### 6.5 Read-model rebuild

1. replay canonical security events
2. replay lifecycle overlays
3. replay leg and valuation-reference events
4. checkpoint rebuilt projections

## 7. Phase Sequence

### 7.1 Phase 1 goal

Deliver canonical swap identity, lifecycle and leg projections, and derivative/reference APIs.

### 7.2 Phase 1 implementation order

1. add swap DTOs and query contracts
2. add leg, lifecycle, and valuation-reference projection tables
3. implement swap reference service
4. implement lifecycle and leg-projection services
5. expose swap reference endpoints
6. add lifecycle and leg rebuild tests

### 7.3 Phase 1 exit criteria

- swaps query through canonical APIs
- lifecycle and leg views rebuild deterministically
- governance, reconciliation, and pricing consumers can rely on canonical projections

### 7.4 Phase 2 goals

- novation and unwind overlays
- richer valuation-reference metadata
- deeper reconciliation and governance tooling

## 8. Target API Surface

### 8.1 Reference

- `GET /api/security-master/swaps/{securityId}`
- `GET /api/security-master/swaps/search`

### 8.2 Lifecycle

- `GET /api/security-master/swaps/{securityId}/lifecycle`

### 8.3 Legs / valuation

- `GET /api/security-master/swaps/{securityId}/legs`
- `GET /api/security-master/swaps/{securityId}/valuation-reference`

## 9. Proposed Repo Structure

```text
src/
  Meridian.Application/
    Derivatives/
      ISwapReferenceService.cs
      SwapReferenceService.cs
      ISwapLifecycleService.cs
      SwapLifecycleService.cs
  Meridian.Contracts/
    Derivatives/
      SwapDtos.cs
  Meridian.Storage/
    SecurityMaster/
      SwapProjectionStore.cs
  Meridian.Ui.Shared/
    Endpoints/
      SwapEndpoints.cs
tests/
  Meridian.Tests/
    Derivatives/
    SecurityMaster/
```

## 10. Recommended First Ten Implementation Tickets

1. Add swap DTOs and query contracts.
2. Add leg and lifecycle projection records.
3. Add exposure and valuation-reference projection records.
4. Implement swap reference service.
5. Implement lifecycle and leg-projection services.
6. Expose swap reference endpoints.
7. Add leg validation and serialization tests.
8. Add lifecycle-state sweep tests.
9. Add rebuild orchestration coverage.
10. Add governance and reconciliation swap-reference views.

## 11. Final Target State

Meridian treats a swap as a canonical derivative identity with explainable leg structure, lifecycle state, and valuation-reference metadata. Accounting, reconciliation, and pricing consumers all use the same rebuilt reference model.

## Related Documents

- [UFL Supported Asset Profiles](ufl-supported-assets-index.md)
- [UFL Direct Lending Capability Profile](ufl-direct-lending-target-state-v2.md)
- [Governance and Fund Operations Blueprint](governance-fund-ops-blueprint.md)
