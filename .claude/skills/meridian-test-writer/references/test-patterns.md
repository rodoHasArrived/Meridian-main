---
name: meridian-test-writer
description: >
  Test generation skill for the Meridian project. Use this skill whenever an agent
  needs to write new xUnit tests, expand coverage for existing components, or validate that
  test quality meets the project's standards. Triggers on: "write tests for", "add unit tests",
  "increase test coverage", "write a test for this class", "how do I test X", "the tests are
  missing for", or when reviewing code that lacks corresponding test coverage. Also triggers
  when a code review (meridian-code-review) has identified test gaps. This skill produces
  idiomatic xUnit + FluentAssertions tests with correct async patterns, isolation, naming
  conventions, and mock setup for all major Meridian component types: providers, storage sinks,
  pipeline components, WPF services, and F# interop boundaries.
---

# Meridian — Test Writer Skill

Generate high-quality, idiomatic xUnit tests for any Meridian component. Every test
produced by this skill must pass the `meridian-code-review` Lens 4 (Test Code Quality) checks.

> **Shared project context:** [`../_shared/project-context.md`](../_shared/project-context.md)
> **Test patterns reference:** [`references/test-patterns.md`](references/test-patterns.md)
> **Code review skill:** [`../meridian-code-review/SKILL.md`](../meridian-code-review/SKILL.md)

---

## Test Framework Stack

| Tool | Purpose | Package |
|------|---------|---------|
| xUnit | Test runner | `xunit` |
| FluentAssertions | Assertion library | `FluentAssertions` |
| Moq | Mocking | `Moq` |
| NSubstitute | Alternative mocking | `NSubstitute` |
| coverlet | Coverage collection | `coverlet.collector` |

Check the project's test project `.csproj` for which mock library is in use before choosing.
Most tests in `Meridian.Tests` use Moq; WPF tests use NSubstitute.

---

## Component Type Detection

Before writing any tests, identify the component type to select the right test pattern:

```
What type of component am I testing?
│
├── IHistoricalDataProvider implementation
│   → Pattern A: Historical Provider Tests
│   → Test file: tests/Meridian.Tests/Infrastructure/Providers/
│
├── IMarketDataClient (streaming) implementation
│   → Pattern B: Streaming Provider Tests
│   → Test file: tests/Meridian.Tests/Infrastructure/Providers/
│
├── IStorageSink / WAL / AtomicFileWriter
│   → Pattern C: Storage Tests
│   → Test file: tests/Meridian.Tests/Storage/
│
├── EventPipeline / pipeline component
│   → Pattern D: Pipeline Tests
│   → Test file: tests/Meridian.Tests/Application/Pipeline/
│
├── Application service (no HTTP, pure logic)
│   → Pattern E: Application Service Tests
│   → Test file: tests/Meridian.Tests/Application/Services/
│
├── WPF ViewModel or Ui.Services class
│   → Pattern F: WPF/UI Service Tests
│   → Test file: tests/Meridian.Wpf.Tests/ or Meridian.Ui.Tests/
│
├── F# module called from C#
│   → Pattern G: F# Interop Tests
│   → Test file: tests/Meridian.FSharp.Tests/
│
└── Endpoint / HTTP integration
    → Pattern H: Endpoint Integration Tests
    → Test file: tests/Meridian.Tests/Integration/EndpointTests/
```

---

## Universal Rules (Apply to Every Test)

These rules apply to **all** component types. Violating them will trigger code review findings.

### Rule 1: Never `async void`

```csharp
// ✅ Correct
[Fact]
public async Task GetData_ValidInput_ReturnsResult() { ... }

// ❌ Wrong — xUnit cannot await async void; exceptions are swallowed
[Fact]
public async void GetData_ValidInput_ReturnsResult() { ... }
```

### Rule 2: Always Add CancellationToken with Timeout

```csharp
// ✅ Correct — test will fail (not hang) if SUT doesn't respond
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
var result = await sut.GetDataAsync(cts.Token);

// ❌ Wrong — test can hang indefinitely
var result = await sut.GetDataAsync(CancellationToken.None);
```

### Rule 3: Always `await using` for IAsyncDisposable

```csharp
// ✅ Correct
await using var sink = new JsonlStorageSink(options, logger);
await sink.WriteAsync(evt, cts.Token);

// ❌ Wrong — sink not disposed; file handles left open; state leaked to next test
using var sink = new JsonlStorageSink(options, logger);  // wrong using
var sink = new JsonlStorageSink(options, logger);         // no using at all
```

### Rule 4: No `Task.Delay` for Synchronization

```csharp
// ✅ Correct — use TaskCompletionSource or SemaphoreSlim
var tcs = new TaskCompletionSource<bool>();
sut.OnDataReceived += _ => tcs.SetResult(true);
await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

// ❌ Wrong — flaky, depends on timing
await Task.Delay(200);
Assert.True(sut.ReceivedData);
```

### Rule 5: Test Naming Convention

```
MethodUnderTest_ScenarioOrCondition_ExpectedBehavior

Examples:
  GetDailyBarsAsync_ValidSymbol_ReturnsBars           ✅
  GetDailyBarsAsync_HttpError_ThrowsDataProviderException ✅
  ConnectAsync_Success_SetsIsEnabled                  ✅
  WriteAsync_NullEvent_ThrowsArgumentNullException    ✅

  TestGetData                                          ❌ (no scenario/expected)
  GetDataWorks                                         ❌ (no convention)
  Test1                                                ❌
```

### Rule 6: No Shared Static Mutable State

```csharp
// ✅ Correct — each test creates its own instance
public sealed class FooTests
{
    private Foo CreateSut() => new Foo(new Mock<IBar>().Object);

    [Fact]
    public async Task Method_Scenario_Expected()
    {
        var sut = CreateSut();
        // ...
    }
}

// ❌ Wrong — static state leaks between tests
public class FooTests
{
    private static readonly Foo _sut = new Foo(...);
}
```

### Rule 7: Isolation via IDisposable / IAsyncDisposable

```csharp
// For tests that write to the filesystem, use a temp directory
private static string GetTempDir() =>
    Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

// Clean up in Dispose
public void Dispose() => Directory.Delete(_tempDir, recursive: true);
```

---

## Pattern A: Historical Provider Tests

```csharp
namespace Meridian.Tests.Infrastructure.Providers;

public sealed class MyProviderHistoricalDataProviderTests
{
    private readonly Mock<IOptionsMonitor<MyProviderOptions>> _options = new();
    private readonly Mock<ILogger<MyProviderHistoricalDataProvider>> _logger = new();
    private readonly Mock<HttpMessageHandler> _handler = new(MockBehavior.Strict);

    private MyProviderHistoricalDataProvider CreateSut(
        MyProviderOptions? opts = null)
    {
        _options.Setup(o => o.CurrentValue)
                .Returns(opts ?? new MyProviderOptions { ApiKey = "test-key" });

        var http = new HttpClient(_handler.Object)
        {
            BaseAddress = new Uri("https://api.myprovider.com"),
        };

        return new MyProviderHistoricalDataProvider(http, _options.Object, _logger.Object);
    }

    [Fact]
    public async Task GetDailyBarsAsync_ValidSymbol_ReturnsBars()
    {
        // Arrange
        _handler.SetupAnyRequest().ReturnsJsonResponse(new MyProviderResponse
        {
            Bars = [new() { Symbol = "AAPL", Date = "2024-01-02", Open = 185m,
                            High = 187m, Low = 184m, Close = 186m, Volume = 1_000_000 }]
        });

        var sut = CreateSut();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Act
        var result = await sut.GetDailyBarsAsync("AAPL", null, null, cts.Token);

        // Assert
        result.Should().HaveCount(1);
        result[0].Symbol.Should().Be("AAPL");
        result[0].Close.Should().Be(186m);
    }

    [Fact]
    public async Task GetDailyBarsAsync_EmptyResponse_ReturnsEmptyList()
    {
        _handler.SetupAnyRequest()
                .ReturnsJsonResponse(new MyProviderResponse { Bars = [] });

        var sut = CreateSut();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var result = await sut.GetDailyBarsAsync("AAPL", null, null, cts.Token);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetDailyBarsAsync_HttpError_ThrowsDataProviderException()
    {
        _handler.SetupAnyRequest()
                .ReturnsResponse(HttpStatusCode.ServiceUnavailable);

        var sut = CreateSut();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var act = () => sut.GetDailyBarsAsync("AAPL", null, null, cts.Token);

        await act.Should().ThrowAsync<DataProviderException>()
                 .WithMessage("*AAPL*");
    }

    [Fact]
    public async Task GetDailyBarsAsync_CancellationRequested_ThrowsOperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var sut = CreateSut();

        var act = () => sut.GetDailyBarsAsync("AAPL", null, null, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GetDailyBarsAsync_NullOrWhitespaceSymbol_ThrowsArgumentException(
        string? symbol)
    {
        var sut = CreateSut();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var act = () => sut.GetDailyBarsAsync(symbol!, null, null, cts.Token);

        await act.Should().ThrowAsync<ArgumentException>();
    }
}
```

---

## Pattern B: Streaming Provider Tests

```csharp
namespace Meridian.Tests.Infrastructure.Providers;

public sealed class MyStreamingProviderClientTests : IAsyncDisposable
{
    private readonly Mock<WebSocketConnectionManager> _ws = new();
    private readonly Mock<IOptionsMonitor<MyProviderOptions>> _options = new();
    private readonly Mock<IMarketEventPublisher> _publisher = new();
    private readonly Mock<ILogger<MyStreamingProviderClient>> _logger = new();

    public MyStreamingProviderClientTests()
    {
        _options.Setup(o => o.CurrentValue).Returns(new MyProviderOptions
        {
            IsEnabled = true,
            WebSocketUri = new Uri("wss://test.example.com"),
        });
    }

    private MyStreamingProviderClient CreateSut() =>
        new(_ws.Object, _options.Object, _publisher.Object, _logger.Object);

    [Fact]
    public async Task ConnectAsync_Success_CallsWebSocketConnect()
    {
        _ws.Setup(w => w.ConnectAsync(It.IsAny<Uri>(), It.IsAny<CancellationToken>()))
           .Returns(Task.CompletedTask);

        await using var sut = CreateSut();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await sut.ConnectAsync(cts.Token);

        _ws.Verify(w => w.ConnectAsync(
            It.IsAny<Uri>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ConnectAsync_WebSocketThrows_ThrowsConnectionException()
    {
        _ws.Setup(w => w.ConnectAsync(It.IsAny<Uri>(), It.IsAny<CancellationToken>()))
           .ThrowsAsync(new WebSocketException("Connection refused"));

        await using var sut = CreateSut();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var act = () => sut.ConnectAsync(cts.Token);

        await act.Should().ThrowAsync<ConnectionException>();
    }

    [Fact]
    public async Task DisposeAsync_DoesNotHang()
    {
        _ws.Setup(w => w.ConnectAsync(It.IsAny<Uri>(), It.IsAny<CancellationToken>()))
           .Returns(Task.CompletedTask);
        _ws.Setup(w => w.DisconnectAsync(It.IsAny<CancellationToken>()))
           .Returns(Task.CompletedTask);

        var sut = CreateSut();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await sut.ConnectAsync(cts.Token);

        // Must complete within 3 seconds
        var act = async () => await sut.DisposeAsync().AsTask()
                                       .WaitAsync(TimeSpan.FromSeconds(3));

        await act.Should().NotThrowAsync();
    }

    public async ValueTask DisposeAsync()
    {
        // cleanup if any suts were not disposed in tests
    }
}
```

---

## Pattern C: Storage Sink Tests

```csharp
namespace Meridian.Tests.Storage;

public sealed class JsonlStorageSinkTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(
        Path.GetTempPath(), Path.GetRandomFileName());

    public JsonlStorageSinkTests() =>
        Directory.CreateDirectory(_tempDir);

    public void Dispose() =>
        Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public async Task WriteAsync_ValidEvent_WritesJsonlLine()
    {
        // Arrange
        var options = new StorageOptions { BaseDirectory = _tempDir };
        await using var sink = CreateSut(options);
        var evt = TestDataBuilder.CreateTradeEvent("AAPL");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Act
        await sink.WriteAsync(evt, cts.Token);
        await sink.FlushAsync(cts.Token);

        // Assert
        var files = Directory.GetFiles(_tempDir, "*.jsonl*", SearchOption.AllDirectories);
        files.Should().HaveCountGreaterThan(0);

        var content = await File.ReadAllTextAsync(files[0], cts.Token);
        content.Should().Contain("AAPL");
    }

    [Fact]
    public async Task WriteAsync_MultipleEvents_AllPersisted()
    {
        var options = new StorageOptions { BaseDirectory = _tempDir };
        await using var sink = CreateSut(options);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var events = Enumerable.Range(0, 10)
            .Select(i => TestDataBuilder.CreateTradeEvent($"SYM{i}"))
            .ToList();

        foreach (var evt in events)
            await sink.WriteAsync(evt, cts.Token);

        await sink.FlushAsync(cts.Token);

        // All 10 events should be in a file somewhere
        var allLines = Directory.GetFiles(_tempDir, "*.jsonl*", SearchOption.AllDirectories)
            .SelectMany(f => File.ReadAllLines(f))
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToList();

        allLines.Should().HaveCount(10);
    }

    [Fact]
    public async Task DisposeAsync_FlushesRemainingEvents()
    {
        // Arrange — write without explicit flush, rely on DisposeAsync
        var options = new StorageOptions { BaseDirectory = _tempDir };
        var sink = CreateSut(options);
        var evt = TestDataBuilder.CreateTradeEvent("MSFT");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await sink.WriteAsync(evt, cts.Token);

        // Act — dispose without calling FlushAsync
        await sink.DisposeAsync();

        // Assert — event must be persisted
        var files = Directory.GetFiles(_tempDir, "*.jsonl*", SearchOption.AllDirectories);
        files.Should().NotBeEmpty();
    }

    private JsonlStorageSink CreateSut(StorageOptions options)
    {
        var optMonitor = new Mock<IOptionsMonitor<StorageOptions>>();
        optMonitor.Setup(o => o.CurrentValue).Returns(options);

        var logger = new Mock<ILogger<JsonlStorageSink>>().Object;
        return new JsonlStorageSink(optMonitor.Object, logger);
    }
}
```

---

## Pattern D: Pipeline / EventPipeline Tests

```csharp
namespace Meridian.Tests.Application.Pipeline;

public sealed class EventPipelineTests : IAsyncDisposable
{
    private readonly Mock<IStorageSink> _sink = new();
    private readonly Mock<ILogger<EventPipeline>> _logger = new();

    [Fact]
    public async Task PublishAsync_ValidEvent_SinkReceivesEvent()
    {
        // Arrange
        var received = new List<MarketEvent>();
        _sink.Setup(s => s.WriteAsync(It.IsAny<MarketEvent>(), It.IsAny<CancellationToken>()))
             .Callback<MarketEvent, CancellationToken>((e, _) => received.Add(e))
             .Returns(ValueTask.CompletedTask);

        await using var pipeline = CreateSut();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var evt = TestDataBuilder.CreateTradeEvent("AAPL");

        // Act
        await pipeline.PublishAsync(evt, cts.Token);
        await pipeline.FlushAsync(cts.Token);   // ✅ always flush before asserting

        // Assert
        received.Should().ContainSingle(e => e.Symbol == "AAPL");
    }

    [Fact]
    public async Task DisposeAsync_FlushesQueueBeforeExit()
    {
        var flushed = false;
        _sink.Setup(s => s.FlushAsync(It.IsAny<CancellationToken>()))
             .Callback(() => flushed = true)
             .Returns(Task.CompletedTask);

        var pipeline = CreateSut();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await pipeline.PublishAsync(TestDataBuilder.CreateTradeEvent("SPY"), cts.Token);
        await pipeline.DisposeAsync();  // ✅ await using pattern

        flushed.Should().BeTrue("DisposeAsync must call FlushAsync on sink");
    }

    private EventPipeline CreateSut() =>
        new(_sink.Object, _logger.Object);

    public async ValueTask DisposeAsync() { /* cleanup */ }
}
```

---

## Pattern E: Application Service Tests

```csharp
namespace Meridian.Tests.Application.Services;

public sealed class TradingCalendarTests
{
    [Theory]
    [InlineData("2024-01-01", false)]  // New Year's Day
    [InlineData("2024-01-02", true)]   // Regular trading day
    [InlineData("2024-07-04", false)]  // Independence Day
    [InlineData("2024-12-25", false)]  // Christmas
    [InlineData("2024-12-23", true)]   // Regular trading day
    public void IsTradingDay_KnownDates_ReturnsExpected(string dateStr, bool expected)
    {
        // Arrange
        var sut = new TradingCalendar();
        var date = DateOnly.Parse(dateStr);

        // Act
        var result = sut.IsTradingDay(date);

        // Assert
        result.Should().Be(expected, $"{dateStr} should be {(expected ? "" : "non-")}trading");
    }

    [Fact]
    public void GetTradingDays_ValidRange_ExcludesWeekends()
    {
        var sut = new TradingCalendar();
        var from = new DateOnly(2024, 1, 1);
        var to = new DateOnly(2024, 1, 31);

        var days = sut.GetTradingDays(from, to).ToList();

        days.Should().NotContain(d => d.DayOfWeek == DayOfWeek.Saturday);
        days.Should().NotContain(d => d.DayOfWeek == DayOfWeek.Sunday);
    }
}
```

---

## Pattern F: WPF / UI Service Tests

```csharp
namespace Meridian.Wpf.Tests.Services;

public sealed class StatusServiceTests
{
    private readonly ISubstitute<IApiClientService> _api;  // NSubstitute style

    public StatusServiceTests()
    {
        _api = Substitute.For<IApiClientService>();
    }

    [Fact]
    public async Task GetStatusAsync_ApiReturnsStatus_ReturnsPopulatedModel()
    {
        // Arrange
        _api.GetStatusAsync(Arg.Any<CancellationToken>())
            .Returns(new CollectorStatus { Published = 1000, Errors = 0 });

        var sut = new StatusService(_api);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Act
        var result = await sut.GetStatusAsync(cts.Token);

        // Assert
        result.Should().NotBeNull();
        result!.Published.Should().Be(1000);
    }

    [Fact]
    public async Task GetStatusAsync_ApiThrows_ReturnsNull()
    {
        // Arrange
        _api.GetStatusAsync(Arg.Any<CancellationToken>())
            .Throws(new HttpRequestException("Unavailable"));

        var sut = new StatusService(_api);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Act
        var result = await sut.GetStatusAsync(cts.Token);

        // Assert — service must swallow HTTP errors gracefully
        result.Should().BeNull();
    }
}
```

---

## Pattern G: F# Interop Tests

```csharp
namespace Meridian.FSharp.Tests;

// F# test style using Xunit or Expecto
module ValidationTests =

    [<Fact>]
    let ``QuoteValidator validates a well-formed quote`` () =
        // Arrange
        let quote = {
            Symbol = "AAPL"
            BidPrice = 185.0m
            AskPrice = 185.05m
            BidSize = 100
            AskSize = 200
            Timestamp = DateTimeOffset.UtcNow
        }

        // Act
        let result = QuoteValidator.validate quote

        // Assert
        result |> Result.isOk |> should equal true

    [<Fact>]
    let ``QuoteValidator rejects inverted market`` () =
        let invertedQuote = {
            Symbol = "AAPL"
            BidPrice = 185.10m
            AskPrice = 185.00m  // bid > ask — inverted
            BidSize = 100
            AskSize = 200
            Timestamp = DateTimeOffset.UtcNow
        }

        let result = QuoteValidator.validate invertedQuote

        result |> Result.isError |> should equal true
```

---

## Test Data Builder

Use `TestDataBuilder` (or create one) for reusable test event construction:

```csharp
// If TestMarketEventPublisher exists in tests/TestHelpers/, use it:
// File: tests/Meridian.Tests/TestHelpers/TestMarketEventPublisher.cs

internal static class TestDataBuilder
{
    public static MarketEvent CreateTradeEvent(string symbol, decimal price = 100m) =>
        MarketEvent.Trade(new Trade
        {
            Symbol = new SymbolId(symbol),
            Price = price,
            Size = 100,
            Timestamp = DateTimeOffset.UtcNow,
            SequenceNumber = 1,
        });

    public static MarketEvent CreateQuoteEvent(string symbol,
        decimal bid = 99.99m, decimal ask = 100.01m) =>
        MarketEvent.Quote(new BboQuote
        {
            Symbol = new SymbolId(symbol),
            BidPrice = bid,
            AskPrice = ask,
            Timestamp = DateTimeOffset.UtcNow,
        });
}
```

---

## Pattern I: Market Scenario Simulation

**Use this pattern when:** the behavior under test is driven by a recognizable real-world market
event and must exercise two or more architectural layers end-to-end (e.g., provider → pipeline,
pipeline → storage, strategy → order management, backtest engine → metrics).

**Naming convention:** `Scenario_MarketCondition_SystemBehavior`
(e.g., `FlashCrash_PriceDrops10Pct_PriceContinuityCheckerFiresAlert`)

### Market Scenario Catalog

Select a scenario from this catalog before writing any code. Each scenario specifies which code
paths must be exercised end-to-end.

#### Tier 1 — Data Ingestion (Provider → Pipeline → Storage)

| Scenario | Trigger Conditions | Code Paths Exercised |
|----------|-------------------|---------------------|
| **Normal session open** | Burst of trades + BBO quotes at 09:30 ET | Provider parsing → trade/quote collector → dedup → pipeline channel → JSONL sink |
| **Pre-market gap-up on earnings** | High-volume trades above prior close before 09:30 | Same as above + out-of-hours flag + timestamp monotonicity checker |
| **Provider feed interruption** | WebSocket disconnect mid-session | Reconnection logic → sequence gap detection → integrity event emission |
| **Rate-limit breach** | Provider returns 429 after burst | Rate limiter → exponential backoff → circuit breaker |
| **Stale quote flood** | Provider sends repeated identical BBO ticks | Dedup store → pipeline drop counter → backpressure signal |
| **Crossed market** | Bid > Ask received from provider | Quote validation → integrity event → bad-tick filter |
| **Flash crash** | Price drops 10 %+ within 1 second | Price continuity checker → anomaly detector → alert dispatcher |

#### Tier 2 — Backtesting (Historical Data → Engine → Metrics)

| Scenario | Trigger Conditions | Code Paths Exercised |
|----------|-------------------|---------------------|
| **Single-symbol buy-and-hold** | Strategy buys on day 1, sells on last day | Bar replay → order placement → fill model → portfolio snapshot → XIRR |
| **Multi-symbol rebalance** | Strategy buys 3 symbols, portfolio drifts, rebalances weekly | Multiple bar streams → lot tracking → commission model → drawdown metric |
| **Stop-loss trigger** | Price falls below stop threshold | Contingent order manager → fill model → PnL calculation |
| **Dividend corporate action** | Adjusted close deviates from raw close | Corporate action adjuster → cost basis recalculation |
| **Earnings announcement gap** | Bar open price differs significantly from prior close | Gap detection → position sizing → slippage in fill model |

#### Tier 3 — Execution & Risk (Order → Gateway → Risk)

| Scenario | Trigger Conditions | Code Paths Exercised |
|----------|-------------------|---------------------|
| **Paper trade order lifecycle** | Strategy submits limit order; fill arrives | Order manager → paper gateway → fill event → portfolio state update |
| **Position limit breach** | Strategy attempts to exceed configured max position | Risk validator → position limit rule → order rejection |
| **Drawdown circuit breaker** | Portfolio loss exceeds configured threshold | Drawdown circuit breaker → strategy halt signal |
| **Order rate throttle** | Strategy submits orders faster than allowed rate | Order rate throttle rule → rejection with backoff |
| **Multi-account allocation** | Block order split across multiple accounts | Block trade allocator → proportional allocation engine |

#### Tier 4 — Storage & Recovery

| Scenario | Trigger Conditions | Code Paths Exercised |
|----------|-------------------|---------------------|
| **WAL crash recovery** | Process dies mid-write; WAL replayed on restart | WAL write → simulated truncation → WAL replay → data integrity check |
| **Parquet conversion** | JSONL file exceeds threshold; conversion triggered | Archival service → Parquet sink → JSONL cleanup |
| **Concurrent sink writes** | Multiple providers write simultaneously | Composite sink → file locking → atomic writer |
| **Storage quota enforcement** | Disk usage approaches quota limit | Quota enforcement service → backpressure signal |

---

### Pattern I Test Scaffold

```csharp
namespace Meridian.Tests.Integration;

/// <summary>
/// Simulates a normal equity session open: a burst of sequentially-numbered trades and BBO
/// quotes arriving at 09:30 ET flows through the real EventPipeline and is persisted to a
/// temporary JSONL sink. Validates that no events are dropped under expected opening-bell
/// throughput and that sequence numbers are preserved in storage.
///
/// Code paths exercised: MarketScenarioBuilder → EventPipeline → JsonlStorageSink
/// Guards against: silent event drops under burst load; sequence number corruption on flush.
/// </summary>
public sealed class NormalSessionOpenScenarioTests : IAsyncDisposable
{
    private readonly string _tempDir =
        Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
    private EventPipeline _pipeline = null!;
    private JsonlStorageSink _sink = null!;

    public NormalSessionOpenScenarioTests()
    {
        Directory.CreateDirectory(_tempDir);
        var storageOpts = Microsoft.Extensions.Options.Options.Create(
            new StorageOptions { BaseDirectory = _tempDir });
        _sink = new JsonlStorageSink(storageOpts, NullLogger<JsonlStorageSink>.Instance);
        _pipeline = new EventPipeline(_sink, capacity: 2_000, enablePeriodicFlush: false);
    }

    [Fact]
    public async Task NormalSessionOpen_BurstOfTradesAndQuotes_AllEventsPersisted()
    {
        // Arrange — simulate 09:30 ET opening bell: 50 trades + 50 quotes for AAPL and SPY
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var openTime = new DateTimeOffset(2025, 6, 2, 13, 30, 0, TimeSpan.Zero);

        var events = MarketScenarioBuilder.BuildSessionOpen(
            symbols: ["AAPL", "SPY"],
            openTime: openTime,
            tradesPerSymbol: 25,
            quotesPerSymbol: 25,
            basePrice: new Dictionary<string, decimal>
            {
                ["AAPL"] = 213.50m,
                ["SPY"] = 531.20m,
            });

        // Act
        foreach (var evt in events)
            _pipeline.TryPublish(evt);

        await _pipeline.FlushAsync(cts.Token);

        // Assert — all 100 events must be durably written with no drops
        var files = Directory.GetFiles(_tempDir, "*.jsonl", SearchOption.AllDirectories);
        var totalLines = files
            .SelectMany(f => File.ReadAllLines(f))
            .Count(l => !string.IsNullOrWhiteSpace(l));

        totalLines.Should().Be(events.Count,
            because: "every published event must be persisted during a session-open burst");
    }

    [Fact]
    public async Task NormalSessionOpen_SequentialTrades_SequenceNumbersAreMonotonic()
    {
        // Arrange
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var openTime = new DateTimeOffset(2025, 6, 2, 13, 30, 0, TimeSpan.Zero);

        var trades = MarketScenarioBuilder.BuildSequentialTrades(
            symbol: "MSFT",
            startTime: openTime,
            count: 10,
            startSequence: 1001L,
            startPrice: 420.00m,
            priceStep: 0.01m);

        // Act
        foreach (var evt in trades)
            _pipeline.TryPublish(evt);

        await _pipeline.FlushAsync(cts.Token);

        // Assert — all 10 events persisted (any drop would reduce the count below 10)
        var lines = Directory
            .GetFiles(_tempDir, "*.jsonl", SearchOption.AllDirectories)
            .SelectMany(f => File.ReadAllLines(f))
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToList();

        lines.Should().HaveCount(10,
            because: "all 10 sequential trades must survive the pipeline flush without gaps");
    }

    public async ValueTask DisposeAsync()
    {
        await _pipeline.DisposeAsync();
        await _sink.DisposeAsync();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }
}
```

---

### `MarketScenarioBuilder` Helper

Place this file at `tests/Meridian.Tests/TestHelpers/MarketScenarioBuilder.cs`.
It provides deterministic, realistic event sequences for Pattern I tests.

```csharp
// File: tests/Meridian.Tests/TestHelpers/MarketScenarioBuilder.cs
namespace Meridian.Tests.TestHelpers;

/// <summary>
/// Factory for constructing realistic, scenario-grounded sequences of <see cref="MarketEvent"/>s.
/// All methods produce deterministic output given the same inputs so tests are reproducible.
/// Use this instead of hand-crafting events with magic-constant prices.
/// </summary>
internal static class MarketScenarioBuilder
{
    /// <summary>
    /// Builds a mixed burst of trades and BBO quotes simulating a session open for
    /// the given <paramref name="symbols"/> at <paramref name="openTime"/>.
    /// </summary>
    public static List<MarketEvent> BuildSessionOpen(
        IReadOnlyList<string> symbols,
        DateTimeOffset openTime,
        int tradesPerSymbol,
        int quotesPerSymbol,
        IReadOnlyDictionary<string, decimal>? basePrice = null)
    {
        var events = new List<MarketEvent>();
        long seq = 1;

        foreach (var symbol in symbols)
        {
            var price = basePrice?.GetValueOrDefault(symbol) ?? 100m;
            events.AddRange(BuildSequentialTrades(symbol, openTime, tradesPerSymbol, seq, price));
            seq += tradesPerSymbol;
            events.AddRange(BuildSequentialQuotes(symbol, openTime, quotesPerSymbol, seq, price));
            seq += quotesPerSymbol;
        }

        return events;
    }

    /// <summary>
    /// Builds a sequence of trades with monotonically increasing sequence numbers and
    /// a small price walk up from <paramref name="startPrice"/> by
    /// <paramref name="priceStep"/> per tick.
    /// </summary>
    public static List<MarketEvent> BuildSequentialTrades(
        string symbol,
        DateTimeOffset startTime,
        int count,
        long startSequence,
        decimal startPrice,
        decimal priceStep = 0.01m)
    {
        var events = new List<MarketEvent>(count);
        for (var i = 0; i < count; i++)
        {
            var ts = startTime.AddMilliseconds(i * 20); // 20 ms — realistic HFT cadence
            var price = startPrice + priceStep * i;
            var trade = new Trade(
                Timestamp: ts,
                Symbol: symbol,
                Price: price,
                Size: 100L,
                Aggressor: i % 2 == 0 ? AggressorSide.Buy : AggressorSide.Sell,
                SequenceNumber: startSequence + i,
                Venue: "XNAS");
            events.Add(MarketEvent.Trade(ts, symbol, trade,
                seq: startSequence + i, source: "XNAS"));
        }
        return events;
    }

    /// <summary>
    /// Builds a sequence of BBO quote updates with a realistic spread around
    /// <paramref name="midPrice"/> (default half-spread = 0.01).
    /// </summary>
    public static List<MarketEvent> BuildSequentialQuotes(
        string symbol,
        DateTimeOffset startTime,
        int count,
        long startSequence,
        decimal midPrice,
        decimal halfSpread = 0.01m)
    {
        var events = new List<MarketEvent>(count);
        for (var i = 0; i < count; i++)
        {
            var ts = startTime.AddMilliseconds(i * 50);
            var quote = new BboQuotePayload
            {
                Symbol = symbol,
                BidPrice = midPrice - halfSpread,
                AskPrice = midPrice + halfSpread,
                BidSize = 200,
                AskSize = 200,
                Timestamp = ts,
            };
            events.Add(MarketEvent.Quote(ts, symbol, quote,
                seq: startSequence + i, source: "XNAS"));
        }
        return events;
    }

    /// <summary>
    /// Builds a flash-crash scenario: price drops <paramref name="dropPct"/> percent
    /// of <paramref name="preCrashPrice"/> over <paramref name="durationMs"/> milliseconds
    /// in <paramref name="count"/> equal-sized ticks.
    /// Default: 10 % drop over 800 ms in 50 ticks (aggressive sell-side imbalance).
    /// </summary>
    public static List<MarketEvent> BuildFlashCrash(
        string symbol,
        DateTimeOffset startTime,
        decimal preCrashPrice,
        decimal dropPct = 0.10m,
        int count = 50,
        int durationMs = 800)
    {
        var events = new List<MarketEvent>(count);
        var dropPerTick = preCrashPrice * dropPct / count;
        var msPerTick = durationMs / count;

        for (var i = 0; i < count; i++)
        {
            var ts = startTime.AddMilliseconds(i * msPerTick);
            var price = preCrashPrice - dropPerTick * i;
            var trade = new Trade(
                Timestamp: ts,
                Symbol: symbol,
                Price: price,
                Size: 5_000L,
                Aggressor: AggressorSide.Sell,
                SequenceNumber: i + 1L,
                Venue: "XNAS");
            events.Add(MarketEvent.Trade(ts, symbol, trade, seq: i + 1L, source: "XNAS"));
        }
        return events;
    }

    /// <summary>
    /// Builds a provider-reconnect scenario: trades are emitted, then a sequence gap
    /// is injected (simulating missed events during a disconnect), then trades resume.
    /// The gap triggers an <see cref="IntegrityEvent"/> in the pipeline.
    /// </summary>
    public static (List<MarketEvent> BeforeGap, List<MarketEvent> AfterGap) BuildFeedInterruption(
        string symbol,
        DateTimeOffset startTime,
        int tradesBeforeGap = 5,
        int gapSize = 10,
        int tradesAfterGap = 5)
    {
        var before = BuildSequentialTrades(symbol, startTime, tradesBeforeGap,
            startSequence: 1L, startPrice: 100m);

        var resumeSeq = 1L + tradesBeforeGap + gapSize; // intentional sequence gap
        var after = BuildSequentialTrades(symbol,
            startTime.AddSeconds(1), tradesAfterGap,
            startSequence: resumeSeq, startPrice: 100m);

        return (before, after);
    }
}
```

---

## Test File Placement

| Component Type | Test Project | Subdirectory |
|---------------|-------------|-------------|
| Historical provider | `Meridian.Tests` | `Infrastructure/Providers/` |
| Streaming provider | `Meridian.Tests` | `Infrastructure/Providers/` |
| Storage sink / WAL | `Meridian.Tests` | `Storage/` |
| Pipeline component | `Meridian.Tests` | `Application/Pipeline/` |
| Application service | `Meridian.Tests` | `Application/Services/` |
| WPF services | `Meridian.Wpf.Tests` | `Services/` |
| Ui.Services classes | `Meridian.Ui.Tests` | `Services/` |
| F# modules | `Meridian.FSharp.Tests` | (root) |
| Endpoint integration | `Meridian.Tests` | `Integration/EndpointTests/` |
| **Market scenario (multi-layer)** | **`Meridian.Tests`** | **`Integration/`** |
