using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Meridian.Contracts.Workstation;
using Meridian.Wpf.Models;
using Meridian.Wpf.Services;
using Meridian.Wpf.Workstation.Models;

namespace Meridian.Wpf.ViewModels;

public sealed class StrategyRunPortfolioViewModel : BindableBase
{
    private readonly StrategyRunWorkspaceService _runService;
    private readonly NavigationService _navigationService;
    private string? _runId;
    private object? _parameter;
    private PortfolioPositionSummary? _selectedPosition;
    private InspectorPanelModel _selectedPositionInspector = BuildEmptyPositionInspector();

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

    public ObservableCollection<PortfolioPositionSummary> Positions { get; } = [];

    public WorkstationTableModel<PortfolioPositionSummary> PositionsTable { get; }

    public PortfolioPositionSummary? SelectedPosition
    {
        get => _selectedPosition;
        set
        {
            if (SetProperty(ref _selectedPosition, value))
            {
                SelectedPositionInspector = value is null
                    ? BuildEmptyPositionInspector()
                    : BuildPositionInspector(value);
                RaiseSelectedPositionStateChanged();
            }
        }
    }

    public InspectorPanelModel SelectedPositionInspector
    {
        get => _selectedPositionInspector;
        private set => SetProperty(ref _selectedPositionInspector, value);
    }

    public bool HasSelectedPosition => SelectedPosition is not null;

    public string RunDrillInTooltip => string.IsNullOrWhiteSpace(_runId)
        ? "Select a retained strategy run before opening related run drill-ins."
        : $"Open retained run drill-ins for {_runId}.";

    public string SelectedSecurityTooltip => SelectedPosition switch
    {
        null => "Select a position before opening Security Master.",
        { Security: { } security } => $"Open Security Master for {security.DisplayName}.",
        { Symbol: { Length: > 0 } symbol } => $"Open Security Master lookup for {symbol}.",
        _ => "Select a mapped position before opening Security Master."
    };

    private string _title = "Portfolio Drill-In";
    public string Title
    {
        get => _title;
        private set => SetProperty(ref _title, value);
    }

    private string _statusText = "Select a strategy run to inspect portfolio state.";
    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    private string _equityText = "-";
    public string EquityText
    {
        get => _equityText;
        private set => SetProperty(ref _equityText, value);
    }

    private string _cashText = "-";
    public string CashText
    {
        get => _cashText;
        private set => SetProperty(ref _cashText, value);
    }

    private string _grossExposureText = "-";
    public string GrossExposureText
    {
        get => _grossExposureText;
        private set => SetProperty(ref _grossExposureText, value);
    }

    private string _netExposureText = "-";
    public string NetExposureText
    {
        get => _netExposureText;
        private set => SetProperty(ref _netExposureText, value);
    }

    private string _realizedPnlText = "-";
    public string RealizedPnlText
    {
        get => _realizedPnlText;
        private set => SetProperty(ref _realizedPnlText, value);
    }

    private string _unrealizedPnlText = "-";
    public string UnrealizedPnlText
    {
        get => _unrealizedPnlText;
        private set => SetProperty(ref _unrealizedPnlText, value);
    }

    private string _commissionsText = "-";
    public string CommissionsText
    {
        get => _commissionsText;
        private set => SetProperty(ref _commissionsText, value);
    }

    private string _asOfText = "-";
    public string AsOfText
    {
        get => _asOfText;
        private set => SetProperty(ref _asOfText, value);
    }

    private string _securityResolvedText = "-";
    public string SecurityResolvedText
    {
        get => _securityResolvedText;
        private set => SetProperty(ref _securityResolvedText, value);
    }

    private string _securityMissingText = "-";
    public string SecurityMissingText
    {
        get => _securityMissingText;
        private set => SetProperty(ref _securityMissingText, value);
    }

    public IRelayCommand OpenBrowserCommand { get; }
    public IRelayCommand OpenRunDetailCommand { get; }
    public IRelayCommand OpenLedgerCommand { get; }
    public IRelayCommand OpenCashFlowCommand { get; }
    public IRelayCommand OpenSelectedSecurityCommand { get; }

    internal StrategyRunPortfolioViewModel(
        StrategyRunWorkspaceService runService,
        NavigationService navigationService)
    {
        _runService = runService;
        _navigationService = navigationService;
        PositionsTable = new WorkstationTableModel<PortfolioPositionSummary>(
            Positions,
            [
                new("Symbol", nameof(PortfolioPositionSummary.Symbol), 90),
                new("Mark readiness", "MarkFreshness.Status", 125),
                new("Observed on", "MarkFreshness.ObservedOn", 110),
                new("Mark age (days)", "MarkFreshness.AgeDays", 100),
                new("Security", "Security.DisplayName", 180),
                new("Asset Class", "Security.AssetClass", 105),
                new("Identifier", "Security.PrimaryIdentifier", 125),
                new("Qty", nameof(PortfolioPositionSummary.Quantity), 90, "{0:N0}"),
                new("Avg Cost", nameof(PortfolioPositionSummary.AverageCostBasis), 105, "{0:C2}"),
                new("Realized PnL", nameof(PortfolioPositionSummary.RealizedPnl), 115, "{0:C2}"),
                new("Unrealized PnL", nameof(PortfolioPositionSummary.UnrealizedPnl), 125, "{0:C2}"),
                new("Short", nameof(PortfolioPositionSummary.IsShort), 70)
            ],
            "Run portfolio positions",
            "No positions retained",
            "Select a retained strategy run with a portfolio snapshot before reviewing positions.");

        OpenBrowserCommand = new RelayCommand(() => _navigationService.NavigateTo("StrategyRuns"));
        OpenRunDetailCommand = new RelayCommand(OpenRunDetail, () => !string.IsNullOrWhiteSpace(_runId));
        OpenLedgerCommand = new RelayCommand(OpenLedger, () => !string.IsNullOrWhiteSpace(_runId));
        OpenCashFlowCommand = new RelayCommand(OpenCashFlow, () => !string.IsNullOrWhiteSpace(_runId));
        OpenSelectedSecurityCommand = new RelayCommand(OpenSelectedSecurity, () => SelectedPosition is not null);
    }

    private async Task LoadFromParameterAsync(object? parameter, CancellationToken ct = default)
    {
        var runId = parameter as string;
        if (string.IsNullOrWhiteSpace(runId))
        {
            ApplyNoRunSelectedState();
            return;
        }

        _runId = runId;
        var portfolio = await _runService.GetPortfolioAsync(runId, ct);
        if (portfolio is null)
        {
            ApplyPortfolioUnavailableState(runId);
            return;
        }

        Title = $"Portfolio {portfolio.PortfolioId}";
        StatusText = portfolio.SecurityMissingCount > 0
            ? $"{portfolio.Positions.Count} positions captured for run {portfolio.RunId}. {portfolio.SecurityMissingCount} symbol(s) still need Security Master mapping."
            : $"{portfolio.Positions.Count} positions captured for run {portfolio.RunId}.";
        var blockedMarks = portfolio.Positions.Where(position => position.MarkFreshness?.Status != "Current").ToArray();
        if (blockedMarks.Length > 0)
        {
            StatusText += $" Review required: {string.Join(", ", blockedMarks.Select(position => position.Symbol))}. Recorded valuation numbers require refreshed mark evidence before approval.";
        }
        var valuationMark = new MarkFreshnessPresentation(blockedMarks.Length > 0 ? blockedMarks[0].MarkFreshness : portfolio.Positions.FirstOrDefault()?.MarkFreshness);
        EquityText = valuationMark.RecordedValue(portfolio.TotalEquity);
        CashText = portfolio.Cash.ToString("C2");
        GrossExposureText = valuationMark.RecordedValue(portfolio.GrossExposure);
        NetExposureText = valuationMark.RecordedValue(portfolio.NetExposure);
        RealizedPnlText = portfolio.RealizedPnl.ToString("C2");
        UnrealizedPnlText = valuationMark.RecordedValue(portfolio.UnrealizedPnl);
        CommissionsText = portfolio.Commissions.ToString("C2");
        AsOfText = portfolio.AsOf.LocalDateTime.ToString("g");
        SecurityResolvedText = portfolio.SecurityResolvedCount.ToString("N0");
        SecurityMissingText = portfolio.SecurityMissingCount.ToString("N0");

        Positions.Clear();
        foreach (var position in portfolio.Positions)
        {
            Positions.Add(position);
        }

        SelectedPosition = Positions.FirstOrDefault();

        OpenRunDetailCommand.NotifyCanExecuteChanged();
        OpenLedgerCommand.NotifyCanExecuteChanged();
        OpenCashFlowCommand.NotifyCanExecuteChanged();
        OpenSelectedSecurityCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(RunDrillInTooltip));
    }

    private void ApplyNoRunSelectedState()
    {
        _runId = null;
        ResetPortfolioState();
        StatusText = "Select a strategy run to inspect portfolio state.";
        OpenRunDetailCommand.NotifyCanExecuteChanged();
        OpenLedgerCommand.NotifyCanExecuteChanged();
        OpenCashFlowCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(RunDrillInTooltip));
    }

    private void ApplyPortfolioUnavailableState(string runId)
    {
        _runId = null;
        ResetPortfolioState();
        StatusText = $"No portfolio snapshot is available for run '{runId}'.";
        OpenRunDetailCommand.NotifyCanExecuteChanged();
        OpenLedgerCommand.NotifyCanExecuteChanged();
        OpenCashFlowCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(RunDrillInTooltip));
    }

    private void ResetPortfolioState()
    {
        Title = "Portfolio Drill-In";
        EquityText = "-";
        CashText = "-";
        GrossExposureText = "-";
        NetExposureText = "-";
        RealizedPnlText = "-";
        UnrealizedPnlText = "-";
        CommissionsText = "-";
        AsOfText = "-";
        SecurityResolvedText = "-";
        SecurityMissingText = "-";
        Positions.Clear();
        SelectedPosition = null;
    }

    private void RaiseSelectedPositionStateChanged()
    {
        OnPropertyChanged(nameof(HasSelectedPosition));
        OnPropertyChanged(nameof(SelectedSecurityTooltip));
        OpenSelectedSecurityCommand.NotifyCanExecuteChanged();
    }

    internal static InspectorPanelModel BuildPositionInspector(PortfolioPositionSummary selected)
    {
        var mark = new MarkFreshnessPresentation(selected.MarkFreshness);
        var security = selected.Security;
        var securityTitle = security?.DisplayName ?? "Security mapping unavailable";
        var securityDetail = security is null
            ? "No Security Master record is attached to this retained position. Open Security Master lookup before relying on downstream asset coverage."
            : $"Security Master resolves this position to {security.DisplayName}.";
        var coverageValue = security is null ? "Mapping needed" : security.CoverageStatus.ToString();

        return new InspectorPanelModel
        {
            Title = selected.Symbol,
            Subtitle = securityTitle,
            Detail = $"{mark.Reason} {securityDetail}",
            Badge = new WorkstationBadgeModel("Mark readiness", mark.Label, "\uE8A5", mark.Tone),
            Facts =
            [
                new("Quantity", selected.Quantity.ToString("N0"), selected.IsShort ? "Short exposure" : "Long exposure"),
                new("Average cost", selected.AverageCostBasis.ToString("C2")),
                new("Realized PnL", selected.RealizedPnl.ToString("C2")),
                new("Recorded unrealized PnL", mark.RecordedValue(selected.UnrealizedPnl)),
                new("Mark observed on", mark.ObservedOn),
                new("Mark age", mark.Age),
                new("Valuation date", mark.ValuationDate),
                new("Mark policy", mark.PolicyVersion),
                new("Security coverage", coverageValue),
                new("Asset class", security?.AssetClass ?? "-", security?.SubType ?? string.Empty),
                new("Identifier", security?.PrimaryIdentifier ?? "-", security?.MatchedIdentifierKind ?? string.Empty),
                new("Currency", security?.Currency ?? "-"),
                new("Scope", BuildScopeText(selected))
            ]
        };
    }

    private static InspectorPanelModel BuildEmptyPositionInspector()
        => new()
        {
            Title = "No position selected",
            Subtitle = "Run portfolio inspector",
            Detail = "Select a retained position row to review Security Master coverage, scope, exposure, and PnL before opening downstream drill-ins."
        };

    private static string BuildScopeText(PortfolioPositionSummary selected)
    {
        var scopes = new[]
            {
                selected.AccountScopeDisplayName,
                selected.EntityScopeDisplayName,
                selected.SleeveScopeDisplayName,
                selected.VehicleScopeDisplayName
            }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();

        return scopes.Length == 0
            ? "Run portfolio scope"
            : string.Join(" / ", scopes);
    }

    private void OpenRunDetail()
    {
        if (!string.IsNullOrWhiteSpace(_runId))
        {
            _navigationService.NavigateTo("RunDetail", _runId);
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

    private void OpenSelectedSecurity()
    {
        if (SelectedPosition?.Security?.SecurityId is Guid securityId)
        {
            _navigationService.NavigateTo("SecurityMaster", securityId);
            return;
        }

        if (!string.IsNullOrWhiteSpace(SelectedPosition?.Symbol))
        {
            _navigationService.NavigateTo("SecurityMaster", SelectedPosition.Symbol);
        }
    }
}
