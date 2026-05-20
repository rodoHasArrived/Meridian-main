# Provider Capability Matrix (Adapter Readiness)

This matrix enumerates the current adapter subfolders under `src/Meridian.Infrastructure/Adapters/` and maps each adapter to the core capability flags used for readiness tracking.

**Canonical source note:** this file is the canonical adapter readiness source for capability coverage and governance follow-up.

| Adapter Folder | Streaming | Historical | Symbol Search | Options | Corporate Actions | Brokerage Sync / Execution | Owner | Target Sprint | Blocking Dependency | Next Action | Readiness Impact |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `Alpaca` | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | Data Integrations | Sprint 27 | Runtime credential evidence refresh | Regenerate runtime validation packet with current credentials and reconnect proofs | High – multi-surface provider used across readiness, options, and brokerage workflows |
| `AlphaVantage` | ❌ | ✅ | ❌ | ❌ | ❌ | ❌ | Data Integrations | Sprint 28 | API key-bound runtime validation capture | Add bounded runtime evidence and confirm current rate-limit posture | Medium – historical fallback and indicator coverage |
| `Core` | ⚙️ Shared base | ⚙️ Shared base | ⚙️ Shared base | ⚙️ Shared base | ⚙️ Shared base | ⚙️ Shared base | Platform Infrastructure | Sprint 27 | N/A (framework layer) | Keep adapter base abstractions aligned with provider SDK contracts | High – foundation for multiple adapters |
| `Edgar` | ❌ | ❌ | ✅ | ❌ | ❌ | ❌ | Security Master & Reference Data | Sprint 28 | SEC endpoint + cache refresh validation | Re-run symbol-search ingest and issuer-enrichment validation | Medium – reference-data confidence and issuer mapping |
| `Failover` | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ | Platform Infrastructure | Sprint 27 | Provider health scoring calibration | Re-verify failover routing against latest degradation calibration baselines | High – affects continuity posture under provider incidents |
| `Finnhub` | ❌ | ✅ | ✅ | ❌ | ❌ | ❌ | Data Integrations | Sprint 28 | Token-scoped runtime evidence | Capture fresh runtime packet for historical + symbol-search paths | Medium – supplemental data coverage |
| `Fred` | ❌ | ✅ | ❌ | ❌ | ❌ | ❌ | Research Data | Sprint 29 | Series catalog coverage confirmation | Validate mapped-series onboarding and freshness checks | Low – research enrichment, not trade execution-critical |
| `InteractiveBrokers` | ✅ | ✅ | ❌ | ❌ | ❌ | ✅ | Brokerage Integrations | Sprint 27 | `IBAPI` runtime entitlement + binary compatibility | Re-run compile + runtime bounded smoke with current IBAPI entitlement path | High – execution and broker-sync readiness lane |
| `NYSE` | ✅ | ✅ | ✅ | ❌ | ✅ | ❌ | Exchange Integrations | Sprint 28 | NYSE Connect credentialed run artifacts | Regenerate runtime L1 + corporate-action packet for latest review window | Medium – exchange direct data trust inputs |
| `NasdaqDataLink` | ❌ | ✅ | ❌ | ❌ | ❌ | ❌ | Data Integrations | Sprint 29 | Dataset/API entitlement validation | Reconfirm dataset mappings and retention-compatible evidence capture | Low – backfill enrichment path |
| `OpenFigi` | ❌ | ❌ | ✅ | ❌ | ❌ | ❌ | Security Master & Reference Data | Sprint 28 | API quota and symbol-mapping drift checks | Re-run FIGI enrichment sampling against current symbol corpus | Medium – canonical identifier quality |
| `Polygon` | ✅ | ✅ | ✅ | ✅ | ✅ | ❌ | Data Integrations | Sprint 27 | Live websocket/runtime packet regeneration | Re-run replay/live validation for trades/quotes/aggregates and options chain | High – primary streaming + options coverage |
| `Robinhood` | ✅ | ✅ | ✅ | ✅ | ❌ | ✅ | Brokerage Integrations | Sprint 27 | Tokenized bounded runtime evidence refresh | Regenerate brokerage read/order + options + quote packet for DK review | High – paper brokerage and options workflow support |
| `Stooq` | ❌ | ✅ | ❌ | ❌ | ❌ | ❌ | Data Integrations | Sprint 29 | Feed availability + parser drift check | Revalidate free-source historical ingestion/parsing | Low – non-critical free historical fallback |
| `Synthetic` | ✅ | ✅ | ❌ | ✅ | ❌ | ❌ | Platform Infrastructure | Sprint 28 | Fixture profile expansion for deterministic scenarios | Extend deterministic synthetic scenarios for readiness and UI regression usage | Medium – test/demo and controlled validation backbone |
| `Templates` | ❌ | ❌ | ❌ | ❌ | ❌ | ✅ | Platform Infrastructure | Sprint 30 | Template governance drift | Align template comments/contracts with latest brokerage gateway patterns | Low – scaffolding only |
| `Tiingo` | ❌ | ✅ | ❌ | ❌ | ❌ | ❌ | Data Integrations | Sprint 29 | Token-backed runtime evidence refresh | Re-run historical path validation and check throttling behavior | Low – supplemental historical source |
| `TradeStation` | ❌ | ❌ | ❌ | ❌ | ❌ | ✅ | Brokerage Integrations | Sprint 28 | OAuth/session lifecycle validation evidence | Capture refreshed order/position/account sync smoke evidence | Medium – execution adapter maturity tracking |
| `Tradier` | ❌ | ❌ | ❌ | ✅ | ❌ | ✅ | Brokerage Integrations | Sprint 28 | Runtime order/options evidence packet | Validate order lifecycle mapping and option payload normalization in bounded run | Medium – options + execution integration lane |
| `TwelveData` | ❌ | ✅ | ❌ | ❌ | ❌ | ❌ | Data Integrations | Sprint 29 | API entitlement and interval-coverage check | Re-run historical backfill sample across representative intervals | Low – additional historical coverage |
| `YahooFinance` | ❌ | ✅ | ❌ | ❌ | ❌ | ❌ | Data Integrations | Sprint 29 | Unofficial endpoint stability re-check | Revalidate parser + pagination behavior against current upstream responses | Low – unofficial historical fallback |

## Capability Flag Definitions

- **Streaming**: adapter exposes real-time market data client capabilities.
- **Historical**: adapter provides historical backfill or historical-query capabilities.
- **Symbol Search**: adapter provides symbol discovery, lookup, or mapping services.
- **Options**: adapter provides options chain/contract/quote workflows.
- **Corporate Actions**: adapter exposes dividend/split/corporate action workflows.
- **Brokerage Sync / Execution**: adapter supports brokerage account sync, order lifecycle, or execution gateway operations.
