using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Meridian.Contracts.Workstation;
using Meridian.Strategies.Services;
using Meridian.Wpf.Models;
using Meridian.Wpf.Services;
using Meridian.Wpf.Workstation.Models;

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
    private CashLadderBucketDto? _selectedLadderBucket;
    private InspectorPanelModel _selectedEntryInspector = BuildEmptyEntryInspector();
    private InspectorPanelModel _selectedLadderBucketInspector = BuildEmptyLadderBucketInspector();
    private InspectorPanelModel _continuityInspector = BuildEmptyContinuityInspector();
    private string _runActionStateTitle = "Select a retained run";
    private string _runActionStateDetail = "Run Detail, Portfolio, Ledger, and Security Master actions unlock after retained cash-flow evidence loads.";

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

    public WorkstationTableModel<CashFlowEntryDto> EntriesTable { get; }
    public WorkstationTableModel<CashLadderBucketDto> LadderTable { get; }

    public CashFlowEntryDto? SelectedEntry
    {
        get => _selectedEntry;
        set
        {
            if (SetProperty(ref _selectedEntry, value))
            {
                SelectedEntryInspector = value is null
                    ? BuildEmptyEntryInspector()
                    : BuildEntryInspector(value);
                RaiseSelectedEntryStateChanged();
            }
        }
    }

    public CashLadderBucketDto? SelectedLadderBucket
    {
        get => _selectedLadderBucket;
        set
        {
            if (SetProperty(ref _selectedLadderBucket, value))
            {
                SelectedLadderBucketInspector = value is null
                    ? BuildEmptyLadderBucketInspector()
                    : BuildLadderBucketInspector(value);
                OnPropertyChanged(nameof(HasSelectedLadderBucket));
            }
        }
    }

    public InspectorPanelModel SelectedEntryInspector
    {
        get => _selectedEntryInspector;
        private set => SetProperty(ref _selectedEntryInspector, value);
    }

    public InspectorPanelModel SelectedLadderBucketInspector
    {
        get => _selectedLadderBucketInspector;
        private set => SetProperty(ref _selectedLadderBucketInspector, value);
    }

    public InspectorPanelModel ContinuityInspector
    {
        get => _continuityInspector;
        private set => SetProperty(ref _continuityInspector, value);
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

    public bool CanOpenRunDrillIns => !string.IsNullOrWhiteSpace(_runId);

    public string RunDrillInTooltip => string.IsNullOrWhiteSpace(_runId)
        ? "Select a retained strategy run before opening related run drill-ins."
        : $"Open retained run drill-ins for {_runId}.";

    public bool CanOpenSelectedSecurity
        => SelectedEntry is { Symbol: { } symbol } && !string.IsNullOrWhiteSpace(symbol);

    public string SelectedSecurityTooltip
    {
        get
        {
            if (SelectedEntry is null)
            {
                return "Select a cash-flow event before opening Security Master.";
            }

            return CanOpenSelectedSecurity
                ? $"Open Security Master lookup for {SelectedEntry.Symbol}."
                : "Select a security-linked cash-flow event before opening Security Master.";
        }
    }

    public string RunActionStateTitle
    {
        get => _runActionStateTitle;
        private set => SetProperty(ref _runActionStateTitle, value);
    }

    public string RunActionStateDetail
    {
        get => _runActionStateDetail;
        private set => SetProperty(ref _runActionStateDetail, value);
    }

    public IRelayCommand OpenBrowserCommand { get; }
    public IRelayCommand OpenRunDetailCommand { get; }
    public IRelayCommand OpenPortfolioCommand { get; }
    public IRelayCommand OpenLedgerCommand { get; }
    public IRelayCommand OpenSelectedSecurityCommand { get; }

    public bool HasEntries => Entries.Count > 0;

    public bool HasLadderBuckets => LadderBuckets.Count > 0;

    public bool HasSelectedEntry => SelectedEntry is not null;

    public bool HasSelectedLadderBucket => SelectedLadderBucket is not null;

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

            if (StatusText.StartsWith("No cash flow data", StringComparison.Ordinal) ||
                StatusText.StartsWith("Could not load cash-flow data", StringComparison.Ordinal))
            {
                return "Cash-flow data unavailable";
            }

            if (string.IsNullOrWhiteSpace(_runId))
            {
                return "Select a run to inspect cash flows";
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

            if (StatusText.StartsWith("No cash flow data", StringComparison.Ordinal) ||
                StatusText.StartsWith("Could not load cash-flow data", StringComparison.Ordinal))
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

            if (StatusText.StartsWith("No cash flow data", StringComparison.Ordinal) ||
                StatusText.StartsWith("Could not load cash-flow data", StringComparison.Ordinal))
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

        EntriesTable = new WorkstationTableModel<CashFlowEntryDto>(
            Entries,
            [
                new("Timestamp", nameof(CashFlowEntryDto.Timestamp), 150, "{0:g}"),
                new("Type", nameof(CashFlowEntryDto.EventKind), 120),
                new("Symbol", nameof(CashFlowEntryDto.Symbol), 90),
                new("Amount", nameof(CashFlowEntryDto.Amount), 115, "{0:C2}"),
                new("Currency", nameof(CashFlowEntryDto.Currency), 80),
                new("Account", nameof(CashFlowEntryDto.AccountId), 120),
                new("Description", nameof(CashFlowEntryDto.Description), 240)
            ],
            "Cash-flow events",
            "No cash-flow events",
            "Select a retained run with cash-flow evidence before reviewing events.");

        LadderTable = new WorkstationTableModel<CashLadderBucketDto>(
            LadderBuckets,
            [
                new("Bucket Start", nameof(CashLadderBucketDto.BucketStart), 115, "{0:yyyy-MM-dd}"),
                new("Bucket End", nameof(CashLadderBucketDto.BucketEnd), 115, "{0:yyyy-MM-dd}"),
                new("Inflows", nameof(CashLadderBucketDto.ProjectedInflows), 115, "{0:C2}"),
                new("Outflows", nameof(CashLadderBucketDto.ProjectedOutflows), 115, "{0:C2}"),
                new("Net Flow", nameof(CashLadderBucketDto.NetFlow), 110, "{0:C2}"),
                new("Currency", nameof(CashLadderBucketDto.Currency), 80),
                new("Events", nameof(CashLadderBucketDto.EventCount), 70)
            ],
            "Cash ladder",
            "No cash ladder buckets",
            "Select a retained run with cash-flow events before reviewing projected buckets.");

        OpenBrowserCommand = new RelayCommand(() => _navigationService.NavigateTo("StrategyRuns"));
        OpenRunDetailCommand = new RelayCommand(OpenRunDetail, () => CanOpenRunDrillIns);
        OpenPortfolioCommand = new RelayCommand(OpenPortfolio, () => CanOpenRunDrillIns);
        OpenLedgerCommand = new RelayCommand(OpenLedger, () => CanOpenRunDrillIns);
        OpenSelectedSecurityCommand = new RelayCommand(OpenSelectedSecurity, () => CanOpenSelectedSecurity);
    }

    public Task LoadRunAsync(string? runId, CancellationToken ct = default)
        => LoadRunDataForRunIdAsync(runId, ct);

    private async Task LoadFromParameterAsync(object? parameter, CancellationToken ct = default)
        => await LoadRunDataForRunIdAsync(parameter as string, ct);

    private async Task LoadRunDataForRunIdAsync(string? runId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(runId))
        {
            ApplyNoRunSelectedState();
            return;
        }

        ApplyLoadingState(runId);
        try
        {
            var summaryTask = _runService.GetCashFlowAsync(runId, ct: ct);
            var continuityTask = _continuityService?.GetRunContinuityAsync(runId, ct) ??
                                 Task.FromResult<StrategyRunContinuityDetail?>(null);

            await Task.WhenAll(summaryTask, continuityTask);

            var summary = await summaryTask;
            var continuity = await continuityTask;
            if (summary is null)
            {
                ApplyRunUnavailableState(runId);
                ApplyContinuity(null);
                return;
            }

            ApplySummary(summary, continuity);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ApplyLoadFailedState(runId, ex);
        }
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
        BucketSummaryText = $"{summary.Ladder.Buckets.Count} bucket(s) x {summary.Ladder.BucketDays}d - {summary.Currency}";
        StatusText = $"{summary.TotalEntries} cash-flow event(s) loaded. Net position: {summary.NetCashFlow:C2}.";
        RunActionStateTitle = "Run drill-ins ready";
        RunActionStateDetail = $"Open detail, portfolio, or ledger review for run {summary.RunId}. Security Master unlocks after selecting a symbol-linked cash-flow event.";

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

        SelectedLadderBucket = LadderBuckets.FirstOrDefault();

        ApplyContinuity(continuity);
        NotifyRunCommandsChanged();
        RaiseCashFlowStateChanged();
    }

    private void ApplyNoRunSelectedState()
    {
        ResetCashFlowState();
        StatusText = "Select a strategy run to inspect its cash flows.";
        RunActionStateTitle = "Select a retained run";
        RunActionStateDetail = "Run Detail, Portfolio, Ledger, and Security Master actions unlock after retained cash-flow evidence loads.";
        NotifyRunCommandsChanged();
        RaiseCashFlowStateChanged();
    }

    private void ApplyLoadingState(string runId)
    {
        ResetCashFlowState();
        StatusText = $"Loading cash-flow evidence for run '{runId}'...";
        RunActionStateTitle = "Loading cash-flow evidence";
        RunActionStateDetail = "Run drill-ins stay disabled until retained cash-flow events and continuity posture finish loading.";
        NotifyRunCommandsChanged();
        RaiseCashFlowStateChanged();
    }

    private void ApplyRunUnavailableState(string runId)
    {
        ResetCashFlowState();
        StatusText = $"No cash flow data is available for run '{runId}'.";
        RunActionStateTitle = "Cash-flow evidence unavailable";
        RunActionStateDetail = "Open the run browser and select a retained run before using cash-flow drill-ins.";
        NotifyRunCommandsChanged();
        RaiseCashFlowStateChanged();
    }

    private void ApplyLoadFailedState(string runId, Exception exception)
    {
        ResetCashFlowState();
        StatusText = $"Could not load cash-flow data for run '{runId}'.";
        RunActionStateTitle = "Cash-flow load failed";
        RunActionStateDetail = $"Resolve the loading issue before opening cash-flow drill-ins: {exception.Message}";
        ApplyContinuity(null);
        NotifyRunCommandsChanged();
        RaiseCashFlowStateChanged();
    }

    private void ResetCashFlowState()
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
        SelectedLadderBucket = null;
        SelectedEntryInspector = BuildEmptyEntryInspector();
        SelectedLadderBucketInspector = BuildEmptyLadderBucketInspector();
        ResetContinuityState();
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
        RaisePropertyChanged(nameof(CanOpenRunDrillIns));
        RaisePropertyChanged(nameof(RunDrillInTooltip));
        RaisePropertyChanged(nameof(RunActionStateTitle));
        RaisePropertyChanged(nameof(RunActionStateDetail));
    }

    private void NotifyRunCommandsChanged()
    {
        OpenRunDetailCommand.NotifyCanExecuteChanged();
        OpenPortfolioCommand.NotifyCanExecuteChanged();
        OpenLedgerCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanOpenRunDrillIns));
        OnPropertyChanged(nameof(RunDrillInTooltip));
    }

    private void RaiseSelectedEntryStateChanged()
    {
        OnPropertyChanged(nameof(HasSelectedEntry));
        OnPropertyChanged(nameof(CanOpenSelectedSecurity));
        OnPropertyChanged(nameof(SelectedSecurityTooltip));
        OpenSelectedSecurityCommand.NotifyCanExecuteChanged();
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
            ContinuityInspector = BuildContinuityInspector(
                ContinuityPostureText,
                ContinuityDetailText,
                ContinuityWarningText,
                _continuityService is null ? "Not connected" : "Unavailable");
            return;
        }

        var status = continuity.ContinuityStatus;
        ContinuityPostureText = BuildContinuityPostureText(status);
        ContinuityDetailText =
            $"Run {FormatSeamHealth(status.RunHealth)}; portfolio {FormatSeamHealth(status.PortfolioHealth)}; ledger {FormatSeamHealth(status.LedgerHealth)}; cash flow {FormatSeamHealth(status.CashFlowHealth)}; reconciliation {FormatSeamHealth(status.ReconciliationHealth)}.";
        ContinuityWarningText = BuildContinuityWarningText(status);
        ContinuityInspector = BuildContinuityInspector(
            ContinuityPostureText,
            ContinuityDetailText,
            ContinuityWarningText,
            "Shared service");
    }

    private void ResetContinuityState()
    {
        ContinuityPostureText = "Shared continuity appears after a retained run is selected.";
        ContinuityDetailText = "Select a run to compare portfolio, ledger, cash-flow, reconciliation, and warning posture from the shared continuity model.";
        ContinuityWarningText = "No shared continuity payload is loaded.";
        ContinuityInspector = BuildEmptyContinuityInspector();
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

    internal static InspectorPanelModel BuildEntryInspector(CashFlowEntryDto selected)
    {
        var hasSymbol = !string.IsNullOrWhiteSpace(selected.Symbol);
        var title = string.IsNullOrWhiteSpace(selected.Description)
            ? selected.EventKind
            : selected.Description!;

        return new InspectorPanelModel
        {
            Title = title,
            Subtitle = hasSymbol ? $"{selected.Symbol} cash-flow event" : "Run cash-flow event",
            Detail = hasSymbol
                ? "Review the retained cash movement before opening Security Master lookup for the linked symbol."
                : "This retained cash movement is not linked to a security symbol.",
            Badge = new WorkstationBadgeModel(
                "Amount",
                selected.Amount.ToString("C2"),
                "\uE8F1",
                selected.Amount >= 0 ? WorkspaceTone.Success : WorkspaceTone.Warning),
            Facts =
            [
                new("Timestamp", selected.Timestamp.LocalDateTime.ToString("g")),
                new("Type", selected.EventKind),
                new("Symbol", hasSymbol ? selected.Symbol! : "-"),
                new("Amount", selected.Amount.ToString("C2")),
                new("Currency", selected.Currency),
                new("Account", string.IsNullOrWhiteSpace(selected.AccountId) ? "-" : selected.AccountId!),
                new("Source", "Retained run cash flow")
            ]
        };
    }

    private static InspectorPanelModel BuildEmptyEntryInspector()
        => new()
        {
            Title = "No cash-flow event selected",
            Subtitle = "Run cash-flow inspector",
            Detail = "Select a retained cash-flow event to review amount, account, timestamp, and Security Master lookup readiness."
        };

    internal static InspectorPanelModel BuildLadderBucketInspector(CashLadderBucketDto selected)
        => new()
        {
            Title = $"{selected.BucketStart.LocalDateTime:yyyy-MM-dd} to {selected.BucketEnd.LocalDateTime:yyyy-MM-dd}",
            Subtitle = "Cash ladder bucket",
            Detail = "Review the projected inflow and outflow bucket before using cash posture in run review.",
            Badge = new WorkstationBadgeModel(
                "Net",
                selected.NetFlow.ToString("C2"),
                "\uE8F1",
                selected.NetFlow >= 0 ? WorkspaceTone.Success : WorkspaceTone.Warning),
            Facts =
            [
                new("Start", selected.BucketStart.LocalDateTime.ToString("g")),
                new("End", selected.BucketEnd.LocalDateTime.ToString("g")),
                new("Inflows", selected.ProjectedInflows.ToString("C2")),
                new("Outflows", selected.ProjectedOutflows.ToString("C2")),
                new("Currency", selected.Currency),
                new("Events", selected.EventCount.ToString("N0"))
            ]
        };

    private static InspectorPanelModel BuildEmptyLadderBucketInspector()
        => new()
        {
            Title = "No cash ladder bucket selected",
            Subtitle = "Cash ladder inspector",
            Detail = "Select a generated cash ladder bucket to inspect projected inflows, outflows, net flow, and event count."
        };

    private static InspectorPanelModel BuildContinuityInspector(
        string posture,
        string detail,
        string warning,
        string source)
        => new()
        {
            Title = posture,
            Subtitle = "Shared continuity posture",
            Detail = detail,
            Badge = new WorkstationBadgeModel("Continuity", source, "\uE8A5", ToneForContinuity(posture)),
            Facts =
            [
                new("Warning", warning),
                new("Source", source)
            ]
        };

    private static InspectorPanelModel BuildEmptyContinuityInspector()
        => new()
        {
            Title = "No continuity payload",
            Subtitle = "Shared continuity posture",
            Detail = "Select a retained strategy run to compare run, portfolio, ledger, cash-flow, reconciliation, and warning posture.",
            Badge = new WorkstationBadgeModel("Continuity", "Not loaded", "\uE8A5", WorkspaceTone.Neutral),
            Facts =
            [
                new("Warning", "No shared continuity payload is loaded."),
                new("Source", "Awaiting run selection")
            ]
        };

    private static string ToneForContinuity(string posture)
    {
        if (posture.Contains("blocked", StringComparison.OrdinalIgnoreCase))
        {
            return WorkspaceTone.Danger;
        }

        if (posture.Contains("ready", StringComparison.OrdinalIgnoreCase))
        {
            return WorkspaceTone.Success;
        }

        if (posture.Contains("review", StringComparison.OrdinalIgnoreCase) ||
            posture.Contains("incomplete", StringComparison.OrdinalIgnoreCase) ||
            posture.Contains("unavailable", StringComparison.OrdinalIgnoreCase) ||
            posture.Contains("not connected", StringComparison.OrdinalIgnoreCase))
        {
            return WorkspaceTone.Warning;
        }

        return WorkspaceTone.Neutral;
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

    private void OpenSelectedSecurity()
    {
        if (SelectedEntry is { Symbol: { } symbol } && !string.IsNullOrWhiteSpace(symbol))
        {
            _navigationService.NavigateTo("SecurityMaster", symbol);
        }
    }
}
