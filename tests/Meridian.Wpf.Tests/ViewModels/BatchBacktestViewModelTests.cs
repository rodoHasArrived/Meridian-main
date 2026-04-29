using FluentAssertions;
using Meridian.Backtesting;
using Meridian.Backtesting.Sdk;
using Meridian.Ledger;
using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed class BatchBacktestViewModelTests
{
    [Fact]
    public void StartBatchCommand_BuildsRequestSweepAndDisplaysReturnedResults()
    {
        WpfTestThread.Run(async () =>
        {
            var service = new CapturingBatchBacktestService((request, progress, _) =>
            {
                progress.Report(new BatchBacktestProgress
                {
                    Completed = 1,
                    Total = request.ParameterGrid.Count,
                    CurrentLabel = "InitialCash=100000"
                });

                return Task.FromResult(new BatchBacktestSummary
                {
                    Runs =
                    [
                        new BatchBacktestRun
                        {
                            Parameters = request.ParameterGrid[0],
                            Result = BuildResult(request.BaseRequest with { InitialCash = 100_000m }),
                            DurationMs = 125
                        },
                        new BatchBacktestRun
                        {
                            Parameters = request.ParameterGrid[1],
                            Result = null,
                            DurationMs = 80,
                            ErrorMessage = "No local data found"
                        }
                    ],
                    TotalDuration = TimeSpan.FromMilliseconds(205)
                });
            });

            var viewModel = new BatchBacktestViewModel(service)
            {
                SymbolsText = "spy, aapl",
                FromDate = new DateTime(2026, 1, 1),
                ToDate = new DateTime(2026, 1, 31),
                InitialCash = 100_000m,
                DataRoot = "./data/test",
                SweepStart = 100_000m,
                SweepStop = 200_000m,
                SweepStep = 100_000m,
                MaxConcurrency = 2
            };

            await viewModel.StartBatchCommand.ExecuteAsync(null);

            service.CapturedRequest.Should().NotBeNull();
            service.CapturedRequest!.BaseRequest.Symbols.Should().Equal("SPY", "AAPL");
            service.CapturedRequest.BaseRequest.DataRoot.Should().Be("./data/test");
            service.CapturedRequest.MaxConcurrency.Should().Be(2);
            service.CapturedRequest.ParameterGrid.Should().HaveCount(2);
            service.CapturedRequest.ParameterGrid[0][nameof(BacktestRequest.InitialCash)].Should().Be(100_000m);
            service.CapturedRequest.ParameterGrid[1][nameof(BacktestRequest.InitialCash)].Should().Be(200_000m);

            viewModel.Results.Should().HaveCount(2);
            viewModel.SucceededRuns.Should().Be(1);
            viewModel.FailedRuns.Should().Be(1);
            viewModel.CompletedRuns.Should().Be(2);
            viewModel.ProgressPercent.Should().Be(100);
            viewModel.StatusText.Should().Contain("failed");
            viewModel.Results.Single(item => item.HasError).ErrorMessage.Should().Be("No local data found");
            viewModel.HasResults.Should().BeTrue();
            viewModel.IsResultsEmptyStateVisible.Should().BeFalse();
            viewModel.ResultsEmptyStateTitle.Should().Be("Batch results need review");
            viewModel.ResultsEmptyStateDetail.Should().Contain("1 succeeded, 1 failed");
        });
    }

    [Fact]
    public void StartBatchCommand_WhenServiceFails_SurfacesFailureAndStopsRunning()
    {
        WpfTestThread.Run(async () =>
        {
            var service = new CapturingBatchBacktestService((_, _, _) =>
                throw new InvalidOperationException("batch service unavailable"));
            var viewModel = new BatchBacktestViewModel(service)
            {
                SweepStart = 100_000m,
                SweepStop = 100_000m,
                SweepStep = 100_000m
            };

            await viewModel.StartBatchCommand.ExecuteAsync(null);

            viewModel.IsRunning.Should().BeFalse();
            viewModel.Results.Should().BeEmpty();
            viewModel.StatusText.Should().Be("Batch failed: batch service unavailable");
            viewModel.SummaryText.Should().Be("No completed summary was returned.");
            viewModel.HasResults.Should().BeFalse();
            viewModel.IsResultsEmptyStateVisible.Should().BeTrue();
            viewModel.ResultsEmptyStateTitle.Should().Be("Batch did not return results");
            viewModel.ResultsEmptyStateDetail.Should().Be("No completed summary was returned.");
        });
    }

    [Fact]
    public void CancelCommand_CancelsActiveBatch()
    {
        WpfTestThread.Run(async () =>
        {
            var service = new BlockingBatchBacktestService();
            var viewModel = new BatchBacktestViewModel(service)
            {
                SweepStart = 100_000m,
                SweepStop = 100_000m,
                SweepStep = 100_000m
            };

            var runTask = viewModel.StartBatchCommand.ExecuteAsync(null);
            await service.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

            viewModel.IsRunning.Should().BeTrue();
            viewModel.CancelCommand.Execute(null);

            await runTask;

            service.CancellationObserved.Should().BeTrue();
            viewModel.IsRunning.Should().BeFalse();
            viewModel.StatusText.Should().Be("Cancelled.");
            viewModel.HasResults.Should().BeFalse();
            viewModel.IsResultsEmptyStateVisible.Should().BeTrue();
            viewModel.ResultsEmptyStateTitle.Should().Be("Batch cancelled before results");
            viewModel.ResultsEmptyStateDetail.Should().Contain("completed before cancellation");
        });
    }

    [Fact]
    public void ResultsEmptyState_InitialAndValidationStatesProvideOperatorGuidance()
    {
        WpfTestThread.Run(() =>
        {
            var service = new CapturingBatchBacktestService((_, _, _) =>
                Task.FromResult(new BatchBacktestSummary
                {
                    Runs = [],
                    TotalDuration = TimeSpan.Zero
                }));
            var viewModel = new BatchBacktestViewModel(service)
            {
                SweepStart = 100_000m,
                SweepStop = 100_000m,
                SweepStep = 100_000m
            };

            viewModel.HasResults.Should().BeFalse();
            viewModel.IsResultsEmptyStateVisible.Should().BeTrue();
            viewModel.ResultsEmptyStateTitle.Should().Be("No batch results yet");
            viewModel.ResultsEmptyStateDetail.Should().Contain("Start the configured sweep");

            viewModel.SweepStep = 0;

            viewModel.CanStartBatch.Should().BeFalse();
            viewModel.ResultsEmptyStateTitle.Should().Be("No batch results yet");
            viewModel.ResultsEmptyStateDetail.Should().Be("Resolve the validation issue, then start the request sweep.");
        });
    }

    [Fact]
    public void BatchBacktestPageSource_BindsResultsEmptyState()
    {
        var xaml = File.ReadAllText(GetRepositoryFilePath(@"src\Meridian.Wpf\Views\BatchBacktestPage.xaml"));

        xaml.Should().Contain("BatchBacktestResultsGrid");
        xaml.Should().Contain("BatchBacktestResultsEmptyState");
        xaml.Should().Contain("{Binding HasResults, Converter={StaticResource BoolToVisibilityConverter}}");
        xaml.Should().Contain("{Binding IsResultsEmptyStateVisible, Converter={StaticResource BoolToVisibilityConverter}}");
        xaml.Should().Contain("{Binding ResultsEmptyStateTitle}");
        xaml.Should().Contain("{Binding ResultsEmptyStateDetail}");
        xaml.Should().Contain("BatchBacktestResultsEmptyStateTitle");
        xaml.Should().Contain("BatchBacktestResultsEmptyStateDetail");
    }

    private static string GetRepositoryFilePath(string relativePath)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException($"Could not locate repository file '{relativePath}' from '{AppContext.BaseDirectory}'.");
    }

    [Fact]
    public void ClearResultsCommand_ReturnsResultsPanelToIdleGuidance()
    {
        WpfTestThread.Run(async () =>
        {
            var service = new CapturingBatchBacktestService((request, _, _) =>
                Task.FromResult(new BatchBacktestSummary
                {
                    Runs =
                    [
                        new BatchBacktestRun
                        {
                            Parameters = request.ParameterGrid[0],
                            Result = BuildResult(request.BaseRequest),
                            DurationMs = 125
                        }
                    ],
                    TotalDuration = TimeSpan.FromMilliseconds(125)
                }));
            var viewModel = new BatchBacktestViewModel(service)
            {
                SweepStart = 100_000m,
                SweepStop = 100_000m,
                SweepStep = 100_000m
            };

            await viewModel.StartBatchCommand.ExecuteAsync(null);

            viewModel.HasResults.Should().BeTrue();

            viewModel.ClearResultsCommand.Execute(null);

            viewModel.HasResults.Should().BeFalse();
            viewModel.IsResultsEmptyStateVisible.Should().BeTrue();
            viewModel.ResultsEmptyStateTitle.Should().Be("No batch results yet");
            viewModel.ResultsEmptyStateDetail.Should().Contain("Start the configured sweep");
        });
    }

    private static BacktestResult BuildResult(BacktestRequest request)
    {
        var metrics = new BacktestMetrics(
            InitialCapital: request.InitialCash,
            FinalEquity: request.InitialCash + 1_250m,
            GrossPnl: 1_300m,
            NetPnl: 1_250m,
            TotalReturn: 0.0125m,
            AnnualizedReturn: 0.15m,
            SharpeRatio: 1.42,
            SortinoRatio: 1.60,
            CalmarRatio: 0.90,
            MaxDrawdown: 300m,
            MaxDrawdownPercent: 0.03m,
            MaxDrawdownRecoveryDays: 4,
            ProfitFactor: 1.8,
            WinRate: 0.57,
            TotalTrades: 7,
            WinningTrades: 4,
            LosingTrades: 3,
            TotalCommissions: 12m,
            TotalMarginInterest: 0m,
            TotalShortRebates: 0m,
            Xirr: 0.12,
            SymbolAttribution: new Dictionary<string, SymbolAttribution>());

        var universe = request.Symbols is { Count: > 0 }
            ? new HashSet<string>(request.Symbols, StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(["SPY"], StringComparer.OrdinalIgnoreCase);

        return new BacktestResult(
            Request: request,
            Universe: universe,
            Snapshots: [],
            CashFlows: [],
            Fills: [],
            Metrics: metrics,
            Ledger: new Meridian.Ledger.Ledger(),
            ElapsedTime: TimeSpan.FromSeconds(1.2),
            TotalEventsProcessed: 1234);
    }

    private sealed class CapturingBatchBacktestService(
        Func<BatchBacktestRequest, IProgress<BatchBacktestProgress>, CancellationToken, Task<BatchBacktestSummary>> handler)
        : IBatchBacktestService
    {
        public BatchBacktestRequest? CapturedRequest { get; private set; }

        public Task<BatchBacktestSummary> RunBatchAsync(
            BatchBacktestRequest request,
            IProgress<BatchBacktestProgress> progress,
            CancellationToken ct)
        {
            CapturedRequest = request;
            return handler(request, progress, ct);
        }
    }

    private sealed class BlockingBatchBacktestService : IBatchBacktestService
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool CancellationObserved { get; private set; }

        public async Task<BatchBacktestSummary> RunBatchAsync(
            BatchBacktestRequest request,
            IProgress<BatchBacktestProgress> progress,
            CancellationToken ct)
        {
            Started.SetResult();

            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            }
            catch (OperationCanceledException)
            {
                CancellationObserved = ct.IsCancellationRequested;
                throw;
            }

            throw new InvalidOperationException("Unreachable after infinite delay.");
        }
    }
}
