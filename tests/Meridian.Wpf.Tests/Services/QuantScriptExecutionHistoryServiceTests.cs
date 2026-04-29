#if WINDOWS
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Meridian.Backtesting.Sdk;
using Meridian.Execution.Sdk;
using Meridian.Ledger;
using Meridian.QuantScript.Documents;
using Meridian.Strategies.Services;
using Meridian.Strategies.Storage;
using Meridian.Wpf.Models;
using Meridian.Wpf.Services;

namespace Meridian.Wpf.Tests.Services;

public sealed class QuantScriptExecutionHistoryServiceTests
{
    [Fact]
    public async Task RecordExecutionAsync_WithSingleBacktest_WritesLocalHistoryAndMirrorsResearchRun()
    {
        var dataRoot = Path.Combine(Path.GetTempPath(), "quant-script-history-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dataRoot);

        using var scope = new ConfigScope(dataRoot);
        var strategyRunService = new StrategyRunWorkspaceService(
            new StrategyRunStore(),
            new PortfolioReadService(),
            new LedgerReadService());
        var service = new QuantScriptExecutionHistoryService(
            ConfigService.Instance,
            strategyRunService,
            NullLogger<QuantScriptExecutionHistoryService>.Instance);

        var record = await service.RecordExecutionAsync(
            new QuantScriptExecutionRecordRequest(
                DocumentTitle: "Hello SPY",
                DocumentPath: @"C:\temp\hello-spy.csx",
                DocumentKind: QuantScriptDocumentKind.LegacyScript,
                Success: true,
                ParameterSnapshot: new Dictionary<string, string>
                {
                    ["symbol"] = "SPY"
                },
                RuntimeParameters:
                [
                    new QuantScriptResolvedParameterDescriptorRecord(
                        "symbol",
                        "string",
                        "symbol",
                        "SPY",
                        "SPY")
                ],
                ConsoleExcerpt: "Loaded SPY.",
                Metrics:
                [
                    new QuantScriptExecutionMetricRecord("Total Return", "5.00%", "Backtest")
                ],
                PlotTitles: ["Price"],
                CapturedBacktests: [BuildBacktestResult(dataRoot)]));

        var historyPath = Path.Combine(dataRoot, "_quantscript", "runs", $"{record.ExecutionId}.json");
        var history = await service.GetHistoryAsync();
        var detail = await strategyRunService.GetRunDetailAsync(record.MirroredRunId!);

        File.Exists(historyPath).Should().BeTrue();
        record.MirroredRunId.Should().NotBeNullOrEmpty();
        history.Should().ContainSingle(item => item.ExecutionId == record.ExecutionId);
        detail.Should().NotBeNull();
        detail!.Parameters.Should().Contain(new KeyValuePair<string, string>("documentPath", @"C:\temp\hello-spy.csx"));
        detail.Parameters.Should().Contain(new KeyValuePair<string, string>("executionId", record.ExecutionId));
        detail.Parameters.Should().Contain(new KeyValuePair<string, string>("symbol", "SPY"));
    }

    [Fact]
    public async Task RecordExecutionAsync_WithMultipleBacktests_WritesWarningAndSkipsMirroring()
    {
        var dataRoot = Path.Combine(Path.GetTempPath(), "quant-script-history-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dataRoot);

        using var scope = new ConfigScope(dataRoot);
        var strategyRunService = new StrategyRunWorkspaceService(
            new StrategyRunStore(),
            new PortfolioReadService(),
            new LedgerReadService());
        var service = new QuantScriptExecutionHistoryService(
            ConfigService.Instance,
            strategyRunService,
            NullLogger<QuantScriptExecutionHistoryService>.Instance);

        var record = await service.RecordExecutionAsync(
            new QuantScriptExecutionRecordRequest(
                DocumentTitle: "Backtest Notebook",
                DocumentPath: @"C:\temp\backtest-notebook.qnb",
                DocumentKind: QuantScriptDocumentKind.Notebook,
                Success: true,
                ParameterSnapshot: new Dictionary<string, string>(),
                RuntimeParameters: [],
                ConsoleExcerpt: "Ran two backtests.",
                Metrics: [],
                PlotTitles: [],
                CapturedBacktests:
                [
                    BuildBacktestResult(dataRoot),
                    BuildBacktestResult(dataRoot)
                ]));

        var history = await service.GetHistoryAsync();
        var runs = await strategyRunService.GetRecordedRunsAsync();

        record.MirroredRunId.Should().BeNull();
        record.Warning.Should().Contain("skipped");
        history.Should().ContainSingle(item => item.ExecutionId == record.ExecutionId);
        runs.Should().BeEmpty();
    }

    private static BacktestResult BuildBacktestResult(string dataRoot)
    {
        var startedAt = new DateTimeOffset(2026, 4, 1, 14, 0, 0, TimeSpan.Zero);
        var completedAt = startedAt.AddMinutes(5);
        var positions = new Dictionary<string, Position>(StringComparer.OrdinalIgnoreCase)
        {
            ["SPY"] = new("SPY", 10, 500m, 50m, 100m)
        };

        var account = FinancialAccount.CreateDefaultBrokerage(100_000m, 0.05, 0.02);
        var accountSnapshots = new Dictionary<string, FinancialAccountSnapshot>(StringComparer.OrdinalIgnoreCase)
        {
            [account.AccountId] = new FinancialAccountSnapshot(
                AccountId: account.AccountId,
                DisplayName: account.DisplayName,
                Kind: account.Kind,
                Institution: account.Institution,
                Cash: 95_000m,
                MarginBalance: 0m,
                LongMarketValue: 5_500m,
                ShortMarketValue: 0m,
                Equity: 100_500m,
                Positions: positions,
                Rules: account.Rules!)
        };

        var snapshot = new PortfolioSnapshot(
            Timestamp: completedAt,
            Date: DateOnly.FromDateTime(completedAt.UtcDateTime),
            Cash: 95_000m,
            MarginBalance: 0m,
            LongMarketValue: 5_500m,
            ShortMarketValue: 0m,
            TotalEquity: 100_500m,
            DailyReturn: 0.005m,
            Positions: positions,
            Accounts: accountSnapshots,
            DayCashFlows: Array.Empty<CashFlowEntry>());

        var ledger = new Meridian.Ledger.Ledger();
        var cash = new LedgerAccount("Cash", LedgerAccountType.Asset);
        var equity = new LedgerAccount("Owner Equity", LedgerAccountType.Equity);
        ledger.PostLines(startedAt, "initial-capital", new[]
        {
            (cash, 100_000m, 0m),
            (equity, 0m, 100_000m)
        });

        return new BacktestResult(
            Request: new BacktestRequest(
                From: new DateOnly(2026, 4, 1),
                To: new DateOnly(2026, 4, 5),
                Symbols: ["SPY"],
                InitialCash: 100_000m,
                DataRoot: dataRoot),
            Universe: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "SPY" },
            Snapshots: [snapshot],
            CashFlows: Array.Empty<CashFlowEntry>(),
            Fills:
            [
                new FillEvent(Guid.NewGuid(), Guid.NewGuid(), "SPY", 10, 500m, 1m, startedAt.AddMinutes(1), account.AccountId)
            ],
            Metrics: new BacktestMetrics(
                InitialCapital: 100_000m,
                FinalEquity: 100_500m,
                GrossPnl: 501m,
                NetPnl: 500m,
                TotalReturn: 0.005m,
                AnnualizedReturn: 0.005m,
                SharpeRatio: 1.0,
                SortinoRatio: 1.0,
                CalmarRatio: 1.0,
                MaxDrawdown: 100m,
                MaxDrawdownPercent: 0.001m,
                MaxDrawdownRecoveryDays: 1,
                ProfitFactor: 1.2,
                WinRate: 1.0,
                TotalTrades: 1,
                WinningTrades: 1,
                LosingTrades: 0,
                TotalCommissions: 1m,
                TotalMarginInterest: 0m,
                TotalShortRebates: 0m,
                Xirr: 0.01,
                SymbolAttribution: new Dictionary<string, SymbolAttribution>
                {
                    ["SPY"] = new("SPY", 500m, 50m, 1, 1m, 0m)
                }),
            Ledger: ledger,
            ElapsedTime: TimeSpan.FromMinutes(5),
            TotalEventsProcessed: 10);
    }

    private sealed class ConfigScope : IDisposable
    {
        private readonly string _configPath = FirstRunService.Instance.ConfigFilePath;
        private readonly string? _originalContent;
        private readonly bool _hadExistingFile;

        public ConfigScope(string dataRoot)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_configPath)!);
            _hadExistingFile = File.Exists(_configPath);
            if (_hadExistingFile)
                _originalContent = File.ReadAllText(_configPath);

            File.WriteAllText(_configPath, JsonSerializer.Serialize(new
            {
                DataRoot = dataRoot
            }));
        }

        public void Dispose()
        {
            if (_hadExistingFile)
            {
                File.WriteAllText(_configPath, _originalContent ?? string.Empty);
            }
            else if (File.Exists(_configPath))
            {
                File.Delete(_configPath);
            }
        }
    }
}
#endif
