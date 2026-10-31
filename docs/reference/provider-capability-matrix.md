# Provider Capability Matrix

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-10-30

Use this reference to identify the shared provider contracts implemented in Meridian and the
boundaries of their runtime exposure. The matrix covers all 22 direct folders under
[`src/Meridian.Infrastructure/Adapters/`](../../src/Meridian.Infrastructure/Adapters/).
The source and test definitions were inspected at `fcd72ec33307770b3f432e48daaf22bff25c2f8f`;
this review does not establish a successful live-provider session or operator acceptance.

## Readiness Interpretation

Capability coverage, runtime availability, and acceptance have different evidence owners:

| Question | Source of truth |
| --- | --- |
| Which shared contracts does an adapter declare? | [`ProviderCapabilityDescriptorCatalog`](../../src/Meridian.Infrastructure/Adapters/Core/ProviderCapabilityDescriptorCatalog.cs), summarized below. |
| What is the family's implementation state, owner, risk, and next action? | [Adapter readiness registry](../source/data/adapter-readiness.yml) and its [generated matrix](../source/generated/adapter-readiness-matrix.md). |
| Can the configured host construct and use the capability? | Descriptor/module factories, [provider credentials](../operators/provider-credentials.md), and [`ProviderReadinessService`](../../src/Meridian.Ui.Shared/Services/ProviderReadinessService.cs). |
| What validation and sign-off does a use require? | [Provider Validation Matrix](provider-validation-matrix.md) and [validation evidence schema](provider-validation-evidence-schema.md). |

The registry owns family readiness labels such as `complete`, `partial`, `experimental`, and
`template-only`; this page does not maintain a second overall readiness inventory. A completed
implementation or passing deterministic test does not establish account entitlement, live
connectivity, data completeness, permission to trade, or production certification. Historical
validation packets apply to their recorded scope and run, not automatically to the reviewed commit.

## Shared Contract Capability Matrix

**Yes** means a compatible implementation is declared in the catalog for that contract.
**—** means the family advertises no slot for that contract; it can still have separate interfaces,
hosted ingestion, financial-connectivity functions, or mapping utilities described below.
Neither value describes the provider's full commercial API or whether a configured instance is
currently enabled.

| Column | Shared contract and scope |
| --- | --- |
| Streaming | `IMarketDataClient`; transport and supported event types vary by adapter. |
| Historical | `IHistoricalDataProvider`; interval, dataset, adjustment, and asset coverage remain provider-specific. |
| Search | `ISymbolSearchProvider`; this is distinct from identifier resolution. |
| Options | `IOptionsChainProvider`; this is distinct from broker-specific option-definition requests. |
| Corporate actions | On-demand `ICorporateActionProvider`; separate ingestion and historical evidence do not imply this contract. |
| Brokerage | `IBrokerageGateway`; account/portfolio/activity synchronization and individual order operations require their own support. |

| Adapter folder (provider ID) | Streaming | Historical | Search | Options | Corporate actions | Brokerage | Capability boundary |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `Alpaca` (`alpaca`) | Yes | Yes | Yes | Yes | Yes | Yes | Feed, asset class, credentials, and paper/live environment are separately selected; corporate actions cover dividend/split announcements. |
| `AlphaVantage` (`alphavantage`) | — | Yes | Yes | — | Yes | — | Credential/enable-gated history and keyword search; adjusted-daily dividend/split projection is review-only. |
| `Core` | — | — | — | — | — | — | Shared factories, contracts, pacing, and orchestration; not an independent provider family. |
| `Edgar` (`edgar`) | — | — | Yes | — | — | — | SEC company/ticker discovery; filing and Security Master ingestion use separate contracts. |
| `Failover` | — | — | — | — | — | — | Composite streaming orchestration over catalogued providers; no independent family descriptor. |
| `Finnhub` (`finnhub`) | — | Yes | Yes | — | Yes | — | Daily/intraday candles and symbol search; dividend/split endpoint projection is review-only. |
| `Fred` (`fred`) | — | Yes | Yes | — | — | — | Economic-series observations and discovery, with module-owned factories; observations are represented as synthetic OHLC bars. |
| `InteractiveBrokers` (`ibkr`) | Yes | Yes | — | — | — | Yes | Default build surfaces can simulate; real broker connectivity requires the official vendor runtime described below. |
| `NasdaqDataLink` (`nasdaq`) | — | Yes | Yes | — | Yes | — | Dataset history and exact `DATABASE/DATASET` search; review-only dividend/split projection depends on dataset fields. |
| `NYSE` (`nyse`) | Yes | — | — | — | — | — | Shared streaming adapter plus a separate ProviderSdk compatibility data source. |
| `OpenFigi` (`openfigi`) | — | — | — | — | — | — | Catalogued identifier resolver; mapping/search uses `ISymbolResolver`, not `ISymbolSearchProvider`. |
| `Plaid` | — | — | — | — | — | — | Financial-connectivity client and connection persistence under Plaid-specific contracts. |
| `Polygon` (`polygon`) | Yes | Yes | Yes | Yes | — | — | Corporate actions use hosted Security Master ingestion; streaming coverage is narrower than overall family coverage. |
| `Robinhood` (`robinhood`) | Yes | Yes | Yes | Yes | — | Yes | Unofficial session-based integration; streaming is REST-polled BBO quotes; search is enable-gated, and options additionally require an access token. |
| `Stooq` (`stooq`) | — | Yes | — | — | — | — | End-of-day CSV history. |
| `Synthetic` (`synthetic`) | Yes | Yes | Yes | Yes | — | — | Deterministic simulation; streaming/search are equity-only, with separate synthetic option chains and historical corporate-action evidence. |
| `Templates` | — | — | — | — | — | — | Copy-only scaffolds excluded from runtime registration. |
| `Tiingo` (`tiingo`) | — | Yes | Yes | — | Yes | — | Adjusted end-of-day history and utility search; `divCash`/`splitFactor` projection is review-only. |
| `TradeStation` | — | — | — | — | — | — | Payload mappers and reconciliation evidence; no concrete shared runtime gateway. |
| `Tradier` | — | — | — | — | — | — | Canonical mappers and reconciliation evidence; no shared options or runtime gateway implementation. |
| `TwelveData` (`twelvedata`) | — | Yes | Yes | — | Yes | — | Daily `time_series` history and symbol search; dividend/split endpoints are review-only. |
| `YahooFinance` (`yahoo`) | — | Yes | — | — | — | — | Historical/fallback adapter with daily and supported intraday aggregate bars; no streaming/search/on-demand corporate-action slot. |

### Runtime activation and streaming limits

- Descriptor factories may return no instance when configuration or credentials disable a
  capability. This does not authorize substitution with another provider family. FRED's hosted
  factories are published only after successful
  [`FredProviderModule`](../../src/Meridian.Infrastructure/Adapters/Fred/FredProviderModule.cs)
  registration, with final enable/credential checks. Catalog presence alone does not activate them.
- Alpaca declares equities, options, crypto, and news stream asset classes. A connected equities
  socket does not prove the other streams are active. Feed-specific routing and entitlements remain
  separate, and readiness requires authentication and subscription acknowledgments; see
  [asset-stream routing tests](../../tests/Meridian.Tests/Infrastructure/Providers/AlpacaAssetStreamRoutingTests.cs)
  and [connection-readiness tests](../../tests/Meridian.Tests/Infrastructure/Providers/AlpacaConnectionReadinessTests.cs).
- Polygon's declared streaming instruments are equities, equity options, and index options.
  Its broader family metadata must not be interpreted as equal streaming coverage for forex,
  crypto, or indexes. The shared client does not support market depth.
- Robinhood implements the streaming contract through REST polling of best-bid/offer quotes.
  Trade and market-depth subscriptions are unsupported. Its options path requires both the
  Robinhood backfill enable flag and a resolved access token. See
  [`ProviderFactory.Runtime`](../../src/Meridian.Infrastructure/Adapters/Core/ProviderFactory.Runtime.cs)
  and [`RobinhoodMarketDataClient`](../../src/Meridian.Infrastructure/Adapters/Robinhood/RobinhoodMarketDataClient.cs).
- Historical adapters have distinct interval, timestamp, adjustment, and dataset semantics. FRED
  observations are economic series, not market trades; Nasdaq Data Link identifiers are dataset
  codes. Use [Provider Backfill Operations](../operators/provider-backfill-operations.md) for
  ordering, checkpoints, fallback, and coverage evidence. An empty result alone does not establish
  complete historical coverage or the absence of corporate actions.

### Interfaces outside the six shared contracts

These implemented paths explain why a family can have useful behavior beside a **—** cell:

| Family | Separate surface and limit |
| --- | --- |
| EDGAR | [`EdgarReferenceDataProvider`](../../src/Meridian.Infrastructure/Adapters/Edgar/EdgarReferenceDataProvider.cs) and [`EdgarSecurityMasterIngestProvider`](../../src/Meridian.Infrastructure/Adapters/Edgar/EdgarSecurityMasterIngestProvider.cs) serve reference/filing and Security Master ingestion. The catalog separately declares its shared symbol-search adapter; these ingestion paths do not implement `ICorporateActionProvider`. |
| NYSE | [`NYSEDataSource`](../../src/Meridian.Infrastructure/Adapters/NYSE/NYSEDataSource.cs) implements ProviderSdk realtime/historical contracts, including compatibility dividend/split access. It is catalogued as `CompatibilityDataSource`; its historical helper is not an `IHistoricalDataProvider`. |
| OpenFIGI | [`OpenFigiSymbolResolver`](../../src/Meridian.Infrastructure/Adapters/OpenFigi/OpenFigiSymbolResolver.cs) is catalogued as `SymbolResolver`. Its identifier mapping and search results do not implement the shared symbol-search contract. |
| Plaid | [`PlaidHttpClient`](../../src/Meridian.Infrastructure/Adapters/Plaid/PlaidHttpClient.cs) and [`FilePlaidConnectionRepository`](../../src/Meridian.Infrastructure/Adapters/Plaid/FilePlaidConnectionRepository.cs) implement financial-connectivity ports. Use [Plaid operations](../operators/plaid-provider-operations.md); these are not market-data or brokerage-gateway registrations. |
| Polygon | [`PolygonCorporateActionFetcher`](../../src/Meridian.Infrastructure/Adapters/Polygon/PolygonCorporateActionFetcher.cs) is hosted Security Master ingestion, not an on-demand `ICorporateActionProvider`. |
| Synthetic | [`SyntheticHistoricalDataProvider`](../../src/Meridian.Infrastructure/Adapters/Synthetic/SyntheticHistoricalDataProvider.cs) provides historical corporate-action evidence through `ICorporateActionSource`, not an on-demand corporate-action provider. |
| Interactive Brokers | [`IBDataServices`](../../src/Meridian.Infrastructure/Adapters/InteractiveBrokers/IBDataServices.cs) exposes broker-specific scanner discovery, contract details, option-chain definitions, and other entitlement-aware data services. These do not fill the generic search/options slots. |

### Corporate-action release and acceptance

The six on-demand corporate-action adapters above implement dividend/split retrieval or projection;
this does not imply support for every corporate-action type or permission to apply an observation.
Their declared release statuses are:

| Provider(s) | `CorporateActionProviderReleaseStatusDto` | Acceptance implication |
| --- | --- | --- |
| Alpaca | `AcceptanceEligible` | Eligible for the acceptance path when the required source evidence and workflow controls are satisfied; the label is not acceptance of an individual event. |
| AlphaVantage, Finnhub, NasdaqDataLink, Tiingo, TwelveData | `ReviewOnly` | Observations remain staged for review; cross-provider agreement does not promote the provider or authorize automatic application. |

[`CorporateActionIngestOrchestratorTests`](../../tests/Meridian.Tests/SecurityMaster/CorporateActionIngestOrchestratorTests.cs)
checks review-only consensus staging, evidence requirements, and rejection of provider attempts to
raise their release status through command content.
[`CorporateActionOperationsServiceTests`](../../tests/Meridian.Tests/SecurityMaster/CorporateActionOperationsServiceTests.cs)
checks that acceptance fails closed for a review-only provider release. Missing credentials,
endpoint failures, or partial responses must not be interpreted as verified absence of actions.

### Brokerage and Interactive Brokers runtime

[`HostedBrokerageGatewayServiceCollectionExtensions`](../../src/Meridian/HostedBrokerageGatewayServiceCollectionExtensions.cs)
registers Alpaca, Interactive Brokers (`ibkr`, with `ib` compatibility alias), and Robinhood gateways
and their account/portfolio/activity synchronization surfaces. Robinhood uses a read-only adapter
for synchronization; its gateway is a separate execution surface. Supported order operations still
vary by gateway. Tradier and TradeStation remain mapper-only and unregistered, as checked by
[`HostedBrokerageGatewayRegistrationTests`](../../tests/Meridian.Tests/Execution/HostedBrokerageGatewayRegistrationTests.cs).

Interactive Brokers requires special build interpretation: default builds do not load an official
IB API assembly or establish real broker connectivity. Real runtime builds require
`EnableIbApiVendor=true` and exactly one official SDK input, `IBApiProjectPath` or `IBApiDllPath`.
`EnableIbApiSmoke=true` is compile/callback compatibility evidence, not live runtime evidence.
Use [IB API Compatibility](interactive-brokers-api-compatibility.md) for the supported build and
release-evidence contract, and the [IB onboarding runbook](../operators/provider-onboarding-interactive-brokers.md)
for session and entitlement validation.

## Live execution manual override routes

This is the canonical route reference for the retired
[`docs/operations/live-execution-controls.md`](../operations/live-execution-controls.md) path.
The routes below are defined by
[`UiApiRoutes`](../../src/Meridian.Contracts/Api/UiApiRoutes.cs) and implemented by
[`ExecutionEndpoints`](../../src/Meridian.Ui.Shared/Endpoints/ExecutionEndpoints.cs).

| Method | Route | Result |
| --- | --- | --- |
| `POST` | `/api/execution/controls/manual-overrides` | Creates an `ExecutionManualOverride`; returns `201` on success. |
| `POST` | `/api/execution/controls/manual-overrides/{overrideId}/clear` | Clears the identified override; returns `200` on success or `404` when it cannot be found. |

Creation requires `kind` and `reason`; optional scope fields are `symbol`, `strategyId`, `runId`,
`expiresAt`, and `correlationId`. Clearing accepts optional `reason` and `correlationId`.
Both operations require `ManageOrders` permission and a resolved authenticated actor, apply the
mutation rate limit, and return `503` when operator controls are unavailable. Audit records retain
the actor and correlation metadata. These routes describe control operations; they do not confer
provider certification or establish live-trading readiness. Endpoint behavior and audit retention
are covered by
[`ExecutionGovernanceEndpointsTests`](../../tests/Meridian.Tests/Ui/ExecutionGovernanceEndpointsTests.cs).

## Evidence and Maintenance

The linked tests describe executable regression coverage; their presence is not a claim that a
particular run passed. Record the tested commit, results, and any required provider/session evidence
when making a readiness decision.

| Boundary | Inspectable regression evidence |
| --- | --- |
| Descriptor/interface parity, supported capabilities, and all direct adapter folders | [`ProviderCapabilityDescriptorCatalogTests`](../../tests/Meridian.Tests/Providers/ProviderCapabilityDescriptorCatalogTests.cs). |
| Hosted module registration and late credential/configuration gating | [`FredProviderModuleCompositionTests`](../../tests/Meridian.Tests/Application/Composition/FredProviderModuleCompositionTests.cs) and [`ProviderFactoryCredentialContextTests`](../../tests/Meridian.Tests/Infrastructure/Providers/ProviderFactoryCredentialContextTests.cs). |
| Historical request/response scope, secondary search, and corporate-action projection | [Provider-specific tests](../../tests/Meridian.Tests/Infrastructure/Providers/) and the per-family evidence links in the [generated adapter matrix](../source/generated/adapter-readiness-matrix.md). |
| Hosted brokerage exposure and exclusion of mapper-only experiments | [`HostedBrokerageGatewayRegistrationTests`](../../tests/Meridian.Tests/Execution/HostedBrokerageGatewayRegistrationTests.cs). |
| Manual override route consistency | [`test_live_execution_controls_route_consistency.py`](../../tests/scripts/test_live_execution_controls_route_consistency.py). |

When a capability changes, update the catalog/factory implementation, its contract tests, and the
[source-owned registry](../source/data/adapter-readiness.yml), then regenerate its matrix. Update
this reference for contract or interpretation changes. Retain implementation readiness, live-provider
validation, and operator acceptance as separate claims. Provider quotas, licensing, and commercial
entitlements must be checked against current provider terms and the configured account before use;
repository pacing defaults do not establish those allowances.
