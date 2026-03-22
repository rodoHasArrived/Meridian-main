using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Meridian.Contracts.Workstation;
using Meridian.Wpf.Services;

namespace Meridian.Wpf.ViewModels;

public sealed class StrategyRunDetailViewModel : BindableBase
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

    public ObservableCollection<ParameterItemViewModel> Parameters { get; } = [];

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

    public IRelayCommand OpenBrowserCommand { get; }
    public IRelayCommand OpenPortfolioCommand { get; }
    public IRelayCommand OpenLedgerCommand { get; }

    public StrategyRunDetailViewModel()
        : this(StrategyRunWorkspaceService.Instance, NavigationService.Instance)
    {
    }

    internal StrategyRunDetailViewModel(
        StrategyRunWorkspaceService runService,
        NavigationService navigationService)
    {
        _runService = runService;
        _navigationService = navigationService;
        OpenBrowserCommand = new RelayCommand(() => _navigationService.NavigateTo("StrategyRuns"));
        OpenPortfolioCommand = new RelayCommand(OpenPortfolio, () => !string.IsNullOrWhiteSpace(_runId));
        OpenLedgerCommand = new RelayCommand(OpenLedger, () => !string.IsNullOrWhiteSpace(_runId));
    }

    private async Task LoadFromParameterAsync(object? parameter, CancellationToken ct = default)
    {
        var runId = parameter as string;
        if (string.IsNullOrWhiteSpace(runId))
        {
            StatusText = "Select a strategy run from the browser.";
            return;
        }

        _runId = runId;
        var detail = await _runService.GetRunDetailAsync(runId, ct);
        if (detail is null)
        {
            StatusText = $"Strategy run '{runId}' was not found.";
            return;
        }

        ApplyDetail(detail);
    }

    private void ApplyDetail(StrategyRunDetail detail)
    {
        Title = $"{detail.Summary.StrategyName} ({detail.Summary.RunId[..Math.Min(8, detail.Summary.RunId.Length)]})";
        StatusText = $"{detail.Summary.Status} {detail.Summary.Mode} run recorded at {detail.Summary.StartedAt.LocalDateTime:g}.";
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

        Parameters.Clear();
        foreach (var parameter in detail.Parameters.OrderBy(static entry => entry.Key, StringComparer.OrdinalIgnoreCase))
        {
            Parameters.Add(new ParameterItemViewModel(parameter.Key, parameter.Value));
        }

        OpenPortfolioCommand.NotifyCanExecuteChanged();
        OpenLedgerCommand.NotifyCanExecuteChanged();
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

    private static string FormatCurrency(decimal? value) => value.HasValue ? value.Value.ToString("C2") : "-";
    private static string FormatPercent(decimal? value) => value.HasValue ? value.Value.ToString("P2") : "-";
}

public sealed record ParameterItemViewModel(string Name, string Value);
