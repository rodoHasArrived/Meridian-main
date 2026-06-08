using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Meridian.Contracts.Workstation;
using Meridian.Wpf.Models;
using Meridian.Wpf.Services;
using Meridian.Wpf.Workstation.Models;

namespace Meridian.Wpf.ViewModels;

public sealed class StrategyRunDetailViewModel : BindableBase
{
    private readonly StrategyRunWorkspaceService _runService;
    private readonly NavigationService _navigationService;

    private string? _runId;
    private object? _parameter;
    private ParameterItemViewModel? _selectedParameter;
    private InspectorPanelModel _runSummaryInspector = BuildEmptyRunSummaryInspector();
    private InspectorPanelModel _selectedParameterInspector = BuildEmptyParameterInspector();
    private string _runActionStateTitle = "Select a retained run";
    private string _runActionStateDetail = "Portfolio, Ledger, Risk, and Cash Flow unlock after selecting a retained strategy run.";

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

    public ObservableCollection<ParameterItemViewModel> Parameters { get; } = [];

    public WorkstationTableModel<ParameterItemViewModel> ParametersTable { get; }

    public ParameterItemViewModel? SelectedParameter
    {
        get => _selectedParameter;
        set
        {
            if (SetProperty(ref _selectedParameter, value))
            {
                SelectedParameterInspector = value is null
                    ? BuildEmptyParameterInspector()
                    : BuildParameterInspector(value);
                OnPropertyChanged(nameof(HasSelectedParameter));
            }
        }
    }

    public InspectorPanelModel RunSummaryInspector
    {
        get => _runSummaryInspector;
        private set => SetProperty(ref _runSummaryInspector, value);
    }

    public InspectorPanelModel SelectedParameterInspector
    {
        get => _selectedParameterInspector;
        private set => SetProperty(ref _selectedParameterInspector, value);
    }

    public bool HasSelectedParameter => SelectedParameter is not null;

    private string _title = "Strategy Run Detail";
    public string Title
    {
        get => _title;
        private set => SetProperty(ref _title, value);
    }

    private string _statusText = "Select a strategy run from the browser.";
    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    private string _modeText = "-";
    public string ModeText
    {
        get => _modeText;
        private set => SetProperty(ref _modeText, value);
    }

    private string _engineText = "-";
    public string EngineText
    {
        get => _engineText;
        private set => SetProperty(ref _engineText, value);
    }

    private string _startedAtText = "-";
    public string StartedAtText
    {
        get => _startedAtText;
        private set => SetProperty(ref _startedAtText, value);
    }

    private string _completedAtText = "-";
    public string CompletedAtText
    {
        get => _completedAtText;
        private set => SetProperty(ref _completedAtText, value);
    }

    private string _netPnlText = "-";
    public string NetPnlText
    {
        get => _netPnlText;
        private set => SetProperty(ref _netPnlText, value);
    }

    private string _returnText = "-";
    public string ReturnText
    {
        get => _returnText;
        private set => SetProperty(ref _returnText, value);
    }

    private string _equityText = "-";
    public string EquityText
    {
        get => _equityText;
        private set => SetProperty(ref _equityText, value);
    }

    private string _fillCountText = "-";
    public string FillCountText
    {
        get => _fillCountText;
        private set => SetProperty(ref _fillCountText, value);
    }

    private string _portfolioReferenceText = "-";
    public string PortfolioReferenceText
    {
        get => _portfolioReferenceText;
        private set => SetProperty(ref _portfolioReferenceText, value);
    }

    private string _ledgerReferenceText = "-";
    public string LedgerReferenceText
    {
        get => _ledgerReferenceText;
        private set => SetProperty(ref _ledgerReferenceText, value);
    }

    public bool CanOpenRunDrillIns => !string.IsNullOrWhiteSpace(_runId);

    public string RunDrillInTooltip => string.IsNullOrWhiteSpace(_runId)
        ? "Select a retained strategy run before opening related run drill-ins."
        : $"Open retained run drill-ins for {_runId}.";

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
    public IRelayCommand OpenPortfolioCommand { get; }
    public IRelayCommand OpenLedgerCommand { get; }
    public IRelayCommand OpenCashFlowCommand { get; }
    public IRelayCommand OpenRiskCommand { get; }

    internal StrategyRunDetailViewModel(
        StrategyRunWorkspaceService runService,
        NavigationService navigationService)
    {
        _runService = runService;
        _navigationService = navigationService;
        ParametersTable = new WorkstationTableModel<ParameterItemViewModel>(
            Parameters,
            [
                new("Name", nameof(ParameterItemViewModel.Name), 180),
                new("Value", nameof(ParameterItemViewModel.Value), 260)
            ],
            "Run parameters",
            "No parameters retained",
            "Select a retained strategy run with persisted parameters before reviewing run configuration.");

        OpenBrowserCommand = new RelayCommand(() => _navigationService.NavigateTo("StrategyRuns"));
        OpenPortfolioCommand = new RelayCommand(OpenPortfolio, () => CanOpenRunDrillIns);
        OpenLedgerCommand = new RelayCommand(OpenLedger, () => CanOpenRunDrillIns);
        OpenCashFlowCommand = new RelayCommand(OpenCashFlow, () => CanOpenRunDrillIns);
        OpenRiskCommand = new RelayCommand(OpenRisk, () => CanOpenRunDrillIns);
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
            var detail = await _runService.GetRunDetailAsync(runId, ct);
            if (detail is null)
            {
                ApplyRunUnavailableState(runId);
                return;
            }

            _runId = runId;
            ApplyDetail(detail);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ApplyLoadFailedState(runId, ex);
        }
    }

    private void ApplyDetail(StrategyRunDetail detail)
    {
        Title = $"{detail.Summary.StrategyName} ({detail.Summary.RunId[..Math.Min(8, detail.Summary.RunId.Length)]})";
        StatusText = $"{detail.Summary.Status} {detail.Summary.Mode} run recorded at {detail.Summary.StartedAt.LocalDateTime:g}.";
        RunActionStateTitle = "Run drill-ins ready";
        RunActionStateDetail = $"Open portfolio, ledger, risk, or cash-flow review for run {detail.Summary.RunId}.";
        ModeText = detail.Summary.Mode.ToString();
        EngineText = detail.Summary.Engine.ToString();
        StartedAtText = detail.Summary.StartedAt.LocalDateTime.ToString("g");
        CompletedAtText = detail.Summary.CompletedAt?.LocalDateTime.ToString("g") ?? "In progress";
        NetPnlText = FormatCurrency(detail.Summary.NetPnl);
        ReturnText = FormatPercent(detail.Summary.TotalReturn);
        EquityText = FormatCurrency(detail.Summary.FinalEquity);
        FillCountText = detail.Summary.FillCount.ToString("N0");
        PortfolioReferenceText = detail.Summary.PortfolioId ?? "Not linked";
        LedgerReferenceText = detail.Summary.LedgerReference ?? "Not linked";
        RunSummaryInspector = BuildRunSummaryInspector(detail);

        Parameters.Clear();
        foreach (var parameter in detail.Parameters.OrderBy(static entry => entry.Key, StringComparer.OrdinalIgnoreCase))
        {
            Parameters.Add(new ParameterItemViewModel(parameter.Key, parameter.Value));
        }

        SelectedParameter = Parameters.FirstOrDefault();
        NotifyRunCommandsChanged();
    }

    private void ApplyNoRunSelectedState()
    {
        ResetRunState();
        StatusText = "Select a strategy run from the browser.";
        RunActionStateTitle = "Select a retained run";
        RunActionStateDetail = "Portfolio, Ledger, Risk, and Cash Flow unlock after selecting a retained strategy run.";
        NotifyRunCommandsChanged();
    }

    private void ApplyLoadingState(string runId)
    {
        ResetRunState();
        StatusText = $"Loading run detail for '{runId}'...";
        RunActionStateTitle = "Loading run evidence";
        RunActionStateDetail = "Run drill-ins stay disabled until retained run detail has loaded.";
        NotifyRunCommandsChanged();
    }

    private void ApplyRunUnavailableState(string runId)
    {
        ResetRunState();
        StatusText = $"Strategy run '{runId}' was not found.";
        RunActionStateTitle = "Run evidence unavailable";
        RunActionStateDetail = "Open the run browser and select a retained run to enable related drill-ins.";
        RunSummaryInspector = new InspectorPanelModel
        {
            Title = "Run unavailable",
            Subtitle = "Run detail inspector",
            Detail = $"No retained strategy run matched '{runId}'.",
            Badge = new WorkstationBadgeModel("Run", "Unavailable", "\uE783", WorkspaceTone.Warning)
        };
        NotifyRunCommandsChanged();
    }

    private void ApplyLoadFailedState(string runId, Exception exception)
    {
        ResetRunState();
        StatusText = $"Could not load run detail for '{runId}'.";
        RunActionStateTitle = "Run detail failed";
        RunActionStateDetail = "Resolve the load error, then reopen a retained run before using related drill-ins.";
        RunSummaryInspector = new InspectorPanelModel
        {
            Title = "Run detail failed",
            Subtitle = "Run detail inspector",
            Detail = exception.Message,
            Badge = new WorkstationBadgeModel("Run", "Failed", "\uE783", WorkspaceTone.Danger)
        };
        NotifyRunCommandsChanged();
    }

    private void ResetRunState()
    {
        _runId = null;
        Title = "Strategy Run Detail";
        ModeText = "-";
        EngineText = "-";
        StartedAtText = "-";
        CompletedAtText = "-";
        NetPnlText = "-";
        ReturnText = "-";
        EquityText = "-";
        FillCountText = "-";
        PortfolioReferenceText = "-";
        LedgerReferenceText = "-";
        Parameters.Clear();
        SelectedParameter = null;
        RunSummaryInspector = BuildEmptyRunSummaryInspector();
    }

    private void NotifyRunCommandsChanged()
    {
        OpenPortfolioCommand.NotifyCanExecuteChanged();
        OpenLedgerCommand.NotifyCanExecuteChanged();
        OpenCashFlowCommand.NotifyCanExecuteChanged();
        OpenRiskCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanOpenRunDrillIns));
        OnPropertyChanged(nameof(RunDrillInTooltip));
    }

    internal static InspectorPanelModel BuildRunSummaryInspector(StrategyRunDetail detail)
    {
        var statusValue = detail.Summary.Status.ToString();
        return new InspectorPanelModel
        {
            Title = detail.Summary.RunId,
            Subtitle = $"{detail.Summary.StrategyName} run evidence",
            Detail = "Review retained run timing, PnL, references, and execution counts before opening downstream drill-ins.",
            Badge = new WorkstationBadgeModel("Status", statusValue, "\uE8A5", ToneForStatus(statusValue)),
            Facts =
            [
                new("Started", detail.Summary.StartedAt.LocalDateTime.ToString("g")),
                new("Completed", detail.Summary.CompletedAt?.LocalDateTime.ToString("g") ?? "In progress"),
                new("Return", FormatPercent(detail.Summary.TotalReturn)),
                new("Fills", detail.Summary.FillCount.ToString("N0")),
                new("Portfolio", detail.Summary.PortfolioId ?? "Not linked"),
                new("Ledger", detail.Summary.LedgerReference ?? "Not linked")
            ]
        };
    }

    private static InspectorPanelModel BuildEmptyRunSummaryInspector()
        => new()
        {
            Title = "No run selected",
            Subtitle = "Run detail inspector",
            Detail = "Select a retained strategy run to review run evidence and unlock downstream drill-ins."
        };

    private static InspectorPanelModel BuildParameterInspector(ParameterItemViewModel selected)
        => new()
        {
            Title = selected.Name,
            Subtitle = "Run parameter",
            Detail = "This parameter was retained with the selected strategy run.",
            Badge = new WorkstationBadgeModel("Parameter", "Retained", "\uE8A5", WorkspaceTone.Info),
            Facts =
            [
                new("Name", selected.Name),
                new("Value", selected.Value)
            ]
        };

    private static InspectorPanelModel BuildEmptyParameterInspector()
        => new()
        {
            Title = "No parameter selected",
            Subtitle = "Run parameter inspector",
            Detail = "Select a retained parameter row to inspect the run configuration value."
        };

    private static string ToneForStatus(string status)
    {
        if (status.Contains("Complete", StringComparison.OrdinalIgnoreCase) ||
            status.Contains("Promoted", StringComparison.OrdinalIgnoreCase))
        {
            return WorkspaceTone.Success;
        }

        if (status.Contains("Fail", StringComparison.OrdinalIgnoreCase) ||
            status.Contains("Cancel", StringComparison.OrdinalIgnoreCase))
        {
            return WorkspaceTone.Danger;
        }

        if (status.Contains("Running", StringComparison.OrdinalIgnoreCase) ||
            status.Contains("Pause", StringComparison.OrdinalIgnoreCase))
        {
            return WorkspaceTone.Warning;
        }

        return WorkspaceTone.Neutral;
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

    private void OpenCashFlow()
    {
        if (!string.IsNullOrWhiteSpace(_runId))
        {
            _navigationService.NavigateTo("RunCashFlow", _runId);
        }
    }

    private void OpenRisk()
    {
        if (!string.IsNullOrWhiteSpace(_runId))
        {
            _navigationService.NavigateTo("RunRisk", _runId);
        }
    }

    private static string FormatCurrency(decimal? value) => value.HasValue ? value.Value.ToString("C2") : "-";
    private static string FormatPercent(decimal? value) => value.HasValue ? value.Value.ToString("P2") : "-";
}

public sealed record ParameterItemViewModel(string Name, string Value);
