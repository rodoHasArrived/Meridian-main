# CLAUDE.testing.md - Testing Guide

This document provides guidance for AI assistants working with tests in Meridian.

---

## Test Framework Stack

| Package | Version | Purpose |
|---------|---------|---------|
| xUnit | 2.6.6 | Test framework |
| FluentAssertions | 6.12.0 | Fluent assertion library |
| Moq | 4.20.70 | Mocking framework |
| NSubstitute | 5.1.0 | Alternative mocking framework |
| MassTransit.TestFramework | 8.2.5 | Message bus testing |
| coverlet | 6.0.0 | Code coverage |
| BenchmarkDotNet | (benchmarks) | Performance testing |

---

## Test Project Locations

| Project | Location | Files | Purpose |
|---------|----------|-------|---------|
| C# Unit Tests | `tests/Meridian.Tests/` | 134 | Main test suite |
| F# Tests | `tests/Meridian.FSharp.Tests/` | 4 | F# domain tests |
| UI Service Tests | `tests/Meridian.Ui.Tests/` | 15 | Desktop UI service tests |
| WPF Tests | `tests/Meridian.Wpf.Tests/` | 5 | WPF desktop service tests |
| Benchmarks | `benchmarks/Meridian.Benchmarks/` | - | Performance benchmarks |

**Total: 158 test files (154 C# + 4 F#)**

---

## Running Tests

### Basic Commands

```bash
# Run all tests
dotnet test

# Run specific test project
dotnet test tests/Meridian.Tests

# Run F# tests
dotnet test tests/Meridian.FSharp.Tests

# Run WPF desktop service tests (Windows only)
dotnet test tests/Meridian.Wpf.Tests

# Run desktop UI service tests (Windows only)
dotnet test tests/Meridian.Ui.Tests

# Run all desktop tests
make test-desktop-services

# Run with verbose output
dotnet test --logger "console;verbosity=detailed"

# Run specific test class
dotnet test --filter "FullyQualifiedName~TradeDataCollectorTests"

# Run specific test method
dotnet test --filter "TradeDataCollectorTests.OnTrade_ValidTrade_EmitsEvent"
```

### Code Coverage

```bash
# Run with coverage collection
dotnet test --collect:"XPlat Code Coverage"

# Generate HTML report (requires reportgenerator tool)
dotnet tool install -g dotnet-reportgenerator-globaltool
reportgenerator -reports:**/coverage.cobertura.xml -targetdir:coverage-report

# View coverage report
open coverage-report/index.html
```

### Using Makefile

```bash
# Run all tests
make test

# Run with coverage
make test-coverage
```

---

## Test Organization

### Test Directory Structure

```
tests/Meridian.Tests/           # 134 C# test files
├── Application/                           # 52 files
│   ├── Backfill/                          # 8 files
│   │   ├── AdditionalProviderContractTests.cs
│   │   ├── BackfillStatusStoreTests.cs
│   │   ├── BackfillWorkerServiceTests.cs
│   │   ├── CompositeHistoricalDataProviderTests.cs
│   │   ├── HistoricalProviderContractTests.cs
│   │   ├── PriorityBackfillQueueTests.cs
│   │   ├── RateLimiterTests.cs
│   │   └── ScheduledBackfillTests.cs
│   ├── Commands/                          # 8 files
│   ├── Config/                            # 3 files
│   ├── Credentials/                       # 3 files
│   ├── Indicators/                        # 1 file
│   ├── Monitoring/                        # 13 files
│   ├── Pipeline/                          # 7 files
│   └── Services/                          # 12 files
├── Domain/                                # 16 files
│   ├── Collectors/                        # 4 files
│   └── Models/                            # 12 files
├── Infrastructure/                        # 16 files
│   ├── DataSources/                       # 1 file
│   ├── Providers/                         # 12 files
│   ├── Resilience/                        # 2 files
│   └── Shared/                            # 1 file
├── Integration/                           # 21 files
│   ├── EndpointTests/                     # 17 files
│   └── (root integration tests)           # 4 files
├── ProviderSdk/                           # 4 files
├── Serialization/                         # 1 file
├── Storage/                               # 19 files
├── SymbolSearch/                          # 2 files
└── TestHelpers/                           # 1 file

tests/Meridian.FSharp.Tests/    # 4 F# test files
├── CalculationTests.fs
├── ValidationTests.fs
├── DomainTests.fs
└── PipelineTests.fs

tests/Meridian.Ui.Tests/        # 15 C# test files
├── Services/                              # 13 files
│   ├── ApiClientServiceTests.cs
│   ├── BackfillProviderConfigServiceTests.cs
│   ├── BackfillServiceTests.cs
│   ├── ChartingServiceTests.cs
│   ├── FixtureDataServiceTests.cs
│   ├── FormValidationServiceTests.cs
│   ├── LeanIntegrationServiceTests.cs
│   ├── OrderBookVisualizationServiceTests.cs
│   ├── PortfolioImportServiceTests.cs
│   ├── SchemaServiceTests.cs
│   ├── SystemHealthServiceTests.cs
│   ├── TimeSeriesAlignmentServiceTests.cs
│   └── WatchlistServiceTests.cs
└── Collections/                           # 2 files
    ├── BoundedObservableCollectionTests.cs
    └── CircularBufferTests.cs

tests/Meridian.Wpf.Tests/       # 5 C# test files
└── Services/                              # 5 files
    ├── ConfigServiceTests.cs
    ├── ConnectionServiceTests.cs
    ├── NavigationServiceTests.cs
    ├── StatusServiceTests.cs
    └── WpfDataQualityServiceTests.cs
```

### Test Class Naming

- Use `{ClassUnderTest}Tests` naming convention
- Group related tests in nested classes when appropriate

```csharp
public class TradeDataCollectorTests
{
    public class OnTrade
    {
        [Fact]
        public void WithValidTrade_EmitsTradeEvent() { }

        [Fact]
        public void WithSequenceGap_EmitsIntegrityEvent() { }
    }

    public class Reset
    {
        [Fact]
        public void ClearsAccumulatedStatistics() { }
    }
}
```

---

## Test Patterns

### Arrange-Act-Assert (AAA)

```csharp
[Fact]
public async Task GetHistoricalBarsAsync_ReturnsData()
{
    // Arrange
    var provider = CreateProvider();
    var symbol = "AAPL";
    var start = DateTime.Today.AddDays(-7);
    var end = DateTime.Today;

    // Act
    var bars = await provider.GetHistoricalBarsAsync(
        symbol, start, end, BarTimeframe.Daily);

    // Assert
    bars.Should().NotBeEmpty();
    bars.Should().AllSatisfy(b =>
    {
        b.Symbol.Should().Be(symbol);
        b.Timestamp.Should().BeOnOrAfter(start);
        b.Timestamp.Should().BeOnOrBefore(end);
    });
}
```

### FluentAssertions Examples

```csharp
// Basic assertions
result.Should().NotBeNull();
result.Should().Be(expected);
result.Should().BeEquivalentTo(expected);

// Collection assertions
items.Should().HaveCount(5);
items.Should().Contain(x => x.Symbol == "AAPL");
items.Should().BeInAscendingOrder(x => x.Timestamp);
items.Should().OnlyContain(x => x.Price > 0);

// Numeric assertions
price.Should().BePositive();
price.Should().BeApproximately(100.0m, 0.01m);
count.Should().BeInRange(1, 100);

// Exception assertions
action.Should().Throw<ArgumentException>()
    .WithMessage("*invalid*");

await asyncAction.Should().ThrowAsync<InvalidOperationException>();

// Object graph assertions
order.Should().BeEquivalentTo(expected, options => options
    .Excluding(x => x.Id)
    .Using<DateTime>(ctx => ctx.Subject.Should()
        .BeCloseTo(ctx.Expectation, TimeSpan.FromSeconds(1)))
    .WhenTypeIs<DateTime>());
```

### Mocking with Moq

```csharp
public class TradeDataCollectorTests
{
    private readonly Mock<IMarketEventPublisher> _publisherMock;
    private readonly Mock<IQuoteStateStore> _quoteStoreMock;
    private readonly Mock<ILogger<TradeDataCollector>> _loggerMock;
    private readonly TradeDataCollector _sut;

    public TradeDataCollectorTests()
    {
        _publisherMock = new Mock<IMarketEventPublisher>();
        _quoteStoreMock = new Mock<IQuoteStateStore>();
        _loggerMock = new Mock<ILogger<TradeDataCollector>>();

        _sut = new TradeDataCollector(
            _publisherMock.Object,
            _quoteStoreMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public void OnTrade_PublishesEvent()
    {
        // Arrange
        var trade = CreateTestTrade();

        // Act
        _sut.OnTrade(trade);

        // Assert
        _publisherMock.Verify(
            x => x.Publish(It.Is<MarketEvent>(e =>
                e.Type == MarketEventType.Trade &&
                e.Symbol == trade.Symbol)),
            Times.Once);
    }

    [Fact]
    public void OnTrade_WithQuoteContext_InfersAggressor()
    {
        // Arrange
        var quote = new BboQuotePayload { BidPrice = 99m, AskPrice = 101m };
        _quoteStoreMock
            .Setup(x => x.TryGet("AAPL", out It.Ref<BboQuotePayload>.IsAny))
            .Returns((string _, out BboQuotePayload q) =>
            {
                q = quote;
                return true;
            });

        var trade = CreateTestTrade(price: 101m);  // At ask = buyer

        // Act
        _sut.OnTrade(trade);

        // Assert
        _publisherMock.Verify(
            x => x.Publish(It.Is<MarketEvent>(e =>
                ((Trade)e.Payload).Aggressor == AggressorSide.Buyer)),
            Times.Once);
    }
}
```

### Mocking with NSubstitute

```csharp
public class AlpacaClientTests
{
    private readonly ILogger<AlpacaMarketDataClient> _logger;
    private readonly AlpacaMarketDataClient _sut;

    public AlpacaClientTests()
    {
        _logger = Substitute.For<ILogger<AlpacaMarketDataClient>>();
        var options = Options.Create(new AlpacaOptions
        {
            KeyId = "test-key",
            SecretKey = "test-secret"
        });

        _sut = new AlpacaMarketDataClient(_logger, options);
    }

    [Fact]
    public async Task ConnectAsync_LogsConnection()
    {
        // Act
        await _sut.ConnectAsync();

        // Assert
        _logger.Received().Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("Connecting")),
            Arg.Any<Exception>(),
            Arg.Any<Func<object, Exception?, string>>());
    }
}
```

---

## MassTransit Testing

### In-Memory Test Harness

```csharp
public class MassTransitPublisherTests : IAsyncLifetime
{
    private readonly ITestHarness _harness;
    private readonly MassTransitPublisher _sut;

    public MassTransitPublisherTests()
    {
        _harness = new InMemoryTestHarness();
    }

    public async Task InitializeAsync()
    {
        await _harness.Start();
        _sut = new MassTransitPublisher(_harness.Bus);
    }

    public async Task DisposeAsync()
    {
        await _harness.Stop();
    }

    [Fact]
    public async Task Publish_TradeEvent_SendsToConsumer()
    {
        // Arrange
        var evt = CreateTradeEvent();

        // Act
        await _sut.PublishAsync(evt);

        // Assert
        (await _harness.Published.Any<ITradeOccurred>()).Should().BeTrue();

        var published = _harness.Published.Select<ITradeOccurred>().First();
        published.Context.Message.Symbol.Should().Be(evt.Symbol);
    }
}
```

### Consumer Testing

```csharp
public class TradeConsumerTests
{
    [Fact]
    public async Task Consume_ValidTrade_ProcessesSuccessfully()
    {
        // Arrange
        var harness = new InMemoryTestHarness();
        var consumerHarness = harness.Consumer<TradeOccurredConsumer>();

        await harness.Start();
        try
        {
            // Act
            await harness.InputQueueSendEndpoint.Send<ITradeOccurred>(new
            {
                Symbol = "AAPL",
                Price = 150.0m,
                Size = 100L,
                Timestamp = DateTimeOffset.UtcNow
            });

            // Assert
            (await consumerHarness.Consumed.Any<ITradeOccurred>()).Should().BeTrue();
        }
        finally
        {
            await harness.Stop();
        }
    }
}
```

---

## Integration Tests

### Database/Storage Integration

```csharp
public class JsonlStorageSinkIntegrationTests : IAsyncLifetime
{
    private readonly string _testDir;
    private readonly JsonlStorageSink _sut;

    public JsonlStorageSinkIntegrationTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testDir);

        var options = Options.Create(new StorageOptions
        {
            DataRoot = _testDir,
            NamingConvention = FileNamingConvention.BySymbol,
            DatePartition = DatePartition.Daily
        });

        _sut = new JsonlStorageSink(
            NullLogger<JsonlStorageSink>.Instance,
            options);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        Directory.Delete(_testDir, recursive: true);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task WriteAsync_CreatesFile()
    {
        // Arrange
        var evt = CreateTestEvent("AAPL");

        // Act
        await _sut.WriteAsync(evt);
        await _sut.FlushAsync();

        // Assert
        var files = Directory.GetFiles(_testDir, "*.jsonl", SearchOption.AllDirectories);
        files.Should().NotBeEmpty();
    }

    [Fact]
    public async Task WriteAsync_AppendsToPreviousFile()
    {
        // Arrange
        var evt1 = CreateTestEvent("AAPL");
        var evt2 = CreateTestEvent("AAPL");

        // Act
        await _sut.WriteAsync(evt1);
        await _sut.WriteAsync(evt2);
        await _sut.FlushAsync();

        // Assert
        var files = Directory.GetFiles(_testDir, "*.jsonl", SearchOption.AllDirectories);
        files.Should().HaveCount(1);

        var lines = await File.ReadAllLinesAsync(files[0]);
        lines.Should().HaveCount(2);
    }
}
```

### Provider Integration (Skip in CI)

```csharp
public class AlpacaIntegrationTests
{
    private readonly AlpacaMarketDataClient _client;
    private readonly bool _hasCredentials;

    public AlpacaIntegrationTests()
    {
        var keyId = Environment.GetEnvironmentVariable("ALPACA__KEYID");
        var secretKey = Environment.GetEnvironmentVariable("ALPACA__SECRETKEY");

        _hasCredentials = !string.IsNullOrEmpty(keyId) && !string.IsNullOrEmpty(secretKey);

        if (_hasCredentials)
        {
            var options = Options.Create(new AlpacaOptions
            {
                KeyId = keyId!,
                SecretKey = secretKey!
            });
            _client = new AlpacaMarketDataClient(
                NullLogger<AlpacaMarketDataClient>.Instance,
                options);
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ConnectAsync_WithValidCredentials_Succeeds()
    {
        Skip.If(!_hasCredentials, "Alpaca credentials not configured");

        // Act
        await _client.ConnectAsync();

        // Assert
        _client.Status.Should().Be(DataSourceStatus.Connected);
    }
}
```

---

## Benchmarks

### BenchmarkDotNet Example

```csharp
// Location: benchmarks/Meridian.Benchmarks/EventPipelineBenchmarks.cs

[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net90)]
public class EventPipelineBenchmarks
{
    private EventPipeline _pipeline = null!;
    private MarketEvent[] _events = null!;

    [GlobalSetup]
    public void Setup()
    {
        _pipeline = new EventPipeline(capacity: 100_000);
        _events = Enumerable.Range(0, 10_000)
            .Select(i => CreateTestEvent(i))
            .ToArray();
    }

    [Benchmark]
    public void PublishEvents()
    {
        foreach (var evt in _events)
        {
            _pipeline.TryPublish(evt);
        }
    }

    [Benchmark]
    public async Task ConsumeEvents()
    {
        // Pre-fill pipeline
        foreach (var evt in _events)
        {
            _pipeline.TryPublish(evt);
        }

        var count = 0;
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));

        await foreach (var evt in _pipeline.ConsumeAsync(cts.Token))
        {
            count++;
            if (count >= _events.Length) break;
        }
    }
}
```

### Running Benchmarks

```bash
# Run all benchmarks
dotnet run --project benchmarks/Meridian.Benchmarks -c Release

# Run specific benchmark
dotnet run --project benchmarks/Meridian.Benchmarks -c Release -- --filter *EventPipeline*

# Export results to various formats
dotnet run --project benchmarks/Meridian.Benchmarks -c Release -- --exporters html,csv,json
```

---

## Test Data Factories

### Creating Test Data

```csharp
public static class TestDataFactory
{
    public static Trade CreateTrade(
        string symbol = "AAPL",
        decimal price = 150.0m,
        long size = 100,
        AggressorSide aggressor = AggressorSide.Unknown,
        long sequenceNumber = 1)
    {
        return new Trade(
            Timestamp: DateTimeOffset.UtcNow,
            Symbol: symbol,
            Price: price,
            Size: size,
            Aggressor: aggressor,
            SequenceNumber: sequenceNumber,
            StreamId: "test-stream",
            Venue: "TEST");
    }

    public static BboQuotePayload CreateQuote(
        string symbol = "AAPL",
        decimal bidPrice = 149.50m,
        decimal askPrice = 150.50m,
        long bidSize = 500,
        long askSize = 300)
    {
        return new BboQuotePayload(
            Timestamp: DateTimeOffset.UtcNow,
            Symbol: symbol,
            BidPrice: bidPrice,
            BidSize: bidSize,
            AskPrice: askPrice,
            AskSize: askSize,
            MidPrice: (bidPrice + askPrice) / 2,
            Spread: askPrice - bidPrice,
            SequenceNumber: 1,
            StreamId: "test-stream",
            Venue: "TEST");
    }

    public static MarketEvent CreateMarketEvent(
        MarketEventType type = MarketEventType.Trade,
        string symbol = "AAPL")
    {
        return new MarketEvent(
            Timestamp: DateTimeOffset.UtcNow,
            Symbol: symbol,
            Type: type,
            Payload: type switch
            {
                MarketEventType.Trade => CreateTrade(symbol),
                MarketEventType.Quote => CreateQuote(symbol),
                _ => throw new NotSupportedException()
            });
    }

    public static IEnumerable<Trade> CreateTrades(int count, string symbol = "AAPL")
    {
        return Enumerable.Range(1, count)
            .Select(i => CreateTrade(
                symbol: symbol,
                price: 150.0m + (i * 0.01m),
                sequenceNumber: i));
    }
}
```

---

## Testing Conventions

### Test Method Naming

Use the pattern: `{Method}_{Scenario}_{ExpectedResult}`

```csharp
// Good
public void OnTrade_WithSequenceGap_EmitsIntegrityEvent()
public void GetHistoricalBars_WithInvalidDateRange_ThrowsArgumentException()
public void Parse_ValidJsonl_ReturnsEvents()

// Avoid
public void Test1()
public void TestTradeProcessing()
public void ShouldWork()
```

### Theory Data

```csharp
public class ValidationTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    [InlineData(100, true)]
    [InlineData(1000000000, false)]  // Too large
    public void ValidatePrice_ReturnsExpectedResult(decimal price, bool expected)
    {
        var result = Validator.ValidatePrice(price);
        result.IsValid.Should().Be(expected);
    }

    [Theory]
    [MemberData(nameof(InvalidSymbolTestCases))]
    public void ValidateSymbol_WithInvalidInput_ReturnsFalse(string symbol, string reason)
    {
        var result = Validator.ValidateSymbol(symbol);

        result.IsValid.Should().BeFalse();
        result.Error.Should().Contain(reason);
    }

    public static IEnumerable<object[]> InvalidSymbolTestCases()
    {
        yield return new object[] { "", "empty" };
        yield return new object[] { null!, "null" };
        yield return new object[] { new string('A', 100), "too long" };
        yield return new object[] { "AAPL@NYSE", "invalid character" };
    }
}
```

### Async Testing

```csharp
[Fact]
public async Task ProcessAsync_CompletesWithinTimeout()
{
    // Arrange
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

    // Act
    var task = _sut.ProcessAsync(cts.Token);

    // Assert
    await task.Should().CompleteWithinAsync(TimeSpan.FromSeconds(5));
}

[Fact]
public async Task ProcessAsync_CancellationRequested_ThrowsOperationCanceled()
{
    // Arrange
    using var cts = new CancellationTokenSource();
    cts.Cancel();

    // Act & Assert
    await _sut.Invoking(x => x.ProcessAsync(cts.Token))
        .Should().ThrowAsync<OperationCanceledException>();
}
```

---

## Test Categories

Use traits to categorize tests:

```csharp
[Fact]
[Trait("Category", "Unit")]
public void UnitTest() { }

[Fact]
[Trait("Category", "Integration")]
public void IntegrationTest() { }

[Fact]
[Trait("Category", "Performance")]
public void PerformanceTest() { }

// Run only unit tests
// dotnet test --filter "Category=Unit"

// Skip integration tests
// dotnet test --filter "Category!=Integration"
```

---

## Related Documentation

- [xUnit Documentation](https://xunit.net/docs/getting-started/netcore/cmdline)
- [FluentAssertions Documentation](https://fluentassertions.com/introduction)
- [Moq Documentation](https://github.com/moq/moq4/wiki/Quickstart)
- [MassTransit Testing](https://masstransit.io/documentation/concepts/testing)
- [BenchmarkDotNet Documentation](https://benchmarkdotnet.org/articles/overview.html)

## Related Resources

- **Master AI index:** [`docs/ai/README.md`](../../archive/docs/README.md)
- **Root context:** [`CLAUDE.md`](../../../CLAUDE.md)
- **Code review (Lens 4 - Test Quality):** [`.github/agents/code-review-agent.md`](../../../.github/agents/code-review-agent.md)
- **Test instructions:** [`.github/instructions/dotnet-tests.instructions.md`](../../../.github/instructions/dotnet-tests.instructions.md)

---

*Last Updated: 2026-03-16*
