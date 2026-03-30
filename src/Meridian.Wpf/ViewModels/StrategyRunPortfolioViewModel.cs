using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Meridian.Contracts.Workstation;
using Meridian.Wpf.Services;

namespace Meridian.Wpf.ViewModels;

public sealed class StrategyRunPortfolioViewModel : BindableBase
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

    public ObservableCollection<PortfolioPositionSummary> Positions { get; } = [];

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

    internal StrategyRunPortfolioViewModel(
        StrategyRunWorkspaceService runService,
        NavigationService navigationService)
    {
        _runService = runService;
        _navigationService = navigationService;
        OpenBrowserCommand = new RelayCommand(() => _navigationService.NavigateTo("StrategyRuns"));
        OpenRunDetailCommand = new RelayCommand(OpenRunDetail, () => !string.IsNullOrWhiteSpace(_runId));
        OpenLedgerCommand = new RelayCommand(OpenLedger, () => !string.IsNullOrWhiteSpace(_runId));
    }

    private async Task LoadFromParameterAsync(object? parameter, CancellationToken ct = default)
    {
        var runId = parameter as string;
        if (string.IsNullOrWhiteSpace(runId))
        {
            StatusText = "Select a strategy run to inspect portfolio state.";
            return;
        }

        _runId = runId;
        var portfolio = await _runService.GetPortfolioAsync(runId, ct);
        if (portfolio is null)
        {
            StatusText = $"No portfolio snapshot is available for run '{runId}'.";
            return;
        }

        Title = $"Portfolio {portfolio.PortfolioId}";
        StatusText = portfolio.SecurityMissingCount > 0
            ? $"{portfolio.Positions.Count} positions captured for run {portfolio.RunId}. {portfolio.SecurityMissingCount} symbol(s) still need Security Master mapping."
            : $"{portfolio.Positions.Count} positions captured for run {portfolio.RunId}.";
        EquityText = portfolio.TotalEquity.ToString("C2");
        CashText = portfolio.Cash.ToString("C2");
        GrossExposureText = portfolio.GrossExposure.ToString("C2");
        NetExposureText = portfolio.NetExposure.ToString("C2");
        RealizedPnlText = portfolio.RealizedPnl.ToString("C2");
        UnrealizedPnlText = portfolio.UnrealizedPnl.ToString("C2");
        CommissionsText = portfolio.Commissions.ToString("C2");
        AsOfText = portfolio.AsOf.LocalDateTime.ToString("g");
        SecurityResolvedText = portfolio.SecurityResolvedCount.ToString("N0");
        SecurityMissingText = portfolio.SecurityMissingCount.ToString("N0");

        Positions.Clear();
        foreach (var position in portfolio.Positions)
        {
            Positions.Add(position);
        }

        OpenRunDetailCommand.NotifyCanExecuteChanged();
        OpenLedgerCommand.NotifyCanExecuteChanged();
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
}
