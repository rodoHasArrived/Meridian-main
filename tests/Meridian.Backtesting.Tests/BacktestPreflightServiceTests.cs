using FluentAssertions;
using Meridian.Application.Backtesting;
using Meridian.Backtesting.Engine;
using Meridian.Backtesting.Sdk;
using Meridian.Contracts.Backtesting;
using Meridian.Contracts.Services;
using Meridian.Domain.Events;
using Meridian.Storage;
using Meridian.Storage.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Backtesting.Tests;

public sealed class BacktestPreflightServiceTests : IDisposable
{
    private readonly string _dataRoot;

    public BacktestPreflightServiceTests()
    {
        _dataRoot = Path.Combine(Path.GetTempPath(), $"meridian-preflight-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dataRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataRoot))
            Directory.Delete(_dataRoot, recursive: true);
    }

    [Fact]
    public async Task RunAsync_ValidRequest_ReturnsReadyReport()
    {
        var sut = new BacktestPreflightService();

        var report = await sut.RunAsync(new BacktestPreflightRequestDto(
            From: new DateOnly(2024, 1, 1),
            To: new DateOnly(2024, 1, 31),
            DataRoot: _dataRoot,
            Symbols: ["AAPL", "MSFT"]));

        report.IsReadyToRun.Should().BeTrue();
        report.HasWarnings.Should().BeFalse();
        report.Checks.Should().OnlyContain(c => c.Status == BacktestPreflightCheckStatusDto.Passed);
    }

    [Fact]
    public async Task RunAsync_InvalidRangeAndMissingRoot_ReturnsFailures()
    {
        var sut = new BacktestPreflightService();

        var report = await sut.RunAsync(new BacktestPreflightRequestDto(
            From: new DateOnly(2024, 2, 1),
            To: new DateOnly(2024, 1, 1),
            DataRoot: Path.Combine(_dataRoot, "missing"),
            Symbols: ["AAPL", "AAPL"]));

        report.IsReadyToRun.Should().BeFalse();
        report.HasWarnings.Should().BeTrue();
        report.Checks.Should().Contain(c => c.Name == "Date Range" && c.Status == BacktestPreflightCheckStatusDto.Failed);
        report.Checks.Should().Contain(c => c.Name == "Data Root" && c.Status == BacktestPreflightCheckStatusDto.Failed);
        report.Checks.Should().Contain(c => c.Name == "Symbol Scope" && c.Status == BacktestPreflightCheckStatusDto.Warning);
    }

    [Fact]
    public async Task RunAsync_Cancelled_ThrowsOperationCanceledException()
    {
        var sut = new BacktestPreflightService();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => sut.RunAsync(new BacktestPreflightRequestDto(
            new DateOnly(2024, 1, 1),
            new DateOnly(2024, 1, 2),
            _dataRoot), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Engine_RunAsync_PreflightFailure_ThrowsInvalidOperationException()
    {
        var catalog = new StorageCatalogService(_dataRoot, new StorageOptions());
        var engine = new BacktestEngine(
            NullLogger<BacktestEngine>.Instance,
            catalog,
            backtestPreflightService: new AlwaysFailingPreflightService());

        var request = new BacktestRequest(
            From: new DateOnly(2024, 1, 1),
            To: new DateOnly(2024, 1, 2),
            DataRoot: _dataRoot);

        var act = () => engine.RunAsync(request, new LocalNoOpStrategy(), progress: null, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Backtest preflight failed*");
    }

    private sealed class AlwaysFailingPreflightService : IBacktestPreflightService
    {
        public Task<BacktestPreflightReportV2Dto> RunAsync(BacktestPreflightRequestDto request, CancellationToken ct = default)
            => Task.FromResult(new BacktestPreflightReportV2Dto(
                IsReadyToRun: false,
                HasWarnings: false,
                Checks:
                [
                    new BacktestPreflightCheckResultDto(
                        Name: "Synthetic",
                        Status: BacktestPreflightCheckStatusDto.Failed,
                        Message: "forced failure")
                ],
                TotalDurationMs: 1,
                CheckedAt: DateTimeOffset.UtcNow,
                SummaryMessage: "forced"));
    }

    private sealed class LocalNoOpStrategy : IBacktestStrategy
    {
        public string Name => "local-no-op";
        public void Initialize(IBacktestContext ctx) { }
        public void OnTrade(Trade trade, IBacktestContext ctx) { }
        public void OnQuote(BboQuotePayload quote, IBacktestContext ctx) { }
        public void OnBar(HistoricalBar bar, IBacktestContext ctx) { }
        public void OnOrderBook(LOBSnapshot snapshot, IBacktestContext ctx) { }
        public void OnOrderFill(FillEvent fill, IBacktestContext ctx) { }
        public void OnDayEnd(DateOnly date, IBacktestContext ctx) { }
        public void OnFinished(IBacktestContext ctx) { }
    }
}
