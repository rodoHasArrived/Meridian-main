using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Shared.Services;
using Meridian.Wpf.Models;
using Meridian.Wpf.Services;
using Meridian.Wpf.Workstation.Models;

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
    private readonly IStatementReconciliationWorkbenchService _statementReconciliationWorkbenchService;
    private readonly IPrivateCapitalCloseCockpitService? _privateCapitalCloseCockpitService;
    private readonly FundLedgerCollectionsSectionViewModel _collectionsSection = new();
    private readonly FundLedgerWorkbenchSectionViewModel _workbenchSection = new();

    private string _title = "Fund Operations";
    private string _statusText = "Select a fund profile to inspect fund operations.";
    private string _overviewStatusText = "Select a fund profile to unlock accounting fund operations.";
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
    private string _reportPackStatusText = "Accounting report-pack preview is waiting for a fund context.";
    private string _reportPackKindText = GovernanceReportKindDto.TrialBalance.ToString();
    private string _reportPackAsOfText = "-";
    private string _reportPackNetAssetsText = "-";
    private string _reportPackTrialBalanceLinesText = "0";
    private string _reportPackAssetSectionsText = "0";
    private string _reportPackGeneratedAtText = "-";
    private GovernanceReportKindDto _selectedReportKind = GovernanceReportKindDto.TrialBalance;
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
    private GovernanceLifecycleProjectionDto? _accountingLifecycle;
    private PrivateCapitalCloseCockpitDto? _privateCapitalCloseCockpit;

    public FundLedgerViewModel(
        FundLedgerReadService fundLedgerReadService,
        FundContextService fundContextService,
        NavigationService navigationService,
        FundAccountReadService fundAccountReadService,
        CashFinancingReadService cashFinancingReadService,
        IFundReconciliationWorkbenchService fundReconciliationWorkbenchService,
        FundOperationsWorkspaceReadService fundOperationsWorkspaceReadService,
        StrategyRunWorkspaceService runWorkspaceService,
        IStatementReconciliationWorkbenchService? statementReconciliationWorkbenchService = null,
        IPrivateCapitalCloseCockpitService? privateCapitalCloseCockpitService = null)
    {
        _fundLedgerReadService = fundLedgerReadService ?? throw new ArgumentNullException(nameof(fundLedgerReadService));
        _fundContextService = fundContextService ?? throw new ArgumentNullException(nameof(fundContextService));
        _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
        _fundAccountReadService = fundAccountReadService ?? throw new ArgumentNullException(nameof(fundAccountReadService));
        _cashFinancingReadService = cashFinancingReadService ?? throw new ArgumentNullException(nameof(cashFinancingReadService));
        _fundReconciliationWorkbenchService = fundReconciliationWorkbenchService ?? throw new ArgumentNullException(nameof(fundReconciliationWorkbenchService));
        _fundOperationsWorkspaceReadService = fundOperationsWorkspaceReadService ?? throw new ArgumentNullException(nameof(fundOperationsWorkspaceReadService));
        _runWorkspaceService = runWorkspaceService ?? throw new ArgumentNullException(nameof(runWorkspaceService));
        _statementReconciliationWorkbenchService = statementReconciliationWorkbenchService ?? new NullStatementReconciliationWorkbenchService();
        _privateCapitalCloseCockpitService = privateCapitalCloseCockpitService;

        AccountQueueTable = new WorkstationTableModel<FundAccountSummary>(
            Accounts,
            [
                new("Account", nameof(FundAccountSummary.DisplayName), 220),
                new("Code", nameof(FundAccountSummary.AccountCode), 120),
                new("Type", nameof(FundAccountSummary.AccountType), 110),
                new("Institution", nameof(FundAccountSummary.Institution), 160),
                new("Structure", nameof(FundAccountSummary.StructureLabel), 190),
                new("Workflow", nameof(FundAccountSummary.WorkflowLabel), 210),
                new("Cash", nameof(FundAccountSummary.CashBalance), 110, "{0:C2}"),
                new("NAV", nameof(FundAccountSummary.NetAssetValue), 110, "{0:C2}"),
                new("Open Breaks", nameof(FundAccountSummary.OpenBreaks), 90)
            ],
            "Fund ledger accounts",
            "No fund accounts loaded",
            "Select a fund profile or load retained fund account records before opening account-level portfolio review.");

        RefreshCommand = new AsyncRelayCommand(LoadAsync);
        OpenAccountingCommand = new RelayCommand(() => _navigationService.NavigateTo("AccountingShell"));
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
        RefreshStatementReconciliationCommand = new AsyncRelayCommand(RefreshStatementReconciliationAsync);

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

    public ObservableCollection<FundTrialBalanceLine> TrialBalance => _collectionsSection.TrialBalance;

    public ObservableCollection<FundJournalLine> Journal => _collectionsSection.Journal;

    public ObservableCollection<FundAccountSummary> Accounts => _collectionsSection.Accounts;

    public WorkstationTableModel<FundAccountSummary> AccountQueueTable { get; }

    public ObservableCollection<BankAccountSnapshot> BankSnapshots => _collectionsSection.BankSnapshots;

    public ObservableCollection<FundPortfolioPosition> PortfolioPositions => _collectionsSection.PortfolioPositions;

    public ObservableCollection<CashFlowEntryDto> CashFlowEntries => _collectionsSection.CashFlowEntries;

    public ObservableCollection<CashLadderBucketDto> CashFlowBuckets => _collectionsSection.CashFlowBuckets;

    public ObservableCollection<FundTrialBalanceLine> VisibleTrialBalance => _collectionsSection.VisibleTrialBalance;

    public ObservableCollection<FundJournalLine> VisibleJournal => _collectionsSection.VisibleJournal;

    public ObservableCollection<FundLedgerDimensionView> LedgerDimensions => _collectionsSection.LedgerDimensions;

    public ObservableCollection<FundReconciliationItem> ReconciliationRuns => _collectionsSection.ReconciliationRuns;

    public ObservableCollection<string> CashFinancingHighlights => _collectionsSection.CashFinancingHighlights;

    public ObservableCollection<FundAuditEntry> AuditTrail => _collectionsSection.AuditTrail;

    public ObservableCollection<FundReportAssetClassSectionDto> ReportPackAssetSections => _collectionsSection.ReportPackAssetSections;

    public ObservableCollection<FundFinancialOperationsQueueRow> FinancialOperationsQueueItems => _collectionsSection.FinancialOperationsQueueItems;

    public ObservableCollection<FundAccountingRecordEvidenceCategoryRow> AccountingRecordEvidenceCategories => _collectionsSection.AccountingRecordEvidenceCategories;

    public ObservableCollection<FundOperationsEvidencePackageRow> OperationsEvidencePackages => _collectionsSection.OperationsEvidencePackages;

    public ObservableCollection<FundOperationsReconciliationLaneRow> OperationsReconciliationLanes => _collectionsSection.OperationsReconciliationLanes;

    public ObservableCollection<FundPrivateCapitalCloseLaneRow> PrivateCapitalCloseLanes => _collectionsSection.PrivateCapitalCloseLanes;

    public ObservableCollection<FundPrivateCapitalEvidencePackageRow> PrivateCapitalEvidencePackages => _collectionsSection.PrivateCapitalEvidencePackages;

    public ObservableCollection<FundPrivateCapitalNavSupportPackageRow> PrivateCapitalNavSupportPackages => _collectionsSection.PrivateCapitalNavSupportPackages;

    public ObservableCollection<FundPrivateCapitalCloseApprovalRow> PrivateCapitalCloseApprovals => _collectionsSection.PrivateCapitalCloseApprovals;

    internal FundLedgerCollectionsSectionViewModel CollectionsSection => _collectionsSection;

    internal FundLedgerWorkbenchSectionViewModel WorkbenchSection => _workbenchSection;

    private InspectorPanelModel _selectedAccountInspector = BuildSelectedAccountInspector(null);

    public InspectorPanelModel SelectedAccountInspector
    {
        get => _selectedAccountInspector;
        private set => SetProperty(ref _selectedAccountInspector, value);
    }

    public string AccountQueueStatusText
        => $"{TotalAccountsText} account(s): {BankAccountsText} bank, {BrokerageAccountsText} brokerage, {CustodyAccountsText} custody.";

    public string SelectedAccountPortfolioTooltip
        => SelectedAccount is null
            ? "Select an account row before opening account portfolio detail."
            : $"Open account portfolio detail for {SelectedAccount.DisplayName}.";

    private bool SetFundLedgerSectionProperty<T>(
        T current,
        Action<T> apply,
        T value,
        [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(current, value))
        {
            return false;
        }

        apply(value);
        RaisePropertyChanged(propertyName);
        return true;
    }

    public IAsyncRelayCommand RefreshCommand { get; }

    public IRelayCommand OpenAccountingCommand { get; }

    public IRelayCommand OpenGovernanceCommand => OpenAccountingCommand;

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
        get => WorkbenchSection.CurrentWorkbenchModeText;
        private set => SetFundLedgerSectionProperty(WorkbenchSection.CurrentWorkbenchModeText, text => WorkbenchSection.CurrentWorkbenchModeText = text, value);
    }

    public string CurrentWorkbenchTitleText
    {
        get => WorkbenchSection.CurrentWorkbenchTitleText;
        private set => SetFundLedgerSectionProperty(WorkbenchSection.CurrentWorkbenchTitleText, text => WorkbenchSection.CurrentWorkbenchTitleText = text, value);
    }

    public string CurrentWorkbenchSubtitleText
    {
        get => WorkbenchSection.CurrentWorkbenchSubtitleText;
        private set => SetFundLedgerSectionProperty(WorkbenchSection.CurrentWorkbenchSubtitleText, text => WorkbenchSection.CurrentWorkbenchSubtitleText = text, value);
    }

    public string RouteBannerTitleText
    {
        get => WorkbenchSection.RouteBannerTitleText;
        private set => SetFundLedgerSectionProperty(WorkbenchSection.RouteBannerTitleText, text => WorkbenchSection.RouteBannerTitleText = text, value);
    }

    public string RouteBannerDetailText
    {
        get => WorkbenchSection.RouteBannerDetailText;
        private set => SetFundLedgerSectionProperty(WorkbenchSection.RouteBannerDetailText, text => WorkbenchSection.RouteBannerDetailText = text, value);
    }

    public bool HasRouteBanner
    {
        get => WorkbenchSection.HasRouteBanner;
        private set => SetFundLedgerSectionProperty(WorkbenchSection.HasRouteBanner, visible => WorkbenchSection.HasRouteBanner = visible, value);
    }

    public string ReconciliationOwnershipText
    {
        get => WorkbenchSection.ReconciliationOwnershipText;
        private set => SetFundLedgerSectionProperty(WorkbenchSection.ReconciliationOwnershipText, text => WorkbenchSection.ReconciliationOwnershipText = text, value);
    }

    public string ReconciliationSnapshotWarningText
    {
        get => WorkbenchSection.ReconciliationSnapshotWarningText;
        private set => SetFundLedgerSectionProperty(WorkbenchSection.ReconciliationSnapshotWarningText, text => WorkbenchSection.ReconciliationSnapshotWarningText = text, value);
    }

    public string ReportPackOwnershipText
    {
        get => WorkbenchSection.ReportPackOwnershipText;
        private set => SetFundLedgerSectionProperty(WorkbenchSection.ReportPackOwnershipText, text => WorkbenchSection.ReportPackOwnershipText = text, value);
    }

    public string ReportPackSnapshotWarningText
    {
        get => WorkbenchSection.ReportPackSnapshotWarningText;
        private set => SetFundLedgerSectionProperty(WorkbenchSection.ReportPackSnapshotWarningText, text => WorkbenchSection.ReportPackSnapshotWarningText = text, value);
    }

    public string FinancialOperationsQueueStatusText
    {
        get => WorkbenchSection.FinancialOperationsQueueStatusText;
        private set => SetFundLedgerSectionProperty(WorkbenchSection.FinancialOperationsQueueStatusText, text => WorkbenchSection.FinancialOperationsQueueStatusText = text, value);
    }

    public string FinancialOperationsQueueSummaryText
    {
        get => WorkbenchSection.FinancialOperationsQueueSummaryText;
        private set => SetFundLedgerSectionProperty(WorkbenchSection.FinancialOperationsQueueSummaryText, text => WorkbenchSection.FinancialOperationsQueueSummaryText = text, value);
    }

    public string AccountingRecordStatusText
    {
        get => WorkbenchSection.AccountingRecordStatusText;
        private set => SetFundLedgerSectionProperty(WorkbenchSection.AccountingRecordStatusText, text => WorkbenchSection.AccountingRecordStatusText = text, value);
    }

    public string AccountingRecordSummaryText
    {
        get => WorkbenchSection.AccountingRecordSummaryText;
        private set => SetFundLedgerSectionProperty(WorkbenchSection.AccountingRecordSummaryText, text => WorkbenchSection.AccountingRecordSummaryText = text, value);
    }

    public string AccountingRecordEvidenceText
    {
        get => WorkbenchSection.AccountingRecordEvidenceText;
        private set => SetFundLedgerSectionProperty(WorkbenchSection.AccountingRecordEvidenceText, text => WorkbenchSection.AccountingRecordEvidenceText = text, value);
    }

    public string PrivateCapitalCloseStatusText
    {
        get => WorkbenchSection.PrivateCapitalCloseStatusText;
        private set => SetFundLedgerSectionProperty(WorkbenchSection.PrivateCapitalCloseStatusText, text => WorkbenchSection.PrivateCapitalCloseStatusText = text, value);
    }

    public string PrivateCapitalCloseSummaryText
    {
        get => WorkbenchSection.PrivateCapitalCloseSummaryText;
        private set => SetFundLedgerSectionProperty(WorkbenchSection.PrivateCapitalCloseSummaryText, text => WorkbenchSection.PrivateCapitalCloseSummaryText = text, value);
    }

    public string PrivateCapitalCloseEvidenceText
    {
        get => WorkbenchSection.PrivateCapitalCloseEvidenceText;
        private set => SetFundLedgerSectionProperty(WorkbenchSection.PrivateCapitalCloseEvidenceText, text => WorkbenchSection.PrivateCapitalCloseEvidenceText = text, value);
    }

    public string PrivateCapitalCloseBlockerText
    {
        get => WorkbenchSection.PrivateCapitalCloseBlockerText;
        private set => SetFundLedgerSectionProperty(WorkbenchSection.PrivateCapitalCloseBlockerText, text => WorkbenchSection.PrivateCapitalCloseBlockerText = text, value);
    }

    public WorkstationStateModel ReviewedAutomationState
    {
        get => WorkbenchSection.ReviewedAutomationState;
        private set => SetFundLedgerSectionProperty(WorkbenchSection.ReviewedAutomationState, state => WorkbenchSection.ReviewedAutomationState = state, value);
    }

    public WorkstationStateModel ReportPackReadinessState
    {
        get => WorkbenchSection.ReportPackReadinessState;
        private set => SetFundLedgerSectionProperty(WorkbenchSection.ReportPackReadinessState, state => WorkbenchSection.ReportPackReadinessState = state, value);
    }

    public WorkstationStateModel AccountingRecordReadinessState
    {
        get => WorkbenchSection.AccountingRecordReadinessState;
        private set => SetFundLedgerSectionProperty(WorkbenchSection.AccountingRecordReadinessState, state => WorkbenchSection.AccountingRecordReadinessState = state, value);
    }

    public WorkstationStateModel PrivateCapitalCloseReadinessState
    {
        get => WorkbenchSection.PrivateCapitalCloseReadinessState;
        private set => SetFundLedgerSectionProperty(WorkbenchSection.PrivateCapitalCloseReadinessState, state => WorkbenchSection.PrivateCapitalCloseReadinessState = state, value);
    }

    public bool IsReportPackLoading
    {
        get => WorkbenchSection.IsReportPackLoading;
        private set => SetFundLedgerSectionProperty(WorkbenchSection.IsReportPackLoading, loading => WorkbenchSection.IsReportPackLoading = loading, value);
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
        get => WorkbenchSection.SelectedTabIndex;
        set
        {
            if (SetFundLedgerSectionProperty(WorkbenchSection.SelectedTabIndex, tab => WorkbenchSection.SelectedTabIndex = tab, value))
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
                SelectedAccountInspector = BuildSelectedAccountInspector(value);
                RaisePropertyChanged(nameof(SelectedAccountPortfolioTooltip));
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
        var accountingWorkspaceTask = _fundOperationsWorkspaceReadService.GetWorkspaceAsync(new FundOperationsWorkspaceQuery(
            FundProfileId: activeFund.FundProfileId,
            Currency: activeFund.BaseCurrency), ct);
        var privateCapitalCloseTask = LoadPrivateCapitalCloseCockpitAsync(activeFund, ct);

        await Task.WhenAll(ledgerTask, accountsTask, bankSnapshotsTask, cashTask, reconciliationTask, portfolioTask, accountingWorkspaceTask, privateCapitalCloseTask);

        var ledger = await ledgerTask;
        var accounts = await accountsTask;
        var bankSnapshots = await bankSnapshotsTask;
        var cashSummary = await cashTask;
        var reconciliationSnapshot = await reconciliationTask;
        var portfolioPositions = await portfolioTask;
        var accountingWorkspace = await accountingWorkspaceTask;
        var privateCapitalCloseCockpit = await privateCapitalCloseTask;
        _accountingLifecycle = accountingWorkspace.Governance;

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is not null)
        {
            await dispatcher.InvokeAsync(async () =>
            {
                Title = $"Fund Operations · {activeFund.DisplayName}";
                StatusText = accounts.Count == 0 && ledger?.JournalEntryCount is not > 0
                    ? "Fund operations are ready, but no linked account or ledger activity has been recorded yet."
                    : "Accounting fund operations are loaded for the active fund profile.";

                ApplyLedger(ledger);
                ApplyAccounts(accounts, context);
                BuildLedgerDimensions(activeFund, ledger);
                ApplyBankSnapshots(bankSnapshots);
                ApplyCashSummary(cashSummary);
                ApplyPortfolio(portfolioPositions);
                await ApplyReconciliationWorkbenchAsync(activeFund, reconciliationSnapshot, ct);
                await LoadStatementReconciliationAsync(ct);
                BuildWorkspaceSummary(activeFund, ledger, accounts, cashSummary, reconciliationSnapshot.Summary);
                BuildAuditTrail(ledger, reconciliationSnapshot.Summary);
                await RefreshReportPackPreviewAsync(ct);
                UpdateReconciliationWorkbenchPresentation();
                UpdateAccountingRecordWorkbenchPresentation();
                UpdateReportPackWorkbenchPresentation();
                ApplyPrivateCapitalCloseCockpit(privateCapitalCloseCockpit);
            });
        }
        else
        {
            Title = $"Fund Operations · {activeFund.DisplayName}";
            StatusText = accounts.Count == 0 && ledger?.JournalEntryCount is not > 0
                ? "Fund operations are ready, but no linked account or ledger activity has been recorded yet."
                : "Accounting fund operations are loaded for the active fund profile.";

            ApplyLedger(ledger);
            ApplyAccounts(accounts, context);
            BuildLedgerDimensions(activeFund, ledger);
            ApplyBankSnapshots(bankSnapshots);
            ApplyCashSummary(cashSummary);
            ApplyPortfolio(portfolioPositions);
            await ApplyReconciliationWorkbenchAsync(activeFund, reconciliationSnapshot, ct);
            await LoadStatementReconciliationAsync(ct);
            BuildWorkspaceSummary(activeFund, ledger, accounts, cashSummary, reconciliationSnapshot.Summary);
            BuildAuditTrail(ledger, reconciliationSnapshot.Summary);
            await RefreshReportPackPreviewAsync(ct);
            UpdateReconciliationWorkbenchPresentation();
            UpdateAccountingRecordWorkbenchPresentation();
            UpdateReportPackWorkbenchPresentation();
            ApplyPrivateCapitalCloseCockpit(privateCapitalCloseCockpit);
        }
    }

    private async Task<PrivateCapitalCloseCockpitDto?> LoadPrivateCapitalCloseCockpitAsync(
        FundProfileDetail activeFund,
        CancellationToken ct)
    {
        if (_privateCapitalCloseCockpitService is null)
        {
            return null;
        }

        return await _privateCapitalCloseCockpitService
            .GetCockpitAsync(fundProfileId: activeFund.FundProfileId, ct: ct)
            .ConfigureAwait(false);
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
        OverviewStatusText = "Select a fund profile to unlock accounting fund operations.";
        PortfolioStatusText = "No fund-scoped portfolio posture is available yet.";
        BankingStatusText = "No banking snapshots are available yet.";
        ReconciliationStatusText = "No reconciliation runs are available yet.";
        SecurityCoverageText = "Security Master coverage has not been evaluated yet.";
        WorkspaceAsOfText = "-";
        TotalAccountsText = "0";
        BankAccountsText = "0";
        BrokerageAccountsText = "0";
        CustodyAccountsText = "0";
        RaisePropertyChanged(nameof(AccountQueueStatusText));
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
        FinancialOperationsQueueItems.Clear();
        AccountingRecordEvidenceCategories.Clear();
        OperationsEvidencePackages.Clear();
        OperationsReconciliationLanes.Clear();
        PrivateCapitalCloseLanes.Clear();
        PrivateCapitalEvidencePackages.Clear();
        PrivateCapitalNavSupportPackages.Clear();
        PrivateCapitalCloseApprovals.Clear();
        _reportPackPreview = null;
        _accountingLifecycle = null;
        _privateCapitalCloseCockpit = null;
        FinancialOperationsQueueStatusText = "Queue pending";
        FinancialOperationsQueueSummaryText = "Load Operations Continuity and close cockpit evidence to inspect active Financial Operations work items.";
        AccountingRecordStatusText = "Accounting-record evidence is waiting for fund context.";
        AccountingRecordSummaryText = "Load Operations Continuity detail to inspect retained source records, normalized activity, reconciliation cases, ledger evidence, approvals, and report-pack lineage.";
        AccountingRecordEvidenceText = "0/8 evidence categories complete; 60 sec target not measured";
        AccountingRecordReadinessState = WorkstationStateModel.Empty(
            "Accounting record waiting for fund context",
            "Select a fund profile to inspect retained source, normalized activity, reconciliation, ledger, approval, and report-pack evidence.",
            "Open Operations Continuity",
            "OperationsContinuity");
        PrivateCapitalCloseStatusText = "Private-capital close cockpit is waiting for fund context.";
        PrivateCapitalCloseSummaryText = "Load the close cockpit to inspect partner capital tie-outs, NAV support, approval history, close-package evidence, and period-lock readiness.";
        PrivateCapitalCloseEvidenceText = "0 evidence links";
        PrivateCapitalCloseBlockerText = "No source-backed close cockpit has been loaded.";
        ReviewedAutomationState = WorkstationStateModel.Empty(
            "Reviewed automation pending",
            "Load Operations Continuity detail to inspect shared automation review posture.",
            "Open Operations Continuity",
            "OperationsContinuity");
        PrivateCapitalCloseReadinessState = WorkstationStateModel.Empty(
            "Close cockpit waiting for fund context",
            "Select a fund profile to inspect source-backed private-capital close lanes, NAV support packages, approval history, and period-lock readiness.",
            "Open Operations Close",
            "OperationsClose");
        ReportPackStatusText = "Accounting report-pack preview is waiting for a fund context.";
        ReportPackReadinessState = WorkstationStateModel.Empty(
            "Report pack waiting for fund context",
            "Select a fund profile and refresh the preview before distributing reporting artifacts.",
            "Refresh Preview",
            "Report Pack");
        ReportPackKindText = GovernanceReportKindDto.TrialBalance.ToString();
        ReportPackAsOfText = "-";
        ReportPackNetAssetsText = "-";
        ReportPackTrialBalanceLinesText = "0";
        ReportPackAssetSectionsText = "0";
        ReportPackGeneratedAtText = "-";
        ResetReconciliationWorkbenchState();
        ResetStatementReconciliationState();
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
        RaisePropertyChanged(nameof(AccountQueueStatusText));
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
        RaisePropertyChanged(nameof(AccountQueueStatusText));
        OverviewStatusText = summary.TotalAccounts == 0 && summary.JournalEntryCount == 0
            ? "The Accounting shell is ready. Link accounts, import positions, or record a run to populate fund operations."
            : BuildOverviewStatus(summary);
    }

    internal static InspectorPanelModel BuildSelectedAccountInspector(FundAccountSummary? account)
    {
        if (account is null)
        {
            return new InspectorPanelModel
            {
                Title = "No account selected",
                Subtitle = "Account portfolio action blocked",
                Detail = "Select an account row to inspect balances, workflow links, and open-break pressure before opening account-level portfolio review."
            };
        }

        return new InspectorPanelModel
        {
            Title = account.DisplayName,
            Subtitle = $"{account.AccountType} account",
            Detail = "Use this selected account context for account-level portfolio, banking, and reconciliation drill-throughs.",
            Badge = new WorkstationBadgeModel(
                "State",
                account.IsActive ? "Active" : "Inactive",
                "\uE8D4",
                account.IsActive ? WorkspaceTone.Success : WorkspaceTone.Warning),
            Facts =
            [
                new("Code", account.AccountCode),
                new("Currency", account.BaseCurrency),
                new("Institution", account.Institution ?? "Not set"),
                new("Structure", account.StructureLabel),
                new("Workflow", account.WorkflowLabel),
                new("Cash", account.CashBalance.ToString("C2")),
                new("Securities", account.SecuritiesMarketValue.ToString("C2")),
                new("NAV", account.NetAssetValue.ToString("C2")),
                new("Open breaks", account.OpenBreaks.ToString("N0")),
                new("Last snapshot", account.LastSnapshotDate?.ToString("MMM dd yyyy") ?? "No retained snapshot")
            ]
        };
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

        if (_accountingLifecycle is { WorkflowUpdatedAtUtc: { } workflowUpdatedAt } accountingLifecycle)
        {
            AuditTrail.Add(new FundAuditEntry(
                Timestamp: workflowUpdatedAt,
                Category: "Approval lifecycle",
                Description: accountingLifecycle.AuditTraceability,
                Reference: accountingLifecycle.ActiveWorkflowId ?? "shared-accounting"));
        }

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
            ReportPackStatusText = "Select a fund profile to build an accounting report-pack preview.";
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
        var signoffPosture = _accountingLifecycle?.SignoffPosture;
        var closeReadiness = _accountingLifecycle?.CloseReadiness;
        ReconciliationOwnershipText = string.IsNullOrWhiteSpace(signoffPosture)
            ? $"{operatorLabel}. Reconciliation sign-off should happen only after queue review, stale-snapshot confirmation, and security coverage checks are complete."
            : $"{operatorLabel}. {signoffPosture} {closeReadiness}".Trim();
        ReconciliationSnapshotWarningText = BuildSnapshotWarningText(
            ReconciliationLastRefreshText,
            "Queue refresh timing is not confirmed. Refresh before resolving breaks or signing off.");
    }

    private void UpdateReportPackWorkbenchPresentation()
    {
        var reportOwner = string.IsNullOrWhiteSpace(ReconciliationOperatorText) ||
                          string.Equals(ReconciliationOperatorText, DefaultReconciliationOperator, StringComparison.OrdinalIgnoreCase)
            ? "Accounting operator"
            : ReconciliationOperatorText;
        var readiness = _accountingLifecycle?.CloseReadiness;
        var traceability = _accountingLifecycle?.AuditTraceability;
        ReportPackOwnershipText = string.IsNullOrWhiteSpace(readiness)
            ? $"{reportOwner} owns final report-pack sign-off once accounting, reconciliation, and audit evidence align."
            : $"{reportOwner} owns final report-pack sign-off. {readiness}";
        ReportPackSnapshotWarningText = BuildSnapshotWarningText(
            ReportPackGeneratedAtText,
            string.IsNullOrWhiteSpace(traceability)
                ? "Report-pack freshness is unknown. Refresh the preview before distributing reporting artifacts."
                : $"{traceability} Refresh the preview before distributing reporting artifacts.");
        ReportPackReadinessState = BuildReportPackReadinessState(reportOwner, readiness, traceability);
    }

    private void UpdateAccountingRecordWorkbenchPresentation()
    {
        var summary = _accountingLifecycle?.AccountingRecordSummary;
        AccountingRecordEvidenceCategories.Clear();
        OperationsEvidencePackages.Clear();
        OperationsReconciliationLanes.Clear();
        ReviewedAutomationState = BuildReviewedAutomationState(_accountingLifecycle?.ReviewedAutomation);

        foreach (var package in _accountingLifecycle?.EvidencePackages ?? [])
        {
            OperationsEvidencePackages.Add(new FundOperationsEvidencePackageRow(
                PackageId: package.PackageId,
                Label: package.Label,
                StatusLabel: FormatEvidenceStatusLabel(package.Status),
                Summary: package.Summary,
                CategoryLabel: FormatEvidencePackageCategoryLabel(package.CompleteCategoryCount, package.RequiredCategoryCount),
                EvidenceLabel: FormatEvidenceCount(CountEvidenceLinks(package)),
                RequiredActionsLabel: FormatRequiredActions(package.RequiredActions),
                SourceTarget: MapAccountingRecordRouteTarget(package.RouteHint),
                EvidenceSubject: BuildOperationsEvidencePackageSubject(package.PackageId),
                EvidenceSubjectTarget: BuildOperationsEvidencePackageSubjectTarget(package.PackageId),
                IsReady: package.IsReady));
        }

        foreach (var lane in _accountingLifecycle?.ReconciliationLanes ?? [])
        {
            OperationsReconciliationLanes.Add(new FundOperationsReconciliationLaneRow(
                LaneId: lane.LaneId,
                Label: lane.Label,
                StatusLabel: FormatReconciliationLaneStatusLabel(lane.Status),
                Summary: lane.Summary,
                BreaksLabel: FormatBreakCount(lane.BreakCount),
                EvidenceLabel: FormatEvidenceCount(CountEvidenceLinks(lane)),
                RequiredActionsLabel: FormatRequiredActions(lane.RequiredActions),
                SourceTarget: MapAccountingRecordRouteTarget(lane.RouteHint),
                EvidenceSubject: BuildOperationsReconciliationLaneSubject(lane.LaneId),
                EvidenceSubjectTarget: BuildOperationsReconciliationLaneSubjectTarget(lane.LaneId),
                IsReady: lane.IsReady));
        }

        if (summary is null)
        {
            AccountingRecordStatusText = "Accounting-record evidence is not available for the active fund workflow.";
            AccountingRecordSummaryText = "Operations Continuity has not returned a shared accounting-record summary for this fund context.";
            AccountingRecordEvidenceText = "0/8 evidence categories complete; 60 sec target not measured";
            AccountingRecordReadinessState = WorkstationStateModel.Empty(
                "Accounting record evidence pending",
                AccountingRecordSummaryText,
                "Open Operations Continuity",
                "OperationsContinuity");
            UpdateFinancialOperationsQueuePresentation();
            return;
        }

        foreach (var category in summary.EvidenceCategories)
        {
            AccountingRecordEvidenceCategories.Add(new FundAccountingRecordEvidenceCategoryRow(
                Key: category.Key,
                Label: string.IsNullOrWhiteSpace(category.Label) ? category.Key : category.Label,
                StatusLabel: category.IsComplete ? "Complete" : "Review required",
                StatusDetail: string.IsNullOrWhiteSpace(category.Status)
                    ? category.IsComplete ? "Evidence category is complete." : "Evidence category still needs review."
                    : category.Status,
                RequiredEvidenceLabel: category.RequiredEvidence is { Count: > 0 }
                    ? string.Join(", ", category.RequiredEvidence.Where(static item => !string.IsNullOrWhiteSpace(item)))
                    : "Required evidence not specified",
                EvidenceLabel: category.EvidenceLinks.Count == 0
                    ? "No linked evidence"
                    : $"{category.EvidenceLinks.Count} evidence link{(category.EvidenceLinks.Count == 1 ? string.Empty : "s")}",
                SourceTarget: MapAccountingRecordRouteTarget(category.RouteHint),
                EvidenceSubject: BuildAccountingRecordEvidenceSubject(summary.RecordId),
                EvidenceSubjectTarget: BuildAccountingRecordEvidenceSubjectTarget(summary.RecordId),
                IsComplete: category.IsComplete));
        }

        AccountingRecordStatusText = summary.IsAuditReady ? "Audit ready" : "Review required";
        AccountingRecordSummaryText = string.IsNullOrWhiteSpace(summary.Summary)
            ? $"{summary.CompleteCategoryCount}/{summary.RequiredCategoryCount} required accounting-record evidence categories are complete."
            : summary.Summary;
        AccountingRecordEvidenceText = BuildAccountingRecordEvidenceText(summary);
        AccountingRecordReadinessState = BuildAccountingRecordReadinessState(summary);
        UpdateFinancialOperationsQueuePresentation();
    }

    private void UpdateFinancialOperationsQueuePresentation()
    {
        FinancialOperationsQueueItems.Clear();

        var lifecycle = _accountingLifecycle;
        if (lifecycle is not null)
        {
            foreach (var breakCase in lifecycle.BreakCases.Where(static item => !IsBreakCaseClosed(item)))
            {
                var blockedOutputs = breakCase.BlockedOutputs?
                    .Where(static output => !string.IsNullOrWhiteSpace(output))
                    .ToArray() ?? [];
                var escalation = string.IsNullOrWhiteSpace(breakCase.EscalationLevel)
                    ? "No escalation recorded"
                    : string.IsNullOrWhiteSpace(breakCase.EscalationReason)
                        ? breakCase.EscalationLevel!
                        : $"{breakCase.EscalationLevel}: {breakCase.EscalationReason}";
                var action = string.IsNullOrWhiteSpace(breakCase.SuggestedAction)
                    ? blockedOutputs.Length == 0 ? "Resolve or assign the reconciliation break." : $"Unblock {string.Join(", ", blockedOutputs)}."
                    : breakCase.SuggestedAction!;
                var route = breakCase.EvidenceLinks.FirstOrDefault()?.Route;

                FinancialOperationsQueueItems.Add(new FundFinancialOperationsQueueRow(
                    QueueId: $"break:{breakCase.BreakId}",
                    KindLabel: "Break",
                    Label: breakCase.BreakId,
                    StatusLabel: FormatQueueStatusLabel(breakCase.Status),
                    Detail: $"{breakCase.Category} / {breakCase.CheckId}; {escalation}",
                    OwnerLabel: string.IsNullOrWhiteSpace(breakCase.Owner) ? "Unassigned" : breakCase.Owner!,
                    TimingLabel: BuildBreakCaseTimingLabel(breakCase),
                    EvidenceLabel: FormatEvidenceCount(breakCase.EvidenceLinks.Count),
                    ActionLabel: action,
                    SourceTarget: MapAccountingRecordRouteTarget(route),
                    IsBlocked: IsBreakCaseBlocked(breakCase)));
            }

            foreach (var task in lifecycle.CloseChecklist.Where(static item => !IsChecklistTaskComplete(item)))
            {
                FinancialOperationsQueueItems.Add(new FundFinancialOperationsQueueRow(
                    QueueId: $"checklist:{task.TaskId}",
                    KindLabel: "Checklist",
                    Label: string.IsNullOrWhiteSpace(task.Label) ? task.Gate.ToString() : task.Label,
                    StatusLabel: FormatQueueStatusLabel(task.Status),
                    Detail: string.IsNullOrWhiteSpace(task.RequiredEvidence) ? "Required evidence pending." : task.RequiredEvidence,
                    OwnerLabel: string.IsNullOrWhiteSpace(task.Owner) ? "Owner pending" : task.Owner,
                    TimingLabel: BuildChecklistTimingLabel(task),
                    EvidenceLabel: string.IsNullOrWhiteSpace(task.EvidencePointer) ? "Evidence pointer pending" : task.EvidencePointer!,
                    ActionLabel: string.IsNullOrWhiteSpace(task.BlockingReason)
                        ? task.CanAcknowledge ? "Ready for acknowledgement" : "Complete checklist evidence and approvals."
                        : task.BlockingReason!,
                    SourceTarget: MapAccountingRecordRouteTarget(task.RemediationRoute),
                    IsBlocked: IsChecklistTaskBlocked(task)));
            }

            foreach (var approval in lifecycle.Approvals
                .Where(static item => item.Status is not OperationsApprovalStateDto.Approved)
                .OrderByDescending(static item => item.DecidedAtUtc ?? item.SubmittedAtUtc ?? DateTimeOffset.MinValue))
            {
                var route = approval.EvidenceLinks.FirstOrDefault()?.Route;
                FinancialOperationsQueueItems.Add(new FundFinancialOperationsQueueRow(
                    QueueId: $"approval:{approval.ApprovalId}",
                    KindLabel: "Approval",
                    Label: approval.ApprovalId,
                    StatusLabel: FormatQueueStatusLabel(approval.Status.ToString()),
                    Detail: string.IsNullOrWhiteSpace(approval.Rationale) ? "Approval rationale pending." : approval.Rationale!,
                    OwnerLabel: BuildApprovalOwnerLabel(approval.Operator, approval.Reviewer),
                    TimingLabel: BuildApprovalTimingLabel(approval.SubmittedAtUtc, approval.DecidedAtUtc),
                    EvidenceLabel: FormatEvidenceCount(approval.EvidenceLinks.Count),
                    ActionLabel: approval.Status is OperationsApprovalStateDto.Rejected
                        ? "Resolve rejection before close sign-off."
                        : "Complete workflow approval.",
                    SourceTarget: MapAccountingRecordRouteTarget(route ?? "approval"),
                    IsBlocked: approval.Status is OperationsApprovalStateDto.Rejected));
            }

            foreach (var lane in lifecycle.ReconciliationLanes.Where(static item => !item.IsReady || item.BreakCount > 0))
            {
                FinancialOperationsQueueItems.Add(new FundFinancialOperationsQueueRow(
                    QueueId: $"reconciliation-lane:{lane.LaneId}",
                    KindLabel: "Reconciliation",
                    Label: string.IsNullOrWhiteSpace(lane.Label) ? lane.LaneId : lane.Label,
                    StatusLabel: FormatReconciliationLaneStatusLabel(lane.Status),
                    Detail: string.IsNullOrWhiteSpace(lane.Summary) ? "Reconciliation lane requires review." : lane.Summary,
                    OwnerLabel: "Accounting operations",
                    TimingLabel: FormatBreakCount(lane.BreakCount),
                    EvidenceLabel: FormatEvidenceCount(CountEvidenceLinks(lane)),
                    ActionLabel: FormatRequiredActions(lane.RequiredActions),
                    SourceTarget: MapAccountingRecordRouteTarget(lane.RouteHint),
                    IsBlocked: lane.Status is OperationsReconciliationLaneStatusDto.Blocked or OperationsReconciliationLaneStatusDto.Missing));
            }

            foreach (var package in lifecycle.EvidencePackages.Where(static item => !item.IsReady))
            {
                FinancialOperationsQueueItems.Add(new FundFinancialOperationsQueueRow(
                    QueueId: $"evidence-package:{package.PackageId}",
                    KindLabel: "Evidence package",
                    Label: string.IsNullOrWhiteSpace(package.Label) ? package.PackageId : package.Label,
                    StatusLabel: FormatEvidenceStatusLabel(package.Status),
                    Detail: string.IsNullOrWhiteSpace(package.Summary) ? "Evidence package requires review." : package.Summary,
                    OwnerLabel: "Evidence operations",
                    TimingLabel: FormatEvidencePackageCategoryLabel(package.CompleteCategoryCount, package.RequiredCategoryCount),
                    EvidenceLabel: FormatEvidenceCount(CountEvidenceLinks(package)),
                    ActionLabel: FormatRequiredActions(package.RequiredActions),
                    SourceTarget: MapAccountingRecordRouteTarget(package.RouteHint),
                    IsBlocked: IsEvidenceStatusBlocked(package.Status)));
            }
        }

        var cockpit = _privateCapitalCloseCockpit;
        if (cockpit is not null)
        {
            foreach (var lane in cockpit.Lanes.Where(static item => !item.IsReady))
            {
                FinancialOperationsQueueItems.Add(new FundFinancialOperationsQueueRow(
                    QueueId: $"private-capital-lane:{lane.LaneId}",
                    KindLabel: "Private-capital close",
                    Label: string.IsNullOrWhiteSpace(lane.Label) ? lane.LaneId : lane.Label,
                    StatusLabel: FormatEvidenceStatusLabel(lane.Status),
                    Detail: string.IsNullOrWhiteSpace(lane.Summary) ? "Private-capital close lane requires review." : lane.Summary,
                    OwnerLabel: "Fund operations",
                    TimingLabel: "Close lane",
                    EvidenceLabel: FormatEvidenceCount(CountEvidenceLinks(lane)),
                    ActionLabel: FormatRequiredActions(lane.RequiredActions),
                    SourceTarget: MapPrivateCapitalCloseRouteTarget(lane.Route),
                    IsBlocked: IsEvidenceStatusBlocked(lane.Status)));
            }

            foreach (var package in cockpit.EvidencePackages.Where(static item => !item.IsReady))
            {
                FinancialOperationsQueueItems.Add(new FundFinancialOperationsQueueRow(
                    QueueId: $"private-capital-package:{package.PackageId}",
                    KindLabel: "Private-capital package",
                    Label: string.IsNullOrWhiteSpace(package.Label) ? package.PackageId : package.Label,
                    StatusLabel: FormatEvidenceStatusLabel(package.Status),
                    Detail: string.IsNullOrWhiteSpace(package.Summary) ? "Private-capital evidence package requires review." : package.Summary,
                    OwnerLabel: "Fund operations",
                    TimingLabel: FormatEvidencePackageCategoryLabel(package.CompleteCategoryCount, package.RequiredCategoryCount),
                    EvidenceLabel: FormatEvidenceCount(CountEvidenceLinks(package)),
                    ActionLabel: FormatRequiredActions(package.RequiredActions),
                    SourceTarget: MapPrivateCapitalCloseRouteTarget(package.RouteHint),
                    IsBlocked: IsEvidenceStatusBlocked(package.Status)));
            }

            foreach (var package in cockpit.NavSupportPackages.Where(static item => !item.IsReady))
            {
                FinancialOperationsQueueItems.Add(new FundFinancialOperationsQueueRow(
                    QueueId: $"nav-support:{package.PackageId}",
                    KindLabel: "NAV support",
                    Label: string.IsNullOrWhiteSpace(package.Label) ? package.PackageId : package.Label,
                    StatusLabel: FormatEvidenceStatusLabel(package.Status),
                    Detail: string.IsNullOrWhiteSpace(package.Summary) ? "NAV support package requires review." : package.Summary,
                    OwnerLabel: "Fund operations",
                    TimingLabel: FormatNavSupportComponentLabel(package.Components),
                    EvidenceLabel: FormatEvidenceCount(CountEvidenceLinks(package)),
                    ActionLabel: FormatRequiredActions(package.RequiredActions),
                    SourceTarget: MapPrivateCapitalCloseRouteTarget(package.Route),
                    IsBlocked: IsEvidenceStatusBlocked(package.Status)));
            }

            foreach (var approval in cockpit.ApprovalHistory
                .Where(static item => item.Status is not OperationsApprovalStateDto.Approved)
                .OrderByDescending(static item => item.DecidedAtUtc ?? item.SubmittedAtUtc ?? DateTimeOffset.MinValue))
            {
                FinancialOperationsQueueItems.Add(new FundFinancialOperationsQueueRow(
                    QueueId: $"private-capital-approval:{approval.ApprovalId}",
                    KindLabel: "Private-capital approval",
                    Label: approval.ApprovalId,
                    StatusLabel: FormatQueueStatusLabel(approval.Status.ToString()),
                    Detail: string.IsNullOrWhiteSpace(approval.Rationale) ? "Approval rationale pending." : approval.Rationale!,
                    OwnerLabel: BuildApprovalOwnerLabel(approval.Operator, approval.Reviewer),
                    TimingLabel: BuildApprovalTimingLabel(approval.SubmittedAtUtc, approval.DecidedAtUtc),
                    EvidenceLabel: FormatEvidenceCount(CountEvidenceLinks(approval)),
                    ActionLabel: approval.Status is OperationsApprovalStateDto.Rejected
                        ? "Resolve rejection before close sign-off."
                        : "Complete private-capital approval.",
                    SourceTarget: MapPrivateCapitalCloseRouteTarget(approval.WorkflowRoute),
                    IsBlocked: approval.Status is OperationsApprovalStateDto.Rejected));
            }
        }

        var itemCount = FinancialOperationsQueueItems.Count;
        var blockedCount = FinancialOperationsQueueItems.Count(static item => item.IsBlocked);
        if (itemCount == 0)
        {
            FinancialOperationsQueueStatusText = lifecycle is null && cockpit is null ? "Queue pending" : "Clear";
            FinancialOperationsQueueSummaryText = lifecycle is null && cockpit is null
                ? "Load Operations Continuity and close cockpit evidence to inspect active Financial Operations work items."
                : "No active Financial Operations queue items are surfaced for the selected fund.";
            return;
        }

        FinancialOperationsQueueStatusText = $"{itemCount:N0} active item{(itemCount == 1 ? string.Empty : "s")}";
        FinancialOperationsQueueSummaryText =
            $"{blockedCount:N0} blocked, {itemCount - blockedCount:N0} review item{(itemCount - blockedCount == 1 ? string.Empty : "s")} across reconciliation, close checklist, approvals, evidence packages, NAV support, and private-capital close.";
    }

    private void ApplyPrivateCapitalCloseCockpit(PrivateCapitalCloseCockpitDto? cockpit)
    {
        _privateCapitalCloseCockpit = cockpit;
        PrivateCapitalCloseLanes.Clear();
        PrivateCapitalEvidencePackages.Clear();
        PrivateCapitalNavSupportPackages.Clear();
        PrivateCapitalCloseApprovals.Clear();

        if (cockpit is null)
        {
            PrivateCapitalCloseStatusText = "Close cockpit unavailable";
            PrivateCapitalCloseSummaryText = "The shared private-capital close cockpit service is not registered for this desktop context.";
            PrivateCapitalCloseEvidenceText = "0 evidence links";
            PrivateCapitalCloseBlockerText = "Register the Financial Operations close cockpit service before using this WPF close view.";
            PrivateCapitalCloseReadinessState = WorkstationStateModel.Empty(
                "Close cockpit unavailable",
                PrivateCapitalCloseSummaryText,
                "Open Operations Close",
                "OperationsClose");
            UpdateFinancialOperationsQueuePresentation();
            return;
        }

        foreach (var lane in cockpit.Lanes)
        {
            PrivateCapitalCloseLanes.Add(new FundPrivateCapitalCloseLaneRow(
                LaneId: lane.LaneId,
                Label: lane.Label,
                StatusLabel: FormatEvidenceStatusLabel(lane.Status),
                Summary: lane.Summary,
                EvidenceLabel: FormatEvidenceCount(CountEvidenceLinks(lane)),
                RequiredActionsLabel: FormatRequiredActions(lane.RequiredActions),
                SourceTarget: MapPrivateCapitalCloseRouteTarget(lane.Route),
                IsReady: lane.IsReady));
        }

        foreach (var package in cockpit.EvidencePackages)
        {
            PrivateCapitalEvidencePackages.Add(new FundPrivateCapitalEvidencePackageRow(
                PackageId: package.PackageId,
                Label: package.Label,
                StatusLabel: FormatEvidenceStatusLabel(package.Status),
                Summary: package.Summary,
                CategoryLabel: FormatEvidencePackageCategoryLabel(package.CompleteCategoryCount, package.RequiredCategoryCount),
                EvidenceLabel: FormatEvidenceCount(CountEvidenceLinks(package)),
                RequiredActionsLabel: FormatRequiredActions(package.RequiredActions),
                SourceTarget: MapPrivateCapitalCloseRouteTarget(package.RouteHint),
                IsReady: package.IsReady));
        }

        foreach (var package in cockpit.NavSupportPackages)
        {
            PrivateCapitalNavSupportPackages.Add(new FundPrivateCapitalNavSupportPackageRow(
                PackageId: package.PackageId,
                Label: package.Label,
                StatusLabel: FormatEvidenceStatusLabel(package.Status),
                Summary: package.Summary,
                ShadowNavLabel: FormatShadowNavLabel(package.ShadowNav, package.Currency),
                EvidenceLabel: FormatEvidenceCount(CountEvidenceLinks(package)),
                ComponentLabel: FormatNavSupportComponentLabel(package.Components),
                SourceTarget: MapPrivateCapitalCloseRouteTarget(package.Route),
                IsReady: package.IsReady));
        }

        foreach (var approval in cockpit.ApprovalHistory
            .OrderByDescending(static approval => approval.DecidedAtUtc ?? approval.SubmittedAtUtc ?? DateTimeOffset.MinValue))
        {
            PrivateCapitalCloseApprovals.Add(new FundPrivateCapitalCloseApprovalRow(
                ApprovalId: approval.ApprovalId,
                StatusLabel: approval.Status.ToString(),
                OperatorLabel: string.IsNullOrWhiteSpace(approval.Operator) ? "Operator not recorded" : approval.Operator!,
                ReviewerLabel: string.IsNullOrWhiteSpace(approval.Reviewer) ? "Reviewer not recorded" : approval.Reviewer!,
                Rationale: string.IsNullOrWhiteSpace(approval.Rationale) ? "No rationale recorded" : approval.Rationale!,
                DecidedAtLabel: FormatCloseTimestamp(approval.DecidedAtUtc ?? approval.SubmittedAtUtc),
                EvidenceLabel: FormatEvidenceCount(CountEvidenceLinks(approval)),
                SourceTarget: MapPrivateCapitalCloseRouteTarget(approval.WorkflowRoute)));
        }

        PrivateCapitalCloseStatusText = cockpit.IsReadyToClose
            ? "Ready to close"
            : FormatEvidenceStatusLabel(cockpit.OverallStatus);
        PrivateCapitalCloseSummaryText =
            $"{cockpit.ReadyLaneCount}/{cockpit.Lanes.Count} close lanes ready; {cockpit.BlockedLaneCount} blocked or missing; {cockpit.EvidencePackages.Count} evidence package(s); {cockpit.WorkflowCount} workflow(s), {cockpit.FundEventCount} fund event(s), {cockpit.CapitalAccountCount} capital account(s), {cockpit.ReportOutputCount} report output(s).";
        PrivateCapitalCloseEvidenceText = FormatEvidenceCount(CountCloseEvidenceLinks(cockpit));
        PrivateCapitalCloseBlockerText = BuildPrivateCapitalCloseBlockerText(cockpit);
        PrivateCapitalCloseReadinessState = BuildPrivateCapitalCloseReadinessState(cockpit);
        UpdateFinancialOperationsQueuePresentation();
    }

    private static WorkstationStateModel BuildPrivateCapitalCloseReadinessState(PrivateCapitalCloseCockpitDto cockpit)
    {
        var statusLabel = FormatEvidenceStatusLabel(cockpit.OverallStatus);
        var isReady = cockpit.IsReadyToClose;
        var kind = cockpit.OverallStatus switch
        {
            EvidenceStatusDto.Ready => WorkstationStateKind.Ready,
            EvidenceStatusDto.Stale => WorkstationStateKind.Stale,
            EvidenceStatusDto.Unknown => WorkstationStateKind.Empty,
            EvidenceStatusDto.Missing => WorkstationStateKind.Blocked,
            _ => WorkstationStateKind.Blocked
        };
        var readinessTone = cockpit.OverallStatus switch
        {
            EvidenceStatusDto.Ready => WorkstationReadinessTone.EvidenceLinked,
            EvidenceStatusDto.ReviewRequired => WorkstationReadinessTone.SignoffRequired,
            EvidenceStatusDto.Stale => WorkstationReadinessTone.Stale,
            EvidenceStatusDto.Blocked or EvidenceStatusDto.Missing => WorkstationReadinessTone.Blocked,
            _ => WorkstationReadinessTone.Neutral
        };
        var tone = cockpit.OverallStatus switch
        {
            EvidenceStatusDto.Ready => WorkspaceTone.Success,
            EvidenceStatusDto.ReviewRequired or EvidenceStatusDto.Stale => WorkspaceTone.Warning,
            EvidenceStatusDto.Blocked or EvidenceStatusDto.Missing => WorkspaceTone.Danger,
            _ => WorkspaceTone.Neutral
        };
        var evidenceCount = CountCloseEvidenceLinks(cockpit);
        var actionPosture = new WorkstationActionPostureModel(
            isReady ? "Review close package" : "Resolve close blockers",
            isReady
                ? "Inspect the retained close-package, NAV support, and approval evidence before final sign-off."
                : "Open Operations Close to resolve partner capital, NAV, approval, close-package, or period-lock blockers.",
            "OperationsClose",
            "Accounting operator",
            readinessTone,
            tone);
        var evidenceLinks = cockpit.Lanes
            .Where(static lane => CountEvidenceLinks(lane) > 0)
            .Select(lane => new WorkstationEvidenceLinkModel(
                lane.Label,
                MapPrivateCapitalCloseRouteTarget(lane.Route),
                lane.LaneId,
                lane.Summary))
            .Concat(cockpit.EvidencePackages
                .Where(static package => CountEvidenceLinks(package) > 0)
                .Select(package => new WorkstationEvidenceLinkModel(
                    package.Label,
                    MapPrivateCapitalCloseRouteTarget(package.RouteHint),
                    package.PackageId,
                    package.Summary)))
            .Concat(cockpit.NavSupportPackages
                .Where(static package => CountEvidenceLinks(package) > 0)
                .Select(package => new WorkstationEvidenceLinkModel(
                    package.Label,
                    MapPrivateCapitalCloseRouteTarget(package.Route),
                    package.PackageId,
                    package.Summary)))
            .Concat(cockpit.ApprovalHistory
                .Where(static approval => CountEvidenceLinks(approval) > 0)
                .Select(approval => new WorkstationEvidenceLinkModel(
                    "Approval history",
                    MapPrivateCapitalCloseRouteTarget(approval.WorkflowRoute),
                    approval.ApprovalId,
                    string.IsNullOrWhiteSpace(approval.Rationale) ? approval.Status.ToString() : approval.Rationale!)))
            .Take(6)
            .ToArray();
        var recoveryActions = cockpit.NextActions
            .Take(3)
            .Select(action => new WorkstationRecoveryActionModel(
                action.Label,
                string.IsNullOrWhiteSpace(action.Code) ? "Close action" : action.Code,
                MapPrivateCapitalCloseRouteTarget(action.RouteHint ?? action.Route)))
            .ToArray();
        if (recoveryActions.Length == 0)
        {
            recoveryActions = cockpit.Lanes
                .Where(static lane => !lane.IsReady)
                .SelectMany(static lane => lane.RequiredActions.Select(action => new WorkstationRecoveryActionModel(
                    string.IsNullOrWhiteSpace(action) ? $"Review {lane.Label}" : action,
                    lane.Summary,
                    MapPrivateCapitalCloseRouteTarget(lane.Route))))
                .Take(3)
                .ToArray();
        }

        if (recoveryActions.Length == 0)
        {
            recoveryActions =
            [
                new WorkstationRecoveryActionModel(
                    "Open close cockpit",
                    "Review the shared private-capital close cockpit before sign-off.",
                    "OperationsClose")
            ];
        }

        return new WorkstationStateModel(
            kind,
            isReady ? "Private-capital close ready" : $"Private-capital close {statusLabel.ToLowerInvariant()}",
            $"{cockpit.ReadyLaneCount}/{cockpit.Lanes.Count} close lanes ready; {cockpit.BlockedLaneCount} blocked or missing; {cockpit.EvidencePackages.Count} evidence package(s); {cockpit.NavSupportPackages.Count} NAV support package(s); {cockpit.ApprovalHistory.Count} approval event(s).",
            actionPosture.Label,
            actionPosture.Target,
            FormatEvidenceCount(evidenceCount),
            "\uE9D9",
            tone,
            readinessTone,
            actionPosture,
            evidenceLinks,
            recoveryActions,
            new WorkstationSignoffRequirementModel(
                "Accounting operator",
                isReady ? "Close evidence ready for sign-off" : "Close evidence requires review",
                "Sign-off should cite partner capital tie-outs, NAV support packages, approval history, close-package evidence, and period-lock readiness.",
                isReady ? WorkspaceTone.Success : WorkspaceTone.Warning));
    }

    private static string BuildPrivateCapitalCloseBlockerText(PrivateCapitalCloseCockpitDto cockpit)
    {
        var blocker = cockpit.Blockers.FirstOrDefault();
        if (blocker is not null)
        {
            return $"{blocker.Severity}: {blocker.Message}";
        }

        var nextAction = cockpit.NextActions.FirstOrDefault();
        if (nextAction is not null)
        {
            return $"Next action: {nextAction.Label}";
        }

        return cockpit.IsReadyToClose
            ? "No blockers reported by the shared close cockpit."
            : "No blockers were returned, but at least one close lane is not ready.";
    }

    private static bool IsBreakCaseClosed(OperationsBreakCaseDto breakCase)
    {
        var status = breakCase.Status.Trim();
        return status.Equals("Resolved", StringComparison.OrdinalIgnoreCase) ||
            status.Equals("Dismissed", StringComparison.OrdinalIgnoreCase) ||
            status.Equals("Matched", StringComparison.OrdinalIgnoreCase) ||
            status.Equals("SignedOff", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsBreakCaseBlocked(OperationsBreakCaseDto breakCase)
    {
        var status = breakCase.Status.Trim();
        var severity = breakCase.Severity.Trim();
        return status.Equals("Blocked", StringComparison.OrdinalIgnoreCase) ||
            status.Equals("AwaitingEvidence", StringComparison.OrdinalIgnoreCase) ||
            severity.Equals("Critical", StringComparison.OrdinalIgnoreCase) ||
            severity.Equals("Error", StringComparison.OrdinalIgnoreCase) ||
            breakCase.BlockedOutputs is { Count: > 0 };
    }

    private static string BuildBreakCaseTimingLabel(OperationsBreakCaseDto breakCase)
    {
        if (!string.IsNullOrWhiteSpace(breakCase.SlaState) && breakCase.SlaDueAtUtc.HasValue)
        {
            return $"SLA {FormatQueueStatusLabel(breakCase.SlaState!)} due {FormatQueueTimestamp(breakCase.SlaDueAtUtc)}";
        }

        if (!string.IsNullOrWhiteSpace(breakCase.SlaState))
        {
            return $"SLA {FormatQueueStatusLabel(breakCase.SlaState!)}";
        }

        return breakCase.DueDate.HasValue
            ? $"Due {FormatQueueDate(breakCase.DueDate)}"
            : "No due date";
    }

    private static bool IsChecklistTaskComplete(OperationsCloseChecklistTaskDto task)
    {
        var status = task.Status.Trim();
        return status.Equals("Done", StringComparison.OrdinalIgnoreCase) ||
            status.Equals("Complete", StringComparison.OrdinalIgnoreCase) ||
            status.Equals("Completed", StringComparison.OrdinalIgnoreCase) ||
            status.Equals("Acknowledged", StringComparison.OrdinalIgnoreCase) ||
            task.AcknowledgedAtUtc.HasValue;
    }

    private static bool IsChecklistTaskBlocked(OperationsCloseChecklistTaskDto task)
    {
        var status = task.Status.Trim();
        return status.Equals("Blocked", StringComparison.OrdinalIgnoreCase) ||
            status.Equals("Expired", StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrWhiteSpace(task.BlockingReason);
    }

    private static string BuildChecklistTimingLabel(OperationsCloseChecklistTaskDto task)
    {
        var due = task.DueDate.HasValue ? $"Due {FormatQueueDate(task.DueDate)}" : "No due date";
        var expires = task.ExpiresOn.HasValue ? $"expires {FormatQueueDate(task.ExpiresOn)}" : "no expiration";
        return $"{due}; {expires}";
    }

    private static string BuildApprovalOwnerLabel(string? operatorLabel, string? reviewerLabel)
    {
        if (!string.IsNullOrWhiteSpace(reviewerLabel))
        {
            return $"Reviewer {reviewerLabel}";
        }

        if (!string.IsNullOrWhiteSpace(operatorLabel))
        {
            return $"Operator {operatorLabel}";
        }

        return "Approval owner pending";
    }

    private static string BuildApprovalTimingLabel(DateTimeOffset? submittedAt, DateTimeOffset? decidedAt)
    {
        if (decidedAt.HasValue)
        {
            return $"Decided {FormatQueueTimestamp(decidedAt)}";
        }

        if (submittedAt.HasValue)
        {
            return $"Submitted {FormatQueueTimestamp(submittedAt)}";
        }

        return "Approval timing pending";
    }

    private static bool IsEvidenceStatusBlocked(EvidenceStatusDto status)
        => status is EvidenceStatusDto.Blocked or EvidenceStatusDto.Missing;

    private static string FormatQueueDate(DateOnly? value)
        => value.HasValue
            ? value.Value.ToString("MMM d, yyyy", System.Globalization.CultureInfo.InvariantCulture)
            : "No date";

    private static string FormatQueueTimestamp(DateTimeOffset? value)
        => value.HasValue
            ? value.Value.UtcDateTime.ToString("MMM d, HH:mm 'UTC'", System.Globalization.CultureInfo.InvariantCulture)
            : "No timestamp";

    private static string FormatQueueStatusLabel(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Status pending";
        }

        var text = value.Trim().Replace('_', ' ').Replace('-', ' ');
        var builder = new System.Text.StringBuilder(text.Length + 8);
        for (var index = 0; index < text.Length; index++)
        {
            var current = text[index];
            if (index > 0 &&
                char.IsUpper(current) &&
                !char.IsWhiteSpace(text[index - 1]) &&
                (char.IsLower(text[index - 1]) || index + 1 < text.Length && char.IsLower(text[index + 1])))
            {
                builder.Append(' ');
            }

            builder.Append(current);
        }

        return builder.ToString();
    }

    private static string FormatEvidenceStatusLabel(EvidenceStatusDto status)
        => status switch
        {
            EvidenceStatusDto.ReviewRequired => "Review Required",
            _ => status.ToString()
        };

    private static string FormatReconciliationLaneStatusLabel(OperationsReconciliationLaneStatusDto status)
        => status switch
        {
            OperationsReconciliationLaneStatusDto.ReviewRequired => "Review Required",
            _ => status.ToString()
        };

    private static string FormatEvidenceCount(int count)
        => $"{count:N0} evidence link{(count == 1 ? string.Empty : "s")}";

    private static string FormatBreakCount(int count)
        => $"{count:N0} open break{(count == 1 ? string.Empty : "s")}";

    private static int CountCloseEvidenceLinks(PrivateCapitalCloseCockpitDto cockpit)
        => cockpit.Lanes.Sum(static lane => CountEvidenceLinks(lane)) +
            cockpit.EvidencePackages.Sum(static package => CountEvidenceLinks(package)) +
            cockpit.NavSupportPackages.Sum(static package => CountEvidenceLinks(package)) +
            cockpit.ApprovalHistory.Sum(static approval => CountEvidenceLinks(approval));

    private static int CountEvidenceLinks(PrivateCapitalCloseCockpitLaneDto lane)
        => Math.Max(lane.EvidenceLinkCount, lane.EvidenceLinks.Count);

    private static int CountEvidenceLinks(PrivateCapitalNavSupportPackageDto package)
        => Math.Max(package.EvidenceLinkCount, package.EvidenceLinks.Count);

    private static int CountEvidenceLinks(OperationsEvidencePackageSummaryDto package)
        => Math.Max(package.EvidenceLinkCount, package.EvidenceLinks.Count);

    private static int CountEvidenceLinks(OperationsReconciliationLaneSummaryDto lane)
        => lane.EvidenceLinks.Count;

    private static int CountEvidenceLinks(PrivateCapitalCloseCockpitApprovalDto approval)
        => Math.Max(approval.EvidenceLinkCount, approval.EvidenceLinks.Count);

    private static string FormatRequiredActions(IReadOnlyList<string>? requiredActions)
    {
        var actions = (requiredActions ?? [])
            .Where(static action => !string.IsNullOrWhiteSpace(action))
            .ToArray();
        return actions.Length == 0
            ? "No required actions"
            : string.Join("; ", actions);
    }

    private static string FormatNavSupportComponentLabel(IReadOnlyList<PrivateCapitalNavSupportComponentDto> components)
    {
        var readyCount = components.Count(static component => component.IsReady);
        var reviewComponents = components
            .Where(static component => !component.IsReady)
            .Select(static component => string.IsNullOrWhiteSpace(component.Label) ? component.ComponentId : component.Label)
            .Where(static label => !string.IsNullOrWhiteSpace(label))
            .ToArray();

        return reviewComponents.Length == 0
            ? $"{readyCount}/{components.Count} components ready"
            : $"{readyCount}/{components.Count} components ready; review {string.Join(", ", reviewComponents)}";
    }

    private static string FormatEvidencePackageCategoryLabel(int completeCategoryCount, int requiredCategoryCount)
        => $"{completeCategoryCount:N0}/{requiredCategoryCount:N0} categories complete";

    private static string FormatShadowNavLabel(decimal? shadowNav, string? currency)
    {
        if (!shadowNav.HasValue)
        {
            return "-";
        }

        return string.IsNullOrWhiteSpace(currency)
            ? shadowNav.Value.ToString("N2")
            : $"{shadowNav.Value:N2} {currency}";
    }

    private static string FormatCloseTimestamp(DateTimeOffset? timestamp)
        => timestamp.HasValue ? timestamp.Value.LocalDateTime.ToString("g") : "Not decided";

    private static string MapPrivateCapitalCloseRouteTarget(string? route)
    {
        if (string.IsNullOrWhiteSpace(route))
        {
            return "OperationsClose";
        }

        var normalized = route.Trim();
        if (normalized.Contains("report", StringComparison.OrdinalIgnoreCase))
        {
            return "FundReportPack";
        }

        if (normalized.Contains("evidence", StringComparison.OrdinalIgnoreCase))
        {
            return "EvidenceWorkbench";
        }

        if (normalized.Contains("operations", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("close", StringComparison.OrdinalIgnoreCase))
        {
            return "OperationsClose";
        }

        return normalized;
    }

    private static string BuildAccountingRecordEvidenceText(OperationsAccountingRecordSummaryDto summary)
    {
        var readiness = summary.AuditPackReadiness;
        if (readiness is null)
        {
            return $"{summary.CompleteCategoryCount}/{summary.RequiredCategoryCount} evidence categories complete; 60 sec target not measured";
        }

        var readinessLabel = readiness.IsComplete
            ? "audit pack complete"
            : $"{readiness.MissingEvidenceCategories.Count} audit-pack categor{(readiness.MissingEvidenceCategories.Count == 1 ? "y" : "ies")} missing";
        return $"{summary.CompleteCategoryCount}/{summary.RequiredCategoryCount} evidence categories complete; {readinessLabel}; generated in {readiness.GeneratedInSeconds:F3}s, {readiness.SlaTargetSeconds}s target {(readiness.SlaMet ? "met" : "missed")}";
    }

    private static WorkstationStateModel BuildReviewedAutomationState(OperationsReviewedAutomationSummaryDto? reviewedAutomation)
    {
        if (reviewedAutomation is null)
        {
            return WorkstationStateModel.Empty(
                "Reviewed automation unavailable",
                "Operations Continuity has not returned reviewed automation posture for this fund context.",
                "Open Operations Continuity",
                "OperationsContinuity");
        }

        var requiresReview = reviewedAutomation.RequiresHumanReview ||
            reviewedAutomation.Status is EvidenceStatusDto.ReviewRequired or EvidenceStatusDto.Stale;
        var statusLabel = FormatEvidenceStatusLabel(reviewedAutomation.Status);
        var kind = reviewedAutomation.Status switch
        {
            EvidenceStatusDto.Ready when !reviewedAutomation.RequiresHumanReview => WorkstationStateKind.Ready,
            EvidenceStatusDto.Stale => WorkstationStateKind.Stale,
            EvidenceStatusDto.Unknown => WorkstationStateKind.Empty,
            EvidenceStatusDto.Blocked or EvidenceStatusDto.Missing => WorkstationStateKind.Blocked,
            _ => requiresReview ? WorkstationStateKind.Stale : WorkstationStateKind.Blocked
        };
        var readinessTone = reviewedAutomation.Status switch
        {
            EvidenceStatusDto.Ready when !reviewedAutomation.RequiresHumanReview => WorkstationReadinessTone.EvidenceLinked,
            EvidenceStatusDto.Stale => WorkstationReadinessTone.Stale,
            EvidenceStatusDto.Blocked or EvidenceStatusDto.Missing => WorkstationReadinessTone.Blocked,
            _ => requiresReview ? WorkstationReadinessTone.SignoffRequired : WorkstationReadinessTone.Neutral
        };
        var tone = reviewedAutomation.Status switch
        {
            EvidenceStatusDto.Ready when !reviewedAutomation.RequiresHumanReview => WorkspaceTone.Success,
            EvidenceStatusDto.Blocked or EvidenceStatusDto.Missing => WorkspaceTone.Danger,
            EvidenceStatusDto.ReviewRequired or EvidenceStatusDto.Stale => WorkspaceTone.Warning,
            _ => requiresReview ? WorkspaceTone.Warning : WorkspaceTone.Neutral
        };
        var actionPosture = new WorkstationActionPostureModel(
            requiresReview ? "Review automation" : "Review retained evidence",
            "Open Operations Continuity to inspect reviewed automation posture, retained evidence, and material-action guardrails.",
            "OperationsContinuity",
            "Accounting operator",
            readinessTone,
            tone);
        var evidenceLinks = reviewedAutomation.EvidenceLinks
            .Where(static link => !string.IsNullOrWhiteSpace(link.EvidenceId))
            .Select(link => new WorkstationEvidenceLinkModel(
                string.IsNullOrWhiteSpace(link.Label) ? link.EvidenceId : link.Label,
                MapReviewedAutomationRouteTarget(link.Route),
                link.EvidenceId,
                string.IsNullOrWhiteSpace(link.Source) ? "Reviewed automation evidence" : link.Source!))
            .Take(6)
            .ToArray();
        var recoveryActions = reviewedAutomation.RequiredActions
            .Where(static action => !string.IsNullOrWhiteSpace(action))
            .Select(static action => new WorkstationRecoveryActionModel(
                action,
                "Review generated commentary, audit requests, retained evidence, and guardrails before material Financial Operations action.",
                "OperationsContinuity"))
            .Take(3)
            .ToArray();

        if (requiresReview && recoveryActions.Length == 0)
        {
            recoveryActions =
            [
                new WorkstationRecoveryActionModel(
                    "Review automation evidence",
                    "Review generated commentary, audit requests, retained evidence, and guardrails before approval, posting, publication, payment release, or evidence deletion.",
                    "OperationsContinuity")
            ];
        }

        var stage = string.IsNullOrWhiteSpace(reviewedAutomation.Stage)
            ? "Reviewed automation"
            : reviewedAutomation.Stage;
        var summary = string.IsNullOrWhiteSpace(reviewedAutomation.Summary)
            ? "Shared reviewed automation posture returned without summary text."
            : reviewedAutomation.Summary;
        var allowed = FormatReviewedAutomationList(reviewedAutomation.AllowedUseCases, "No allowed use cases returned");
        var prohibited = FormatReviewedAutomationList(reviewedAutomation.ProhibitedActions, "No prohibited actions returned");
        var required = FormatRequiredActions(reviewedAutomation.RequiredActions);

        return new WorkstationStateModel(
            kind,
            requiresReview ? $"Reviewed automation {statusLabel.ToLowerInvariant()}" : "Reviewed automation retained",
            $"{stage}: {summary} Allowed: {allowed}. Prohibited: {prohibited}. Required: {required}.",
            actionPosture.Label,
            actionPosture.Target,
            evidenceLinks.Length == 0 ? "No retained review evidence" : $"{evidenceLinks.Length} retained review evidence link{(evidenceLinks.Length == 1 ? string.Empty : "s")}",
            "\uE9D9",
            tone,
            readinessTone,
            actionPosture,
            evidenceLinks,
            recoveryActions,
            new WorkstationSignoffRequirementModel(
                "Accounting operator",
                requiresReview ? "Human review required" : "Human review retained",
                "Automation remains advisory; material Financial Operations actions require human operator origin.",
                requiresReview ? WorkspaceTone.Warning : WorkspaceTone.Success));
    }

    private static string FormatReviewedAutomationList(IReadOnlyList<string>? values, string fallback)
    {
        var items = (values ?? [])
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .ToArray();

        if (items.Length == 0)
        {
            return fallback;
        }

        const int visibleLimit = 5;
        if (items.Length <= visibleLimit)
        {
            return string.Join(", ", items);
        }

        return $"{string.Join(", ", items.Take(visibleLimit))}, +{items.Length - visibleLimit} more";
    }

    private static string MapReviewedAutomationRouteTarget(string? route)
    {
        if (string.IsNullOrWhiteSpace(route))
        {
            return "OperationsContinuity";
        }

        return route.Contains("evidence", StringComparison.OrdinalIgnoreCase)
            ? "EvidenceWorkbench"
            : MapAccountingRecordRouteTarget(route);
    }

    private static WorkstationStateModel BuildAccountingRecordReadinessState(OperationsAccountingRecordSummaryDto summary)
    {
        var evidenceCount = summary.EvidenceLinks.Count +
            summary.EvidenceCategories.Sum(static category => category.EvidenceLinks.Count);
        var actionPosture = new WorkstationActionPostureModel(
            summary.IsAuditReady ? "Review evidence" : "Resolve evidence gaps",
            "Open the shared Operations Continuity workbench to inspect account-period source, ledger, reconciliation, approval, and report-pack evidence.",
            "OperationsContinuity",
            "Accounting operator",
            summary.IsAuditReady ? WorkstationReadinessTone.EvidenceLinked : WorkstationReadinessTone.SignoffRequired,
            summary.IsAuditReady ? WorkspaceTone.Success : WorkspaceTone.Warning);
        var evidenceLinks = summary.EvidenceCategories
            .Where(static category => category.EvidenceLinks.Count > 0)
            .Select(category => new WorkstationEvidenceLinkModel(
                string.IsNullOrWhiteSpace(category.Label) ? category.Key : category.Label,
                MapAccountingRecordRouteTarget(category.RouteHint),
                BuildAccountingRecordEvidenceSubject(summary.RecordId),
                $"{category.EvidenceLinks.Count} retained evidence link{(category.EvidenceLinks.Count == 1 ? string.Empty : "s")}"))
            .ToArray();

        return new WorkstationStateModel(
            summary.IsAuditReady ? WorkstationStateKind.Ready : WorkstationStateKind.Blocked,
            summary.IsAuditReady ? "Accounting record audit ready" : "Accounting record review required",
            string.IsNullOrWhiteSpace(summary.Summary)
                ? $"{summary.CompleteCategoryCount}/{summary.RequiredCategoryCount} required accounting-record evidence categories are complete."
                : summary.Summary,
            actionPosture.Label,
            actionPosture.Target,
            evidenceCount == 0 ? "No retained evidence links" : $"{evidenceCount} retained evidence link{(evidenceCount == 1 ? string.Empty : "s")}",
            "\uE9D9",
            summary.IsAuditReady ? WorkspaceTone.Success : WorkspaceTone.Warning,
            summary.IsAuditReady ? WorkstationReadinessTone.EvidenceLinked : WorkstationReadinessTone.SignoffRequired,
            actionPosture,
            evidenceLinks,
            RecoveryActions:
            [
                new WorkstationRecoveryActionModel(
                    "Review accounting record",
                    "Use Operations Continuity to resolve incomplete source, normalization, reconciliation, ledger, approval, or report-pack evidence.",
                    "OperationsContinuity")
            ],
            SignoffRequirement: new WorkstationSignoffRequirementModel(
                "Accounting operator",
                summary.IsAuditReady ? "Evidence ready for sign-off" : "Evidence categories require review",
                "Sign-off should reference retained source data, normalized activity, reconciliation history, ledger evidence, approvals, and report-pack lineage."));
    }

    private static string MapAccountingRecordRouteTarget(string? routeHint)
    {
        if (string.IsNullOrWhiteSpace(routeHint))
        {
            return "OperationsContinuity";
        }

        var normalized = routeHint.Trim();
        if (normalized.Contains("report", StringComparison.OrdinalIgnoreCase))
        {
            return "FundReportPack";
        }

        if (normalized.Contains("ledger", StringComparison.OrdinalIgnoreCase))
        {
            return "FundLedger";
        }

        if (normalized.Contains("reconciliation", StringComparison.OrdinalIgnoreCase))
        {
            return "FundReconciliation";
        }

        if (normalized.Contains("approval", StringComparison.OrdinalIgnoreCase))
        {
            return "AccountingApprovals";
        }

        if (normalized.Contains("data", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("provider", StringComparison.OrdinalIgnoreCase))
        {
            return "DataShell";
        }

        return "OperationsContinuity";
    }

    private static string BuildAccountingRecordEvidenceSubject(string? recordId)
    {
        var id = string.IsNullOrWhiteSpace(recordId) ? "current" : recordId.Trim();
        return $"accounting-record/{id}";
    }

    private static string BuildAccountingRecordEvidenceSubjectTarget(string? recordId)
        => $"EvidenceWorkbench:{BuildAccountingRecordEvidenceSubject(recordId)}";

    private static string BuildOperationsEvidencePackageSubject(string? packageId)
    {
        var id = string.IsNullOrWhiteSpace(packageId) ? "current" : packageId.Trim();
        return $"operations-evidence-package/{id}";
    }

    private static string BuildOperationsEvidencePackageSubjectTarget(string? packageId)
        => $"EvidenceWorkbench:{BuildOperationsEvidencePackageSubject(packageId)}";

    private static string BuildOperationsReconciliationLaneSubject(string? laneId)
    {
        var id = string.IsNullOrWhiteSpace(laneId) ? "current" : laneId.Trim();
        return $"operations-reconciliation-lane/{id}";
    }

    private static string BuildOperationsReconciliationLaneSubjectTarget(string? laneId)
        => $"EvidenceWorkbench:{BuildOperationsReconciliationLaneSubject(laneId)}";

    private WorkstationStateModel BuildReportPackReadinessState(
        string reportOwner,
        string? readiness,
        string? traceability)
    {
        if (_reportPackPreview is null)
        {
            var action = new WorkstationActionPostureModel(
                "Refresh Preview",
                "Build the report-pack preview from the current fund, reconciliation, and ledger state.",
                "FundReportPack",
                reportOwner,
                WorkstationReadinessTone.Recovery,
                WorkspaceTone.Warning);

            return WorkstationStateModel.Recovery(
                "Report pack preview needed",
                ReportPackStatusText,
                action,
                recoveryActions:
                [
                    new WorkstationRecoveryActionModel(
                        "Refresh report pack",
                        "Refresh preview after accounting, reconciliation, and audit evidence is loaded.",
                        "FundReportPack")
                ],
                signoffRequirement: new WorkstationSignoffRequirementModel(
                    reportOwner,
                    "Pending preview",
                    "Final sign-off is blocked until a current preview is available."));
        }

        var evidenceLinks = new[]
        {
            new WorkstationEvidenceLinkModel(
                "Report-pack preview",
                "FundReportPack",
                _reportPackPreview.ReportKind.ToString(),
                $"Generated {ReportPackGeneratedAtText}"),
            new WorkstationEvidenceLinkModel(
                "Audit traceability",
                "FundAuditTrail",
                "approval lifecycle",
                string.IsNullOrWhiteSpace(traceability) ? "Traceability not reported by the shared lifecycle." : traceability)
        };

        var detail = string.IsNullOrWhiteSpace(readiness)
            ? ReportPackStatusText
            : $"{ReportPackStatusText} {readiness}";
        var actionPosture = new WorkstationActionPostureModel(
            "Review handoff",
            "Confirm preview freshness, ledger evidence, reconciliation state, and report owner before distribution.",
            "FundReportPack",
            reportOwner,
            WorkstationReadinessTone.EvidenceLinked,
            WorkspaceTone.Info);

        return new WorkstationStateModel(
            WorkstationStateKind.Ready,
            "Report pack evidence linked",
            detail,
            actionPosture.Label,
            actionPosture.Target,
            $"Generated {ReportPackGeneratedAtText}",
            "\uE8A5",
            WorkspaceTone.Success,
            WorkstationReadinessTone.EvidenceLinked,
            actionPosture,
            evidenceLinks,
            RecoveryActions:
            [
                new WorkstationRecoveryActionModel(
                    "Refresh before distribution",
                    "Refresh the preview after any ledger, reconciliation, or approval change.",
                    "FundReportPack")
            ],
            SignoffRequirement: new WorkstationSignoffRequirementModel(
                reportOwner,
                string.IsNullOrWhiteSpace(readiness) ? "Pending operator sign-off" : readiness,
                "Sign-off should reference the current preview and audit traceability."));
    }

    private static (string Mode, string Title, string Subtitle) DescribeWorkbench(FundOperationsTab tab) => tab switch
    {
        FundOperationsTab.Overview => ("Overview Mode", "Overview Workbench", "Fund-wide operating summary, liquidity posture, and exception pressure."),
        FundOperationsTab.Reconciliation => ("Reconciliation Mode", "Reconciliation Workbench", "Exception queue, operator ownership, stale-snapshot checks, and break resolution."),
        FundOperationsTab.ReportPack => ("Reporting Mode", "Report Pack Workbench", "Reporting preview, handoff readiness, and sign-off posture for accounting artifacts."),
        FundOperationsTab.AuditTrail => ("Accounting Mode", "Audit Trail Workbench", "Recent journal and reconciliation evidence for operator review and traceability."),
        FundOperationsTab.Portfolio => ("Accounting Mode", "Portfolio Accounting Workbench", "Fund-scoped positions, security coverage, and exposure review inside Accounting."),
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
            "This navigation landed inside the shared Accounting workbench.")
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
