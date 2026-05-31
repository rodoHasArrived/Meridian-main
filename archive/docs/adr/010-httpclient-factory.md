# ADR-010: HttpClientFactory for HTTP Client Lifecycle Management

**Status:** Accepted
**Date:** 2024-11-20
**Deciders:** Core Team

## Context

The Meridian makes numerous HTTP calls to external APIs:
- 10+ historical data providers (Alpaca, Polygon, Tiingo, Yahoo Finance, etc.)
- 5+ streaming providers with REST endpoints for authentication
- 4+ symbol search providers
- Various internal services (credential validation, webhooks, OAuth)

Originally, HTTP clients were created as instance fields or per-request:

```csharp
// Anti-pattern: Instance HttpClient
private readonly HttpClient _client = new HttpClient();

// Anti-pattern: Per-request HttpClient
using var client = new HttpClient();
```

These patterns cause serious problems:
1. **Socket exhaustion**: Each `HttpClient` holds sockets that aren't released until garbage collection
2. **DNS caching issues**: `HttpClient` caches DNS indefinitely, missing updates
3. **Inconsistent configuration**: Timeouts, headers, and policies scattered across codebase
4. **Difficult testing**: Hard to mock HTTP calls without DI

## Decision

Use `IHttpClientFactory` from `Microsoft.Extensions.Http` for all HTTP client creation:

1. **Named clients** for each provider/service with specific configuration
2. **Centralized registration** in `HttpClientConfiguration.cs`
3. **Standard resilience policies** (retry, circuit breaker) via Polly
4. **Backward-compatible factory** for transitional code

## Implementation Links

<!-- These links are verified by the build process -->

| Component | Location | Purpose |
|-----------|----------|---------|
| Client Names | `src/Meridian.Infrastructure/Http/HttpClientConfiguration.cs` | Named client identifiers |
| Registration | `src/Meridian.Infrastructure/Http/HttpClientConfiguration.cs` | DI configuration |
| Resilience Policies | `src/Meridian.Infrastructure/Http/SharedResiliencePolicies.cs` | Retry/circuit breaker |
| Alpaca Provider | `src/Meridian.Infrastructure/Adapters/Alpaca/AlpacaMarketDataClient.cs` | Usage example |
| Historical Providers | `src/Meridian.Infrastructure/Adapters/Core/` | Provider implementations |

## Rationale

### Named Client Pattern

Each external service gets a dedicated named client with specific configuration:

```csharp
public static class HttpClientNames
{
    public const string Alpaca = "alpaca";
    public const string AlpacaData = "alpaca-data";
    public const string Polygon = "polygon";
    public const string TiingoHistorical = "tiingo-historical";
    // ... 20+ named clients
}

// Registration
services.AddHttpClient(HttpClientNames.TiingoHistorical)
    .ConfigureHttpClient(client =>
    {
        client.BaseAddress = new Uri("https://api.tiingo.com/");
        client.Timeout = TimeSpan.FromSeconds(30);
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
    })
    .AddStandardResiliencePolicy();
```

### Standard Resilience Policies

All clients receive consistent retry and circuit breaker policies:

```csharp
// Retry with exponential backoff
HttpPolicyExtensions
    .HandleTransientHttpError()
    .OrResult(msg => msg.StatusCode == HttpStatusCode.TooManyRequests)
    .WaitAndRetryAsync(
        retryCount: 3,
        sleepDurationProvider: retryAttempt =>
            TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));

// Circuit breaker to prevent cascading failures
HttpPolicyExtensions
    .HandleTransientHttpError()
    .CircuitBreakerAsync(
        handledEventsAllowedBeforeBreaking: 5,
        durationOfBreak: TimeSpan.FromSeconds(30));
```

### Benefits

| Benefit | Description |
|---------|-------------|
| Connection pooling | Automatic socket reuse and lifecycle management |
| DNS refresh | Connections recycled to pick up DNS changes |
| Centralized config | All client settings in one place |
| Consistent policies | Same retry/timeout behavior across providers |
| Testability | Easy to mock via DI |
| Observability | Can add logging handlers |

## Alternatives Considered

### Alternative 1: Static HttpClient Instances

Use `static readonly HttpClient` per provider class.

**Pros:**
- Simple implementation
- No DI complexity

**Cons:**
- DNS caching issues remain
- Configuration scattered
- Difficult to test
- No connection pooling control

**Why rejected:** Doesn't solve DNS or testability issues.

### Alternative 2: Custom Connection Pool

Build custom HTTP connection management.

**Pros:**
- Full control over behavior

**Cons:**
- Significant development effort
- Likely to have bugs
- Duplicates existing framework functionality

**Why rejected:** Reinventing the wheel.

### Alternative 3: Third-Party Libraries (Refit, RestSharp)

Use higher-level HTTP abstraction libraries.

**Pros:**
- Less boilerplate
- Declarative API definitions

**Cons:**
- Additional dependencies
- Learning curve
- May not fit all use cases

**Why rejected:** `IHttpClientFactory` provides sufficient abstraction with less overhead.

## Consequences

### Positive

- No more socket exhaustion under load
- DNS changes picked up automatically
- Consistent timeout and retry behavior
- Easier testing with DI
- Single place to add logging/metrics
- Clear client naming improves code readability

### Negative

- Must inject `IHttpClientFactory` instead of using `new HttpClient()`
- Named clients require string constants (mitigated by `HttpClientNames` class)
- Transitional period required to migrate existing code

### Neutral

- Slight increase in startup time for client registration
- Polly dependency for resilience policies

## Compliance

### Code Contracts

```csharp
// Clients MUST be obtained from factory
public class MyProvider
{
    private readonly IHttpClientFactory _clientFactory;

    public MyProvider(IHttpClientFactory clientFactory)
    {
        _clientFactory = clientFactory;
    }

    public async Task CallApiAsync(CancellationToken ct)
    {
        // CORRECT: Use factory
        using var client = _clientFactory.CreateClient(HttpClientNames.MyService);

        // WRONG: Direct instantiation
        // using var client = new HttpClient();
    }
}
```

### Migration Path

For code not yet converted to DI, use the transitional factory:

```csharp
// Transitional pattern (temporary)
var client = HttpClientFactoryProvider.CreateClient(HttpClientNames.MyService);
```

New code should always inject `IHttpClientFactory` directly.

### Runtime Verification

- `[ImplementsAdr("ADR-010")]` on `HttpClientConfiguration`
- Startup validation ensures factory is initialized
- Connection pool metrics available via health endpoints

## References

- [Microsoft HttpClientFactory Docs](https://docs.microsoft.com/en-us/dotnet/core/extensions/httpclient-factory)
- [Polly Resilience Library](https://github.com/App-vNext/Polly)
- [Steve Gordon's HttpClientFactory Series](https://www.stevejgordon.co.uk/introduction-to-httpclientfactory-aspnetcore)
- [ADR-001: Provider Abstraction](001-provider-abstraction.md) - Provider interfaces

---

*Last Updated: 2026-02-20*
