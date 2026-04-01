# High-Impact Improvements Analysis

**Date:** 2026-02-06
**Version Analyzed:** 1.6.1
**Scope:** Functionality, reliability, and user experience

---

## Executive Summary

After a thorough codebase analysis spanning architecture, resilience, testing, and UX, the following improvements are ranked by impact-to-effort ratio. They target three themes: **closing critical reliability gaps**, **completing the user-facing surface**, and **unlocking operational confidence**.

Of the original 15 improvements identified, **10 have been fully implemented**, **3 are partially complete**, and **2 remain open**. This update reflects the current state and introduces **4 new improvement areas** uncovered during the latest review.

### Progress Snapshot

| Status | Count | Items |
|--------|-------|-------|
| Completed | 10 | #1, #3, #4, #5, #6, #9, #10, #11, #12, #14 |
| Partially Complete | 3 | #2, #8, #13 |
| Not Started | 2 | #7, #15 |
| New (added this review) | 4 | #16, #17, #18, #19 |

---

## Completed Improvements

### 1. Implement Automatic Resubscription on WebSocket Reconnect

**Impact: Critical | Area: Streaming Reliability | Status: COMPLETED**

Both Alpaca and Polygon providers now maintain subscription registries via `SubscriptionManager` and automatically replay all active subscriptions on WebSocket reconnect. `WebSocketConnectionManager` provides a reconnection callback mechanism that each provider uses to re-authenticate and resubscribe.

**Implementation:**
- `SubscriptionManager` tracks subscriptions by kind with `GetSymbolsByKind()` for recovery
- `AlpacaMarketDataClient.OnConnectionLostAsync()` passes an `onReconnected` callback that re-authenticates and calls `TrySendSubscribeAsync()`
- `PolygonMarketDataClient.ResubscribeAllAsync()` replays trades, quotes, and aggregate subscriptions after `ConnectAsync()`
- Both providers log successful resubscription events

**Files:** `Infrastructure/Resilience/WebSocketConnectionManager.cs`, `Infrastructure/Providers/Streaming/Alpaca/AlpacaMarketDataClient.cs`, `Infrastructure/Providers/Streaming/Polygon/PolygonMarketDataClient.cs`, `Infrastructure/Providers/Shared/SubscriptionManager.cs`

---

### 3. Close the API Route Implementation Gap

**Impact: High | Area: Web Dashboard UX | Status: COMPLETED (Phase 1)**

All unimplemented routes now return `501 Not Implemented` with a structured JSON body (`{ "error": "Not yet implemented", "route": "...", "planned": true }`). `StubEndpoints.cs` registers 180 stub routes covering symbol management, backfill, provider status, storage, diagnostics, admin/maintenance, analytics, system health, and messaging endpoints.

**Remaining work (Phase 2-3):** Implement handler logic for highest-value stub groups. Core endpoints (status, config, backfill, failover, providers) are already fully functional.

**Files:** `Ui.Shared/Endpoints/StubEndpoints.cs`, `Contracts/Api/UiApiRoutes.cs`

---

### 4. Add Real-Time Dashboard Updates via Server-Sent Events

**Impact: High | Area: Web UX | Status: COMPLETED**

SSE endpoint at `/api/events/stream` pushes status updates every 2 seconds including event throughput, active subscriptions, provider health, backpressure level, and recent errors. The dashboard template includes a JavaScript `EventSource` client that updates DOM elements in real time, with automatic fallback to polling if SSE connection drops (reconnects after 10 seconds).

**Files:** `Ui.Shared/Endpoints/StatusEndpoints.cs`, `Ui.Shared/HtmlTemplates.cs`

---

### 5. Fix Storage Sink Disposal Race Condition

**Impact: High | Area: Data Durability | Status: COMPLETED**

Both `JsonlStorageSink` and `ParquetStorageSink` now follow the correct disposal sequence: cancel disposal token, dispose flush timer (waiting for pending callbacks), execute guaranteed final flush under semaphore gate, then dispose writers and remaining resources.

**Remaining work (low priority):** Extract shared buffering/flushing logic into a `BufferedSinkBase` class to prevent future divergence between the two sinks.

**Files:** `Storage/Sinks/JsonlStorageSink.cs`, `Storage/Sinks/ParquetStorageSink.cs`

---

### 6. Add Provider Factory with Runtime Switching

**Impact: High | Area: Architecture / Flexibility | Status: COMPLETED**

`IMarketDataClientFactory` and `MarketDataClientFactory` replace the previous switch statement in `Program.cs`. The factory supports IB, Alpaca, Polygon, StockSharp, and NYSE providers. Runtime provider switching is enabled via the `/api/config/data-source` POST endpoint, and the failover chain creates client instances dynamically from the factory.

**Files:** `Infrastructure/Providers/MarketDataClientFactory.cs`, `Program.cs`, `Ui.Shared/Endpoints/ConfigEndpoints.cs`

---

### 9. Add Backfill Progress Reporting

**Impact: Medium-High | Area: UX | Status: COMPLETED**

`BackfillProgressTracker` tracks per-symbol progress with date ranges, calculates percentage complete per symbol and overall, and provides `BackfillProgressSnapshot` with detailed metrics (completed symbols, failed symbols, errors). Exposed via `/api/backfill/progress` endpoint.

**Files:** `Infrastructure/Providers/Backfill/BackfillProgressTracker.cs`, `Infrastructure/Providers/Historical/Queue/BackfillWorkerService.cs`, `Ui.Shared/Endpoints/BackfillEndpoints.cs`

---

### 10. Harden WAL Recovery for Large Files

**Impact: Medium | Area: Durability | Status: COMPLETED**

`WriteAheadLog.GetUncommittedRecordsAsync()` now uses `IAsyncEnumerable<WalRecord>` with streaming reads and processes records in batches of 10,000. A configurable `UncommittedSizeWarningThreshold` (default 50MB) logs warnings when uncommitted WAL data exceeds the threshold. Full SHA256 checksums replace the previous truncated 8-byte variant.

**Files:** `Storage/Archival/WriteAheadLog.cs`

---

### 11. Add OpenAPI/Swagger Documentation

**Impact: Medium | Area: Developer UX / Integration | Status: COMPLETED**

`Swashbuckle.AspNetCore` and `Microsoft.AspNetCore.OpenApi` are integrated. Swagger UI is served at `/swagger` in development mode with the OpenAPI spec at `/swagger/v1/swagger.json`. An `ApiDocumentationService` provides additional documentation generation.

**Remaining work:** Add `[ProducesResponseType]` annotations to endpoint handlers for complete schema documentation in the generated spec.

**Files:** `Ui.Shared/Endpoints/UiEndpoints.cs`, `Meridian.Ui.Shared.csproj`

---

### 12. Fix SubscriptionManager Memory Leak

**Impact: Medium | Area: Reliability | Status: COMPLETED**

`SubscriptionManager.Unsubscribe()` and `UnsubscribeSymbol()` now properly remove entries from internal dictionaries. A `Count` property exposes active subscription count for monitoring. All subscription lifecycle events (subscribe, unsubscribe) are logged at Debug level.

**Files:** `Infrastructure/Providers/Shared/SubscriptionManager.cs`

---

### 14. Add Authentication to HTTP Endpoints

**Impact: Medium | Area: Security | Status: COMPLETED**

`ApiKeyMiddleware` enforces API key authentication via `X-Api-Key` header or `api_key` query parameter. Reads from `MDC_API_KEY` environment variable with constant-time string comparison to prevent timing attacks. `ApiKeyRateLimitMiddleware` enforces 120 requests/minute per key with sliding window, returns `429 Too Many Requests` with `Retry-After` header. Health check endpoints (`/healthz`, `/readyz`, `/livez`) are exempt. If `MDC_API_KEY` is not set, all requests are allowed (backward compatible).

**Files:** `Ui.Shared/Endpoints/ApiKeyMiddleware.cs`, `Ui.Shared/Endpoints/UiEndpoints.cs`

---

## Partially Complete Improvements

### 2. Add Exponential Backoff to Backfill Rate Limit Handling

**Impact: High | Area: Backfill Reliability | Status: PARTIALLY COMPLETE**

Exponential backoff (2s base, 60s cap) with jitter is implemented in `BackfillWorkerService`. Retry budget is enforced at 3 attempts per request.

**Remaining work:**
- Parse the `Retry-After` response header from HTTP 429 responses to honor provider-specified cooldown periods
- Currently detects rate limits from exception messages rather than HTTP headers

**Files:** `Infrastructure/Providers/Historical/Queue/BackfillWorkerService.cs`

---

### 8. Implement Dropped Event Audit Trail

**Impact: Medium-High | Area: Data Integrity | Status: PARTIALLY COMPLETE**

`DroppedEventAuditTrail` logs dropped events to `_audit/dropped_events.jsonl` in JSONL format with timestamp, event type, symbol, sequence, source, and drop reason. Integrated with `EventPipeline` and tracks drop counts per symbol via `ConcurrentDictionary`.

**Remaining work:**
- Add `/api/quality/drops` HTTP endpoint to expose `DroppedEventStatistics` for gap-aware consumers
- Optionally trigger backfill for symbols with significant drops

**Files:** `Application/Pipeline/DroppedEventAuditTrail.cs`, `Application/Pipeline/EventPipeline.cs`

---

### 13. Reduce GC Pressure in Hot Message Paths

**Impact: Medium | Area: Performance | Status: PARTIALLY COMPLETE**

StockSharp `MessageConverter` uses `ObjectPool<List<OrderBookLevel>>` with pre-sized lists (32 items) and proper try/finally return-to-pool patterns.

**Remaining work:**
- Polygon WebSocket message handler still uses `JsonDocument.Parse()` per message without pooling, creates heap allocations via `Encoding.UTF8.GetString(messageBuilder.ToArray())` at ~100 Hz
- Apply `ObjectPool<T>` and `Utf8JsonReader` / `Span<T>`-based parsing to `PolygonMarketDataClient`
- Benchmark before/after with `Meridian.Benchmarks`

**Files:** `Infrastructure/Providers/Streaming/Polygon/PolygonMarketDataClient.cs`

---

## Open Improvements

### 7. Add Endpoint Integration Tests

**Impact: High | Area: Quality / Regression Prevention | Status: NOT STARTED**

The HTTP API layer has no dedicated integration tests using `WebApplicationFactory<T>`. Only `EndpointStubDetectionTests.cs` exists, which validates route format and discovers unmapped routes but does not test actual HTTP request/response pairs.

**What to do:**
- Use `WebApplicationFactory<T>` from `Microsoft.AspNetCore.Mvc.Testing`
- Write tests for all implemented endpoints: status, config, backfill, failover, providers
- Assert response status codes, content types, and response schema shapes
- Include negative cases (invalid input, missing config)

**Files:** New `tests/Meridian.Tests/Integration/EndpointTests/`

---

### 15. Consolidate Desktop App Navigation

**Impact: Medium | Area: Desktop UX | Status: PARTIALLY COMPLETE**

WPF has been consolidated into 5 workspaces (Monitor, Collect, Storage, Quality, Settings) with ~15 navigation items and a command palette (Ctrl+K). UWP still has 129 flat `NavigationViewItem` elements with workspace headers but no actual consolidation - users see all 40+ pages in a single menu.

**Remaining work:**
- Consolidate UWP `MainPage.xaml` navigation to match the WPF workspace model
- Reduce UWP navigation items to ~15 consolidated entries per workspace
- Ensure command palette (Ctrl+K) surfaces the same grouped search experience

**Files:** `Meridian.Uwp/Views/MainPage.xaml`, `Meridian.Uwp/Services/NavigationService.cs`

---

## New Improvements

### 16. Add `/api/quality/drops` Endpoint for Drop Statistics

**Impact: Medium | Area: Observability | Effort: Low**

The `DroppedEventAuditTrail` collects detailed drop statistics but this data is not exposed via the HTTP API. Gap-aware consumers and dashboards have no programmatic way to query drop events.

**What to do:**
- Add `GET /api/quality/drops` endpoint returning `DroppedEventStatistics` (total drops, per-symbol breakdown, recent drop events)
- Add `GET /api/quality/drops/{symbol}` for per-symbol drill-down
- Include drop rate in the existing `/api/status` response

**Files:** `Ui.Shared/Endpoints/` (new handler), `Application/Pipeline/DroppedEventAuditTrail.cs`

---

### 17. Add `Retry-After` Header Parsing to Backfill Rate Limiting

**Impact: Medium | Area: Backfill Reliability | Effort: Low**

`BackfillWorkerService` detects rate limits from exception message text (`ex.Message.Contains("429")`) rather than parsing HTTP response headers. This misses provider-specified cooldown periods and may retry too aggressively or too conservatively.

**What to do:**
- Catch `HttpRequestException` and extract `Retry-After` from the response headers
- If present, use provider-specified delay instead of calculated exponential backoff
- Fall back to exponential backoff when header is absent
- Log the source of the delay decision (provider-specified vs. calculated)

**Files:** `Infrastructure/Providers/Historical/Queue/BackfillWorkerService.cs`

---

### 18. Complete Polygon WebSocket Zero-Allocation Message Parsing

**Impact: Medium | Area: Performance | Effort: Medium**

The Polygon WebSocket handler allocates per-message via `JsonDocument.Parse()`, `Encoding.UTF8.GetString()`, and `List<T>` construction at ~100 Hz. StockSharp has already been optimized with `ObjectPool<T>`, demonstrating the pattern.

**What to do:**
- Replace `JsonDocument.Parse()` with `Utf8JsonReader` for zero-copy parsing
- Pool `List<OrderBookLevel>` using `ObjectPool<T>` (matching StockSharp pattern)
- Eliminate `Encoding.UTF8.GetString(messageBuilder.ToArray())` by reading directly from `ReadOnlySpan<byte>`
- Validate with `Meridian.Benchmarks` before/after

**Files:** `Infrastructure/Providers/Streaming/Polygon/PolygonMarketDataClient.cs`

---

### 19. Add Endpoint `[ProducesResponseType]` Annotations for OpenAPI Completeness

**Impact: Low-Medium | Area: Developer UX | Effort: Medium**

Swagger infrastructure is in place but the generated OpenAPI spec lacks response type documentation. Without `[ProducesResponseType]` annotations, the spec shows generic `200 OK` for all endpoints with no schema information.

**What to do:**
- Add `[ProducesResponseType]` attributes to all implemented endpoint handlers
- Include error response types (400, 401, 404, 429, 500, 501)
- Add XML documentation comments for request/response models
- Generate and publish the OpenAPI spec as a CI build artifact

**Files:** `Ui.Shared/Endpoints/*.cs`, `Contracts/Api/*.cs`

---

## Updated Priority Matrix

| # | Improvement | Impact | Effort | Priority | Status |
|---|------------|--------|--------|----------|--------|
| 1 | WebSocket resubscription | Critical | Low | **P0** | DONE |
| 2 | Backfill rate limit backoff | High | Low | **P0** | DONE (partial) |
| 5 | Storage sink disposal fix | High | Low | **P0** | DONE |
| 12 | Subscription memory leak fix | Medium | Low | **P0** | DONE |
| 3 | API route gap closure | High | Medium | **P1** | DONE (Phase 1) |
| 4 | Real-time dashboard (SSE) | High | Medium | **P1** | DONE |
| 6 | Provider factory pattern | High | Medium | **P1** | DONE |
| 8 | Dropped event audit trail | Med-High | Low | **P1** | DONE (partial) |
| 9 | Backfill progress reporting | Med-High | Medium | **P1** | DONE |
| 7 | Endpoint integration tests | High | Medium | **P2** | **OPEN** |
| 10 | WAL recovery hardening | Medium | Medium | **P2** | DONE |
| 14 | HTTP API authentication | Medium | Medium | **P2** | DONE |
| 11 | OpenAPI documentation | Medium | Low | **P2** | DONE |
| 13 | GC pressure reduction | Medium | Medium | **P3** | PARTIAL |
| 15 | Desktop nav consolidation | Medium | High | **P3** | PARTIAL |

### New Items

| # | Improvement | Impact | Effort | Priority | Status |
|---|------------|--------|--------|----------|--------|
| 16 | `/api/quality/drops` endpoint | Medium | Low | **P1** | **OPEN** |
| 17 | `Retry-After` header parsing | Medium | Low | **P1** | **OPEN** |
| 18 | Polygon zero-alloc parsing | Medium | Medium | **P2** | **OPEN** |
| 19 | OpenAPI response annotations | Low-Med | Medium | **P3** | **OPEN** |

---

## Impact Summary

**P0 items are complete.** All critical reliability gaps (WebSocket resubscription, backfill backoff, storage sink disposal, subscription cleanup) have been addressed.

**P1 items are largely complete.** The API surface is covered with stub responses, SSE enables real-time monitoring, the provider factory enables runtime switching, and backfill progress is visible. Remaining P1 work is incremental: exposing drop statistics via HTTP (#16) and parsing `Retry-After` headers (#17).

**P2 items are mostly complete.** WAL recovery, API authentication, and OpenAPI documentation are done. The primary remaining gap is **endpoint integration tests** (#7), which is the single highest-value open item for regression prevention.

**P3 items are in progress.** Polygon message parsing optimization (#13/#18) and UWP navigation consolidation (#15) are the main remaining efforts.

### Recommended Next Actions

1. **Add endpoint integration tests (#7)** - Highest remaining value; prevents regressions in the growing HTTP API surface
2. **Expose drop statistics endpoint (#16)** - Low effort, completes the observability story
3. **Parse `Retry-After` headers (#17)** - Low effort, improves backfill reliability with rate-limited providers
4. **Optimize Polygon message parsing (#18)** - Medium effort, measurable latency improvement for high-frequency data
5. **Consolidate UWP navigation (#15)** - Only if UWP remains actively maintained alongside WPF
