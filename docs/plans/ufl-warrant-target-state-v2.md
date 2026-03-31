# UFL Warrant Target-State Package V2

**Owner:** Core Team
**Audience:** Product, architecture, domain, storage, and application contributors
**Last Updated:** 2026-03-31
**Status:** active
**Reviewed:** 2026-03-31

> **Naming standard:** All new F# types and DTOs in this package must follow the
> [Domain Naming Standard](../ai/claude/CLAUDE.domain-naming.md).
> For warrants: definition record → `WarrantDef`; warrant-type field → `WarrantType: string`;
> expiry field → `ExpiryDt: DateOnly option`; strike field → `Strike: decimal option`;
> multiplier field → `Multiplier: decimal option`.

## Summary

This document captures the target-state V2 package for `UFL` warrant assets inside Meridian's broader
security-master, derivatives, market-data, and execution expansion.

It assumes:

- a modular monolith
- warrants stored in security master with a reference to the underlying security
- call/put classification and strike/expiry metadata tracked at the instrument level
- warrant lifecycle events (issuance, expiry, exercise) modeled as security-master events

A warrant is a derivative instrument issued by a company or financial institution that grants the holder
the right (but not the obligation) to buy or sell the underlying security at a specified price before
or on an expiry date. Unlike exchange-traded options, warrants are typically issued by the company itself
and have longer expiry periods.

## Repo Fit

### Verified Meridian constraints

- Meridian models `SecurityKind.Warrant` and `WarrantTerms` in
  `src/Meridian.FSharp/Domain/SecurityMaster.fs`.
- `WarrantTerms.UnderlyingId` references a `SecurityId` in the security master.
- `SecurityMasterMapping` maps the `"Warrant"` asset class into the F# domain.
- `SecurityMasterCsvParser` accepts `"Warrant"` as a CSV asset-class value.
- Classification uses `AssetClass.Derivative` with `AssetFamily.ListedDerivative`.
- `SecurityKind.underlyingSecurityId` extracts the underlying `SecurityId` from warrant terms.

### Proposed UFL-specific additions

- Warrant lifecycle projections (active, exercised, expired)
- Exercise-right modeling (American / European)
- Corporate-action linkage for warrant terms adjustment
- Reference endpoints for warrant lookup and underlying resolution

### Suggested Meridian mapping if implemented in-place

- F# domain extensions in `src/Meridian.FSharp/Domain/`
- Application services in `src/Meridian.Application/Warrants/`
- Contracts in `src/Meridian.Contracts/Warrants/`
- Storage in `src/Meridian.Storage/SecurityMaster/`

## Domain Model

### `WarrantTerms` (already in `SecurityKind`)

| Field | Type | Required | Description |
|---|---|---|---|
| `UnderlyingId` | `SecurityId` | yes | Security master ID of the underlying instrument |
| `WarrantType` | `string` | yes | `"Call"` or `"Put"` |
| `Strike` | `decimal option` | no | Exercise price per underlying unit |
| `Expiry` | `DateOnly option` | no | Last exercise date |
| `Multiplier` | `decimal option` | no | Number of underlying units per warrant |

### Validation rules (already enforced)

- `WarrantType` must not be blank.
- `WarrantType` must be `"Call"` or `"Put"` (case-insensitive).
- `Strike`, when present, must be greater than zero.
- `Multiplier`, when present, must be greater than zero.

### Proposed new domain type: `WarrantDef`

```fsharp
[<RequireQualifiedAccess>]
type WarrantType = Call | Put

[<RequireQualifiedAccess>]
type ExerciseStyle = American | European

type WarrantDef = {
    WarrantId: WarrantId
    UnderlyingId: SecurityId
    WarrantType: WarrantType
    ExerciseStyle: ExerciseStyle option
    Strike: decimal option
    ExpiryDt: DateOnly option
    Multiplier: decimal option
    IssuerName: string option
    IssueDt: DateOnly option
    SettlementType: string option
}
```

## Classification

| Attribute | Value |
|---|---|
| `AssetClass` | `Derivative` |
| `AssetFamily` | `ListedDerivative` |
| `SubType` | `OtherSubType "Warrant"` |
| `TypeName` | `"Warrant"` |
| `IsDerivative` | `true` |

## Storage Design

- Warrant records stored as `SecurityProjectionRecord` with `AssetClass = "Warrant"`.
- Expiry stored as Maturity in the `TradingParameters` projection (via `LegacyUpgrade`).
- Warrant exercise events modeled as security-master amendment events on expiry.

## Underlying Resolution

- `SecurityKind.underlyingSecurityId` already returns `Some terms.UnderlyingId` for warrants.
- The security-master service can resolve the underlying symbol and asset class on demand.
- Circular references are prevented by validation: a warrant cannot reference itself.

## API Surface (target-state)

| Endpoint | Method | Description |
|---|---|---|
| `/security-master/warrants` | GET | List warrant securities |
| `/security-master/warrants/{id}` | GET | Get warrant detail including underlying resolution |
| `/security-master/warrants/{id}/underlying` | GET | Resolve underlying security for a warrant |
| `/security-master/warrants/expiring` | GET | List warrants expiring within a given window |

## Lifecycle Events (target-state)

| Event | Trigger | Action |
|---|---|---|
| `WarrantIssued` | New warrant created via `CreateSecurity` | Project warrant to active state |
| `TermsAdjusted` | Corporate action on underlying | Amend strike/multiplier via `AmendTerms` |
| `WarrantExpired` | Expiry date reached | Deactivate via `DeactivateSecurity` |
| `WarrantExercised` | Holder exercises | Record exercise event; trigger fill if paper |

## Validation Rules (full target-state)

1. `WarrantType` must be `"Call"` or `"Put"` (case-insensitive).
2. `Strike`, when present, must be greater than zero.
3. `Multiplier`, when present, must be greater than zero.
4. `ExpiryDt`, when present, must be in the future at time of creation.
5. `UnderlyingId` must resolve to an active security in the security master.

## Implementation Checklist

- [x] `WarrantTerms` record in `SecurityMaster.fs`
- [x] `SecurityKind.Warrant` case in `SecurityKind` DU
- [x] `isDerivative = true` for `Warrant` in `SecurityKind` module
- [x] `underlyingSecurityId` returns `Some terms.UnderlyingId` for `Warrant`
- [x] Validation in `SecurityMasterCommands.fs`
- [x] Classification in `SecurityMasterLegacyUpgrade.fs`
- [x] Serialization in `Interop.SecurityMaster.fs`
- [x] Mapping in `SecurityMasterMapping.cs`
- [x] CSV import alias `"Warrant"` in `SecurityMasterCsvParser.cs`
- [x] Test coverage in `SecurityMasterAssetClassSupportTests.cs`
- [ ] `WarrantDef` extended domain type with `ExerciseStyle`
- [ ] Warrant lifecycle events (exercise, expiry)
- [ ] Corporate-action adjustment linkage
- [ ] REST endpoints for warrant lookup and underlying resolution
- [ ] Expiry-window query endpoint
