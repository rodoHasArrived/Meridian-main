# meridian-provider-builder — Provider Patterns Reference

This file contains complete, copy-ready implementation patterns for every major
provider concern. All patterns are verified against the live codebase.

> **Authoritative interfaces:** [`../../_shared/project-context.md`](../../_shared/project-context.md)
> **Rate limiter source:** `src/Meridian.Infrastructure/Adapters/Core/RateLimiting/RateLimiter.cs`
> **Resilience source:** `src/Meridian.Infrastructure/Resilience/`

---

## Pattern 1: Historical Provider Full Skeleton

```csharp
using Meridian.Contracts.Domain;
using Meridian.Core.Exceptions;
using Meridian.Core.Serialization;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.ProviderSdk;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Http.Json;
using System.Text.Json;

// ✅ ADR-001: IHistoricalDataProvider contract
// ✅ ADR-004: CancellationToken on all async methods
// ✅ ADR-005: DataSource attribute for discovery
// ✅ ADR-014: JsonSerializerContext source generation
[DataSource("my-provider")]
[ImplementsAdr("ADR-001", "Core historical data provider contract")]
[ImplementsAdr("ADR-004", "All async methods support CancellationToken")]
public sealed class MyProviderHistoricalDataProvider : BaseHistoricalDataProvider
{
    private readonly HttpClient _http;
    private readonly IOptionsMonitor<MyProviderOptions> _options;
    private readonly ILogger<MyProviderHistoricalDataProvider> _logger;

    public override string Name => "my-provider";
    public override string DisplayName => "My Provider";
    public override string Description => "Historical daily bars from My Provider.";
    public override int Priority => 50;

    public override HistoricalDataCapabilities Capabilities => new()
    {
        SupportsDailyBars = true,
        SupportsIntradayBars = false,
        EarliestDate = new DateOnly(2000, 1, 1),
    };

    public MyProviderHistoricalDataProvider(
        HttpClient http,
        IOptionsMonitor<MyProviderOptions> options,
        ILogger<MyProviderHistoricalDataProvider> logger)
        : base(logger, requestsPerMinute: 60)   // set your rate limit here
    {
        _http = http;
        _options = options;
        _logger = logger;
    }

    public override async Task<IReadOnlyList<HistoricalBar>> GetDailyBarsAsync(
        string symbol, DateOnly? from, DateOnly? to, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);

        // ✅ Rate limiting — MUST be first line before HTTP call
        await WaitForRateLimitSlotAsync(ct);

        var opts = _options.CurrentValue;
        var url = BuildUrl(symbol, from, to, opts.ApiKey);

        _logger.LogDebug("Fetching {Symbol} from {Url}", symbol, url);

        try
        {
            // ✅ Source-generated deserialization (ADR-014)
            var response = await _http.GetFromJsonAsync(
                url,
                MarketDataJsonContext.Default.MyProviderResponse,
                ct);

            if (response is null)
            {
                _logger.LogWarning("Empty response for {Symbol}", symbol);
                return [];
            }

            return MapToBars(response);
        }
        catch (HttpRequestException ex)
        {
            // ✅ Domain exception hierarchy
            throw new DataProviderException(
                $"HTTP error fetching {symbol} from {Name}", ex);
        }
        catch (OperationCanceledException)
        {
            // ✅ Always re-throw cancellation
            throw;
        }
    }

    private static string BuildUrl(string symbol, DateOnly? from, DateOnly? to, string apiKey)
    {
        // build provider-specific URL
        return $"https://api.myprovider.com/v1/bars?symbol={symbol}&apikey={apiKey}";
    }

    private static IReadOnlyList<HistoricalBar> MapToBars(MyProviderResponse response)
    {
        return response.Bars.Select(b => new HistoricalBar
        {
            Symbol = b.Symbol,
            Date = DateOnly.Parse(b.Date),
            Open = b.Open,
            High = b.High,
            Low = b.Low,
            Close = b.Close,
            Volume = b.Volume,
        }).ToList();
    }
}
```

---

## Pattern 2: Streaming Provider Full Skeleton

```csharp
using Meridian.Core.Exceptions;
using Meridian.Core.Serialization;
using Meridian.Infrastructure.Resilience;
using Meridian.Infrastructure.Shared;
using Meridian.ProviderSdk;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

// ✅ ADR-001: IMarketDataClient contract
// ✅ ADR-004: CancellationToken on all async methods
[DataSource("my-streaming-provider")]
[ImplementsAdr("ADR-001", "Core streaming data provider contract")]
[ImplementsAdr("ADR-004", "All async methods support CancellationToken")]
public sealed class MyStreamingProviderClient : IMarketDataClient
{
    private readonly WebSocketConnectionManager _wsManager;
    private readonly IOptionsMonitor<MyProviderOptions> _options;
    private readonly ILogger<MyStreamingProviderClient> _logger;
    private readonly IMarketEventPublisher _publisher;
    private CancellationTokenSource? _cts;

    public bool IsEnabled => _options.CurrentValue.IsEnabled;

    public MyStreamingProviderClient(
        WebSocketConnectionManager wsManager,
        IOptionsMonitor<MyProviderOptions> options,
        IMarketEventPublisher publisher,
        ILogger<MyStreamingProviderClient> logger)
    {
        _wsManager = wsManager;
        _options = options;
        _publisher = publisher;
        _logger = logger;
    }

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        try
        {
            await _wsManager.ConnectAsync(_options.CurrentValue.WebSocketUri, ct);
            _logger.LogInformation("Connected to {Provider}", Name);
            _ = Task.Run(() => ReceiveLoopAsync(_cts.Token), _cts.Token);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new ConnectionException($"Failed to connect to {Name}", ex);
        }
    }

    public async Task DisconnectAsync(CancellationToken ct = default)
    {
        _cts?.Cancel();
        await _wsManager.DisconnectAsync(ct);
        _logger.LogInformation("Disconnected from {Provider}", Name);
    }

    public int SubscribeTrades(SymbolConfig cfg)
    {
        // Send subscription message; return subscription ID
        _wsManager.SendAsync(BuildSubscribeMessage(cfg.Symbol));
        return cfg.GetHashCode(); // use stable ID in real impl
    }

    public void UnsubscribeTrades(int subscriptionId)
    {
        // Send unsubscribe message
    }

    public int SubscribeMarketDepth(SymbolConfig cfg) => 0; // not supported
    public void UnsubscribeMarketDepth(int subscriptionId) { }

    // ✅ Reconnection — REQUIRED for all streaming providers
    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var message = await _wsManager.ReceiveAsync(ct);
                await HandleMessageAsync(message, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "WebSocket error in {Provider}, reconnecting...", Name);

                // ✅ Reconnect on any unexpected error
                await ReconnectWithBackoffAsync(ct);
            }
        }
    }

    private async Task ReconnectWithBackoffAsync(CancellationToken ct)
    {
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), ct);
                await _wsManager.ConnectAsync(_options.CurrentValue.WebSocketUri, ct);
                _logger.LogInformation("Reconnected to {Provider} on attempt {Attempt}", Name, attempt);
                return;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Reconnect attempt {Attempt} failed for {Provider}", attempt, Name);
            }
        }

        _logger.LogError("Failed to reconnect to {Provider} after 5 attempts", Name);
    }

    private Task HandleMessageAsync(string message, CancellationToken ct)
    {
        // ✅ Source-generated deserialization (ADR-014)
        var tick = JsonSerializer.Deserialize(message,
            MarketDataJsonContext.Default.MyProviderTickMessage);

        if (tick is not null)
        {
            _publisher.Publish(MapToMarketEvent(tick));
        }

        return Task.CompletedTask;
    }

    // ✅ IAsyncDisposable — REQUIRED
    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync(CancellationToken.None);
        _cts?.Cancel();
        _cts?.Dispose();
        GC.SuppressFinalize(this);
    }
}
```

---

## Pattern 3: Provider Options Class

```csharp
public sealed class MyProviderOptions
{
    public bool IsEnabled { get; set; } = false;
    public string ApiKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://api.myprovider.com/v1";
    public Uri WebSocketUri { get; set; } = new("wss://stream.myprovider.com");
    public int RateLimitPerMinute { get; set; } = 60;
}
```

---

## Pattern 4: DI Registration Module

```csharp
public sealed class MyProviderModule : IProviderModule
{
    public void Register(IServiceCollection services, IConfiguration config)
    {
        services.Configure<MyProviderOptions>(config.GetSection("MyProvider"));

        // ✅ IHttpClientFactory — never new HttpClient()
        services.AddHttpClient<MyProviderHistoricalDataProvider>((sp, client) =>
        {
            var opts = sp.GetRequiredService<IOptionsMonitor<MyProviderOptions>>().CurrentValue;
            client.BaseAddress = new Uri(opts.BaseUrl);
        });

        services.AddSingleton<IHistoricalDataProvider, MyProviderHistoricalDataProvider>();
    }
}
```

---

## Pattern 5: JsonSerializerContext Registration

Add to `src/Meridian.Core/Serialization/MarketDataJsonContext.cs`:

```csharp
[JsonSerializable(typeof(MyProviderResponse))]
[JsonSerializable(typeof(MyProviderTickMessage))]
[JsonSerializable(typeof(List<MyProviderBar>))]
// ... add alongside existing [JsonSerializable] attributes
public partial class MarketDataJsonContext : JsonSerializerContext { }
```

---

## Pattern 6: Historical Provider Test Scaffold

```csharp
// File: tests/Meridian.Tests/Infrastructure/Providers/
//       MyProviderHistoricalDataProviderTests.cs

public sealed class MyProviderHistoricalDataProviderTests
{
    private readonly Mock<HttpMessageHandler> _handler = new();
    private readonly Mock<IOptionsMonitor<MyProviderOptions>> _options = new();
    private readonly Mock<ILogger<MyProviderHistoricalDataProvider>> _logger = new();

    private MyProviderHistoricalDataProvider CreateSut()
    {
        _options.Setup(o => o.CurrentValue).Returns(new MyProviderOptions
        {
            IsEnabled = true,
            ApiKey = "test-key",
        });

        var client = new HttpClient(_handler.Object)
        {
            BaseAddress = new Uri("https://api.myprovider.com"),
        };

        return new MyProviderHistoricalDataProvider(client, _options.Object, _logger.Object);
    }

    [Fact]
    public async Task GetDailyBarsAsync_ValidSymbol_ReturnsBars()
    {
        // Arrange
        _handler.SetupRequest(HttpMethod.Get, "*")
            .ReturnsJsonResponse(new MyProviderResponse { /* ... */ });

        var sut = CreateSut();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Act
        var result = await sut.GetDailyBarsAsync("AAPL", null, null, cts.Token);

        // Assert
        result.Should().NotBeEmpty();
        result[0].Symbol.Should().Be("AAPL");
    }

    [Fact]
    public async Task GetDailyBarsAsync_HttpError_ThrowsDataProviderException()
    {
        // Arrange
        _handler.SetupRequest(HttpMethod.Get, "*").ReturnsResponse(HttpStatusCode.ServiceUnavailable);
        var sut = CreateSut();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Act
        var act = () => sut.GetDailyBarsAsync("AAPL", null, null, cts.Token);

        // Assert
        await act.Should().ThrowAsync<DataProviderException>();
    }

    [Fact]
    public async Task GetDailyBarsAsync_CancellationRequested_ThrowsOperationCanceledException()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var sut = CreateSut();

        // Act
        var act = () => sut.GetDailyBarsAsync("AAPL", null, null, cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task GetDailyBarsAsync_EmptyResponse_ReturnsEmptyList()
    {
        // Arrange
        _handler.SetupRequest(HttpMethod.Get, "*")
            .ReturnsJsonResponse(new MyProviderResponse { Bars = [] });
        var sut = CreateSut();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Act
        var result = await sut.GetDailyBarsAsync("AAPL", null, null, cts.Token);

        // Assert
        result.Should().BeEmpty();
    }
}
```

---

## Pattern 7: Streaming Provider Test Scaffold

```csharp
public sealed class MyStreamingProviderClientTests
{
    [Fact]
    public async Task ConnectAsync_Success_SetsIsEnabled()
    {
        // Arrange
        var wsManager = new Mock<WebSocketConnectionManager>();
        wsManager.Setup(w => w.ConnectAsync(It.IsAny<Uri>(), It.IsAny<CancellationToken>()))
                 .Returns(Task.CompletedTask);

        var sut = CreateSut(wsManager.Object);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Act
        await sut.ConnectAsync(cts.Token);

        // Assert — after connect, provider should be active
        wsManager.Verify(w => w.ConnectAsync(It.IsAny<Uri>(), It.IsAny<CancellationToken>()),
                         Times.Once);
    }

    [Fact]
    public async Task ConnectAsync_ConnectionFailed_ThrowsConnectionException()
    {
        // Arrange
        var wsManager = new Mock<WebSocketConnectionManager>();
        wsManager.Setup(w => w.ConnectAsync(It.IsAny<Uri>(), It.IsAny<CancellationToken>()))
                 .ThrowsAsync(new WebSocketException("refused"));

        var sut = CreateSut(wsManager.Object);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Act + Assert
        await sut.Invoking(s => s.ConnectAsync(cts.Token))
                 .Should().ThrowAsync<ConnectionException>();
    }

    [Fact]
    public async Task DisposeAsync_CancelsReceiveLoop()
    {
        // Arrange
        var sut = CreateSut();

        // Act + Assert — DisposeAsync must not hang
        var act = async () => await sut.DisposeAsync().AsTask()
            .WaitAsync(TimeSpan.FromSeconds(3));

        await act.Should().NotThrowAsync();
    }
}
```

---

## appsettings.sample.json Section Template

```json
"MyProvider": {
  "IsEnabled": false,
  "ApiKey": "",
  "BaseUrl": "https://api.myprovider.com/v1",
  "WebSocketUri": "wss://stream.myprovider.com",
  "RateLimitPerMinute": 60
}
```

---

## Rate Limiter Quick Reference

| Method | Class | Notes |
|--------|-------|-------|
| `WaitForRateLimitSlotAsync(ct)` | `BaseHistoricalDataProvider` | Use this in `GetDailyBarsAsync` |
| `WaitForSlotAsync(ct)` | `RateLimiter` | Direct use in non-base-class scenarios |
| ~~`WaitAsync(ct)`~~ | ~~`RateLimiter`~~ | **Does not exist** — common AI error |

File: `src/Meridian.Infrastructure/Adapters/Core/RateLimiting/RateLimiter.cs`

---

## ADR Compliance Quick Reference

| ADR | What to do |
|-----|-----------|
| ADR-001 | Add `[ImplementsAdr("ADR-001", "...")]` to provider class |
| ADR-004 | Every async method has `CancellationToken ct = default` |
| ADR-005 | Add `[DataSource("provider-name")]` to provider class |
| ADR-010 | Use `IHttpClientFactory`, not `new HttpClient()` |
| ADR-014 | Register all DTOs in `MarketDataJsonContext`; use `Default.*` overloads |
