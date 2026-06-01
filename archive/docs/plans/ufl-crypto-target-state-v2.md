# UFL Crypto Capability Profile

**Owner:** Core Team
**Audience:** Product, architecture, domain, storage, and application contributors
**Last Updated:** 2026-05-29

## TODO Checklist (Concrete Implementation Items)
- [ ] Define scope boundaries for **ufl crypto target state v2** and document explicit in-scope vs out-of-scope items.
- [ ] Break delivery into PR-sized milestones with owner, dependency, and evidence artifact for each milestone.
- [ ] Implement the first milestone in code/config/scripts and link the exact validating test or command output.
- [ ] Add/update operator runbook steps and rollback procedure for the ufl crypto target state v2 workflow.
- [ ] Record completion evidence in `docs/status/` (or linked packet) and mark corresponding checklist items done.

**Status:** active
**Reviewed:** 2026-03-31

> **Naming standard:** All new F# types and DTOs in this package must follow the
> [Domain Naming Standard](../ai/claude/CLAUDE.domain-naming.md).
> For crypto: definition record → `CryptoDef`; base-currency field → `BaseCcy: string`;
> quote-currency field → `QuoteCcy: string`; network field → `Network: string option`.

## Summary

This document captures the target-state V2 package for `UFL` cryptocurrency spot assets inside Meridian's
broader security-master, market-data, and execution expansion.

It assumes:

- a modular monolith
- cryptocurrency spot pairs stored in security master
- exchange and network symbology normalized to canonical crypto IDs
- market-data streaming from crypto-enabled providers (e.g. Alpaca, Polygon)

The CSV import pipeline has accepted `"Crypto"` as an alias mapping to `"CryptoCurrency"` since the
initial import tooling was built. This package gives that `"CryptoCurrency"` asset class a full
domain-model and implementation plan to match.

## Evidence Boundary

### Implemented

- `SecurityKind.CryptoCurrency` and `CryptoTerms` exist in `src/Meridian.FSharp/Domain/SecurityMaster.fs`.
- `SecurityMasterMapping` maps the `"CryptoCurrency"` asset class into the F# domain.
- `SecurityMasterCsvParser` accepts `"Crypto"` and `"CryptoCurrency"` as CSV asset-class values.
- Security Master validation enforces nonblank base and quote currencies and prevents identical currency values.
- `src/Meridian.Application/CryptoCurrency/ICryptoReferenceService.cs` and `CryptoProjectionService` define crypto reference reads.
- `src/Meridian.Ui.Shared/Endpoints/CryptoReferenceEndpoints.cs` exposes crypto reference-data endpoints.
- `tests/Meridian.Tests/SecurityMaster/SecurityMasterAssetClassSupportTests.cs` verifies basic create support for crypto instruments.

### Partially Implemented

- Canonical crypto terms and reference surfaces exist, but network/venue alias projections, custody metadata, 24/7 market-state behavior, and rebuild evidence remain partial.

### Target-State Only

- Network-level metadata such as chain ID and block confirmation policy.
- Custodian and wallet-address tracking per holding.
- 24/7 market-state model.
- Provider-independent network and venue alias projections.

### Explicitly Out of Scope

- Wallet private-key custody.
- Blockchain indexing.
- On-chain accounting automation.
- Exchange-specific execution workflows beyond canonical reference data.

## UFL Capability Profile

| Capability | Level | Current evidence | Target addition | Tests |
| --- | ---: | --- | --- | --- |
| InstrumentIdentity | L1 | `SecurityKind.CryptoCurrency`, terms, mapping, CSV aliases, validation, and basic create test exist. | canonical crypto pair profile metadata | F# validation and C# mapping tests |
| ProviderAlias | L1/L2 partial | reference services support crypto reads. | network, venue, and provider alias isolation | alias projection tests |
| Lifecycle | L0 | target-state only. | active, delisted, restricted, network-suspended, and review states | lifecycle projection tests |
| TermsVersioning | L1 partial | base, quote, and network terms exist. | network metadata and custody versioning | terms versioning tests |
| ProjectionRebuild | L1/L2 partial | crypto projection service and endpoint shape exist. | network, venue, alias, custody, and lifecycle projections with rebuild metadata | rebuild/checkpoint tests |
| WorkstationControl | L0 | target-state only. | Data and Accounting review of custody and network metadata | workstation endpoint tests |

## Current Maturity

`L1/L2 partial`: canonical crypto terms, validation, basic create support, and reference surfaces exist. L3 maturity requires network, venue, alias, custody, lifecycle, and rebuild metadata tests.

## Next Milestone Contract

**Goal:** advance crypto to L3 by adding provider-independent network, venue, alias, custody, lifecycle, and rebuild metadata projections.

**Files likely touched:**

- `src/Meridian.FSharp/Domain/SecurityMaster.fs`
- `src/Meridian.Application/CryptoCurrency/`
- `src/Meridian.Contracts/CryptoCurrency/`
- `src/Meridian.Ui.Shared/Endpoints/`
- `tests/Meridian.Tests/`

**Acceptance evidence:**

- validation and mapping tests for crypto terms and aliases.
- endpoint contract tests for crypto reference, network, and base-currency reads.
- projection/rebuild tests for network, venue, alias, custody, lifecycle, and source-event metadata.
- provider-payload isolation tests for canonical crypto projections.

**Exit criteria:** crypto consumers can query canonical pair, network, and venue views with deterministic rebuild metadata and no dependency on raw exchange payload shape.

## Provider Payload Boundary

Provider payloads may be retained as source evidence, but crypto workflows must consume canonical pair terms, network metadata, provider aliases, custody references, and rebuild metadata.

## Repo Fit

### Verified Meridian constraints

- Meridian models `SecurityKind.CryptoCurrency` and `CryptoTerms` in
  `src/Meridian.FSharp/Domain/SecurityMaster.fs`.
- `SecurityMasterMapping` maps the `"CryptoCurrency"` asset class into the F# domain.
- `SecurityMasterCsvParser` accepts `"Crypto"` and `"CryptoCurrency"` as CSV asset-class values.
- Classification uses `AssetClass.Other` with `AssetFamily.OtherFamily "Crypto"`.

### Proposed UFL-specific additions

- Network-level metadata (chain ID, block confirmation policy)
- Custodian and wallet-address tracking per holding
- 24/7 market-state model (no exchange open/close)
- Reference endpoints for crypto pair lookup

### Suggested Meridian mapping if implemented in-place

- F# domain extensions in `src/Meridian.FSharp/Domain/`
- Application services in `src/Meridian.Application/Crypto/`
- Contracts in `src/Meridian.Contracts/Crypto/`
- Storage in `src/Meridian.Storage/SecurityMaster/`

## Domain Model

### `CryptoTerms` (already in `SecurityKind`)

| Field | Type | Required | Description |
|---|---|---|---|
| `BaseCurrency` | `string` | yes | Crypto token/coin code, e.g. `BTC`, `ETH` |
| `QuoteCurrency` | `string` | yes | Quote currency code, e.g. `USD`, `EUR` |
| `Network` | `string option` | no | Blockchain network name, e.g. `Bitcoin`, `Ethereum` |

### Validation rules (already enforced)

- `BaseCurrency` must not be blank.
- `QuoteCurrency` must not be blank.
- `BaseCurrency` and `QuoteCurrency` must differ.

### Proposed new domain type: `CryptoDef`

```fsharp
type CryptoDef = {
    CryptoId: CryptoId
    BaseCcy: string
    QuoteCcy: string
    Network: string option
    ChainId: int option
    CustodianName: string option
    MinTradeSize: decimal option
    TickSize: decimal option
}
```

## Classification

| Attribute | Value |
|---|---|
| `AssetClass` | `Other` |
| `AssetFamily` | `OtherFamily "Crypto"` |
| `SubType` | `OtherSubType "CryptoCurrency"` |
| `TypeName` | `"CryptoCurrency"` |
| `IsDerivative` | `false` |

## Storage Design

- Crypto records stored as `SecurityProjectionRecord` with `AssetClass = "CryptoCurrency"`.
- Tick and bar data stored using existing JSONL/Parquet sinks.
- Market-state model for crypto should suppress exchange-hours SLA checks (always open).

## API Surface (target-state)

| Endpoint | Method | Description |
|---|---|---|
| `/security-master/crypto` | GET | List crypto pair securities |
| `/security-master/crypto/{id}` | GET | Get crypto pair detail |
| `/security-master/crypto/{id}/ticker` | GET | Live ticker for a crypto pair |

## Validation Rules (full target-state)

1. `BaseCurrency` must not be blank.
2. `QuoteCurrency` must not be blank.
3. `BaseCurrency` and `QuoteCurrency` must differ.
4. `Network`, when present, must match a known blockchain network name.
5. `MinTradeSize`, when present, must be greater than zero.

## Implementation Checklist

- [x] `CryptoTerms` record in `SecurityMaster.fs`
- [x] `SecurityKind.CryptoCurrency` case in `SecurityKind` DU
- [x] Validation in `SecurityMasterCommands.fs`
- [x] Classification in `SecurityMasterLegacyUpgrade.fs`
- [x] Serialization in `Interop.SecurityMaster.fs`
- [x] Mapping in `SecurityMasterMapping.cs`
- [x] CSV import alias in `SecurityMasterCsvParser.cs` (`"Crypto"` → `"CryptoCurrency"`)
- [x] Test coverage in `SecurityMasterAssetClassSupportTests.cs`
- [ ] `CryptoDef` extended domain type
- [ ] Chain-ID and custodian metadata
- [ ] 24/7 market-state model (no open/close SLA)
- [ ] Crypto-specific projection service
- [ ] REST endpoints for crypto pair lookup
