---
name: meridian-provider-builder
description: >
  Step-by-step guided skill for implementing new data provider adapters in Meridian.
  Use this skill whenever an agent needs to build or extend an IMarketDataClient (streaming),
  IHistoricalDataProvider (backfill), or ISymbolSearchProvider (symbol search) implementation.
  Triggers on: "add a new provider", "implement a data source", "add support for X exchange",
  "create a historical provider for Y", "build a streaming adapter", "add symbol search for Z",
  or whenever code files reference ProviderSdk, DataSourceAttribute, IMarketDataClient, or
  IHistoricalDataProvider in a scaffolding / creation context. This skill produces complete,
  compliant provider implementations with rate limiting, reconnection logic, attribute decoration,
  DI registration, and a matching test scaffold — all anchored to the repository's existing
  patterns and ADR contracts.
license: See repository LICENSE
compatibility: >
  Portable Agent Skill package for Agent Skills-compatible hosts. Requires repository source access
  and optionally reads reference patterns plus companion review/test skills when deeper guidance is needed.
metadata:
  owner: meridian-ai
  version: "1.1"
  spec: open-agent-skills-v1
---
# Meridian — Provider Builder Skill

Build complete, architecturally compliant data provider adapters for Meridian.
Every provider produced by this skill must be ready to pass the `meridian-code-review` skill's
Lens 5 (Provider Implementation Compliance) and Lens 3 (Error Handling & Resilience)
without warnings.

> **Shared project context:** [`../_shared/project-context.md`](../_shared/project-context.md)
> **Reference patterns:** [`references/provider-patterns.md`](references/provider-patterns.md)
> **Code review skill:** [`../meridian-code-review/SKILL.md`](../meridian-code-review/SKILL.md)

---

## Integration Pattern

Every provider build task follows this 4-step workflow:

### 1 — GATHER CONTEXT (MCP)
- Fetch the GitHub issue or feature request describing the new provider
- Read the relevant template file (`_Template/TemplateMarketDataClient.cs` or `TemplateHistoricalDataProvider.cs`)
- Check `docs/ai/ai-known-errors.md` for known provider implementation mistakes

### 2 — ANALYZE & PLAN (Agents)
- Identify the correct provider type using the decision tree below
- Map which compliance checklist items apply to this provider type
- Plan the file set: options, models, implementation, module, tests

### 3 — EXECUTE (Skills + Manual)
- Build files in the prescribed order (Steps 1–12)
- Apply all required attributes, patterns, and conventions
- Write the matching test scaffold

### 4 — COMPLETE (MCP)
- Commit the new provider files and test scaffold
- Create a PR via GitHub summarizing the provider, its capabilities, and the compliance checklist status
- Request review; the `meridian-code-review` skill's Lens 5 checklist is the acceptance gate

---

## When to Use This Skill

Use `meridian-provider-builder` when the task is one of:

- Adding a brand-new streaming provider (implements `IMarketDataClient`)
- Adding a brand-new historical data provider (implements `IHistoricalDataProvider`)
- Adding a symbol search provider (implements `ISymbolSearchProvider`)
- Extending an existing provider with new data types (e.g., adding L2 depth to an existing quotes provider)
- Diagnosing a provider implementation to determine if it is architecturally compliant

Do **not** use this skill for:

- Configuration changes only (use standard editing)
- UI work (use the WPF MVVM patterns from `meridian-code-review` Lens 1)
- General refactoring (use `meridian-code-review`)

---

## Provider Type Decision Tree

Before writing any code, identify the correct provider type:

```
What does this provider supply?
├── Real-time streaming ticks / quotes / L2 order book
│   └── Implement IMarketDataClient
│       File: src/Meridian.ProviderSdk/IMarketDataClient.cs
│       Template: src/Meridian.Infrastructure/Adapters/_Template/TemplateMarketDataClient.cs
│
├── Historical OHLCV / tick data (backfill use case)
│   └── Implement IHistoricalDataProvider
│       File: src/Meridian.Infrastructure/Adapters/Core/IHistoricalDataProvider.cs
│       Template: src/Meridian.Infrastructure/Adapters/_Template/TemplateHistoricalDataProvider.cs
│       Base: BaseHistoricalDataProvider (handles rate limiting + retry automatically)
│
└── Symbol search / lookup (resolving tickers)
    └── Implement ISymbolSearchProvider
        Template: src/Meridian.Infrastructure/Adapters/_Template/TemplateSymbolSearchProvider.cs
        Base: BaseSymbolSearchProvider
```

---

## Step-by-Step Build Process

### Step 1 — Read the Template

**Always** start from the template scaffolding, not from a blank file or from copying another provider.

- Streaming: `src/Meridian.Infrastructure/Adapters/_Template/TemplateMarketDataClient.cs`
- Historical: `src/Meridian.Infrastructure/Adapters/_Template/TemplateHistoricalDataProvider.cs`
- Symbol search: `src/Meridian.Infrastructure/Adapters/_Template/TemplateSymbolSearchProvider.cs`

Read the full template before writing any code. The templates contain inline comments explaining
every required section.

### Step 2 — Create the Provider Directory

```
src/Meridian.Infrastructure/Adapters/{ProviderName}/
├── {ProviderName}MarketDataClient.cs      (streaming) OR
│   {ProviderName}HistoricalDataProvider.cs (historical)
├── {ProviderName}Options.cs               (config)
├── {ProviderName}Models.cs                (response DTOs)
└── {ProviderName}ProviderModule.cs        (DI registration)
```

### Step 3 — Apply All Required Attributes

Every provider class **must** have both attributes:

```csharp
[DataSource("provider-name-kebab-case")]
[ImplementsAdr("ADR-001", "Core streaming/historical data provider contract")]
[ImplementsAdr("ADR-004", "All async methods support CancellationToken")]
public sealed class MyProviderMarketDataClient : IMarketDataClient
```

Missing either attribute is a CRITICAL finding in code review.

### Step 4 — Configuration via IOptionsMonitor

**Never** use `IOptions<T>` for provider settings. Always use `IOptionsMonitor<T>`:

```csharp
// ✅ Correct — supports hot reload
public MyProviderClient(IOptionsMonitor<MyProviderOptions> options, ...) { ... }

// ❌ Wrong — static snapshot, breaks hot reload
public MyProviderClient(IOptions<MyProviderOptions> options, ...) { ... }
```

Define provider options in `{ProviderName}Options.cs` and register in `appsettings.sample.json`.

### Step 5 — Rate Limiting (Historical Providers Only)

Use `BaseHistoricalDataProvider.WaitForRateLimitSlotAsync(ct)` — never implement your own
rate limiting, and never call the non-existent `WaitAsync()`:

```csharp
// ✅ Correct
await WaitForRateLimitSlotAsync(ct);
var response = await _http.GetAsync(url, ct);

// ❌ Wrong method name
await _rateLimiter.WaitAsync(ct);

// ❌ Wrong — skips inherited rate limiting
var response = await _http.GetAsync(url, ct);
```

See `src/Meridian.Infrastructure/Adapters/Core/RateLimiting/RateLimiter.cs` for the
implementation. The correct public method is `WaitForSlotAsync(CancellationToken ct)`.

### Step 6 — WebSocket Reconnection (Streaming Providers Only)

Do **not** implement reconnection from scratch. Use the existing infrastructure:

```csharp
// Use WebSocketConnectionManager for lifecycle
private readonly WebSocketConnectionManager _wsManager;

// Use WebSocketResiliencePolicy for retry/backoff
// File: src/Meridian.Infrastructure/Resilience/WebSocketResiliencePolicy.cs

// Reconnection must be triggered on ANY disconnect:
private async Task OnDisconnectedAsync(CancellationToken ct)
{
    _logger.LogWarning("Provider disconnected, scheduling reconnection");
    await _wsManager.ReconnectAsync(ct);
}
```

A streaming provider with no reconnection path is a CRITICAL failure. The provider must handle:
1. Initial connection failure (retry with backoff)
2. Mid-session disconnect (auto-reconnect and re-subscribe)
3. Graceful shutdown via `CancellationToken`

### Step 7 — JSON Deserialization (ADR-014)

Never use reflection-based JSON serialization in a provider:

```csharp
// ✅ Correct — source-generated context
var result = JsonSerializer.Deserialize(json, MarketDataJsonContext.Default.MyProviderResponse);

// ❌ Wrong — reflection-based (ADR-014 violation)
var result = JsonSerializer.Deserialize<MyProviderResponse>(json);
```

Register new response DTOs in `MarketDataJsonContext` at
`src/Meridian.Core/Serialization/MarketDataJsonContext.cs`:

```csharp
[JsonSerializable(typeof(MyProviderResponse))]
[JsonSerializable(typeof(MyProviderTick))]
public partial class MarketDataJsonContext : JsonSerializerContext { }
```

### Step 8 — HTTP Client Registration (ADR-010)

Use `IHttpClientFactory`, not new `HttpClient()`:

```csharp
// In DI registration (ProviderModule):
services.AddHttpClient<MyProviderClient>(client =>
{
    client.BaseAddress = new Uri(options.BaseUrl);
    client.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
});

// In the provider class:
private readonly HttpClient _http;  // injected by factory
```

### Step 9 — CancellationToken Propagation

Every async method must accept and propagate a `CancellationToken`:

```csharp
// ✅ Correct
public async Task<IReadOnlyList<HistoricalBar>> GetDailyBarsAsync(
    string symbol, DateOnly? from, DateOnly? to, CancellationToken ct = default)
{
    await WaitForRateLimitSlotAsync(ct);
    return await _http.GetFromJsonAsync<List<HistoricalBar>>(url, ctx, ct);
}

// ❌ Wrong — CancellationToken.None discards the caller's cancellation
var result = await _http.GetAsync(url, CancellationToken.None);
```

### Step 10 — DisposeAsync Pattern

Streaming providers must implement `IAsyncDisposable` and cancel outstanding operations:

```csharp
public async ValueTask DisposeAsync()
{
    await DisconnectAsync(CancellationToken.None);
    _cts?.Cancel();
    _wsManager?.Dispose();
    GC.SuppressFinalize(this);
}
```

Historical providers should cancel in-flight HTTP requests and dispose the HttpClient if owned.

### Step 11 — DI Registration

Register via a `IProviderModule` implementation. Do **not** register directly in `Program.cs`:

```csharp
public sealed class MyProviderModule : IProviderModule
{
    public void Register(IServiceCollection services, IConfiguration config)
    {
        services.Configure<MyProviderOptions>(config.GetSection("MyProvider"));
        services.AddHttpClient<MyProviderHistoricalDataProvider>();
        services.AddSingleton<IHistoricalDataProvider, MyProviderHistoricalDataProvider>();
    }
}
```

### Step 12 — Write Tests

Every new provider needs at minimum these test cases:

**For historical providers:**
```
{ProviderName}HistoricalDataProviderTests.cs
├── GetDailyBarsAsync_ValidSymbol_ReturnsBars
├── GetDailyBarsAsync_RateLimited_WaitsBeforeRequest
├── GetDailyBarsAsync_HttpError_ThrowsDataProviderException
├── GetDailyBarsAsync_CancellationRequested_ThrowsOperationCanceledException
└── GetDailyBarsAsync_EmptyResponse_ReturnsEmptyList
```

**For streaming providers:**
```
{ProviderName}MarketDataClientTests.cs
├── ConnectAsync_Success_SetsIsEnabled
├── ConnectAsync_ConnectionFailed_RetriesWithBackoff
├── DisconnectedMidSession_AutoReconnects
├── SubscribeTrades_AfterConnect_ReceivesTicks
├── DisposeAsync_CancelsOutstandingOperations
└── ConnectAsync_CancellationRequested_ThrowsOperationCanceledException
```

See `references/provider-patterns.md` for complete test scaffolding examples.

---

## Compliance Checklist

Before submitting a provider implementation, verify all of these:

- [ ] `[DataSource("provider-name")]` attribute present on the class
- [ ] `[ImplementsAdr("ADR-001", ...)]` attribute present on the class
- [ ] `[ImplementsAdr("ADR-004", ...)]` attribute present on the class
- [ ] `IOptionsMonitor<T>` used (not `IOptions<T>`)
- [ ] `WaitForRateLimitSlotAsync(ct)` called before every HTTP request (historical)
- [ ] WebSocket reconnection handler implemented (streaming)
- [ ] All `async` methods accept `CancellationToken ct = default`
- [ ] `CancellationToken.None` never passed to async calls — always forward `ct`
- [ ] JSON deserialization uses `MarketDataJsonContext.Default.*` (not reflection)
- [ ] New DTOs registered in `MarketDataJsonContext` with `[JsonSerializable]`
- [ ] HTTP client registered via `IHttpClientFactory` (not `new HttpClient()`)
- [ ] `DisposeAsync` cancels outstanding operations and disposes resources
- [ ] Class is `sealed` (providers are not designed for inheritance)
- [ ] Private fields use `_` prefix
- [ ] All log calls use structured params (no string interpolation)
- [ ] Provider-specific exceptions derive from `DataProviderException`
- [ ] Provider credentials come from `IOptionsMonitor<T>` (never hardcoded)
- [ ] Provider registered via `IProviderModule.Register()`
- [ ] At least 5 tests covering: success path, rate limit, HTTP error, cancellation, empty response

---

## Common Mistakes (Known AI Error Patterns)

These mistakes are documented in `docs/ai/ai-known-errors.md`. Read that file before writing
a provider.

| Mistake | Symptom | Fix |
|---------|---------|-----|
| `WaitAsync()` call | Compile error — method doesn't exist | Use `WaitForSlotAsync(ct)` |
| `IOptions<T>` for credentials | Hot-reload broken, credentials stale | Switch to `IOptionsMonitor<T>` |
| Missing reconnection in streaming | Provider silently drops data after network blip | Add `OnDisconnectedAsync` handler |
| Direct `new HttpClient()` | Connections leak, DNS not refreshed | Use `IHttpClientFactory` |
| Missing `[ImplementsAdr]` | Code review CRITICAL finding | Add both ADR-001 and ADR-004 attributes |
| Reflection JSON | Startup overhead, AOT incompatible | Register types in `MarketDataJsonContext` |
| `CancellationToken.None` in `DisposeAsync` | Shutdown hang if server is slow | Pass a bounded-timeout token for dispose |

---

## Output Format

When building a provider, produce files in this order:

1. `{ProviderName}Options.cs` — configuration DTO
2. `{ProviderName}Models.cs` — provider response DTOs (only if needed)
3. `{ProviderName}HistoricalDataProvider.cs` or `{ProviderName}MarketDataClient.cs` — main implementation
4. `{ProviderName}ProviderModule.cs` — DI registration
5. `MarketDataJsonContext.cs` diff — add `[JsonSerializable]` entries
6. `appsettings.sample.json` diff — add configuration section
7. `{ProviderName}Tests.cs` — test scaffold

For each file, add a header comment listing which compliance checklist items it satisfies:

```csharp
// ✅ ADR-001: IHistoricalDataProvider contract
// ✅ ADR-004: CancellationToken on all async methods
// ✅ ADR-014: JsonSerializerContext source generation
// ✅ Rate limiting via WaitForRateLimitSlotAsync
```
