# UFL Capability Model

**Owner:** Core Team
**Audience:** Product, architecture, domain, storage, application, and workstation contributors
**Last Updated:** 2026-05-29
**Status:** active UFL foundation

## Summary

UFL is a capability and conformance framework first, and a set of asset-profile documents second. Asset profiles should be thin deltas over a common UFL model: they state which capabilities apply, what maturity level is currently evidenced, what target additions remain, and what tests prove the next milestone.

Provider payloads may be retained as evidence, but downstream UFL workflows must consume canonical Security Master identities, canonical terms, and canonical projections.

## Shared Framework

Every UFL asset follows the same common architecture before it adds asset-specific behavior:

1. **Identity and terms:** Security Master owns canonical `SecurityId`, asset class, common terms, asset-specific terms, aliases, issuer or counterparty links, and underlying links.
2. **Lifecycle and validation:** deterministic validators enforce required fields, date ordering, valid enum values, lifecycle state, and promotion or review guardrails.
3. **Projection and rebuild:** shared rebuild orchestration projects canonical terms into asset-scoped read models with checkpoints, source-event metadata, correlation IDs, and rebuild sequence.
4. **Evidence and accounting:** assets that need fund-operations depth add accounting-impact previews, journal drafts, reconciliation links, report evidence, and period-control behavior through the shared AccountingImpact capability.
5. **Workstation controls:** operator review, approval, correction, rollback, promotion, and evidence handling stay in governed workstation surfaces, not provider-specific adapters.

The asset document is the profile over this framework. It must say which capabilities are active, which are target-state only, and which tests or evidence would move the asset to the next maturity level.

## Capability Set

| Capability | Purpose | Typical assets |
| --- | --- | --- |
| InstrumentIdentity | Canonical security identity, asset class, display name, currency, and status. | All assets |
| ProviderAlias | External symbols, provider IDs, source provenance, and alias resolution. | Equities, options, bonds, crypto |
| IssuerOrCounterparty | Issuer, borrower, counterparty, fund family, or institution linkage. | Bonds, CDs, deposits, loans, repos |
| UnderlyingLink | Canonical link from a derivative or related security to its underlying. | Options, warrants, CFDs, convertibles |
| Lifecycle | Active, matured, expired, adjusted, called, defaulted, closed, or review state. | Bonds, options, T-bills, loans |
| TermsVersioning | Versioned economic terms with provenance and effective dates. | Direct loans, swaps, preferred equity |
| CashFlowSchedule | Expected payments, coupons, amortization, maturities, and projected flows. | Bonds, loans, CDs, swaps |
| AccrualConvention | Day count, coupon, rate index, spread, reset, and convention metadata. | Bonds, CDs, loans, deposits |
| CorporateAction | Splits, dividends, conversions, redemptions, and contract adjustments. | Equities, options, warrants |
| CollateralOrMargin | Collateral, haircut, exposure, and margin metadata. | Repos, swaps, CFDs |
| AccountingImpact | Draft journals, approvals, ledger links, reconciliation, and reporting evidence. | Direct lending, equity actions, bonds |
| ProjectionRebuild | Replay-safe read-model rebuilds, checkpoints, lineage, and deterministic outputs. | All supported UFL assets |
| WorkstationControl | Operator review, approval, correction, rollback, and evidence handling. | Accounting, Reporting, Data |

## Maturity Levels

| Level | Name | Meaning |
| ---: | --- | --- |
| L0 | Cataloged | Asset appears in roadmap or docs only. |
| L1 | Canonical Terms | `SecurityKind`, terms, mapping, and validation exist. |
| L2 | Reference Read | Stable DTOs or endpoints expose canonical reference data. |
| L3 | Projection Safe | Rebuildable projections, checkpoints, lineage, and replay tests exist. |
| L4 | Operational Workflow | Operator actions, approval/review, correction, and audit trail exist. |
| L5 | Accounting/Reconciliation Integrated | Journals, period controls, reconciliation, and reporting evidence exist. |

Use partial levels when current evidence is mixed, for example `L1/L2 partial`. A target-state description is not a maturity claim unless the package names current code and test evidence.

## Architectural Lanes

| Lane | Owner | Includes |
| --- | --- | --- |
| Lane A - UFL Reference Kernel | Security Master | canonical identity, terms, asset class, aliases, issuer/counterparty, underlying links, validation, read APIs |
| Lane B - UFL Projection and Evidence Kernel | Shared application/storage infrastructure | rebuild orchestration, projection checkpoints, event lineage, source provenance, replay-safe read models, status/evidence artifacts |
| Lane C - Asset-Specific Operations | Vertical modules when needed | loan servicing, servicer ingestion, corporate-action accounting, repo exposure, swap leg references, option chain lifecycle, bond accrual/lifecycle extensions |

Reference-heavy assets should usually advance Lane A before deep operational workflows. Direct lending is intentionally deeper because servicing, projection, accounting, reconciliation, and servicer-ingestion behavior are core to the asset.

## Conformance Rules

- A `SecurityKind` case, parser alias, or DTO alone is not enough to claim full UFL maturity.
- L1 requires canonical terms and deterministic validation through the Security Master path.
- L2 requires stable read contracts or endpoints over canonical reference data.
- L3 requires asset-scoped projection metadata, deterministic rebuild behavior, checkpoints, and replay tests.
- L4 requires workstation controls for review, approval, correction, rollback, and audit evidence.
- L5 requires accounting-impact previews, journal/reconciliation links, period-control handling, and reporting evidence.
- Provider payloads can support evidence and troubleshooting, but conformance claims must be based on canonical terms, canonical aliases, and canonical projections.
- Use `partial` when a capability exists in one path but lacks projection, endpoint, rebuild, or test evidence across the asset profile.

## Asset Profile Shape

Every asset profile should use the same architecture:

```text
UFL Asset Profile
|-- Canonical Core
|   |-- SecurityId
|   |-- AssetClass
|   |-- CommonTerms
|   `-- Required economics
|-- Capability Extensions
|   |-- UnderlyingLink
|   |-- Lifecycle
|   |-- CashFlowSchedule
|   |-- AccountingImpact
|   `-- CollateralOrMargin
|-- Projection + Query
|   |-- Current snapshot
|   |-- Lifecycle projection
|   |-- Alias projection
|   `-- Evidence projection
`-- Orchestration
    |-- Rebuild
    |-- Checkpoints
    |-- Outbox
    `-- Operator workflow
```

## Required Asset Sections

Each converted asset profile must include:

- `Evidence Boundary`
- `UFL Capability Profile`
- `Current Maturity`
- `Next Milestone Contract`
- `Provider Payload Boundary`

The details of each section are defined in [UFL Asset Profile Template](../../archive/docs/plans/ufl-asset-profile-template.md).

## Related Documents

- [UFL Supported Asset Profiles](ufl-supported-assets-index.md)
- [UFL Conformance Matrix](ufl-conformance-matrix.md)
- [UFL Projection and Evidence Kernel](../../archive/docs/plans/ufl-projection-and-evidence-kernel.md)
- [UFL Accounting Impact Model](../../archive/docs/plans/ufl-accounting-impact-model.md)
- [UFL Asset Profile Template](../../archive/docs/plans/ufl-asset-profile-template.md)

