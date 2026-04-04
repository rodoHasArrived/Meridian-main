using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Meridian.Contracts.Workstation;
using Meridian.Wpf.Models;
using Meridian.Wpf.Services;

namespace Meridian.Wpf.ViewModels;

public sealed class FundLedgerViewModel : BindableBase, IDisposable
{
    private readonly FundLedgerReadService _fundLedgerReadService;
    private readonly FundContextService _fundContextService;
    private readonly NavigationService _navigationService;
    private readonly FundAccountReadService _fundAccountReadService;
    private readonly CashFinancingReadService _cashFinancingReadService;
    private readonly ReconciliationReadService _reconciliationReadService;
    private readonly StrategyRunWorkspaceService _runWorkspaceService;

    private string _title = "Fund Operations";
    private string _statusText = "Select a fund profile to inspect fund operations.";
    private string _overviewStatusText = "Select a fund profile to unlock governance fund operations.";
    private string _portfolioStatusText = "No fund-scoped portfolio posture is available yet.";
    private string _bankingStatusText = "No banking snapshots are available yet.";
    private string _reconciliationStatusText = "No reconciliation runs are available yet.";
    private string _securityCoverageText = "Security Master coverage has not been evaluated yet.";
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
    private string _workspaceAsOfText = "-";
    private string _totalAccountsText = "0";
    private string _bankAccountsText = "0";
    private string _brokerageAccountsText = "0";
    private string _custodyAccountsText = "0";
    private string _totalCashText = "-";
    private string _grossExposureText = "-";
    private string _netExposureText = "-";
    private string _totalEquityText = "-";
    private string _financingCostText = "-";
    private string _pendingSettlementText = "-";
    private string _openBreaksText = "0";
    private string _reconciliationRunsText = "0";
    private int _selectedTabIndex;
    private FundAccountSummary? _selectedAccount;
    private FundPortfolioPosition? _selectedPortfolioPosition;

    public FundLedgerViewModel(
        FundLedgerReadService fundLedgerReadService,
        FundContextService fundContextService,
        NavigationService navigationService,
        FundAccountReadService fundAccountReadService,
        CashFinancingReadService cashFinancingReadService,
        ReconciliationReadService reconciliationReadService,
        StrategyRunWorkspaceService runWorkspaceService)
    {
        _fundLedgerReadService = fundLedgerReadService ?? throw new ArgumentNullException(nameof(fundLedgerReadService));
        _fundContextService = fundContextService ?? throw new ArgumentNullException(nameof(fundContextService));
        _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
        _fundAccountReadService = fundAccountReadService ?? throw new ArgumentNullException(nameof(fundAccountReadService));
        _cashFinancingReadService = cashFinancingReadService ?? throw new ArgumentNullException(nameof(cashFinancingReadService));
        _reconciliationReadService = reconciliationReadService ?? throw new ArgumentNullException(nameof(reconciliationReadService));
        _runWorkspaceService = runWorkspaceService ?? throw new ArgumentNullException(nameof(runWorkspaceService));

        TrialBalance = [];
        Journal = [];
        Accounts = [];
        BankSnapshots = [];
        PortfolioPositions = [];
        ReconciliationRuns = [];
        CashFinancingHighlights = [];
        AuditTrail = [];

        RefreshCommand = new AsyncRelayCommand(LoadAsync);
        OpenGovernanceCommand = new RelayCommand(() => _navigationService.NavigateTo("GovernanceShell"));
        OpenRunLedgerCommand = new RelayCommand(OpenLatestRunLedger);
        SwitchFundCommand = new RelayCommand(() => _fundContextService.RequestSwitchFund());
        OpenAccountsCommand = new RelayCommand(() => _navigationService.NavigateTo("FundAccounts"));
        OpenBankingCommand = new RelayCommand(() => _navigationService.NavigateTo("FundBanking"));
        OpenPortfolioCommand = new RelayCommand(() => _navigationService.NavigateTo("FundPortfolio"));
        OpenCashFinancingCommand = new RelayCommand(() => _navigationService.NavigateTo("FundCashFinancing"));
        OpenTrialBalanceCommand = new RelayCommand(() => _navigationService.NavigateTo("FundTrialBalance"));
        OpenReconciliationCommand = new RelayCommand(() => _navigationService.NavigateTo("FundReconciliation"));
        OpenAuditTrailCommand = new RelayCommand(() => _navigationService.NavigateTo("FundAuditTrail"));
        OpenSelectedAccountPortfolioCommand = new RelayCommand(OpenSelectedAccountPortfolio, () => SelectedAccount is not null);
        OpenSelectedPortfolioSecurityCommand = new RelayCommand(OpenSelectedPortfolioSecurity, () => SelectedPortfolioPosition is not null);

        _fundContextService.ActiveFundProfileChanged += OnActiveFundProfileChanged;
    }

    public object? Parameter
    {
        get => null;
        set
        {
            if (value is FundOperationsNavigationContext context)
            {
                _ = LoadAsync(context);
                return;
            }

            _ = LoadAsync();
        }
    }

    public ObservableCollection<FundTrialBalanceLine> TrialBalance { get; }

    public ObservableCollection<FundJournalLine> Journal { get; }

    public ObservableCollection<FundAccountSummary> Accounts { get; }

    public ObservableCollection<BankAccountSnapshot> BankSnapshots { get; }

    public ObservableCollection<FundPortfolioPosition> PortfolioPositions { get; }

    public ObservableCollection<FundReconciliationItem> ReconciliationRuns { get; }

    public ObservableCollection<string> CashFinancingHighlights { get; }

    public ObservableCollection<FundAuditEntry> AuditTrail { get; }

    public IAsyncRelayCommand RefreshCommand { get; }

    public IRelayCommand OpenGovernanceCommand { get; }

    public IRelayCommand OpenRunLedgerCommand { get; }

    public IRelayCommand SwitchFundCommand { get; }

    public IRelayCommand OpenAccountsCommand { get; }

    public IRelayCommand OpenBankingCommand { get; }

    public IRelayCommand OpenPortfolioCommand { get; }

    public IRelayCommand OpenCashFinancingCommand { get; }

    public IRelayCommand OpenTrialBalanceCommand { get; }

    public IRelayCommand OpenReconciliationCommand { get; }

    public IRelayCommand OpenAuditTrailCommand { get; }

    public IRelayCommand OpenSelectedAccountPortfolioCommand { get; }

    public IRelayCommand OpenSelectedPortfolioSecurityCommand { get; }

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

    public string OverviewStatusText
    {
        get => _overviewStatusText;
        private set => SetProperty(ref _overviewStatusText, value);
    }

    public string PortfolioStatusText
    {
        get => _portfolioStatusText;
        private set => SetProperty(ref _portfolioStatusText, value);
    }

    public string BankingStatusText
    {
        get => _bankingStatusText;
        private set => SetProperty(ref _bankingStatusText, value);
    }

    public string ReconciliationStatusText
    {
        get => _reconciliationStatusText;
        private set => SetProperty(ref _reconciliationStatusText, value);
    }

    public string SecurityCoverageText
    {
        get => _securityCoverageText;
        private set => SetProperty(ref _securityCoverageText, value);
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

    public string WorkspaceAsOfText
    {
        get => _workspaceAsOfText;
        private set => SetProperty(ref _workspaceAsOfText, value);
    }

    public string TotalAccountsText
    {
        get => _totalAccountsText;
        private set => SetProperty(ref _totalAccountsText, value);
    }

    public string BankAccountsText
    {
        get => _bankAccountsText;
        private set => SetProperty(ref _bankAccountsText, value);
    }

    public string BrokerageAccountsText
    {
        get => _brokerageAccountsText;
        private set => SetProperty(ref _brokerageAccountsText, value);
    }

    public string CustodyAccountsText
    {
        get => _custodyAccountsText;
        private set => SetProperty(ref _custodyAccountsText, value);
    }

    public string TotalCashText
    {
        get => _totalCashText;
        private set => SetProperty(ref _totalCashText, value);
    }

    public string GrossExposureText
    {
        get => _grossExposureText;
        private set => SetProperty(ref _grossExposureText, value);
    }

    public string NetExposureText
    {
        get => _netExposureText;
        private set => SetProperty(ref _netExposureText, value);
    }

    public string TotalEquityText
    {
        get => _totalEquityText;
        private set => SetProperty(ref _totalEquityText, value);
    }

    public string FinancingCostText
    {
        get => _financingCostText;
        private set => SetProperty(ref _financingCostText, value);
    }

    public string PendingSettlementText
    {
        get => _pendingSettlementText;
        private set => SetProperty(ref _pendingSettlementText, value);
    }

    public string OpenBreaksText
    {
        get => _openBreaksText;
        private set => SetProperty(ref _openBreaksText, value);
    }

    public string ReconciliationRunsText
    {
        get => _reconciliationRunsText;
        private set => SetProperty(ref _reconciliationRunsText, value);
    }

    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set => SetProperty(ref _selectedTabIndex, value);
    }

    public FundAccountSummary? SelectedAccount
    {
        get => _selectedAccount;
        set
        {
            if (SetProperty(ref _selectedAccount, value))
            {
                OpenSelectedAccountPortfolioCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public FundPortfolioPosition? SelectedPortfolioPosition
    {
        get => _selectedPortfolioPosition;
        set
        {
            if (SetProperty(ref _selectedPortfolioPosition, value))
            {
                OpenSelectedPortfolioSecurityCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public async Task LoadAsync()
    {
        await LoadAsync(null);
    }

    public async Task LoadAsync(FundOperationsNavigationContext? context, CancellationToken ct = default)
    {
        var activeFund = await ResolveActiveFundAsync(context, ct);
        ApplySelectedTab(context);

        if (activeFund is null)
        {
            ResetEmptyState();
            return;
        }

        var ledgerTask = _fundLedgerReadService.GetAsync(new FundLedgerQuery(
            FundProfileId: activeFund.FundProfileId,
            ScopeKind: activeFund.DefaultLedgerScope), ct);
        var accountsTask = _fundAccountReadService.GetAccountsAsync(activeFund.FundProfileId, ct);
        var bankSnapshotsTask = _fundAccountReadService.GetBankSnapshotsAsync(activeFund.FundProfileId, ct);
        var cashTask = _cashFinancingReadService.GetAsync(activeFund.FundProfileId, activeFund.BaseCurrency, ct);
        var reconciliationTask = _reconciliationReadService.GetAsync(activeFund.FundProfileId, ct);
        var portfolioTask = BuildFundPortfolioAsync(activeFund.FundProfileId, ct);

        await Task.WhenAll(ledgerTask, accountsTask, bankSnapshotsTask, cashTask, reconciliationTask, portfolioTask);

        var ledger = ledgerTask.Result;
        var accounts = accountsTask.Result;
        var bankSnapshots = bankSnapshotsTask.Result;
        var cashSummary = cashTask.Result;
        var reconciliation = reconciliationTask.Result;
        var portfolioPositions = portfolioTask.Result;

        Title = $"Fund Operations · {activeFund.DisplayName}";
        StatusText = accounts.Count == 0 && ledger?.JournalEntryCount is not > 0
            ? "Fund operations are ready, but no linked account or ledger activity has been recorded yet."
            : "Governance fund operations are loaded for the active fund profile.";

        ApplyLedger(ledger);
        ApplyAccounts(accounts, context);
        ApplyBankSnapshots(bankSnapshots);
        ApplyCashSummary(cashSummary);
        ApplyPortfolio(portfolioPositions);
        ApplyReconciliation(reconciliation);
        BuildWorkspaceSummary(activeFund, ledger, accounts, cashSummary, reconciliation);
        BuildAuditTrail(ledger, reconciliation);
    }

    public void Dispose()
    {
        _fundContextService.ActiveFundProfileChanged -= OnActiveFundProfileChanged;
    }

    private async Task<FundProfileDetail?> ResolveActiveFundAsync(
        FundOperationsNavigationContext? context,
        CancellationToken ct)
    {
        if (context is { FundProfileId: not null } &&
            !string.Equals(_fundContextService.CurrentFundProfile?.FundProfileId, context.FundProfileId, StringComparison.OrdinalIgnoreCase))
        {
            await _fundContextService.SelectFundProfileAsync(context.FundProfileId);
        }

        return _fundContextService.CurrentFundProfile;
    }

    private void ApplySelectedTab(FundOperationsNavigationContext? context)
    {
        if (context is not null)
        {
            SelectedTabIndex = (int)context.Tab;
            return;
        }

        SelectedTabIndex = MapPageTagToTabIndex(_navigationService.GetCurrentPageTag());
    }

    private void ResetEmptyState()
    {
        Title = "Fund Operations";
        StatusText = "Select a fund profile to inspect fund operations.";
        OverviewStatusText = "Select a fund profile to unlock governance fund operations.";
        PortfolioStatusText = "No fund-scoped portfolio posture is available yet.";
        BankingStatusText = "No banking snapshots are available yet.";
        ReconciliationStatusText = "No reconciliation runs are available yet.";
        SecurityCoverageText = "Security Master coverage has not been evaluated yet.";
        WorkspaceAsOfText = "-";
        TotalAccountsText = "0";
        BankAccountsText = "0";
        BrokerageAccountsText = "0";
        CustodyAccountsText = "0";
        TotalCashText = "-";
        GrossExposureText = "-";
        NetExposureText = "-";
        TotalEquityText = "-";
        FinancingCostText = "-";
        PendingSettlementText = "-";
        OpenBreaksText = "0";
        ReconciliationRunsText = "0";
        TrialBalance.Clear();
        Journal.Clear();
        Accounts.Clear();
        SelectedAccount = null;
        BankSnapshots.Clear();
        PortfolioPositions.Clear();
        SelectedPortfolioPosition = null;
        ReconciliationRuns.Clear();
        CashFinancingHighlights.Clear();
        AuditTrail.Clear();
    }

    private void ApplyLedger(FundLedgerSummary? summary)
    {
        if (summary is null)
        {
            AsOfText = "-";
            JournalEntriesText = "0";
            LedgerEntriesText = "0";
            AssetBalanceText = "-";
            EquityBalanceText = "-";
            RevenueBalanceText = "-";
            ExpenseBalanceText = "-";
            EntityCountText = "0";
            SleeveCountText = "0";
            VehicleCountText = "0";
            TrialBalance.Clear();
            Journal.Clear();
            return;
        }

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

    private void ApplyAccounts(
        IReadOnlyList<FundAccountSummary> accounts,
        FundOperationsNavigationContext? context)
    {
        Accounts.Clear();
        foreach (var account in accounts)
        {
            Accounts.Add(account);
        }

        SelectedAccount = context?.AccountId is Guid accountId
            ? Accounts.FirstOrDefault(account => account.AccountId == accountId)
            : Accounts.FirstOrDefault();
    }

    private void ApplyBankSnapshots(IReadOnlyList<BankAccountSnapshot> snapshots)
    {
        BankSnapshots.Clear();
        foreach (var snapshot in snapshots)
        {
            BankSnapshots.Add(snapshot);
        }

        BankingStatusText = snapshots.Count == 0
            ? "No bank or brokerage statement history has been loaded yet."
            : $"{snapshots.Count} bank-facing account snapshot(s) loaded.";
    }

    private void ApplyCashSummary(CashFinancingSummary summary)
    {
        TotalCashText = summary.TotalCash.ToString("C2");
        GrossExposureText = summary.GrossExposure.ToString("C2");
        NetExposureText = summary.NetExposure.ToString("C2");
        TotalEquityText = summary.TotalEquity.ToString("C2");
        FinancingCostText = summary.FinancingCost.ToString("C2");
        PendingSettlementText = summary.PendingSettlement.ToString("C2");

        CashFinancingHighlights.Clear();
        foreach (var highlight in summary.Highlights)
        {
            CashFinancingHighlights.Add(highlight);
        }
    }

    private void ApplyPortfolio(IReadOnlyList<FundPortfolioPosition> positions)
    {
        PortfolioPositions.Clear();
        foreach (var position in positions)
        {
            PortfolioPositions.Add(position);
        }

        SelectedPortfolioPosition = PortfolioPositions.FirstOrDefault();

        PortfolioStatusText = positions.Count == 0
            ? "No fund-scoped positions are available yet. Record a run or import a portfolio to populate this tab."
            : $"{positions.Count} aggregated position row(s) are contributing to fund portfolio posture.";

        var resolved = positions.Count(position => position.HasSecurityCoverage);
        var unresolved = positions.Count - resolved;
        SecurityCoverageText = positions.Count == 0
            ? "Security Master coverage will appear once fund-scoped positions are available."
            : unresolved == 0
                ? $"All {resolved} aggregated position(s) are mapped into Security Master."
                : $"{resolved} aggregated position(s) are mapped and {unresolved} still need Security Master coverage.";
    }

    private void ApplyReconciliation(ReconciliationSummary summary)
    {
        OpenBreaksText = summary.OpenBreakCount.ToString("N0");
        ReconciliationRunsText = summary.RunCount.ToString("N0");

        ReconciliationRuns.Clear();
        foreach (var item in summary.RecentRuns)
        {
            ReconciliationRuns.Add(item);
        }

        ReconciliationStatusText = summary.RunCount == 0
            ? "No account reconciliation runs have been recorded yet."
            : $"{summary.RunCount} reconciliation run(s) loaded with {summary.OpenBreakCount} open break(s).";
    }

    private void BuildWorkspaceSummary(
        FundProfileDetail activeFund,
        FundLedgerSummary? ledger,
        IReadOnlyList<FundAccountSummary> accounts,
        CashFinancingSummary cashSummary,
        ReconciliationSummary reconciliation)
    {
        var securityResolvedCount = PortfolioPositions.Count(position => position.HasSecurityCoverage);
        var securityMissingCount = PortfolioPositions.Count - securityResolvedCount;
        var summary = new FundWorkspaceSummary(
            FundProfileId: activeFund.FundProfileId,
            FundDisplayName: activeFund.DisplayName,
            BaseCurrency: activeFund.BaseCurrency,
            AsOf: ledger?.AsOf ?? DateTimeOffset.UtcNow,
            TotalAccounts: accounts.Count,
            BankAccountCount: accounts.Count(account => account.AccountType == Meridian.Contracts.FundStructure.AccountTypeDto.Bank),
            BrokerageAccountCount: accounts.Count(account => account.AccountType == Meridian.Contracts.FundStructure.AccountTypeDto.Brokerage),
            CustodyAccountCount: accounts.Count(account => account.AccountType == Meridian.Contracts.FundStructure.AccountTypeDto.Custody),
            TotalCash: cashSummary.TotalCash,
            GrossExposure: cashSummary.GrossExposure,
            NetExposure: cashSummary.NetExposure,
            TotalEquity: cashSummary.TotalEquity,
            FinancingCost: cashSummary.FinancingCost,
            PendingSettlement: cashSummary.PendingSettlement,
            OpenReconciliationBreaks: reconciliation.OpenBreakCount,
            ReconciliationRuns: reconciliation.RunCount,
            JournalEntryCount: ledger?.JournalEntryCount ?? 0,
            TrialBalanceLineCount: ledger?.TrialBalance.Count ?? 0,
            SecurityResolvedCount: securityResolvedCount,
            SecurityMissingCount: securityMissingCount,
            SecurityCoverageIssues: reconciliation.SecurityCoverageIssueCount);

        WorkspaceAsOfText = summary.AsOf.LocalDateTime.ToString("g");
        TotalAccountsText = summary.TotalAccounts.ToString("N0");
        BankAccountsText = summary.BankAccountCount.ToString("N0");
        BrokerageAccountsText = summary.BrokerageAccountCount.ToString("N0");
        CustodyAccountsText = summary.CustodyAccountCount.ToString("N0");
        OverviewStatusText = summary.TotalAccounts == 0 && summary.JournalEntryCount == 0
            ? "The governance shell is ready. Link accounts, import positions, or record a run to populate fund operations."
            : BuildOverviewStatus(summary);
    }

    private static string BuildOverviewStatus(FundWorkspaceSummary summary)
    {
        var status = $"{summary.FundDisplayName} is loaded with {summary.TotalAccounts} account(s), {summary.JournalEntryCount} journal entries, and {summary.ReconciliationRuns} reconciliation run(s).";

        if (summary.SecurityMissingCount > 0)
        {
            status += $" {summary.SecurityMissingCount} unresolved security mapping(s) still need Security Master coverage.";
        }

        if (summary.SecurityCoverageIssues > 0)
        {
            status += $" {summary.SecurityCoverageIssues} reconciliation security coverage issue(s) remain open.";
        }

        return status;
    }

    private void BuildAuditTrail(FundLedgerSummary? ledger, ReconciliationSummary reconciliation)
    {
        AuditTrail.Clear();

        if (ledger is not null)
        {
            foreach (var line in ledger.Journal.Take(10))
            {
                AuditTrail.Add(new FundAuditEntry(
                    Timestamp: line.Timestamp,
                    Category: "Journal",
                    Description: line.Description,
                    Reference: line.JournalEntryId.ToString("N")));
            }
        }

        foreach (var run in reconciliation.RecentRuns.Take(10))
        {
            AuditTrail.Add(new FundAuditEntry(
                Timestamp: run.RequestedAt,
                Category: "Reconciliation",
                Description: run.ScopeLabel == "Strategy Run"
                    ? $"{run.StrategyName ?? run.AccountDisplayName} · {run.Status} · {run.TotalBreaks} break(s) · {run.SecurityIssueCount} security issue(s)"
                    : $"{run.AccountDisplayName} · {run.Status} · {run.TotalBreaks} break(s)",
                Reference: run.RunId ?? run.ReconciliationRunId.ToString("N")));
        }

        var ordered = AuditTrail
            .OrderByDescending(entry => entry.Timestamp)
            .ToArray();

        AuditTrail.Clear();
        foreach (var entry in ordered)
        {
            AuditTrail.Add(entry);
        }
    }

    private async Task<IReadOnlyList<FundPortfolioPosition>> BuildFundPortfolioAsync(
        string fundProfileId,
        CancellationToken ct)
    {
        var runs = await _runWorkspaceService.GetRecordedRunsAsync(ct);
        var relevantRuns = runs
            .Where(run => string.Equals(run.FundProfileId, fundProfileId, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (relevantRuns.Length == 0)
        {
            return [];
        }

        var accounts = await _fundAccountReadService.GetAccountsAsync(fundProfileId, ct);
        var linkedAccountsByRun = accounts
            .Where(account => !string.IsNullOrWhiteSpace(account.PortfolioId) || !string.IsNullOrWhiteSpace(account.LedgerReference))
            .GroupBy(account => account.PortfolioId ?? account.LedgerReference ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        var rows = new Dictionary<string, FundPortfolioAccumulator>(StringComparer.OrdinalIgnoreCase);

        foreach (var run in relevantRuns)
        {
            var portfolio = await _runWorkspaceService.GetPortfolioAsync(run.RunId, ct);
            if (portfolio is null)
            {
                continue;
            }

            foreach (var position in portfolio.Positions)
            {
                if (!rows.TryGetValue(position.Symbol, out var accumulator))
                {
                    accumulator = new FundPortfolioAccumulator(position.Symbol);
                    rows[position.Symbol] = accumulator;
                }

                accumulator.NetQuantity += position.IsShort ? -Math.Abs(position.Quantity) : position.Quantity;
                accumulator.RealizedPnl += position.RealizedPnl;
                accumulator.UnrealizedPnl += position.UnrealizedPnl;
                accumulator.WeightedCostBasis += position.AverageCostBasis * Math.Abs(position.Quantity);
                accumulator.TotalWeight += Math.Abs(position.Quantity);
                accumulator.ContributingRuns.Add(run.RunId);
                if (position.Security is not null)
                {
                    accumulator.SecurityId ??= position.Security.SecurityId;
                    accumulator.SecurityDisplayName ??= position.Security.DisplayName;
                    accumulator.AssetClass ??= position.Security.AssetClass;
                    accumulator.SecuritySubType ??= position.Security.SubType;
                    accumulator.PrimaryIdentifier ??= position.Security.PrimaryIdentifier;
                    accumulator.SecurityResolvedContributions++;
                }
                else
                {
                    accumulator.SecurityMissingContributions++;
                }

                accumulator.LinkedAccounts += linkedAccountsByRun.TryGetValue(portfolio.PortfolioId, out var linked)
                    ? linked
                    : 0;
            }
        }

        return rows.Values
            .OrderByDescending(accumulator => Math.Abs(accumulator.UnrealizedPnl) + Math.Abs(accumulator.RealizedPnl))
            .Select(accumulator => new FundPortfolioPosition(
                Symbol: accumulator.Symbol,
                NetQuantity: accumulator.NetQuantity,
                WeightedAverageCostBasis: accumulator.TotalWeight == 0 ? 0m : accumulator.WeightedCostBasis / accumulator.TotalWeight,
                RealizedPnl: accumulator.RealizedPnl,
                UnrealizedPnl: accumulator.UnrealizedPnl,
                ContributingRuns: accumulator.ContributingRuns.Count,
                LinkedAccounts: accumulator.LinkedAccounts,
                SecurityId: accumulator.SecurityId,
                SecurityDisplayName: accumulator.SecurityDisplayName,
                AssetClass: accumulator.AssetClass,
                SecuritySubType: accumulator.SecuritySubType,
                PrimaryIdentifier: accumulator.PrimaryIdentifier,
                HasSecurityCoverage: accumulator.SecurityResolvedContributions > 0 && accumulator.SecurityMissingContributions == 0,
                CoverageLabel: accumulator.SecurityMissingContributions == 0 && accumulator.SecurityResolvedContributions > 0
                    ? "Mapped"
                    : accumulator.SecurityResolvedContributions > 0
                        ? "Partial"
                        : "Unresolved",
                ContributingRunIds: accumulator.ContributingRuns.Order(StringComparer.OrdinalIgnoreCase).ToArray(),
                SecurityResolvedContributions: accumulator.SecurityResolvedContributions,
                SecurityMissingContributions: accumulator.SecurityMissingContributions))
            .ToArray();
    }

    private void OpenLatestRunLedger()
    {
        _ = OpenLatestRunLedgerAsync();
    }

    private async Task OpenLatestRunLedgerAsync()
    {
        var activeFund = _fundContextService.CurrentFundProfile;
        if (activeFund is null)
        {
            _navigationService.NavigateTo("RunLedger");
            return;
        }

        var runs = await _runWorkspaceService.GetRecordedRunsAsync();
        var run = runs.FirstOrDefault(item =>
            string.Equals(item.FundProfileId, activeFund.FundProfileId, StringComparison.OrdinalIgnoreCase));
        if (run is null)
        {
            _navigationService.NavigateTo("RunLedger");
            return;
        }

        _navigationService.NavigateTo("RunLedger", run.RunId);
    }

    private void OpenSelectedAccountPortfolio()
    {
        if (SelectedAccount is null)
        {
            return;
        }

        _navigationService.NavigateTo("AccountPortfolio", new FundOperationsNavigationContext(
            Tab: FundOperationsTab.Accounts,
            FundProfileId: _fundContextService.CurrentFundProfile?.FundProfileId,
            AccountId: SelectedAccount.AccountId));
    }

    private void OpenSelectedPortfolioSecurity()
    {
        if (SelectedPortfolioPosition?.SecurityId is Guid securityId)
        {
            _navigationService.NavigateTo("SecurityMaster", securityId);
            return;
        }

        if (!string.IsNullOrWhiteSpace(SelectedPortfolioPosition?.Symbol))
        {
            _navigationService.NavigateTo("SecurityMaster", SelectedPortfolioPosition.Symbol);
        }
    }

    private async void OnActiveFundProfileChanged(object? sender, FundProfileChangedEventArgs e)
    {
        await LoadAsync();
    }

    private static int MapPageTagToTabIndex(string? pageTag) => pageTag switch
    {
        "FundAccounts" => (int)FundOperationsTab.Accounts,
        "FundBanking" => (int)FundOperationsTab.Banking,
        "FundPortfolio" => (int)FundOperationsTab.Portfolio,
        "FundCashFinancing" => (int)FundOperationsTab.CashFinancing,
        "FundLedger" => (int)FundOperationsTab.Journal,
        "FundTrialBalance" => (int)FundOperationsTab.TrialBalance,
        "FundReconciliation" => (int)FundOperationsTab.Reconciliation,
        "FundAuditTrail" => (int)FundOperationsTab.AuditTrail,
        _ => (int)FundOperationsTab.Overview
    };

    private sealed class FundPortfolioAccumulator
    {
        public FundPortfolioAccumulator(string symbol)
        {
            Symbol = symbol;
        }

        public string Symbol { get; }

        public long NetQuantity { get; set; }

        public decimal WeightedCostBasis { get; set; }

        public decimal TotalWeight { get; set; }

        public decimal RealizedPnl { get; set; }

        public decimal UnrealizedPnl { get; set; }

        public int LinkedAccounts { get; set; }

        public Guid? SecurityId { get; set; }

        public string? SecurityDisplayName { get; set; }

        public string? AssetClass { get; set; }

        public string? SecuritySubType { get; set; }

        public string? PrimaryIdentifier { get; set; }

        public int SecurityResolvedContributions { get; set; }

        public int SecurityMissingContributions { get; set; }

        public HashSet<string> ContributingRuns { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
