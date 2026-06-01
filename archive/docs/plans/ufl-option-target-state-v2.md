# UFL Option Capability Profile

**Owner:** Core Team
**Audience:** Product, architecture, domain, storage, and application contributors
**Last Updated:** 2026-05-28
**Status:** active UFL asset profile

## Summary

Option is a reference-heavy derivatives profile. The current Meridian baseline has canonical option terms, validation, and adjacent option runtime consumers. The next work is L3 projection safety for canonical contract identity, underlying linkage, provider-independent series/chain views, lifecycle, aliases, and adjusted-contract lineage.

This profile does not include OTC options, margin methodology, exercise processing, clearing integration, or full volatility-surface analytics.

## Evidence Boundary

### Implemented

- `SecurityKind.Option` exists in `src/Meridian.FSharp/Domain/SecurityMaster.fs`.
- `OptionContractSpec` and `OptionChainSnapshot` exist in `src/Meridian.Contracts/Domain/Models/`.
- Option subscriptions and endpoints already provide nearby runtime consumers.
- Current validation enforces `Put` or `Call`, positive strike, and positive multiplier.

### Partially Implemented

- Canonical option terms and runtime consumers exist, but option-series, lifecycle, alias, and adjusted-contract projections are not evidenced as delivered in this package.
- Provider chain ingestion exists as a concept, but canonical downstream chain projection remains target-state.

### Target-State Only

- Series-level option projection.
- Listed/active/expiring/expired/adjusted lifecycle projection.
- Underlying-link referential validation service.
- Provider-independent option reference endpoints.
- Adjusted-contract lineage and replay behavior.

### Explicitly Out of Scope

- OTC options.
- Margin methodology.
- Exercise processing and clearing integration.
- Full volatility-surface analytics.

## UFL Capability Profile

| Capability | Level | Current evidence | Target addition | Tests |
| --- | ---: | --- | --- | --- |
| InstrumentIdentity | L1 | `SecurityKind.Option` and validation exist. | canonical contract identity with expiry/strike normalization | F# validation and C# mapping tests |
| ProviderAlias | L1 partial | option chain models and provider consumers exist. | alias and provider-symbol resolution projection | chain normalization tests |
| UnderlyingLink | L1 | `OptionTerms.UnderlyingId` exists. | resolver and referential validation | mapping/service tests |
| Lifecycle | L0 | none named as delivered. | listed, active, expiring, expired, adjusted projection | lifecycle projection tests |
| CorporateAction | L0 | adjusted-contract lineage is target-state. | adjustment event and contract lineage projection | adjustment replay tests |
| ProjectionRebuild | L1 partial | shared Security Master rebuild exists. | option-scoped replay and checkpoint metadata | rebuild/checkpoint tests |

## Current Maturity

`L1/L2 partial`: canonical option terms and adjacent option runtime consumers exist. L3 projection safety is still target-state until series, lifecycle, alias, adjusted-contract, and rebuild tests exist.

## UFL Asset Profile

| Layer | Option profile |
| --- | --- |
| Canonical Core | `SecurityId`, `AssetClass = Option`, `CommonTerms`, underlying security ID, put/call, strike, expiry, multiplier, exchange, style, adjusted flag. |
| Capability Extensions | `ProviderAlias`, `UnderlyingLink`, `Lifecycle`, `CorporateAction` for adjustments, `ProjectionRebuild`. |
| Projection + Query | option contract snapshot, option series projection, option lifecycle projection, alias/resolution projection, adjusted-contract lineage projection. |
| Orchestration | chain normalization worker, underlying resolver, expiration worker, adjustment replay worker, asset-class-scoped rebuild. |

## Provider Payload Boundary

Provider chain payloads may be retained as source evidence and troubleshooting context. Trading, screening, Reporting, and workstation views must consume canonical option contracts, series, lifecycle state, aliases, and underlying links.

## Next Milestone Contract

**Goal:** advance options to L3 by normalizing provider chain data into canonical contract, series, alias, and lifecycle projections with underlying-link validation.

**Files likely touched:**

- `src/Meridian.FSharp/Domain/SecurityMaster.fs`
- `src/Meridian.Application/SecurityMaster/`
- `src/Meridian.Application/Options/`
- `src/Meridian.Contracts/Options/`
- `src/Meridian.Ui.Shared/Endpoints/`
- `tests/Meridian.Tests/`

**Acceptance evidence:**

- Option terms validation tests.
- Chain normalization tests.
- Underlying-link resolver tests.
- Projection and rebuild tests for series, alias, lifecycle, and adjustment state.
- Endpoint contract tests for canonical option reference reads.

**Exit criteria:** downstream option APIs read canonical projections and never require raw provider chain payloads.

## Related Documents

- [UFL Supported Asset Profiles](ufl-supported-assets-index.md)
- [UFL Capability Model](ufl-capability-model.md)
- [UFL Conformance Matrix](ufl-conformance-matrix.md)
- [UFL Projection and Evidence Kernel](ufl-projection-and-evidence-kernel.md)
