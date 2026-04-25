using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Meridian.Ui.Shared.Services;
using Meridian.Contracts.Workstation;
using Meridian.Wpf.Models;
using Meridian.Wpf.Services;

namespace Meridian.Wpf.ViewModels;

public sealed partial class FundLedgerViewModel : BindableBase, IDisposable
{
    private readonly FundLedgerReadService _fundLedgerReadService;
    private readonly FundContextService _fundContextService;
    private readonly NavigationService _navigationService;
    private readonly FundAccountReadService _fundAccountReadService;
    private readonly CashFinancingReadService _cashFinancingReadService;
    private readonly IFundReconciliationWorkbenchService _fundReconciliationWorkbenchService;
    private readonly FundOperationsWorkspaceReadService _fundOperationsWorkspaceReadService;
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
    private string _cashProjectionStatusText = "Cash-flow projection will appear once fund-scoped runs record funding events.";
    private string _cashFlowEntryCountText = "0";
    private string _projectedInflowsText = "-";
    private string _projectedOutflowsText = "-";
    private string _netProjectedCashFlowText = "-";
    private string _projectionBucketSummaryText = "-";
    private string _openBreaksText = "0";
    private string _reconciliationRunsText = "0";
    private string _reportPackStatusText = "Governance report-pack preview is waiting for a fund context.";
    private string _reportPackKindText = GovernanceReportKindDto.TrialBalance.ToString();
    private string _reportPackAsOfText = "-";
    private string _reportPackNetAssetsText = "-";
    private string _reportPackTrialBalanceLinesText = "0";
    private string _reportPackAssetSectionsText = "0";
    private string _reportPackGeneratedAtText = "-";
    private string _currentWorkbenchModeText = "Overview Mode";
    private string _currentWorkbenchTitleText = "Overview Workbench";
    private string _currentWorkbenchSubtitleText = "Fund-wide operating summary, liquidity posture, and exception pressure.";
    private string _routeBannerTitleText = string.Empty;
    private string _routeBannerDetailText = string.Empty;
    private bool _hasRouteBanner;
    private string _reconciliationOwnershipText = "Assign an operator before reconciliation sign-off.";
    private string _reconciliationSnapshotWarningText = "Queue refresh timing is not confirmed. Refresh before resolving breaks or signing off.";
    private string _reportPackOwnershipText = "Governance operator sign-off is pending.";
    private string _reportPackSnapshotWarningText = "Report-pack freshness is unknown. Refresh the preview before distributing reporting artifacts.";
    private bool _isReportPackLoading;
    private GovernanceReportKindDto _selectedReportKind = GovernanceReportKindDto.TrialBalance;
    private int _selectedTabIndex;
    private string? _routedPageTag;
    private FundAccountSummary? _selectedAccount;
    private FundPortfolioPosition? _selectedPortfolioPosition;
    private CashFlowEntryDto? _selectedCashFlowEntry;
    private FundLedgerDimensionView? _selectedLedgerDimension;
    private string _selectedLedgerDimensionDisplayText = "Consolidated Fund View";
    private string _selectedLedgerDimensionCoverageText = "Full fund ledger coverage is shown until account-linked ledger rows are available.";
    private string _selectedLedgerDimensionStatusText = "Consolidated ledger posture is active.";
    private string _selectedLedgerLinkedAccountsText = "0";
    private string _selectedLedgerTrialBalanceLinesText = "0";
    private string _selectedLedgerJournalEntriesText = "0";
    private string _selectedLedgerAssetBalanceText = "-";
    private string _selectedLedgerEquityBalanceText = "-";
    private FundReportPackPreviewDto? _reportPackPreview;

    public FundLedgerViewModel(
        FundLedgerReadService fundLedgerReadService,
        FundContextService fundContextService,
        NavigationService navigationService,
        FundAccountReadService fundAccountReadService,
        CashFinancingReadService cashFinancingReadService,
        IFundReconciliationWorkbenchService fundReconciliationWorkbenchService,
        FundOperationsWorkspaceReadService fundOperationsWorkspaceReadService,
        StrategyRunWorkspaceService runWorkspaceService)
    {
        _fundLedgerReadService = fundLedgerReadService ?? throw new ArgumentNullException(nameof(fundLedgerReadService));
        _fundContextService = fundContextService ?? throw new ArgumentNullException(nameof(fundContextService));
        _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
        _fundAccountReadService = fundAccountReadService ?? throw new ArgumentNullException(nameof(fundAccountReadService));
        _cashFinancingReadService = cashFinancingReadService ?? throw new ArgumentNullException(nameof(cashFinancingReadService));
        _fundReconciliationWorkbenchService = fundReconciliationWorkbenchService ?? throw new ArgumentNullException(nameof(fundReconciliationWorkbenchService));
        _fundOperationsWorkspaceReadService = fundOperationsWorkspaceReadService ?? throw new ArgumentNullException(nameof(fundOperationsWorkspaceReadService));
        _runWorkspaceService = runWorkspaceService ?? throw new ArgumentNullException(nameof(runWorkspaceService));

        TrialBalance = [];
        Journal = [];
        Accounts = [];
        BankSnapshots = [];
        PortfolioPositions = [];
        CashFlowEntries = [];
        CashFlowBuckets = [];
        VisibleTrialBalance = [];
        VisibleJournal = [];
        LedgerDimensions = [];
        ReconciliationRuns = [];
        CashFinancingHighlights = [];
        AuditTrail = [];
        ReportPackAssetSections = [];

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
        OpenReportPackCommand = new RelayCommand(() => _navigationService.NavigateTo("FundReportPack"));
        RefreshReportPackCommand = new AsyncRelayCommand(RefreshReportPackPreviewAsync);
        OpenSelectedAccountPortfolioCommand = new RelayCommand(OpenSelectedAccountPortfolio, () => SelectedAccount is not null);
        OpenSelectedPortfolioSecurityCommand = new RelayCommand(OpenSelectedPortfolioSecurity, () => SelectedPortfolioPosition is not null);
        OpenSelectedCashFlowSecurityCommand = new RelayCommand(OpenSelectedCashFlowSecurity, () => !string.IsNullOrWhiteSpace(SelectedCashFlowEntry?.Symbol));

        _fundContextService.ActiveFundProfileChanged += OnActiveFundProfileChanged;
        InitializeReconciliationWorkbench();
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

    public ObservableCollection<CashFlowEntryDto> CashFlowEntries { get; }

    public ObservableCollection<CashLadderBucketDto> CashFlowBuckets { get; }

    public ObservableCollection<FundTrialBalanceLine> VisibleTrialBalance { get; }

    public ObservableCollection<FundJournalLine> VisibleJournal { get; }

    public ObservableCollection<FundLedgerDimensionView> LedgerDimensions { get; }

    public ObservableCollection<FundReconciliationItem> ReconciliationRuns { get; }

    public ObservableCollection<string> CashFinancingHighlights { get; }

    public ObservableCollection<FundAuditEntry> AuditTrail { get; }

    public ObservableCollection<FundReportAssetClassSectionDto> ReportPackAssetSections { get; }

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

    public IRelayCommand OpenReportPackCommand { get; }

    public IAsyncRelayCommand RefreshReportPackCommand { get; }

    public IRelayCommand OpenSelectedAccountPortfolioCommand { get; }

    public IRelayCommand OpenSelectedPortfolioSecurityCommand { get; }

    public IRelayCommand OpenSelectedCashFlowSecurityCommand { get; }

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

    public string CashProjectionStatusText
    {
        get => _cashProjectionStatusText;
        private set => SetProperty(ref _cashProjectionStatusText, value);
    }

    public string CashFlowEntryCountText
    {
        get => _cashFlowEntryCountText;
        private set => SetProperty(ref _cashFlowEntryCountText, value);
    }

    public string ProjectedInflowsText
    {
        get => _projectedInflowsText;
        private set => SetProperty(ref _projectedInflowsText, value);
    }

    public string ProjectedOutflowsText
    {
        get => _projectedOutflowsText;
        private set => SetProperty(ref _projectedOutflowsText, value);
    }

    public string NetProjectedCashFlowText
    {
        get => _netProjectedCashFlowText;
        private set => SetProperty(ref _netProjectedCashFlowText, value);
    }

    public string ProjectionBucketSummaryText
    {
        get => _projectionBucketSummaryText;
        private set => SetProperty(ref _projectionBucketSummaryText, value);
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

    public string ReportPackStatusText
    {
        get => _reportPackStatusText;
        private set => SetProperty(ref _reportPackStatusText, value);
    }

    public string ReportPackKindText
    {
        get => _reportPackKindText;
        private set => SetProperty(ref _reportPackKindText, value);
    }

    public string ReportPackAsOfText
    {
        get => _reportPackAsOfText;
        private set => SetProperty(ref _reportPackAsOfText, value);
    }

    public string ReportPackNetAssetsText
    {
        get => _reportPackNetAssetsText;
        private set => SetProperty(ref _reportPackNetAssetsText, value);
    }

    public string ReportPackTrialBalanceLinesText
    {
        get => _reportPackTrialBalanceLinesText;
        private set => SetProperty(ref _reportPackTrialBalanceLinesText, value);
    }

    public string ReportPackAssetSectionsText
    {
        get => _reportPackAssetSectionsText;
        private set => SetProperty(ref _reportPackAssetSectionsText, value);
    }

    public string ReportPackGeneratedAtText
    {
        get => _reportPackGeneratedAtText;
        private set
        {
            if (SetProperty(ref _reportPackGeneratedAtText, value))
            {
                UpdateReportPackWorkbenchPresentation();
            }
        }
    }

    public string CurrentWorkbenchModeText
    {
        get => _currentWorkbenchModeText;
        private set => SetProperty(ref _currentWorkbenchModeText, value);
    }

    public string CurrentWorkbenchTitleText
    {
        get => _currentWorkbenchTitleText;
        private set => SetProperty(ref _currentWorkbenchTitleText, value);
    }

    public string CurrentWorkbenchSubtitleText
    {
        get => _currentWorkbenchSubtitleText;
        private set => SetProperty(ref _currentWorkbenchSubtitleText, value);
    }

    public string RouteBannerTitleText
    {
        get => _routeBannerTitleText;
        private set => SetProperty(ref _routeBannerTitleText, value);
    }

    public string RouteBannerDetailText
    {
        get => _routeBannerDetailText;
        private set => SetProperty(ref _routeBannerDetailText, value);
    }

    public bool HasRouteBanner
    {
        get => _hasRouteBanner;
        private set => SetProperty(ref _hasRouteBanner, value);
    }

    public string ReconciliationOwnershipText
    {
        get => _reconciliationOwnershipText;
        private set => SetProperty(ref _reconciliationOwnershipText, value);
    }

    public string ReconciliationSnapshotWarningText
    {
        get => _reconciliationSnapshotWarningText;
        private set => SetProperty(ref _reconciliationSnapshotWarningText, value);
    }

    public string ReportPackOwnershipText
    {
        get => _reportPackOwnershipText;
        private set => SetProperty(ref _reportPackOwnershipText, value);
    }

    public string ReportPackSnapshotWarningText
    {
        get => _reportPackSnapshotWarningText;
        private set => SetProperty(ref _reportPackSnapshotWarningText, value);
    }

    public bool IsReportPackLoading
    {
        get => _isReportPackLoading;
        private set => SetProperty(ref _isReportPackLoading, value);
    }

    public int SelectedReportKindIndex
    {
        get => (int)_selectedReportKind;
        set
        {
            var normalized = Enum.IsDefined(typeof(GovernanceReportKindDto), value)
                ? (GovernanceReportKindDto)value
                : GovernanceReportKindDto.TrialBalance;

            if (SetProperty(ref _selectedReportKind, normalized))
            {
                _ = RefreshReportPackPreviewAsync();
            }
        }
    }

    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set
        {
            if (SetProperty(ref _selectedTabIndex, value))
            {
                UpdateWorkbenchIdentity();
                UpdateRouteBannerPresentation();
            }
        }
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

    public CashFlowEntryDto? SelectedCashFlowEntry
    {
        get => _selectedCashFlowEntry;
        set
        {
            if (SetProperty(ref _selectedCashFlowEntry, value))
            {
                OpenSelectedCashFlowSecurityCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public FundLedgerDimensionView? SelectedLedgerDimension
    {
        get => _selectedLedgerDimension;
        set
        {
            if (SetProperty(ref _selectedLedgerDimension, value))
            {
                ApplyLedgerDimensionFilter();
            }
        }
    }

    public string SelectedLedgerDimensionDisplayText
    {
        get => _selectedLedgerDimensionDisplayText;
        private set => SetProperty(ref _selectedLedgerDimensionDisplayText, value);
    }

    public string SelectedLedgerDimensionCoverageText
    {
        get => _selectedLedgerDimensionCoverageText;
        private set => SetProperty(ref _selectedLedgerDimensionCoverageText, value);
    }

    public string SelectedLedgerDimensionStatusText
    {
        get => _selectedLedgerDimensionStatusText;
        private set => SetProperty(ref _selectedLedgerDimensionStatusText, value);
    }

    public string SelectedLedgerLinkedAccountsText
    {
        get => _selectedLedgerLinkedAccountsText;
        private set => SetProperty(ref _selectedLedgerLinkedAccountsText, value);
    }

    public string SelectedLedgerTrialBalanceLinesText
    {
        get => _selectedLedgerTrialBalanceLinesText;
        private set => SetProperty(ref _selectedLedgerTrialBalanceLinesText, value);
    }

    public string SelectedLedgerJournalEntriesText
    {
        get => _selectedLedgerJournalEntriesText;
        private set => SetProperty(ref _selectedLedgerJournalEntriesText, value);
    }

    public string SelectedLedgerAssetBalanceText
    {
        get => _selectedLedgerAssetBalanceText;
        private set => SetProperty(ref _selectedLedgerAssetBalanceText, value);
    }

    public string SelectedLedgerEquityBalanceText
    {
        get => _selectedLedgerEquityBalanceText;
        private set => SetProperty(ref _selectedLedgerEquityBalanceText, value);
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
        var reconciliationTask = _fundReconciliationWorkbenchService.GetSnapshotAsync(activeFund.FundProfileId, ct);
        var portfolioTask = BuildFundPortfolioAsync(activeFund.FundProfileId, ct);

        await Task.WhenAll(ledgerTask, accountsTask, bankSnapshotsTask, cashTask, reconciliationTask, portfolioTask);

        var ledger = await ledgerTask;
        var accounts = await accountsTask;
        var bankSnapshots = await bankSnapshotsTask;
        var cashSummary = await cashTask;
        var reconciliationSnapshot = await reconciliationTask;
        var portfolioPositions = await portfolioTask;

        Title = $"Fund Operations · {activeFund.DisplayName}";
        StatusText = accounts.Count == 0 && ledger?.JournalEntryCount is not > 0
            ? "Fund operations are ready, but no linked account or ledger activity has been recorded yet."
            : "Governance fund operations are loaded for the active fund profile.";

        ApplyLedger(ledger);
        ApplyAccounts(accounts, context);
        BuildLedgerDimensions(activeFund, ledger);
        ApplyBankSnapshots(bankSnapshots);
        ApplyCashSummary(cashSummary);
        ApplyPortfolio(portfolioPositions);
        await ApplyReconciliationWorkbenchAsync(activeFund, reconciliationSnapshot, ct);
        BuildWorkspaceSummary(activeFund, ledger, accounts, cashSummary, reconciliationSnapshot.Summary);
        BuildAuditTrail(ledger, reconciliationSnapshot.Summary);
        await RefreshReportPackPreviewAsync(ct);
        UpdateReconciliationWorkbenchPresentation();
        UpdateReportPackWorkbenchPresentation();
    }

    public void Dispose()
    {
        _fundContextService.ActiveFundProfileChanged -= OnActiveFundProfileChanged;
        DisposeReconciliationWorkbench();
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
        _routedPageTag = ResolveRoutedPageTag(context);
        SelectedTabIndex = context is not null
            ? (int)context.Tab
            : MapPageTagToTabIndex(_navigationService.GetCurrentPageTag());
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
        CashProjectionStatusText = "Cash-flow projection will appear once fund-scoped runs record funding events.";
        CashFlowEntryCountText = "0";
        ProjectedInflowsText = "-";
        ProjectedOutflowsText = "-";
        NetProjectedCashFlowText = "-";
        ProjectionBucketSummaryText = "-";
        OpenBreaksText = "0";
        ReconciliationRunsText = "0";
        TrialBalance.Clear();
        Journal.Clear();
        VisibleTrialBalance.Clear();
        VisibleJournal.Clear();
        LedgerDimensions.Clear();
        Accounts.Clear();
        SelectedAccount = null;
        BankSnapshots.Clear();
        PortfolioPositions.Clear();
        SelectedPortfolioPosition = null;
        CashFlowEntries.Clear();
        CashFlowBuckets.Clear();
        SelectedCashFlowEntry = null;
        SelectedLedgerDimension = null;
        SelectedLedgerDimensionDisplayText = "Consolidated Fund View";
        SelectedLedgerDimensionCoverageText = "Full fund ledger coverage is shown until account-linked ledger rows are available.";
        SelectedLedgerDimensionStatusText = "Consolidated ledger posture is active.";
        SelectedLedgerLinkedAccountsText = "0";
        SelectedLedgerTrialBalanceLinesText = "0";
        SelectedLedgerJournalEntriesText = "0";
        SelectedLedgerAssetBalanceText = "-";
        SelectedLedgerEquityBalanceText = "-";
        ReconciliationRuns.Clear();
        CashFinancingHighlights.Clear();
        AuditTrail.Clear();
        ReportPackAssetSections.Clear();
        _reportPackPreview = null;
        ReportPackStatusText = "Governance report-pack preview is waiting for a fund context.";
        ReportPackKindText = GovernanceReportKindDto.TrialBalance.ToString();
        ReportPackAsOfText = "-";
        ReportPackNetAssetsText = "-";
        ReportPackTrialBalanceLinesText = "0";
        ReportPackAssetSectionsText = "0";
        ReportPackGeneratedAtText = "-";
        ResetReconciliationWorkbenchState();
        UpdateWorkbenchIdentity();
        UpdateRouteBannerPresentation();
        UpdateReconciliationWorkbenchPresentation();
        UpdateReportPackWorkbenchPresentation();
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
        CashFlowEntryCountText = summary.CashFlowEntryCount.ToString("N0");
        ProjectedInflowsText = summary.ProjectedInflows.ToString("C2");
        ProjectedOutflowsText = summary.ProjectedOutflows.ToString("C2");
        NetProjectedCashFlowText = summary.NetProjectedCashFlow.ToString("C2");
        ProjectionBucketSummaryText = summary.ProjectionBucketDays > 0
            ? $"{summary.CashFlowBuckets?.Count ?? 0} bucket(s) x {summary.ProjectionBucketDays}d"
            : "-";
        CashProjectionStatusText = summary.CashFlowEntryCount == 0
            ? "No run-derived cash-flow projections are available yet for the active fund."
            : $"{summary.CashFlowEntryCount} cash-flow event(s) are available across the shared fund continuity model.";

        CashFinancingHighlights.Clear();
        foreach (var highlight in summary.Highlights)
        {
            CashFinancingHighlights.Add(highlight);
        }

        CashFlowEntries.Clear();
        foreach (var entry in summary.CashFlowEntries ?? [])
        {
            CashFlowEntries.Add(entry);
        }

        CashFlowBuckets.Clear();
        foreach (var bucket in summary.CashFlowBuckets ?? [])
        {
            CashFlowBuckets.Add(bucket);
        }

        SelectedCashFlowEntry = CashFlowEntries.FirstOrDefault(entry => !string.IsNullOrWhiteSpace(entry.Symbol))
            ?? CashFlowEntries.FirstOrDefault();
    }

    private void BuildLedgerDimensions(
        FundProfileDetail activeFund,
        FundLedgerSummary? ledger)
    {
        var previousSelectionKey = SelectedLedgerDimension?.Key;
        var slices = ledger?.LedgerSlices ?? [];
        var consolidatedSlice = slices.FirstOrDefault(slice => slice.ScopeKind == FundLedgerScope.Consolidated);
        var entitySlices = slices.Where(slice => slice.ScopeKind == FundLedgerScope.Entity).ToArray();
        var sleeveSlices = slices.Where(slice => slice.ScopeKind == FundLedgerScope.Sleeve).ToArray();
        var vehicleSlices = slices.Where(slice => slice.ScopeKind == FundLedgerScope.Vehicle).ToArray();
        var consolidatedTotals = consolidatedSlice?.Totals ?? BuildSummaryTotals(ledger);
        var consolidatedLinkedAccountCount = CountLinkedAccounts(consolidatedSlice is null ? [] : [consolidatedSlice]);
        var consolidatedTrialBalanceLineCount = consolidatedSlice?.TrialBalance.Count ?? ledger?.TrialBalance.Count ?? 0;
        var consolidatedJournalEntryCount = consolidatedSlice?.Totals.JournalEntryCount ?? ledger?.Journal.Count ?? 0;

        var dimensions = new List<FundLedgerDimensionView>
        {
            new(
                Key: "consolidated",
                DisplayName: "Consolidated Fund View",
                CoverageText: $"1 ledger slice · {consolidatedLinkedAccountCount} linked account(s) · {consolidatedTrialBalanceLineCount} trial-balance line(s) · {consolidatedJournalEntryCount} journal entry(s) across the full fund view.",
                StatusText: "Shows the complete fund ledger, including generic postings that are not tied to a specific financial account.",
                ExpectedScopeCount: 1,
                MaterializedScopeCount: consolidatedSlice is null ? 0 : 1,
                LinkedAccountCount: consolidatedLinkedAccountCount,
                TrialBalanceLineCount: consolidatedTrialBalanceLineCount,
                JournalEntryCount: consolidatedJournalEntryCount,
                Totals: consolidatedTotals,
                IsConsolidated: true,
                HasScopedLedgerData: true,
                LedgerSlices: consolidatedSlice is null ? [] : [consolidatedSlice])
        };

        dimensions.Add(CreateLedgerDimensionView(
            key: "entity-linked",
            displayName: "Entity-Linked View",
            expectedScopeCount: activeFund.EntityIds?.Count ?? 0,
            slices: entitySlices));
        dimensions.Add(CreateLedgerDimensionView(
            key: "sleeve-linked",
            displayName: "Sleeve-Linked View",
            expectedScopeCount: activeFund.SleeveIds?.Count ?? 0,
            slices: sleeveSlices));
        dimensions.Add(CreateLedgerDimensionView(
            key: "vehicle-linked",
            displayName: "Vehicle-Linked View",
            expectedScopeCount: activeFund.VehicleIds?.Count ?? 0,
            slices: vehicleSlices));

        LedgerDimensions.Clear();
        foreach (var dimension in dimensions.Where(static dimension =>
                     dimension.IsConsolidated ||
                     dimension.ExpectedScopeCount > 0 ||
                     dimension.MaterializedScopeCount > 0 ||
                     dimension.HasScopedLedgerData))
        {
            LedgerDimensions.Add(dimension);
        }

        SelectedLedgerDimension = LedgerDimensions.FirstOrDefault(dimension =>
                                     string.Equals(dimension.Key, previousSelectionKey, StringComparison.OrdinalIgnoreCase))
                                 ?? LedgerDimensions.FirstOrDefault();
    }

    private static FundLedgerDimensionView CreateLedgerDimensionView(
        string key,
        string displayName,
        int expectedScopeCount,
        IReadOnlyList<FundLedgerSliceDto> slices)
    {
        var linkedAccountCount = CountLinkedAccounts(slices);
        var materializedScopeCount = slices.Count;
        var trialBalanceLineCount = slices.Sum(static slice => slice.TrialBalance.Count);
        var totals = BuildDimensionTotals(slices);
        var journalEntryCount = totals.JournalEntryCount;
        var hasScopedLedgerData = trialBalanceLineCount > 0 || journalEntryCount > 0;
        var coverageText = $"{expectedScopeCount} profile scope(s) · {materializedScopeCount} ledger slice(s) · {linkedAccountCount} linked account(s) · {trialBalanceLineCount} trial-balance line(s) · {journalEntryCount} journal entry(s)";
        var statusText = materializedScopeCount == 0
            ? "No ledger slices are currently materialized for this structure."
            : hasScopedLedgerData
                ? materializedScopeCount < expectedScopeCount && expectedScopeCount > 0
                    ? "Ledger slices are partially materialized and ready for scoped review."
                    : "Ledger slices are ready for scoped review."
                : linkedAccountCount > 0
                    ? "Ledger slices exist for this structure, but current postings have not hit them yet."
                    : "Ledger slices exist, but current postings remain generic and are not tied to linked accounts.";

        return new FundLedgerDimensionView(
            Key: key,
            DisplayName: displayName,
            CoverageText: coverageText,
            StatusText: statusText,
            ExpectedScopeCount: expectedScopeCount,
            MaterializedScopeCount: materializedScopeCount,
            LinkedAccountCount: linkedAccountCount,
            TrialBalanceLineCount: trialBalanceLineCount,
            JournalEntryCount: journalEntryCount,
            Totals: totals,
            IsConsolidated: false,
            HasScopedLedgerData: hasScopedLedgerData,
            LedgerSlices: slices);
    }

    private void ApplyLedgerDimensionFilter()
    {
        if (SelectedLedgerDimension is null)
        {
            VisibleTrialBalance.Clear();
            VisibleJournal.Clear();
            SelectedLedgerDimensionDisplayText = "Consolidated Fund View";
            SelectedLedgerDimensionCoverageText = "Full fund ledger coverage is shown until account-linked ledger rows are available.";
            SelectedLedgerDimensionStatusText = "Consolidated ledger posture is active.";
            SelectedLedgerLinkedAccountsText = "0";
            SelectedLedgerTrialBalanceLinesText = "0";
            SelectedLedgerJournalEntriesText = "0";
            SelectedLedgerAssetBalanceText = "-";
            SelectedLedgerEquityBalanceText = "-";
            return;
        }

        var visibleTrialBalance = SelectedLedgerDimension.LedgerSlices
            .SelectMany(static slice => slice.TrialBalance)
            .ToArray();
        if (visibleTrialBalance.Length == 0 && SelectedLedgerDimension.IsConsolidated)
        {
            visibleTrialBalance = TrialBalance.ToArray();
        }

        var visibleJournal = SelectedLedgerDimension.LedgerSlices
            .SelectMany(static slice => slice.Journal)
            .OrderByDescending(static line => line.Timestamp)
            .ThenByDescending(static line => line.JournalEntryId)
            .DistinctBy(static line => line.JournalEntryId)
            .ToArray();
        if (visibleJournal.Length == 0 && SelectedLedgerDimension.IsConsolidated)
        {
            visibleJournal = Journal.ToArray();
        }

        VisibleTrialBalance.Clear();
        foreach (var line in visibleTrialBalance)
        {
            VisibleTrialBalance.Add(line);
        }

        VisibleJournal.Clear();
        foreach (var line in visibleJournal)
        {
            VisibleJournal.Add(line);
        }

        SelectedLedgerDimensionDisplayText = SelectedLedgerDimension.DisplayName;
        SelectedLedgerDimensionCoverageText = SelectedLedgerDimension.CoverageText;
        SelectedLedgerDimensionStatusText = SelectedLedgerDimension.StatusText;
        SelectedLedgerLinkedAccountsText = SelectedLedgerDimension.LinkedAccountCount.ToString("N0");
        SelectedLedgerTrialBalanceLinesText = visibleTrialBalance.Length.ToString("N0");
        SelectedLedgerJournalEntriesText = visibleJournal.Length.ToString("N0");
        SelectedLedgerAssetBalanceText = SelectedLedgerDimension.Totals.AssetBalance.ToString("C2");
        SelectedLedgerEquityBalanceText = SelectedLedgerDimension.Totals.EquityBalance.ToString("C2");
    }

    private static FundLedgerTotalsDto BuildSummaryTotals(FundLedgerSummary? ledger)
        => ledger is null
            ? new FundLedgerTotalsDto(0, 0, 0m, 0m, 0m, 0m, 0m)
            : ledger.ConsolidatedTotals
              ?? new FundLedgerTotalsDto(
                  JournalEntryCount: ledger.JournalEntryCount,
                  LedgerEntryCount: ledger.LedgerEntryCount,
                  AssetBalance: ledger.AssetBalance,
                  LiabilityBalance: ledger.LiabilityBalance,
                  EquityBalance: ledger.EquityBalance,
                  RevenueBalance: ledger.RevenueBalance,
                  ExpenseBalance: ledger.ExpenseBalance);

    private static FundLedgerTotalsDto BuildDimensionTotals(IReadOnlyList<FundLedgerSliceDto> slices)
        => new(
            JournalEntryCount: slices.Sum(static slice => slice.Totals.JournalEntryCount),
            LedgerEntryCount: slices.Sum(static slice => slice.Totals.LedgerEntryCount),
            AssetBalance: slices.Sum(static slice => slice.Totals.AssetBalance),
            LiabilityBalance: slices.Sum(static slice => slice.Totals.LiabilityBalance),
            EquityBalance: slices.Sum(static slice => slice.Totals.EquityBalance),
            RevenueBalance: slices.Sum(static slice => slice.Totals.RevenueBalance),
            ExpenseBalance: slices.Sum(static slice => slice.Totals.ExpenseBalance));

    private static int CountLinkedAccounts(IReadOnlyList<FundLedgerSliceDto> slices)
        => slices
            .SelectMany(static slice => slice.TrialBalance.Select(static line => line.FinancialAccountId)
                .Concat(slice.Journal.SelectMany(static line => line.FinancialAccountIds ?? [])))
            .Where(static accountId => !string.IsNullOrWhiteSpace(accountId))
            .Select(static accountId => accountId!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

    private static decimal SumBalance(IEnumerable<FundTrialBalanceLine> lines, string accountType) =>
        lines
            .Where(line => string.Equals(line.AccountType, accountType, StringComparison.Ordinal))
            .Sum(static line => line.Balance);

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

    private void OpenSelectedCashFlowSecurity()
    {
        if (!string.IsNullOrWhiteSpace(SelectedCashFlowEntry?.Symbol))
        {
            _navigationService.NavigateTo("SecurityMaster", SelectedCashFlowEntry.Symbol);
        }
    }

    public async Task RefreshReportPackPreviewAsync(CancellationToken ct = default)
    {
        var activeFund = _fundContextService.CurrentFundProfile;
        if (activeFund is null)
        {
            ReportPackStatusText = "Select a fund profile to build a governance report-pack preview.";
            ReportPackAssetSections.Clear();
            return;
        }

        IsReportPackLoading = true;
        try
        {
            var preview = await _fundOperationsWorkspaceReadService
                .PreviewReportPackAsync(
                    new FundReportPackPreviewRequestDto(
                        FundProfileId: activeFund.FundProfileId,
                        ReportKind: _selectedReportKind,
                        AsOf: DateTimeOffset.UtcNow,
                        Currency: activeFund.BaseCurrency),
                    ct)
                .ConfigureAwait(false);

            _reportPackPreview = preview;
            ReportPackKindText = preview.ReportKind.ToString();
            ReportPackAsOfText = preview.AsOf.LocalDateTime.ToString("g");
            ReportPackNetAssetsText = preview.TotalNetAssets.ToString("C2");
            ReportPackTrialBalanceLinesText = preview.TrialBalanceLineCount.ToString("N0");
            ReportPackAssetSectionsText = preview.AssetClassSectionCount.ToString("N0");
            ReportPackGeneratedAtText = preview.GeneratedAt.LocalDateTime.ToString("g");
            ReportPackStatusText = $"{preview.DisplayName} {preview.ReportKind} preview is ready for operator handoff.";

            ReportPackAssetSections.Clear();
            foreach (var section in preview.AssetClassSections.OrderByDescending(static section => section.Total))
            {
                ReportPackAssetSections.Add(section);
            }
        }
        catch (OperationCanceledException)
        {
            ReportPackStatusText = "Report-pack preview refresh cancelled.";
        }
        catch (Exception ex)
        {
            ReportPackStatusText = $"Unable to build report-pack preview: {ex.Message}";
            ReportPackAssetSections.Clear();
        }
        finally
        {
            IsReportPackLoading = false;
            UpdateReportPackWorkbenchPresentation();
        }
    }

    private async void OnActiveFundProfileChanged(object? sender, FundProfileChangedEventArgs e)
    {
        await LoadAsync();
    }

    private void UpdateWorkbenchIdentity()
    {
        var tab = SelectedTabIndex is >= byte.MinValue and <= byte.MaxValue &&
                  Enum.IsDefined(typeof(FundOperationsTab), (byte)SelectedTabIndex)
            ? (FundOperationsTab)SelectedTabIndex
            : FundOperationsTab.Overview;

        var identity = DescribeWorkbench(tab);
        CurrentWorkbenchModeText = identity.Mode;
        CurrentWorkbenchTitleText = identity.Title;
        CurrentWorkbenchSubtitleText = identity.Subtitle;
    }

    private void UpdateRouteBannerPresentation()
    {
        if (string.IsNullOrWhiteSpace(_routedPageTag) ||
            MapPageTagToTabIndex(_routedPageTag) != SelectedTabIndex)
        {
            HasRouteBanner = false;
            RouteBannerTitleText = string.Empty;
            RouteBannerDetailText = string.Empty;
            return;
        }

        var route = DescribeRouteBanner(_routedPageTag);
        HasRouteBanner = true;
        RouteBannerTitleText = route.Title;
        RouteBannerDetailText = route.Detail;
    }

    private void UpdateReconciliationWorkbenchPresentation()
    {
        var operatorLabel = string.IsNullOrWhiteSpace(ReconciliationOperatorText) ||
                            string.Equals(ReconciliationOperatorText, DefaultReconciliationOperator, StringComparison.OrdinalIgnoreCase)
            ? "Owner not confirmed"
            : $"Owner {ReconciliationOperatorText}";

        ReconciliationOwnershipText = $"{operatorLabel}. Reconciliation sign-off should happen only after queue review, stale-snapshot confirmation, and security coverage checks are complete.";
        ReconciliationSnapshotWarningText = BuildSnapshotWarningText(
            ReconciliationLastRefreshText,
            "Queue refresh timing is not confirmed. Refresh before resolving breaks or signing off.");
    }

    private void UpdateReportPackWorkbenchPresentation()
    {
        var reportOwner = string.IsNullOrWhiteSpace(ReconciliationOperatorText) ||
                          string.Equals(ReconciliationOperatorText, DefaultReconciliationOperator, StringComparison.OrdinalIgnoreCase)
            ? "Governance operator"
            : ReconciliationOperatorText;

        ReportPackOwnershipText = $"{reportOwner} owns final report-pack sign-off once accounting, reconciliation, and audit evidence align.";
        ReportPackSnapshotWarningText = BuildSnapshotWarningText(
            ReportPackGeneratedAtText,
            "Report-pack freshness is unknown. Refresh the preview before distributing reporting artifacts.");
    }

    private static (string Mode, string Title, string Subtitle) DescribeWorkbench(FundOperationsTab tab) => tab switch
    {
        FundOperationsTab.Overview => ("Overview Mode", "Overview Workbench", "Fund-wide operating summary, liquidity posture, and exception pressure."),
        FundOperationsTab.Reconciliation => ("Reconciliation Mode", "Reconciliation Workbench", "Exception queue, operator ownership, stale-snapshot checks, and break resolution."),
        FundOperationsTab.ReportPack => ("Reporting Mode", "Report Pack Workbench", "Reporting preview, handoff readiness, and sign-off posture for governance artifacts."),
        FundOperationsTab.AuditTrail => ("Accounting Mode", "Audit Trail Workbench", "Recent journal and reconciliation evidence for operator review and traceability."),
        FundOperationsTab.Portfolio => ("Accounting Mode", "Portfolio Accounting Workbench", "Fund-scoped positions, security coverage, and exposure review inside governance."),
        FundOperationsTab.Banking => ("Accounting Mode", "Banking Workbench", "Bank-facing balances, statements, and settlement posture for the active fund."),
        FundOperationsTab.CashFinancing => ("Accounting Mode", "Cash & Financing Workbench", "Funding ladder, financing cost, and pending-settlement review."),
        FundOperationsTab.Journal => ("Accounting Mode", "Journal Workbench", "Booked journal history and ledger-linked review for accounting operators."),
        FundOperationsTab.TrialBalance => ("Accounting Mode", "Trial Balance Workbench", "Scoped ledger balances, material lines, and accounting posture."),
        FundOperationsTab.Accounts => ("Accounting Mode", "Accounts Workbench", "Account-centered review across banking, brokerage, custody, and linked workflows."),
        _ => ("Accounting Mode", "Fund Operations Workbench", "Fund operations workbench for accounting, reconciliation, and reporting review.")
    };

    private static (string Title, string Detail) DescribeRouteBanner(string pageTag) => pageTag switch
    {
        "FundTrialBalance" => (
            "Routed to Trial Balance Workbench",
            "This deep link opened the accounting trial-balance tab so the selected ledger slice and balance-line review target are explicit."),
        "FundReconciliation" => (
            "Routed to Reconciliation Workbench",
            "This deep link opened the reconciliation tab so operator ownership, queue freshness, and break resolution posture stay visible."),
        "FundReportPack" => (
            "Routed to Report Pack Workbench",
            "This deep link opened the reporting tab so preview freshness, handoff readiness, and sign-off posture are explicit."),
        _ => (
            "Routed to Fund Operations",
            "This navigation landed inside the shared governance workbench.")
    };

    private static string BuildSnapshotWarningText(string timestampText, string fallback)
    {
        if (string.IsNullOrWhiteSpace(timestampText) || string.Equals(timestampText, "-", StringComparison.Ordinal))
        {
            return fallback;
        }

        if (!DateTime.TryParse(timestampText, out var parsedTimestamp))
        {
            return fallback;
        }

        var age = DateTime.Now - parsedTimestamp;
        if (age > TimeSpan.FromMinutes(30))
        {
            return $"Snapshot may be stale ({Math.Round(age.TotalMinutes):N0} minute(s) old). {fallback}";
        }

        return $"Snapshot confirmed at {parsedTimestamp:g}. Refresh again before sign-off if desk activity or ledger posture has changed.";
    }

    private string? ResolveRoutedPageTag(FundOperationsNavigationContext? context)
    {
        var currentPageTag = _navigationService.GetCurrentPageTag();
        if (IsRouteBannerPage(currentPageTag))
        {
            return currentPageTag;
        }

        return context is null
            ? null
            : MapTabToRoutedPageTag(context.Tab);
    }

    private static bool IsRouteBannerPage(string? pageTag) =>
        string.Equals(pageTag, "FundTrialBalance", StringComparison.Ordinal) ||
        string.Equals(pageTag, "FundReconciliation", StringComparison.Ordinal) ||
        string.Equals(pageTag, "FundReportPack", StringComparison.Ordinal);

    private static string? MapTabToRoutedPageTag(FundOperationsTab tab) => tab switch
    {
        FundOperationsTab.TrialBalance => "FundTrialBalance",
        FundOperationsTab.Reconciliation => "FundReconciliation",
        FundOperationsTab.ReportPack => "FundReportPack",
        _ => null
    };

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
        "FundReportPack" => (int)FundOperationsTab.ReportPack,
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
