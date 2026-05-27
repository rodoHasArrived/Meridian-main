using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Meridian.Contracts.Workstation;
using Meridian.Strategies.Services;
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
    private readonly StrategyRunContinuityService? _continuityService;
    private string? _runId;
    private object? _parameter;
    private CashFlowEntryDto? _selectedEntry;

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

    public CashFlowEntryDto? SelectedEntry
    {
        get => _selectedEntry;
        set
        {
            if (SetProperty(ref _selectedEntry, value))
            {
                OpenSelectedSecurityCommand.NotifyCanExecuteChanged();
            }
        }
    }

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
        private set
        {
            if (SetProperty(ref _statusText, value))
            {
                RaiseCashFlowStateChanged();
            }
        }
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

    private string _continuityPostureText = "Shared continuity appears after a retained run is selected.";
    public string ContinuityPostureText
    {
        get => _continuityPostureText;
        private set => SetProperty(ref _continuityPostureText, value);
    }

    private string _continuityDetailText = "Select a run to compare portfolio, ledger, cash-flow, reconciliation, and warning posture from the shared continuity model.";
    public string ContinuityDetailText
    {
        get => _continuityDetailText;
        private set => SetProperty(ref _continuityDetailText, value);
    }

    private string _continuityWarningText = "No shared continuity payload is loaded.";
    public string ContinuityWarningText
    {
        get => _continuityWarningText;
        private set => SetProperty(ref _continuityWarningText, value);
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
    public IRelayCommand OpenSelectedSecurityCommand { get; }

    public bool HasEntries => Entries.Count > 0;

    public bool HasLadderBuckets => LadderBuckets.Count > 0;

    public bool IsEntriesEmptyStateVisible => !HasEntries;

    public bool IsLadderEmptyStateVisible => !HasLadderBuckets;

    public string EntriesEmptyStateTitle
    {
        get
        {
            if (HasEntries)
            {
                return "Cash-flow events loaded";
            }

            if (string.IsNullOrWhiteSpace(_runId))
            {
                return StatusText.StartsWith("No cash flow data", StringComparison.Ordinal)
                    ? "Cash-flow data unavailable"
                    : "Select a run to inspect cash flows";
            }

            return "No cash-flow events recorded";
        }
    }

    public string EntriesEmptyStateDetail
    {
        get
        {
            if (HasEntries)
            {
                return StatusText;
            }

            if (StatusText.StartsWith("No cash flow data", StringComparison.Ordinal))
            {
                return StatusText;
            }

            if (string.IsNullOrWhiteSpace(_runId))
            {
                return "Open a strategy run from the run browser to review cash movements by timestamp, account, symbol, and event type.";
            }

            return "The selected run has no trade, commission, dividend, funding, or other cash-flow entries in the retained result.";
        }
    }

    public string LadderEmptyStateTitle
        => HasLadderBuckets ? "Cash ladder loaded" : "No cash ladder buckets";

    public string LadderEmptyStateDetail
    {
        get
        {
            if (HasLadderBuckets)
            {
                return BucketSummaryText;
            }

            if (StatusText.StartsWith("No cash flow data", StringComparison.Ordinal))
            {
                return "Load a retained run with cash-flow evidence before reviewing projected inflow and outflow buckets.";
            }

            if (string.IsNullOrWhiteSpace(_runId))
            {
                return "Select a strategy run to build the cash ladder from retained cash-flow events.";
            }

            if (HasEntries)
            {
                return "Cash-flow events are loaded, but no ladder buckets were generated for the retained run.";
            }

            return "No time buckets were generated because the selected run has no retained cash-flow events.";
        }
    }

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
        NavigationService navigationService,
        StrategyRunContinuityService? continuityService = null)
    {
        _runService = runService ?? throw new ArgumentNullException(nameof(runService));
        _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
        _continuityService = continuityService;
        OpenBrowserCommand = new RelayCommand(() => _navigationService.NavigateTo("StrategyRuns"));
        OpenRunDetailCommand = new RelayCommand(OpenRunDetail, () => !string.IsNullOrWhiteSpace(_runId));
        OpenPortfolioCommand = new RelayCommand(OpenPortfolio, () => !string.IsNullOrWhiteSpace(_runId));
        OpenLedgerCommand = new RelayCommand(OpenLedger, () => !string.IsNullOrWhiteSpace(_runId));
        OpenSelectedSecurityCommand = new RelayCommand(OpenSelectedSecurity, () => !string.IsNullOrWhiteSpace(SelectedEntry?.Symbol));
    }

    private async Task LoadFromParameterAsync(object? parameter, CancellationToken ct = default)
    {
        var runId = parameter as string;
        if (string.IsNullOrWhiteSpace(runId))
        {
            ResetLoadedState("Select a strategy run to inspect its cash flows.");
            return;
        }

        var summaryTask = _runService.GetCashFlowAsync(runId, ct: ct);
        var continuityTask = _continuityService?.GetRunContinuityAsync(runId, ct) ??
                             Task.FromResult<StrategyRunContinuityDetail?>(null);

        await Task.WhenAll(summaryTask, continuityTask).ConfigureAwait(false);

        var summary = await summaryTask.ConfigureAwait(false);
        var continuity = await continuityTask.ConfigureAwait(false);
        if (summary is null)
        {
            ResetLoadedState($"No cash flow data is available for run '{runId}'.");
            ApplyContinuity(null);
            return;
        }

        ApplySummary(summary, continuity);
    }

    private void ApplySummary(RunCashFlowSummary summary, StrategyRunContinuityDetail? continuity)
    {
        _runId = summary.RunId;
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

        SelectedEntry = Entries.FirstOrDefault(entry => !string.IsNullOrWhiteSpace(entry.Symbol));

        LadderBuckets.Clear();
        foreach (var bucket in summary.Ladder.Buckets)
        {
            LadderBuckets.Add(bucket);
        }

        ApplyContinuity(continuity);
        OpenRunDetailCommand.NotifyCanExecuteChanged();
        OpenPortfolioCommand.NotifyCanExecuteChanged();
        OpenLedgerCommand.NotifyCanExecuteChanged();
        OpenSelectedSecurityCommand.NotifyCanExecuteChanged();
        RaiseCashFlowStateChanged();
    }

    private void ResetLoadedState(string statusText)
    {
        _runId = null;
        Title = "Cash Flow";
        AsOfText = "-";
        TotalEntriesText = "-";
        TotalInflowsText = "-";
        TotalOutflowsText = "-";
        NetCashFlowText = "-";
        BucketSummaryText = "-";
        Entries.Clear();
        LadderBuckets.Clear();
        SelectedEntry = null;
        StatusText = statusText;
        ResetContinuityState();
        OpenRunDetailCommand.NotifyCanExecuteChanged();
        OpenPortfolioCommand.NotifyCanExecuteChanged();
        OpenLedgerCommand.NotifyCanExecuteChanged();
        OpenSelectedSecurityCommand.NotifyCanExecuteChanged();
        RaiseCashFlowStateChanged();
    }

    private void RaiseCashFlowStateChanged()
    {
        RaisePropertyChanged(nameof(HasEntries));
        RaisePropertyChanged(nameof(HasLadderBuckets));
        RaisePropertyChanged(nameof(IsEntriesEmptyStateVisible));
        RaisePropertyChanged(nameof(IsLadderEmptyStateVisible));
        RaisePropertyChanged(nameof(EntriesEmptyStateTitle));
        RaisePropertyChanged(nameof(EntriesEmptyStateDetail));
        RaisePropertyChanged(nameof(LadderEmptyStateTitle));
        RaisePropertyChanged(nameof(LadderEmptyStateDetail));
        RaisePropertyChanged(nameof(ContinuityPostureText));
        RaisePropertyChanged(nameof(ContinuityDetailText));
        RaisePropertyChanged(nameof(ContinuityWarningText));
    }

    private void ApplyContinuity(StrategyRunContinuityDetail? continuity)
    {
        if (continuity is null)
        {
            ContinuityPostureText = _continuityService is null
                ? "Shared continuity is not connected in this desktop context."
                : "Shared continuity is unavailable for this run.";
            ContinuityDetailText = _continuityService is null
                ? "Open the WPF shell through the hosted app so the registered continuity service can compare portfolio, ledger, cash-flow, and reconciliation seams."
                : "The shared continuity service did not return canonical run, portfolio, ledger, cash-flow, and reconciliation posture for this run.";
            ContinuityWarningText = "No continuity warnings were returned.";
            return;
        }

        var status = continuity.ContinuityStatus;
        ContinuityPostureText = BuildContinuityPostureText(status);
        ContinuityDetailText =
            $"Run {FormatSeamHealth(status.RunHealth)}; portfolio {FormatSeamHealth(status.PortfolioHealth)}; ledger {FormatSeamHealth(status.LedgerHealth)}; cash flow {FormatSeamHealth(status.CashFlowHealth)}; reconciliation {FormatSeamHealth(status.ReconciliationHealth)}.";
        ContinuityWarningText = BuildContinuityWarningText(status);
    }

    private void ResetContinuityState()
    {
        ContinuityPostureText = "Shared continuity appears after a retained run is selected.";
        ContinuityDetailText = "Select a run to compare portfolio, ledger, cash-flow, reconciliation, and warning posture from the shared continuity model.";
        ContinuityWarningText = "No shared continuity payload is loaded.";
    }

    private static string BuildContinuityPostureText(StrategyRunContinuityStatus status)
    {
        if (status.Warnings.Any(static warning => warning.Severity == StrategyRunContinuityWarningSeverity.Critical) ||
            status.OpenReconciliationBreaks > 0)
        {
            return "Continuity blocked";
        }

        if (status.HasWarnings)
        {
            return "Continuity needs review";
        }

        return status.HasRun &&
               status.HasPortfolio &&
               status.HasLedger &&
               status.HasCashFlow &&
               status.HasReconciliation
            ? "Continuity ready"
            : "Continuity incomplete";
    }

    private static string BuildContinuityWarningText(StrategyRunContinuityStatus status)
    {
        var warning = status.Warnings
            .OrderByDescending(static item => item.Severity)
            .FirstOrDefault();

        return warning is null
            ? "No continuity warnings were returned."
            : $"{warning.Code}: {warning.Message}";
    }

    private static string FormatSeamHealth(StrategyRunContinuitySeamHealthStatus status)
        => status switch
        {
            StrategyRunContinuitySeamHealthStatus.Healthy => "Healthy",
            StrategyRunContinuitySeamHealthStatus.Missing => "Missing",
            StrategyRunContinuitySeamHealthStatus.Stale => "Stale",
            _ => status.ToString()
        };

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

    private void OpenSelectedSecurity()
    {
        if (!string.IsNullOrWhiteSpace(SelectedEntry?.Symbol))
        {
            _navigationService.NavigateTo("SecurityMaster", SelectedEntry.Symbol);
        }
    }
}
