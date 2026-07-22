using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.Wpf.Models;
using Meridian.Wpf.Services;
using Meridian.Wpf.Workstation.Models;

namespace Meridian.Wpf.ViewModels;

public sealed class StrategyRunLedgerViewModel : BindableBase
{
    private readonly StrategyRunWorkspaceService _runService;
    private readonly NavigationService _navigationService;
    private string? _runId;
    private object? _parameter;
    private LedgerTrialBalanceLine? _selectedTrialBalanceLine;
    private InspectorPanelModel _selectedTrialBalanceInspector = BuildEmptyTrialBalanceInspector();

    public object? Parameter
    {
        get => _parameter;
        set
        {
            if (SetProperty(ref _parameter, value))
            {
                CurrentLoadTask = LoadFromParameterAsync(value);
                _ = CurrentLoadTask;
            }
        }
    }

    internal Task CurrentLoadTask { get; private set; } = Task.CompletedTask;

    public ObservableCollection<LedgerTrialBalanceLine> TrialBalance { get; } = [];
    public ObservableCollection<LedgerJournalLine> Journal { get; } = [];
    public WorkstationTableModel<LedgerTrialBalanceLine> TrialBalanceTable { get; }
    public WorkstationTableModel<LedgerJournalLine> JournalTable { get; }

    public LedgerTrialBalanceLine? SelectedTrialBalanceLine
    {
        get => _selectedTrialBalanceLine;
        set
        {
            if (SetProperty(ref _selectedTrialBalanceLine, value))
            {
                SelectedTrialBalanceInspector = value is null
                    ? BuildEmptyTrialBalanceInspector()
                    : BuildTrialBalanceInspector(value);
                RaiseSelectedTrialBalanceStateChanged();
            }
        }
    }

    public InspectorPanelModel SelectedTrialBalanceInspector
    {
        get => _selectedTrialBalanceInspector;
        private set => SetProperty(ref _selectedTrialBalanceInspector, value);
    }

    public bool HasSelectedTrialBalanceLine => SelectedTrialBalanceLine is not null;

    public bool CanOpenSelectedSecurity => SelectedTrialBalanceLine?.Security is not null ||
        !string.IsNullOrWhiteSpace(SelectedTrialBalanceLine?.Symbol);

    public string RunDrillInTooltip => string.IsNullOrWhiteSpace(_runId)
        ? "Select a retained strategy run before opening related run drill-ins."
        : $"Open retained run drill-ins for {_runId}.";

    public string SelectedSecurityTooltip => SelectedTrialBalanceLine switch
    {
        null => "Select a trial-balance line before opening Security Master.",
        { Security: { } security } => $"Open Security Master for {security.DisplayName}.",
        { Symbol: { Length: > 0 } symbol } => $"Open Security Master lookup for {symbol}.",
        _ => "Select a security-linked trial-balance line before opening Security Master."
    };

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
    public IRelayCommand OpenSelectedSecurityCommand { get; }

    internal StrategyRunLedgerViewModel(
        StrategyRunWorkspaceService runService,
        NavigationService navigationService)
    {
        _runService = runService;
        _navigationService = navigationService;
        TrialBalanceTable = new WorkstationTableModel<LedgerTrialBalanceLine>(
            TrialBalance,
            [
                new("Account", nameof(LedgerTrialBalanceLine.AccountName), 170),
                new("Type", nameof(LedgerTrialBalanceLine.AccountType), 100),
                new("Symbol", nameof(LedgerTrialBalanceLine.Symbol), 90),
                new("Security", "Security.DisplayName", 170),
                new("Asset Class", "Security.AssetClass", 105),
                new("Identifier", "Security.PrimaryIdentifier", 120),
                new("Fund", "Dimensions.FundId", 120),
                new("Entity", "Dimensions.EntityId", 120),
                new("Sleeve", "Dimensions.SleeveId", 120),
                new("Strategy", "Dimensions.StrategyId", 130),
                new("Investor", "Dimensions.InvestorId", 140),
                new("Capital Account", "Dimensions.CapitalAccountId", 160),
                new("Instrument", "Dimensions.InstrumentId", 180),
                new("Tax Lot", "Dimensions.TaxLotId", 135),
                new("Cost Center", "Dimensions.CostCenterId", 120),
                new("Counterparty", "Dimensions.CounterpartyId", 140),
                new("Organization", "Dimensions.OrganizationId", 145),
                new("Portfolio", "Dimensions.PortfolioId", 145),
                new("Book", "Dimensions.BookId", 125),
                new("Account Scope", "Dimensions.AccountId", 145),
                new("Customer", "Dimensions.CustomerId", 140),
                new("Vendor", "Dimensions.VendorId", 140),
                new("Project", "Dimensions.ProjectId", 140),
                new("Financial Account", nameof(LedgerTrialBalanceLine.FinancialAccountId), 150),
                new("Balance", nameof(LedgerTrialBalanceLine.Balance), 115, "{0:C2}"),
                new("Entries", nameof(LedgerTrialBalanceLine.EntryCount), 75, "{0:N0}")
            ],
            "Run trial balance",
            "No trial-balance lines retained",
            "Select a retained strategy run with ledger output before reviewing account balances.");
        JournalTable = new WorkstationTableModel<LedgerJournalLine>(
            Journal,
            [
                new("Timestamp", nameof(LedgerJournalLine.Timestamp), 150, "{0:g}"),
                new("Description", nameof(LedgerJournalLine.Description), 320),
                new("Fund", "Dimensions.FundId", 120),
                new("Entity", "Dimensions.EntityId", 120),
                new("Sleeve", "Dimensions.SleeveId", 120),
                new("Strategy", "Dimensions.StrategyId", 130),
                new("Investor", "Dimensions.InvestorId", 140),
                new("Capital Account", "Dimensions.CapitalAccountId", 160),
                new("Instrument", "Dimensions.InstrumentId", 180),
                new("Tax Lot", "Dimensions.TaxLotId", 135),
                new("Cost Center", "Dimensions.CostCenterId", 120),
                new("Counterparty", "Dimensions.CounterpartyId", 140),
                new("Organization", "Dimensions.OrganizationId", 145),
                new("Portfolio", "Dimensions.PortfolioId", 145),
                new("Book", "Dimensions.BookId", 125),
                new("Account Scope", "Dimensions.AccountId", 145),
                new("Customer", "Dimensions.CustomerId", 140),
                new("Vendor", "Dimensions.VendorId", 140),
                new("Project", "Dimensions.ProjectId", 140),
                new("Debits", nameof(LedgerJournalLine.TotalDebits), 115, "{0:C2}"),
                new("Credits", nameof(LedgerJournalLine.TotalCredits), 115, "{0:C2}"),
                new("Lines", nameof(LedgerJournalLine.LineCount), 70, "{0:N0}")
            ],
            "Run journal",
            "No journal entries retained",
            "Select a retained strategy run with ledger output before reviewing posted journal entries.");

        OpenBrowserCommand = new RelayCommand(() => _navigationService.NavigateTo("StrategyRuns"));
        OpenRunDetailCommand = new RelayCommand(OpenRunDetail, () => !string.IsNullOrWhiteSpace(_runId));
        OpenPortfolioCommand = new RelayCommand(OpenPortfolio, () => !string.IsNullOrWhiteSpace(_runId));
        OpenCashFlowCommand = new RelayCommand(OpenCashFlow, () => !string.IsNullOrWhiteSpace(_runId));
        OpenSelectedSecurityCommand = new RelayCommand(OpenSelectedSecurity, () => CanOpenSelectedSecurity);
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
        var ledger = await _runService.GetLedgerAsync(runId, ct);
        if (ledger is null)
        {
            ApplyLedgerUnavailableState(runId);
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

        SelectedTrialBalanceLine = TrialBalance.FirstOrDefault();

        Journal.Clear();
        foreach (var line in ledger.Journal)
        {
            Journal.Add(line);
        }

        OpenRunDetailCommand.NotifyCanExecuteChanged();
        OpenPortfolioCommand.NotifyCanExecuteChanged();
        OpenCashFlowCommand.NotifyCanExecuteChanged();
        OpenSelectedSecurityCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(RunDrillInTooltip));
    }

    private void ApplyNoRunSelectedState()
    {
        _runId = null;
        ResetLedgerState();
        StatusText = "Select a strategy run to inspect ledger state.";
        NotifyRunCommandsChanged();
    }

    private void ApplyLedgerUnavailableState(string runId)
    {
        _runId = null;
        ResetLedgerState();
        StatusText = $"No ledger is available for run '{runId}'.";
        NotifyRunCommandsChanged();
    }

    private void ResetLedgerState()
    {
        Title = "Ledger Drill-In";
        AsOfText = "-";
        JournalEntriesText = "-";
        LedgerEntriesText = "-";
        AssetBalanceText = "-";
        EquityBalanceText = "-";
        RevenueBalanceText = "-";
        ExpenseBalanceText = "-";
        SecurityResolvedText = "-";
        SecurityMissingText = "-";
        TrialBalance.Clear();
        Journal.Clear();
        SelectedTrialBalanceLine = null;
    }

    private void NotifyRunCommandsChanged()
    {
        OpenRunDetailCommand.NotifyCanExecuteChanged();
        OpenPortfolioCommand.NotifyCanExecuteChanged();
        OpenCashFlowCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(RunDrillInTooltip));
    }

    private void RaiseSelectedTrialBalanceStateChanged()
    {
        OnPropertyChanged(nameof(HasSelectedTrialBalanceLine));
        OnPropertyChanged(nameof(CanOpenSelectedSecurity));
        OnPropertyChanged(nameof(SelectedSecurityTooltip));
        OpenSelectedSecurityCommand.NotifyCanExecuteChanged();
    }

    internal static InspectorPanelModel BuildTrialBalanceInspector(LedgerTrialBalanceLine selected)
    {
        var security = selected.Security;
        var securityTitle = security?.DisplayName
            ?? (string.IsNullOrWhiteSpace(selected.Symbol) ? "No security symbol" : "Security mapping unavailable");
        var securityDetail = security is null
            ? string.IsNullOrWhiteSpace(selected.Symbol)
                ? "This account balance is not linked to a Security Master symbol."
                : "No Security Master record is attached to this trial-balance line. Open Security Master lookup before relying on downstream asset coverage."
            : $"Security Master resolves this account balance to {security.DisplayName}.";
        var coverageValue = security is null
            ? string.IsNullOrWhiteSpace(selected.Symbol) ? "Not security-linked" : "Mapping needed"
            : security.CoverageStatus.ToString();
        var coverageTone = security is null
            ? WorkspaceTone.Warning
            : security.CoverageStatus == WorkstationSecurityCoverageStatus.Resolved
                ? WorkspaceTone.Success
                : WorkspaceTone.Warning;

        return new InspectorPanelModel
        {
            Title = selected.AccountName,
            Subtitle = securityTitle,
            Detail = securityDetail,
            Badge = new WorkstationBadgeModel("Coverage", coverageValue, "\uE8D4", coverageTone),
            Facts =
            [
                new("Account type", selected.AccountType),
                new("Balance", selected.Balance.ToString("C2")),
                new("Entries", selected.EntryCount.ToString("N0")),
                new("Symbol", string.IsNullOrWhiteSpace(selected.Symbol) ? "-" : selected.Symbol),
                new("Financial account", string.IsNullOrWhiteSpace(selected.FinancialAccountId) ? "-" : selected.FinancialAccountId),
                new("Asset class", security?.AssetClass ?? "-", security?.SubType ?? string.Empty),
                new("Identifier", security?.PrimaryIdentifier ?? "-", security?.MatchedIdentifierKind ?? string.Empty),
                new("Fund", DisplayDimension(selected.Dimensions?.FundId)),
                new("Entity", DisplayDimension(selected.Dimensions?.EntityId)),
                new("Sleeve", DisplayDimension(selected.Dimensions?.SleeveId)),
                new("Strategy", DisplayDimension(selected.Dimensions?.StrategyId)),
                new("Investor", DisplayDimension(selected.Dimensions?.InvestorId)),
                new("Capital account", DisplayDimension(selected.Dimensions?.CapitalAccountId)),
                new("Instrument", selected.Dimensions?.InstrumentId?.ToString("D") ?? "-"),
                new("Tax lot", DisplayDimension(selected.Dimensions?.TaxLotId)),
                new("Cost center", DisplayDimension(selected.Dimensions?.CostCenterId)),
                new("Counterparty", DisplayDimension(selected.Dimensions?.CounterpartyId)),
                new("Organization", DisplayDimension(selected.Dimensions?.OrganizationId)),
                new("Portfolio", DisplayDimension(selected.Dimensions?.PortfolioId)),
                new("Book", DisplayDimension(selected.Dimensions?.BookId)),
                new("Account scope", DisplayDimension(selected.Dimensions?.AccountId)),
                new("Customer", DisplayDimension(selected.Dimensions?.CustomerId)),
                new("Vendor", DisplayDimension(selected.Dimensions?.VendorId)),
                new("Project", DisplayDimension(selected.Dimensions?.ProjectId)),
                new("External GL", BuildExternalGlDimensionText(selected.Dimensions)),
                new("Scope", BuildScopeText(selected))
            ]
        };
    }

    private static InspectorPanelModel BuildEmptyTrialBalanceInspector()
        => new()
        {
            Title = "No trial-balance line selected",
            Subtitle = "Run ledger inspector",
            Detail = "Select a retained trial-balance line to review account balance, Security Master coverage, scope, and entry count."
        };

    private static string BuildScopeText(LedgerTrialBalanceLine selected)
    {
        var dimensionScope = BuildDimensionScopeText(selected.Dimensions);
        if (!string.IsNullOrWhiteSpace(dimensionScope))
        {
            return dimensionScope;
        }

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
            ? "Run ledger scope"
            : string.Join(" / ", scopes);
    }

    private static string BuildDimensionScopeText(LedgerDimensionSetDto? dimensions)
    {
        if (dimensions is null)
        {
            return string.Empty;
        }

        var scopes = new[]
            {
                dimensions.FundId,
                dimensions.EntityId,
                dimensions.SleeveId,
                dimensions.StrategyId,
                dimensions.InvestorId,
                dimensions.CapitalAccountId,
                dimensions.InstrumentId?.ToString("D"),
                dimensions.TaxLotId,
                dimensions.CostCenterId,
                dimensions.CounterpartyId,
                dimensions.OrganizationId,
                dimensions.PortfolioId,
                dimensions.BookId,
                dimensions.AccountId,
                dimensions.CustomerId,
                dimensions.VendorId,
                dimensions.ProjectId
            }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();

        return scopes.Length == 0
            ? string.Empty
            : string.Join(" / ", scopes);
    }

    private static string BuildExternalGlDimensionText(LedgerDimensionSetDto? dimensions)
    {
        var externalDimensions = dimensions?.ExternalGlDimensions;
        if (externalDimensions is null || externalDimensions.Count == 0)
        {
            return "-";
        }

        return string.Join(
            ", ",
            externalDimensions
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
                .OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Select(static pair => $"{pair.Key.Trim()}: {pair.Value.Trim()}"));
    }

    private static string DisplayDimension(string? value)
        => string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();

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

    private void OpenSelectedSecurity()
    {
        if (SelectedTrialBalanceLine?.Security?.SecurityId is Guid securityId)
        {
            _navigationService.NavigateTo("SecurityMaster", securityId);
            return;
        }

        if (!string.IsNullOrWhiteSpace(SelectedTrialBalanceLine?.Symbol))
        {
            _navigationService.NavigateTo("SecurityMaster", SelectedTrialBalanceLine.Symbol);
        }
    }
}
