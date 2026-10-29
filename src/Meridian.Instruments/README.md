---
doc_type: source-readme
doc_schema: meridian.source-readme
doc_schema_version: "1.0.0"
module_id: SRC-DESIGN-INSTRUMENTS
path: src/Meridian.Instruments
status: active
owner_lane: Accounting and Ledger
last_reviewed: 2026-10-06
---

# src/Meridian.Instruments

Bond analytics v2 consumes the shared Contracts economics and exercise calculators; explicit analytics v1 dispatch preserves retained settlement verification. The cash ladder v2 uses projection-bound terms for declared call/put proceeds and per-reset evidence for floating shocks. Already-observed resets remain fixed; future index rates are shifted before contractual spread and cap/floor application. Missing evidence and unsupported signed coupon cash block the scenario. `LegacyPortfolioCashLadderEngine` retains v1 behavior for explicit historical replay; undeclared records keep their compatibility behavior.

`AccountCashForecastEngine` and `AccountCashForecastService` retain bounded account/book cash
forecasts over the shared `PortfolioCashLadderEngine`. Opening settled cash, expected and observed
flows, economic/settlement/receipt dates, terms/projection provenance and exact dated FX remain
inspectable. Observed flows replace corresponding predictions in the ladder; only validated
observed cash reconciles to closing cash. Missing scope, source evidence, FX or unexplained closing
variance blocks certification. Corrections retain linked earlier snapshots and factor predictions.
Opening and closing cash require independently identifiable retained observations, including
single-day periods with equal balances. Linked expected and actual flows must agree on security
identity, including whether an identity is absent. Existing receipts remain immutable; formerly
certified inputs violating these guards fail replay closed and require source review.
`AccountCashForecastService.CaptureAsync` rejects a non-UTC or missing recording time as caller input before the engine or store runs; the oversight capture endpoint applies the same rule before any retained-evidence verification.
Certification describes cash evidence reconciliation and grants no payment authority. See
[account oversight evidence](../../docs/domain/account-oversight-evidence.md) and
`AccountCashForecastTests` for the bounded contract.

Direct-lending Asset Operations publication derives terms, status, and ledger row identities
from retained loan/security facts so retries preserve those visible IDs. Ledger projection dates
come from the retained projection as-of date, or the contract effective date when no run exists.
Replay identity coverage is in `AssetOperationsReadServiceTests`; publication observation timestamps
and cross-writer ordering require separate evidence before claiming exactly-once effects.


Canonical split posting explicitly opts into `CanonicalLotTransferJournal`: the projection emits
a reviewed cashless basis transfer for one whole-unit successor. Existing operational split
callers retain their no-journal quantity projection. `CorporateActionAssetAccountingEventMapper`
requires the complete canonical instruction for exchange/refunding and explicitly opted-in lot
transfers; other split/merger projections keep their existing mapping behavior. Optional canonical
instructions retain their exact reviewed payload on the Projected event.

Bond calculations use explicit economic/settlement/recorded cutoffs. Declared Asset Operations projections consume the shared cash-flow service and stable period identities. Bond analytics exposes per-100 clean/dirty prices, accrued interest, yield and risk with typed capability blockers; principal factors remain separate from inflation indexation. Bond migration previews stage normal Security Master drafts; the persisted full-inventory cutover gate blocks undeclared live calculations without rewriting historical journals. See [bond expansion](../../docs/engineering/plans/bond-capability-expansion.md).

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
  traces, ledger-support references, and timeline variances without writing ledger facts, including
  contractual bond principal schedules mapped into sinking-fund repayment projections.
- `AssetOperations/FactorPaydownProjectionService.cs` - deterministic MBS factor-paydown projector
  that validates retained evidence, held face, factor bounds, currency rounding, and optimistic
  position version before producing typed event, economic-state, and projection-lineage records.
- `Indicators/TechnicalIndicatorService.cs` - live and historical technical indicator calculations
  over market trades, quotes, and OHLCV bars using Skender.Stock.Indicators.

## Important workflows

Use this README to understand the module before editing source files. Update the registry when validation, roadmap links, diagrams, or ownership changes.

`PortfolioCashLadderEngine.GetContributionCurrencies` exposes raw currencies through the same
latest-run, date-window, held-quantity, and capital-kind selectors used to build the ladder.
The shared workstation read service uses this metadata before amount arithmetic to reject
missing currency or unsupported FX aggregation. Superseded and out-of-window flows and
unrecognized capital activities remain excluded; the engine retains its warning for an
unrecognized in-window capital kind. Early-call validation excludes flows discarded after the
selected call date while retaining coupons due on or before the call and every future principal
flow that the scenario pulls into the requested window.

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
Asset Operations supplies the Expected and Projected inputs to the shared Asset Accounting Event
Spine for Acquisition, Capitalization, Valuation, Income, Corporate Action, Impairment,
Depreciation/Amortization, and Disposal. Every projected event carries the authoritative
SecurityId, book-position id and expected version, book/period/basis scope, economic-event identity,
projection lineage, projected balanced effect, and complete typed retained evidence. Generated
obligations and service availability can explain pending work, but neither can manufacture evidence
readiness or advance an event into Drafted or Posted state.
`CorporateActionAssetAccountingEventMapper` requires a reviewed `OpenLotSuccessorInstructionDto`
for cashless Reg S/144A exchange, advance-refunding handoffs and projections explicitly marked
`CanonicalLotTransferJournal`. Whole-unit splits opt into that transfer mode; older operational and
identifier-changing projections retain their existing mapping. The optional marker is omitted when
false. Any supplied instruction must match the
exact existing projection, source security version, predecessor and successor allocations, and
retained evidence; its fingerprint participates in the posting identity. The mapper carries it as
a typed corporate-action lot instruction into governed candidate preparation. It rejects missing
or contradictory instructions and successor corrections, and cannot approve or persist lots.
Corporate-action monetary projections use the Contracts `CurrencyMinorUnits` definition, including
four-decimal CLF/UYW amounts, so projected carrying allocations and canonical successor validation
agree. Explicit canonical split, stock-merger and non-taxable spin-off transfers retain the exact
functional carrying basis as their event amount, including sub-minor-unit basis left by amortization
or partial relief. Ordinary projections retain currency rounding; successor allocations conserve
the exact total through the final allocation residual. The successor handoff refuses non-`Successor`
Reg S/144A roles and zero functional carrying allocations before governed drafting.
Return-of-capital mapping requires the exact `OpenLotCapitalReturnInstructionDto` and its
reviewed in-place lot plan. Missing, changed or mixed successor/capital-return inputs refuse
mapping. Its fingerprint binds the event handoff to both independent carrying-basis reductions;
the mapper retains the approved cash/return-of-capital recipe without approving or writing it.
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

The bond reference read model no longer emits `cashFlowSource` from bond accounting elections;
those elections were never populated and are retired from the Security Master terms.

## Diagrams

`DIA-ASSURANCE-LOOP`

## Roadmap traceability

<!-- source-roadmap-traceability:begin module=SRC-DESIGN-INSTRUMENTS -->
| Roadmap item | Title |
| --- | --- |
| `W10-LOT-002` | Security-identified open-lot convergence |
| `W4-RECON-001` | Portfolio ledger reconciliation readiness |
| `W5-MASSET-001` | Multi-asset operational coverage proof lane |
| `W9-ASSET-010` | Asset Accounting Event Spine and atomic lot posting |
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
