using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Meridian.Backtesting;
using Meridian.Backtesting.Sdk;

namespace Meridian.Wpf.ViewModels;

/// <summary>
/// Describes one supported request-level parameter that the desktop batch panel can sweep.
/// </summary>
public sealed record BatchSweepParameterOption(
    string Key,
    string Label,
    string Format,
    decimal DefaultStart,
    decimal DefaultStop,
    decimal DefaultStep);

/// <summary>
/// Result item for display in the batch backtest results grid.
/// </summary>
public sealed class BatchRunResultItem
{
    public required string ParametersSummary { get; init; }
    public required string Status { get; init; }
    public required string TotalReturnText { get; init; }
    public required string SharpeRatioText { get; init; }
    public required string MaxDrawdownText { get; init; }
    public required string NetPnlText { get; init; }
    public required string TradesText { get; init; }
    public required string EventsText { get; init; }
    public required string DurationText { get; init; }
    public required string ErrorMessage { get; init; }
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public static BatchRunResultItem FromRun(BatchBacktestRun run)
    {
        var result = run.Result;
        var metrics = result?.Metrics;

        return new BatchRunResultItem
        {
            ParametersSummary = FormatParameterLabel(run.Parameters),
            Status = result is null ? "Failed" : "Complete",
            TotalReturnText = metrics is null ? "-" : $"{metrics.TotalReturn:P2}",
            SharpeRatioText = metrics is null ? "-" : $"{metrics.SharpeRatio:F3}",
            MaxDrawdownText = metrics is null ? "-" : $"{metrics.MaxDrawdownPercent:P2}",
            NetPnlText = metrics is null ? "-" : $"{metrics.NetPnl:C2}",
            TradesText = metrics is null ? "-" : $"{metrics.TotalTrades:N0}",
            EventsText = result is null ? "-" : $"{result.TotalEventsProcessed:N0}",
            DurationText = FormatDuration(TimeSpan.FromMilliseconds(run.DurationMs)),
            ErrorMessage = run.ErrorMessage ?? string.Empty
        };
    }

    private static string FormatParameterLabel(IReadOnlyDictionary<string, object> parameters)
        => parameters.Count == 0
            ? "Base request"
            : string.Join(", ", parameters.Select(kvp => $"{kvp.Key}={FormatValue(kvp.Value)}"));

    private static string FormatValue(object value)
        => value switch
        {
            decimal decimalValue => decimalValue.ToString("0.####"),
            double doubleValue => doubleValue.ToString("0.####"),
            float floatValue => floatValue.ToString("0.####"),
            _ => value.ToString() ?? string.Empty
        };

    private static string FormatDuration(TimeSpan duration)
        => duration.TotalSeconds >= 1
            ? $"{duration.TotalSeconds:F1}s"
            : $"{duration.TotalMilliseconds:N0} ms";
}

/// <summary>
/// ViewModel for real batch backtest execution with request parameter sweeps.
/// </summary>
public sealed class BatchBacktestViewModel : BindableBase, IDisposable, IDataErrorInfo
{
    private const int MaxGeneratedRuns = 250;
    private readonly IBatchBacktestService _batchBacktestService;
    private CancellationTokenSource? _runCts;
    private bool _isDisposed;

    private string _symbolsText = "SPY,AAPL,MSFT";
    private DateTime _fromDate = DateTime.Today.AddYears(-1);
    private DateTime _toDate = DateTime.Today;
    private decimal _initialCash = 100_000m;
    private double _annualMarginRate = 0.05;
    private string _dataRoot = "./data";
    private BatchSweepParameterOption? _selectedSweepParameter;
    private decimal _sweepStart;
    private decimal _sweepStop;
    private decimal _sweepStep;
    private int _maxConcurrency = Math.Max(1, Environment.ProcessorCount - 1);
    private int _totalRuns;
    private int _completedRuns;
    private int _succeededRuns;
    private int _failedRuns;
    private double _progressPercent;
    private string _currentLabel = "Idle";
    private string _statusText = "Configure a request sweep, then start the batch.";
    private string _summaryText = "No batch run yet.";
    private string _validationSummary = string.Empty;
    private string _totalDurationText = "-";
    private bool _isRunning;

    public BatchBacktestViewModel(IBatchBacktestService batchBacktestService)
    {
        _batchBacktestService = batchBacktestService ?? throw new ArgumentNullException(nameof(batchBacktestService));

        SweepParameters =
        [
            new(nameof(BacktestRequest.InitialCash), "Initial capital", "C0", 50_000m, 250_000m, 50_000m),
            new(nameof(BacktestRequest.SlippageBasisPoints), "Slippage (bps)", "0.##", 0m, 10m, 2.5m),
            new(nameof(BacktestRequest.CommissionRate), "Commission rate", "0.####", 0m, 0.02m, 0.005m),
            new(nameof(BacktestRequest.RiskFreeRate), "Risk-free rate", "P2", 0m, 0.08m, 0.02m),
            new(nameof(BacktestRequest.MaxParticipationRate), "Max participation", "P2", 0m, 0.15m, 0.05m)
        ];

        StartBatchCommand = new AsyncRelayCommand(StartBatchAsync);
        CancelCommand = new RelayCommand(CancelBatch, () => IsRunning);
        ClearResultsCommand = new RelayCommand(ClearResults, () => !IsRunning && Results.Count > 0);

        SelectedSweepParameter = SweepParameters[0];
        UpdateValidationSummary();
    }

    public ObservableCollection<BatchSweepParameterOption> SweepParameters { get; }
    public ObservableCollection<BatchRunResultItem> Results { get; } = [];

    public IAsyncRelayCommand StartBatchCommand { get; }
    public IRelayCommand CancelCommand { get; }
    public IRelayCommand ClearResultsCommand { get; }

    public string SymbolsText
    {
        get => _symbolsText;
        set
        {
            if (SetProperty(ref _symbolsText, value))
            {
                UpdateValidationSummary();
            }
        }
    }

    public DateTime FromDate
    {
        get => _fromDate;
        set
        {
            if (SetProperty(ref _fromDate, value))
            {
                UpdateValidationSummary();
            }
        }
    }

    public DateTime ToDate
    {
        get => _toDate;
        set
        {
            if (SetProperty(ref _toDate, value))
            {
                UpdateValidationSummary();
            }
        }
    }

    public decimal InitialCash
    {
        get => _initialCash;
        set
        {
            if (SetProperty(ref _initialCash, value))
            {
                UpdateValidationSummary();
            }
        }
    }

    public double AnnualMarginRate
    {
        get => _annualMarginRate;
        set
        {
            if (SetProperty(ref _annualMarginRate, value))
            {
                UpdateValidationSummary();
            }
        }
    }

    public string DataRoot
    {
        get => _dataRoot;
        set
        {
            if (SetProperty(ref _dataRoot, value))
            {
                UpdateValidationSummary();
            }
        }
    }

    public BatchSweepParameterOption? SelectedSweepParameter
    {
        get => _selectedSweepParameter;
        set
        {
            if (SetProperty(ref _selectedSweepParameter, value) && value is not null)
            {
                SweepStart = value.DefaultStart;
                SweepStop = value.DefaultStop;
                SweepStep = value.DefaultStep;
                RaisePropertyChanged(nameof(ParameterPreviewText));
                UpdateValidationSummary();
            }
        }
    }

    public decimal SweepStart
    {
        get => _sweepStart;
        set
        {
            if (SetProperty(ref _sweepStart, value))
            {
                RaisePropertyChanged(nameof(ParameterPreviewText));
                UpdateValidationSummary();
            }
        }
    }

    public decimal SweepStop
    {
        get => _sweepStop;
        set
        {
            if (SetProperty(ref _sweepStop, value))
            {
                RaisePropertyChanged(nameof(ParameterPreviewText));
                UpdateValidationSummary();
            }
        }
    }

    public decimal SweepStep
    {
        get => _sweepStep;
        set
        {
            if (SetProperty(ref _sweepStep, value))
            {
                RaisePropertyChanged(nameof(ParameterPreviewText));
                UpdateValidationSummary();
            }
        }
    }

    public int MaxConcurrency
    {
        get => _maxConcurrency;
        set
        {
            if (SetProperty(ref _maxConcurrency, value))
            {
                UpdateValidationSummary();
            }
        }
    }

    public int TotalRuns
    {
        get => _totalRuns;
        private set
        {
            if (SetProperty(ref _totalRuns, value))
            {
                RaiseResultsStateChanged();
            }
        }
    }

    public int CompletedRuns
    {
        get => _completedRuns;
        private set
        {
            if (SetProperty(ref _completedRuns, value))
            {
                RaiseResultsStateChanged();
            }
        }
    }

    public int SucceededRuns
    {
        get => _succeededRuns;
        private set => SetProperty(ref _succeededRuns, value);
    }

    public int FailedRuns
    {
        get => _failedRuns;
        private set => SetProperty(ref _failedRuns, value);
    }

    public double ProgressPercent
    {
        get => _progressPercent;
        private set => SetProperty(ref _progressPercent, value);
    }

    public string CurrentLabel
    {
        get => _currentLabel;
        private set
        {
            if (SetProperty(ref _currentLabel, value))
            {
                RaiseResultsStateChanged();
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set
        {
            if (SetProperty(ref _statusText, value))
            {
                RaiseResultsStateChanged();
            }
        }
    }

    public string SummaryText
    {
        get => _summaryText;
        private set
        {
            if (SetProperty(ref _summaryText, value))
            {
                RaiseResultsStateChanged();
            }
        }
    }

    public string ValidationSummary
    {
        get => _validationSummary;
        private set
        {
            if (SetProperty(ref _validationSummary, value))
            {
                RaiseResultsStateChanged();
            }
        }
    }

    public string TotalDurationText
    {
        get => _totalDurationText;
        private set => SetProperty(ref _totalDurationText, value);
    }

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (SetProperty(ref _isRunning, value))
            {
                RaisePropertyChanged(nameof(CanStartBatch));
                StartBatchCommand.NotifyCanExecuteChanged();
                CancelCommand.NotifyCanExecuteChanged();
                ClearResultsCommand.NotifyCanExecuteChanged();
                RaiseResultsStateChanged();
            }
        }
    }

    public bool CanStartBatch => !IsRunning && string.IsNullOrWhiteSpace(ValidationSummary);

    public bool HasResults => Results.Count > 0;

    public bool IsResultsEmptyStateVisible => !HasResults;

    public string ResultsEmptyStateTitle
    {
        get
        {
            if (HasResults)
            {
                return FailedRuns > 0 ? "Batch results need review" : "Batch results ready";
            }

            if (IsRunning)
            {
                return "Waiting for first result";
            }

            if (StatusText.StartsWith("Batch failed:", StringComparison.Ordinal))
            {
                return "Batch did not return results";
            }

            if (string.Equals(StatusText, "Cancelled.", StringComparison.Ordinal))
            {
                return "Batch cancelled before results";
            }

            return "No batch results yet";
        }
    }

    public string ResultsEmptyStateDetail
    {
        get
        {
            if (HasResults)
            {
                return SummaryText;
            }

            if (IsRunning)
            {
                return TotalRuns <= 0
                    ? $"{CurrentLabel}. Waiting for batch progress."
                    : $"{CurrentLabel} - {CompletedRuns:N0} of {TotalRuns:N0} completed.";
            }

            if (StatusText.StartsWith("Batch failed:", StringComparison.Ordinal) ||
                string.Equals(StatusText, "Cancelled.", StringComparison.Ordinal))
            {
                return SummaryText;
            }

            if (!string.IsNullOrWhiteSpace(ValidationSummary))
            {
                return "Resolve the validation issue, then start the request sweep.";
            }

            return "Start the configured sweep to populate return, risk, trade, event, and duration columns.";
        }
    }

    public string ParameterPreviewText
    {
        get
        {
            if (SelectedSweepParameter is null)
            {
                return "No sweep parameter selected.";
            }

            var values = TryBuildSweepValues(out var sweepValues, out _)
                ? sweepValues
                : [];

            return values.Count == 0
                ? "No valid runs generated."
                : $"{values.Count} run{(values.Count == 1 ? string.Empty : "s")} from {FormatSweepValue(values[0])} to {FormatSweepValue(values[^1])}";
        }
    }

    public string Error => string.Empty;

    public string this[string columnName] => columnName switch
    {
        nameof(FromDate) or nameof(ToDate) when FromDate.Date > ToDate.Date => "Start date must be on or before end date.",
        nameof(InitialCash) when InitialCash <= 0 => "Initial capital must be greater than zero.",
        nameof(AnnualMarginRate) when AnnualMarginRate < 0 => "Annual margin rate cannot be negative.",
        nameof(DataRoot) when string.IsNullOrWhiteSpace(DataRoot) => "Data root is required.",
        nameof(SweepStep) when SweepStep <= 0 => "Sweep step must be greater than zero.",
        nameof(SweepStart) or nameof(SweepStop) when SweepStart > SweepStop => "Sweep start must be less than or equal to stop.",
        nameof(MaxConcurrency) when MaxConcurrency <= 0 => "Max concurrency must be at least one.",
        _ => string.Empty
    };

    private async Task StartBatchAsync()
    {
        UpdateValidationSummary();
        if (!CanStartBatch)
        {
            StatusText = "Resolve validation errors before starting the batch.";
            return;
        }

        var request = BuildBatchRequest();
        _runCts = new CancellationTokenSource();

        ResetRunState(request.ParameterGrid.Count);
        IsRunning = true;

        try
        {
            var progress = new Progress<BatchBacktestProgress>(ApplyProgress);
            var summary = await _batchBacktestService
                .RunBatchAsync(request, progress, _runCts.Token)
                .ConfigureAwait(true);

            ApplySummary(summary);
        }
        catch (OperationCanceledException)
        {
            StatusText = "Cancelled.";
            SummaryText = $"{CompletedRuns:N0} of {TotalRuns:N0} runs completed before cancellation.";
        }
        catch (Exception ex)
        {
            StatusText = $"Batch failed: {ex.Message}";
            SummaryText = "No completed summary was returned.";
        }
        finally
        {
            IsRunning = false;
            _runCts?.Dispose();
            _runCts = null;
        }
    }

    private void CancelBatch()
    {
        if (!IsRunning)
        {
            return;
        }

        StatusText = "Cancelling...";
        _runCts?.Cancel();
    }

    private void ClearResults()
    {
        if (IsRunning)
        {
            return;
        }

        Results.Clear();
        RaiseResultsStateChanged();
        CompletedRuns = 0;
        SucceededRuns = 0;
        FailedRuns = 0;
        ProgressPercent = 0;
        TotalDurationText = "-";
        CurrentLabel = "Idle";
        SummaryText = "No batch run yet.";
        StatusText = "Configure a request sweep, then start the batch.";
        ClearResultsCommand.NotifyCanExecuteChanged();
    }

    private BatchBacktestRequest BuildBatchRequest()
    {
        var symbols = string.IsNullOrWhiteSpace(SymbolsText)
            ? null
            : SymbolsText
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(symbol => symbol.ToUpperInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

        var baseRequest = new BacktestRequest(
            DateOnly.FromDateTime(FromDate.Date),
            DateOnly.FromDateTime(ToDate.Date),
            symbols,
            InitialCash,
            AnnualMarginRate,
            DataRoot: DataRoot.Trim());

        return new BatchBacktestRequest
        {
            BaseRequest = baseRequest,
            ParameterGrid = BuildParameterGrid(),
            MaxConcurrency = MaxConcurrency
        };
    }

    private IReadOnlyList<Dictionary<string, object>> BuildParameterGrid()
    {
        if (SelectedSweepParameter is null || !TryBuildSweepValues(out var values, out _))
        {
            return [];
        }

        return values
            .Select(value => new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                [SelectedSweepParameter.Key] = NormalizeParameterValue(SelectedSweepParameter.Key, value)
            })
            .ToArray();
    }

    private bool TryBuildSweepValues(out List<decimal> values, out string error)
    {
        values = [];
        error = string.Empty;

        if (SweepStep <= 0)
        {
            error = "Sweep step must be greater than zero.";
            return false;
        }

        if (SweepStart > SweepStop)
        {
            error = "Sweep start must be less than or equal to stop.";
            return false;
        }

        for (var value = SweepStart; value <= SweepStop; value += SweepStep)
        {
            values.Add(value);
            if (values.Count > MaxGeneratedRuns)
            {
                error = $"Sweep generates more than {MaxGeneratedRuns:N0} runs.";
                return false;
            }
        }

        if (values.Count == 0)
        {
            error = "Sweep must generate at least one run.";
            return false;
        }

        return true;
    }

    private void ResetRunState(int totalRuns)
    {
        Results.Clear();
        RaiseResultsStateChanged();
        TotalRuns = totalRuns;
        CompletedRuns = 0;
        SucceededRuns = 0;
        FailedRuns = 0;
        ProgressPercent = 0;
        CurrentLabel = "Starting...";
        StatusText = $"Running {totalRuns:N0} request sweep{(totalRuns == 1 ? string.Empty : "s")}...";
        SummaryText = "Batch in progress.";
        TotalDurationText = "-";
        ClearResultsCommand.NotifyCanExecuteChanged();
    }

    private void ApplyProgress(BatchBacktestProgress progress)
    {
        CompletedRuns = progress.Completed;
        TotalRuns = progress.Total;
        CurrentLabel = string.IsNullOrWhiteSpace(progress.CurrentLabel) ? "Running..." : progress.CurrentLabel;
        ProgressPercent = progress.Total <= 0 ? 0 : 100d * progress.Completed / progress.Total;
        StatusText = $"Running {CompletedRuns:N0}/{TotalRuns:N0}";
    }

    private void ApplySummary(BatchBacktestSummary summary)
    {
        Results.Clear();
        foreach (var run in summary.Runs.OrderBy(run => FormatParameterLabel(run.Parameters), StringComparer.OrdinalIgnoreCase))
        {
            Results.Add(BatchRunResultItem.FromRun(run));
        }

        SucceededRuns = summary.Runs.Count(run => run.Result is not null);
        FailedRuns = summary.Runs.Count - SucceededRuns;
        CompletedRuns = summary.Runs.Count;
        TotalRuns = summary.Runs.Count;
        ProgressPercent = TotalRuns <= 0 ? 0 : 100;
        CurrentLabel = "Complete";
        TotalDurationText = FormatDuration(summary.TotalDuration);
        StatusText = FailedRuns == 0 ? "Batch complete." : "Batch complete with failed runs.";
        SummaryText = $"{SucceededRuns:N0} succeeded, {FailedRuns:N0} failed, {TotalDurationText} total.";
        RaiseResultsStateChanged();
        ClearResultsCommand.NotifyCanExecuteChanged();
    }

    private void UpdateValidationSummary()
    {
        var errors = new[]
        {
            this[nameof(FromDate)],
            this[nameof(InitialCash)],
            this[nameof(AnnualMarginRate)],
            this[nameof(DataRoot)],
            this[nameof(SweepStart)],
            this[nameof(SweepStep)],
            this[nameof(MaxConcurrency)],
            TryBuildSweepValues(out _, out var sweepError) ? string.Empty : sweepError
        };

        ValidationSummary = string.Join(" ", errors.Where(error => !string.IsNullOrWhiteSpace(error)).Distinct());
        RaisePropertyChanged(nameof(CanStartBatch));
        StartBatchCommand.NotifyCanExecuteChanged();
    }

    private void RaiseResultsStateChanged()
    {
        RaisePropertyChanged(nameof(HasResults));
        RaisePropertyChanged(nameof(IsResultsEmptyStateVisible));
        RaisePropertyChanged(nameof(ResultsEmptyStateTitle));
        RaisePropertyChanged(nameof(ResultsEmptyStateDetail));
    }

    private string FormatSweepValue(decimal value)
        => SelectedSweepParameter?.Format switch
        {
            "C0" => value.ToString("C0"),
            "P2" => value.ToString("P2"),
            "0.##" => value.ToString("0.##"),
            "0.####" => value.ToString("0.####"),
            _ => value.ToString("0.####")
        };

    private static object NormalizeParameterValue(string key, decimal value)
        => key switch
        {
            nameof(BacktestRequest.AnnualMarginRate) or
            nameof(BacktestRequest.AnnualShortRebateRate) or
            nameof(BacktestRequest.RiskFreeRate) => decimal.ToDouble(value),
            _ => value
        };

    private static string FormatParameterLabel(IReadOnlyDictionary<string, object> parameters)
        => parameters.Count == 0
            ? "Base request"
            : string.Join(", ", parameters.Select(kvp => $"{kvp.Key}={kvp.Value}"));

    private static string FormatDuration(TimeSpan duration)
        => duration.TotalMinutes >= 1
            ? $"{duration.TotalMinutes:F1}m"
            : duration.TotalSeconds >= 1
                ? $"{duration.TotalSeconds:F1}s"
                : $"{duration.TotalMilliseconds:N0} ms";

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _runCts?.Cancel();
        _runCts?.Dispose();
    }
}
