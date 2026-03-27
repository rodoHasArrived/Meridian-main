using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using Meridian.Backtesting;
using Meridian.Backtesting.Sdk;

namespace Meridian.Wpf.ViewModels;

/// <summary>
/// Result item for display in batch backtest results grid.
/// </summary>
public sealed class BatchRunResultItem : BindableBase
{
    private string _parametersSummary = string.Empty;
    public string ParametersSummary
    {
        get => _parametersSummary;
        set => SetProperty(ref _parametersSummary, value);
    }

    private double _sharpeRatio;
    public double SharpeRatio
    {
        get => _sharpeRatio;
        set => SetProperty(ref _sharpeRatio, value);
    }

    private double _totalReturnPct;
    public double TotalReturnPct
    {
        get => _totalReturnPct;
        set => SetProperty(ref _totalReturnPct, value);
    }

    private double _maxDrawdownPct;
    public double MaxDrawdownPct
    {
        get => _maxDrawdownPct;
        set => SetProperty(ref _maxDrawdownPct, value);
    }

    private long _durationMs;
    public long DurationMs
    {
        get => _durationMs;
        set => SetProperty(ref _durationMs, value);
    }

    private bool _hasError;
    public bool HasError
    {
        get => _hasError;
        set => SetProperty(ref _hasError, value);
    }

    private string _errorMessage = string.Empty;
    public string ErrorMessage
    {
        get => _errorMessage;
        set => SetProperty(ref _errorMessage, value);
    }
}

/// <summary>
/// ViewModel for batch backtest execution with parameter grid sweeping.
/// Manages concurrent backtest runs and displays progress and results in the WPF UI.
/// </summary>
public sealed class BatchBacktestViewModel : BindableBase, IDisposable
{
    private CancellationTokenSource? _cancellationTokenSource;

    public BatchBacktestViewModel()
    {
        StartBatchCommand = new RelayCommand(StartBatch, CanStartBatch);
        CancelCommand = new RelayCommand(CancelBatch, CanCancel);
    }

    // ── Progress properties ──────────────────────────────────────────────────

    private int _totalRuns;
    public int TotalRuns
    {
        get => _totalRuns;
        set => SetProperty(ref _totalRuns, value);
    }

    private int _completedRuns;
    public int CompletedRuns
    {
        get => _completedRuns;
        set => SetProperty(ref _completedRuns, value);
    }

    private double _progressPercent;
    public double ProgressPercent
    {
        get => _progressPercent;
        set => SetProperty(ref _progressPercent, value);
    }

    private string _currentLabel = string.Empty;
    public string CurrentLabel
    {
        get => _currentLabel;
        set => SetProperty(ref _currentLabel, value);
    }

    private bool _isRunning;
    public bool IsRunning
    {
        get => _isRunning;
        set
        {
            if (SetProperty(ref _isRunning, value))
            {
                (StartBatchCommand as RelayCommand)?.NotifyCanExecuteChanged();
                (CancelCommand as RelayCommand)?.NotifyCanExecuteChanged();
            }
        }
    }

    // ── Results collection ───────────────────────────────────────────────────

    public ObservableCollection<BatchRunResultItem> Results { get; } = [];

    // ── Commands ─────────────────────────────────────────────────────────────

    public ICommand StartBatchCommand { get; }
    public ICommand CancelCommand { get; }

    private bool CanStartBatch() => !_isRunning;
    private bool CanCancel() => _isRunning;

    private void StartBatch()
    {
        _cancellationTokenSource = new CancellationTokenSource();
        _ = RunBatchAsync(_cancellationTokenSource.Token);
    }

    private void CancelBatch()
    {
        _cancellationTokenSource?.Cancel();
    }

    /// <summary>
    /// Executes the batch backtest with a sample 3x3 parameter grid.
    /// </summary>
    private async Task RunBatchAsync(CancellationToken ct)
    {
        IsRunning = true;
        CompletedRuns = 0;
        ProgressPercent = 0;
        Results.Clear();

        try
        {
            // Build sample 3x3 parameter grid
            var parameterGrid = GenerateSampleParameterGrid();
            TotalRuns = parameterGrid.Count;

            // For demo purposes, simulate backtest runs without actual BacktestEngine
            // In production, inject IBatchBacktestService and use it
            await SimulateBatchRunAsync(parameterGrid, ct);
        }
        catch (OperationCanceledException)
        {
            // User cancelled
        }
        catch (Exception)
        {
            // Error occurred during batch
        }
        finally
        {
            IsRunning = false;
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
        }
    }

    /// <summary>
    /// Simulates batch backtest execution for demo purposes.
    /// In production, this would call IBatchBacktestService.RunBatchAsync.
    /// </summary>
    private async Task SimulateBatchRunAsync(List<Dictionary<string, object>> parameterGrid, CancellationToken ct)
    {
        var progress = new Progress<BatchBacktestProgress>(p =>
        {
            Application.Current.Dispatcher.InvokeAsync(() =>
            {
                CompletedRuns = p.Completed;
                CurrentLabel = p.CurrentLabel;
                ProgressPercent = (p.Total > 0) ? (100.0 * p.Completed / p.Total) : 0;
            });
        });

        foreach (var (index, paramSet) in parameterGrid.WithIndex())
        {
            ct.ThrowIfCancellationRequested();

            var label = FormatParameterLabel(paramSet);
            var completedBefore = CompletedRuns;

            Application.Current.Dispatcher.InvokeAsync(() =>
            {
                CurrentLabel = label;
            });

            // Simulate backtest execution
            await Task.Delay(Random.Shared.Next(500, 2000), ct);

            // Simulate result
            var hasError = index % 5 == 0; // 20% error rate for demo
            var item = new BatchRunResultItem
            {
                ParametersSummary = label,
                SharpeRatio = hasError ? 0 : Random.Shared.NextDouble() * 3,
                TotalReturnPct = hasError ? 0 : (Random.Shared.NextDouble() - 0.5) * 100,
                MaxDrawdownPct = hasError ? 0 : Math.Abs(Random.Shared.NextDouble() * -50),
                DurationMs = Random.Shared.Next(500, 2000),
                HasError = hasError,
                ErrorMessage = hasError ? "Simulated error for demo" : string.Empty
            };

            Application.Current.Dispatcher.InvokeAsync(() =>
            {
                Results.Add(item);
                CompletedRuns = completedBefore + 1;
                ProgressPercent = (100.0 * CompletedRuns / TotalRuns);
            });
        }
    }

    /// <summary>
    /// Generates a sample 3x3 parameter grid for demonstration.
    /// </summary>
    private static List<Dictionary<string, object>> GenerateSampleParameterGrid()
    {
        var grid = new List<Dictionary<string, object>>();

        var lookbackPeriods = new[] { 10, 20, 30 };
        var thresholdValues = new[] { 0.5, 1.0, 1.5 };

        foreach (var lookback in lookbackPeriods)
        {
            foreach (var threshold in thresholdValues)
            {
                grid.Add(new Dictionary<string, object>
                {
                    { "LookbackPeriod", lookback },
                    { "SignalThreshold", threshold }
                });
            }
        }

        return grid;
    }

    private static string FormatParameterLabel(Dictionary<string, object> paramSet)
    {
        var parts = paramSet.Select(kvp => $"{kvp.Key}={kvp.Value}");
        return string.Join(", ", parts);
    }

    public void Dispose()
    {
        _cancellationTokenSource?.Dispose();
    }
}

/// <summary>Extension method for enumerating with index.</summary>
internal static class EnumerableExtensions
{
    public static IEnumerable<(int, T)> WithIndex<T>(this IEnumerable<T> source)
    {
        var index = 0;
        foreach (var item in source)
        {
            yield return (index++, item);
        }
    }
}
