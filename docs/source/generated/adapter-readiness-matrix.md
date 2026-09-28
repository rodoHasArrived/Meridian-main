<!--
generated: true
generator: build/scripts/docs/render-adapter-readiness.py
generator_version: 1.0.0
render_contract: meridian.generated-docs.v1
schema_versions:
  - meridian.adapter-readiness@1.0.0
inputs:
  - docs/source/data/adapter-readiness.yml
do_not_edit: true
-->

# Adapter Readiness Matrix

**Status:** generated

**Owner:** Data Confidence and Validation

Source: [adapter readiness registry](../data/adapter-readiness.yml). Edit the registry and regenerate this view; the [documentation ownership contract](../../documentation-ownership.md) governs its location.

Readiness describes the checked-in implementation and cited evidence for each folder's bounded scope. It does not grant runtime entitlements, certify vendor availability, or replace [provider validation and operator sign-off](../../reference/provider-validation-matrix.md). Evidence links identify tests; their presence does not claim a passing live run.

- **complete:** the stated bounded implementation is covered by targeted repository evidence.
- **partial:** implemented surfaces remain bounded by missing coverage, integration work, or external acceptance.
- **experimental:** exploratory or unofficial integration, or mapper-only assets without a runtime adapter.
- **template-only:** copyable scaffolding without production registration.

The six capability columns mean implementations of the shared streaming, historical, symbol-search, options, on-demand corporate-action, and brokerage contracts in `ProviderCapabilityDescriptorCatalog`. A **No** does not exclude a different integration surface: resolver and compatibility data-source contracts are listed separately below. Canonical IDs and aliases come from `ProviderIdentity`; an ID alone grants no capability. Core and Failover have no independent provider ID.

| Folder | Canonical ID | Readiness | Streaming | Historical | Symbol search | Options | Corporate actions | Brokerage |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| [Alpaca](#alpaca) | `alpaca` | partial | Yes | Yes | Yes | Yes | Yes | Yes |
| [AlphaVantage](#alphavantage) | `alphavantage` | partial | No | Yes | Yes | No | Yes | No |
| [Core](#core) | n/a | complete | No | No | No | No | No | No |
| [Edgar](#edgar) | `edgar` | partial | No | No | Yes | No | No | No |
| [Failover](#failover) | n/a | complete | No | No | No | No | No | No |
| [Finnhub](#finnhub) | `finnhub` | partial | No | Yes | Yes | No | Yes | No |
| [Fred](#fred) | `fred` | partial | No | Yes | Yes | No | No | No |
| [InteractiveBrokers](#interactivebrokers) | `ibkr` | partial | Yes | Yes | No | No | No | Yes |
| [NYSE](#nyse) | `nyse` | partial | Yes | No | No | No | No | No |
| [NasdaqDataLink](#nasdaqdatalink) | `nasdaq` | partial | No | Yes | Yes | No | Yes | No |
| [OpenFigi](#openfigi) | `openfigi` | partial | No | No | No | No | No | No |
| [Plaid](#plaid) | `plaid` | partial | No | No | No | No | No | No |
| [Polygon](#polygon) | `polygon` | partial | Yes | Yes | Yes | Yes | No | No |
| [Robinhood](#robinhood) | `robinhood` | experimental | Yes | Yes | Yes | Yes | No | Yes |
| [Stooq](#stooq) | `stooq` | partial | No | Yes | No | No | No | No |
| [Synthetic](#synthetic) | `synthetic` | complete | Yes | Yes | Yes | Yes | No | No |
| [Templates](#templates) | `templates` | template-only | No | No | No | No | No | No |
| [Tiingo](#tiingo) | `tiingo` | partial | No | Yes | Yes | No | Yes | No |
| [TradeStation](#tradestation) | `tradestation` | experimental | No | No | No | No | No | No |
| [Tradier](#tradier) | `tradier` | experimental | No | No | No | No | No | No |
| [TwelveData](#twelvedata) | `twelvedata` | partial | No | Yes | Yes | No | Yes | No |
| [YahooFinance](#yahoofinance) | `yahoo` | partial | No | Yes | No | No | No | No |

## Alpaca

**Owner:** Data Confidence and Validation

**Next action:** Retain account/feed-specific paper and live evidence and obtain operator acceptance before promoting operational readiness.

**Aliases:** `alpaca-api`, `alpaca-brokerage`, `alpaca-corp-actions`, `alpaca-options`, `alpaca-symbols`

**Credentials:** ALPACA_KEY_ID and ALPACA_SECRET_KEY via the credential resolver; configured account and feed entitlements are required for options, SIP/OPRA and brokerage surfaces.

**Optional SDK:** None; implemented using the repository HTTP/WebSocket and shared .NET contracts.

**Risks and external dependencies:**

- The historical default is 200 requests/minute; actual quotas and IEX/SIP or indicative/OPRA access depend on the configured plan.
- Unit and fixture evidence does not establish live execution acceptance; brokerage use remains subject to shared execution governance.
- Pacing values describe repository defaults; verify current provider terms and the configured account allowance before operation.

**Degradation / fail-closed behavior:** Missing credentials prevent credential-gated backfill/search activation. Asset-stream routing fails closed when the requested feed has no usable entitlement; diagnostics preserve the selected feed and entitlement. REST and trade-update errors remain observable. Corporate-action HTTP failures and non-cancellation exceptions are logged and return empty action collections; empty results must not be treated as verified absence of actions. Cancellation propagates.

**Implementation types:**

- Streaming: `AlpacaMarketDataClient`
- Historical: `AlpacaHistoricalDataProvider`
- Symbol search: `AlpacaSymbolSearchProvider`
- Options: `AlpacaOptionsChainProvider`
- Corporate actions: `AlpacaCorporateActionProvider`
- Brokerage: `AlpacaBrokerageGateway`

**Registration path:**

- [ProviderCapabilityDescriptorCatalog](../../../src/Meridian.Infrastructure/Adapters/Core/ProviderCapabilityDescriptorCatalog.cs) (`src/Meridian.Infrastructure/Adapters/Core/ProviderCapabilityDescriptorCatalog.cs`)
- [ProviderFactory](../../../src/Meridian.Infrastructure/Adapters/Core/ProviderFactory.Runtime.cs) (`src/Meridian.Infrastructure/Adapters/Core/ProviderFactory.Runtime.cs`)
- [AlpacaProviderModule](../../../src/Meridian.Infrastructure/Adapters/Alpaca/AlpacaProviderModule.cs) (`src/Meridian.Infrastructure/Adapters/Alpaca/AlpacaProviderModule.cs`)

**Targeted evidence:**

- Test: [AlpacaCredentialAndReconnectTests](../../../tests/Meridian.Tests/Infrastructure/Providers/AlpacaCredentialAndReconnectTests.cs) (`tests/Meridian.Tests/Infrastructure/Providers/AlpacaCredentialAndReconnectTests.cs`)
- Test: [AlpacaAssetStreamRoutingTests](../../../tests/Meridian.Tests/Infrastructure/Providers/AlpacaAssetStreamRoutingTests.cs) (`tests/Meridian.Tests/Infrastructure/Providers/AlpacaAssetStreamRoutingTests.cs`)
- Test: [AlpacaHistoricalDataProviderTests](../../../tests/Meridian.Tests/Infrastructure/Providers/AlpacaHistoricalDataProviderTests.cs) (`tests/Meridian.Tests/Infrastructure/Providers/AlpacaHistoricalDataProviderTests.cs`)
- Test: [AlpacaSymbolSearchProviderTests](../../../tests/Meridian.Tests/Infrastructure/Providers/AlpacaSymbolSearchProviderTests.cs) (`tests/Meridian.Tests/Infrastructure/Providers/AlpacaSymbolSearchProviderTests.cs`)
- Test: [AlpacaCorporateActionProviderTests](../../../tests/Meridian.Tests/Infrastructure/Providers/AlpacaCorporateActionProviderTests.cs) (`tests/Meridian.Tests/Infrastructure/Providers/AlpacaCorporateActionProviderTests.cs`)
- Test: [AlpacaBrokerageGatewayTests](../../../tests/Meridian.Tests/Infrastructure/Providers/AlpacaBrokerageGatewayTests.cs) (`tests/Meridian.Tests/Infrastructure/Providers/AlpacaBrokerageGatewayTests.cs`)
- Source: [AlpacaOptionsChainProvider](../../../src/Meridian.Infrastructure/Adapters/Alpaca/AlpacaOptionsChainProvider.cs) (`src/Meridian.Infrastructure/Adapters/Alpaca/AlpacaOptionsChainProvider.cs`)

## AlphaVantage

**Owner:** Data Confidence and Validation

**Next action:** Retain credentialed evidence for the configured tier and adjusted-daily corporate-action access without consuming quota implicitly.

**Aliases:** `alpha-vantage`, `alpha_vantage`, `alphavantage-corp-actions`, `alphavantage-symbols`

**Credentials:** ALPHA_VANTAGE_API_KEY (ALPHAVANTAGE__APIKEY alias); historical and search activation is explicitly opt-in.

**Optional SDK:** None; implemented using the repository HTTP/WebSocket and shared .NET contracts.

**Risks and external dependencies:**

- Local pacing is 5 requests/minute with a constrained free-tier daily allowance; adjusted data may require paid entitlement.
- Provider Note/Information payloads can represent quota exhaustion even when HTTP succeeds.
- Pacing values describe repository defaults; verify current provider terms and the configured account allowance before operation.

**Degradation / fail-closed behavior:** Absent credentials or absent opt-in skip historical/search construction. Missing direct-call credentials fail; quota responses become RateLimitException. Corporate-action fetches distinguish rate limits from empty/failure results and propagate cancellation. Missing credentials and non-rate-limit fetch failures can return empty action collections; an empty collection is not verified absence of actions.

**Implementation types:**

- Historical: `AlphaVantageHistoricalDataProvider`
- Symbol search: `AlphaVantageSymbolSearchProvider`
- Corporate actions: `AlphaVantageCorporateActionProvider`

**Registration path:**

- [ProviderCapabilityDescriptorCatalog](../../../src/Meridian.Infrastructure/Adapters/Core/ProviderCapabilityDescriptorCatalog.cs) (`src/Meridian.Infrastructure/Adapters/Core/ProviderCapabilityDescriptorCatalog.cs`)
- [ProviderFactory](../../../src/Meridian.Infrastructure/Adapters/Core/ProviderFactory.cs) (`src/Meridian.Infrastructure/Adapters/Core/ProviderFactory.cs`)

**Targeted evidence:**

- Test: [AlphaVantageHistoricalDataProviderTests](../../../tests/Meridian.Tests/Infrastructure/Providers/AlphaVantageHistoricalDataProviderTests.cs) (`tests/Meridian.Tests/Infrastructure/Providers/AlphaVantageHistoricalDataProviderTests.cs`)
- Test: [AlphaVantageSymbolSearchProviderTests](../../../tests/Meridian.Tests/Infrastructure/Providers/AlphaVantageSymbolSearchProviderTests.cs) (`tests/Meridian.Tests/Infrastructure/Providers/AlphaVantageSymbolSearchProviderTests.cs`)
- Test: [AlphaVantageCorporateActionProviderTests](../../../tests/Meridian.Tests/Infrastructure/Providers/AlphaVantageCorporateActionProviderTests.cs) (`tests/Meridian.Tests/Infrastructure/Providers/AlphaVantageCorporateActionProviderTests.cs`)

## Core

**Owner:** Data Confidence and Validation

**Next action:** Keep descriptor factories, identity aliases and contract/folder tests aligned when adding adapters.

**Aliases:** None.

**Credentials:** No independent provider credentials; shared factories delegate credential resolution to each provider family.

**Optional SDK:** No optional vendor SDK for shared primitives; vendor-specific requirements remain attached to their family.

**Risks and external dependencies:**

- Shared factory, registry, rate-limit and failover behavior affects every runtime provider.
- A successful construction or advertised capability is not live connectivity or entitlement evidence.

**Degradation / fail-closed behavior:** Factory construction failures are logged and isolated per provider. Credential policy is resolved centrally; composite history rotates on structured rate-limit signals and retains bounded retry behavior. This folder is shared infrastructure, not an independently routable provider.

**Implementation types:**

- No shared-contract provider implementation; see the scoped evidence below.

**Registration path:**

- [ProviderServiceExtensions](../../../src/Meridian.Infrastructure/Adapters/Core/ProviderServiceExtensions.cs) (`src/Meridian.Infrastructure/Adapters/Core/ProviderServiceExtensions.cs`)
- [ProviderFactory](../../../src/Meridian.Infrastructure/Adapters/Core/ProviderFactory.cs) (`src/Meridian.Infrastructure/Adapters/Core/ProviderFactory.cs`)

**Targeted evidence:**

- Test: [ProviderFactoryCredentialContextTests](../../../tests/Meridian.Tests/Infrastructure/Providers/ProviderFactoryCredentialContextTests.cs) (`tests/Meridian.Tests/Infrastructure/Providers/ProviderFactoryCredentialContextTests.cs`)
- Test: [CompositeProviderStaleDataTests](../../../tests/Meridian.Tests/Infrastructure/Providers/CompositeProviderStaleDataTests.cs) (`tests/Meridian.Tests/Infrastructure/Providers/CompositeProviderStaleDataTests.cs`)
- Test: [ProviderRateLimitTrackerTests](../../../tests/Meridian.Tests/Infrastructure/Providers/ProviderRateLimitTrackerTests.cs) (`tests/Meridian.Tests/Infrastructure/Providers/ProviderRateLimitTrackerTests.cs`)
- Test: [ProviderCapabilityDescriptorCatalogTests](../../../tests/Meridian.Tests/Providers/ProviderCapabilityDescriptorCatalogTests.cs) (`tests/Meridian.Tests/Providers/ProviderCapabilityDescriptorCatalogTests.cs`)

## Edgar

**Owner:** Data Confidence and Validation

**Next action:** Retain current SEC discovery and ingestion evidence, including source refresh behavior and deployment User-Agent policy.

**Aliases:** `edgar-symbols`

**Credentials:** No API key; SEC requests include an identifying User-Agent. Deployment contact identity and SEC access policy must remain appropriate.

**Optional SDK:** None; implemented using the repository HTTP/WebSocket and shared .NET contracts.

**Risks and external dependencies:**

- Search paces SEC access at 8 requests/second and caches the company list; availability and refresh depend on SEC endpoints.
- Reference documents and Security Master ingestion are separate from the on-demand corporate-action contract.
- Pacing values describe repository defaults; verify current provider terms and the configured account allowance before operation.

**Degradation / fail-closed behavior:** Availability reports false on fetch failure; discovery uses its cached company list and does not fabricate market data. Reference-data ingestion remains a separately registered workflow; no streaming, history or corporate-action-provider capability is asserted.

**Implementation types:**

- Symbol search: `EdgarSymbolSearchProvider`

**Registration path:**

- [ProviderCapabilityDescriptorCatalog](../../../src/Meridian.Infrastructure/Adapters/Core/ProviderCapabilityDescriptorCatalog.cs) (`src/Meridian.Infrastructure/Adapters/Core/ProviderCapabilityDescriptorCatalog.cs`)
- [StorageFeatureRegistration](../../../src/Meridian.Application/Composition/Features/StorageFeatureRegistration.cs) (`src/Meridian.Application/Composition/Features/StorageFeatureRegistration.cs`)

**Targeted evidence:**

- Test: [EdgarSymbolSearchProviderTests](../../../tests/Meridian.Tests/Infrastructure/Providers/EdgarSymbolSearchProviderTests.cs) (`tests/Meridian.Tests/Infrastructure/Providers/EdgarSymbolSearchProviderTests.cs`)
- Test: [EdgarReferenceDataProviderTests](../../../tests/Meridian.Tests/Infrastructure/Providers/EdgarReferenceDataProviderTests.cs) (`tests/Meridian.Tests/Infrastructure/Providers/EdgarReferenceDataProviderTests.cs`)
- Source: [EdgarSecurityMasterIngestProvider](../../../src/Meridian.Infrastructure/Adapters/Edgar/EdgarSecurityMasterIngestProvider.cs) (`src/Meridian.Infrastructure/Adapters/Edgar/EdgarSecurityMasterIngestProvider.cs`)

## Failover

**Owner:** Data Confidence and Validation

**Next action:** Keep failover and primary-recovery tests synchronized with subscription and latency policy changes.

**Aliases:** None.

**Credentials:** No independent credentials; every configured primary/backup provider must satisfy its own credentials and subscriptions.

**Optional SDK:** None; composes the already registered provider clients.

**Risks and external dependencies:**

- Recovery depends on usable backup feeds and subscription restoration; candidates must meet recent-latency thresholds.
- This orchestration family consumes provider capability contracts without declaring its own independent vendor capability row.

**Degradation / fail-closed behavior:** Two-phase handoff commits an active provider only after connection and subscription restoration. Missing runtime handlers, rejected transitions or cancellation retain prior state; failed unsubscriptions are logged and disposal rejects pending handoffs.

**Implementation types:**

- No shared-contract provider implementation; see the scoped evidence below.

**Registration path:**

- [CollectorModeRunner](../../../src/Meridian.Application/Composition/Startup/ModeRunners/CollectorModeRunner.cs) (`src/Meridian.Application/Composition/Startup/ModeRunners/CollectorModeRunner.cs`)

**Targeted evidence:**

- Test: [FailoverAwareMarketDataClientTests](../../../tests/Meridian.Tests/Infrastructure/Providers/FailoverAwareMarketDataClientTests.cs) (`tests/Meridian.Tests/Infrastructure/Providers/FailoverAwareMarketDataClientTests.cs`)
- Test: [StreamingFailoverServiceTests](../../../tests/Meridian.Tests/Infrastructure/Providers/StreamingFailoverServiceTests.cs) (`tests/Meridian.Tests/Infrastructure/Providers/StreamingFailoverServiceTests.cs`)

## Finnhub

**Owner:** Data Confidence and Validation

**Next action:** Retain evidence for historical and corporate-action coverage under the deployed API plan.

**Aliases:** `finnhub-corp-actions`, `finnhub-symbols`

**Credentials:** FINNHUB_API_KEY (FINNHUB__APIKEY alias); candle and corporate-action access is plan-dependent.

**Optional SDK:** None; implemented using the repository HTTP/WebSocket and shared .NET contracts.

**Risks and external dependencies:**

- Local historical/search pacing is 60 requests/minute.
- Dividend/split and historical endpoints may have entitlement and coverage restrictions independent of symbol discovery.
- Pacing values describe repository defaults; verify current provider terms and the configured account allowance before operation.

**Degradation / fail-closed behavior:** Credential-gated construction skips missing keys; direct history calls reject missing keys. Rate limits are surfaced by the shared HTTP boundary; unavailable or malformed provider data does not become fabricated bars. Corporate-action HTTP failures and non-cancellation exceptions are logged and return empty action collections; empty results must not be treated as verified absence of actions. Cancellation propagates.

**Implementation types:**

- Historical: `FinnhubHistoricalDataProvider`
- Symbol search: `FinnhubSymbolSearchProvider`
- Corporate actions: `FinnhubCorporateActionProvider`

**Registration path:**

- [ProviderCapabilityDescriptorCatalog](../../../src/Meridian.Infrastructure/Adapters/Core/ProviderCapabilityDescriptorCatalog.cs) (`src/Meridian.Infrastructure/Adapters/Core/ProviderCapabilityDescriptorCatalog.cs`)
- [ProviderFactory](../../../src/Meridian.Infrastructure/Adapters/Core/ProviderFactory.cs) (`src/Meridian.Infrastructure/Adapters/Core/ProviderFactory.cs`)

**Targeted evidence:**

- Test: [FinnhubHistoricalDataProviderTests](../../../tests/Meridian.Tests/Infrastructure/Providers/FinnhubHistoricalDataProviderTests.cs) (`tests/Meridian.Tests/Infrastructure/Providers/FinnhubHistoricalDataProviderTests.cs`)
- Test: [FinnhubSymbolSearchProviderTests](../../../tests/Meridian.Tests/Infrastructure/Providers/FinnhubSymbolSearchProviderTests.cs) (`tests/Meridian.Tests/Infrastructure/Providers/FinnhubSymbolSearchProviderTests.cs`)
- Test: [FinnhubCorporateActionProviderTests](../../../tests/Meridian.Tests/Infrastructure/Providers/FinnhubCorporateActionProviderTests.cs) (`tests/Meridian.Tests/Infrastructure/Providers/FinnhubCorporateActionProviderTests.cs`)

## Fred

**Owner:** Data Confidence and Validation

**Next action:** Retain configured-series evidence including observation gaps and revision interpretation before operational use.

**Aliases:** `fred-symbols`

**Credentials:** FRED_API_KEY (FRED__APIKEY alias); explicitly enable the FRED backfill family.

**Optional SDK:** None; implemented using the repository HTTP/WebSocket and shared .NET contracts.

**Risks and external dependencies:**

- Local historical/search pacing is 120 requests/minute.
- Economic series are not exchange-traded price bars; release timing, revisions and missing observations require downstream interpretation.
- Pacing values describe repository defaults; verify current provider terms and the configured account allowance before operation.

**Degradation / fail-closed behavior:** Absent opt-in or credentials prevents factory activation; direct history calls reject missing keys. HTTP 429 is a structured rate limit; invalid JSON fails rather than becoming usable data. Search returns economic series IDs.

**Implementation types:**

- Historical: `FredHistoricalDataProvider`
- Symbol search: `FredSymbolSearchProvider`

**Registration path:**

- [ProviderCapabilityDescriptorCatalog](../../../src/Meridian.Infrastructure/Adapters/Core/ProviderCapabilityDescriptorCatalog.cs) (`src/Meridian.Infrastructure/Adapters/Core/ProviderCapabilityDescriptorCatalog.cs`)
- [ProviderFactory](../../../src/Meridian.Infrastructure/Adapters/Core/ProviderFactory.cs) (`src/Meridian.Infrastructure/Adapters/Core/ProviderFactory.cs`)

**Targeted evidence:**

- Test: [FredHistoricalDataProviderTests](../../../tests/Meridian.Tests/Infrastructure/Providers/FredHistoricalDataProviderTests.cs) (`tests/Meridian.Tests/Infrastructure/Providers/FredHistoricalDataProviderTests.cs`)
- Test: [FredSymbolSearchProviderTests](../../../tests/Meridian.Tests/Infrastructure/Providers/FredSymbolSearchProviderTests.cs) (`tests/Meridian.Tests/Infrastructure/Providers/FredSymbolSearchProviderTests.cs`)

## InteractiveBrokers

**Owner:** Data Confidence and Validation

**Next action:** Retain official-vendor build and paper-socket evidence with account-scoped callback and entitlement provenance before live acceptance.

**Aliases:** `ib`, `interactive-brokers`, `interactive_brokers`, `interactivebrokers`

**Credentials:** Authenticated TWS or IB Gateway session, configured host/port/client ID, account access and relevant market-data subscriptions; Flex reconciliation has separate credentials and identity.

**Optional SDK:** Official IBApi DLL/project for real TWS/Gateway connectivity with EnableIbApiVendor=true; EnableIbApiSmoke=true is compile-only verification, not the vendor runtime.

**Risks and external dependencies:**

- Historical pacing is 60 requests/10 minutes with 15-second minimum spacing in the guidance surface.
- Vendor version, local socket reachability, account permissions and live/frozen/delayed entitlements determine usable runtime behavior.
- Pacing values describe repository defaults; verify current provider terms and the configured account allowance before operation.

**Degradation / fail-closed behavior:** Without the official SDK, streaming uses explicit simulation and history is unavailable/empty with guidance. Smoke or guidance builds cannot advertise live execution. Unknown requested accounts and unscoped durable callbacks fail closed; richer IBDataServices option/scanner APIs are not shared options/search providers.

**Implementation types:**

- Streaming: `IBMarketDataClient`
- Historical: `IBHistoricalDataProvider`
- Brokerage: `IBBrokerageGateway`

**Registration path:**

- [ProviderCapabilityDescriptorCatalog](../../../src/Meridian.Infrastructure/Adapters/Core/ProviderCapabilityDescriptorCatalog.cs) (`src/Meridian.Infrastructure/Adapters/Core/ProviderCapabilityDescriptorCatalog.cs`)
- [ProviderFactory](../../../src/Meridian.Infrastructure/Adapters/Core/ProviderFactory.Runtime.cs) (`src/Meridian.Infrastructure/Adapters/Core/ProviderFactory.Runtime.cs`)

**Targeted evidence:**

- Test: [IBMarketDataClientContractTests](../../../tests/Meridian.Tests/Infrastructure/Providers/IBMarketDataClientContractTests.cs) (`tests/Meridian.Tests/Infrastructure/Providers/IBMarketDataClientContractTests.cs`)
- Test: [IBHistoricalProviderContractTests](../../../tests/Meridian.Tests/Infrastructure/Providers/IBHistoricalProviderContractTests.cs) (`tests/Meridian.Tests/Infrastructure/Providers/IBHistoricalProviderContractTests.cs`)
- Test: [IBBrokerageGatewayTests](../../../tests/Meridian.Tests/Infrastructure/Providers/IBBrokerageGatewayTests.cs) (`tests/Meridian.Tests/Infrastructure/Providers/IBBrokerageGatewayTests.cs`)
- Test: [IBApiVersionValidatorTests](../../../tests/Meridian.Tests/Infrastructure/Providers/IBApiVersionValidatorTests.cs) (`tests/Meridian.Tests/Infrastructure/Providers/IBApiVersionValidatorTests.cs`)
- Test: [IBDataServicesTests](../../../tests/Meridian.Tests/Infrastructure/Providers/IBDataServicesTests.cs) (`tests/Meridian.Tests/Infrastructure/Providers/IBDataServicesTests.cs`)
- Source: [IBBuildGuidance](../../../src/Meridian.Infrastructure/Adapters/InteractiveBrokers/IBBuildGuidance.cs) (`src/Meridian.Infrastructure/Adapters/InteractiveBrokers/IBBuildGuidance.cs`)

## NYSE

**Owner:** Data Confidence and Validation

**Next action:** Retain credentialed endpoint/feed-tier evidence for streaming and the compatibility historical path.

**Aliases:** `nyse-streaming`

**Credentials:** NYSE_API_KEY and NYSE_API_SECRET; optional NYSE_CLIENT_ID/configured ClientId for OAuth client-credentials authentication and an entitled NYSE feed.

**Optional SDK:** None; implemented using the repository HTTP/WebSocket and shared .NET contracts.

**Risks and external dependencies:**

- Direct feed contracts, endpoints and feed-tier subscriptions must be validated against the configured NYSE service.
- Subscription ceiling defaults to 500; Level 2 and integrated-feed entitlement are separate external dependencies.
- NYSEDataSource advertises local limits of 100 requests/minute, 5000/hour and 50000/day; these are code metadata, not a guarantee of vendor allowance.
- Pacing values describe repository defaults; verify current provider terms and the configured account allowance before operation.

**Degradation / fail-closed behavior:** Missing credentials prevent OAuth acquisition; HTTP 429 is a structured rate limit. Streaming diagnostics preserve actual lifecycle evidence. Compatibility history is exposed only through NYSEDataSource/IHistoricalDataSource; internal NyseHistoricalDataProvider is not an IHistoricalDataProvider.

**Implementation types:**

- Streaming: `NyseMarketDataClient`
- Compatibility data source: `NYSEDataSource` (separate contract)

**Registration path:**

- [ProviderCapabilityDescriptorCatalog](../../../src/Meridian.Infrastructure/Adapters/Core/ProviderCapabilityDescriptorCatalog.cs) (`src/Meridian.Infrastructure/Adapters/Core/ProviderCapabilityDescriptorCatalog.cs`)
- [NYSEServiceExtensions](../../../src/Meridian.Infrastructure/Adapters/NYSE/NYSEServiceExtensions.cs) (`src/Meridian.Infrastructure/Adapters/NYSE/NYSEServiceExtensions.cs`)

**Targeted evidence:**

- Test: [NYSECredentialAndRateLimitTests](../../../tests/Meridian.Tests/Infrastructure/Providers/NYSECredentialAndRateLimitTests.cs) (`tests/Meridian.Tests/Infrastructure/Providers/NYSECredentialAndRateLimitTests.cs`)
- Test: [NyseMarketDataClientContractTests](../../../tests/Meridian.Tests/Infrastructure/Providers/NyseMarketDataClientContractTests.cs) (`tests/Meridian.Tests/Infrastructure/Providers/NyseMarketDataClientContractTests.cs`)
- Test: [NyseSharedLifecycleTests](../../../tests/Meridian.Tests/Infrastructure/Providers/NyseSharedLifecycleTests.cs) (`tests/Meridian.Tests/Infrastructure/Providers/NyseSharedLifecycleTests.cs`)
- Test: [NyseNationalTradesCsvParserTests](../../../tests/Meridian.Tests/Infrastructure/Providers/NyseNationalTradesCsvParserTests.cs) (`tests/Meridian.Tests/Infrastructure/Providers/NyseNationalTradesCsvParserTests.cs`)
- Source: [NYSEServiceExtensions](../../../src/Meridian.Infrastructure/Adapters/NYSE/NYSEServiceExtensions.cs) (`src/Meridian.Infrastructure/Adapters/NYSE/NYSEServiceExtensions.cs`)
- Source: [NYSEDataSource](../../../src/Meridian.Infrastructure/Adapters/NYSE/NYSEDataSource.cs) (`src/Meridian.Infrastructure/Adapters/NYSE/NYSEDataSource.cs`)

## NasdaqDataLink

**Owner:** Data Confidence and Validation

**Next action:** Select and verify a current entitled dataset and retain freshness/coverage evidence before operational acceptance.

**Aliases:** `nasdaq-corp-actions`, `nasdaq-data-link`, `nasdaq-symbols`, `nasdaq_data_link`, `nasdaqdatalink`

**Credentials:** NASDAQ_DATA_LINK_API_KEY (NASDAQ__APIKEY alias) is optional for limited free history but required for the credential-gated dataset-search path; selected datasets may require subscriptions.

**Optional SDK:** None; implemented using the repository HTTP/WebSocket and shared .NET contracts.

**Risks and external dependencies:**

- Local historical/search pacing is a conservative 50 requests/day.
- The default WIKI dataset is legacy; exact DATABASE/DATASET identity, freshness and licensing must be validated for the selected database.
- Pacing values describe repository defaults; verify current provider terms and the configured account allowance before operation.

**Degradation / fail-closed behavior:** Limited history can be constructed without a key; search is skipped without credentials. Search preserves exact dataset codes to avoid ticker ambiguity. Dataset dividend/split extraction does not imply coverage beyond the selected dataset. Corporate-action HTTP failures and non-cancellation exceptions are logged and return empty action collections; empty results must not be treated as verified absence of actions. Cancellation propagates.

**Implementation types:**

- Historical: `NasdaqDataLinkHistoricalDataProvider`
- Symbol search: `NasdaqDataLinkSymbolSearchProvider`
- Corporate actions: `NasdaqDataLinkCorporateActionProvider`

**Registration path:**

- [ProviderCapabilityDescriptorCatalog](../../../src/Meridian.Infrastructure/Adapters/Core/ProviderCapabilityDescriptorCatalog.cs) (`src/Meridian.Infrastructure/Adapters/Core/ProviderCapabilityDescriptorCatalog.cs`)
- [ProviderFactory](../../../src/Meridian.Infrastructure/Adapters/Core/ProviderFactory.cs) (`src/Meridian.Infrastructure/Adapters/Core/ProviderFactory.cs`)

**Targeted evidence:**

- Test: [NasdaqDataLinkHistoricalDataProviderTests](../../../tests/Meridian.Tests/Infrastructure/Providers/NasdaqDataLinkHistoricalDataProviderTests.cs) (`tests/Meridian.Tests/Infrastructure/Providers/NasdaqDataLinkHistoricalDataProviderTests.cs`)
- Test: [NasdaqDataLinkSymbolSearchProviderTests](../../../tests/Meridian.Tests/Infrastructure/Providers/NasdaqDataLinkSymbolSearchProviderTests.cs) (`tests/Meridian.Tests/Infrastructure/Providers/NasdaqDataLinkSymbolSearchProviderTests.cs`)
- Test: [NasdaqDataLinkCorporateActionProviderTests](../../../tests/Meridian.Tests/Infrastructure/Providers/NasdaqDataLinkCorporateActionProviderTests.cs) (`tests/Meridian.Tests/Infrastructure/Providers/NasdaqDataLinkCorporateActionProviderTests.cs`)

## OpenFigi

**Owner:** Data Confidence and Validation

**Next action:** Retain representative exchange-scoped ambiguity and identifier-resolution evidence for the deployed universe.

**Aliases:** `open-figi`, `openfigi-api`

**Credentials:** OPENFIGI_API_KEY is optional; the client supports unauthenticated identifier mapping.

**Optional SDK:** None; implemented using the repository HTTP/WebSocket and shared .NET contracts.

**Risks and external dependencies:**

- Client pacing is 25 requests/minute without a key or 250 with one, with conservative 100-identifier batches.
- Ambiguous ticker mappings and upstream candidate ordering require exchange-aware selection.
- Pacing values describe repository defaults; verify current provider terms and the configured account allowance before operation.

**Degradation / fail-closed behavior:** Availability reports false on request failure; exchange-scoped candidates are preferred when available, otherwise upstream ordering is retained. ISymbolResolver provides identifier mapping and enrichment, not ISymbolSearchProvider or market-data delivery.

**Implementation types:**

- Symbol resolver: `OpenFigiSymbolResolver` (separate contract)

**Registration path:**

- [SymbolManagementFeatureRegistration](../../../src/Meridian.Application/Composition/Features/SymbolManagementFeatureRegistration.cs) (`src/Meridian.Application/Composition/Features/SymbolManagementFeatureRegistration.cs`)

**Targeted evidence:**

- Test: [OpenFigiClientTests](../../../tests/Meridian.Tests/SymbolSearch/OpenFigiClientTests.cs) (`tests/Meridian.Tests/SymbolSearch/OpenFigiClientTests.cs`)
- Test: [OpenFigiClientAmbiguityTests](../../../tests/Meridian.Tests/SymbolSearch/OpenFigiClientAmbiguityTests.cs) (`tests/Meridian.Tests/SymbolSearch/OpenFigiClientAmbiguityTests.cs`)
- Source: [OpenFigiSymbolResolver](../../../src/Meridian.Infrastructure/Adapters/OpenFigi/OpenFigiSymbolResolver.cs) (`src/Meridian.Infrastructure/Adapters/OpenFigi/OpenFigiSymbolResolver.cs`)

## Plaid

**Owner:** Data Confidence and Validation

**Next action:** Retain institution/product-specific sandbox and deployment evidence; continue explicit live-transfer gating.

**Aliases:** `plaid-api`

**Credentials:** PLAID_CLIENT_ID and PLAID_SECRET plus selected environment; linked-item access tokens are retained through the encrypted credential store.

**Optional SDK:** None; implemented using the repository HTTP/WebSocket and shared .NET contracts.

**Risks and external dependencies:**

- Institution/product availability, item consent, webhook verification and environment quotas are external dependencies.
- Transfers require explicit enablement; financial connectivity and investment snapshots do not implement IBrokerageGateway or market-data capability contracts.
- Pacing values describe repository defaults; verify current provider terms and the configured account allowance before operation.

**Degradation / fail-closed behavior:** Transport rejects unsuccessful HTTP responses and empty response bodies. Workstation services resolve required credentials and keep access tokens out of connection metadata; transfers remain governed by environment and enablement checks.

**Implementation types:**

- No shared-contract provider implementation; see the scoped evidence below.

**Registration path:**

- [WorkstationServiceCollectionExtensions](../../../src/Meridian.Ui.Shared/Services/WorkstationServiceCollectionExtensions.cs) (`src/Meridian.Ui.Shared/Services/WorkstationServiceCollectionExtensions.cs`)

**Targeted evidence:**

- Test: [PlaidWorkstationServiceTests](../../../tests/Meridian.Tests/Ui/PlaidWorkstationServiceTests.cs) (`tests/Meridian.Tests/Ui/PlaidWorkstationServiceTests.cs`)
- Test: [PlaidWebhookVerifierTests](../../../tests/Meridian.Tests/Ui/PlaidWebhookVerifierTests.cs) (`tests/Meridian.Tests/Ui/PlaidWebhookVerifierTests.cs`)
- Source: [PlaidHttpClient](../../../src/Meridian.Infrastructure/Adapters/Plaid/PlaidHttpClient.cs) (`src/Meridian.Infrastructure/Adapters/Plaid/PlaidHttpClient.cs`)
- Source: [FilePlaidConnectionRepository](../../../src/Meridian.Infrastructure/Adapters/Plaid/FilePlaidConnectionRepository.cs) (`src/Meridian.Infrastructure/Adapters/Plaid/FilePlaidConnectionRepository.cs`)

## Polygon

**Owner:** Data Confidence and Validation

**Next action:** Retain configured-plan live feed and options evidence alongside recorded-session and corporate-action ingestion checks.

**Aliases:** `polygon-io`, `polygon-options`, `polygon-symbols`, `polygonio`

**Credentials:** POLYGON_API_KEY (POLYGON__APIKEY alias); stream, history and options access depends on the subscription plan.

**Optional SDK:** None; implemented using the repository HTTP/WebSocket and shared .NET contracts.

**Risks and external dependencies:**

- Historical local pacing defaults to 5 requests/minute; real-time/options availability is entitlement-dependent.
- Streaming supports equities/options; broader historical Forex/Crypto/Index coverage does not imply stream support.
- Pacing values describe repository defaults; verify current provider terms and the configured account allowance before operation.

**Degradation / fail-closed behavior:** Missing keys prevent credential-gated factory activation and direct history calls fail. Authentication/rate-limit errors and reconnect state remain visible; duplicate or decreasing per-ticker session sequences are rejected. Corporate-action ingestion uses a hosted Security Master fetcher, not ICorporateActionProvider.

**Implementation types:**

- Streaming: `PolygonMarketDataClient`
- Historical: `PolygonHistoricalDataProvider`
- Symbol search: `PolygonSymbolSearchProvider`
- Options: `PolygonOptionsChainProvider`

**Registration path:**

- [ProviderCapabilityDescriptorCatalog](../../../src/Meridian.Infrastructure/Adapters/Core/ProviderCapabilityDescriptorCatalog.cs) (`src/Meridian.Infrastructure/Adapters/Core/ProviderCapabilityDescriptorCatalog.cs`)
- [ProviderFactory](../../../src/Meridian.Infrastructure/Adapters/Core/ProviderFactory.Runtime.cs) (`src/Meridian.Infrastructure/Adapters/Core/ProviderFactory.Runtime.cs`)
- [StorageFeatureRegistration](../../../src/Meridian.Application/Composition/Features/StorageFeatureRegistration.cs) (`src/Meridian.Application/Composition/Features/StorageFeatureRegistration.cs`)

**Targeted evidence:**

- Test: [PolygonMarketDataClientTests](../../../tests/Meridian.Tests/Infrastructure/Providers/PolygonMarketDataClientTests.cs) (`tests/Meridian.Tests/Infrastructure/Providers/PolygonMarketDataClientTests.cs`)
- Test: [PolygonHistoricalProviderContractTests](../../../tests/Meridian.Tests/Infrastructure/Providers/PolygonProviderContractTests.cs) (`tests/Meridian.Tests/Infrastructure/Providers/PolygonProviderContractTests.cs`)
- Test: [PolygonRecordedSessionReplayTests](../../../tests/Meridian.Tests/Infrastructure/Providers/PolygonRecordedSessionReplayTests.cs) (`tests/Meridian.Tests/Infrastructure/Providers/PolygonRecordedSessionReplayTests.cs`)
- Test: [PolygonSequenceIntegrityTests](../../../tests/Meridian.Tests/Infrastructure/Providers/PolygonSequenceIntegrityTests.cs) (`tests/Meridian.Tests/Infrastructure/Providers/PolygonSequenceIntegrityTests.cs`)
- Test: [PolygonCorporateActionFetcherTests](../../../tests/Meridian.Tests/Infrastructure/Providers/PolygonCorporateActionFetcherTests.cs) (`tests/Meridian.Tests/Infrastructure/Providers/PolygonCorporateActionFetcherTests.cs`)
- Source: [PolygonOptionsChainProvider](../../../src/Meridian.Infrastructure/Adapters/Polygon/PolygonOptionsChainProvider.cs) (`src/Meridian.Infrastructure/Adapters/Polygon/PolygonOptionsChainProvider.cs`)

## Robinhood

**Owner:** Data Confidence and Validation

**Next action:** Keep experimental opt-in posture and retain token/endpoint, options and brokerage reconciliation evidence before any promotion.

**Aliases:** `robinhood-brokerage`, `robinhood-live`, `robinhood-options`, `robinhood-symbols`

**Credentials:** ROBINHOOD_ACCESS_TOKEN for unofficial endpoints; historical/options activation requires explicit backfill enablement. Read-only brokerage sync has separate ROBINHOOD_BROKERAGE_ACCESS_TOKEN and configured account/portfolio/activity endpoints.

**Optional SDK:** None; implemented using the repository HTTP/WebSocket and shared .NET contracts.

**Risks and external dependencies:**

- Unofficial endpoints and personal-token authentication may change without compatibility guarantees.
- The live market-data client polls quotes; historical pacing is 100 requests/hour and search uses a separate conservative limiter.
- Order modification is cancel/resubmit, so live execution needs explicit governance and reconciliation evidence.
- Pacing values describe repository defaults; verify current provider terms and the configured account allowance before operation.

**Degradation / fail-closed behavior:** Missing access token makes authenticated surfaces unavailable and polling connect fails. Options registration is credential/opt-in gated. Polling diagnostics report actual state with no WebSocket claim; read-only sync does not place orders.

**Implementation types:**

- Streaming: `RobinhoodMarketDataClient`
- Historical: `RobinhoodHistoricalDataProvider`
- Symbol search: `RobinhoodSymbolSearchProvider`
- Options: `RobinhoodOptionsChainProvider`
- Brokerage: `RobinhoodBrokerageGateway`

**Registration path:**

- [ProviderCapabilityDescriptorCatalog](../../../src/Meridian.Infrastructure/Adapters/Core/ProviderCapabilityDescriptorCatalog.cs) (`src/Meridian.Infrastructure/Adapters/Core/ProviderCapabilityDescriptorCatalog.cs`)
- [ProviderFactory](../../../src/Meridian.Infrastructure/Adapters/Core/ProviderFactory.Runtime.cs) (`src/Meridian.Infrastructure/Adapters/Core/ProviderFactory.Runtime.cs`)
- [HostedBrokerageGatewayServiceCollectionExtensions](../../../src/Meridian/HostedBrokerageGatewayServiceCollectionExtensions.cs) (`src/Meridian/HostedBrokerageGatewayServiceCollectionExtensions.cs`)

**Targeted evidence:**

- Test: [RobinhoodMarketDataClientTests](../../../tests/Meridian.Tests/Infrastructure/Providers/RobinhoodMarketDataClientTests.cs) (`tests/Meridian.Tests/Infrastructure/Providers/RobinhoodMarketDataClientTests.cs`)
- Test: [RobinhoodHistoricalDataProviderTests](../../../tests/Meridian.Tests/Infrastructure/Providers/RobinhoodHistoricalDataProviderTests.cs) (`tests/Meridian.Tests/Infrastructure/Providers/RobinhoodHistoricalDataProviderTests.cs`)
- Test: [RobinhoodSymbolSearchProviderTests](../../../tests/Meridian.Tests/Infrastructure/Providers/RobinhoodSymbolSearchProviderTests.cs) (`tests/Meridian.Tests/Infrastructure/Providers/RobinhoodSymbolSearchProviderTests.cs`)
- Test: [RobinhoodBrokerageGatewayTests](../../../tests/Meridian.Tests/Infrastructure/Providers/RobinhoodBrokerageGatewayTests.cs) (`tests/Meridian.Tests/Infrastructure/Providers/RobinhoodBrokerageGatewayTests.cs`)
- Test: [RobinhoodReadOnlyBrokerageSyncAdapterTests](../../../tests/Meridian.Tests/Infrastructure/Providers/RobinhoodReadOnlyBrokerageSyncAdapterTests.cs) (`tests/Meridian.Tests/Infrastructure/Providers/RobinhoodReadOnlyBrokerageSyncAdapterTests.cs`)
- Source: [RobinhoodOptionsChainProvider](../../../src/Meridian.Infrastructure/Adapters/Robinhood/RobinhoodOptionsChainProvider.cs) (`src/Meridian.Infrastructure/Adapters/Robinhood/RobinhoodOptionsChainProvider.cs`)

## Stooq

**Owner:** Data Confidence and Validation

**Next action:** Retain current endpoint, symbol-normalization and EOD freshness evidence for the intended universe.

**Aliases:** None.

**Credentials:** No credentials; accesses the public EOD CSV endpoint.

**Optional SDK:** None; implemented using the repository HTTP/WebSocket and shared .NET contracts.

**Risks and external dependencies:**

- No documented service-level quota is encoded; shared conservative delay and HTTP resilience apply.
- Public CSV format, symbol suffixes and EOD freshness remain external dependencies.
- Pacing values describe repository defaults; verify current provider terms and the configured account allowance before operation.

**Degradation / fail-closed behavior:** Missing/no-data responses return an empty result; malformed CSV rows are skipped rather than assigned invented prices. Shared HTTP handling surfaces structured rate limits. The family declares daily history only.

**Implementation types:**

- Historical: `StooqHistoricalDataProvider`

**Registration path:**

- [ProviderCapabilityDescriptorCatalog](../../../src/Meridian.Infrastructure/Adapters/Core/ProviderCapabilityDescriptorCatalog.cs) (`src/Meridian.Infrastructure/Adapters/Core/ProviderCapabilityDescriptorCatalog.cs`)
- [ProviderFactory](../../../src/Meridian.Infrastructure/Adapters/Core/ProviderFactory.cs) (`src/Meridian.Infrastructure/Adapters/Core/ProviderFactory.cs`)

**Targeted evidence:**

- Test: [StooqHistoricalDataProviderTests](../../../tests/Meridian.Tests/Infrastructure/Providers/StooqHistoricalDataProviderTests.cs) (`tests/Meridian.Tests/Infrastructure/Providers/StooqHistoricalDataProviderTests.cs`)
- Test: [StooqParsingTests](../../../tests/Meridian.Tests/Infrastructure/Providers/FreeHistoricalProviderParsingTests.cs) (`tests/Meridian.Tests/Infrastructure/Providers/FreeHistoricalProviderParsingTests.cs`)

## Synthetic

**Owner:** Data Confidence and Validation

**Next action:** Maintain deterministic fixtures and explicit synthetic provenance as the offline universe changes.

**Aliases:** `synthetic-options`

**Credentials:** No external credentials; synthetic backfill/search is opt-in and uses the configured deterministic universe.

**Optional SDK:** None; no external vendor transport or SDK is needed.

**Risks and external dependencies:**

- Generated observations and option chains are development/test evidence, never vendor market or brokerage evidence.
- Synthetic streaming/search equity coverage is narrower than its option-chain universe.

**Degradation / fail-closed behavior:** Runs deterministically offline and retains synthetic provenance. Historical corporate-action evidence uses ICorporateActionSource rather than ICorporateActionProvider; no brokerage adapter exists. Complete means the bounded offline surface, not live readiness.

**Implementation types:**

- Streaming: `SyntheticMarketDataClient`
- Historical: `SyntheticHistoricalDataProvider`
- Symbol search: `SyntheticMarketDataClient`
- Options: `SyntheticOptionsChainProvider`

**Registration path:**

- [ProviderCapabilityDescriptorCatalog](../../../src/Meridian.Infrastructure/Adapters/Core/ProviderCapabilityDescriptorCatalog.cs) (`src/Meridian.Infrastructure/Adapters/Core/ProviderCapabilityDescriptorCatalog.cs`)
- [ProviderFactory](../../../src/Meridian.Infrastructure/Adapters/Core/ProviderFactory.cs) (`src/Meridian.Infrastructure/Adapters/Core/ProviderFactory.cs`)

**Targeted evidence:**

- Test: [SyntheticMarketDataProviderTests](../../../tests/Meridian.Tests/Infrastructure/Providers/SyntheticMarketDataProviderTests.cs) (`tests/Meridian.Tests/Infrastructure/Providers/SyntheticMarketDataProviderTests.cs`)
- Test: [SyntheticHistoricalProviderContractTests](../../../tests/Meridian.Tests/Infrastructure/Providers/SyntheticHistoricalProviderContractTests.cs) (`tests/Meridian.Tests/Infrastructure/Providers/SyntheticHistoricalProviderContractTests.cs`)
- Test: [SyntheticOptionsChainProviderTests](../../../tests/Meridian.Tests/Infrastructure/Providers/SyntheticOptionsChainProviderTests.cs) (`tests/Meridian.Tests/Infrastructure/Providers/SyntheticOptionsChainProviderTests.cs`)
- Source: [SyntheticReferenceDataCatalog](../../../src/Meridian.Infrastructure/Adapters/Synthetic/SyntheticReferenceDataCatalog.cs) (`src/Meridian.Infrastructure/Adapters/Synthetic/SyntheticReferenceDataCatalog.cs`)

## Templates

**Owner:** Data Confidence and Validation

**Next action:** When creating a provider, replace template seams, define a new canonical identity and add registration plus targeted transport/governance evidence.

**Aliases:** `template-brokerage`

**Credentials:** No production credentials; copied adapters must define their own resolver and credential requirements.

**Optional SDK:** None for the scaffold; a copied implementation must document its own SDK requirements.

**Risks and external dependencies:**

- Scaffold lifecycle and in-memory order state are demonstration behavior, not broker connectivity.
- Copy-target metadata must never become an operational provider registration without implementation and evidence.

**Degradation / fail-closed behavior:** Obsolete copy-only scaffolds provide configurable readiness and in-memory account/order behavior for tests. The runtime catalog explicitly excludes them; implemented scaffold interfaces do not confer production capability claims.

**Implementation types:**

- No shared-contract provider implementation; see the scoped evidence below.

**Registration path:**

- [ProviderCapabilityDescriptorCatalog](../../../src/Meridian.Infrastructure/Adapters/Core/ProviderCapabilityDescriptorCatalog.cs) (`src/Meridian.Infrastructure/Adapters/Core/ProviderCapabilityDescriptorCatalog.cs`)

**Targeted evidence:**

- Test: [TemplateBrokerageGatewayTests](../../../tests/Meridian.Tests/Infrastructure/Providers/TemplateBrokerageGatewayTests.cs) (`tests/Meridian.Tests/Infrastructure/Providers/TemplateBrokerageGatewayTests.cs`)
- Source: [TemplateBrokerageGateway](../../../src/Meridian.Infrastructure/Adapters/Templates/TemplateBrokerageGateway.cs) (`src/Meridian.Infrastructure/Adapters/Templates/TemplateBrokerageGateway.cs`)
- Source: [BrokerAdapterTemplate](../../../src/Meridian.Infrastructure/Adapters/Templates/BrokerAdapterTemplate.cs) (`src/Meridian.Infrastructure/Adapters/Templates/BrokerAdapterTemplate.cs`)

## Tiingo

**Owner:** Data Confidence and Validation

**Next action:** Retain deployed-plan evidence for adjusted-EOD actions, historical coverage and active-symbol filtering.

**Aliases:** `tiingo-corp-actions`, `tiingo-symbols`

**Credentials:** TIINGO_API_TOKEN (TIINGO__TOKEN alias); adjusted EOD and discovery access depend on the configured plan.

**Optional SDK:** None; implemented using the repository HTTP/WebSocket and shared .NET contracts.

**Risks and external dependencies:**

- Historical/search local pacing is 50 requests/hour.
- Adjusted-EOD dividend/split coverage and endpoint availability are vendor-dependent.
- Pacing values describe repository defaults; verify current provider terms and the configured account allowance before operation.

**Degradation / fail-closed behavior:** Missing tokens skip credential-gated construction and direct history calls reject missing tokens. Search skips malformed or inactive rows, filters client-side, and preserves shared error/rate-limit handling. Corporate-action HTTP failures and non-cancellation exceptions are logged and return empty action collections; empty results must not be treated as verified absence of actions. Cancellation propagates.

**Implementation types:**

- Historical: `TiingoHistoricalDataProvider`
- Symbol search: `TiingoSymbolSearchProvider`
- Corporate actions: `TiingoCorporateActionProvider`

**Registration path:**

- [ProviderCapabilityDescriptorCatalog](../../../src/Meridian.Infrastructure/Adapters/Core/ProviderCapabilityDescriptorCatalog.cs) (`src/Meridian.Infrastructure/Adapters/Core/ProviderCapabilityDescriptorCatalog.cs`)
- [ProviderFactory](../../../src/Meridian.Infrastructure/Adapters/Core/ProviderFactory.cs) (`src/Meridian.Infrastructure/Adapters/Core/ProviderFactory.cs`)

**Targeted evidence:**

- Test: [TiingoHistoricalDataProviderTests](../../../tests/Meridian.Tests/Infrastructure/Providers/TiingoHistoricalDataProviderTests.cs) (`tests/Meridian.Tests/Infrastructure/Providers/TiingoHistoricalDataProviderTests.cs`)
- Test: [TiingoSymbolSearchProviderTests](../../../tests/Meridian.Tests/Infrastructure/Providers/TiingoSymbolSearchProviderTests.cs) (`tests/Meridian.Tests/Infrastructure/Providers/TiingoSymbolSearchProviderTests.cs`)
- Test: [TiingoCorporateActionProviderTests](../../../tests/Meridian.Tests/Infrastructure/Providers/TiingoCorporateActionProviderTests.cs) (`tests/Meridian.Tests/Infrastructure/Providers/TiingoCorporateActionProviderTests.cs`)

## TradeStation

**Owner:** Data Confidence and Validation

**Next action:** Implement governed transport, canonical runtime contracts and credential/negative-path evidence before registering runtime capabilities.

**Aliases:** None.

**Credentials:** None for the current pure payload mappers; transport credentials are not implemented in this direct family.

**Optional SDK:** No SDK used by the pure mappers; runtime integration requirements remain to be designed.

**Risks and external dependencies:**

- Only normalization assets exist here; no concrete shared-contract runtime adapter is registered.
- Mapper fixture coverage cannot establish token handling, rate-limit behavior or broker connectivity.

**Degradation / fail-closed behavior:** Required identity fields are validated by payload mappers. There is no transport or execution fallback to activate; the descriptor catalog explicitly excludes this mapper-only family.

**Implementation types:**

- No shared-contract provider implementation; see the scoped evidence below.

**Registration path:**

- [ProviderCapabilityDescriptorCatalog](../../../src/Meridian.Infrastructure/Adapters/Core/ProviderCapabilityDescriptorCatalog.cs) (`src/Meridian.Infrastructure/Adapters/Core/ProviderCapabilityDescriptorCatalog.cs`)

**Targeted evidence:**

- Test: [TradeStationPayloadMappersTests](../../../tests/Meridian.Tests/Infrastructure/Providers/TradeStationPayloadMappersTests.cs) (`tests/Meridian.Tests/Infrastructure/Providers/TradeStationPayloadMappersTests.cs`)
- Source: [TradeStationPayloadMappers](../../../src/Meridian.Infrastructure/Adapters/TradeStation/TradeStationPayloadMappers.cs) (`src/Meridian.Infrastructure/Adapters/TradeStation/TradeStationPayloadMappers.cs`)

## Tradier

**Owner:** Data Confidence and Validation

**Next action:** Implement governed provider transport and shared-contract adapters with credential, quota and refusal tests before runtime registration.

**Aliases:** None.

**Credentials:** None for the current pure canonical mappers; transport credentials are not implemented in this direct family.

**Optional SDK:** No SDK used by the pure mappers; runtime integration requirements remain to be designed.

**Risks and external dependencies:**

- Equity/options/fill normalization is mapper-only; no concrete shared-contract runtime adapter is registered.
- Execution reconciliation fixtures do not prove broker transport, rate limits or live orders.

**Degradation / fail-closed behavior:** Mappers normalize supplied payloads and validate required data; they cannot connect or execute orders. The descriptor catalog excludes the family from runtime provider capabilities.

**Implementation types:**

- No shared-contract provider implementation; see the scoped evidence below.

**Registration path:**

- [ProviderCapabilityDescriptorCatalog](../../../src/Meridian.Infrastructure/Adapters/Core/ProviderCapabilityDescriptorCatalog.cs) (`src/Meridian.Infrastructure/Adapters/Core/ProviderCapabilityDescriptorCatalog.cs`)

**Targeted evidence:**

- Test: [TradierCanonicalMappersTests](../../../tests/Meridian.Tests/Infrastructure/Adapters/TradierCanonicalMappersTests.cs) (`tests/Meridian.Tests/Infrastructure/Adapters/TradierCanonicalMappersTests.cs`)
- Test: [TradierExecutionReconciliationTests](../../../tests/Meridian.Tests/Execution/TradierExecutionReconciliationTests.cs) (`tests/Meridian.Tests/Execution/TradierExecutionReconciliationTests.cs`)
- Source: [TradierCanonicalMappers](../../../src/Meridian.Infrastructure/Adapters/Tradier/TradierCanonicalMappers.cs) (`src/Meridian.Infrastructure/Adapters/Tradier/TradierCanonicalMappers.cs`)

## TwelveData

**Owner:** Data Confidence and Validation

**Next action:** Retain paid-plan fundamentals evidence and verify credential/entitlement refusal paths for the deployed key.

**Aliases:** `twelve-data`, `twelve_data`, `twelvedata-api`, `twelvedata-corp-actions`, `twelvedata-symbols`

**Credentials:** TWELVEDATA_API_KEY (TWELVEDATA__APIKEY alias); dividends/splits require paid-plan fundamentals access.

**Optional SDK:** None; implemented using the repository HTTP/WebSocket and shared .NET contracts.

**Risks and external dependencies:**

- Historical/search local pacing is 8 requests/minute.
- Corporate-action entitlement is separate from basic history/search; symbol filters are applied client-side.
- Pacing values describe repository defaults; verify current provider terms and the configured account allowance before operation.

**Degradation / fail-closed behavior:** Missing credentials skip history/search construction. Vendor status/error payloads are handled explicitly; paid endpoint refusal cannot establish absence of corporate actions. Corporate-action failures remain logged and cancellation propagates. Corporate-action HTTP failures and non-cancellation exceptions are logged and return empty action collections; empty results must not be treated as verified absence of actions. Cancellation propagates.

**Implementation types:**

- Historical: `TwelveDataHistoricalDataProvider`
- Symbol search: `TwelveDataSymbolSearchProvider`
- Corporate actions: `TwelveDataCorporateActionProvider`

**Registration path:**

- [ProviderCapabilityDescriptorCatalog](../../../src/Meridian.Infrastructure/Adapters/Core/ProviderCapabilityDescriptorCatalog.cs) (`src/Meridian.Infrastructure/Adapters/Core/ProviderCapabilityDescriptorCatalog.cs`)
- [ProviderFactory](../../../src/Meridian.Infrastructure/Adapters/Core/ProviderFactory.cs) (`src/Meridian.Infrastructure/Adapters/Core/ProviderFactory.cs`)

**Targeted evidence:**

- Test: [TwelveDataHistoricalDataProviderTests](../../../tests/Meridian.Tests/Infrastructure/Providers/TwelveDataHistoricalDataProviderTests.cs) (`tests/Meridian.Tests/Infrastructure/Providers/TwelveDataHistoricalDataProviderTests.cs`)
- Test: [TwelveDataSymbolSearchProviderTests](../../../tests/Meridian.Tests/Infrastructure/Providers/TwelveDataSymbolSearchProviderTests.cs) (`tests/Meridian.Tests/Infrastructure/Providers/TwelveDataSymbolSearchProviderTests.cs`)
- Test: [TwelveDataCorporateActionProviderTests](../../../tests/Meridian.Tests/Infrastructure/Providers/TwelveDataCorporateActionProviderTests.cs) (`tests/Meridian.Tests/Infrastructure/Providers/TwelveDataCorporateActionProviderTests.cs`)

## YahooFinance

**Owner:** Data Confidence and Validation

**Next action:** Retain current endpoint and interval-window coverage evidence and monitor for throttling or payload drift.

**Aliases:** `yahoo-finance`, `yahoofinance`

**Credentials:** No API key; public Yahoo Finance chart endpoints.

**Optional SDK:** None; implemented using the repository HTTP/WebSocket and shared .NET contracts.

**Risks and external dependencies:**

- Local pacing is 2000 requests/hour; public endpoint throttling, retention and format can change.
- Intraday interval/date-range restrictions and adjusted-price semantics require explicit coverage evidence.
- Pacing values describe repository defaults; verify current provider terms and the configured account allowance before operation.

**Degradation / fail-closed behavior:** Shared HTTP boundaries preserve rate-limit signals and missing data returns empty results; invalid provider payloads do not become fabricated observations. No streaming, search, options or on-demand corporate-action provider is advertised.

**Implementation types:**

- Historical: `YahooFinanceHistoricalDataProvider`

**Registration path:**

- [ProviderCapabilityDescriptorCatalog](../../../src/Meridian.Infrastructure/Adapters/Core/ProviderCapabilityDescriptorCatalog.cs) (`src/Meridian.Infrastructure/Adapters/Core/ProviderCapabilityDescriptorCatalog.cs`)
- [ProviderFactory](../../../src/Meridian.Infrastructure/Adapters/Core/ProviderFactory.cs) (`src/Meridian.Infrastructure/Adapters/Core/ProviderFactory.cs`)

**Targeted evidence:**

- Test: [YahooFinanceHistoricalDataProviderTests](../../../tests/Meridian.Tests/Infrastructure/Providers/YahooFinanceHistoricalDataProviderTests.cs) (`tests/Meridian.Tests/Infrastructure/Providers/YahooFinanceHistoricalDataProviderTests.cs`)
- Test: [YahooFinanceIntradayContractTests](../../../tests/Meridian.Tests/Application/Backfill/YahooFinanceIntradayContractTests.cs) (`tests/Meridian.Tests/Application/Backfill/YahooFinanceIntradayContractTests.cs`)
- Test: [ProviderCapabilityDescriptorCatalogTests](../../../tests/Meridian.Tests/Providers/ProviderCapabilityDescriptorCatalogTests.cs) (`tests/Meridian.Tests/Providers/ProviderCapabilityDescriptorCatalogTests.cs`)

## Regeneration and validation

```bash
python build/scripts/docs/render-adapter-readiness.py
python build/scripts/docs/validate-adapter-readiness.py --summary
python build/scripts/docs/render-adapter-readiness.py --check
python -m unittest tests/scripts/test_adapter_readiness.py
```
