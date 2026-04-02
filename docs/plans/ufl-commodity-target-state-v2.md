# UFL Commodity Target-State Package V2

**Owner:** Core Team
**Audience:** Product, architecture, domain, storage, and application contributors
**Last Updated:** 2026-03-31
**Status:** active
**Reviewed:** 2026-03-31

> **Naming standard:** All new F# types and DTOs in this package must follow the
> [Domain Naming Standard](../ai/claude/CLAUDE.domain-naming.md).
> For commodities: definition record → `CommodityDef`; commodity type field → `CommodityType: string`;
> denomination field → `Denomination: string option`; contract-size field → `ContractSize: decimal option`.

## Summary

This document captures the target-state V2 package for `UFL` commodity assets inside Meridian's broader
security-master, derivatives, market-data, and execution expansion.

It assumes:

- a modular monolith
- commodity spot and contract instruments stored in security master
- exchange and venue symbology normalized to canonical commodity IDs
- deterministic replay of listing, expiry, and roll metadata

This package turns the `CommodityTerms` support added to `SecurityKind` into a concrete
implementation plan for commodity identity, classification, and operational workflows.

## Repo Fit

### Verified Meridian constraints

- Meridian models `SecurityKind.Commodity` and `CommodityTerms` in
  `src/Meridian.FSharp/Domain/SecurityMaster.fs`.
- `SecurityMasterMapping` maps the `"Commodity"` asset class into the F# domain.
- `SecurityMasterCsvParser` accepts `"Commodity"` as a CSV asset-class value.
- Classification uses `AssetClass.Other` with `AssetFamily.OtherFamily "Commodity"`.

### Proposed UFL-specific additions

- Commodity series and pricing projections (spot, rolling front contract)
- Venue and delivery-point tracking for physical commodities
- Commodity sub-type enumeration (`Metal`, `Energy`, `Agricultural`, `SoftCommodity`)
- Reference endpoints for commodity lookup and contract views

### Suggested Meridian mapping if implemented in-place

- F# domain extensions in `src/Meridian.FSharp/Domain/`
- Application services in `src/Meridian.Application/Commodities/`
- Contracts in `src/Meridian.Contracts/Commodities/`
- Storage in `src/Meridian.Storage/SecurityMaster/`

## Domain Model

### `CommodityTerms` (already in `SecurityKind`)

| Field | Type | Required | Description |
|---|---|---|---|
| `CommodityType` | `string` | yes | Sub-category: `Metal`, `Energy`, `Agricultural`, `SoftCommodity` |
| `Denomination` | `string option` | no | Price denomination string, e.g. `USD/troy oz`, `USD/barrel` |
| `ContractSize` | `decimal option` | no | Number of units per lot or contract |

### Validation rules (already enforced)

- `CommodityType` must not be blank.
- `ContractSize`, when present, must be greater than zero.

### Proposed new domain type: `CommodityDef`

```fsharp
type CommoditySubType =
    | Metal
    | Energy
    | Agricultural
    | SoftCommodity
    | OtherCommodity of string

type CommodityDef = {
    CommodityId: CommodityId
    CommodityType: CommoditySubType
    BaseName: string
    Denomination: string option
    DeliveryVenue: string option
    DeliveryCtry: string option
    ContractSize: decimal option
    TickSize: decimal option
}
```

## Classification

| Attribute | Value |
|---|---|
| `AssetClass` | `Other` |
| `AssetFamily` | `OtherFamily "Commodity"` |
| `SubType` | `OtherSubType "Commodity"` |
| `TypeName` | `"Commodity"` |
| `IsDerivative` | `false` |

## Storage Design

- Commodity records stored as `SecurityProjectionRecord` with `AssetClass = "Commodity"`.
- Spot price series stored as standard market-data events via existing JSONL/Parquet sinks.
- No custom partition key required; standard symbol-based partitioning applies.

## API Surface (target-state)

| Endpoint | Method | Description |
|---|---|---|
| `/security-master/commodities` | GET | List commodity securities |
| `/security-master/commodities/{id}` | GET | Get commodity detail |
| `/security-master/commodities/{id}/spot-price` | GET | Latest spot price for a commodity |

## Validation Rules (full target-state)

1. `CommodityType` must not be blank.
2. `ContractSize`, when present, must be greater than zero.
3. `Denomination`, when present, must follow the pattern `CCY/unit` (e.g. `USD/troy oz`).
4. `DeliveryCtry`, when present, must be a valid ISO 3166-1 alpha-2 country code.

## Implementation Checklist

- [x] `CommodityTerms` record in `SecurityMaster.fs`
- [x] `SecurityKind.Commodity` case in `SecurityKind` DU
- [x] Validation in `SecurityMasterCommands.fs`
- [x] Classification in `SecurityMasterLegacyUpgrade.fs`
- [x] Serialization in `Interop.SecurityMaster.fs`
- [x] Mapping in `SecurityMasterMapping.cs`
- [x] CSV import alias in `SecurityMasterCsvParser.cs`
- [x] Test coverage in `SecurityMasterAssetClassSupportTests.cs`
- [ ] `CommodityDef` extended domain type
- [ ] Sub-type enumeration `CommoditySubType`
- [ ] Delivery venue and country tracking
- [ ] Commodity-specific projection service
- [ ] REST endpoints for commodity lookup
