using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.Input;
using Meridian.Contracts.Workstation;
using Meridian.Wpf.Services;

namespace Meridian.Wpf.ViewModels;

/// <summary>A single row in the comparison diff table.</summary>
public sealed record ComparisonRow(string Label, string ValueA, string ValueB);

public sealed class StrategyRunBrowserViewModel : BindableBase
{
    private readonly StrategyRunWorkspaceService _runService;
    private readonly NavigationService _navigationService;
    private readonly WorkspaceService _workspaceService;
    private readonly List<StrategyRunSummary> _allRuns = new();

    public ObservableCollection<StrategyRunSummary> Runs { get; } = [];
    public IReadOnlyList<string> ModeFilters { get; } = ["All", "Backtest", "Paper", "Live"];

    private StrategyRunSummary? _selectedRun;
    public StrategyRunSummary? SelectedRun
    {
        get => _selectedRun;
        set
        {
            if (SetProperty(ref _selectedRun, value))
            {
                RaisePropertyChanged(nameof(CanOpenSelectedRun));
                RaisePropertyChanged(nameof(CanCompareRuns));
                NotifyCommandsChanged();
            }
        }
    }

    private StrategyRunSummary? _comparisonRun;
    /// <summary>
    /// The second run selected for cross-mode comparison.
    /// When both <see cref="SelectedRun"/> and this are set, <see cref="CompareRunsCommand"/> is enabled.
    /// </summary>
    public StrategyRunSummary? ComparisonRun
    {
        get => _comparisonRun;
        set
        {
            if (SetProperty(ref _comparisonRun, value))
            {
                RaisePropertyChanged(nameof(CanCompareRuns));
                NotifyCommandsChanged();
            }
        }
    }

    private IReadOnlyList<RunComparisonDto>? _comparisonResult;
    /// <summary>Populated after <see cref="CompareRunsCommand"/> completes.</summary>
    public IReadOnlyList<RunComparisonDto>? ComparisonResult
    {
        get => _comparisonResult;
        private set
        {
            if (SetProperty(ref _comparisonResult, value))
            {
                RaisePropertyChanged(nameof(IsComparisonVisible));
                RaisePropertyChanged(nameof(ComparisonRows));
            }
        }
    }

    /// <summary><c>true</c> when a comparison result is available to display.</summary>
    public bool IsComparisonVisible => ComparisonResult is { Count: > 0 };

    /// <summary>Flat metric rows suitable for binding in the comparison diff panel.</summary>
    public IReadOnlyList<ComparisonRow> ComparisonRows
    {
        get
        {
            if (ComparisonResult is not { Count: >= 2 })
            {
                return Array.Empty<ComparisonRow>();
            }

            var a = ComparisonResult[0];
            var b = ComparisonResult[1];

            return
            [
                new ComparisonRow("Strategy",        a.StrategyName,                                    b.StrategyName),
                new ComparisonRow("Mode",             a.Mode.ToString(),                                 b.Mode.ToString()),
                new ComparisonRow("Net PnL",          FormatDecimal(a.NetPnl, "C2"),                    FormatDecimal(b.NetPnl, "C2")),
                new ComparisonRow("Total Return",     FormatDecimal(a.TotalReturn, "P2"),               FormatDecimal(b.TotalReturn, "P2")),
                new ComparisonRow("Ann. Return",      FormatDecimal(a.AnnualizedReturn, "P2"),          FormatDecimal(b.AnnualizedReturn, "P2")),
                new ComparisonRow("Final Equity",     FormatDecimal(a.FinalEquity, "C2"),               FormatDecimal(b.FinalEquity, "C2")),
                new ComparisonRow("Sharpe",           FormatDouble(a.SharpeRatio, "F3"),                FormatDouble(b.SharpeRatio, "F3")),
                new ComparisonRow("Sortino",          FormatDouble(a.SortinoRatio, "F3"),               FormatDouble(b.SortinoRatio, "F3")),
                new ComparisonRow("Calmar",           FormatDouble(a.CalmarRatio, "F3"),                FormatDouble(b.CalmarRatio, "F3")),
                new ComparisonRow("Max Drawdown",     FormatDecimal(a.MaxDrawdown, "C2"),               FormatDecimal(b.MaxDrawdown, "C2")),
                new ComparisonRow("Max DD %",         FormatDecimal(a.MaxDrawdownPercent, "P2"),        FormatDecimal(b.MaxDrawdownPercent, "P2")),
                new ComparisonRow("DD Recovery",      $"{a.MaxDrawdownRecoveryDays}d",                  $"{b.MaxDrawdownRecoveryDays}d"),
                new ComparisonRow("Profit Factor",    FormatDouble(a.ProfitFactor, "F2"),               FormatDouble(b.ProfitFactor, "F2")),
                new ComparisonRow("Win Rate",         FormatDouble(a.WinRate, "P1"),                    FormatDouble(b.WinRate, "P1")),
                new ComparisonRow("Total Trades",     a.TotalTrades.ToString(),                         b.TotalTrades.ToString()),
                new ComparisonRow("Fills",            a.FillCount.ToString(),                           b.FillCount.ToString()),
                new ComparisonRow("Commissions",      FormatDecimal(a.TotalCommissions, "C2"),          FormatDecimal(b.TotalCommissions, "C2")),
                new ComparisonRow("XIRR",             FormatDouble(a.Xirr, "P2"),                       FormatDouble(b.Xirr, "P2")),
                new ComparisonRow("Parent Run",       a.ParentRunId ?? "—",                             b.ParentRunId ?? "—"),
            ];
        }
    }

    private static string FormatDecimal(decimal? value, string format) =>
        value.HasValue ? value.Value.ToString(format) : "—";

    private static string FormatDouble(double? value, string format) =>
        value.HasValue ? value.Value.ToString(format) : "—";

    private string _searchText = string.Empty;
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                _workspaceService.UpdatePageFilterState("StrategyRuns", "SearchText", value);
                ApplyFilters();
            }
        }
    }

    private string _selectedModeFilter = "All";
    public string SelectedModeFilter
    {
        get => _selectedModeFilter;
        set
        {
            if (SetProperty(ref _selectedModeFilter, value))
            {
                _workspaceService.UpdatePageFilterState("StrategyRuns", "ModeFilter", value);
                ApplyFilters();
            }
        }
    }

    private string _statusText = "Loading strategy runs...";
    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public bool CanOpenSelectedRun => SelectedRun is not null;

    /// <summary><c>true</c> when two distinct runs are selected and ready to compare.</summary>
    public bool CanCompareRuns =>
        SelectedRun is not null &&
        ComparisonRun is not null &&
        !string.Equals(SelectedRun.RunId, ComparisonRun.RunId, StringComparison.Ordinal);

    public IAsyncRelayCommand RefreshCommand { get; }
    public IRelayCommand OpenDetailCommand { get; }
    public IRelayCommand OpenPortfolioCommand { get; }
    public IRelayCommand OpenLedgerCommand { get; }
    public IAsyncRelayCommand CompareRunsCommand { get; }
    public IRelayCommand ClearComparisonCommand { get; }

    internal StrategyRunBrowserViewModel(
        StrategyRunWorkspaceService runService,
        NavigationService navigationService,
        WorkspaceService workspaceService)
    {
        _runService = runService;
        _navigationService = navigationService;
        _workspaceService = workspaceService;

        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        OpenDetailCommand = new RelayCommand(OpenDetail, () => CanOpenSelectedRun);
        OpenPortfolioCommand = new RelayCommand(OpenPortfolio, () => CanOpenSelectedRun);
        OpenLedgerCommand = new RelayCommand(OpenLedger, () => CanOpenSelectedRun);
        CompareRunsCommand = new AsyncRelayCommand(ExecuteCompareAsync, () => CanCompareRuns);
        ClearComparisonCommand = new RelayCommand(ClearComparison);
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        SearchText = _workspaceService.GetPageFilterState("StrategyRuns", "SearchText") ?? string.Empty;
        SelectedModeFilter = _workspaceService.GetPageFilterState("StrategyRuns", "ModeFilter") ?? "All";
        await RefreshAsync(ct);
    }

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        var runs = await _runService.GetRunsAsync(null, ct);

        _allRuns.Clear();
        _allRuns.AddRange(runs);

        ApplyFilters();
    }

    private void ApplyFilters()
    {
        IEnumerable<StrategyRunSummary> filtered = _allRuns;

        if (!string.Equals(SelectedModeFilter, "All", StringComparison.OrdinalIgnoreCase) &&
            Enum.TryParse<StrategyRunMode>(SelectedModeFilter, ignoreCase: true, out var mode))
        {
            filtered = filtered.Where(run => run.Mode == mode);
        }

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var query = SearchText.Trim();
            filtered = filtered.Where(run =>
                run.RunId.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                run.StrategyId.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                run.StrategyName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                (run.PortfolioId?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        Runs.Clear();
        foreach (var run in filtered.OrderByDescending(static run => run.StartedAt))
        {
            Runs.Add(run);
        }

        SelectedRun = Runs.FirstOrDefault();
        StatusText = Runs.Count switch
        {
            0 when _allRuns.Count == 0 => "No recorded strategy runs yet. Complete a backtest to populate this browser.",
            0 => "No strategy runs match the current filters.",
            _ => $"{Runs.Count} strategy run{(Runs.Count == 1 ? string.Empty : "s")} loaded."
        };
    }

    private async Task ExecuteCompareAsync(CancellationToken ct = default)
    {
        if (SelectedRun is null || ComparisonRun is null)
        {
            return;
        }

        ComparisonResult = null;
        StatusText = "Comparing runs...";

        var result = await _runService.CompareRunsAsync(
            [SelectedRun.RunId, ComparisonRun.RunId], ct);

        ComparisonResult = result;
        StatusText = result.Count >= 2
            ? $"Showing comparison for {result[0].StrategyName} vs {result[1].StrategyName}."
            : "Comparison complete.";
    }

    private void ClearComparison()
    {
        ComparisonRun = null;
        ComparisonResult = null;
        StatusText = Runs.Count > 0
            ? $"{Runs.Count} strategy run{(Runs.Count == 1 ? string.Empty : "s")} loaded."
            : StatusText;
    }

    private void OpenDetail()
    {
        if (SelectedRun is not null)
        {
            _navigationService.NavigateTo("RunDetail", SelectedRun.RunId);
        }
    }

    private void OpenPortfolio()
    {
        if (SelectedRun is not null)
        {
            _navigationService.NavigateTo("RunPortfolio", SelectedRun.RunId);
        }
    }

    private void OpenLedger()
    {
        if (SelectedRun is not null)
        {
            _navigationService.NavigateTo("RunLedger", SelectedRun.RunId);
        }
    }

    private void NotifyCommandsChanged()
    {
        OpenDetailCommand.NotifyCanExecuteChanged();
        OpenPortfolioCommand.NotifyCanExecuteChanged();
        OpenLedgerCommand.NotifyCanExecuteChanged();
        (CompareRunsCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
    }
}
