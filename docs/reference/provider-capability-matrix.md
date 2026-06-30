# Provider Capability Matrix (Adapter Readiness)

**Status:** canonical  
**Owner:** core-team  
**Reviewed:** 2026-06-30

**Last Updated:** 2026-06-30

This matrix enumerates every adapter subfolder under `src/Meridian.Infrastructure/Adapters/` and records capability coverage for:

Canonical source note: this file is the canonical adapter readiness source used for governance/readiness review in this repository.

- streaming
- historical
- symbol search
- options chain
- corporate actions
- brokerage sync / execution

State tags:

- **complete**: production-ready implementation exists in the folder for the capability.
- **partial**: capability exists but is scoped, read-only, or otherwise limited.
- **experimental**: early/incomplete implementation, mapper-only path, or not yet fully wired.
- **template-only**: scaffold/sample only, no active provider behavior.

## Readiness Interpretation

Use the matrix as capability evidence, not as a blanket production-readiness label:

- `Alpaca`, `Polygon`, and `InteractiveBrokers` are the only adapter rows currently marked
  `complete` overall.
- `Robinhood` and `NYSE` expose important runtime surfaces, but their overall state remains
  `partial`: Robinhood is unofficial and bounded by manual broker-session evidence, while NYSE
  still depends on entitlement/session evidence outside the active Wave 1 closure.
- The free-tier historical providers (`AlphaVantage`, `Finnhub`, `Fred`, `NasdaqDataLink`,
  `Stooq`, `Tiingo`, `TwelveData`, and `YahooFinance`) are not production-grade streaming rows.
  They are inventory/backfill rows unless a capability column says otherwise.
- A `complete` historical/backfill cell means the adapter surface and deterministic tests exist; it
  does not mean cross-provider gap-remediation SLA enforcement is complete. Use
  [Provider Backfill Operations](../operators/provider-backfill-operations.md) and
  [Provider Validation Matrix](provider-validation-matrix.md) for ordering, checkpoint, remediation,
  and evidence-closure posture.
- `OpenFigi`, `Edgar`, `Tradier`, and `TradeStation` must not be promoted from partial or
  experimental support to production provider readiness without provider-specific runtime,
  governance, and validation evidence.

## Adapter Capability + Risk Matrix

| Adapter | Streaming | Historical | Symbol Search | Options Chain | Corporate Actions | Brokerage Sync / Execution | Overall State | Ownership | Dependency / Operational Risks |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `Alpaca` | complete | complete | complete | complete | complete | complete | complete | Data Integrations + Brokerage Integrations | API key/secret setup, account entitlement differences, websocket session reliability, rate-limit tiers. |
| `AlphaVantage` | template-only | complete | partial | template-only | partial | template-only | partial | Data Integrations | Strict per-minute/day API limits, key provisioning, free-tier throttling and response-shape drift; symbol-search support is keyword/region scoped, and corporate-action coverage is limited to adjusted-daily dividend/split projection. |
| `Core` | template-only | template-only | template-only | template-only | template-only | template-only | template-only | Platform Infrastructure | Shared abstractions only; misuse risk if downstream adapters assume runtime behavior from base types. |
| `Edgar` | template-only | template-only | complete | template-only | partial | template-only | partial | Security Master & Reference Data | SEC endpoint availability, filing-schema variance, backoff/compliance for public data endpoints. |
| `Failover` | partial | template-only | template-only | template-only | template-only | template-only | partial | Platform Infrastructure | Non-deterministic upstream outages drive behavior; correctness depends on provider health scoring quality. |
| `Finnhub` | template-only | complete | complete | template-only | template-only | template-only | partial | Data Integrations | Token limits, per-plan endpoint entitlements, burst throttling, upstream payload drift. |
| `Fred` | template-only | complete | partial | template-only | template-only | template-only | partial | Research Data | API key setup, macro-series release lag/revisions, low-frequency dataset semantics; series-search support is credential-gated over the official FRED `series/search` endpoint. |
| `InteractiveBrokers` | complete | complete | template-only | template-only | template-only | complete | complete | Data Integrations + Brokerage Integrations | IB Gateway/TWS runtime dependency, IBAPI version compatibility, session lifecycle and entitlement complexity. |
| `NYSE` | complete | partial | partial | template-only | partial | template-only | partial | Exchange Integrations | Credentialed exchange access, licensing constraints, feed-format stability, market-hours dependent behavior. |
| `NasdaqDataLink` | template-only | complete | template-only | template-only | partial | template-only | partial | Data Integrations | Dataset-specific entitlement, API quota limits, symbol/dataset mapping fragility; corporate-action coverage is limited to dataset `Ex-Dividend`/`Split Ratio` projection. |
| `OpenFigi` | template-only | template-only | partial | template-only | template-only | template-only | partial | Security Master & Reference Data | API quota/rate-limit controls, identifier mapping ambiguity, request batching constraints. |
| `Polygon` | complete | complete | complete | complete | complete | template-only | complete | Data Integrations | Tier-dependent endpoint access, websocket reconnect pressure, strict API-limit windows, external network variability. |
| `Robinhood` | complete | complete | complete | complete | template-only | partial | partial | Data Integrations + Brokerage Integrations | Unofficial/consumer workflow volatility, auth/token lifecycle sensitivity, non-deterministic brokerage behavior. |
| `Stooq` | template-only | complete | template-only | template-only | template-only | template-only | partial | Data Integrations | Unofficial/free endpoint stability, scraping/parsing drift, availability uncertainty. |
| `Synthetic` | complete | complete | partial | complete | template-only | template-only | experimental | Platform Infrastructure | Deterministic test harness by design; risk is realism gap vs live providers and scenario coverage bias. |
| `Templates` | template-only | template-only | template-only | template-only | template-only | template-only | template-only | Platform Infrastructure | Scaffolding only; accidental production registration is the primary risk. |
| `Tiingo` | template-only | complete | partial | template-only | partial | template-only | partial | Data Integrations | Token setup, paid-plan feature gating, utility-search response-shape drift, request-throttle and pagination variability; symbol search is credential-gated and client-filtered, and corporate-action coverage is limited to adjusted-EOD dividend/split projection. |
| `TradeStation` | template-only | template-only | template-only | template-only | template-only | experimental | experimental | Brokerage Integrations | Mapper-only asset currently; descriptor and hosted-gateway guardrails keep it out of runtime provider surfaces until OAuth/session orchestration and execution lifecycle support exist. |
| `Tradier` | template-only | template-only | template-only | experimental | template-only | experimental | experimental | Brokerage Integrations | Mapper-only asset currently; descriptor and hosted-gateway guardrails keep it out of runtime provider surfaces until sandbox/live divergence, option payload variability, auth, and limit controls are closed. |
| `TwelveData` | template-only | complete | partial | template-only | partial | template-only | partial | Data Integrations | Tiered interval history access, quota constraints, symbol coverage and interval normalization risk; symbol-search support is credential-gated and client-filtered over `/symbol_search`, and corporate-action coverage is paid-plan/credential-gated over `/dividends` and `/splits`. |
| `YahooFinance` | template-only | complete | template-only | template-only | template-only | template-only | partial | Data Integrations | Unofficial endpoint contract risk, anti-bot throttling/format drift, network and parsing nondeterminism; runtime descriptor coverage pins Yahoo to historical/fallback only and fails closed for streaming/search/corporate-action/brokerage claims. |

## Notes

- The matrix is folder-based: capability states are assigned from concrete adapter artifacts present in each subfolder (for example `*MarketDataClient`, `*HistoricalDataProvider`, `*SymbolSearchProvider`, `*OptionsChainProvider`, `*CorporateAction*`, `*BrokerageGateway`/`*BrokerageSync*`).
- `ProviderCapabilityDescriptorCatalog` is contract-based: it registers only concrete adapters that implement the shared runtime provider contracts. Folder-level support such as EDGAR Security Master ingestion, OpenFIGI mapping, NYSE ProviderSdk sources, or mapper-only Tradier/TradeStation assets does not become a runtime descriptor until a compatible shared contract implementation exists.
- `Core` and `Templates` are intentionally non-provider runtime folders and are marked `template-only` by design.
- `TradeStation` and `Tradier` currently expose mapper-focused artifacts in this tree; `ProviderCapabilityDescriptorCatalogTests` and `HostedBrokerageGatewayRegistrationTests` keep them out of runtime provider surfaces until full adapter classes and wiring are present.
