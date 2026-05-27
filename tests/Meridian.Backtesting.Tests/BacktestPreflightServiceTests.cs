using FluentAssertions;
using Meridian.Application.Backtesting;
using Meridian.Application.SecurityMaster;
using Meridian.Backtesting.Engine;
using Meridian.Backtesting.Sdk;
using Meridian.Contracts.Backtesting;
using Meridian.Contracts.SecurityMaster;
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
        foreach (var symbol in new[] { "AAPL", "MSFT" })
        {
            File.WriteAllText(Path.Combine(_dataRoot, $"{symbol}_bars_2024-01-03.jsonl"), "{}\n");
            File.WriteAllText(Path.Combine(_dataRoot, $"{symbol}_quotes_2024-01-03.jsonl"), "{}\n");
            File.WriteAllText(Path.Combine(_dataRoot, $"{symbol}_trades_2024-01-03.jsonl"), "{}\n");
        }

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
    public async Task RunAsync_OrderBookExecutionMissingBookData_ReturnsStructuredFailureAndCoverageWarning()
    {
        File.WriteAllText(Path.Combine(_dataRoot, "AAPL_bars_2024-01-03.jsonl"), "{}\n");
        File.WriteAllText(Path.Combine(_dataRoot, "AAPL_quotes_2024-01-03.jsonl"), "{}\n");
        File.WriteAllText(Path.Combine(_dataRoot, "AAPL_trades_2024-01-03.jsonl"), "{}\n");

        var sut = new BacktestPreflightService();
        var report = await sut.RunAsync(new BacktestPreflightRequestDto(
            From: new DateOnly(2024, 1, 1),
            To: new DateOnly(2024, 1, 31),
            DataRoot: _dataRoot,
            Symbols: ["AAPL"],
            RequiredExecutionModel: "OrderBook"));

        report.IsReadyToRun.Should().BeFalse();
        report.HasWarnings.Should().BeTrue();
        report.Checks.Should().Contain(c => c.Name == "Execution Model Compatibility" && c.Status == BacktestPreflightCheckStatusDto.Failed);
        report.Checks.Should().Contain(c => c.Name == "Replay Coverage" && c.Status == BacktestPreflightCheckStatusDto.Warning && c.Details!["missingBuckets"].Contains("AAPL:2024-01:book"));
    }

    [Fact]
    public async Task RunAsync_MonthlyCoverageGap_ReturnsStructuredWarningBySymbolMonth()
    {
        File.WriteAllText(Path.Combine(_dataRoot, "MSFT_bars_2024-01-05.jsonl"), "{}\n");
        File.WriteAllText(Path.Combine(_dataRoot, "MSFT_quotes_2024-01-05.jsonl"), "{}\n");
        File.WriteAllText(Path.Combine(_dataRoot, "MSFT_trades_2024-01-05.jsonl"), "{}\n");

        var sut = new BacktestPreflightService();
        var report = await sut.RunAsync(new BacktestPreflightRequestDto(
            From: new DateOnly(2024, 1, 1),
            To: new DateOnly(2024, 2, 29),
            DataRoot: _dataRoot,
            Symbols: ["MSFT"],
            RequiredReplayDataTypes: ["bars", "quotes", "trades"]));

        report.IsReadyToRun.Should().BeTrue();
        report.HasWarnings.Should().BeTrue();
        report.Checks.Should().Contain(c => c.Name == "Replay Coverage" && c.Status == BacktestPreflightCheckStatusDto.Warning && c.Details!["missingBuckets"].Contains("MSFT:2024-02:bars"));
    }

    [Fact]
    public async Task RunAsync_OrderBookExecution_WithBySymbolStorageLayout_IsCompatible()
    {
        Directory.CreateDirectory(Path.Combine(_dataRoot, "AAPL", "HistoricalBar"));
        Directory.CreateDirectory(Path.Combine(_dataRoot, "AAPL", "BboQuote"));
        Directory.CreateDirectory(Path.Combine(_dataRoot, "AAPL", "Trade"));
        Directory.CreateDirectory(Path.Combine(_dataRoot, "AAPL", "L2Snapshot"));
        File.WriteAllText(Path.Combine(_dataRoot, "AAPL", "HistoricalBar", "2024-01-03.jsonl"), "{}\n");
        File.WriteAllText(Path.Combine(_dataRoot, "AAPL", "BboQuote", "2024-01-03.jsonl"), "{}\n");
        File.WriteAllText(Path.Combine(_dataRoot, "AAPL", "Trade", "2024-01-03.jsonl"), "{}\n");
        File.WriteAllText(Path.Combine(_dataRoot, "AAPL", "L2Snapshot", "2024-01-03.jsonl"), "{}\n");

        var sut = new BacktestPreflightService();
        var report = await sut.RunAsync(new BacktestPreflightRequestDto(
            From: new DateOnly(2024, 1, 1),
            To: new DateOnly(2024, 1, 31),
            DataRoot: _dataRoot,
            Symbols: ["AAPL"],
            RequiredExecutionModel: "OrderBook"));

        report.IsReadyToRun.Should().BeTrue();
        report.Checks.Should().Contain(c => c.Name == "Execution Model Compatibility" && c.Status == BacktestPreflightCheckStatusDto.Passed);
        report.Checks.Should().Contain(c => c.Name == "Replay Coverage" && c.Status == BacktestPreflightCheckStatusDto.Passed);
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
    public async Task RunAsync_SecurityMasterBlockingIssue_FailsPreflight()
    {
        var gate = new StubSecurityValidationGate(new SecurityValidationIssueDto(
            SecurityValidationSeverityDto.Error,
            "SM_IDENTIFIER_MISSING",
            "Active identifier is missing",
            "Security Master records must have at least one active identifier.",
            ["identifiers"],
            "Attach a canonical identifier.",
            []));
        var sut = new BacktestPreflightService(gate);

        var report = await sut.RunAsync(new BacktestPreflightRequestDto(
            From: new DateOnly(2024, 1, 1),
            To: new DateOnly(2024, 1, 31),
            DataRoot: _dataRoot,
            Symbols: ["AAPL"]));

        report.IsReadyToRun.Should().BeFalse();
        report.Checks.Should().Contain(check =>
            check.Name == "Security Master Validation"
            && check.Status == BacktestPreflightCheckStatusDto.Failed
            && check.Details!["blockedSymbols"] == "AAPL"
            && check.Details["issueCodes"] == "SM_IDENTIFIER_MISSING");
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

    private sealed class StubSecurityValidationGate(SecurityValidationIssueDto issue) : ISecurityValidationGateService
    {
        public Task<SecurityValidationGateResultDto> ValidateSymbolAsync(
            string symbol,
            SecurityValidationWorkflowDto workflow,
            string? workflowReference = null,
            string? actor = null,
            bool persistSnapshot = false,
            CancellationToken ct = default)
        {
            var report = new SecurityValidationReportDto(
                SecurityId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
                Scope: "Security",
                EvaluatedAtUtc: DateTimeOffset.UtcNow,
                HasBlockingIssues: issue.Severity is SecurityValidationSeverityDto.Error or SecurityValidationSeverityDto.Critical,
                CriticalIssueCount: issue.Severity == SecurityValidationSeverityDto.Critical ? 1 : 0,
                ErrorIssueCount: issue.Severity == SecurityValidationSeverityDto.Error ? 1 : 0,
                Issues: [issue]);

            return Task.FromResult(new SecurityValidationGateResultDto(
                workflow,
                symbol.Trim().ToUpperInvariant(),
                report.SecurityId,
                IsResolved: true,
                IsBlocked: report.HasBlockingIssues,
                Report: report,
                Snapshot: null));
        }

        public Task<SecurityValidationGateResultDto> ValidateSecurityAsync(
            Guid securityId,
            SecurityValidationWorkflowDto workflow,
            string? workflowReference = null,
            string? actor = null,
            bool persistSnapshot = false,
            string? symbol = null,
            CancellationToken ct = default)
            => ValidateSymbolAsync(symbol ?? securityId.ToString(), workflow, workflowReference, actor, persistSnapshot, ct);
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
