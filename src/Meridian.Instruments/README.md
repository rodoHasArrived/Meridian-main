---
doc_type: source-readme
doc_schema: meridian.source-readme
doc_schema_version: "1.0.0"
module_id: SRC-DESIGN-INSTRUMENTS
path: src/Meridian.Instruments
status: active
owner_lane: Accounting and Ledger
last_reviewed: 2026-06-05
---

# src/Meridian.Instruments

## Purpose

Physical bounded-context module project for instrument terms, contracts, obligations, classifications, and ledger-projection ownership conformance.

## Layer responsibility

This module belongs to the Design Module layer. Keep changes within that ownership boundary and update the registry if the boundary changes.

## Key folders and files

- `src/Meridian.Instruments` - registered source module root.
- `FixedIncome/BondProjectionService.cs` - storage-backed bond reference, lifecycle, and
  accrual-convention read service plus null fallback.
- `Options/OptionProjectionService.cs` - option contract, series, chain snapshot, expiry ladder,
  and import projection service plus null fallbacks.
- `Equity/EquityProjectionService.cs`, `Futures/FutureProjectionService.cs`,
  `FxSpot/FxSpotProjectionService.cs`, `CryptoCurrency/CryptoProjectionService.cs`,
  `Deposits/DepositProjectionService.cs`, `CertificatesOfDeposit/CertificateOfDepositProjectionService.cs`,
  `Commodities/CommodityProjectionService.cs`, `Derivatives/SwapProjectionService.cs`, and
  `MoneyMarketFunds/MoneyMarketFundProjectionService.cs` - storage-backed asset contract/reference
  read services for shared endpoint and composition consumers.

## Important workflows

Use this README to understand the module before editing source files. Update the registry when validation, roadmap links, diagrams, or ownership changes.

Asset-specific instrument reference services live here because they expose financial instrument
terms, lifecycle, contract, expiry, maturity, accrual, sweep, and chain-linkage details. Application
composition wires these services to Security Master projection stores, while UI Shared adapts them
to shared browser/WPF routes without owning the instrument logic.

## Diagrams

`DIA-ASSURANCE-LOOP`

## Roadmap traceability

<!-- source-roadmap-traceability:begin module=SRC-DESIGN-INSTRUMENTS -->
| Roadmap item | Title |
| --- | --- |
| `W4-RECON-001` | Portfolio ledger reconciliation readiness |
| `W5-MULTIASSET-001` | - |
<!-- source-roadmap-traceability:end -->

## TODO checklist

<!-- source-todos:begin module=SRC-DESIGN-INSTRUMENTS -->
- No registry-backed TODOs are open for this module.
<!-- source-todos:end -->

## Validation

```bash
dotnet build src/Meridian.Instruments/Meridian.Instruments.csproj /p:EnableWindowsTargeting=true
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "FullyQualifiedName~CryptoProjectionServiceTests|FullyQualifiedName~DepositProjectionServiceTests|FullyQualifiedName~CertificateOfDepositProjectionServiceTests|FullyQualifiedName~CommodityProjectionServiceTests|FullyQualifiedName~SwapProjectionServiceTests|FullyQualifiedName~EquityProjectionServiceTests|FullyQualifiedName~FutureProjectionServiceTests|FullyQualifiedName~FxSpotProjectionServiceTests|FullyQualifiedName~BondProjectionServiceTests|FullyQualifiedName~OptionProjectionServiceTests|FullyQualifiedName~MoneyMarketFundProjectionServiceTests|FullyQualifiedName~OptionReferenceEndpointsRoundtripTests|FullyQualifiedName~BondReferenceEndpointsTests|FullyQualifiedName~ReferenceDataEndpointAuthorizationTests|FullyQualifiedName~AssetOperationsReadServiceTests" --logger "console;verbosity=normal" /p:EnableWindowsTargeting=true /p:NodeReuse=false
```

### Migration and archive notes

Bond, option, equity, futures, FX spot, crypto, deposit, certificate-of-deposit, commodity, swap,
and money-market fund projection service contracts and implementations moved out of the layer-oriented
application/reference-data owners into this physical design module so instrument terms and
asset-specific contract read models are owned by `Meridian.Instruments`.

## Change rules

Preserve the module boundary declared in `docs/source/data/source-modules.yml` and update the nearest docs when behavior or workflow semantics change.

## Related docs

- `docs/source/README.md`
- `docs/source/generated/source-module-index.md`
- `docs/architecture/module-map.md`
