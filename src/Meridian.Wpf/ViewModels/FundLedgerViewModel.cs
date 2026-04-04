using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Meridian.Contracts.Workstation;
using Meridian.Wpf.Services;

namespace Meridian.Wpf.ViewModels;

public sealed class FundLedgerViewModel : BindableBase, IDisposable
{
    private readonly FundLedgerReadService _fundLedgerReadService;
    private readonly FundContextService _fundContextService;
    private readonly NavigationService _navigationService;
    private string _title = "Fund Ledger";
    private string _statusText = "Select a fund profile to inspect consolidated ledger state.";
    private string _asOfText = "-";
    private string _journalEntriesText = "-";
    private string _ledgerEntriesText = "-";
    private string _assetBalanceText = "-";
    private string _equityBalanceText = "-";
    private string _revenueBalanceText = "-";
    private string _expenseBalanceText = "-";
    private string _entityCountText = "-";
    private string _sleeveCountText = "-";
    private string _vehicleCountText = "-";

    public FundLedgerViewModel(
        FundLedgerReadService fundLedgerReadService,
        FundContextService fundContextService,
        NavigationService navigationService)
    {
        _fundLedgerReadService = fundLedgerReadService ?? throw new ArgumentNullException(nameof(fundLedgerReadService));
        _fundContextService = fundContextService ?? throw new ArgumentNullException(nameof(fundContextService));
        _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));

        TrialBalance = [];
        Journal = [];

        RefreshCommand = new AsyncRelayCommand(LoadAsync);
        OpenGovernanceCommand = new RelayCommand(() => _navigationService.NavigateTo("GovernanceShell"));
        OpenRunLedgerCommand = new RelayCommand(() => _navigationService.NavigateTo("RunLedger"));
        SwitchFundCommand = new RelayCommand(() => _fundContextService.RequestSwitchFund());

        _fundContextService.ActiveFundProfileChanged += OnActiveFundProfileChanged;
    }

    public ObservableCollection<FundTrialBalanceLine> TrialBalance { get; }

    public ObservableCollection<FundJournalLine> Journal { get; }

    public IAsyncRelayCommand RefreshCommand { get; }

    public IRelayCommand OpenGovernanceCommand { get; }

    public IRelayCommand OpenRunLedgerCommand { get; }

    public IRelayCommand SwitchFundCommand { get; }

    public string Title
    {
        get => _title;
        private set => SetProperty(ref _title, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string AsOfText
    {
        get => _asOfText;
        private set => SetProperty(ref _asOfText, value);
    }

    public string JournalEntriesText
    {
        get => _journalEntriesText;
        private set => SetProperty(ref _journalEntriesText, value);
    }

    public string LedgerEntriesText
    {
        get => _ledgerEntriesText;
        private set => SetProperty(ref _ledgerEntriesText, value);
    }

    public string AssetBalanceText
    {
        get => _assetBalanceText;
        private set => SetProperty(ref _assetBalanceText, value);
    }

    public string EquityBalanceText
    {
        get => _equityBalanceText;
        private set => SetProperty(ref _equityBalanceText, value);
    }

    public string RevenueBalanceText
    {
        get => _revenueBalanceText;
        private set => SetProperty(ref _revenueBalanceText, value);
    }

    public string ExpenseBalanceText
    {
        get => _expenseBalanceText;
        private set => SetProperty(ref _expenseBalanceText, value);
    }

    public string EntityCountText
    {
        get => _entityCountText;
        private set => SetProperty(ref _entityCountText, value);
    }

    public string SleeveCountText
    {
        get => _sleeveCountText;
        private set => SetProperty(ref _sleeveCountText, value);
    }

    public string VehicleCountText
    {
        get => _vehicleCountText;
        private set => SetProperty(ref _vehicleCountText, value);
    }

    public async Task LoadAsync()
    {
        var activeFund = _fundContextService.CurrentFundProfile;
        if (activeFund is null)
        {
            Title = "Fund Ledger";
            StatusText = "Select a fund profile to inspect consolidated ledger state.";
            TrialBalance.Clear();
            Journal.Clear();
            return;
        }

        var summary = await _fundLedgerReadService.GetAsync(new FundLedgerQuery(
            FundProfileId: activeFund.FundProfileId,
            ScopeKind: activeFund.DefaultLedgerScope));

        if (summary is null)
        {
            Title = $"Fund Ledger · {activeFund.DisplayName}";
            StatusText = "The selected fund profile could not be loaded.";
            TrialBalance.Clear();
            Journal.Clear();
            return;
        }

        Title = $"Fund Ledger · {summary.FundDisplayName}";
        StatusText = summary.JournalEntryCount == 0
            ? "No fund ledger activity has been recorded yet for this profile."
            : $"{summary.JournalEntryCount} journal entries and {summary.TrialBalance.Count} trial-balance lines loaded.";

        AsOfText = summary.AsOf.LocalDateTime.ToString("g");
        JournalEntriesText = summary.JournalEntryCount.ToString("N0");
        LedgerEntriesText = summary.LedgerEntryCount.ToString("N0");
        AssetBalanceText = summary.AssetBalance.ToString("C2");
        EquityBalanceText = summary.EquityBalance.ToString("C2");
        RevenueBalanceText = summary.RevenueBalance.ToString("C2");
        ExpenseBalanceText = summary.ExpenseBalance.ToString("C2");
        EntityCountText = summary.EntityCount.ToString("N0");
        SleeveCountText = summary.SleeveCount.ToString("N0");
        VehicleCountText = summary.VehicleCount.ToString("N0");

        TrialBalance.Clear();
        foreach (var line in summary.TrialBalance)
        {
            TrialBalance.Add(line);
        }

        Journal.Clear();
        foreach (var line in summary.Journal)
        {
            Journal.Add(line);
        }
    }

    public void Dispose()
    {
        _fundContextService.ActiveFundProfileChanged -= OnActiveFundProfileChanged;
    }

    private async void OnActiveFundProfileChanged(object? sender, FundProfileChangedEventArgs e)
    {
        await LoadAsync();
    }
}
