# Corporate Actions Data Ingestion Implementation Summary

## Files Created

### 1. `src/Meridian.Infrastructure/Adapters/Polygon/PolygonCorporateActionFetcher.cs`
- **Size**: ~405 lines
- **Purpose**: Background service that fetches dividend and stock split data from Polygon.io REST API
- **Key Components**:
  - Interface `IPolygonCorporateActionFetcher`: Public contract for fetching corporate actions
  - Class `PolygonCorporateActionFetcher`: Sealed class implementing both `IPolygonCorporateActionFetcher` and `IHostedService`
  - Implements rate limiting using `RateLimiter` class (5 requests/min, 0.5s min delay)
  - Graceful error handling with structured logging
  - Supports fetching for single ticker or all active securities
  - Parses JSON responses from two Polygon endpoints:
    - `GET /v3/reference/dividends` for dividend data
    - `GET /v3/reference/splits` for stock split data
  - Converts Polygon responses to `CorporateActionDto` records
  - Persists via `ISecurityMasterEventStore.AppendCorporateActionAsync()`

## Files Modified

### 2. `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.cs`
- **Change**: Added POST endpoint mapping for corporate actions
- **Route**: `POST /api/security-master/{securityId:guid}/corporate-actions`
- **Handler**: Receives `CorporateActionDto`, validates SecurityId matches route parameter, calls `eventStore.AppendCorporateActionAsync()`
- **Returns**: 
  - 200 OK on success
  - 400 Bad Request if SecurityId mismatch
- **Metadata**: WithName="AppendSecurityMasterCorporateAction", supports JSON serialization

### 3. `src/Meridian.Application/Composition/Features/StorageFeatureRegistration.cs`
- **Changes**:
  1. Added using statements for `Meridian.Infrastructure.Adapters.Core` and `Meridian.Infrastructure.Adapters.Polygon`
  2. Added service registrations in `Register()` method:
     - Registered `RateLimiter` singleton with config: 5 requests/min, 0.5s min delay
     - Registered `IPolygonCorporateActionFetcher` interface to `PolygonCorporateActionFetcher` implementation
     - Registered `PolygonCorporateActionFetcher` as direct singleton
     - Registered as `IHostedService` to enable background startup
- **Timing**: Service starts after 30-second delay on application startup

## Technical Implementation Details

### Authentication
- Polygon API key resolved from either:
  1. `AppConfig.DataSources.Sources` list (Polygon provider configuration)
  2. Environment variable `POLYGON_API_KEY` (fallback)
- If no API key is configured, service logs and does not start

### API Integration
- Base URL: `https://api.polygon.io`
- Uses `IHttpClientFactory` (no direct `new HttpClient()`)
- User-Agent header: "Meridian/1.0"
- API Key passed as query parameter: `?apiKey={key}`
- Max 100 results per request, configurable limit

### Data Mapping
**Dividends** (from `/v3/reference/dividends`):
- `cash_amount` → `DividendPerShare`
- `currency` → `Currency`
- `ex_dividend_date` → `ExDate`
- `pay_date` → `PayDate`
- `EventType` = "Dividend"

**Stock Splits** (from `/v3/reference/splits`):
- `split_from` / `split_to` → `SplitRatio` (split_to / split_from)
- `execution_date` → `ExDate`
- `EventType` = "StockSplit"

### Rate Limiting
- Uses existing `RateLimiter` class from `Meridian.Infrastructure.Adapters.Core`
- Configuration: 5 requests per 60-second window, 0.5s minimum delay between requests
- `await _rateLimiter.WaitForSlotAsync(ct)` called before each `AppendCorporateActionAsync()`

### Concurrency & Cancellation
- All async methods accept `CancellationToken ct` parameter
- Background task uses `CancellationTokenSource.CreateLinkedTokenSource()` for clean shutdown
- Handles `OperationCanceledException` gracefully
- `Task.Delay()` used instead of blocking delays
- `ConfigureAwait(false)` on all async calls

### Error Handling
- HTTP errors logged but don't stop processing of other securities
- JSON parsing errors handled with null-safe checks on `JsonElement.GetString()`
- Securities without primary identifiers skipped with debug logging
- All exceptions caught and logged without rethrowing

### Background Execution
- Starts 30 seconds after application startup
- Executes `FetchAndPersistAllAsync()` once on startup
- Searches for all active securities using `ISecurityMasterQueryService.SearchAsync()`
- Rate limits between securities: 500ms delay between fetches
- Responsive to cancellation tokens during all operations

## Key Design Decisions

1. **Sealed Classes**: `PolygonCorporateActionFetcher` is sealed per project conventions (not designed for inheritance)
2. **Dependency Injection**: All dependencies injected via constructor, no service locator patterns
3. **Structured Logging**: Uses semantic parameters (`_logger.LogInformation("message {Param}", param)`) instead of string interpolation
4. **No Blocking**: All I/O operations are async; no `.Result` or `.Wait()` calls
5. **Error Resilience**: HTTP errors don't cascade; logging provides visibility
6. **Attribute Decorators**: Includes `[ImplementsAdr]` attributes referencing ADR-001 and ADR-004
7. **Configuration Resolution**: Supports both config file and environment variables for API key

## ADR Compliance

- **ADR-001**: Provider abstraction pattern followed with `IPolygonCorporateActionFetcher` interface
- **ADR-004**: All async methods include `CancellationToken` parameter
- **ADR-013** (Bounded Channels): Future enhancement - could use bounded channels for fetching if needed
- **ADR-014** (JSON Source Generators): Uses `JsonDocument.Parse()` for Polygon responses (REST API, not our DTOs)

## Testing Recommendations

1. Unit tests for `ParseDividend()` and `ParseSplit()` with mock Polygon JSON responses
2. Integration tests with real Polygon API using test API key
3. Test `FetchAndPersistAllAsync()` with stubbed `ISecurityMasterQueryService` returning test securities
4. Test rate limiter enforcement with timing assertions
5. Test cancellation token propagation across async call chain
6. Test error handling: missing API key, HTTP 401/429/500, malformed JSON

## Future Enhancements

1. Add support for other corporate action types (mergers, rights issues, spinoffs)
2. Implement idempotency to handle retries without duplicating events
3. Add metrics/telemetry for fetch success rates and latency
4. Support scheduled/periodic fetches (not just startup)
5. Add symbol-level granularity options for selective fetching
6. Cache recently fetched tickers to avoid redundant calls within time window
