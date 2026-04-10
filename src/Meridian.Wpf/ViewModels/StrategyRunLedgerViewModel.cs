using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Meridian.Contracts.Workstation;
using Meridian.Wpf.Services;

namespace Meridian.Wpf.ViewModels;

public sealed class StrategyRunLedgerViewModel : BindableBase
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

    public ObservableCollection<LedgerTrialBalanceLine> TrialBalance { get; } = [];
    public ObservableCollection<LedgerJournalLine> Journal { get; } = [];

    private string _title = "Ledger Drill-In";
    public string Title
    {
        get => _title;
        private set => SetProperty(ref _title, value);
    }

    private string _statusText = "Select a strategy run to inspect ledger state.";
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

    private string _journalEntriesText = "-";
    public string JournalEntriesText
    {
        get => _journalEntriesText;
        private set => SetProperty(ref _journalEntriesText, value);
    }

    private string _ledgerEntriesText = "-";
    public string LedgerEntriesText
    {
        get => _ledgerEntriesText;
        private set => SetProperty(ref _ledgerEntriesText, value);
    }

    private string _assetBalanceText = "-";
    public string AssetBalanceText
    {
        get => _assetBalanceText;
        private set => SetProperty(ref _assetBalanceText, value);
    }

    private string _equityBalanceText = "-";
    public string EquityBalanceText
    {
        get => _equityBalanceText;
        private set => SetProperty(ref _equityBalanceText, value);
    }

    private string _revenueBalanceText = "-";
    public string RevenueBalanceText
    {
        get => _revenueBalanceText;
        private set => SetProperty(ref _revenueBalanceText, value);
    }

    private string _expenseBalanceText = "-";
    public string ExpenseBalanceText
    {
        get => _expenseBalanceText;
        private set => SetProperty(ref _expenseBalanceText, value);
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
    public IRelayCommand OpenPortfolioCommand { get; }
    public IRelayCommand OpenCashFlowCommand { get; }

    internal StrategyRunLedgerViewModel(
        StrategyRunWorkspaceService runService,
        NavigationService navigationService)
    {
        _runService = runService;
        _navigationService = navigationService;
        OpenBrowserCommand = new RelayCommand(() => _navigationService.NavigateTo("StrategyRuns"));
        OpenRunDetailCommand = new RelayCommand(OpenRunDetail, () => !string.IsNullOrWhiteSpace(_runId));
        OpenPortfolioCommand = new RelayCommand(OpenPortfolio, () => !string.IsNullOrWhiteSpace(_runId));
        OpenCashFlowCommand = new RelayCommand(OpenCashFlow, () => !string.IsNullOrWhiteSpace(_runId));
    }

    private async Task LoadFromParameterAsync(object? parameter, CancellationToken ct = default)
    {
        var runId = parameter as string;
        if (string.IsNullOrWhiteSpace(runId))
        {
            StatusText = "Select a strategy run to inspect ledger state.";
            return;
        }

        _runId = runId;
        var ledger = await _runService.GetLedgerAsync(runId, ct);
        if (ledger is null)
        {
            StatusText = $"No ledger is available for run '{runId}'.";
            return;
        }

        Title = $"Ledger {ledger.LedgerReference}";
        StatusText = ledger.SecurityMissingCount > 0
            ? $"{ledger.JournalEntryCount} journal entries and {ledger.TrialBalance.Count} trial-balance lines loaded. {ledger.SecurityMissingCount} symbol(s) still need Security Master mapping."
            : $"{ledger.JournalEntryCount} journal entries and {ledger.TrialBalance.Count} trial-balance lines loaded.";
        AsOfText = ledger.AsOf.LocalDateTime.ToString("g");
        JournalEntriesText = ledger.JournalEntryCount.ToString("N0");
        LedgerEntriesText = ledger.LedgerEntryCount.ToString("N0");
        AssetBalanceText = ledger.AssetBalance.ToString("C2");
        EquityBalanceText = ledger.EquityBalance.ToString("C2");
        RevenueBalanceText = ledger.RevenueBalance.ToString("C2");
        ExpenseBalanceText = ledger.ExpenseBalance.ToString("C2");
        SecurityResolvedText = ledger.SecurityResolvedCount.ToString("N0");
        SecurityMissingText = ledger.SecurityMissingCount.ToString("N0");

        TrialBalance.Clear();
        foreach (var line in ledger.TrialBalance)
        {
            TrialBalance.Add(line);
        }

        Journal.Clear();
        foreach (var line in ledger.Journal)
        {
            Journal.Add(line);
        }

        OpenRunDetailCommand.NotifyCanExecuteChanged();
        OpenPortfolioCommand.NotifyCanExecuteChanged();
        OpenCashFlowCommand.NotifyCanExecuteChanged();
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

    private void OpenCashFlow()
    {
        if (!string.IsNullOrWhiteSpace(_runId))
        {
            _navigationService.NavigateTo("RunCashFlow", _runId);
        }
    }
}
