# UFL Equity Capability Profile

**Owner:** Core Team
**Audience:** Product, architecture, domain, storage, and application contributors
**Last Updated:** 2026-05-28
**Status:** active UFL asset profile

## Summary

Equity is a reference-heavy UFL profile with a future operational accounting lane for corporate actions. The current Meridian baseline has canonical equity terms, classification support, and reference reads. The next work is L3 projection safety for lifecycle, aliases, preferred/convertible terms, and normalized corporate-action evidence before any L5 accounting claim.

Warrants are modeled as their own `SecurityKind.Warrant` package, not as an equity classification in this profile.

## Evidence Boundary

### Implemented

- `SecurityKind.Equity`, `EquityTerms`, `PreferredTerms`, `ConvertibleTerms`, and `EquityClassification` exist in `src/Meridian.FSharp/Domain/SecurityMaster.fs`.
- Common equity remains valid when `EquityTerms.Classification` is omitted.
- Preferred and convertible-preferred shapes are covered by F# domain tests.
- Shared reference endpoints expose canonical equity reads through `/api/reference-data/equities/*`.

### Partially Implemented

- Equity reference reads exist, but equity-specific lifecycle, alias, preferred-term, convertible-term, and corporate-action projections are not evidenced as complete in this package.
- Corporate-action accounting is a documented target flow, not a delivered L5 claim for all equity actions.

### Target-State Only

- Equity lifecycle and alias-resolution projections.
- Preferred-term and convertible-term projection tables.
- Dividend schedule and conversion parity read models.
- Corporate-action accounting preview, approval, posting, and reconciliation evidence.

### Explicitly Out of Scope

- Warrant lifecycle, which belongs in [UFL Warrant Capability Profile](ufl-warrant-target-state-v2.md).
- Market-making, locate, borrow, margin, or short-sale workflows.
- Native mobile or mobile-first workflows.

## UFL Capability Profile

| Capability | Level | Current evidence | Target addition | Tests |
| --- | ---: | --- | --- | --- |
| InstrumentIdentity | L1 | `SecurityKind.Equity`, `EquityTerms`, and classification types exist. | canonical equity profile with share-class metadata | F# validation and C# mapping tests |
| ProviderAlias | L2 partial | equity reference endpoints exist. | alias-resolution projection with provider provenance | endpoint and alias projection tests |
| IssuerOrCounterparty | L0 | none named as delivered in this package. | issuer and listing-venue projection | issuer/listing projection tests |
| UnderlyingLink | L1 partial | convertible terms include `UnderlyingSecurityId`. | referential validation and conversion-link projection | mapping/service tests |
| Lifecycle | L0 | none named as delivered. | listed, suspended, delisted, inactive projection | lifecycle projection tests |
| CorporateAction | L0 | target flow documented. | normalized event projection and accounting-impact preview | corporate-action tests |
| AccountingImpact | L0 | target flow documented. | draft journals, approvals, reconciliation/reporting evidence | journal/reconciliation tests |
| ProjectionRebuild | L1 partial | shared Security Master rebuild exists. | equity-scoped replay and checkpoint metadata | rebuild/checkpoint tests |

## Current Maturity

`L1/L2 partial`: canonical equity terms, classification support, and reference reads exist. Equity-specific lifecycle, corporate-action, accounting, and projection rebuild safety remain target-state.

## UFL Asset Profile

| Layer | Equity profile |
| --- | --- |
| Canonical Core | `SecurityId`, `AssetClass = Equity`, `CommonTerms`, share class, voting rights, common/preferred/convertible classification. |
| Capability Extensions | `ProviderAlias`, optional `UnderlyingLink` for convertibles, `Lifecycle`, `CorporateAction`, future `AccountingImpact`. |
| Projection + Query | equity snapshot, alias projection, lifecycle projection, preferred/convertible term projection, corporate-action evidence projection. |
| Orchestration | asset-class-scoped rebuild, corporate-action normalization, approval-gated journal workflow only after L3 projections are stable. |

## Provider Payload Boundary

Provider corporate-action feeds, listing symbols, and reference payloads may be retained as evidence. Downstream equity workflows must consume normalized corporate-action terms, canonical Security Master identities, and canonical projections before any accounting or reporting action.

## Next Milestone Contract

**Goal:** advance equities to L3 by adding canonical lifecycle, alias, preferred-term, convertible-term, and corporate-action evidence projections before claiming accounting integration.

**Files likely touched:**

- `src/Meridian.FSharp/Domain/SecurityMaster.fs`
- `src/Meridian.Application/SecurityMaster/`
- `src/Meridian.Contracts/SecurityMaster/`
- `src/Meridian.Ui.Shared/Endpoints/`
- `tests/Meridian.Tests/`

**Acceptance evidence:**

- F# validation tests for preferred and convertible terms.
- C# mapping and endpoint tests for equity reference reads.
- Projection tests for lifecycle, aliases, and preferred/convertible reads.
- Corporate-action preview tests before journal posting is claimed.

**Exit criteria:** no L5 equity claim is marked delivered until balanced journals and reconciliation evidence are tested.

## Related Documents

- [UFL Supported Asset Profiles](ufl-supported-assets-index.md)
- [UFL Capability Model](ufl-capability-model.md)
- [UFL Conformance Matrix](ufl-conformance-matrix.md)
- [UFL Accounting Impact Model](ufl-accounting-impact-model.md)
