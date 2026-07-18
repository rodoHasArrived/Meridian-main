---
doc_type: source-readme
doc_schema: meridian.source-readme
doc_schema_version: "1.0.0"
module_id: SRC-DESIGN-INSTRUMENTS
path: src/Meridian.Instruments
status: active
owner_lane: Accounting and Ledger
last_reviewed: 2026-07-13
---

# src/Meridian.Instruments

## Purpose

Physical bounded-context module project for instrument terms, contracts, obligations,
classifications, instrument roles, book-position economic projections, and ledger-projection
ownership conformance.

## Layer responsibility

This module belongs to the Design Module layer. Keep changes within that ownership boundary and update the registry if the boundary changes.

## Key folders and files

- `src/Meridian.Instruments` - registered source module root.
- `FixedIncome/BondProjectionService.cs` - storage-backed bond reference, lifecycle, and
  accrual-convention read service plus null fallback.
- `Options/OptionProjectionService.cs` - option contract, series, chain snapshot, expiry ladder,
  and import projection service plus null fallbacks.
- `Options/OptionsChainService.cs` - provider-backed option-chain discovery, failover, filtering,
  quote/snapshot caching, and summary/status service for UI and strategy consumers.
- `Equity/EquityProjectionService.cs`, `Futures/FutureProjectionService.cs`,
  `FxSpot/FxSpotProjectionService.cs`, `CryptoCurrency/CryptoProjectionService.cs`,
  `Deposits/DepositProjectionService.cs`, `CertificatesOfDeposit/CertificateOfDepositProjectionService.cs`,
  `Commodities/CommodityProjectionService.cs`, `Derivatives/SwapProjectionService.cs`, and
  `MoneyMarketFunds/MoneyMarketFundProjectionService.cs` - storage-backed asset contract/reference
  read services for shared endpoint and composition consumers.
- `MoneyMarketFunds/IMoneyMarketFundService.cs`,
  `MoneyMarketFunds/InMemoryMoneyMarketFundService.cs`, and
  `MoneyMarketFunds/PostgresMoneyMarketFundService.cs` - money-market fund reference, liquidity,
  sweep-profile, fund-family, and rebuild projection services.
- `AssetOperations/AssetOperationsReadService.cs` - Security Master-keyed asset operations query,
  command, and projection builder for shared terms, lifecycle, cash-flow, activity,
  reconciliation, ledger, evidence, workflow-audit, terms/obligations timeline, and readiness views,
  plus durable instrument-role, book-position, economic-state, and projection-lineage collections
  merged from the dedicated projection store without replacing existing security-scoped detail.
- `AssetOperations/AssetObligationProjectionService.cs` - retained-term projection service for
  Security Master-keyed assets that emits V1 expected cash flows, non-cash obligations, formula
  traces, ledger-support references, and timeline variances without writing ledger facts.
- `AssetOperations/FactorPaydownProjectionService.cs` - deterministic MBS factor-paydown projector
  that validates retained evidence, held face, factor bounds, currency rounding, and optimistic
  position version before producing typed event, economic-state, and projection-lineage records.
- `Indicators/TechnicalIndicatorService.cs` - live and historical technical indicator calculations
  over market trades, quotes, and OHLCV bars using Skender.Stock.Indicators.

## Important workflows

Use this README to understand the module before editing source files. Update the registry when validation, roadmap links, diagrams, or ownership changes.

Asset-specific instrument reference services live here because they expose financial instrument
terms, lifecycle, contract, expiry, maturity, accrual, sweep, liquidity, fund-family, and
chain-linkage details. Asset Operations projections also live here because they adapt instrument
terms, obligation schedules, projected cash flows, ledger projections, and readiness evidence into
the shared Security Master-keyed operational view. The terms/obligations timeline is derived here
from retained Security Master terms, projected cash-flow runs, generated non-cash obligations,
actual activity, reconciliation results, lifecycle events, variances, and ledger references so
browser and WPF clients render a shared event rail without owning projection logic.
Application composition wires these services to Security Master, Asset Operations, and money-market
projection stores, while UI Shared adapts them to shared browser/WPF routes without owning the
instrument logic.

The instrument-to-journal dependency direction is:

```text
Reference Data / Security Master
-> Instruments / Asset Operations
-> Financial Operations
-> Ledger / Storage
```

Security Master remains the canonical owner of `SecurityId`, identifiers, classification, and
reference-data evidence. This module consumes that identity and owns the downstream economic
interpretation expressed by `InstrumentRoleDto`, `BookPositionDto`, `PositionEconomicStateDto`,
`EconomicEventReferenceDto`, and `ProjectionLineageDto`. A unified passport/read model may compose
both domains, but this module does not introduce an Instrument Master above Security Master.

Instrument and Asset Operations projections may support a governed posting candidate, but they do
not write or redefine ledger facts. Financial Operations resolves `AccountingBookContextDto`, reuses
the existing accounting policy rule pack through `AccountingRulePackReferenceDto`, and owns the
candidate and approval workflow. Ledger and Storage remain responsible for balanced immutable
`JournalEntry` append and its child ledger entries. Candidate journals, projected economic events,
position state, and balance snapshots remain drafts or rebuildable read models rather than a second
accounting truth.
The factor-paydown model computes `held face x (prior factor - current factor)`. Equal factors emit
no posting candidate; factor increases, missing evidence, stale versions, invalid face/factors, and
unrepresentable currency results fail closed. Its event identity excludes run timestamps so replay
of the same retained factor row produces the same ledger-book/source-event idempotency key.
Technical indicator calculation lives here because moving-average, oscillator, volatility, VWAP,
and volume-derived analytics are instrument-market analytics rather than application orchestration.
The service keeps per-symbol streaming state bounded by `IndicatorConfiguration.MaxQuotesHistory`
and accepts historical bar batches for deterministic replay/test calculations.
Option-chain discovery and quote/snapshot caching also live here because option contracts,
expirations, strikes, and chain snapshots are instrument-market data rather than host
orchestration. Application composition still wires provider implementations and collectors, while
UI Shared and strategy adapters consume `Meridian.Instruments.Options.OptionsChainService`.

## Diagrams

`DIA-ASSURANCE-LOOP`

## Roadmap traceability

<!-- source-roadmap-traceability:begin module=SRC-DESIGN-INSTRUMENTS -->
| Roadmap item | Title |
| --- | --- |
| `W4-RECON-001` | Portfolio ledger reconciliation readiness |
| `W5-MASSET-001` | Multi-asset operational coverage proof lane |
<!-- source-roadmap-traceability:end -->

## TODO checklist

<!-- source-todos:begin module=SRC-DESIGN-INSTRUMENTS -->
- No registry-backed TODOs are open for this module.
<!-- source-todos:end -->

## Validation

```bash
dotnet build src/Meridian.Instruments/Meridian.Instruments.csproj /p:EnableWindowsTargeting=true
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "FullyQualifiedName~CryptoProjectionServiceTests|FullyQualifiedName~DepositProjectionServiceTests|FullyQualifiedName~CertificateOfDepositProjectionServiceTests|FullyQualifiedName~CommodityProjectionServiceTests|FullyQualifiedName~SwapProjectionServiceTests|FullyQualifiedName~EquityProjectionServiceTests|FullyQualifiedName~FutureProjectionServiceTests|FullyQualifiedName~FxSpotProjectionServiceTests|FullyQualifiedName~BondProjectionServiceTests|FullyQualifiedName~OptionProjectionServiceTests|FullyQualifiedName~MoneyMarketFundProjectionServiceTests|FullyQualifiedName~MoneyMarketFundServiceTests|FullyQualifiedName~MmfRebuildTests|FullyQualifiedName~MmfLiquidityServiceTests|FullyQualifiedName~MmfFamilyNormalizationTests|FullyQualifiedName~OptionReferenceEndpointsRoundtripTests|FullyQualifiedName~BondReferenceEndpointsTests|FullyQualifiedName~ReferenceDataEndpointAuthorizationTests|FullyQualifiedName~AssetOperationsReadServiceTests" --logger "console;verbosity=normal" /p:EnableWindowsTargeting=true /p:NodeReuse=false
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "FullyQualifiedName~TechnicalIndicatorServiceTests" --logger "console;verbosity=normal" /p:EnableWindowsTargeting=true /p:NodeReuse=false
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "FullyQualifiedName~OptionsChainServiceTests|FullyQualifiedName~OptionsEndpoints" --logger "console;verbosity=normal" /p:EnableWindowsTargeting=true /p:NodeReuse=false
```

### Migration and archive notes

Bond, option, equity, futures, FX spot, crypto, deposit, certificate-of-deposit, commodity, swap,
money-market fund, Asset Operations, technical indicator analytics, option-chain query/failover,
and money-market fund
reference/liquidity/sweep service
contracts and implementations moved out of the layer-oriented application/reference-data owners into
this physical design module so instrument terms, asset-specific contract read models, Security
Master-keyed operational readiness projections, technical indicators, option-chain market-data
services, and MMF liquidity projections are owned by `Meridian.Instruments`.

The additive role, book-position, economic-state, event-reference, and projection-lineage contract
alignment does not migrate existing Security Master, direct-lending, portfolio, fund-account, or
asset-specific records. Slice 3 adds effective-dated security/book and position lookup plus
transactional optimistic concurrency over the Slice 2 projection tables. Asset Operations reads
compose that durable typed history with the existing security-scoped projection instead of replacing
terms, cash flows, reconciliation, readiness, or workflow state. These records remain rebuildable
economic projections; they cannot create another ledger or balance authority.

## Change rules

Preserve the module boundary declared in `docs/source/data/source-modules.yml` and update the nearest docs when behavior or workflow semantics change.

## Related docs

- `docs/source/README.md`
- `docs/source/generated/source-module-index.md`
- `docs/architecture/module-map.md`
