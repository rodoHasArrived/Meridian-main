# Provider Core Hardening Notes

## Canonical reusable patterns (`src/Meridian.Infrastructure/Adapters/Core/`)

### 1) Cancellation
- Provider operations should accept and forward `CancellationToken` to all async waits and I/O.
- Canonical primitive: `BaseHistoricalDataProvider.ExecuteGetAsync(...)` and `ExecuteGetAndReadAsync(...)` propagate `ct` through rate-limiter waits, resilience execution, HTTP calls, and content reads.

### 2) Timeout
- Canonical timeout behavior is owned by `HttpResiliencePolicy.CreateComprehensivePipeline(...)`.
- It applies:
  - per-request timeout (`requestTimeout`, default 30s), and
  - total operation timeout (5 minutes) around the composed strategy.

### 3) Retry
- Canonical retry behavior is Polly-based and centralized in `HttpResiliencePolicy`.
- Preferred for provider HTTP GETs:
  - `BaseHistoricalDataProvider`'s `ResiliencePipeline` (created via `CreateComprehensivePipeline(...)`)
  - retry with exponential backoff + jitter on transient HTTP/network failures.

### 4) Throttling
- Canonical throttling primitive is `RateLimiter` (`Core/RateLimiting/RateLimiter.cs`).
- Provider usage should go through `BaseHistoricalDataProvider.WaitForRateLimitSlotAsync(...)`, which also updates request counters used by `IRateLimitAwareProvider` status.

### 5) Error mapping
- Canonical response classification is `HttpResponseHandler.TryHandleResponseAsync(...)` as used by `BaseHistoricalDataProvider.HandleHttpResponseAsync(...)`.
- Standard categories:
  - `404` => not found (empty result path)
  - `401/403` => auth failure
  - `429` => rate limited (+ Retry-After propagation)
  - `5xx` => transient/server failure
  - other non-success => provider error

## Refactor summary (shared primitives preferred where equivalent)

The following providers were updated to prefer base shared primitives instead of direct ad hoc HTTP handling:

- `AlphaVantageHistoricalDataProvider`
  - Switched daily and intraday fetch flows to `ExecuteGetAndReadAsync(...)`.
  - Removed duplicated manual `WaitForRateLimitSlotAsync(...) + Http.GetAsync(...) + ResponseHandler.HandleResponseAsync(...)` pattern.

- `NasdaqDataLinkHistoricalDataProvider`
  - Switched adjusted-daily flow to `ExecuteGetAndReadAsync(...)`.
  - Preserved empty-result behavior for not-found payloads.

- `StooqHistoricalDataProvider`
  - Switched daily-bar fetch flow to `ExecuteGetAndReadAsync(...)`.
  - Preserved empty-result behavior for not-found payloads.

- `YahooFinanceHistoricalDataProvider`
  - Switched daily-bar flow to `ExecuteGetAndReadAsync(...)`.
  - Switched intraday chunk fetching to `ExecuteGetAsync(...)` + `HandleHttpResponseAsync(...)` for shared retry/timeout/throttle/error mapping semantics.

## Documented exceptions and rationale

1. Yahoo Finance intraday `422 UnprocessableEntity` handling remains provider-specific.
   - Rationale: Yahoo may return semantically rich vendor messages for interval/range constraints that are not generic transport failures.
   - We preserve custom extraction (`TryExtractYahooErrorDescription(...)`) and throw a tailored domain message before generic mapping.

2. Alpha Vantage body-level rate-limit semantics remain provider-specific.
   - Rationale: Alpha Vantage frequently returns HTTP 200 with a body `Note` message indicating quota exhaustion.
   - This cannot be inferred from status code alone, so body inspection remains necessary after canonical transport-level handling.

## Migration guidance for future provider work

When implementing or refactoring provider HTTP flows:

1. Prefer `ExecuteGetAndReadAsync(...)` for simple GET+read scenarios.
2. Prefer `ExecuteGetAsync(...)` + `HandleHttpResponseAsync(...)` if provider logic needs access to raw response metadata.
3. Keep provider-specific branches only where vendor contract behavior is non-standard (e.g., HTTP 200 error payloads, unique status semantics).
4. Avoid direct calls to `ResponseHandler` in provider adapters; use base primitives unless a legacy shim path is explicitly required.
