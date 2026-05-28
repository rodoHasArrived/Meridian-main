# TradeStation Endpoint Inventory and Meridian Adapter Mapping

## Scope

This inventory covers TradeStation endpoints and behaviors needed for a Meridian adapter across:

- auth/session
- account/positions
- orders
- fills/executions
- market data
- historical retrieval

Primary references:

- TradeStation v3 docs (`https://api.tradestation.com/docs/`)
- TradeStation legacy Swagger (`https://tradestation.github.io/api-docs/swagger.json`) used for explicit endpoint/path discovery where v3 docs currently emphasize fundamentals and not a full static path list.

---

## 1) Endpoint Inventory by Capability

## Auth / Session

| Capability | Endpoint / Flow | Notes for Meridian |
|---|---|---|
| OAuth login | `GET https://signin.tradestation.com/authorize` | Use Auth Code (or Auth Code + PKCE when key configured for SPA/native). |
| Token exchange | `POST https://signin.tradestation.com/oauth/token` | Access token expires ~20 minutes; refresh token flow required for long-running host sessions. |
| Token refresh | `POST https://signin.tradestation.com/oauth/token` (`grant_type=refresh_token`) | Support rotating refresh-token mode as well as non-expiring mode. |
| Logout | `GET https://signin.tradestation.com/v2/logout` | Meridian should clear local access+refresh tokens regardless of remote logout redirect. |
| Scopes | `MarketData`, `ReadAccount`, `Trade`, `Matrix`, `OptionSpreads` (+ `openid offline_access`) | Adapter capability checks should fail fast if required scopes missing. |

## Account / Positions

| Capability | Endpoint | Notes for Meridian |
|---|---|---|
| List user accounts | `GET /v2/users/{user_id}/accounts` | Use during provider bootstrap and account-to-fund-account mapping. |
| Balances | `GET /v2/accounts/{account_keys}/balances` | Pollable brokerage snapshot; subject to category quota. |
| Positions | `GET /v2/accounts/{account_keys}/positions` | Snapshot semantics; pair with order/execution streams to avoid stale reads during rapid fills. |

## Orders

| Capability | Endpoint | Notes for Meridian |
|---|---|---|
| Place order | `POST /v2/orders` | Map Meridian order intent model -> TradeStation order schema by asset class. |
| Confirm order | `POST /v2/orders/confirm` | Pre-trade validation hook before submit. |
| Replace/group orders | `POST /v2/orders/groups`, `POST /v2/orders/groups/confirm` | Use for staged multi-leg or grouped flows. |
| Query account orders | `GET /v2/accounts/{account_keys}/orders` | Read model backfill when stream reconnects. |
| Query by order id | `GET /v2/orders/{order_id}` | Canonical point-in-time order status reconciliation. |
| Venue metadata | `GET /v2/orderexecution/exchanges`, `GET /v2/orderexecution/activationtriggers` | Static/cached reference data for routing/validation UI. |

## Fills / Executions

| Capability | Endpoint | Notes for Meridian |
|---|---|---|
| Execution details via order resources | `GET /v2/orders/{order_id}` and account-order listings | TradeStation commonly models fills as order-level execution detail updates rather than a separate universal fills collection endpoint. |
| Streaming order updates (v3 docs mention order stream quota category) | Streaming order services (environment-specific) | Adapter should treat order stream as primary event source and snapshots as reconciliation. |

## Market Data (Realtime)

| Capability | Endpoint | Notes for Meridian |
|---|---|---|
| Quote snapshot | `GET /v2/data/quote/{symbols}` | Multi-symbol snapshot; useful for initial state seeding. |
| Quote stream (changes) | `GET /v2/stream/quote/changes/{symbols}` | Long-lived chunked HTTP stream; not websocket-native in v2 reference. |
| Quote stream (snapshots) | `GET /v2/stream/quote/snapshots/{symbols}` | Useful for periodic reset/re-sync cycles. |
| Bar chart stream | `GET /v2/stream/barchart/{symbol}/{interval}/{unit}` (+ variants) | Can deliver history+realtime in one stream session. |
| Tick bars stream | `GET /v2/stream/tickbars/{symbol}/{interval}/{barsBack}` | Tick aggregation feed. |

## Historical Retrieval

| Capability | Endpoint | Notes for Meridian |
|---|---|---|
| Historical minute/date-range bars | Bar chart stream endpoints with `barsBack`, `startDate`, `endDate` variants | TradeStation applies intraday-history limits (bar count, minute credits, lookback window); adapter must window requests. |
| Historical + realtime continuation | Keep bar stream open after initial history load | Prefer over repeated snapshot calls to reduce quota pressure. |

---

## 2) Behavior Differences vs Existing Meridian Providers

## Event sequencing / stream lifecycle

1. **Chunked HTTP streams with explicit terminal control strings (`END`, `ERROR`)** differ from websocket-first providers (e.g., typical polygon/alpaca realtime connectors).
   - **Handling rule:** parser must be object-framed above HTTP chunk boundaries and detect non-JSON terminators.
2. **Chunk boundaries are non-message boundaries** (JSON may be split across chunks or multiple JSON objects in one chunk).
   - **Handling rule:** maintain incremental buffer + streaming tokenizer; never parse per-chunk directly.
3. **Order/position eventual consistency** can require stream-first + snapshot-reconcile logic.
   - **Handling rule:** treat stream events as timeline driver; schedule periodic `positions/orders` snapshot repair.

## Status semantics

1. **Order status transitions may be venue/product specific** and can include partially-filled/intermediate states not 1:1 with internal `ExecutionStatus` enums used by existing adapters.
   - **Handling rule:** define explicit TradeStation->Meridian status mapping table with fallback `UnknownExternalStatus` telemetry.
2. **SIM behavior (instant simulated fills)** differs from LIVE latency/partial behavior.
   - **Handling rule:** environment-aware fill expectation in tests and replay verification.

## Pagination / volume controls / quotas

1. **Per-resource quota windows** (e.g., balances, positions, quote endpoints) differ from providers where limits are mostly per-second REST throttles.
   - **Handling rule:** register provider-aware quota buckets in `ProviderRateLimitTracker` and adaptive polling budgets.
2. **Historical minute retrieval hard limits** (max bars per request, max minute span, credits).
   - **Handling rule:** deterministic window slicer with resume tokens/checkpoints in backfill worker.

---

## 3) Adapter Handling Rules (Implementation Contract)

1. **Transport rules**
   - Implement chunked-stream reader with:
     - incremental UTF-8 decode
     - JSON object boundary detection
     - `END` and `ERROR` sentinel handling
     - jittered reconnect policy
2. **State reconciliation rules**
   - Keep in-memory order book keyed by external order id.
   - Merge stream deltas first, then periodic snapshot correction for orders + positions.
   - Use idempotent upserts on execution fragments to avoid duplicate fills during reconnect replays.
3. **Status mapping rules**
   - Maintain provider-specific enum map in one module (no ad hoc mapping in handlers).
   - Emit structured warning metric/log when unknown external status encountered.
4. **Rate-limit rules**
   - Distinct buckets: accounts/balances/positions/orders/quotes/streams.
   - Backoff + retry on quota responses and surface cooldown ETAs to operator logs.
5. **Historical rules**
   - Window historical requests to TradeStation limits.
   - Persist checkpoints so interrupted backfills resume without re-pulling full ranges.

---

## 4) Meridian Module Mapping and Integration Points

## Proposed adapter module layout

| TradeStation capability | Adapter module(s) | Meridian integration point(s) |
|---|---|---|
| Auth/session | `src/Meridian.Infrastructure/Adapters/TradeStation/TradeStationAuthClient.cs`, `TradeStationTokenStore.cs` | Credential/config pipeline and provider construction in `ProviderFactory`. |
| Account/balances/positions | `TradeStationAccountClient.cs` | Brokerage/account read models and provider health surfaces; reconciliation jobs. |
| Orders/executions | `TradeStationOrderClient.cs`, `TradeStationExecutionMapper.cs` | Execution services and order-state projections used by workstation trading/operator inbox. |
| Streaming market data | `TradeStationMarketDataClient.cs` (implements `IMarketDataClient`) | Collector pipeline (`TradeDataCollector`, `MarketDepthCollector`) + provider registry discovery. |
| Historical bars/quotes/trades | `TradeStationHistoricalDataProvider.cs` (implements `IHistoricalDataProvider`) | Backfill worker + composite provider failover path. |
| Symbol mapping/search (optional phase) | `TradeStationSymbolSearchProvider.cs` | Symbol registry/search APIs and UI symbol lookup. |

## Existing Meridian integration files to wire

- Provider interfaces/registry/factory:
  - `src/Meridian.ProviderSdk/IMarketDataClient.cs`
  - `src/Meridian.Infrastructure/Adapters/Core/IHistoricalDataProvider.cs`
  - `src/Meridian.Infrastructure/Adapters/Core/ProviderRegistry.cs`
  - `src/Meridian.Infrastructure/Adapters/Core/ProviderFactory.cs`
  - `src/Meridian.Infrastructure/Adapters/Core/ProviderServiceExtensions.cs`
- Backfill orchestration:
  - `src/Meridian.Infrastructure/Adapters/Core/Backfill/BackfillWorkerService.cs`
- Rate limiting:
  - `src/Meridian.Infrastructure/Adapters/Core/RateLimiting/ProviderRateLimitTracker.cs`
- UI/API read-model flow (orders/readiness/inbox projections):
  - `src/Meridian.Ui.Shared/`
  - workstation API routing/services under `src/Meridian.Ui/` and `src/Meridian.Ui.Services/`

---

## 5) Provider-Difference Checklist for Build Readiness

Before enabling TradeStation in production routing, verify:

1. Stream parser survives split/multi-object chunk patterns and `END`/`ERROR` termination behavior.
2. Order-status mapping table is complete for SIM + LIVE observed states.
3. Snapshot reconciliation closes drift between executions and positions after reconnect.
4. Backfill windowing respects minute-history constraints and resumes from checkpoints.
5. Quota-throttle behavior is observable in logs/metrics and does not starve critical account/order polling.

---

## 6) Notes

- This inventory is intentionally endpoint-focused and adapter-oriented for Meridian implementation planning.
- As TradeStation continues migrating/expanding v3 resources, keep this file synced with the latest `/docs/specification` output and production traffic observations from SIM and LIVE.
