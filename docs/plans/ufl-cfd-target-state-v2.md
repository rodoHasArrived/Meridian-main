# UFL CFD Target-State Package V2

**Owner:** Core Team
**Audience:** Product, architecture, domain, storage, and application contributors
**Last Updated:** 2026-03-31
**Status:** active
**Reviewed:** 2026-03-31

> **Naming standard:** All new F# types and DTOs in this package must follow the
> [Domain Naming Standard](../ai/claude/CLAUDE.domain-naming.md).
> For CFDs: definition record → `CfdDef`; underlying asset-class field → `UnderlyingAssetClass: string`;
> leverage field → `Leverage: decimal option`; margin requirement → `MarginReqPct: decimal option`.

## Summary

This document captures the target-state V2 package for `UFL` Contract for Difference (CFD) assets inside
Meridian's broader security-master, derivatives, market-data, and execution expansion.

It assumes:

- a modular monolith
- CFD instruments stored in security master with a reference to the underlying asset class
- leverage and margin requirement metadata tracked at the instrument level
- execution through the brokerage gateway framework with CFD-specific margin rules

A CFD is a derivative contract between a client and a broker to exchange the difference in value of an
underlying asset between the time a contract is opened and when it is closed. The underlying can be an
equity, index, FX pair, or commodity.

## Repo Fit

### Verified Meridian constraints

- Meridian models `SecurityKind.Cfd` and `CfdTerms` in
  `src/Meridian.FSharp/Domain/SecurityMaster.fs`.
- `SecurityMasterMapping` maps the `"Cfd"` asset class into the F# domain.
- `SecurityMasterCsvParser` accepts `"CFD"` and `"Cfd"` as CSV asset-class values.
- Classification uses `AssetClass.Derivative` with `AssetFamily.ListedDerivative`.

### Proposed UFL-specific additions

- Margin requirement projections per CFD instrument
- Overnight financing charge model
- Risk limit enforcement via `CompositeRiskValidator`
- Reference endpoints for CFD lookup and margin query

### Suggested Meridian mapping if implemented in-place

- F# domain extensions in `src/Meridian.FSharp/Domain/`
- Application services in `src/Meridian.Application/Cfds/`
- Contracts in `src/Meridian.Contracts/Cfds/`
- Storage in `src/Meridian.Storage/SecurityMaster/`

## Domain Model

### `CfdTerms` (already in `SecurityKind`)

| Field | Type | Required | Description |
|---|---|---|---|
| `UnderlyingAssetClass` | `string` | yes | Asset class of the underlying, e.g. `Equity`, `Index`, `FxSpot`, `Commodity` |
| `UnderlyingDescription` | `string option` | no | Human-readable underlying description, e.g. `"S&P 500 CFD"` |
| `Leverage` | `decimal option` | no | Maximum notional leverage ratio, e.g. `10` for 10:1 |

### Validation rules (already enforced)

- `UnderlyingAssetClass` must not be blank.
- `Leverage`, when present, must be greater than zero.

### Proposed new domain type: `CfdDef`

```fsharp
type CfdDef = {
    CfdId: CfdId
    UnderlyingAssetClass: string
    UnderlyingDescription: string option
    Leverage: decimal option
    MarginReqPct: decimal option
    OvernightFinancingRateBps: decimal option
    CurrencyDenominated: string option
}
```

## Classification

| Attribute | Value |
|---|---|
| `AssetClass` | `Derivative` |
| `AssetFamily` | `ListedDerivative` |
| `SubType` | `OtherSubType "Cfd"` |
| `TypeName` | `"Cfd"` |
| `IsDerivative` | `true` |

## Storage Design

- CFD records stored as `SecurityProjectionRecord` with `AssetClass = "Cfd"`.
- Margin requirement stored as a TradingParameters projection field (`MarginRequirementPct`).
- Overnight financing charges tracked as operational cash-flow events if direct-lending module is enabled.

## API Surface (target-state)

| Endpoint | Method | Description |
|---|---|---|
| `/security-master/cfds` | GET | List CFD securities |
| `/security-master/cfds/{id}` | GET | Get CFD detail including margin requirement |
| `/security-master/cfds/{id}/margin` | GET | Current margin requirement for a CFD |

## Risk Integration

CFDs are leveraged instruments. The following risk rules apply:

1. `PositionLimitRule` — maximum gross notional per CFD family
2. `DrawdownCircuitBreaker` — mark-to-market drawdown stop applied to leveraged CFD positions
3. Paper trading gateway must enforce margin requirement on simulated CFD fills

## Validation Rules (full target-state)

1. `UnderlyingAssetClass` must not be blank.
2. `Leverage`, when present, must be greater than zero.
3. `MarginReqPct`, when present, must be between 0 and 100 (exclusive of 0).
4. `OvernightFinancingRateBps`, when present, may be positive or negative.

## Implementation Checklist

- [x] `CfdTerms` record in `SecurityMaster.fs`
- [x] `SecurityKind.Cfd` case in `SecurityKind` DU
- [x] `isDerivative = true` for `Cfd` in `SecurityKind` module
- [x] Validation in `SecurityMasterCommands.fs`
- [x] Classification in `SecurityMasterLegacyUpgrade.fs`
- [x] Serialization in `Interop.SecurityMaster.fs`
- [x] Mapping in `SecurityMasterMapping.cs`
- [x] CSV import aliases `"CFD"` and `"Cfd"` in `SecurityMasterCsvParser.cs`
- [x] Test coverage in `SecurityMasterAssetClassSupportTests.cs`
- [ ] `CfdDef` extended domain type
- [ ] Overnight financing charge model
- [ ] CFD margin enforcement in paper trading gateway
- [ ] CFD-specific risk rule configuration
- [ ] REST endpoints for CFD lookup and margin query
