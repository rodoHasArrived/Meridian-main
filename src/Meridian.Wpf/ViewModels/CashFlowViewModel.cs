using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Meridian.Contracts.Workstation;
using Meridian.Wpf.Services;

namespace Meridian.Wpf.ViewModels;

/// <summary>
/// View model for the cash-flow projection drill-in page.
/// Loads a <see cref="RunCashFlowSummary"/> for a strategy run and exposes
/// both the raw entry list and the time-bucketed cash ladder to the view.
/// </summary>
public sealed class CashFlowViewModel : BindableBase
{
    private readonly StrategyRunWorkspaceService _runService;
    private readonly NavigationService _navigationService;
    private string? _runId;
    private object? _parameter;

    public object? Parameter
    {
        get => _parameter;
        set
        {
            if (SetProperty(ref _parameter, value))
            {
                _ = LoadFromParameterAsync(value);
            }
        }
    }

    public ObservableCollection<CashFlowEntryDto> Entries { get; } = [];
    public ObservableCollection<CashLadderBucketDto> LadderBuckets { get; } = [];

    private string _title = "Cash Flow";
    public string Title
    {
        get => _title;
        private set => SetProperty(ref _title, value);
    }

    private string _statusText = "Select a strategy run to inspect its cash flows.";
    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    private string _asOfText = "-";
    public string AsOfText
    {
        get => _asOfText;
        private set => SetProperty(ref _asOfText, value);
    }

    private string _totalEntriesText = "-";
    public string TotalEntriesText
    {
        get => _totalEntriesText;
        private set => SetProperty(ref _totalEntriesText, value);
    }

    private string _totalInflowsText = "-";
    public string TotalInflowsText
    {
        get => _totalInflowsText;
        private set => SetProperty(ref _totalInflowsText, value);
    }

    private string _totalOutflowsText = "-";
    public string TotalOutflowsText
    {
        get => _totalOutflowsText;
        private set => SetProperty(ref _totalOutflowsText, value);
    }

    private string _netCashFlowText = "-";
    public string NetCashFlowText
    {
        get => _netCashFlowText;
        private set => SetProperty(ref _netCashFlowText, value);
    }

    private string _bucketSummaryText = "-";
    public string BucketSummaryText
    {
        get => _bucketSummaryText;
        private set => SetProperty(ref _bucketSummaryText, value);
    }

    public IRelayCommand OpenBrowserCommand { get; }
    public IRelayCommand OpenRunDetailCommand { get; }
    public IRelayCommand OpenPortfolioCommand { get; }
    public IRelayCommand OpenLedgerCommand { get; }

    /// <summary>
    /// Parameterless constructor for use in XAML code-behind; resolves
    /// dependencies from the static singleton instances.
    /// </summary>
    public CashFlowViewModel()
        : this(StrategyRunWorkspaceService.Instance, NavigationService.Instance)
    {
    }

    internal CashFlowViewModel(
        StrategyRunWorkspaceService runService,
        NavigationService navigationService)
    {
        _runService = runService ?? throw new ArgumentNullException(nameof(runService));
        _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
        OpenBrowserCommand = new RelayCommand(() => _navigationService.NavigateTo("StrategyRuns"));
        OpenRunDetailCommand = new RelayCommand(OpenRunDetail, () => !string.IsNullOrWhiteSpace(_runId));
        OpenPortfolioCommand = new RelayCommand(OpenPortfolio, () => !string.IsNullOrWhiteSpace(_runId));
        OpenLedgerCommand = new RelayCommand(OpenLedger, () => !string.IsNullOrWhiteSpace(_runId));
    }

    private async Task LoadFromParameterAsync(object? parameter, CancellationToken ct = default)
    {
        var runId = parameter as string;
        if (string.IsNullOrWhiteSpace(runId))
        {
            StatusText = "Select a strategy run to inspect its cash flows.";
            return;
        }

        _runId = runId;
        var summary = await _runService.GetCashFlowAsync(runId, ct: ct).ConfigureAwait(false);
        if (summary is null)
        {
            StatusText = $"No cash flow data is available for run '{runId}'.";
            return;
        }

        ApplySummary(summary);
    }

    private void ApplySummary(RunCashFlowSummary summary)
    {
        Title = $"Cash Flow ({summary.RunId[..Math.Min(8, summary.RunId.Length)]})";
        AsOfText = summary.AsOf.LocalDateTime.ToString("g");
        TotalEntriesText = summary.TotalEntries.ToString("N0");
        TotalInflowsText = summary.TotalInflows.ToString("C2");
        TotalOutflowsText = summary.TotalOutflows.ToString("C2");
        NetCashFlowText = summary.NetCashFlow.ToString("C2");
        BucketSummaryText = $"{summary.Ladder.Buckets.Count} bucket(s) × {summary.Ladder.BucketDays}d · {summary.Currency}";
        StatusText = $"{summary.TotalEntries} cash-flow event(s) loaded. Net position: {summary.NetCashFlow:C2}.";

        Entries.Clear();
        foreach (var entry in summary.Entries)
        {
            Entries.Add(entry);
        }

        LadderBuckets.Clear();
        foreach (var bucket in summary.Ladder.Buckets)
        {
            LadderBuckets.Add(bucket);
        }

        OpenRunDetailCommand.NotifyCanExecuteChanged();
        OpenPortfolioCommand.NotifyCanExecuteChanged();
        OpenLedgerCommand.NotifyCanExecuteChanged();
    }

    private void OpenRunDetail()
    {
        if (!string.IsNullOrWhiteSpace(_runId))
        {
            _navigationService.NavigateTo("RunDetail", _runId);
        }
    }

    private void OpenPortfolio()
    {
        if (!string.IsNullOrWhiteSpace(_runId))
        {
            _navigationService.NavigateTo("RunPortfolio", _runId);
        }
    }

    private void OpenLedger()
    {
        if (!string.IsNullOrWhiteSpace(_runId))
        {
            _navigationService.NavigateTo("RunLedger", _runId);
        }
    }
}
