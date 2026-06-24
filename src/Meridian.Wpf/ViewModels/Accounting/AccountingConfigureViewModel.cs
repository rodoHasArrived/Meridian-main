using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CommunityToolkit.Mvvm.Input;
using Meridian.Contracts.AccountingSystem;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.AccountingSystem;
using Meridian.FinancialOperations.Ledger;
using Meridian.Ui.Shared.Services;
using Meridian.Wpf.Models;
using Meridian.Wpf.Services;

namespace Meridian.Wpf.ViewModels.Accounting;

public sealed class AccountingConfigureViewModel : Meridian.Wpf.ViewModels.BindableBase
{
    private const string DefaultActor = "desktop-user";
    private const string DefaultLifecycleActor = "desktop-controller";

    private static readonly ManualJournalEntryTypePreset[] ManualJournalEntryTypePresets =
    [
        new(
            ManualJournalEntryTypeDto.General,
            "General adjustment",
            "Balanced manual accounting adjustment.",
            "Manual accounting adjustment",
            "Assets:Cash",
            "Income:Investment Income",
            "evidence://manual-je/source-document"),
        new(
            ManualJournalEntryTypeDto.AccruedBalance,
            "Accrued balance",
            "Recognize an accrued balance with retained controller support.",
            "Accrued balance adjustment",
            "Expenses:Operating Expenses",
            "Liabilities:Accrued Expenses",
            "evidence://manual-je/accrual-support"),
        new(
            ManualJournalEntryTypeDto.AccruedExpense,
            "Accrued expense",
            "Record accrued expenses before invoice settlement.",
            "Accrued expense entry",
            "Expenses:Operating Expenses",
            "Liabilities:Accrued Expenses",
            "evidence://manual-je/accrued-expense-support"),
        new(
            ManualJournalEntryTypeDto.PrepaidExpense,
            "Prepaid expense",
            "Capitalize a prepaid expense before later recognition.",
            "Prepaid expense entry",
            "Assets:Prepaid Expenses",
            "Assets:Cash",
            "evidence://manual-je/prepaid-expense-support"),
        new(
            ManualJournalEntryTypeDto.Expense,
            "Expense",
            "Recognize an operating expense from retained support.",
            "Expense recognition entry",
            "Expenses:Operating Expenses",
            "Assets:Cash",
            "evidence://manual-je/expense-support"),
        new(
            ManualJournalEntryTypeDto.Amortization,
            "Amortization",
            "Recognize amortization against accumulated amortization.",
            "Amortization entry",
            "Expenses:Amortization Expense",
            "Assets:Accumulated Amortization",
            "evidence://manual-je/amortization-schedule"),
        new(
            ManualJournalEntryTypeDto.Deferral,
            "Deferral",
            "Record deferred revenue or similar deferred balances.",
            "Deferral entry",
            "Assets:Cash",
            "Liabilities:Deferred Revenue",
            "evidence://manual-je/deferral-schedule"),
        new(
            ManualJournalEntryTypeDto.Reclassification,
            "Reclassification",
            "Move an amount between configured accounts without changing total debits.",
            "Account reclassification entry",
            "Expenses:Operating Expenses",
            "Expenses:Investment Fees",
            "evidence://manual-je/reclassification-support"),
        new(
            ManualJournalEntryTypeDto.Reversal,
            "Reversal",
            "Reverse a prior manual or operational accounting entry.",
            "Reversal entry",
            "Income:Investment Income",
            "Assets:Cash",
            "evidence://manual-je/reversal-approval"),
        new(
            ManualJournalEntryTypeDto.CapitalCall,
            "Capital call",
            "Record a capital call against retained LP notice and cash evidence.",
            "Capital call entry",
            "Assets:Cash",
            "Equity:Capital Contributions",
            "evidence://manual-je/capital-call-notice"),
        new(
            ManualJournalEntryTypeDto.Distribution,
            "Distribution",
            "Record an LP distribution with capital account and payment linkage.",
            "Distribution entry",
            "Equity:Distributions",
            "Assets:Cash",
            "evidence://manual-je/distribution-notice"),
        new(
            ManualJournalEntryTypeDto.Subscription,
            "Subscription",
            "Record a subscription receivable and capital contribution allocation.",
            "Subscription entry",
            "Assets:Subscription Receivable",
            "Equity:Capital Contributions",
            "evidence://manual-je/subscription-agreement"),
        new(
            ManualJournalEntryTypeDto.Redemption,
            "Redemption",
            "Record investor redemption activity with approval evidence.",
            "Redemption entry",
            "Equity:Redemptions",
            "Assets:Cash",
            "evidence://manual-je/redemption-approval"),
        new(
            ManualJournalEntryTypeDto.LpTransfer,
            "LP transfer",
            "Move capital account ownership between investor allocations.",
            "LP transfer entry",
            "Equity:LP Transfer Out",
            "Equity:LP Transfer In",
            "evidence://manual-je/lp-transfer-agreement"),
        new(
            ManualJournalEntryTypeDto.ManagementFee,
            "Management fee",
            "Recognize management fee expense from retained fee calculation support.",
            "Management fee entry",
            "Expenses:Management Fees",
            "Assets:Cash",
            "evidence://manual-je/management-fee-calculation")
    ];

    private readonly FundContextService _fundContextService;
    private readonly IAccountingConfigurationService _configurationService;
    private readonly IManualJournalEntryWorkbenchService _manualJournalEntryWorkbenchService;
    private readonly ICapitalAccountWorkbenchService? _capitalAccountWorkbenchService;
    private readonly IAccountingConfigurationStore? _configurationStore;
    private readonly IManualJournalEntryDraftStore? _manualJournalEntryDraftStore;
    private readonly AccountingSystemIntegrationService? _accountingSystemIntegrationService;
    private readonly FundOperationsWorkspaceReadService? _fundOperationsWorkspaceReadService;
    private readonly IAccountingPolicyService? _accountingPolicyService;
    private readonly IAccountingPostingCandidateService? _accountingPostingCandidateService;
    private readonly IManualJournalEntryLifecycleService? _manualJournalEntryLifecycleService;
    private readonly ILedgerBookService? _ledgerBookService;
    private readonly AccountingProductionReadinessService? _accountingProductionReadinessService;
    private readonly IAccountingProductionCertificationProfileStore? _productionCertificationProfileStore;
    private readonly IAccountingTenantAdministrationProfileStore? _tenantAdministrationProfileStore;
    private readonly IAccountingMigrationRunWorkerPlanStore? _migrationRunWorkerPlanStore;

    private AccountingConfigurationWorkspaceDto? _configuration;
    private ManualJournalEntryDraftDto? _selectedDraft;
    private LedgerPeriodDto? _selectedManualJournalPeriod;
    private FundProfileDetail? _activeFundProfile;
    private bool _isLoading;
    private string _statusText = "Select a fund-linked context to load accounting configuration.";
    private string _activeFundText = "No fund selected";
    private string _storagePostureText = "Storage has not been inspected.";
    private string _configurationStatusText = "Not loaded";
    private string _configurationDetailText = "Configuration readiness loads from shared accounting DTOs.";
    private string _rulesStudioStatusText = "Rules Studio has not loaded.";
    private string _rulesStudioDetailText = "Rule versions, tests, generated postings, and promotion approvals load from shared accounting configuration.";
    private string _rulesStudioActionText = "Rules Studio actions have not run.";
    private string _manualJournalStatusText = "Manual journal workbench has not loaded.";
    private string _capitalAccountWorkbenchStatusText = "Capital Account Workbench has not loaded.";
    private string _externalGlStatusText = "External GL evidence has not loaded.";
    private string _postingPostureText = "Posting/export posture has not loaded.";
    private string _chartAccountSetupStatusText = "Chart account setup has not been saved.";
    private string _chartAccountNodeId = "assets-cash-operating";
    private string _chartAccountPath = "Assets:Cash:Operating";
    private string _chartAccountName = "Operating Cash";
    private string _chartAccountType = "Asset";
    private string _chartAccountParentPath = "Assets:Cash";
    private string _chartAccountFinancialAccountId = string.Empty;
    private string _chartAccountEvidenceText = "wpf://accounting/configure/chart-account/setup";
    private string _postingRuleDraftStatusText = "Posting-rule draft has not been saved.";
    private string _postingRuleDraftRuleId = "wpf-configured-posting-rule-v1";
    private string _postingRuleDraftDisplayName = "WPF configured posting rule";
    private string _postingRuleDraftSourceEventType = "ManualJournalEntry.Configured";
    private string _postingRuleDraftTemplateId = "desktop-manual-adjustment-v1";
    private string _postingRuleDraftDescription = "Desktop-authored posting rule retained through Accounting Configure.";
    private int _postingRuleDraftPriority = 10;
    private string _postingRuleDraftEvidenceText = "evidence://wpf/accounting/rules/configured-posting-rule";
    private string _externalGlMappingProfileStatusText = "External GL mapping profile has not been saved.";
    private string _externalGlMappingProviderId = "quickbooks-fixture";
    private string _externalGlMappingProfileId = "quickbooks-fixture-ledger-book-mapping";
    private string _externalGlMappingDisplayName = "External GL mapping";
    private string _externalGlMappingAccountMappingsText = "Assets:Cash:Operating=qbo-1000\nIncome:Investment Income=qbo-4000\nExpenses:Operating Expenses=qbo-6000";
    private string _externalGlMappingMeridianDimensionsText = string.Empty;
    private string _externalGlMappingExternalDimensionsText = string.Empty;
    private string _externalGlMappingEvidenceText = "approval:external-gl-mapping:quickbooks-fixture-ledger-book-mapping";
    private bool _externalGlMappingProfileCertified = true;
    private string _closeReadinessText = "Close readiness has not loaded.";
    private string _closeDetailText = "Close posture is projected from shared operations continuity state.";
    private string _evidencePostureText = "Evidence posture has not loaded.";
    private string _reconciliationPostureText = "Reconciliation posture has not loaded.";
    private string _policyPostureText = "Accounting policy posture has not loaded.";
    private string _postingCandidateStatusText = "Posting-rule journal candidate has not been built.";
    private string _postingCandidateDetailText = "Build a governed draft candidate from the selected posting rule without posting to the ledger.";
    private string _manualJournalLifecycleStatusText = "Manual journal lifecycle actions have not run.";
    private string _ledgerBookSetupStatusText = "Ledger-book setup action has not run.";
    private string _ledgerBookAdministrationText = "Ledger-book administration has not loaded.";
    private string _productionReadinessStatusText = "Production readiness has not loaded.";
    private string _productionReadinessDetailText = "Shared accounting rollout posture has not been assessed.";
    private string _productionReadinessScoreText = "No score";
    private string _productionReadinessLedgerBookText = "Ledger-book rollout readiness has not loaded.";
    private string _productionReadinessExternalGlText = "External GL readiness has not loaded.";
    private string _productionReadinessDimensionalReportingText = "Dimensional reporting readiness has not loaded.";
    private string _productionReadinessTenantAdminText = "Tenant administration readiness has not loaded.";
    private string _productionCertificationProfileStatusText = "Production certification profile has not loaded.";
    private string _productionCertificationProfileScopeText = "Fund and ledger-book scope have not loaded.";
    private string _productionCertificationProfileUpdatedText = "No retained production certification profile loaded.";
    private string _productionCertificationEvidenceText = string.Empty;
    private bool _postingRulesLedgerBookNativeCertified;
    private bool _journalLifecycleLedgerBookNativeCertified;
    private bool _closeReportingLedgerBookNativeCertified;
    private bool _closePlanConfigurationLedgerBookNativeCertified;
    private bool _externalGlLedgerBookNativeCertified;
    private bool _reconciliationLedgerBookNativeCertified;
    private bool _directLendingLedgerBookNativeCertified;
    private bool _strategyLedgerReadLedgerBookNativeCertified;
    private bool _periodReportDimensionQueriesCertified;
    private bool _crossPeriodReportDimensionQueriesCertified;
    private bool _journalQueryDimensionFiltersCertified;
    private bool _externalExportDimensionMappingCertified;
    private bool _ledgerLineDimensionsPersistedCertified;
    private bool _trialBalanceDimensionFiltersCertified;
    private bool _reportPackageDimensionProvenanceCertified;
    private string _tenantAdministrationProfileStatusText = "Tenant administration setup profile has not loaded.";
    private string _tenantAdministrationProfileScopeText = "Tenant and company scope have not loaded.";
    private string _tenantAdministrationProfileUpdatedText = "No retained tenant administration setup profile loaded.";
    private string _implementationSandboxProofStatusText = "Implementation sandbox proof has not been retained.";
    private string _tenantAdministrationEvidenceText = string.Empty;
    private bool _tenantScopeConfigured;
    private bool _adminRoleProfileConfigured;
    private bool _scopedAccessPoliciesConfigured;
    private bool _reportingGroupsConfigured;
    private bool _accountingAdminSurfaceConfigured;
    private bool _wpfAccountingAdminSurfaceConfigured;
    private bool _chartAdministrationStudioConfigured;
    private bool _ruleTestPromotionStudioConfigured;
    private bool _closeSetupStudioConfigured;
    private bool _providerMappingStudioConfigured;
    private bool _tenantCompanyReportGroupSetupStudioConfigured;
    private bool _auditReviewToolingConfigured;
    private bool _bulkImportExportSafeguardsConfigured;
    private bool _performanceValidationConfigured;
    private bool _disasterRecoveryRunbookConfigured;
    private bool _ledgerBookAdministrationStudioConfigured;
    private bool _postingRuleAuthoringStudioConfigured;
    private bool _approvalQueueStudioConfigured;
    private string _approvalQueueId = "accounting-configuration-approval";
    private string _approvalQueueDisplayName = "Accounting configuration approval";
    private string _approvalQueueWorkflowKind = "ConfigurationPromotion";
    private string _approvalQueueRequiredApprovalRole = "Controller";
    private int _approvalQueueRequiredApprovalCount = 2;
    private string _approvalQueueSegregationPolicy = "Preparer cannot approve own configuration change.";
    private string _approvalQueueEvidenceRequirement = "approval-queue;configuration-approval;segregation-review";
    private bool _dimensionMappingStudioConfigured;
    private string _dimensionMappingId = "canonical-provider-dimension-map";
    private string _dimensionMappingDisplayName = "Canonical provider dimension map";
    private string _dimensionMappingProviderId = "quickbooks-fixture";
    private string _dimensionMappingMeridianDimensionsText = string.Empty;
    private string _dimensionMappingProviderDimensionsText = string.Empty;
    private string _dimensionMappingEvidenceRequirement = "dimension-mapping;external-dimension-mapping;gl-dimension-mapping";
    private bool _implementationSandboxConfigured;
    private string _selectedReconciliationView = "Open breaks";
    private string _batchReconciliationActionText = "Select a reconciliation view to prepare batch review.";
    private string _draftMemo = "Manual accounting adjustment";
    private string _draftCurrency = "USD";
    private decimal _draftAmount = 100m;
    private string _draftDebitAccountPath = "Assets:Cash";
    private string _draftCreditAccountPath = "Income:Investment Income";
    private string _draftEvidenceLink = "evidence://manual-je/source-document";
    private ManualJournalEntryTypeDto _selectedEntryType = ManualJournalEntryTypeDto.General;
    private AccountingBasisKindDto _selectedAccountingBasis = AccountingBasisKindDto.Primary;

    public AccountingConfigureViewModel(
        FundContextService fundContextService,
        IAccountingConfigurationService configurationService,
        IManualJournalEntryWorkbenchService manualJournalEntryWorkbenchService,
        IAccountingConfigurationStore? configurationStore = null,
        IManualJournalEntryDraftStore? manualJournalEntryDraftStore = null,
        AccountingSystemIntegrationService? accountingSystemIntegrationService = null,
        FundOperationsWorkspaceReadService? fundOperationsWorkspaceReadService = null,
        IAccountingPolicyService? accountingPolicyService = null,
        ICapitalAccountWorkbenchService? capitalAccountWorkbenchService = null,
        IAccountingPostingCandidateService? accountingPostingCandidateService = null,
        IManualJournalEntryLifecycleService? manualJournalEntryLifecycleService = null,
        ILedgerBookService? ledgerBookService = null,
        AccountingProductionReadinessService? accountingProductionReadinessService = null,
        IAccountingProductionCertificationProfileStore? productionCertificationProfileStore = null,
        IAccountingTenantAdministrationProfileStore? tenantAdministrationProfileStore = null,
        IAccountingMigrationRunWorkerPlanStore? migrationRunWorkerPlanStore = null)
    {
        _fundContextService = fundContextService ?? throw new ArgumentNullException(nameof(fundContextService));
        _configurationService = configurationService ?? throw new ArgumentNullException(nameof(configurationService));
        _manualJournalEntryWorkbenchService = manualJournalEntryWorkbenchService ?? throw new ArgumentNullException(nameof(manualJournalEntryWorkbenchService));
        _capitalAccountWorkbenchService = capitalAccountWorkbenchService;
        _configurationStore = configurationStore;
        _manualJournalEntryDraftStore = manualJournalEntryDraftStore;
        _accountingSystemIntegrationService = accountingSystemIntegrationService;
        _fundOperationsWorkspaceReadService = fundOperationsWorkspaceReadService;
        _accountingPolicyService = accountingPolicyService;
        _accountingPostingCandidateService = accountingPostingCandidateService;
        _manualJournalEntryLifecycleService = manualJournalEntryLifecycleService
            ?? manualJournalEntryWorkbenchService as IManualJournalEntryLifecycleService;
        _ledgerBookService = ledgerBookService;
        _accountingProductionReadinessService = accountingProductionReadinessService;
        _productionCertificationProfileStore = productionCertificationProfileStore;
        _tenantAdministrationProfileStore = tenantAdministrationProfileStore;
        _migrationRunWorkerPlanStore = migrationRunWorkerPlanStore;

        RefreshCommand = new AsyncRelayCommand(() => RefreshAsync());
        SeedBaselineConfigurationCommand = new AsyncRelayCommand(() => SeedBaselineConfigurationAsync());
        ActivateConfigurationCommand = new AsyncRelayCommand(() => ActivateConfigurationAsync());
        SaveManualJournalDraftCommand = new AsyncRelayCommand(() => SaveManualJournalDraftAsync());
        ValidateManualJournalDraftCommand = new AsyncRelayCommand(() => ValidateManualJournalDraftAsync());
        SubmitManualJournalDraftCommand = new AsyncRelayCommand(() => SubmitManualJournalDraftAsync());
        RefreshExternalGlCommand = new AsyncRelayCommand(() => RefreshExternalGlAsync());
        SaveChartAccountCommand = new AsyncRelayCommand(
            () => SaveChartAccountAsync(),
            () => CanSaveChartAccount);
        SavePostingRuleDraftCommand = new AsyncRelayCommand(
            () => SavePostingRuleDraftAsync(),
            () => CanSavePostingRuleDraft);
        SaveExternalGlMappingProfileCommand = new AsyncRelayCommand(
            () => SaveExternalGlMappingProfileAsync(),
            () => CanSaveExternalGlMappingProfile);
        CreateFundScopedPolicyCommand = new AsyncRelayCommand(() => CreateFundScopedPolicyAsync());
        BuildPostingCandidateCommand = new AsyncRelayCommand(() => BuildPostingCandidateAsync());
        ExecuteRulesStudioTestsCommand = new AsyncRelayCommand(() => ExecuteRulesStudioTestsAsync());
        ApproveRulesStudioPromotionCommand = new AsyncRelayCommand(() => ApproveRulesStudioPromotionAsync());
        CreateLedgerBookSetupCommand = new AsyncRelayCommand(() => CreateLedgerBookSetupAsync());
        SaveProductionCertificationProfileCommand = new AsyncRelayCommand(
            () => SaveProductionCertificationProfileAsync(),
            () => CanSaveProductionCertificationProfile);
        SaveTenantAdministrationProfileCommand = new AsyncRelayCommand(
            () => SaveTenantAdministrationProfileAsync(),
            () => CanSaveTenantAdministrationProfile);
        RetainImplementationSandboxProofCommand = new AsyncRelayCommand(
            () => RetainImplementationSandboxProofAsync(),
            () => CanRetainImplementationSandboxProof);
        ApproveManualJournalCommand = new AsyncRelayCommand(() => ApplyManualJournalLifecycleActionAsync(JournalEntryLifecycleActionDto.Approve));
        PostManualJournalCommand = new AsyncRelayCommand(() => ApplyManualJournalLifecycleActionAsync(JournalEntryLifecycleActionDto.Post));
        ReverseManualJournalCommand = new AsyncRelayCommand(() => ApplyManualJournalLifecycleActionAsync(JournalEntryLifecycleActionDto.Reverse));
        RebookManualJournalCommand = new AsyncRelayCommand(() => ApplyManualJournalLifecycleActionAsync(JournalEntryLifecycleActionDto.Rebook));
        LockManualJournalAfterCloseCommand = new AsyncRelayCommand(() => ApplyManualJournalLifecycleActionAsync(JournalEntryLifecycleActionDto.LockAfterClose));

        AccountingBasisOptions = new ObservableCollection<AccountingBasisKindDto>(
            Enum.GetValues<AccountingBasisKindDto>());
        ManualJournalEntryTypeOptions = new ObservableCollection<ManualJournalEntryTypeDto>(
            ManualJournalEntryTypePresets.Select(preset => preset.EntryType));
        ManualJournalEntryTypeRows.ReplaceWith(ManualJournalEntryTypePresets.Select(preset =>
            new AccountingWorkbenchRow(
                preset.Label,
                preset.EntryType.ToString(),
                $"{preset.DebitAccountPath} -> {preset.CreditAccountPath}",
                preset.EvidenceLink,
                preset.EntryType.ToString())));
    }

    public IAsyncRelayCommand RefreshCommand { get; }
    public IAsyncRelayCommand SeedBaselineConfigurationCommand { get; }
    public IAsyncRelayCommand ActivateConfigurationCommand { get; }
    public IAsyncRelayCommand SaveManualJournalDraftCommand { get; }
    public IAsyncRelayCommand ValidateManualJournalDraftCommand { get; }
    public IAsyncRelayCommand SubmitManualJournalDraftCommand { get; }
    public IAsyncRelayCommand RefreshExternalGlCommand { get; }
    public IAsyncRelayCommand SaveChartAccountCommand { get; }
    public IAsyncRelayCommand SavePostingRuleDraftCommand { get; }
    public IAsyncRelayCommand SaveExternalGlMappingProfileCommand { get; }
    public IAsyncRelayCommand CreateFundScopedPolicyCommand { get; }
    public IAsyncRelayCommand BuildPostingCandidateCommand { get; }
    public IAsyncRelayCommand ExecuteRulesStudioTestsCommand { get; }
    public IAsyncRelayCommand ApproveRulesStudioPromotionCommand { get; }
    public IAsyncRelayCommand CreateLedgerBookSetupCommand { get; }
    public IAsyncRelayCommand SaveProductionCertificationProfileCommand { get; }
    public IAsyncRelayCommand SaveTenantAdministrationProfileCommand { get; }
    public IAsyncRelayCommand RetainImplementationSandboxProofCommand { get; }
    public IAsyncRelayCommand ApproveManualJournalCommand { get; }
    public IAsyncRelayCommand PostManualJournalCommand { get; }
    public IAsyncRelayCommand ReverseManualJournalCommand { get; }
    public IAsyncRelayCommand RebookManualJournalCommand { get; }
    public IAsyncRelayCommand LockManualJournalAfterCloseCommand { get; }

    public ObservableCollection<AccountingWorkbenchRow> ValidationRows { get; } = [];
    public ObservableCollection<AccountingWorkbenchRow> ChartRows { get; } = [];
    public ObservableCollection<AccountingWorkbenchRow> TemplateRows { get; } = [];
    public ObservableCollection<AccountingWorkbenchRow> PostingRuleRows { get; } = [];
    public ObservableCollection<AccountingWorkbenchRow> RulesStudioRuleRows { get; } = [];
    public ObservableCollection<AccountingWorkbenchRow> RulesStudioPromotionRows { get; } = [];
    public ObservableCollection<AccountingWorkbenchRow> RulesStudioActionRows { get; } = [];
    public ObservableCollection<AccountingWorkbenchRow> SetupReadinessRows { get; } = [];
    public ObservableCollection<AccountingWorkbenchRow> LedgerBookRows { get; } = [];
    public ObservableCollection<AccountingWorkbenchRow> AuditRows { get; } = [];
    public ObservableCollection<AccountingWorkbenchRow> ProviderRows { get; } = [];
    public ObservableCollection<AccountingWorkbenchRow> ExternalGlMappingProfileRows { get; } = [];
    public ObservableCollection<AccountingWorkbenchRow> ExternalGlEvidencePackageRows { get; } = [];
    public ObservableCollection<ExternalGlEvidenceRow> ExternalGlRows { get; } = [];
    public ObservableCollection<AccountingWorkbenchRow> ManualJournalDraftRows { get; } = [];
    public ObservableCollection<AccountingWorkbenchRow> ManualJournalFundEventLedgerRecordRows { get; } = [];
    public ObservableCollection<AccountingWorkbenchRow> ManualJournalCapitalAccountRows { get; } = [];
    public ObservableCollection<AccountingWorkbenchRow> ManualJournalCapitalAccountSubledgerRows { get; } = [];
    public ObservableCollection<AccountingWorkbenchRow> ManualJournalPaymentIntentRows { get; } = [];
    public ObservableCollection<AccountingWorkbenchRow> ManualJournalFundEventRows { get; } = [];
    public ObservableCollection<AccountingWorkbenchRow> ManualJournalLedgerImpactRows { get; } = [];
    public ObservableCollection<AccountingWorkbenchRow> ManualJournalReportOutputRows { get; } = [];
    public ObservableCollection<AccountingWorkbenchRow> ManualJournalEntryTypeRows { get; } = [];
    public ObservableCollection<AccountingWorkbenchRow> CapitalAccountInvestorRows { get; } = [];
    public ObservableCollection<AccountingWorkbenchRow> CapitalAccountAllocationRuleRows { get; } = [];
    public ObservableCollection<AccountingWorkbenchRow> CapitalAccountStatementLineageRows { get; } = [];
    public ObservableCollection<AccountingWorkbenchRow> CapitalAccountAuditDrillThroughRows { get; } = [];
    public ObservableCollection<AccountingWorkbenchRow> CapitalAccountCapabilityRows { get; } = [];
    public ObservableCollection<AccountingWorkbenchRow> EvidenceRows { get; } = [];
    public ObservableCollection<AccountingWorkbenchRow> ReconciliationRows { get; } = [];
    public ObservableCollection<AccountingWorkbenchRow> PolicyRows { get; } = [];
    public ObservableCollection<AccountingWorkbenchRow> PostingCandidateRows { get; } = [];
    public ObservableCollection<AccountingWorkbenchRow> ManualJournalLifecycleRows { get; } = [];
    public ObservableCollection<AccountingWorkbenchRow> ProductionReadinessComponentRows { get; } = [];
    public ObservableCollection<AccountingWorkbenchRow> ProductionReadinessIssueRows { get; } = [];
    public ObservableCollection<AccountingWorkbenchRow> ProductionReadinessWorkflowRows { get; } = [];
    public ObservableCollection<AccountingWorkbenchRow> ProductionReadinessDimensionalControlRows { get; } = [];
    public ObservableCollection<AccountingWorkbenchRow> ProductionReadinessGapRows { get; } = [];
    public ObservableCollection<AccountingWorkbenchRow> ProductionReadinessMigrationPlanRows { get; } = [];
    public ObservableCollection<AccountingWorkbenchRow> ProductionReadinessMigrationArtifactRows { get; } = [];
    public ObservableCollection<AccountingWorkbenchRow> ProductionReadinessMigrationWorkerPlanRows { get; } = [];
    public ObservableCollection<AccountingWorkbenchRow> TenantAdministrationControlRows { get; } = [];
    public ObservableCollection<string> ReconciliationViewOptions { get; } =
    [
        "Open breaks",
        "Awaiting evidence",
        "Critical or SLA",
        "All"
    ];
    public ObservableCollection<AccountingBasisKindDto> AccountingBasisOptions { get; }
    public ObservableCollection<ManualJournalEntryTypeDto> ManualJournalEntryTypeOptions { get; }

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string ActiveFundText
    {
        get => _activeFundText;
        private set => SetProperty(ref _activeFundText, value);
    }

    public string StoragePostureText
    {
        get => _storagePostureText;
        private set => SetProperty(ref _storagePostureText, value);
    }

    public string ConfigurationStatusText
    {
        get => _configurationStatusText;
        private set => SetProperty(ref _configurationStatusText, value);
    }

    public string ConfigurationDetailText
    {
        get => _configurationDetailText;
        private set => SetProperty(ref _configurationDetailText, value);
    }

    public string RulesStudioStatusText
    {
        get => _rulesStudioStatusText;
        private set => SetProperty(ref _rulesStudioStatusText, value);
    }

    public string RulesStudioDetailText
    {
        get => _rulesStudioDetailText;
        private set => SetProperty(ref _rulesStudioDetailText, value);
    }

    public string RulesStudioActionText
    {
        get => _rulesStudioActionText;
        private set => SetProperty(ref _rulesStudioActionText, value);
    }

    public string ManualJournalStatusText
    {
        get => _manualJournalStatusText;
        private set => SetProperty(ref _manualJournalStatusText, value);
    }

    public string CapitalAccountWorkbenchStatusText
    {
        get => _capitalAccountWorkbenchStatusText;
        private set => SetProperty(ref _capitalAccountWorkbenchStatusText, value);
    }

    public string ExternalGlStatusText
    {
        get => _externalGlStatusText;
        private set => SetProperty(ref _externalGlStatusText, value);
    }

    public string PostingPostureText
    {
        get => _postingPostureText;
        private set => SetProperty(ref _postingPostureText, value);
    }

    public string ChartAccountSetupStatusText
    {
        get => _chartAccountSetupStatusText;
        private set => SetProperty(ref _chartAccountSetupStatusText, value);
    }

    public string ChartAccountNodeId
    {
        get => _chartAccountNodeId;
        set
        {
            if (SetProperty(ref _chartAccountNodeId, value))
            {
                NotifyChartAccountSaveStateChanged();
            }
        }
    }

    public string ChartAccountPath
    {
        get => _chartAccountPath;
        set
        {
            if (SetProperty(ref _chartAccountPath, value))
            {
                NotifyChartAccountSaveStateChanged();
            }
        }
    }

    public string ChartAccountName
    {
        get => _chartAccountName;
        set
        {
            if (SetProperty(ref _chartAccountName, value))
            {
                NotifyChartAccountSaveStateChanged();
            }
        }
    }

    public string ChartAccountType
    {
        get => _chartAccountType;
        set
        {
            if (SetProperty(ref _chartAccountType, value))
            {
                NotifyChartAccountSaveStateChanged();
            }
        }
    }

    public string ChartAccountParentPath
    {
        get => _chartAccountParentPath;
        set => SetProperty(ref _chartAccountParentPath, value);
    }

    public string ChartAccountFinancialAccountId
    {
        get => _chartAccountFinancialAccountId;
        set => SetProperty(ref _chartAccountFinancialAccountId, value);
    }

    public string ChartAccountEvidenceText
    {
        get => _chartAccountEvidenceText;
        set
        {
            if (SetProperty(ref _chartAccountEvidenceText, value))
            {
                NotifyChartAccountSaveStateChanged();
            }
        }
    }

    public bool CanSaveChartAccount =>
        _activeFundProfile is not null
        && !string.IsNullOrWhiteSpace(ChartAccountNodeId)
        && !string.IsNullOrWhiteSpace(ChartAccountPath)
        && !string.IsNullOrWhiteSpace(ChartAccountName)
        && !string.IsNullOrWhiteSpace(ChartAccountType)
        && NormalizeTenantAdministrationEvidence(ChartAccountEvidenceText).Count > 0;

    public string PostingRuleDraftStatusText
    {
        get => _postingRuleDraftStatusText;
        private set => SetProperty(ref _postingRuleDraftStatusText, value);
    }

    public string PostingRuleDraftRuleId
    {
        get => _postingRuleDraftRuleId;
        set
        {
            if (SetProperty(ref _postingRuleDraftRuleId, value))
            {
                NotifyPostingRuleDraftSaveStateChanged();
            }
        }
    }

    public string PostingRuleDraftDisplayName
    {
        get => _postingRuleDraftDisplayName;
        set
        {
            if (SetProperty(ref _postingRuleDraftDisplayName, value))
            {
                NotifyPostingRuleDraftSaveStateChanged();
            }
        }
    }

    public string PostingRuleDraftSourceEventType
    {
        get => _postingRuleDraftSourceEventType;
        set
        {
            if (SetProperty(ref _postingRuleDraftSourceEventType, value))
            {
                NotifyPostingRuleDraftSaveStateChanged();
            }
        }
    }

    public string PostingRuleDraftTemplateId
    {
        get => _postingRuleDraftTemplateId;
        set
        {
            if (SetProperty(ref _postingRuleDraftTemplateId, value))
            {
                NotifyPostingRuleDraftSaveStateChanged();
            }
        }
    }

    public string PostingRuleDraftDescription
    {
        get => _postingRuleDraftDescription;
        set => SetProperty(ref _postingRuleDraftDescription, value);
    }

    public int PostingRuleDraftPriority
    {
        get => _postingRuleDraftPriority;
        set => SetProperty(ref _postingRuleDraftPriority, value);
    }

    public string PostingRuleDraftEvidenceText
    {
        get => _postingRuleDraftEvidenceText;
        set
        {
            if (SetProperty(ref _postingRuleDraftEvidenceText, value))
            {
                NotifyPostingRuleDraftSaveStateChanged();
            }
        }
    }

    public bool CanSavePostingRuleDraft =>
        _activeFundProfile is not null
        && !string.IsNullOrWhiteSpace(PostingRuleDraftRuleId)
        && !string.IsNullOrWhiteSpace(PostingRuleDraftDisplayName)
        && !string.IsNullOrWhiteSpace(PostingRuleDraftSourceEventType)
        && !string.IsNullOrWhiteSpace(PostingRuleDraftTemplateId)
        && NormalizeTenantAdministrationEvidence(PostingRuleDraftEvidenceText).Count > 0;

    public string ExternalGlMappingProfileStatusText
    {
        get => _externalGlMappingProfileStatusText;
        private set => SetProperty(ref _externalGlMappingProfileStatusText, value);
    }

    public string ExternalGlMappingProviderId
    {
        get => _externalGlMappingProviderId;
        set
        {
            if (SetProperty(ref _externalGlMappingProviderId, value))
            {
                NotifyExternalGlMappingProfileSaveStateChanged();
            }
        }
    }

    public string ExternalGlMappingProfileId
    {
        get => _externalGlMappingProfileId;
        set
        {
            if (SetProperty(ref _externalGlMappingProfileId, value))
            {
                NotifyExternalGlMappingProfileSaveStateChanged();
            }
        }
    }

    public string ExternalGlMappingDisplayName
    {
        get => _externalGlMappingDisplayName;
        set
        {
            if (SetProperty(ref _externalGlMappingDisplayName, value))
            {
                NotifyExternalGlMappingProfileSaveStateChanged();
            }
        }
    }

    public string ExternalGlMappingAccountMappingsText
    {
        get => _externalGlMappingAccountMappingsText;
        set
        {
            if (SetProperty(ref _externalGlMappingAccountMappingsText, value))
            {
                NotifyExternalGlMappingProfileSaveStateChanged();
            }
        }
    }

    public string ExternalGlMappingMeridianDimensionsText
    {
        get => _externalGlMappingMeridianDimensionsText;
        set => SetProperty(ref _externalGlMappingMeridianDimensionsText, value);
    }

    public string ExternalGlMappingExternalDimensionsText
    {
        get => _externalGlMappingExternalDimensionsText;
        set => SetProperty(ref _externalGlMappingExternalDimensionsText, value);
    }

    public string ExternalGlMappingEvidenceText
    {
        get => _externalGlMappingEvidenceText;
        set
        {
            if (SetProperty(ref _externalGlMappingEvidenceText, value))
            {
                NotifyExternalGlMappingProfileSaveStateChanged();
            }
        }
    }

    public bool ExternalGlMappingProfileCertified
    {
        get => _externalGlMappingProfileCertified;
        set => SetProperty(ref _externalGlMappingProfileCertified, value);
    }

    public bool CanSaveExternalGlMappingProfile =>
        _activeFundProfile is not null
        && _accountingSystemIntegrationService is not null
        && !string.IsNullOrWhiteSpace(ExternalGlMappingProviderId)
        && !string.IsNullOrWhiteSpace(ExternalGlMappingProfileId)
        && !string.IsNullOrWhiteSpace(ExternalGlMappingDisplayName)
        && ParseExternalGlAccountMappings(ExternalGlMappingAccountMappingsText).Count > 0
        && NormalizeTenantAdministrationEvidence(ExternalGlMappingEvidenceText).Count > 0;

    public string CloseReadinessText
    {
        get => _closeReadinessText;
        private set => SetProperty(ref _closeReadinessText, value);
    }

    public string CloseDetailText
    {
        get => _closeDetailText;
        private set => SetProperty(ref _closeDetailText, value);
    }

    public string EvidencePostureText
    {
        get => _evidencePostureText;
        private set => SetProperty(ref _evidencePostureText, value);
    }

    public string ReconciliationPostureText
    {
        get => _reconciliationPostureText;
        private set => SetProperty(ref _reconciliationPostureText, value);
    }

    public string PolicyPostureText
    {
        get => _policyPostureText;
        private set => SetProperty(ref _policyPostureText, value);
    }

    public string PostingCandidateStatusText
    {
        get => _postingCandidateStatusText;
        private set => SetProperty(ref _postingCandidateStatusText, value);
    }

    public string PostingCandidateDetailText
    {
        get => _postingCandidateDetailText;
        private set => SetProperty(ref _postingCandidateDetailText, value);
    }

    public string ManualJournalLifecycleStatusText
    {
        get => _manualJournalLifecycleStatusText;
        private set => SetProperty(ref _manualJournalLifecycleStatusText, value);
    }

    public string LedgerBookSetupStatusText
    {
        get => _ledgerBookSetupStatusText;
        private set => SetProperty(ref _ledgerBookSetupStatusText, value);
    }

    public string LedgerBookAdministrationText
    {
        get => _ledgerBookAdministrationText;
        private set => SetProperty(ref _ledgerBookAdministrationText, value);
    }

    public string ProductionReadinessStatusText
    {
        get => _productionReadinessStatusText;
        private set => SetProperty(ref _productionReadinessStatusText, value);
    }

    public string ProductionReadinessDetailText
    {
        get => _productionReadinessDetailText;
        private set => SetProperty(ref _productionReadinessDetailText, value);
    }

    public string ProductionReadinessScoreText
    {
        get => _productionReadinessScoreText;
        private set => SetProperty(ref _productionReadinessScoreText, value);
    }

    public string ProductionReadinessLedgerBookText
    {
        get => _productionReadinessLedgerBookText;
        private set => SetProperty(ref _productionReadinessLedgerBookText, value);
    }

    public string ProductionReadinessExternalGlText
    {
        get => _productionReadinessExternalGlText;
        private set => SetProperty(ref _productionReadinessExternalGlText, value);
    }

    public string ProductionReadinessDimensionalReportingText
    {
        get => _productionReadinessDimensionalReportingText;
        private set => SetProperty(ref _productionReadinessDimensionalReportingText, value);
    }

    public string ProductionReadinessTenantAdminText
    {
        get => _productionReadinessTenantAdminText;
        private set => SetProperty(ref _productionReadinessTenantAdminText, value);
    }

    public string ProductionCertificationProfileStatusText
    {
        get => _productionCertificationProfileStatusText;
        private set => SetProperty(ref _productionCertificationProfileStatusText, value);
    }

    public string ProductionCertificationProfileScopeText
    {
        get => _productionCertificationProfileScopeText;
        private set => SetProperty(ref _productionCertificationProfileScopeText, value);
    }

    public string ProductionCertificationProfileUpdatedText
    {
        get => _productionCertificationProfileUpdatedText;
        private set => SetProperty(ref _productionCertificationProfileUpdatedText, value);
    }

    public string ProductionCertificationEvidenceText
    {
        get => _productionCertificationEvidenceText;
        set
        {
            if (SetProperty(ref _productionCertificationEvidenceText, value))
            {
                NotifyProductionCertificationSaveStateChanged();
            }
        }
    }

    public bool PostingRulesLedgerBookNativeCertified
    {
        get => _postingRulesLedgerBookNativeCertified;
        set => SetProperty(ref _postingRulesLedgerBookNativeCertified, value);
    }

    public bool JournalLifecycleLedgerBookNativeCertified
    {
        get => _journalLifecycleLedgerBookNativeCertified;
        set => SetProperty(ref _journalLifecycleLedgerBookNativeCertified, value);
    }

    public bool CloseReportingLedgerBookNativeCertified
    {
        get => _closeReportingLedgerBookNativeCertified;
        set => SetProperty(ref _closeReportingLedgerBookNativeCertified, value);
    }

    public bool ClosePlanConfigurationLedgerBookNativeCertified
    {
        get => _closePlanConfigurationLedgerBookNativeCertified;
        set => SetProperty(ref _closePlanConfigurationLedgerBookNativeCertified, value);
    }

    public bool ExternalGlLedgerBookNativeCertified
    {
        get => _externalGlLedgerBookNativeCertified;
        set => SetProperty(ref _externalGlLedgerBookNativeCertified, value);
    }

    public bool ReconciliationLedgerBookNativeCertified
    {
        get => _reconciliationLedgerBookNativeCertified;
        set => SetProperty(ref _reconciliationLedgerBookNativeCertified, value);
    }

    public bool DirectLendingLedgerBookNativeCertified
    {
        get => _directLendingLedgerBookNativeCertified;
        set => SetProperty(ref _directLendingLedgerBookNativeCertified, value);
    }

    public bool StrategyLedgerReadLedgerBookNativeCertified
    {
        get => _strategyLedgerReadLedgerBookNativeCertified;
        set => SetProperty(ref _strategyLedgerReadLedgerBookNativeCertified, value);
    }

    public bool PeriodReportDimensionQueriesCertified
    {
        get => _periodReportDimensionQueriesCertified;
        set => SetProperty(ref _periodReportDimensionQueriesCertified, value);
    }

    public bool CrossPeriodReportDimensionQueriesCertified
    {
        get => _crossPeriodReportDimensionQueriesCertified;
        set => SetProperty(ref _crossPeriodReportDimensionQueriesCertified, value);
    }

    public bool JournalQueryDimensionFiltersCertified
    {
        get => _journalQueryDimensionFiltersCertified;
        set => SetProperty(ref _journalQueryDimensionFiltersCertified, value);
    }

    public bool ExternalExportDimensionMappingCertified
    {
        get => _externalExportDimensionMappingCertified;
        set => SetProperty(ref _externalExportDimensionMappingCertified, value);
    }

    public bool LedgerLineDimensionsPersistedCertified
    {
        get => _ledgerLineDimensionsPersistedCertified;
        set => SetProperty(ref _ledgerLineDimensionsPersistedCertified, value);
    }

    public bool TrialBalanceDimensionFiltersCertified
    {
        get => _trialBalanceDimensionFiltersCertified;
        set => SetProperty(ref _trialBalanceDimensionFiltersCertified, value);
    }

    public bool ReportPackageDimensionProvenanceCertified
    {
        get => _reportPackageDimensionProvenanceCertified;
        set => SetProperty(ref _reportPackageDimensionProvenanceCertified, value);
    }

    public bool CanSaveProductionCertificationProfile =>
        _activeFundProfile is not null
        && _productionCertificationProfileStore is not null
        && NormalizeTenantAdministrationEvidence(ProductionCertificationEvidenceText).Count > 0;

    public string TenantAdministrationProfileStatusText
    {
        get => _tenantAdministrationProfileStatusText;
        private set => SetProperty(ref _tenantAdministrationProfileStatusText, value);
    }

    public string TenantAdministrationProfileScopeText
    {
        get => _tenantAdministrationProfileScopeText;
        private set => SetProperty(ref _tenantAdministrationProfileScopeText, value);
    }

    public string TenantAdministrationProfileUpdatedText
    {
        get => _tenantAdministrationProfileUpdatedText;
        private set => SetProperty(ref _tenantAdministrationProfileUpdatedText, value);
    }

    public string TenantAdministrationEvidenceText
    {
        get => _tenantAdministrationEvidenceText;
        set
        {
            if (SetProperty(ref _tenantAdministrationEvidenceText, value))
            {
                NotifyTenantAdministrationSaveStateChanged();
            }
        }
    }

    public string ImplementationSandboxProofStatusText
    {
        get => _implementationSandboxProofStatusText;
        private set => SetProperty(ref _implementationSandboxProofStatusText, value);
    }

    public bool TenantScopeConfigured
    {
        get => _tenantScopeConfigured;
        set => SetProperty(ref _tenantScopeConfigured, value);
    }

    public bool AdminRoleProfileConfigured
    {
        get => _adminRoleProfileConfigured;
        set => SetProperty(ref _adminRoleProfileConfigured, value);
    }

    public bool ScopedAccessPoliciesConfigured
    {
        get => _scopedAccessPoliciesConfigured;
        set => SetProperty(ref _scopedAccessPoliciesConfigured, value);
    }

    public bool ReportingGroupsConfigured
    {
        get => _reportingGroupsConfigured;
        set => SetProperty(ref _reportingGroupsConfigured, value);
    }

    public bool AccountingAdminSurfaceConfigured
    {
        get => _accountingAdminSurfaceConfigured;
        set => SetProperty(ref _accountingAdminSurfaceConfigured, value);
    }

    public bool WpfAccountingAdminSurfaceConfigured
    {
        get => _wpfAccountingAdminSurfaceConfigured;
        set => SetProperty(ref _wpfAccountingAdminSurfaceConfigured, value);
    }

    public bool ChartAdministrationStudioConfigured
    {
        get => _chartAdministrationStudioConfigured;
        set => SetProperty(ref _chartAdministrationStudioConfigured, value);
    }

    public bool RuleTestPromotionStudioConfigured
    {
        get => _ruleTestPromotionStudioConfigured;
        set => SetProperty(ref _ruleTestPromotionStudioConfigured, value);
    }

    public bool CloseSetupStudioConfigured
    {
        get => _closeSetupStudioConfigured;
        set => SetProperty(ref _closeSetupStudioConfigured, value);
    }

    public bool ProviderMappingStudioConfigured
    {
        get => _providerMappingStudioConfigured;
        set => SetProperty(ref _providerMappingStudioConfigured, value);
    }

    public bool TenantCompanyReportGroupSetupStudioConfigured
    {
        get => _tenantCompanyReportGroupSetupStudioConfigured;
        set => SetProperty(ref _tenantCompanyReportGroupSetupStudioConfigured, value);
    }

    public bool AuditReviewToolingConfigured
    {
        get => _auditReviewToolingConfigured;
        set => SetProperty(ref _auditReviewToolingConfigured, value);
    }

    public bool BulkImportExportSafeguardsConfigured
    {
        get => _bulkImportExportSafeguardsConfigured;
        set => SetProperty(ref _bulkImportExportSafeguardsConfigured, value);
    }

    public bool PerformanceValidationConfigured
    {
        get => _performanceValidationConfigured;
        set => SetProperty(ref _performanceValidationConfigured, value);
    }

    public bool DisasterRecoveryRunbookConfigured
    {
        get => _disasterRecoveryRunbookConfigured;
        set => SetProperty(ref _disasterRecoveryRunbookConfigured, value);
    }

    public bool LedgerBookAdministrationStudioConfigured
    {
        get => _ledgerBookAdministrationStudioConfigured;
        set => SetProperty(ref _ledgerBookAdministrationStudioConfigured, value);
    }

    public bool PostingRuleAuthoringStudioConfigured
    {
        get => _postingRuleAuthoringStudioConfigured;
        set => SetProperty(ref _postingRuleAuthoringStudioConfigured, value);
    }

    public bool ApprovalQueueStudioConfigured
    {
        get => _approvalQueueStudioConfigured;
        set
        {
            if (SetProperty(ref _approvalQueueStudioConfigured, value))
            {
                NotifyTenantAdministrationSaveStateChanged();
            }
        }
    }

    public string ApprovalQueueId
    {
        get => _approvalQueueId;
        set
        {
            if (SetProperty(ref _approvalQueueId, value))
            {
                NotifyTenantAdministrationSaveStateChanged();
            }
        }
    }

    public string ApprovalQueueDisplayName
    {
        get => _approvalQueueDisplayName;
        set
        {
            if (SetProperty(ref _approvalQueueDisplayName, value))
            {
                NotifyTenantAdministrationSaveStateChanged();
            }
        }
    }

    public string ApprovalQueueWorkflowKind
    {
        get => _approvalQueueWorkflowKind;
        set
        {
            if (SetProperty(ref _approvalQueueWorkflowKind, value))
            {
                NotifyTenantAdministrationSaveStateChanged();
            }
        }
    }

    public string ApprovalQueueRequiredApprovalRole
    {
        get => _approvalQueueRequiredApprovalRole;
        set
        {
            if (SetProperty(ref _approvalQueueRequiredApprovalRole, value))
            {
                NotifyTenantAdministrationSaveStateChanged();
            }
        }
    }

    public int ApprovalQueueRequiredApprovalCount
    {
        get => _approvalQueueRequiredApprovalCount;
        set
        {
            if (SetProperty(ref _approvalQueueRequiredApprovalCount, value))
            {
                NotifyTenantAdministrationSaveStateChanged();
            }
        }
    }

    public string ApprovalQueueSegregationPolicy
    {
        get => _approvalQueueSegregationPolicy;
        set
        {
            if (SetProperty(ref _approvalQueueSegregationPolicy, value))
            {
                NotifyTenantAdministrationSaveStateChanged();
            }
        }
    }

    public string ApprovalQueueEvidenceRequirement
    {
        get => _approvalQueueEvidenceRequirement;
        set
        {
            if (SetProperty(ref _approvalQueueEvidenceRequirement, value))
            {
                NotifyTenantAdministrationSaveStateChanged();
            }
        }
    }

    public bool DimensionMappingStudioConfigured
    {
        get => _dimensionMappingStudioConfigured;
        set
        {
            if (SetProperty(ref _dimensionMappingStudioConfigured, value))
            {
                NotifyTenantAdministrationSaveStateChanged();
            }
        }
    }

    public string DimensionMappingId
    {
        get => _dimensionMappingId;
        set
        {
            if (SetProperty(ref _dimensionMappingId, value))
            {
                NotifyTenantAdministrationSaveStateChanged();
            }
        }
    }

    public string DimensionMappingDisplayName
    {
        get => _dimensionMappingDisplayName;
        set
        {
            if (SetProperty(ref _dimensionMappingDisplayName, value))
            {
                NotifyTenantAdministrationSaveStateChanged();
            }
        }
    }

    public string DimensionMappingProviderId
    {
        get => _dimensionMappingProviderId;
        set
        {
            if (SetProperty(ref _dimensionMappingProviderId, value))
            {
                NotifyTenantAdministrationSaveStateChanged();
            }
        }
    }

    public string DimensionMappingMeridianDimensionsText
    {
        get => _dimensionMappingMeridianDimensionsText;
        set
        {
            if (SetProperty(ref _dimensionMappingMeridianDimensionsText, value))
            {
                NotifyTenantAdministrationSaveStateChanged();
            }
        }
    }

    public string DimensionMappingProviderDimensionsText
    {
        get => _dimensionMappingProviderDimensionsText;
        set
        {
            if (SetProperty(ref _dimensionMappingProviderDimensionsText, value))
            {
                NotifyTenantAdministrationSaveStateChanged();
            }
        }
    }

    public string DimensionMappingEvidenceRequirement
    {
        get => _dimensionMappingEvidenceRequirement;
        set
        {
            if (SetProperty(ref _dimensionMappingEvidenceRequirement, value))
            {
                NotifyTenantAdministrationSaveStateChanged();
            }
        }
    }

    public bool ImplementationSandboxConfigured
    {
        get => _implementationSandboxConfigured;
        set => SetProperty(ref _implementationSandboxConfigured, value);
    }

    public bool CanSaveTenantAdministrationProfile =>
        _activeFundProfile is not null
        && _tenantAdministrationProfileStore is not null
        && NormalizeTenantAdministrationEvidence(TenantAdministrationEvidenceText).Count > 0
        && (!ApprovalQueueStudioConfigured || BuildApprovalQueueConfigurations().Count > 0)
        && (!DimensionMappingStudioConfigured || BuildDimensionMappingConfigurations().Count > 0);

    public bool CanRetainImplementationSandboxProof =>
        _activeFundProfile is not null
        && _tenantAdministrationProfileStore is not null
        && _configuration?.LedgerBookId is not null;

    public string SelectedReconciliationView
    {
        get => _selectedReconciliationView;
        set
        {
            if (SetProperty(ref _selectedReconciliationView, value))
            {
                ApplyReconciliationView();
            }
        }
    }

    public string BatchReconciliationActionText
    {
        get => _batchReconciliationActionText;
        private set => SetProperty(ref _batchReconciliationActionText, value);
    }

    public string DraftMemo
    {
        get => _draftMemo;
        set => SetProperty(ref _draftMemo, value);
    }

    public string DraftCurrency
    {
        get => _draftCurrency;
        set
        {
            if (SetProperty(ref _draftCurrency, value))
            {
                RaisePropertyChanged(nameof(ManualJournalDraftBalanceText));
                RaisePropertyChanged(nameof(ManualJournalDraftBalanceBadgeText));
            }
        }
    }

    public decimal DraftAmount
    {
        get => _draftAmount;
        set
        {
            if (SetProperty(ref _draftAmount, value))
            {
                RaisePropertyChanged(nameof(ManualJournalDraftBalanceText));
                RaisePropertyChanged(nameof(ManualJournalDraftBalanceBadgeText));
            }
        }
    }

    public string DraftDebitAccountPath
    {
        get => _draftDebitAccountPath;
        set => SetProperty(ref _draftDebitAccountPath, value);
    }

    public string DraftCreditAccountPath
    {
        get => _draftCreditAccountPath;
        set => SetProperty(ref _draftCreditAccountPath, value);
    }

    public string DraftEvidenceLink
    {
        get => _draftEvidenceLink;
        set => SetProperty(ref _draftEvidenceLink, value);
    }

    public ManualJournalEntryTypeDto SelectedEntryType
    {
        get => _selectedEntryType;
        set
        {
            if (SetProperty(ref _selectedEntryType, value))
            {
                ApplyManualJournalEntryTypePreset(value);
            }
        }
    }

    public AccountingBasisKindDto SelectedAccountingBasis
    {
        get => _selectedAccountingBasis;
        set => SetProperty(ref _selectedAccountingBasis, value);
    }

    public bool CanSubmitManualJournal => _selectedDraft is { Version: > 0 };

    public string ManualJournalDraftReferenceText
        => _selectedDraft?.JournalEntryId.ToString("D") ?? "New draft";

    public string ManualJournalDraftDateText
        => (_selectedDraft?.AccountingDate ?? DateOnly.FromDateTime(DateTime.UtcNow)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public string ManualJournalDraftPeriodText
        => !string.IsNullOrWhiteSpace(_selectedDraft?.PeriodId)
            ? _selectedDraft.PeriodId
            : _selectedManualJournalPeriod is { } period
                ? $"{period.Label} | {period.PeriodId:D}"
                : "No open ledger period selected";

    public string ManualJournalDraftBalanceText
        => _selectedDraft is { } draft
            ? $"{draft.TotalDebits:0.##} / {draft.TotalCredits:0.##} {draft.Currency}"
            : $"{DraftAmount:0.##} / {DraftAmount:0.##} {DraftCurrency}";

    public string ManualJournalDraftBalanceBadgeText
        => _selectedDraft is { } draft && draft.TotalDebits != draft.TotalCredits
            ? $"Out by {Math.Abs(draft.TotalDebits - draft.TotalCredits):0.##} {draft.Currency}"
            : "Balanced";

    public async Task LoadAsync(CancellationToken ct = default)
        => await RefreshAsync(ct).ConfigureAwait(false);

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        IsLoading = true;
        try
        {
            _activeFundProfile = _fundContextService.CurrentFundProfile;
            StoragePostureText = BuildStoragePosture();

            if (_activeFundProfile is null)
            {
                ApplyLockedState();
                return;
            }

            ActiveFundText = $"{_activeFundProfile.DisplayName} | {_activeFundProfile.BaseCurrency}";
            DraftCurrency = _activeFundProfile.BaseCurrency;
            _configuration = await _configurationService.GetWorkspaceAsync(_activeFundProfile.FundProfileId, ct: ct).ConfigureAwait(false);
            var activeLedgerBookId = ResolveActiveLedgerBook(_configuration)?.LedgerBookId ?? _configuration.LedgerBookId;
            if (activeLedgerBookId.HasValue && _configuration.LedgerBookId != activeLedgerBookId)
            {
                _configuration = await _configurationService.GetWorkspaceAsync(
                    _activeFundProfile.FundProfileId,
                    activeLedgerBookId,
                    ct).ConfigureAwait(false);
            }

            ApplyConfiguration(_configuration);
            await LoadManualJournalPeriodAsync(ct).ConfigureAwait(false);
            await LoadManualJournalWorkbenchAsync(ct).ConfigureAwait(false);
            await LoadPolicyRowsAsync(ct).ConfigureAwait(false);
            await LoadOperationsPostureAsync(ct).ConfigureAwait(false);
            await RefreshExternalGlAsync(ct).ConfigureAwait(false);
            await LoadProductionCertificationProfileAsync(ct).ConfigureAwait(false);
            await LoadTenantAdministrationProfileAsync(ct).ConfigureAwait(false);
            await RefreshProductionReadinessAsync(ct).ConfigureAwait(false);
            StatusText = "Accounting configuration, journal drafts, close evidence, external GL evidence, reconciliation triage, and policies are loaded from shared services.";
        }
        catch (Exception ex)
        {
            StatusText = $"Accounting configure could not load: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task SeedBaselineConfigurationAsync(CancellationToken ct = default)
    {
        if (_activeFundProfile is null)
        {
            StatusText = "Select a fund-linked context before seeding accounting configuration.";
            return;
        }

        var fundProfileId = _activeFundProfile.FundProfileId;
        var evidence = new[] { $"accounting-config://{fundProfileId}/desktop-baseline" };
        try
        {
            _configuration ??= await _configurationService.GetWorkspaceAsync(fundProfileId, ct: ct).ConfigureAwait(false);
            var ledgerBookId = ResolveActiveLedgerBook(_configuration)?.LedgerBookId;
            await _configurationService.UpsertChartNodeAsync(
                new UpsertChartOfAccountsNodeRequest(
                    fundProfileId,
                    new ChartOfAccountsNodeDto("assets-cash", "Assets:Cash", "Cash", "Asset"),
                    DefaultActor,
                    CorrelationId: "wpf-accounting-config-seed",
                    EvidenceLinks: evidence,
                    LedgerBookId: ledgerBookId),
                ct).ConfigureAwait(false);
            await _configurationService.UpsertChartNodeAsync(
                new UpsertChartOfAccountsNodeRequest(
                    fundProfileId,
                    new ChartOfAccountsNodeDto("income-investment", "Income:Investment Income", "Investment Income", "Revenue"),
                    DefaultActor,
                    CorrelationId: "wpf-accounting-config-seed",
                    EvidenceLinks: evidence,
                    LedgerBookId: ledgerBookId),
                ct).ConfigureAwait(false);
            await _configurationService.UpsertChartNodeAsync(
                new UpsertChartOfAccountsNodeRequest(
                    fundProfileId,
                    new ChartOfAccountsNodeDto("expenses-investment-fees", "Expenses:Investment Fees", "Investment Fees", "Expense"),
                    DefaultActor,
                    CorrelationId: "wpf-accounting-config-seed",
                    EvidenceLinks: evidence,
                    LedgerBookId: ledgerBookId),
                ct).ConfigureAwait(false);
            foreach (var node in BuildManualJournalEntryTypeChartNodes())
            {
                await _configurationService.UpsertChartNodeAsync(
                    new UpsertChartOfAccountsNodeRequest(
                        fundProfileId,
                        node,
                        DefaultActor,
                        CorrelationId: "wpf-accounting-config-seed",
                        EvidenceLinks: evidence,
                        LedgerBookId: ledgerBookId),
                    ct).ConfigureAwait(false);
            }

            await _configurationService.UpsertTemplateAsync(
                new UpsertJournalEntryTemplateRequest(
                    fundProfileId,
                    new JournalEntryTemplateDto(
                        "desktop-manual-adjustment-v1",
                        "Desktop manual adjustment",
                        "Balanced manual adjustment template for WPF accounting drafts.",
                        [
                            new JournalEntryTemplateLineDto("debit-cash", "Assets:Cash", AccountingTemplateLineSideDto.Debit, 1m, _activeFundProfile.BaseCurrency, "Debit configured cash account."),
                            new JournalEntryTemplateLineDto("credit-income", "Income:Investment Income", AccountingTemplateLineSideDto.Credit, 1m, _activeFundProfile.BaseCurrency, "Credit configured income account.")
                        ]),
                    DefaultActor,
                    CorrelationId: "wpf-accounting-config-seed",
                    EvidenceLinks: evidence,
                    LedgerBookId: ledgerBookId),
                ct).ConfigureAwait(false);
            foreach (var template in BuildManualJournalEntryTypeTemplates(_activeFundProfile.BaseCurrency))
            {
                await _configurationService.UpsertTemplateAsync(
                    new UpsertJournalEntryTemplateRequest(
                        fundProfileId,
                        template,
                        DefaultActor,
                        CorrelationId: "wpf-accounting-config-seed",
                        EvidenceLinks: evidence,
                        LedgerBookId: ledgerBookId),
                    ct).ConfigureAwait(false);
            }

            await _configurationService.UpsertPostingRuleAsync(
                new UpsertPostingRuleRequest(
                    fundProfileId,
                    new PostingRuleDto(
                        "manual-adjustment-policy-v1",
                        "Manual adjustment policy",
                        "ManualJournalEntry",
                        "desktop-manual-adjustment-v1",
                        Description: "Fund/book configurable WPF policy route for manual accounting adjustments."),
                    DefaultActor,
                    CorrelationId: "wpf-accounting-config-seed",
                    EvidenceLinks: evidence,
                    LedgerBookId: ledgerBookId),
                ct).ConfigureAwait(false);
            foreach (var rule in BuildManualJournalEntryTypePostingRules())
            {
                await _configurationService.UpsertPostingRuleAsync(
                    new UpsertPostingRuleRequest(
                        fundProfileId,
                        rule,
                        DefaultActor,
                        CorrelationId: "wpf-accounting-config-seed",
                        EvidenceLinks: evidence,
                        LedgerBookId: ledgerBookId),
                    ct).ConfigureAwait(false);
            }

            _configuration = await _configurationService.GetWorkspaceAsync(fundProfileId, ledgerBookId, ct).ConfigureAwait(false);
            ApplyConfiguration(_configuration);
            await LoadManualJournalPeriodAsync(ct).ConfigureAwait(false);
            await LoadManualJournalWorkbenchAsync(ct).ConfigureAwait(false);
            await RefreshProductionReadinessAsync(ct).ConfigureAwait(false);
            StatusText = "Baseline chart, template, and posting policy were saved with audit evidence.";
        }
        catch (Exception ex)
        {
            StatusText = $"Baseline accounting configuration could not be saved: {ex.Message}";
        }
    }

    public async Task ActivateConfigurationAsync(CancellationToken ct = default)
    {
        if (_activeFundProfile is null)
        {
            StatusText = "Select a fund-linked context before activating accounting configuration.";
            return;
        }

        try
        {
            _configuration = await _configurationService.ActivateAsync(
                new ActivateAccountingConfigurationRequest(
                    _activeFundProfile.FundProfileId,
                    DefaultActor,
                    _configuration?.LedgerBookId,
                    CorrelationId: "wpf-accounting-config-activate",
                    EvidenceLinks: ["accounting-config://desktop-activation"]),
                ct).ConfigureAwait(false);
            ApplyConfiguration(_configuration);
            await LoadManualJournalPeriodAsync(ct).ConfigureAwait(false);
            await RefreshProductionReadinessAsync(ct).ConfigureAwait(false);
            StatusText = "Accounting configuration is active.";
        }
        catch (Exception ex)
        {
            StatusText = $"Accounting configuration activation is blocked: {ex.Message}";
        }
    }

    public async Task CreateLedgerBookSetupAsync(CancellationToken ct = default)
    {
        if (_activeFundProfile is null)
        {
            LedgerBookSetupStatusText = "Select a fund-linked context before creating ledger-book setup.";
            StatusText = LedgerBookSetupStatusText;
            return;
        }

        if (_ledgerBookService is null)
        {
            LedgerBookSetupStatusText = "Ledger-book service is not registered for this desktop session.";
            StatusText = LedgerBookSetupStatusText;
            return;
        }

        var candidate = _configuration?.LedgerBookSetupCandidate;
        if (candidate is null)
        {
            LedgerBookSetupStatusText = "No shared ledger-book setup candidate is available for the current configuration scope.";
            StatusText = LedgerBookSetupStatusText;
            return;
        }

        try
        {
            var created = await _ledgerBookService.CreateBookAsync(
                new CreateLedgerBookRequest(
                    candidate.FundProfileId,
                    candidate.FundStructureNodeId,
                    candidate.FundStructureNodeKind,
                    candidate.DisplayName,
                    candidate.BaseCurrency,
                    candidate.Description,
                    candidate.AccountingBasis,
                    candidate.AccountingPolicyId,
                    candidate.AccountingPolicyVersion),
                ct).ConfigureAwait(false);

            _configuration = await _configurationService.GetWorkspaceAsync(
                candidate.FundProfileId,
                created.LedgerBookId,
                ct).ConfigureAwait(false);
            ApplyConfiguration(_configuration);
            await LoadManualJournalPeriodAsync(ct).ConfigureAwait(false);
            await LoadManualJournalWorkbenchAsync(ct).ConfigureAwait(false);
            await LoadPolicyRowsAsync(ct).ConfigureAwait(false);
            await LoadOperationsPostureAsync(ct).ConfigureAwait(false);
            await RefreshExternalGlAsync(ct).ConfigureAwait(false);
            await RefreshProductionReadinessAsync(ct).ConfigureAwait(false);

            LedgerBookSetupStatusText = $"Created ledger book {created.DisplayName} ({created.LedgerBookId:D}) from shared setup candidate.";
            StatusText = LedgerBookSetupStatusText;
        }
        catch (Exception ex)
        {
            LedgerBookSetupStatusText = $"Ledger-book setup is blocked: {ex.Message}";
            StatusText = LedgerBookSetupStatusText;
        }
    }

    public async Task SaveTenantAdministrationProfileAsync(CancellationToken ct = default)
    {
        if (_activeFundProfile is null)
        {
            TenantAdministrationProfileStatusText = "Select a fund-linked context before saving tenant administration setup controls.";
            StatusText = TenantAdministrationProfileStatusText;
            return;
        }

        if (_tenantAdministrationProfileStore is null)
        {
            TenantAdministrationProfileStatusText = "Tenant administration profile store is not registered for this desktop session.";
            StatusText = TenantAdministrationProfileStatusText;
            return;
        }

        var tenantId = ResolveTenantAdministrationTenantId();
        var companyId = ResolveTenantAdministrationCompanyId();
        var evidence = WithLedgerBookTenantAdministrationEvidence(
            NormalizeTenantAdministrationEvidence(TenantAdministrationEvidenceText),
            tenantId,
            companyId,
            _configuration?.LedgerBookId,
            LedgerBookAdministrationStudioConfigured,
            ImplementationSandboxConfigured);
        if (evidence.Count == 0)
        {
            TenantAdministrationProfileStatusText = "Retained setup evidence is required before saving tenant administration controls.";
            StatusText = TenantAdministrationProfileStatusText;
            NotifyTenantAdministrationSaveStateChanged();
            return;
        }

        var approvalQueueConfigurations = BuildApprovalQueueConfigurations();
        if (ApprovalQueueStudioConfigured && approvalQueueConfigurations.Count == 0)
        {
            TenantAdministrationProfileStatusText = "Complete approval queue id, display name, workflow kind, approval role/count, segregation policy, and evidence requirement before saving.";
            StatusText = TenantAdministrationProfileStatusText;
            return;
        }

        var dimensionMappingConfigurations = BuildDimensionMappingConfigurations();
        if (DimensionMappingStudioConfigured && dimensionMappingConfigurations.Count == 0)
        {
            TenantAdministrationProfileStatusText = "Complete dimension mapping id, display name, provider id, Meridian/provider dimensions, and evidence requirement before saving.";
            StatusText = TenantAdministrationProfileStatusText;
            return;
        }

        try
        {
            var correlationId = $"wpf-accounting-tenant-admin-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
            var profile = new AccountingTenantAdministrationProfileDto(
                tenantId,
                companyId,
                TenantScopeConfigured,
                AdminRoleProfileConfigured,
                ScopedAccessPoliciesConfigured,
                ReportingGroupsConfigured,
                AccountingAdminSurfaceConfigured,
                DateTimeOffset.UtcNow,
                DefaultLifecycleActor,
                evidence,
                correlationId,
                WpfAccountingAdminSurfaceConfigured: WpfAccountingAdminSurfaceConfigured,
                ChartAdministrationStudioConfigured: ChartAdministrationStudioConfigured,
                RuleTestPromotionStudioConfigured: RuleTestPromotionStudioConfigured,
                CloseSetupStudioConfigured: CloseSetupStudioConfigured,
                ProviderMappingStudioConfigured: ProviderMappingStudioConfigured,
                TenantCompanyReportGroupSetupStudioConfigured: TenantCompanyReportGroupSetupStudioConfigured,
                AuditReviewToolingConfigured: AuditReviewToolingConfigured,
                BulkImportExportSafeguardsConfigured: BulkImportExportSafeguardsConfigured,
                PerformanceValidationConfigured: PerformanceValidationConfigured,
                DisasterRecoveryRunbookConfigured: DisasterRecoveryRunbookConfigured,
                LedgerBookAdministrationStudioConfigured: LedgerBookAdministrationStudioConfigured,
                PostingRuleAuthoringStudioConfigured: PostingRuleAuthoringStudioConfigured,
                ApprovalQueueStudioConfigured: ApprovalQueueStudioConfigured,
                DimensionMappingStudioConfigured: DimensionMappingStudioConfigured,
                ImplementationSandboxConfigured: ImplementationSandboxConfigured,
                ApprovalQueueConfigurations: approvalQueueConfigurations,
                DimensionMappingConfigurations: dimensionMappingConfigurations);

            var saved = await _tenantAdministrationProfileStore.UpsertAsync(
                new AccountingTenantAdministrationProfileUpsertRequestDto(
                    profile,
                    DefaultLifecycleActor,
                    correlationId,
                    evidence),
                ct).ConfigureAwait(false);

            ApplyTenantAdministrationProfile(saved, "Tenant administration setup profile saved; production readiness refreshed from retained controls.");
            await RefreshProductionReadinessAsync(ct).ConfigureAwait(false);
            StatusText = TenantAdministrationProfileStatusText;
        }
        catch (Exception ex)
        {
            TenantAdministrationProfileStatusText = $"Tenant administration setup profile save failed: {ex.Message}";
            StatusText = TenantAdministrationProfileStatusText;
        }
    }

    public async Task RetainImplementationSandboxProofAsync(CancellationToken ct = default)
    {
        if (_activeFundProfile is null)
        {
            ImplementationSandboxProofStatusText = "Select a fund-linked context before retaining implementation sandbox proof.";
            StatusText = ImplementationSandboxProofStatusText;
            return;
        }

        if (_tenantAdministrationProfileStore is null)
        {
            ImplementationSandboxProofStatusText = "Tenant administration profile store is not registered for this desktop session.";
            StatusText = ImplementationSandboxProofStatusText;
            return;
        }

        if (_configuration?.LedgerBookId is not { } ledgerBookId)
        {
            ImplementationSandboxProofStatusText = "Select a ledger-book-scoped Accounting Configure workspace before retaining implementation sandbox proof.";
            StatusText = ImplementationSandboxProofStatusText;
            return;
        }

        var tenantId = ResolveTenantAdministrationTenantId();
        var companyId = ResolveTenantAdministrationCompanyId();
        var evidence = WithLedgerBookTenantAdministrationEvidence(
            NormalizeTenantAdministrationEvidence(TenantAdministrationEvidenceText),
            tenantId,
            companyId,
            ledgerBookId,
            LedgerBookAdministrationStudioConfigured,
            implementationSandboxConfigured: true);
        var approvalQueueConfigurations = BuildApprovalQueueConfigurations();
        if (ApprovalQueueStudioConfigured && approvalQueueConfigurations.Count == 0)
        {
            ImplementationSandboxProofStatusText = "Complete approval queue id, display name, workflow kind, approval role/count, segregation policy, and evidence requirement before retaining implementation sandbox proof.";
            StatusText = ImplementationSandboxProofStatusText;
            return;
        }

        var dimensionMappingConfigurations = BuildDimensionMappingConfigurations();
        if (DimensionMappingStudioConfigured && dimensionMappingConfigurations.Count == 0)
        {
            ImplementationSandboxProofStatusText = "Complete dimension mapping id, display name, provider id, Meridian/provider dimensions, and evidence requirement before retaining implementation sandbox proof.";
            StatusText = ImplementationSandboxProofStatusText;
            return;
        }

        try
        {
            var correlationId = $"wpf-accounting-sandbox-proof-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
            var profile = new AccountingTenantAdministrationProfileDto(
                tenantId,
                companyId,
                TenantScopeConfigured,
                AdminRoleProfileConfigured,
                ScopedAccessPoliciesConfigured,
                ReportingGroupsConfigured,
                AccountingAdminSurfaceConfigured,
                DateTimeOffset.UtcNow,
                DefaultLifecycleActor,
                evidence,
                correlationId,
                WpfAccountingAdminSurfaceConfigured: WpfAccountingAdminSurfaceConfigured,
                ChartAdministrationStudioConfigured: ChartAdministrationStudioConfigured,
                RuleTestPromotionStudioConfigured: RuleTestPromotionStudioConfigured,
                CloseSetupStudioConfigured: CloseSetupStudioConfigured,
                ProviderMappingStudioConfigured: ProviderMappingStudioConfigured,
                TenantCompanyReportGroupSetupStudioConfigured: TenantCompanyReportGroupSetupStudioConfigured,
                AuditReviewToolingConfigured: AuditReviewToolingConfigured,
                BulkImportExportSafeguardsConfigured: BulkImportExportSafeguardsConfigured,
                PerformanceValidationConfigured: PerformanceValidationConfigured,
                DisasterRecoveryRunbookConfigured: DisasterRecoveryRunbookConfigured,
                LedgerBookAdministrationStudioConfigured: LedgerBookAdministrationStudioConfigured,
                PostingRuleAuthoringStudioConfigured: PostingRuleAuthoringStudioConfigured,
                ApprovalQueueStudioConfigured: ApprovalQueueStudioConfigured,
                DimensionMappingStudioConfigured: DimensionMappingStudioConfigured,
                ImplementationSandboxConfigured: true,
                ApprovalQueueConfigurations: approvalQueueConfigurations,
                DimensionMappingConfigurations: dimensionMappingConfigurations);

            var saved = await _tenantAdministrationProfileStore.UpsertAsync(
                new AccountingTenantAdministrationProfileUpsertRequestDto(
                    profile,
                    DefaultLifecycleActor,
                    correlationId,
                    evidence),
                ct).ConfigureAwait(false);

            ApplyTenantAdministrationProfile(saved, "Tenant administration setup profile saved; production readiness refreshed from retained controls.");
            ImplementationSandboxProofStatusText = "Implementation sandbox proof retained; production readiness refreshed from fixture and ledger-book validation evidence.";
            await RefreshProductionReadinessAsync(ct).ConfigureAwait(false);
            StatusText = ImplementationSandboxProofStatusText;
        }
        catch (Exception ex)
        {
            ImplementationSandboxProofStatusText = $"Implementation sandbox proof save failed: {ex.Message}";
            StatusText = ImplementationSandboxProofStatusText;
        }
    }

    public async Task SaveProductionCertificationProfileAsync(CancellationToken ct = default)
    {
        if (_activeFundProfile is null)
        {
            ProductionCertificationProfileStatusText = "Select a fund-linked context before saving production certification controls.";
            StatusText = ProductionCertificationProfileStatusText;
            return;
        }

        if (_productionCertificationProfileStore is null)
        {
            ProductionCertificationProfileStatusText = "Production certification profile store is not registered for this desktop session.";
            StatusText = ProductionCertificationProfileStatusText;
            return;
        }

        var retainedEvidence = NormalizeTenantAdministrationEvidence(ProductionCertificationEvidenceText);
        if (retainedEvidence.Count == 0)
        {
            ProductionCertificationProfileStatusText = "Retained evidence is required before saving production certification controls.";
            StatusText = ProductionCertificationProfileStatusText;
            NotifyProductionCertificationSaveStateChanged();
            return;
        }

        try
        {
            var correlationId = $"wpf-accounting-production-certification-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
            var workflowCertificationArtifacts = BuildWorkflowCertificationArtifacts(retainedEvidence, correlationId);
            var dimensionalCertificationArtifacts = BuildDimensionalCertificationArtifacts(retainedEvidence, correlationId);
            var tenantAdminCertificationArtifacts = BuildTenantAdminCertificationArtifacts(retainedEvidence, correlationId);
            var profile = new AccountingProductionCertificationProfileDto(
                _activeFundProfile.FundProfileId,
                _configuration?.LedgerBookId,
                PostingRulesLedgerBookNativeCertified,
                JournalLifecycleLedgerBookNativeCertified,
                CloseReportingLedgerBookNativeCertified,
                ExternalGlLedgerBookNativeCertified,
                PeriodReportDimensionQueriesCertified,
                CrossPeriodReportDimensionQueriesCertified,
                JournalQueryDimensionFiltersCertified,
                ExternalExportDimensionMappingCertified,
                DateTimeOffset.UtcNow,
                DefaultLifecycleActor,
                retainedEvidence,
                correlationId,
                ResolveTenantAdministrationTenantId(),
                ResolveTenantAdministrationCompanyId(),
                ReconciliationLedgerBookNativeCertified,
                DirectLendingLedgerBookNativeCertified,
                StrategyLedgerReadLedgerBookNativeCertified,
                LedgerLineDimensionsPersistedCertified,
                TrialBalanceDimensionFiltersCertified,
                ReportPackageDimensionProvenanceCertified,
                ClosePlanConfigurationLedgerBookNativeCertified,
                WorkflowCertificationArtifacts: workflowCertificationArtifacts,
                DimensionalCertificationArtifacts: dimensionalCertificationArtifacts,
                TenantAdminCertificationArtifacts: tenantAdminCertificationArtifacts);

            var saved = await _productionCertificationProfileStore.UpsertAsync(
                new AccountingProductionCertificationProfileUpsertRequestDto(
                    profile,
                    DefaultLifecycleActor,
                    correlationId,
                    retainedEvidence),
                ct).ConfigureAwait(false);

            ApplyProductionCertificationProfile(saved, "Production certification profile saved; readiness refreshed from retained book and dimension controls.");
            await RefreshProductionReadinessAsync(ct).ConfigureAwait(false);
            StatusText = ProductionCertificationProfileStatusText;
        }
        catch (Exception ex)
        {
            ProductionCertificationProfileStatusText = $"Production certification profile save failed: {ex.Message}";
            StatusText = ProductionCertificationProfileStatusText;
        }
    }

    public async Task SaveManualJournalDraftAsync(CancellationToken ct = default)
    {
        if (_activeFundProfile is null)
        {
            ManualJournalStatusText = "Select a fund-linked context before saving a manual journal draft.";
            return;
        }

        try
        {
            var saved = await _manualJournalEntryWorkbenchService.SaveDraftAsync(
                new SaveManualJournalEntryDraftRequest(
                    BuildManualJournalDraft(),
                    DefaultActor,
                    CorrelationId: "wpf-manual-je-save",
                    EvidenceLinks: NormalizeEvidenceLink(DraftEvidenceLink),
                    LedgerBookId: _configuration?.LedgerBookId),
                ct).ConfigureAwait(false);
            _selectedDraft = saved;
            await LoadManualJournalWorkbenchAsync(ct).ConfigureAwait(false);
            ManualJournalStatusText = $"Draft {saved.JournalEntryId:D} saved as {saved.Status}; {saved.ValidationIssues.Count} validation issue(s).";
            RaisePropertyChanged(nameof(CanSubmitManualJournal));
            RaiseManualJournalDraftComputedProperties();
        }
        catch (Exception ex)
        {
            ManualJournalStatusText = $"Manual journal draft could not be saved: {ex.Message}";
        }
    }

    public async Task ValidateManualJournalDraftAsync(CancellationToken ct = default)
    {
        if (_activeFundProfile is null)
        {
            ManualJournalStatusText = "Select a fund-linked context before validating a manual journal draft.";
            return;
        }

        try
        {
            var validated = await _manualJournalEntryWorkbenchService.ValidateDraftAsync(
                new ValidateManualJournalEntryDraftRequest(
                    BuildManualJournalDraft(),
                    DefaultActor,
                    CorrelationId: "wpf-manual-je-validate",
                    LedgerBookId: _configuration?.LedgerBookId),
                ct).ConfigureAwait(false);
            ManualJournalStatusText = BuildManualJournalValidationText(validated);
            ApplyManualJournalValidationRows(validated.ValidationIssues);
        }
        catch (Exception ex)
        {
            ManualJournalStatusText = $"Manual journal validation failed: {ex.Message}";
        }
    }

    public async Task SubmitManualJournalDraftAsync(CancellationToken ct = default)
    {
        if (_selectedDraft is null)
        {
            ManualJournalStatusText = "Save the manual journal draft before submitting approval.";
            return;
        }

        try
        {
            var submitted = await _manualJournalEntryWorkbenchService.SubmitApprovalAsync(
                new SubmitManualJournalEntryApprovalRequest(
                    _selectedDraft.JournalEntryId,
                    _selectedDraft.FundProfileId,
                    DefaultActor,
                    _selectedDraft.Version,
                    Notes: "Submitted from WPF Accounting Configure.",
                    CorrelationId: "wpf-manual-je-submit",
                    EvidenceLinks: NormalizeEvidenceLink(DraftEvidenceLink),
                    LedgerBookId: _selectedDraft.LedgerBookId),
                ct).ConfigureAwait(false);
            _selectedDraft = submitted;
            await LoadManualJournalWorkbenchAsync(ct).ConfigureAwait(false);
            ManualJournalStatusText = $"Draft submitted for approval as {submitted.ApprovalId}.";
            RaisePropertyChanged(nameof(CanSubmitManualJournal));
            RaiseManualJournalDraftComputedProperties();
        }
        catch (Exception ex)
        {
            ManualJournalStatusText = $"Manual journal approval submission is blocked: {ex.Message}";
        }
    }

    public async Task ApplyManualJournalLifecycleActionAsync(
        JournalEntryLifecycleActionDto action,
        CancellationToken ct = default)
    {
        if (_manualJournalEntryLifecycleService is null)
        {
            ManualJournalLifecycleStatusText = "Manual journal lifecycle service is not registered for this desktop session.";
            return;
        }

        if (_selectedDraft is null)
        {
            ManualJournalLifecycleStatusText = "Save or select a manual journal draft before applying lifecycle actions.";
            return;
        }

        try
        {
            var request = BuildManualJournalLifecycleRequest(_selectedDraft, action);
            var result = await _manualJournalEntryLifecycleService
                .ApplyLifecycleActionAsync(request, ct)
                .ConfigureAwait(false);
            _selectedDraft = result.JournalEntry;
            await LoadManualJournalWorkbenchAsync(ct).ConfigureAwait(false);
            ApplyManualJournalLifecycleRows(result);
            ManualJournalLifecycleStatusText =
                $"{action} moved journal {_selectedDraft.JournalEntryId:D} from {result.Transition.FromStatus} to {result.Transition.ToStatus}; {result.GeneratedJournalEntries.Count} correction draft(s) generated.";
            ManualJournalStatusText = $"Manual journal lifecycle action {action} completed as {_selectedDraft.Status}.";
            RaisePropertyChanged(nameof(CanSubmitManualJournal));
            RaiseManualJournalDraftComputedProperties();
        }
        catch (Exception ex)
        {
            ManualJournalLifecycleStatusText = $"Manual journal lifecycle action {action} is blocked: {ex.Message}";
            ManualJournalLifecycleRows.ReplaceWith(
            [
                new AccountingWorkbenchRow(
                    action.ToString(),
                    "Blocked",
                    ex.Message,
                    "No lifecycle transition was written; approval, posting, correction, and close-lock controls remain enforced.",
                    _selectedDraft.JournalEntryId.ToString("D"))
            ]);
        }
    }

    public async Task SaveExternalGlMappingProfileAsync(CancellationToken ct = default)
    {
        if (_activeFundProfile is null)
        {
            ExternalGlMappingProfileStatusText = "Select a fund-linked context before saving external GL mapping profiles.";
            StatusText = ExternalGlMappingProfileStatusText;
            return;
        }

        if (_accountingSystemIntegrationService is null)
        {
            ExternalGlMappingProfileStatusText = "External GL mapping profile service is not registered for this desktop session.";
            StatusText = ExternalGlMappingProfileStatusText;
            return;
        }

        var providerId = ExternalGlMappingProviderId.Trim();
        var profileId = ExternalGlMappingProfileId.Trim();
        var displayName = ExternalGlMappingDisplayName.Trim();
        var accountMappings = ParseExternalGlAccountMappings(ExternalGlMappingAccountMappingsText);
        var meridianDimensions = ParseLedgerDimensionSet(
            ExternalGlMappingMeridianDimensionsText,
            BuildDefaultMeridianExternalGlDimensions(providerId, profileId));
        var externalDimensions = ParseLedgerDimensionSet(
            ExternalGlMappingExternalDimensionsText,
            BuildDefaultProviderExternalGlDimensions());
        var retainedEvidence = NormalizeTenantAdministrationEvidence(ExternalGlMappingEvidenceText);
        if (string.IsNullOrWhiteSpace(providerId) ||
            string.IsNullOrWhiteSpace(profileId) ||
            string.IsNullOrWhiteSpace(displayName) ||
            accountMappings.Count == 0 ||
            retainedEvidence.Count == 0)
        {
            ExternalGlMappingProfileStatusText = "Provider, profile id, display name, account mappings, and retained mapping evidence are required before saving.";
            StatusText = ExternalGlMappingProfileStatusText;
            NotifyExternalGlMappingProfileSaveStateChanged();
            return;
        }

        try
        {
            var profile = new ExternalGlMappingProfileDto(
                profileId,
                providerId,
                displayName,
                DateTimeOffset.UtcNow,
                [BuildExternalGlDimensionMappingProfile(providerId, profileId, meridianDimensions, externalDimensions, ExternalGlMappingProfileCertified)],
                accountMappings,
                ExternalGlMappingProfileCertified ? AccountingCertificationStateDto.Certified : AccountingCertificationStateDto.ReadyForReview);
            var evidence = WithExternalGlMappingProfileEvidence(
                retainedEvidence,
                providerId,
                _activeFundProfile.FundProfileId,
                profileId,
                _configuration?.LedgerBookId);
            var saved = await _accountingSystemIntegrationService.UpsertMappingProfileAsync(
                new AccountingSystemMappingProfileUpsertRequestDto(
                    profile,
                    DefaultLifecycleActor,
                    providerId,
                    _activeFundProfile.FundProfileId,
                    _configuration?.LedgerBookId,
                    $"wpf-external-gl-mapping-profile-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
                    evidence,
                    ResolveTenantAdministrationTenantId(),
                    ResolveTenantAdministrationCompanyId()),
                ct).ConfigureAwait(false);
            ApplyExternalGlMappingProfileDraft(saved);
            ExternalGlMappingProfileStatusText = $"External GL mapping profile {saved.ProfileId} saved as {saved.CertificationState}; readiness refreshed from retained provider mapping.";
            StatusText = ExternalGlMappingProfileStatusText;
            await RefreshExternalGlAsync(ct).ConfigureAwait(false);
            await RefreshProductionReadinessAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            ExternalGlMappingProfileStatusText = $"External GL mapping profile save failed: {ex.Message}";
            StatusText = ExternalGlMappingProfileStatusText;
        }
    }

    public async Task SaveChartAccountAsync(CancellationToken ct = default)
    {
        if (_activeFundProfile is null)
        {
            ChartAccountSetupStatusText = "Select a fund-linked context before saving chart accounts.";
            StatusText = ChartAccountSetupStatusText;
            return;
        }

        var nodeId = ChartAccountNodeId.Trim();
        var path = ChartAccountPath.Trim();
        var accountName = ChartAccountName.Trim();
        var accountType = ChartAccountType.Trim();
        var evidence = NormalizeTenantAdministrationEvidence(ChartAccountEvidenceText);
        if (string.IsNullOrWhiteSpace(nodeId) ||
            string.IsNullOrWhiteSpace(path) ||
            string.IsNullOrWhiteSpace(accountName) ||
            string.IsNullOrWhiteSpace(accountType) ||
            evidence.Count == 0)
        {
            ChartAccountSetupStatusText = "Node id, account path, account name, account type, and retained evidence are required before saving chart accounts.";
            StatusText = ChartAccountSetupStatusText;
            NotifyChartAccountSaveStateChanged();
            return;
        }

        try
        {
            _configuration ??= await _configurationService.GetWorkspaceAsync(_activeFundProfile.FundProfileId, ct: ct).ConfigureAwait(false);
            var ledgerBookId = ResolveActiveLedgerBook(_configuration)?.LedgerBookId ?? _configuration.LedgerBookId;
            var node = new ChartOfAccountsNodeDto(
                nodeId,
                path,
                accountName,
                accountType,
                string.IsNullOrWhiteSpace(ChartAccountParentPath) ? null : ChartAccountParentPath.Trim(),
                FinancialAccountId: string.IsNullOrWhiteSpace(ChartAccountFinancialAccountId) ? null : ChartAccountFinancialAccountId.Trim());
            _configuration = await _configurationService.UpsertChartNodeAsync(
                new UpsertChartOfAccountsNodeRequest(
                    _activeFundProfile.FundProfileId,
                    node,
                    DefaultActor,
                    CorrelationId: $"wpf-accounting-chart-account-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
                    EvidenceLinks: evidence,
                    LedgerBookId: ledgerBookId),
                ct).ConfigureAwait(false);

            ApplyConfiguration(_configuration);
            ApplyNextChartAccountDraft(_configuration);
            ChartAccountSetupStatusText = $"Chart account {path} saved through shared Accounting Configure.";
            StatusText = ChartAccountSetupStatusText;
            await RefreshProductionReadinessAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            ChartAccountSetupStatusText = $"Chart account setup failed: {ex.Message}";
            StatusText = ChartAccountSetupStatusText;
        }
    }

    public async Task SavePostingRuleDraftAsync(CancellationToken ct = default)
    {
        if (_activeFundProfile is null)
        {
            PostingRuleDraftStatusText = "Select a fund-linked context before saving posting-rule drafts.";
            StatusText = PostingRuleDraftStatusText;
            return;
        }

        var ruleId = PostingRuleDraftRuleId.Trim();
        var displayName = PostingRuleDraftDisplayName.Trim();
        var sourceEventType = PostingRuleDraftSourceEventType.Trim();
        var templateId = PostingRuleDraftTemplateId.Trim();
        var evidence = NormalizeTenantAdministrationEvidence(PostingRuleDraftEvidenceText);
        if (string.IsNullOrWhiteSpace(ruleId) ||
            string.IsNullOrWhiteSpace(displayName) ||
            string.IsNullOrWhiteSpace(sourceEventType) ||
            string.IsNullOrWhiteSpace(templateId) ||
            evidence.Count == 0)
        {
            PostingRuleDraftStatusText = "Rule id, display name, source event, template id, and retained evidence are required before saving posting-rule drafts.";
            StatusText = PostingRuleDraftStatusText;
            NotifyPostingRuleDraftSaveStateChanged();
            return;
        }

        try
        {
            _configuration ??= await _configurationService.GetWorkspaceAsync(_activeFundProfile.FundProfileId, ct: ct).ConfigureAwait(false);
            var ledgerBookId = ResolveActiveLedgerBook(_configuration)?.LedgerBookId ?? _configuration.LedgerBookId;
            if (!_configuration.JournalTemplates.Any(template =>
                    string.Equals(template.TemplateId, templateId, StringComparison.OrdinalIgnoreCase) &&
                    !template.IsArchived))
            {
                PostingRuleDraftStatusText = $"Posting-rule draft blocked: template {templateId} is not available in the loaded Accounting Configure workspace.";
                StatusText = PostingRuleDraftStatusText;
                return;
            }

            var rule = new PostingRuleDto(
                ruleId,
                displayName,
                sourceEventType,
                templateId,
                RuleVersion: "draft",
                Description: string.IsNullOrWhiteSpace(PostingRuleDraftDescription)
                    ? null
                    : PostingRuleDraftDescription.Trim(),
                Priority: PostingRuleDraftPriority,
                Scope: new LedgerDimensionSetDto(
                    FundId: _activeFundProfile.FundProfileId,
                    BookId: ledgerBookId?.ToString("D", CultureInfo.InvariantCulture)),
                RequiresPromotionApproval: true);

            _configuration = await _configurationService.UpsertPostingRuleAsync(
                new UpsertPostingRuleRequest(
                    _activeFundProfile.FundProfileId,
                    rule,
                    DefaultActor,
                    CorrelationId: $"wpf-accounting-posting-rule-draft-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
                    EvidenceLinks: evidence,
                    LedgerBookId: ledgerBookId),
                ct).ConfigureAwait(false);

            ApplyConfiguration(_configuration);
            ApplyNextPostingRuleDraft(_configuration, ruleId);
            PostingRuleDraftStatusText = $"Posting-rule draft {ruleId} saved through shared Accounting Configure.";
            StatusText = PostingRuleDraftStatusText;
            await RefreshProductionReadinessAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            PostingRuleDraftStatusText = $"Posting-rule draft save failed: {ex.Message}";
            StatusText = PostingRuleDraftStatusText;
        }
    }

    public async Task RefreshExternalGlAsync(CancellationToken ct = default)
    {
        ProviderRows.Clear();
        ExternalGlMappingProfileRows.Clear();
        ExternalGlEvidencePackageRows.Clear();
        ExternalGlRows.Clear();
        if (_accountingSystemIntegrationService is null)
        {
            ExternalGlStatusText = "External GL evidence service is not registered for this desktop session.";
            PostingPostureText = "Posting/export remains unavailable.";
            return;
        }

        try
        {
            var providers = await _accountingSystemIntegrationService.ListProvidersAsync(ct).ConfigureAwait(false);
            foreach (var provider in providers)
            {
                var requirementSummary = provider.MappingRequirements.Count == 0
                    ? provider.StatusDetail
                    : $"{provider.StatusDetail} Requirements: {string.Join(", ", provider.MappingRequirements.Take(3).Select(static requirement => requirement.Label))}.";
                ProviderRows.Add(new AccountingWorkbenchRow(
                    provider.DisplayName,
                    provider.State.ToString(),
                    requirementSummary,
                    provider.ProviderId));
            }

            if (_activeFundProfile is null)
            {
                ExternalGlStatusText = "Select a fund-linked context before importing external GL evidence.";
                PostingPostureText = "Posting/export remains disabled until Meridian-owned ledger entries are approved.";
                return;
            }

            var mappingProfiles = await _accountingSystemIntegrationService
                .ListMappingProfilesAsync(
                    fundProfileId: _activeFundProfile.FundProfileId,
                    ledgerBookId: _configuration?.LedgerBookId,
                    ct: ct,
                    tenantId: ResolveTenantAdministrationTenantId(),
                    companyId: ResolveTenantAdministrationCompanyId())
                .ConfigureAwait(false);
            foreach (var profile in mappingProfiles.Take(20))
            {
                ExternalGlMappingProfileRows.Add(new AccountingWorkbenchRow(
                    profile.DisplayName,
                    profile.CertificationState.ToString(),
                    $"{profile.ProviderId}; {profile.AccountMappings.Count} account mapping(s), {profile.DimensionMappings.Count} dimension mapping(s).",
                    profile.ProfileId,
                    profile.ProfileId));
            }

            if (mappingProfiles.Count > 0)
            {
                ApplyExternalGlMappingProfileDraft(mappingProfiles[0]);
            }
            else
            {
                ApplyDefaultExternalGlMappingDimensionDraft();
            }

            var reconciliation = await _accountingSystemIntegrationService
                .ReconcileLatestAsync(fundProfileId: _activeFundProfile.FundProfileId, ledgerBookId: _configuration?.LedgerBookId, ct: ct)
                .ConfigureAwait(false);

            foreach (var package in reconciliation.EvidencePackages.Take(20))
            {
                ExternalGlEvidencePackageRows.Add(new AccountingWorkbenchRow(
                    package.Label,
                    package.Status.ToString(),
                    package.RequiredActions.Count > 0
                        ? string.Join(" ", package.RequiredActions)
                        : $"{package.EvidenceReferenceCount} retained evidence reference(s).",
                    package.EvidenceReferences.FirstOrDefault() ?? package.PackageId,
                    package.PackageId));
            }

            foreach (var row in reconciliation.Rows.Take(50))
            {
                ExternalGlRows.Add(new ExternalGlEvidenceRow(
                    row.AccountCode,
                    row.AccountName,
                    row.Status.ToString(),
                    row.ExternalDebit,
                    row.ExternalCredit,
                    row.MeridianDebit,
                    row.MeridianCredit,
                    row.Variance,
                    row.Detail,
                    row.EvidenceRef ?? string.Empty));
            }

            ExternalGlStatusText = $"{reconciliation.ProviderId} evidence: {reconciliation.MatchedCount} matched, {reconciliation.BreakCount} review row(s).";
            PostingPostureText = reconciliation.PostingEnabled
                ? "External GL posting/export is enabled by connection policy."
                : reconciliation.PostingDisabledReason;
        }
        catch (Exception ex)
        {
            ExternalGlStatusText = $"External GL evidence could not be loaded: {ex.Message}";
            PostingPostureText = "Posting/export remains disabled while external GL evidence is unavailable.";
        }
    }

    public async Task CreateFundScopedPolicyAsync(CancellationToken ct = default)
    {
        if (_accountingPolicyService is null)
        {
            PolicyPostureText = "Accounting policy service is not registered for this desktop session.";
            return;
        }

        if (_activeFundProfile is null)
        {
            PolicyPostureText = "Select a fund-linked context before creating a fund-scoped accounting policy.";
            return;
        }

        try
        {
            var policyId = $"{_activeFundProfile.FundProfileId}-{SelectedAccountingBasis.ToString().ToLowerInvariant()}-desktop-v1";
            await _accountingPolicyService.CreatePolicyAsync(
                new CreateAccountingPolicyRequest(
                    SelectedAccountingBasis,
                    policyId,
                    "v1",
                    $"{_activeFundProfile.DisplayName} {SelectedAccountingBasis} desktop policy",
                    DateOnly.FromDateTime(DateTime.UtcNow),
                    IsDefault: true,
                    RulesJson: "{\"source\":\"wpf-accounting-configure\"}",
                    FundProfileId: _activeFundProfile.FundProfileId),
                ct).ConfigureAwait(false);
            await LoadPolicyRowsAsync(ct).ConfigureAwait(false);
            PolicyPostureText = $"Fund-scoped {SelectedAccountingBasis} policy is available for {_activeFundProfile.DisplayName}.";
        }
        catch (Exception ex)
        {
            PolicyPostureText = $"Fund-scoped accounting policy could not be created: {ex.Message}";
        }
    }

    public async Task ExecuteRulesStudioTestsAsync(CancellationToken ct = default)
    {
        RulesStudioActionRows.Clear();
        if (_activeFundProfile is null || _configuration is null)
        {
            RulesStudioActionText = "Load a fund-linked accounting configuration before executing rule tests.";
            return;
        }

        try
        {
            var result = await _configurationService.ExecuteRuleTestCasesAsync(
                new ExecuteAccountingRuleTestCasesRequestDto(
                    _activeFundProfile.FundProfileId,
                    DefaultActor,
                    TestCases: [],
                    LedgerBookId: _configuration.LedgerBookId),
                ct).ConfigureAwait(false);

            ApplyRulesStudioTestResult(result);
            RulesStudioActionText =
                $"Executed {result.Results.Count} saved rule test case(s): {result.PassedCount} passed, {result.FailedCount} failed.";
        }
        catch (Exception ex)
        {
            RulesStudioActionText = $"Rules Studio test execution is blocked: {ex.Message}";
            RulesStudioActionRows.ReplaceWith(
            [
                new AccountingWorkbenchRow(
                    "Run saved tests",
                    "Blocked",
                    ex.Message,
                    "No rule state was changed.",
                    _configuration.LedgerBookId?.ToString("D", CultureInfo.InvariantCulture) ?? "fund")
            ]);
        }
    }

    public async Task ApproveRulesStudioPromotionAsync(CancellationToken ct = default)
    {
        if (_activeFundProfile is null || _configuration?.RulesStudio is null)
        {
            RulesStudioActionText = "Load a fund-linked Rules Studio workspace before approving promotion.";
            return;
        }

        var queueItem = _configuration.RulesStudio.PromotionQueue
            .OrderBy(static item => item.CriticalIssueCount)
            .ThenBy(static item => item.MissingRegressionEvidenceCount)
            .ThenBy(static item => item.RuleId, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (queueItem is null)
        {
            RulesStudioActionText = "No posting-rule promotions are waiting for approval.";
            RulesStudioActionRows.ReplaceWith(
            [
                new AccountingWorkbenchRow(
                    "Promotion approval",
                    "No queue",
                    "The shared Rules Studio promotion queue is empty.",
                    "Seed or edit a promotion-gated rule before approval.",
                    string.Empty)
            ]);
            return;
        }

        try
        {
            var approvalId = string.IsNullOrWhiteSpace(queueItem.ApprovalId)
                ? $"wpf-approval-{queueItem.RuleId}-{queueItem.RuleVersion}"
                : queueItem.ApprovalId;
            var evidenceLink = $"evidence://accounting/rules/{queueItem.RuleId}/{queueItem.RuleVersion}/desktop-approval/{approvalId}";
            _configuration = await _configurationService.ApprovePostingRulePromotionAsync(
                new ApprovePostingRulePromotionRequest(
                    _activeFundProfile.FundProfileId,
                    queueItem.RuleId,
                    queueItem.RuleVersion,
                    DefaultActor,
                    approvalId,
                    "Desktop controller approved the posting rule after Rules Studio regression review.",
                    EvidenceLinks: [evidenceLink],
                    RequestedBy: queueItem.RequestedBy,
                    RequestedAtUtc: queueItem.RequestedAtUtc,
                    CorrelationId: "wpf-rules-studio-promotion-approve",
                    LedgerBookId: _configuration.LedgerBookId),
                ct).ConfigureAwait(false);

            ApplyConfiguration(_configuration);
            RulesStudioActionText = $"Approved posting-rule promotion {queueItem.RuleId}/{queueItem.RuleVersion}.";
            RulesStudioActionRows.ReplaceWith(
            [
                new AccountingWorkbenchRow(
                    queueItem.RuleId,
                    "Approved",
                    $"{queueItem.DisplayName} | {queueItem.RuleVersion}",
                    evidenceLink,
                    approvalId)
            ]);
        }
        catch (Exception ex)
        {
            RulesStudioActionText = $"Posting-rule promotion approval is blocked: {ex.Message}";
            RulesStudioActionRows.ReplaceWith(
            [
                new AccountingWorkbenchRow(
                    queueItem.RuleId,
                    "Blocked",
                    ex.Message,
                    queueItem.SuggestedAction,
                    queueItem.ApprovalId ?? string.Empty)
            ]);
        }
    }

    private void ApplyRulesStudioTestResult(AccountingRuleTestSuiteResultDto result)
    {
        var rows = new List<AccountingWorkbenchRow>
        {
            new(
                "Regression suite",
                $"{result.PassedCount}/{result.TotalCount} passed",
                $"Executed by {result.Actor} at {result.ExecutedAtUtc:yyyy-MM-dd HH:mm:ss} UTC.",
                result.FailedCount == 0
                    ? "All saved rule tests passed."
                    : $"{result.FailedCount} saved rule test(s) failed and block promotion or activation.",
                result.LedgerBookId?.ToString("D", CultureInfo.InvariantCulture) ?? "fund")
        };

        rows.AddRange(result.Results.Select(testCase =>
        {
            var issueText = testCase.AssertionIssues.Count == 0
                ? "No assertion issues."
                : string.Join("; ", testCase.AssertionIssues.Select(static issue => issue.Code).Take(4));
            var selectedMatch = testCase.DryRunResult.RuleMatches.FirstOrDefault(match =>
                string.Equals(match.RuleId, testCase.DryRunResult.SelectedRuleId, StringComparison.OrdinalIgnoreCase));
            var selectedRule = testCase.DryRunResult.SelectedRuleId is null
                ? "No selected rule"
                : $"{testCase.DryRunResult.SelectedRuleId}/{selectedMatch?.RuleVersion ?? "unknown"}";
            return new AccountingWorkbenchRow(
                testCase.TestCaseId,
                testCase.Passed ? "Passed" : "Failed",
                $"{testCase.DisplayName} | {selectedRule}",
                issueText,
                $"{testCase.DryRunResult.FundProfileId}:{testCase.DryRunResult.SourceEventType}");
        }));

        RulesStudioActionRows.ReplaceWith(rows);
    }

    public async Task BuildPostingCandidateAsync(CancellationToken ct = default)
    {
        PostingCandidateRows.Clear();
        if (_accountingPostingCandidateService is null)
        {
            PostingCandidateStatusText = "Posting-rule candidate service is not registered for this desktop session.";
            PostingCandidateDetailText = "The desktop surface cannot build governed draft candidates until the shared service is registered.";
            return;
        }

        if (_activeFundProfile is null || _configuration is null)
        {
            PostingCandidateStatusText = "Select and load a fund-linked accounting configuration before building a posting candidate.";
            PostingCandidateDetailText = "Candidate generation requires chart accounts, posting rules, dimensions, and retained evidence.";
            return;
        }

        var rule = ResolvePostingCandidateRule(_configuration);
        if (rule is null)
        {
            PostingCandidateStatusText = "No active posting rule is available for the selected manual journal entry type.";
            PostingCandidateDetailText = "Seed or configure posting rules before building a governed draft candidate.";
            return;
        }

        var ledgerBook = ResolveActiveLedgerBook(_configuration);
        var currency = string.IsNullOrWhiteSpace(DraftCurrency)
            ? _activeFundProfile.BaseCurrency
            : DraftCurrency.Trim().ToUpperInvariant();
        var effectiveDate = DateOnly.FromDateTime(DateTime.UtcNow);
        var correlationId = Guid.NewGuid();
        var sourceEventId = Guid.NewGuid();
        var evidenceLinks = BuildPostingCandidateEvidenceLinks(rule);

        try
        {
            var result = await _accountingPostingCandidateService.BuildCandidateAsync(
                    new PostingRuleJournalCandidateRequestDto(
                        _activeFundProfile.FundProfileId,
                        rule.SourceEventType,
                        Math.Abs(DraftAmount),
                        currency,
                        effectiveDate,
                        DefaultActor,
                        AggregateId: Guid.NewGuid(),
                        PeriodId: Guid.NewGuid(),
                        AccountingTimestamp: DateTimeOffset.UtcNow,
                        Description: $"{rule.DisplayName} governed journal draft candidate",
                        AccountingBasis: ledgerBook?.AccountingBasis ?? SelectedAccountingBasis,
                        LedgerBookId: _configuration.LedgerBookId ?? ledgerBook?.LedgerBookId,
                        Dimensions: BuildPostingCandidateDimensions(rule),
                        CounterpartyId: rule.Scope?.CounterpartyId,
                        InstrumentSymbol: rule.Scope?.InstrumentId?.ToString("D"),
                        CorrelationId: correlationId,
                        SourceEventId: sourceEventId,
                        PolicyId: ledgerBook?.AccountingPolicyId,
                        TreatmentKind: MapPostingCandidateTreatment(SelectedEntryType),
                        PostingKind: LedgerPostingKindDto.Originating,
                        TreasuryContext: BuildManualJournalTreasuryContext(
                            _activeFundProfile.FundProfileId,
                            SelectedEntryType,
                            effectiveDate,
                            sourceEventId),
                        EvidenceLinks: evidenceLinks),
                    ct)
                .ConfigureAwait(false);

            ApplyPostingCandidateResult(rule, result, correlationId, sourceEventId);
        }
        catch (Exception ex)
        {
            PostingCandidateStatusText = $"Posting-rule candidate generation failed: {ex.Message}";
            PostingCandidateDetailText = "No journal draft candidate was created; ledger posting remains unavailable.";
        }
    }

    private void ApplyLockedState()
    {
        _configuration = null;
        _selectedDraft = null;
        ActiveFundText = "No fund selected";
        ConfigurationStatusText = "Locked";
        ConfigurationDetailText = "Select a fund-linked context before configuring chart accounts, templates, rules, or manual journal entries.";
        RulesStudioStatusText = "Locked";
        RulesStudioDetailText = "Select a fund-linked context before reviewing effective-dated rules, tests, and promotion approvals.";
        ManualJournalStatusText = "Locked until a fund context is selected.";
        _selectedManualJournalPeriod = null;
        CapitalAccountWorkbenchStatusText = "Locked until a fund context is selected.";
        ExternalGlStatusText = "Locked until a fund context is selected.";
        PostingPostureText = "Posting/export remains disabled.";
        CloseReadinessText = "Locked";
        CloseDetailText = "Close readiness requires a fund context.";
        EvidencePostureText = "Locked";
        ReconciliationPostureText = "Locked";
        PolicyPostureText = "Locked";
        PostingCandidateStatusText = "Locked until a fund context is selected.";
        PostingCandidateDetailText = "Posting candidates require a fund-linked configuration and retained evidence.";
        ManualJournalLifecycleStatusText = "Locked until a fund context is selected.";
        LedgerBookSetupStatusText = "Locked until a fund context is selected.";
        LedgerBookAdministrationText = "Locked until a fund context is selected.";
        ProductionReadinessStatusText = "Locked";
        ProductionReadinessDetailText = "Select a fund-linked context before assessing accounting production readiness.";
        ProductionReadinessScoreText = "No score";
        ProductionReadinessLedgerBookText = "Locked until a fund context is selected.";
        ProductionReadinessExternalGlText = "Locked until a fund context is selected.";
        ProductionReadinessDimensionalReportingText = "Locked until a fund context is selected.";
        ProductionReadinessTenantAdminText = "Locked until a fund context is selected.";
        ProductionCertificationProfileStatusText = "Locked until a fund context is selected.";
        ProductionCertificationProfileScopeText = "Fund and ledger-book scope require a fund context.";
        ProductionCertificationProfileUpdatedText = "No retained production certification profile loaded.";
        ProductionCertificationEvidenceText = string.Empty;
        PostingRulesLedgerBookNativeCertified = false;
        JournalLifecycleLedgerBookNativeCertified = false;
        CloseReportingLedgerBookNativeCertified = false;
        ClosePlanConfigurationLedgerBookNativeCertified = false;
        ExternalGlLedgerBookNativeCertified = false;
        ReconciliationLedgerBookNativeCertified = false;
        DirectLendingLedgerBookNativeCertified = false;
        StrategyLedgerReadLedgerBookNativeCertified = false;
        PeriodReportDimensionQueriesCertified = false;
        CrossPeriodReportDimensionQueriesCertified = false;
        JournalQueryDimensionFiltersCertified = false;
        ExternalExportDimensionMappingCertified = false;
        LedgerLineDimensionsPersistedCertified = false;
        TrialBalanceDimensionFiltersCertified = false;
        ReportPackageDimensionProvenanceCertified = false;
        TenantAdministrationProfileStatusText = "Locked until a fund context is selected.";
        TenantAdministrationProfileScopeText = "Tenant and company scope require a fund context.";
        TenantAdministrationProfileUpdatedText = "No retained tenant administration setup profile loaded.";
        TenantAdministrationEvidenceText = string.Empty;
        TenantScopeConfigured = false;
        AdminRoleProfileConfigured = false;
        ScopedAccessPoliciesConfigured = false;
        ReportingGroupsConfigured = false;
        AccountingAdminSurfaceConfigured = false;
        WpfAccountingAdminSurfaceConfigured = false;
        ChartAdministrationStudioConfigured = false;
        RuleTestPromotionStudioConfigured = false;
        CloseSetupStudioConfigured = false;
        ProviderMappingStudioConfigured = false;
        TenantCompanyReportGroupSetupStudioConfigured = false;
        AuditReviewToolingConfigured = false;
        BulkImportExportSafeguardsConfigured = false;
        PerformanceValidationConfigured = false;
        DisasterRecoveryRunbookConfigured = false;
        LedgerBookAdministrationStudioConfigured = false;
        PostingRuleAuthoringStudioConfigured = false;
        ApprovalQueueStudioConfigured = false;
        DimensionMappingStudioConfigured = false;
        ImplementationSandboxConfigured = false;
        BatchReconciliationActionText = "No reconciliation rows are loaded.";
        ClearRows();
        StatusText = "Select a fund-linked context to unlock Accounting Configure.";
        NotifyChartAccountSaveStateChanged();
        NotifyPostingRuleDraftSaveStateChanged();
        RaisePropertyChanged(nameof(CanSubmitManualJournal));
        NotifyProductionCertificationSaveStateChanged();
        NotifyTenantAdministrationSandboxStateChanged();
        RaiseManualJournalDraftComputedProperties();
    }

    private void ApplyConfiguration(AccountingConfigurationWorkspaceDto workspace)
    {
        ChartRows.ReplaceWith(workspace.ChartOfAccounts.Select(node =>
            new AccountingWorkbenchRow(node.Path, node.AccountType, node.AccountName, node.IsArchived ? "Archived" : "Active")));
        TemplateRows.ReplaceWith(workspace.JournalTemplates.Select(template =>
            new AccountingWorkbenchRow(template.TemplateId, template.Version, template.DisplayName, $"{template.Lines.Count} line(s)")));
        PostingRuleRows.ReplaceWith(workspace.PostingRules.Select(rule =>
            new AccountingWorkbenchRow(rule.RuleId, rule.SourceEventType, rule.DisplayName, rule.TemplateId)));
        AuditRows.ReplaceWith(workspace.AuditTrail.Take(20).Select(audit =>
            new AccountingWorkbenchRow(audit.Action, audit.Actor, audit.RecordedAtUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture), audit.CorrelationId ?? string.Empty)));
        ApplySetupReadinessRows(workspace);
        ApplyLedgerBookRows(workspace);
        ApplyManualJournalValidationRows(workspace.ValidationIssues);

        var critical = workspace.ValidationIssues.Count(issue => issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);
        ConfigurationStatusText = $"{workspace.Status} {workspace.ConfigurationVersion}";
        ConfigurationDetailText = critical > 0
            ? $"{critical} critical configuration issue(s) block activation."
            : "Chart, templates, posting rules, validation, and audit are shared-contract backed.";
        ApplyRulesStudio(workspace.RulesStudio);
        NotifyChartAccountSaveStateChanged();
        NotifyPostingRuleDraftSaveStateChanged();
        NotifyTenantAdministrationSandboxStateChanged();
    }

    private void ApplyNextChartAccountDraft(AccountingConfigurationWorkspaceDto workspace)
    {
        var nextIndex = workspace.ChartOfAccounts.Count(node => !node.IsArchived) + 1;
        ChartAccountNodeId = $"wpf-chart-account-{nextIndex.ToString(CultureInfo.InvariantCulture)}";
        ChartAccountPath = $"Assets:Configured Account {nextIndex.ToString(CultureInfo.InvariantCulture)}";
        ChartAccountName = $"Configured Account {nextIndex.ToString(CultureInfo.InvariantCulture)}";
        ChartAccountType = "Asset";
        ChartAccountParentPath = "Assets";
        ChartAccountFinancialAccountId = string.Empty;
        ChartAccountEvidenceText = $"wpf://accounting/configure/chart-account/{nextIndex.ToString(CultureInfo.InvariantCulture)}";
    }

    private void ApplyNextPostingRuleDraft(AccountingConfigurationWorkspaceDto workspace, string previousRuleId)
    {
        var nextIndex = workspace.PostingRules.Count(rule => !rule.IsArchived) + 1;
        PostingRuleDraftRuleId = $"wpf-configured-posting-rule-{nextIndex.ToString(CultureInfo.InvariantCulture)}";
        PostingRuleDraftDisplayName = $"WPF configured posting rule {nextIndex.ToString(CultureInfo.InvariantCulture)}";
        PostingRuleDraftSourceEventType = "ManualJournalEntry.Configured";
        PostingRuleDraftTemplateId = workspace.JournalTemplates.FirstOrDefault(template =>
                !template.IsArchived &&
                string.Equals(template.TemplateId, PostingRuleDraftTemplateId, StringComparison.OrdinalIgnoreCase))
            ?.TemplateId
            ?? workspace.JournalTemplates.FirstOrDefault(template => !template.IsArchived)?.TemplateId
            ?? "desktop-manual-adjustment-v1";
        PostingRuleDraftDescription = $"Drafted after {previousRuleId} from WPF Accounting Configure.";
        PostingRuleDraftPriority = nextIndex * 10;
        PostingRuleDraftEvidenceText = $"evidence://wpf/accounting/rules/configured-posting-rule-{nextIndex.ToString(CultureInfo.InvariantCulture)}";
    }

    private void ApplySetupReadinessRows(AccountingConfigurationWorkspaceDto workspace)
    {
        var selectedBook = ResolveActiveLedgerBook(workspace);
        var missingLedgerBookIssue = workspace.ValidationIssues.FirstOrDefault(issue =>
            string.Equals(issue.Code, "configuration.ledger-book-missing", StringComparison.OrdinalIgnoreCase));
        var setupCandidate = workspace.LedgerBookSetupCandidate;
        var criticalCount = workspace.ValidationIssues.Count(issue =>
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);
        var selectedBookStatus = missingLedgerBookIssue is not null
            ? "Missing"
            : selectedBook is not null
                ? selectedBook.DisplayName
                : workspace.LedgerBookId.HasValue
                    ? "Scoped"
                    : "Fund scope";
        var selectedBookDetail = missingLedgerBookIssue?.Message
            ?? (selectedBook is not null
                ? $"{selectedBook.AccountingBasis} basis; policy {selectedBook.AccountingPolicyId}/{selectedBook.AccountingPolicyVersion}."
                : workspace.LedgerBookId.HasValue
                    ? $"Ledger book {workspace.LedgerBookId.Value:D} is selected; setup details are not loaded."
                    : "Configuration is using the fund-level setup scope.");
        var selectedBookAction = missingLedgerBookIssue?.SuggestedAction
            ?? setupCandidate?.SuggestedAction
            ?? (workspace.LedgerBookId?.ToString("D", CultureInfo.InvariantCulture) ?? "fund");
        var activationDetail = criticalCount > 0
            ? $"{criticalCount} critical shared validation issue(s) must be resolved before activation."
            : "Shared accounting configuration validation has no critical blockers.";

        SetupReadinessRows.ReplaceWith(
        [
            new AccountingWorkbenchRow(
                "Selected ledger book",
                selectedBookStatus,
                selectedBookDetail,
                selectedBookAction,
                workspace.LedgerBookId?.ToString("D", CultureInfo.InvariantCulture) ?? "fund"),
            new AccountingWorkbenchRow(
                "Activation readiness",
                criticalCount > 0 ? "Blocked" : "Ready",
                activationDetail,
                missingLedgerBookIssue?.SuggestedAction ?? "Activation can use the current shared configuration scope.",
                $"critical:{criticalCount}")
        ]);
    }

    private void ApplyLedgerBookRows(AccountingConfigurationWorkspaceDto workspace)
    {
        var selectedLedgerBookId = workspace.LedgerBookId ?? ResolveActiveLedgerBook(workspace)?.LedgerBookId;
        var rows = workspace.LedgerBooks
            .OrderByDescending(book => selectedLedgerBookId.HasValue && book.LedgerBookId == selectedLedgerBookId.Value)
            .ThenBy(static book => book.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(book =>
            {
                var selected = selectedLedgerBookId.HasValue && book.LedgerBookId == selectedLedgerBookId.Value;
                var scope = $"{book.FundProfileId} / {book.FundStructureNodeKind} {book.FundStructureNodeId:D}";
                var detail =
                    $"{book.AccountingBasis} basis | {book.BaseCurrency} | {scope} | Updated {book.UpdatedAt:yyyy-MM-dd}";
                var evidence = string.IsNullOrWhiteSpace(book.Description)
                    ? $"Policy {book.AccountingPolicyId}/{book.AccountingPolicyVersion}"
                    : $"{book.Description} | Policy {book.AccountingPolicyId}/{book.AccountingPolicyVersion}";

                return new AccountingWorkbenchRow(
                    book.DisplayName,
                    selected ? "Selected" : "Available",
                    detail,
                    evidence,
                    book.LedgerBookId.ToString("D", CultureInfo.InvariantCulture));
            })
            .ToArray();

        LedgerBookRows.ReplaceWith(rows);
        LedgerBookAdministrationText = rows.Length == 0
            ? "No ledger books are registered for this accounting configuration workspace."
            : $"{rows.Length} ledger book(s) registered; selected {rows.FirstOrDefault(row => row.Status == "Selected")?.Name ?? "none"}.";
    }

    private void ApplyRulesStudio(AccountingRulesStudioDto? rulesStudio)
    {
        if (rulesStudio is null)
        {
            RulesStudioStatusText = "Rules Studio unavailable";
            RulesStudioDetailText = "The shared accounting configuration workspace did not include a Rules Studio projection.";
            RulesStudioRuleRows.Clear();
            RulesStudioPromotionRows.Clear();
            RulesStudioActionRows.Clear();
            return;
        }

        var summary = rulesStudio.Summary;
        RulesStudioStatusText =
            $"{summary.ActiveRules}/{summary.TotalRules} active rule(s), {summary.GeneratedPostingRules} generated posting rule(s), {summary.SavedTestCaseCount} saved test case(s).";
        RulesStudioDetailText =
            $"{summary.EffectiveDatedRules} effective-dated; {summary.PendingPromotionApprovalRules} pending promotion approval; {summary.RulesMissingCurrentVersionRegressionTests} missing current-version regression tests; {summary.CriticalIssueCount} critical issue(s).";

        RulesStudioRuleRows.ReplaceWith(rulesStudio.Rules.Select(rule =>
            new AccountingWorkbenchRow(
                rule.RuleId,
                rule.CanActivate ? "Activation-ready" : rule.CanRequestPromotion ? "Promotion-ready" : "Blocked",
                $"{rule.SourceEventType} | {rule.RuleVersion} | priority {rule.Priority} | {FormatEffectiveWindow(rule.EffectiveFrom, rule.EffectiveTo)}",
                $"{rule.ConditionCount + rule.ConditionGroupCount} condition set(s); {rule.FormulaCount} formula(s); {rule.AllocationCount} allocation(s); {rule.GeneratedPostingLineCount} generated line(s); {rule.SavedTestCaseCount} test(s)",
                rule.PromotionApprovalId ?? string.Empty)));

        RulesStudioPromotionRows.ReplaceWith(rulesStudio.PromotionQueue.Select(item =>
            new AccountingWorkbenchRow(
                item.RuleId,
                item.ApprovalState?.ToString() ?? "Not requested",
                $"{item.DisplayName} | {item.RuleVersion} | requested by {item.RequestedBy}",
                $"{item.RegressionTestCaseCount} regression test(s); {item.MissingRegressionEvidenceCount} missing evidence; {item.CriticalIssueCount} critical issue(s); {item.SuggestedAction}",
                item.ApprovalId ?? string.Empty)));
    }

    private static string FormatEffectiveWindow(DateOnly? effectiveFrom, DateOnly? effectiveTo)
    {
        var from = effectiveFrom?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "open";
        var to = effectiveTo?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "open";
        return $"{from} to {to}";
    }

    private async Task LoadProductionCertificationProfileAsync(CancellationToken ct)
    {
        if (_activeFundProfile is null)
        {
            ProductionCertificationProfileStatusText = "Locked until a fund context is selected.";
            ProductionCertificationProfileScopeText = "Fund and ledger-book scope require a fund context.";
            ProductionCertificationProfileUpdatedText = "No retained production certification profile loaded.";
            return;
        }

        var tenantId = ResolveTenantAdministrationTenantId();
        var companyId = ResolveTenantAdministrationCompanyId();
        var ledgerBookId = _configuration?.LedgerBookId;
        ProductionCertificationProfileScopeText = ledgerBookId.HasValue
            ? $"Tenant {tenantId}; company {companyId}; fund {_activeFundProfile.FundProfileId}; ledger book {ledgerBookId:D}."
            : $"Tenant {tenantId}; company {companyId}; fund {_activeFundProfile.FundProfileId}; fund-level scope.";

        if (_productionCertificationProfileStore is null)
        {
            ProductionCertificationProfileStatusText = "Production certification profile store is not registered for this desktop session.";
            ProductionCertificationProfileUpdatedText = "Retained production certification controls are unavailable.";
            NotifyProductionCertificationSaveStateChanged();
            return;
        }

        try
        {
            var profile = await _productionCertificationProfileStore.GetAsync(tenantId, companyId, _activeFundProfile.FundProfileId, ledgerBookId, ct).ConfigureAwait(false);
            if (profile is null)
            {
                ProductionCertificationProfileStatusText = "No retained production certification profile exists for the active accounting scope.";
                ProductionCertificationProfileUpdatedText = "Save book-native and dimensional controls with retained evidence before production certification.";
                ProductionCertificationEvidenceText = ledgerBookId.HasValue
                    ? $"evidence://ledger-book/{ledgerBookId:D}/production-certification"
                    : $"evidence://fund/{_activeFundProfile.FundProfileId}/production-certification";
                NotifyProductionCertificationSaveStateChanged();
                return;
            }

            ApplyProductionCertificationProfile(profile, "Production certification profile loaded from the shared store.");
        }
        catch (Exception ex)
        {
            ProductionCertificationProfileStatusText = $"Production certification profile could not load: {ex.Message}";
            ProductionCertificationProfileUpdatedText = "Retained production certification controls are unavailable.";
            NotifyProductionCertificationSaveStateChanged();
        }
    }

    private void ApplyProductionCertificationProfile(
        AccountingProductionCertificationProfileDto profile,
        string statusText)
    {
        PostingRulesLedgerBookNativeCertified = profile.PostingRulesLedgerBookNativeCertified;
        JournalLifecycleLedgerBookNativeCertified = profile.JournalLifecycleLedgerBookNativeCertified;
        CloseReportingLedgerBookNativeCertified = profile.CloseReportingLedgerBookNativeCertified;
        ClosePlanConfigurationLedgerBookNativeCertified = profile.ClosePlanConfigurationLedgerBookNativeCertified;
        ExternalGlLedgerBookNativeCertified = profile.ExternalGlLedgerBookNativeCertified;
        ReconciliationLedgerBookNativeCertified = profile.ReconciliationLedgerBookNativeCertified;
        DirectLendingLedgerBookNativeCertified = profile.DirectLendingLedgerBookNativeCertified;
        StrategyLedgerReadLedgerBookNativeCertified = profile.StrategyLedgerReadLedgerBookNativeCertified;
        PeriodReportDimensionQueriesCertified = profile.PeriodReportDimensionQueriesCertified;
        CrossPeriodReportDimensionQueriesCertified = profile.CrossPeriodReportDimensionQueriesCertified;
        JournalQueryDimensionFiltersCertified = profile.JournalQueryDimensionFiltersCertified;
        ExternalExportDimensionMappingCertified = profile.ExternalExportDimensionMappingCertified;
        LedgerLineDimensionsPersistedCertified = profile.LedgerLineDimensionsPersistedCertified;
        TrialBalanceDimensionFiltersCertified = profile.TrialBalanceDimensionFiltersCertified;
        ReportPackageDimensionProvenanceCertified = profile.ReportPackageDimensionProvenanceCertified;
        ProductionCertificationEvidenceText = string.Join(Environment.NewLine, profile.EvidenceReferences);
        ProductionCertificationProfileScopeText = profile.LedgerBookId.HasValue
            ? $"Tenant {FormatReadinessScope(profile.TenantId)}; company {FormatReadinessScope(profile.CompanyId)}; fund {profile.FundProfileId}; ledger book {profile.LedgerBookId:D}."
            : $"Tenant {FormatReadinessScope(profile.TenantId)}; company {FormatReadinessScope(profile.CompanyId)}; fund {profile.FundProfileId}; fund-level scope.";
        ProductionCertificationProfileUpdatedText =
            $"Last retained by {profile.UpdatedBy} at {profile.UpdatedAtUtc.ToLocalTime():g}.";
        ProductionCertificationProfileStatusText = statusText;
        NotifyProductionCertificationSaveStateChanged();
    }

    private async Task LoadTenantAdministrationProfileAsync(CancellationToken ct)
    {
        if (_activeFundProfile is null)
        {
            TenantAdministrationProfileStatusText = "Locked until a fund context is selected.";
            TenantAdministrationProfileScopeText = "Tenant and company scope require a fund context.";
            TenantAdministrationProfileUpdatedText = "No retained tenant administration setup profile loaded.";
            ImplementationSandboxProofStatusText = "Implementation sandbox proof is locked until a fund context is selected.";
            TenantAdministrationControlRows.Clear();
            NotifyTenantAdministrationSandboxStateChanged();
            return;
        }

        var tenantId = ResolveTenantAdministrationTenantId();
        var companyId = ResolveTenantAdministrationCompanyId();
        TenantAdministrationProfileScopeText = $"Tenant {tenantId}; company {companyId}.";

        if (_tenantAdministrationProfileStore is null)
        {
            TenantAdministrationProfileStatusText = "Tenant administration profile store is not registered for this desktop session.";
            TenantAdministrationProfileUpdatedText = "Retained tenant setup controls are unavailable.";
            ImplementationSandboxProofStatusText = "Implementation sandbox proof is unavailable for this desktop session.";
            ApplyTenantAdministrationControlRows();
            NotifyTenantAdministrationSaveStateChanged();
            NotifyTenantAdministrationSandboxStateChanged();
            return;
        }

        try
        {
            var profile = await _tenantAdministrationProfileStore.GetAsync(tenantId, companyId, ct).ConfigureAwait(false);
            if (profile is null)
            {
                TenantAdministrationProfileStatusText = "No retained tenant administration setup profile exists for the active fund context.";
                TenantAdministrationProfileUpdatedText = "Save setup controls with retained evidence before production certification.";
                TenantAdministrationEvidenceText = $"evidence://tenant-admin/{tenantId}/{companyId}/setup";
                ImplementationSandboxProofStatusText = "Implementation sandbox proof has not been retained.";
                ApplyTenantAdministrationControlRows();
                NotifyTenantAdministrationSaveStateChanged();
                NotifyTenantAdministrationSandboxStateChanged();
                return;
            }

            ApplyTenantAdministrationProfile(profile, "Tenant administration setup profile loaded from the shared store.");
        }
        catch (Exception ex)
        {
            TenantAdministrationProfileStatusText = $"Tenant administration setup profile could not load: {ex.Message}";
            TenantAdministrationProfileUpdatedText = "Retained tenant setup controls are unavailable.";
            ImplementationSandboxProofStatusText = "Implementation sandbox proof could not load.";
            ApplyTenantAdministrationControlRows();
            NotifyTenantAdministrationSaveStateChanged();
            NotifyTenantAdministrationSandboxStateChanged();
        }
    }

    private void ApplyTenantAdministrationProfile(
        AccountingTenantAdministrationProfileDto profile,
        string statusText)
    {
        TenantScopeConfigured = profile.TenantScopeConfigured;
        AdminRoleProfileConfigured = profile.AdminRoleProfileConfigured;
        ScopedAccessPoliciesConfigured = profile.ScopedAccessPoliciesConfigured;
        ReportingGroupsConfigured = profile.ReportingGroupsConfigured;
        AccountingAdminSurfaceConfigured = profile.AccountingAdminSurfaceConfigured;
        WpfAccountingAdminSurfaceConfigured = profile.WpfAccountingAdminSurfaceConfigured;
        ChartAdministrationStudioConfigured = profile.ChartAdministrationStudioConfigured;
        RuleTestPromotionStudioConfigured = profile.RuleTestPromotionStudioConfigured;
        CloseSetupStudioConfigured = profile.CloseSetupStudioConfigured;
        ProviderMappingStudioConfigured = profile.ProviderMappingStudioConfigured;
        TenantCompanyReportGroupSetupStudioConfigured = profile.TenantCompanyReportGroupSetupStudioConfigured;
        AuditReviewToolingConfigured = profile.AuditReviewToolingConfigured;
        BulkImportExportSafeguardsConfigured = profile.BulkImportExportSafeguardsConfigured;
        PerformanceValidationConfigured = profile.PerformanceValidationConfigured;
        DisasterRecoveryRunbookConfigured = profile.DisasterRecoveryRunbookConfigured;
        LedgerBookAdministrationStudioConfigured = profile.LedgerBookAdministrationStudioConfigured;
        PostingRuleAuthoringStudioConfigured = profile.PostingRuleAuthoringStudioConfigured;
        ApprovalQueueStudioConfigured = profile.ApprovalQueueStudioConfigured;
        ApplyApprovalQueueConfiguration(profile.ApprovalQueueConfigurations.FirstOrDefault());
        DimensionMappingStudioConfigured = profile.DimensionMappingStudioConfigured;
        ApplyDimensionMappingConfiguration(profile.DimensionMappingConfigurations.FirstOrDefault());
        ImplementationSandboxConfigured = profile.ImplementationSandboxConfigured;
        TenantAdministrationEvidenceText = string.Join(Environment.NewLine, profile.EvidenceReferences);
        TenantAdministrationProfileScopeText = $"Tenant {profile.TenantId}; company {profile.CompanyId}.";
        TenantAdministrationProfileUpdatedText =
            $"Last retained by {profile.UpdatedBy} at {profile.UpdatedAtUtc.ToLocalTime():g}.";
        TenantAdministrationProfileStatusText = statusText;
        ImplementationSandboxProofStatusText = ImplementationSandboxConfigured
            ? "Implementation sandbox proof retained with fixture and ledger-book validation evidence."
            : "Implementation sandbox proof has not been retained.";
        ApplyTenantAdministrationControlRows();
        NotifyTenantAdministrationSaveStateChanged();
        NotifyTenantAdministrationSandboxStateChanged();
    }

    private void NotifyTenantAdministrationSaveStateChanged()
    {
        RaisePropertyChanged(nameof(CanSaveTenantAdministrationProfile));
        SaveTenantAdministrationProfileCommand.NotifyCanExecuteChanged();
    }

    private void NotifyTenantAdministrationSandboxStateChanged()
    {
        RaisePropertyChanged(nameof(CanRetainImplementationSandboxProof));
        RetainImplementationSandboxProofCommand.NotifyCanExecuteChanged();
    }

    private void NotifyChartAccountSaveStateChanged()
    {
        RaisePropertyChanged(nameof(CanSaveChartAccount));
        SaveChartAccountCommand.NotifyCanExecuteChanged();
    }

    private void NotifyPostingRuleDraftSaveStateChanged()
    {
        RaisePropertyChanged(nameof(CanSavePostingRuleDraft));
        SavePostingRuleDraftCommand.NotifyCanExecuteChanged();
    }

    private void NotifyExternalGlMappingProfileSaveStateChanged()
    {
        RaisePropertyChanged(nameof(CanSaveExternalGlMappingProfile));
        SaveExternalGlMappingProfileCommand.NotifyCanExecuteChanged();
    }

    private void NotifyProductionCertificationSaveStateChanged()
    {
        RaisePropertyChanged(nameof(CanSaveProductionCertificationProfile));
        SaveProductionCertificationProfileCommand.NotifyCanExecuteChanged();
    }

    private IReadOnlyList<AccountingApprovalQueueConfigurationDto> BuildApprovalQueueConfigurations()
    {
        if (string.IsNullOrWhiteSpace(ApprovalQueueId) ||
            string.IsNullOrWhiteSpace(ApprovalQueueDisplayName) ||
            string.IsNullOrWhiteSpace(ApprovalQueueWorkflowKind) ||
            string.IsNullOrWhiteSpace(ApprovalQueueRequiredApprovalRole) ||
            ApprovalQueueRequiredApprovalCount <= 0 ||
            string.IsNullOrWhiteSpace(ApprovalQueueSegregationPolicy) ||
            string.IsNullOrWhiteSpace(ApprovalQueueEvidenceRequirement))
        {
            return [];
        }

        return
        [
            new AccountingApprovalQueueConfigurationDto(
                ApprovalQueueId.Trim(),
                ApprovalQueueDisplayName.Trim(),
                ApprovalQueueWorkflowKind.Trim(),
                ApprovalQueueRequiredApprovalRole.Trim(),
                ApprovalQueueRequiredApprovalCount,
                ApprovalQueueSegregationPolicy.Trim(),
                ApprovalQueueEvidenceRequirement.Trim())
        ];
    }

    private void ApplyApprovalQueueConfiguration(AccountingApprovalQueueConfigurationDto? configuration)
    {
        configuration ??= new AccountingApprovalQueueConfigurationDto(
            "accounting-configuration-approval",
            "Accounting configuration approval",
            "ConfigurationPromotion",
            "Controller",
            2,
            "Preparer cannot approve own configuration change.",
            "approval-queue;configuration-approval;segregation-review");

        ApprovalQueueId = configuration.QueueId;
        ApprovalQueueDisplayName = configuration.DisplayName;
        ApprovalQueueWorkflowKind = configuration.WorkflowKind;
        ApprovalQueueRequiredApprovalRole = configuration.RequiredApprovalRole;
        ApprovalQueueRequiredApprovalCount = Math.Max(1, configuration.RequiredApprovalCount);
        ApprovalQueueSegregationPolicy = configuration.SegregationPolicy;
        ApprovalQueueEvidenceRequirement = configuration.EvidenceRequirement;
    }

    private IReadOnlyList<AccountingDimensionMappingConfigurationDto> BuildDimensionMappingConfigurations()
    {
        if (string.IsNullOrWhiteSpace(DimensionMappingId) ||
            string.IsNullOrWhiteSpace(DimensionMappingDisplayName) ||
            string.IsNullOrWhiteSpace(DimensionMappingProviderId) ||
            string.IsNullOrWhiteSpace(DimensionMappingMeridianDimensionsText) ||
            string.IsNullOrWhiteSpace(DimensionMappingProviderDimensionsText) ||
            string.IsNullOrWhiteSpace(DimensionMappingEvidenceRequirement))
        {
            return [];
        }

        var providerId = DimensionMappingProviderId.Trim();
        return
        [
            new AccountingDimensionMappingConfigurationDto(
                DimensionMappingId.Trim(),
                DimensionMappingDisplayName.Trim(),
                providerId,
                ParseLedgerDimensionSet(DimensionMappingMeridianDimensionsText, BuildDefaultMeridianExternalGlDimensions(providerId, DimensionMappingId.Trim())),
                ParseLedgerDimensionSet(DimensionMappingProviderDimensionsText, BuildDefaultProviderExternalGlDimensions()),
                DimensionMappingEvidenceRequirement.Trim())
        ];
    }

    private void ApplyDimensionMappingConfiguration(AccountingDimensionMappingConfigurationDto? configuration)
    {
        configuration ??= new AccountingDimensionMappingConfigurationDto(
            "canonical-provider-dimension-map",
            "Canonical provider dimension map",
            "quickbooks-fixture",
            BuildDefaultMeridianExternalGlDimensions("quickbooks-fixture", "canonical-provider-dimension-map"),
            BuildDefaultProviderExternalGlDimensions(),
            "dimension-mapping;external-dimension-mapping;gl-dimension-mapping");

        DimensionMappingId = configuration.MappingId;
        DimensionMappingDisplayName = configuration.DisplayName;
        DimensionMappingProviderId = configuration.ProviderId;
        DimensionMappingMeridianDimensionsText = FormatExternalGlLedgerDimensionSet(configuration.MeridianDimensions);
        DimensionMappingProviderDimensionsText = FormatExternalGlLedgerDimensionSet(configuration.ProviderDimensions);
        DimensionMappingEvidenceRequirement = configuration.EvidenceRequirement;
    }

    private void ApplyTenantAdministrationControlRows()
    {
        TenantAdministrationControlRows.ReplaceWith(
        [
            new AccountingWorkbenchRow(
                "Tenant config",
                TenantScopeConfigured ? "Configured" : "Missing",
                "Tenant scope, accounting configuration, and company setup are retained.",
                ResolveTenantAdministrationTenantId(),
                "tenant-config"),
            new AccountingWorkbenchRow(
                "Admin roles",
                AdminRoleProfileConfigured ? "Configured" : "Missing",
                "Accounting admin role profiles are configured for governed setup.",
                DefaultLifecycleActor,
                "admin-roles"),
            new AccountingWorkbenchRow(
                "Scoped access",
                ScopedAccessPoliciesConfigured ? "Configured" : "Missing",
                "Accounting permissions are scoped to tenant, company, fund, or book boundaries.",
                ResolveTenantAdministrationCompanyId(),
                "scoped-access"),
            new AccountingWorkbenchRow(
                "Reporting groups",
                ReportingGroupsConfigured ? "Configured" : "Missing",
                "Reporting group principals are configured for close, report, and export review.",
                "close/report/export",
                "reporting-groups"),
            new AccountingWorkbenchRow(
                "Operator surface",
                AccountingAdminSurfaceConfigured ? "Configured" : "Missing",
                "Accounting setup controls are exposed through the governed operator surface.",
                "WPF Accounting Configure",
                "operator-surface"),
            new AccountingWorkbenchRow(
                "WPF admin",
                WpfAccountingAdminSurfaceConfigured ? "Configured" : "Missing",
                "Desktop accounting setup controls are exposed through the governed WPF administration surface.",
                "WPF Accounting Configure",
                "wpf-admin-studio"),
            new AccountingWorkbenchRow(
                "Chart admin",
                ChartAdministrationStudioConfigured ? "Configured" : "Missing",
                "Chart, ledger-book chart, account template, and activation controls are retained.",
                "chart setup",
                "chart-administration-studio"),
            new AccountingWorkbenchRow(
                "Rule tests",
                RuleTestPromotionStudioConfigured ? "Configured" : "Missing",
                "Rules Studio tests, dry-run previews, generated postings, and promotion queues are retained.",
                "rules studio",
                "rule-test-promotion-studio"),
            new AccountingWorkbenchRow(
                "Close setup",
                CloseSetupStudioConfigured ? "Configured" : "Missing",
                "Close checklist, dependencies, sign-offs, materiality, late adjustments, and period locks are retained.",
                "close setup",
                "close-setup-studio"),
            new AccountingWorkbenchRow(
                "Provider maps",
                ProviderMappingStudioConfigured ? "Configured" : "Missing",
                "QuickBooks, Xero, NetSuite, dimension mapping, import evidence, and guarded export setup are retained.",
                "provider mapping",
                "provider-mapping-studio"),
            new AccountingWorkbenchRow(
                "Tenant/report setup",
                TenantCompanyReportGroupSetupStudioConfigured ? "Configured" : "Missing",
                "Tenant, company, scoped access, admin role, and report group setup controls are retained.",
                "tenant/company/report",
                "tenant-company-report-group-studio"),
            new AccountingWorkbenchRow(
                "Audit review",
                AuditReviewToolingConfigured ? "Configured" : "Missing",
                "Audit, evidence, transition, and exception review tooling are retained for operations.",
                "audit/evidence review",
                "audit-review-tooling"),
            new AccountingWorkbenchRow(
                "Bulk safeguards",
                BulkImportExportSafeguardsConfigured ? "Configured" : "Missing",
                "Bulk import, guarded export, reconciliation, preview, approval, and rollback safeguards are retained.",
                "import/export",
                "bulk-import-export-safeguards"),
            new AccountingWorkbenchRow(
                "Performance proof",
                PerformanceValidationConfigured ? "Configured" : "Missing",
                "Accounting configuration, ledger queries, close, report, and export performance validation is retained.",
                "load/capacity",
                "performance-validation"),
            new AccountingWorkbenchRow(
                "Recovery runbook",
                DisasterRecoveryRunbookConfigured ? "Configured" : "Missing",
                "Disaster recovery, restore, replay, escalation, and operating runbook evidence is retained.",
                "DR/runbook",
                "disaster-recovery-runbook"),
            new AccountingWorkbenchRow(
                "Book admin",
                LedgerBookAdministrationStudioConfigured ? "Configured" : "Missing",
                "Ledger-book creation, selection, policy, activation, and period setup are retained.",
                "ledger books",
                "ledger-book-administration-studio"),
            new AccountingWorkbenchRow(
                "Rule authoring",
                PostingRuleAuthoringStudioConfigured ? "Configured" : "Missing",
                "Event predicates, scopes, thresholds, formulas, allocations, and posting previews are retained.",
                "posting rules",
                "posting-rule-authoring-studio"),
            new AccountingWorkbenchRow(
                "Approvals",
                ApprovalQueueStudioConfigured ? "Configured" : "Missing",
                "Rule promotion, journal, configuration, and segregation approval queues are retained.",
                "approvals",
                "approval-queue-studio"),
            new AccountingWorkbenchRow(
                "Dimension maps",
                DimensionMappingStudioConfigured ? "Configured" : "Missing",
                $"Canonical and external GL dimension mapping setup is retained for {DimensionMappingProviderId}.",
                string.IsNullOrWhiteSpace(DimensionMappingId) ? "dimensions" : DimensionMappingId,
                "dimension-mapping-studio"),
            new AccountingWorkbenchRow(
                "Sandbox proof",
                ImplementationSandboxConfigured ? "Configured" : "Missing",
                "Implementation fixture, migration rehearsal, import, rule, close, and report validation is retained.",
                "implementation",
                "implementation-sandbox")
        ]);
    }

    private async Task RefreshProductionReadinessAsync(CancellationToken ct)
    {
        if (_activeFundProfile is null)
        {
            ProductionReadinessStatusText = "Locked";
            ProductionReadinessDetailText = "Select a fund-linked context before assessing accounting production readiness.";
            ProductionReadinessScoreText = "No score";
            ProductionReadinessLedgerBookText = "Locked until a fund context is selected.";
            ProductionReadinessExternalGlText = "Locked until a fund context is selected.";
            ProductionReadinessDimensionalReportingText = "Locked until a fund context is selected.";
            ProductionReadinessTenantAdminText = "Locked until a fund context is selected.";
            ProductionReadinessComponentRows.Clear();
            ProductionReadinessIssueRows.Clear();
            ProductionReadinessWorkflowRows.Clear();
            ProductionReadinessDimensionalControlRows.Clear();
            ProductionReadinessGapRows.Clear();
            ProductionReadinessMigrationPlanRows.Clear();
            ProductionReadinessMigrationArtifactRows.Clear();
            ProductionReadinessMigrationWorkerPlanRows.Clear();
            return;
        }

        if (_accountingProductionReadinessService is null)
        {
            ProductionReadinessStatusText = "Unavailable";
            ProductionReadinessDetailText = "Accounting production-readiness service is not registered for this desktop session.";
            ProductionReadinessScoreText = "No score";
            ProductionReadinessLedgerBookText = "Ledger-book readiness cannot be assessed.";
            ProductionReadinessExternalGlText = "External GL readiness cannot be assessed.";
            ProductionReadinessDimensionalReportingText = "Dimensional reporting readiness cannot be assessed.";
            ProductionReadinessTenantAdminText = "Tenant administration readiness cannot be assessed.";
            ProductionReadinessComponentRows.Clear();
            ProductionReadinessWorkflowRows.Clear();
            ProductionReadinessDimensionalControlRows.Clear();
            ProductionReadinessGapRows.Clear();
            ProductionReadinessMigrationPlanRows.Clear();
            ProductionReadinessMigrationArtifactRows.Clear();
            ProductionReadinessMigrationWorkerPlanRows.Clear();
            ProductionReadinessIssueRows.ReplaceWith(
            [
                new AccountingWorkbenchRow(
                    "production-readiness.service-missing",
                    AccountingConfigurationValidationSeverityDto.Critical.ToString(),
                    ProductionReadinessDetailText,
                    "Register AccountingProductionReadinessService in the Accounting feature module.")
            ]);
            return;
        }

        try
        {
            var request = BuildProductionReadinessRequest();
            var readiness = await _accountingProductionReadinessService.AssessAsync(request, ct).ConfigureAwait(false);
            var workerPlans = await LoadMigrationWorkerPlansAsync(readiness, ct).ConfigureAwait(false);
            ApplyProductionReadiness(readiness, workerPlans);
        }
        catch (Exception ex)
        {
            ProductionReadinessStatusText = "Unavailable";
            ProductionReadinessDetailText = $"Accounting production readiness could not be assessed: {ex.Message}";
            ProductionReadinessScoreText = "No score";
            ProductionReadinessLedgerBookText = "Ledger-book readiness could not be assessed.";
            ProductionReadinessExternalGlText = "External GL readiness could not be assessed.";
            ProductionReadinessDimensionalReportingText = "Dimensional reporting readiness could not be assessed.";
            ProductionReadinessTenantAdminText = "Tenant administration readiness could not be assessed.";
            ProductionReadinessComponentRows.Clear();
            ProductionReadinessWorkflowRows.Clear();
            ProductionReadinessDimensionalControlRows.Clear();
            ProductionReadinessGapRows.Clear();
            ProductionReadinessMigrationPlanRows.Clear();
            ProductionReadinessMigrationArtifactRows.Clear();
            ProductionReadinessMigrationWorkerPlanRows.Clear();
            ProductionReadinessIssueRows.ReplaceWith(
            [
                new AccountingWorkbenchRow(
                    "production-readiness.assessment-failed",
                    AccountingConfigurationValidationSeverityDto.Critical.ToString(),
                    ProductionReadinessDetailText,
                    "Resolve shared accounting service registration or configuration errors before rollout.")
            ]);
        }
    }

    private AccountingProductionReadinessRequestDto BuildProductionReadinessRequest()
    {
        var workspace = _configuration;
        var setupCandidate = workspace?.LedgerBookSetupCandidate;
        var requiredScopes = setupCandidate is null
            ? Array.Empty<LedgerBookRequiredScopeDto>()
            :
            [
                new LedgerBookRequiredScopeDto(
                    setupCandidate.FundStructureNodeId,
                    setupCandidate.FundStructureNodeKind,
                    setupCandidate.AccountingBasis,
                    setupCandidate.DisplayName)
            ];

        return new AccountingProductionReadinessRequestDto(
            _activeFundProfile?.FundProfileId,
            workspace?.LedgerBookId,
            setupCandidate?.AccountingBasis ?? (workspace is null ? null : ResolveActiveLedgerBook(workspace)?.AccountingBasis),
            ProviderId: null,
            TenantId: ResolveTenantAdministrationTenantId(),
            CompanyId: ResolveTenantAdministrationCompanyId(),
            TenantScopeConfigured: TenantScopeConfigured,
            AdminRoleProfileConfigured: AdminRoleProfileConfigured,
            ScopedAccessPoliciesConfigured: ScopedAccessPoliciesConfigured,
            ReportingGroupsConfigured: ReportingGroupsConfigured,
            AccountingAdminSurfaceConfigured: AccountingAdminSurfaceConfigured,
            WpfAccountingAdminSurfaceConfigured: WpfAccountingAdminSurfaceConfigured,
            ChartAdministrationStudioConfigured: ChartAdministrationStudioConfigured,
            RuleTestPromotionStudioConfigured: RuleTestPromotionStudioConfigured,
            CloseSetupStudioConfigured: CloseSetupStudioConfigured,
            ProviderMappingStudioConfigured: ProviderMappingStudioConfigured,
            TenantCompanyReportGroupSetupStudioConfigured: TenantCompanyReportGroupSetupStudioConfigured,
            AuditReviewToolingConfigured: AuditReviewToolingConfigured,
            BulkImportExportSafeguardsConfigured: BulkImportExportSafeguardsConfigured,
            PerformanceValidationConfigured: PerformanceValidationConfigured,
            DisasterRecoveryRunbookConfigured: DisasterRecoveryRunbookConfigured,
            LedgerBookAdministrationStudioConfigured: LedgerBookAdministrationStudioConfigured,
            PostingRuleAuthoringStudioConfigured: PostingRuleAuthoringStudioConfigured,
            ApprovalQueueStudioConfigured: ApprovalQueueStudioConfigured,
            DimensionMappingStudioConfigured: DimensionMappingStudioConfigured,
            ImplementationSandboxConfigured: ImplementationSandboxConfigured,
            TenantAdministrationEvidenceLinks: NormalizeTenantAdministrationEvidence(TenantAdministrationEvidenceText),
            PostingRulesLedgerBookNativeCertified: PostingRulesLedgerBookNativeCertified,
            JournalLifecycleLedgerBookNativeCertified: JournalLifecycleLedgerBookNativeCertified,
            CloseReportingLedgerBookNativeCertified: CloseReportingLedgerBookNativeCertified,
            ClosePlanConfigurationLedgerBookNativeCertified: ClosePlanConfigurationLedgerBookNativeCertified,
            ExternalGlLedgerBookNativeCertified: ExternalGlLedgerBookNativeCertified,
            ReconciliationLedgerBookNativeCertified: ReconciliationLedgerBookNativeCertified,
            DirectLendingLedgerBookNativeCertified: DirectLendingLedgerBookNativeCertified,
            StrategyLedgerReadLedgerBookNativeCertified: StrategyLedgerReadLedgerBookNativeCertified,
            LedgerBookWorkflowEvidenceLinks: NormalizeTenantAdministrationEvidence(ProductionCertificationEvidenceText),
            PeriodReportDimensionQueriesCertified: PeriodReportDimensionQueriesCertified,
            CrossPeriodReportDimensionQueriesCertified: CrossPeriodReportDimensionQueriesCertified,
            JournalQueryDimensionFiltersCertified: JournalQueryDimensionFiltersCertified,
            ExternalExportDimensionMappingCertified: ExternalExportDimensionMappingCertified,
            LedgerLineDimensionsPersistedCertified: LedgerLineDimensionsPersistedCertified,
            TrialBalanceDimensionFiltersCertified: TrialBalanceDimensionFiltersCertified,
            ReportPackageDimensionProvenanceCertified: ReportPackageDimensionProvenanceCertified,
            DimensionalReportingEvidenceLinks: NormalizeTenantAdministrationEvidence(ProductionCertificationEvidenceText),
            RequiredLedgerBookScopes: requiredScopes);
    }

    private async Task<IReadOnlyList<AccountingMigrationRunWorkerPlanDto>> LoadMigrationWorkerPlansAsync(
        AccountingProductionReadinessDto readiness,
        CancellationToken ct)
    {
        if (_migrationRunWorkerPlanStore is null)
        {
            return [];
        }

        var fundProfileId = _activeFundProfile?.FundProfileId;
        var ledgerBookId = _configuration?.LedgerBookId;
        return await _migrationRunWorkerPlanStore.ListAsync(
                fundProfileId,
                ledgerBookId,
                kind: null,
                ct: ct,
                tenantId: ResolveTenantAdministrationTenantId(),
                companyId: ResolveTenantAdministrationCompanyId())
            .ConfigureAwait(false);
    }

    private void ApplyProductionReadiness(
        AccountingProductionReadinessDto readiness,
        IReadOnlyList<AccountingMigrationRunWorkerPlanDto> migrationWorkerPlans)
    {
        ProductionReadinessStatusText = $"{readiness.Status} | {readiness.Score}/100";
        ProductionReadinessScoreText = $"{readiness.Score}/100";
        ProductionReadinessDetailText =
            $"{readiness.Components.Count} component(s), {readiness.CriticalIssueCount} critical issue(s), {readiness.WarningIssueCount} warning issue(s); generated {readiness.GeneratedAtUtc.ToLocalTime():g}.";
        ProductionReadinessLedgerBookText = readiness.LedgerBookRollout is null
            ? "Ledger-book rollout assessment is unavailable."
            : $"{readiness.LedgerBookRollout.BookCount} book(s), {readiness.LedgerBookRollout.OpenPeriodCount} open period(s), {readiness.LedgerBookRollout.CriticalIssueCount} critical rollout issue(s); {readiness.LedgerBookWorkflows?.CompletedControlCount ?? 0}/{readiness.LedgerBookWorkflows?.RequiredControlCount ?? 10} workflow control(s).";
        ProductionReadinessExternalGlText =
            $"{readiness.ExternalGlProviderCount} provider(s), {readiness.CertifiedExternalGlMappingProfileCount} certified mapping profile(s); live posting {(readiness.ExternalGlLivePostingEnabled ? "available" : "disabled")}.";
        ProductionReadinessDimensionalReportingText = readiness.DimensionalReporting is null
            ? "Dimensional reporting readiness is unavailable."
            : $"{readiness.DimensionalReporting.CompletedControlCount}/{readiness.DimensionalReporting.RequiredControlCount} ledger/query/report/export dimension control(s); ledger book {FormatReadinessScope(readiness.DimensionalReporting.LedgerBookId?.ToString("D"))}; {readiness.DimensionalReporting.EvidenceReferences.Count} retained evidence link(s).";
        ProductionReadinessTenantAdminText = readiness.TenantAdministration is null
            ? "Tenant administration readiness is unavailable."
            : $"{readiness.TenantAdministration.CompletedControlCount}/{readiness.TenantAdministration.RequiredControlCount} tenant admin control(s); tenant {FormatReadinessScope(readiness.TenantAdministration.TenantId)}; company {FormatReadinessScope(readiness.TenantAdministration.CompanyId)}; {readiness.TenantAdministration.EvidenceReferences.Count} retained evidence link(s).";

        ProductionReadinessComponentRows.ReplaceWith(readiness.Components.Select(component =>
            new AccountingWorkbenchRow(
                component.Label,
                $"{component.Status} | {component.Score}/100",
                component.Summary,
                component.EvidenceReferences.Count > 0
                    ? string.Join("; ", component.EvidenceReferences)
                    : component.Route ?? string.Empty,
                component.Area.ToString())));

        ProductionReadinessIssueRows.ReplaceWith(readiness.Issues
            .Where(static issue => issue.Severity is AccountingConfigurationValidationSeverityDto.Critical or AccountingConfigurationValidationSeverityDto.Warning)
            .Take(12)
            .Select(issue => new AccountingWorkbenchRow(
                issue.Code,
                issue.Severity.ToString(),
                issue.Message,
                issue.SuggestedAction,
                issue.EvidenceReferences.Count > 0
                    ? string.Join("; ", issue.EvidenceReferences)
                    : issue.Area.ToString())));
        ProductionReadinessWorkflowRows.ReplaceWith(BuildLedgerBookWorkflowRows(readiness.LedgerBookWorkflows));
        ProductionReadinessDimensionalControlRows.ReplaceWith(BuildDimensionalReportingRows(readiness.DimensionalReporting));
        ProductionReadinessGapRows.ReplaceWith(BuildProductionGapRows(readiness.ProductionGaps));
        ProductionReadinessMigrationPlanRows.ReplaceWith(readiness.MigrationRolloutPlan.Select(item =>
            new AccountingWorkbenchRow(
                item.Label,
                $"{item.Status} | {(item.Certified ? "Certified" : "Not certified")}",
                $"{item.ScopeLabel}; latest run {(string.IsNullOrWhiteSpace(item.LatestRunId) ? "missing" : item.LatestRunId)}{(item.LatestRunStatus.HasValue ? $" ({item.LatestRunStatus.Value})" : string.Empty)}; {item.MigratedRecordCount:N0} record(s), {item.IssueCount:N0} issue(s).",
                item.BlockingIssueCodes.Count > 0
                    ? $"{item.RequiredAction} | {string.Join(", ", item.BlockingIssueCodes)}"
                    : item.RequiredAction,
                item.Code)));
        ProductionReadinessMigrationArtifactRows.ReplaceWith(readiness.MigrationRunArtifacts
            .OrderByDescending(static artifact => artifact.StartedAtUtc)
            .ThenBy(static artifact => artifact.RunId, StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .Select(artifact => new AccountingWorkbenchRow(
                FormatMigrationArtifactKind(artifact.Kind),
                artifact.Status.ToString(),
                $"{FormatMigrationArtifactScope(artifact)}; {artifact.MigratedRecordCount:N0} record(s), {artifact.IssueCount:N0} issue(s); started {artifact.StartedAtUtc.ToLocalTime():g}{(artifact.CompletedAtUtc.HasValue ? $", completed {artifact.CompletedAtUtc.Value.ToLocalTime():g}" : string.Empty)}.",
                artifact.EvidenceReferences.Count > 0
                    ? string.Join("; ", artifact.EvidenceReferences)
                    : FormatLedgerDimensionSet(artifact.Dimensions),
                artifact.RunId)));
        ProductionReadinessMigrationWorkerPlanRows.ReplaceWith(migrationWorkerPlans
            .OrderBy(static plan => plan.Kind)
            .ThenBy(static plan => plan.PlanId, StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .Select(plan => new AccountingWorkbenchRow(
                FormatMigrationArtifactKind(plan.Kind),
                plan.SourceRecordCount == plan.MigratedRecordCount ? "Reconciled" : "Mismatch",
                $"{FormatMigrationWorkerPlanScope(plan)}; {plan.SourceRecordCount:N0} source record(s), {plan.MigratedRecordCount:N0} migrated record(s).",
                plan.EvidenceReferences.Count > 0
                    ? string.Join("; ", plan.EvidenceReferences)
                    : FormatLedgerDimensionSet(plan.Dimensions),
                plan.PlanId)));
    }

    private static IEnumerable<AccountingWorkbenchRow> BuildLedgerBookWorkflowRows(
        AccountingLedgerBookWorkflowReadinessDto? readiness)
    {
        if (readiness is null)
        {
            yield return new AccountingWorkbenchRow(
                "Ledger-book workflow controls",
                "Unavailable",
                "Shared ledger-book workflow readiness was not returned.",
                "Register production-readiness assessment before certifying workflow lanes.",
                "ledger-book-workflows:unavailable");
            yield break;
        }

        yield return BuildReadinessControlRow(
            "Ledger-book scope",
            readiness.HasLedgerBookScope,
            readiness.LedgerBookId.HasValue ? $"Ledger book {readiness.LedgerBookId.Value:D}" : "Missing ledger-book scope.",
            "Select or create the governed ledger book before certifying workflow controls.",
            "ledger-book-workflows:scope",
            readiness.EvidenceReferences);
        yield return BuildReadinessControlRow(
            "Ledger-book scoped evidence",
            readiness.HasLedgerBookScopedEvidence,
            "Retained evidence names the selected ledger book.",
            "Attach ledger-book-scoped workflow certification evidence.",
            "ledger-book-workflows:evidence",
            readiness.EvidenceReferences);
        yield return BuildReadinessControlRow(
            "Posting rules",
            readiness.PostingRulesLedgerBookNativeCertified && readiness.HasPostingRulesLedgerBookNativeEvidence,
            "Posting rules and Rules Studio execution are certified for the selected book.",
            "Retain posting-rules, Rules Studio, or posting-candidate evidence for the selected book.",
            "ledger-book-workflows:posting-rules",
            readiness.EvidenceReferences);
        yield return BuildReadinessControlRow(
            "Journal lifecycle",
            readiness.JournalLifecycleLedgerBookNativeCertified && readiness.HasJournalLifecycleLedgerBookNativeEvidence,
            "Manual journal lifecycle is certified for the selected book.",
            "Retain journal-lifecycle or manual-journal evidence for the selected book.",
            "ledger-book-workflows:journal-lifecycle",
            readiness.EvidenceReferences);
        yield return BuildReadinessControlRow(
            "Close/reporting",
            readiness.CloseReportingLedgerBookNativeCertified && readiness.HasCloseReportingLedgerBookNativeEvidence,
            "Close management and report packages are certified for the selected book.",
            "Retain close-management, report-package, or restatement evidence for the selected book.",
            "ledger-book-workflows:close-reporting",
            readiness.EvidenceReferences);
        yield return BuildReadinessControlRow(
            "Close-plan setup",
            readiness.ClosePlanConfigurationLedgerBookNativeCertified && readiness.HasClosePlanConfigurationLedgerBookNativeEvidence,
            "Close-plan configuration is certified for the selected book.",
            "Retain close-plan configuration, setup, checklist, dependency, sign-off, or materiality evidence for the selected book.",
            "ledger-book-workflows:close-plan-configuration",
            readiness.EvidenceReferences);
        yield return BuildReadinessControlRow(
            "External GL",
            readiness.ExternalGlLedgerBookNativeCertified && readiness.HasExternalGlLedgerBookNativeEvidence,
            "External GL import/export review is certified for the selected book.",
            "Retain external-GL import/export evidence for the selected book.",
            "ledger-book-workflows:external-gl",
            readiness.EvidenceReferences);
        yield return BuildReadinessControlRow(
            "Reconciliation",
            readiness.ReconciliationLedgerBookNativeCertified && readiness.HasReconciliationLedgerBookNativeEvidence,
            "Reconciliation workflows are certified for the selected book.",
            "Retain reconciliation or break-queue evidence for the selected book.",
            "ledger-book-workflows:reconciliation",
            readiness.EvidenceReferences);
        yield return BuildReadinessControlRow(
            "Direct lending projections",
            readiness.DirectLendingLedgerBookNativeCertified && readiness.HasDirectLendingLedgerBookNativeEvidence,
            "Direct-lending projections are certified for the selected book.",
            "Retain direct-lending projection evidence for the selected book.",
            "ledger-book-workflows:direct-lending",
            readiness.EvidenceReferences);
        yield return BuildReadinessControlRow(
            "Strategy ledger reads",
            readiness.StrategyLedgerReadLedgerBookNativeCertified && readiness.HasStrategyLedgerReadLedgerBookNativeEvidence,
            "Strategy ledger reads are certified for the selected book.",
            "Retain strategy-run or run-ledger evidence for the selected book.",
            "ledger-book-workflows:strategy-ledger",
            readiness.EvidenceReferences);
    }

    private static IEnumerable<AccountingWorkbenchRow> BuildProductionGapRows(
        IReadOnlyList<AccountingProductionGapDto> gaps)
    {
        foreach (var gap in gaps)
        {
            var areaText = gap.Areas.Count > 0
                ? string.Join(", ", gap.Areas.Select(static area => FormatProductionReadinessArea(area.ToString())))
                : "No readiness area";
            var blockerText = gap.BlockingIssueCodes.Count > 0
                ? string.Join(", ", gap.BlockingIssueCodes)
                : "No blocking issue codes";
            var issueText = gap.Issues.Count > 0
                ? string.Join(" | ", gap.Issues.Select(static issue => $"{issue.Code}: {issue.Message} -> {issue.SuggestedAction}"))
                : blockerText;
            var routeText = gap.Routes.Count > 0
                ? string.Join(", ", gap.Routes)
                : "No route";

            yield return new AccountingWorkbenchRow(
                gap.Label,
                $"{gap.Status} | {gap.HighestSeverity}",
                $"{gap.Summary} Areas: {areaText}.",
                $"{gap.RequiredAction} | {issueText}",
                $"{gap.Code} | {routeText}");
        }
    }

    private static IEnumerable<AccountingWorkbenchRow> BuildDimensionalReportingRows(
        AccountingDimensionalReportingReadinessDto? readiness)
    {
        if (readiness is null)
        {
            yield return new AccountingWorkbenchRow(
                "Dimensional reporting controls",
                "Unavailable",
                "Shared dimensional reporting readiness was not returned.",
                "Register production-readiness assessment before certifying dimensional reporting lanes.",
                "dimensional-reporting:unavailable");
            yield break;
        }

        yield return BuildReadinessControlRow(
            "Ledger-book scope",
            readiness.HasLedgerBookScope,
            readiness.LedgerBookId.HasValue ? $"Ledger book {readiness.LedgerBookId.Value:D}" : "Missing ledger-book scope.",
            "Select or create the governed ledger book before certifying dimensional reporting.",
            "dimensional-reporting:scope",
            readiness.EvidenceReferences);
        yield return BuildReadinessControlRow(
            "Ledger-book scoped evidence",
            readiness.HasLedgerBookScopedEvidence,
            "Retained evidence names the selected ledger book.",
            "Attach ledger-book-scoped dimensional certification evidence.",
            "dimensional-reporting:evidence",
            readiness.EvidenceReferences);
        yield return BuildReadinessControlRow(
            "Period reports",
            readiness.PeriodReportDimensionQueriesCertified && readiness.HasPeriodReportDimensionQueryEvidence,
            "Period reports retain dimensional query evidence.",
            "Retain trial-balance, financial-statement, NAV, or investor-package dimensional evidence.",
            "dimensional-reporting:period-reports",
            readiness.EvidenceReferences);
        yield return BuildReadinessControlRow(
            "Cross-period reports",
            readiness.CrossPeriodReportDimensionQueriesCertified && readiness.HasCrossPeriodReportDimensionQueryEvidence,
            "Cross-period reports retain dimensional query evidence.",
            "Retain comparative, roll-forward, or cross-period dimensional evidence.",
            "dimensional-reporting:cross-period",
            readiness.EvidenceReferences);
        yield return BuildReadinessControlRow(
            "Journal filters",
            readiness.JournalQueryDimensionFiltersCertified && readiness.HasJournalQueryDimensionFilterEvidence,
            "Journal query filters retain dimensional scope.",
            "Retain journal-query, journal-filter, or ledger-journal dimensional evidence.",
            "dimensional-reporting:journal-filters",
            readiness.EvidenceReferences);
        yield return BuildReadinessControlRow(
            "External export mappings",
            readiness.ExternalExportDimensionMappingCertified && readiness.HasExternalExportDimensionMappingEvidence,
            "External exports retain certified dimension mappings.",
            "Retain external-export or external-GL mapping dimensional evidence.",
            "dimensional-reporting:external-export",
            readiness.EvidenceReferences);
        yield return BuildReadinessControlRow(
            "Ledger-line persistence",
            readiness.LedgerLineDimensionsPersistedCertified && readiness.HasLedgerLineDimensionPersistenceEvidence,
            "Posted ledger lines retain canonical dimensions.",
            "Retain ledger-line or journal-line dimension persistence evidence.",
            "dimensional-reporting:ledger-lines",
            readiness.EvidenceReferences);
        yield return BuildReadinessControlRow(
            "Trial-balance filters",
            readiness.TrialBalanceDimensionFiltersCertified && readiness.HasTrialBalanceDimensionFilterEvidence,
            "Trial-balance filters retain dimensional scope.",
            "Retain trial-balance dimension-filter or ledger-report-filter evidence.",
            "dimensional-reporting:trial-balance",
            readiness.EvidenceReferences);
        yield return BuildReadinessControlRow(
            "Report package provenance",
            readiness.ReportPackageDimensionProvenanceCertified && readiness.HasReportPackageDimensionProvenanceEvidence,
            "Report package provenance retains canonical dimensions.",
            "Retain report-line provenance, package-dimension, or NAV package evidence.",
            "dimensional-reporting:report-package",
            readiness.EvidenceReferences);
    }

    private static AccountingWorkbenchRow BuildReadinessControlRow(
        string name,
        bool complete,
        string readyDetail,
        string missingAction,
        string key,
        IReadOnlyList<string> evidenceReferences)
        => new(
            name,
            complete ? "Complete" : "Missing",
            complete ? readyDetail : missingAction,
            evidenceReferences.Count > 0 ? string.Join("; ", evidenceReferences.Take(4)) : "No retained evidence.",
            key);

    private static string FormatProductionReadinessArea(string area)
        => area
            .Replace("ExternalGl", "External GL", StringComparison.Ordinal)
            .Replace("RulesStudio", "Rules Studio", StringComparison.Ordinal)
            .Replace("PostingRules", "Posting Rules", StringComparison.Ordinal)
            .Replace("JournalLifecycle", "Journal Lifecycle", StringComparison.Ordinal)
            .Replace("DimensionalAccounting", "Dimensional Accounting", StringComparison.Ordinal)
            .Replace("CloseReporting", "Close Reporting", StringComparison.Ordinal)
            .Replace("TenantAdministration", "Tenant Administration", StringComparison.Ordinal)
            .Replace("MigrationRollout", "Migration Rollout", StringComparison.Ordinal)
            .Replace("LedgerBooks", "Ledger Books", StringComparison.Ordinal);

    private static string FormatReadinessScope(string? value)
        => string.IsNullOrWhiteSpace(value) ? "missing" : value.Trim();

    private static Guid CreateStableGuid(params string?[] parts)
    {
        var input = string.Join("|", parts.Select(static part => part?.Trim() ?? string.Empty));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        var bytes = hash[..16].ToArray();
        return new Guid(bytes);
    }

    private static string FormatMigrationArtifactKind(AccountingMigrationRunKindDto kind)
        => kind switch
        {
            AccountingMigrationRunKindDto.LedgerBookScope => "Ledger-book scope",
            AccountingMigrationRunKindDto.HistoricalJournalBackfill => "Historical journal backfill",
            AccountingMigrationRunKindDto.DimensionalBackfill => "Dimensional backfill",
            AccountingMigrationRunKindDto.AccountingConfigurationPromotion => "Configuration promotion",
            AccountingMigrationRunKindDto.CloseReportingEvidence => "Close/reporting evidence",
            _ => kind.ToString()
        };

    private static string FormatMigrationArtifactScope(AccountingMigrationRunArtifactDto artifact)
    {
        var scope = new[]
        {
            string.IsNullOrWhiteSpace(artifact.FundProfileId) ? null : $"fund {artifact.FundProfileId}",
            artifact.LedgerBookId.HasValue ? $"book {artifact.LedgerBookId.Value:D}" : "fund-level",
            FormatLedgerDimensionSet(artifact.Dimensions)
        }.Where(static item => !string.IsNullOrWhiteSpace(item));
        return string.Join("; ", scope);
    }

    private static string FormatMigrationWorkerPlanScope(AccountingMigrationRunWorkerPlanDto plan)
    {
        var scope = new[]
        {
            string.IsNullOrWhiteSpace(plan.TenantId) ? null : $"tenant {plan.TenantId}",
            string.IsNullOrWhiteSpace(plan.CompanyId) ? null : $"company {plan.CompanyId}",
            string.IsNullOrWhiteSpace(plan.FundProfileId) ? null : $"fund {plan.FundProfileId}",
            $"book {plan.LedgerBookId:D}",
            FormatLedgerDimensionSet(plan.Dimensions),
            string.IsNullOrWhiteSpace(plan.Summary) ? null : plan.Summary
        }.Where(static item => !string.IsNullOrWhiteSpace(item));
        return string.Join("; ", scope);
    }

    private static string FormatLedgerDimensionSet(LedgerDimensionSetDto? dimensions)
    {
        if (dimensions is null)
        {
            return string.Empty;
        }

        var values = new List<string>
        {
            FormatDimension("fund", dimensions.FundId),
            FormatDimension("entity", dimensions.EntityId),
            FormatDimension("sleeve", dimensions.SleeveId),
            FormatDimension("strategy", dimensions.StrategyId),
            FormatDimension("investor", dimensions.InvestorId),
            FormatDimension("capital", dimensions.CapitalAccountId),
            dimensions.InstrumentId.HasValue ? $"instrument {dimensions.InstrumentId.Value:D}" : string.Empty,
            FormatDimension("tax lot", dimensions.TaxLotId),
            FormatDimension("cost center", dimensions.CostCenterId),
            FormatDimension("counterparty", dimensions.CounterpartyId),
            FormatDimension("organization", dimensions.OrganizationId),
            FormatDimension("portfolio", dimensions.PortfolioId),
            FormatDimension("book", dimensions.BookId),
            FormatDimension("account", dimensions.AccountId),
            FormatDimension("customer", dimensions.CustomerId),
            FormatDimension("vendor", dimensions.VendorId),
            FormatDimension("project", dimensions.ProjectId)
        };

        values.AddRange(dimensions.ExternalGlDimensions
            .OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(static pair => string.IsNullOrWhiteSpace(pair.Value) ? string.Empty : $"external {pair.Key}={pair.Value}"));
        return string.Join(", ", values.Where(static item => !string.IsNullOrWhiteSpace(item)));
    }

    private static string FormatDimension(string label, string? value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : $"{label} {value.Trim()}";

    private PostingRuleDto? ResolvePostingCandidateRule(AccountingConfigurationWorkspaceDto workspace)
    {
        var sourceEventType = $"ManualJournalEntry.{SelectedEntryType}";
        return workspace.PostingRules
                   .Where(static rule => !rule.IsArchived)
                   .Where(rule => string.Equals(rule.SourceEventType, sourceEventType, StringComparison.OrdinalIgnoreCase))
                   .OrderByDescending(static rule => rule.Priority)
                   .ThenBy(static rule => rule.RuleId, StringComparer.OrdinalIgnoreCase)
                   .FirstOrDefault()
               ?? workspace.PostingRules
                   .Where(static rule => !rule.IsArchived)
                   .OrderByDescending(static rule => rule.Priority)
                   .ThenBy(static rule => rule.RuleId, StringComparer.OrdinalIgnoreCase)
                   .FirstOrDefault();
    }

    private LedgerBookDto? ResolveActiveLedgerBook(AccountingConfigurationWorkspaceDto workspace)
        => workspace.LedgerBooks.FirstOrDefault(book => book.LedgerBookId == workspace.LedgerBookId)
           ?? workspace.LedgerBooks.FirstOrDefault();

    private LedgerDimensionSetDto BuildPostingCandidateDimensions(PostingRuleDto rule)
        => rule.Scope is { } scope
            ? scope with
            {
                FundId = string.IsNullOrWhiteSpace(scope.FundId) ? _activeFundProfile?.FundProfileId : scope.FundId,
                EntityId = string.IsNullOrWhiteSpace(scope.EntityId) ? FirstDimensionValue(_activeFundProfile?.EntityIds) : scope.EntityId
            }
            : new LedgerDimensionSetDto(
                FundId: _activeFundProfile?.FundProfileId,
                EntityId: FirstDimensionValue(_activeFundProfile?.EntityIds),
                SleeveId: FirstDimensionValue(_activeFundProfile?.SleeveIds),
                InvestorId: RequiresPrivateCapitalTreasuryContext(SelectedEntryType)
                    ? $"investor:{_activeFundProfile?.FundProfileId}:default"
                    : null,
                CapitalAccountId: RequiresPrivateCapitalTreasuryContext(SelectedEntryType)
                    ? $"capital-account:{_activeFundProfile?.FundProfileId}:default"
                    : null,
                CounterpartyId: rule.Scope?.CounterpartyId);

    private static string? FirstDimensionValue(IReadOnlyList<string>? values)
        => values?.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value))?.Trim();

    private IReadOnlyList<string> BuildPostingCandidateEvidenceLinks(PostingRuleDto rule)
    {
        var links = new List<string>();
        links.AddRange(NormalizeEvidenceLink(DraftEvidenceLink));
        links.Add($"accounting-rule://{rule.RuleId}/{rule.RuleVersion}");
        links.Add($"desktop://accounting/configure/posting-candidate/{rule.RuleId}");
        return links
            .Where(static link => !string.IsNullOrWhiteSpace(link))
            .Select(static link => link.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private void ApplyPostingCandidateResult(
        PostingRuleDto rule,
        PostingRuleJournalCandidateResultDto result,
        Guid correlationId,
        Guid sourceEventId)
    {
        var rows = new List<AccountingWorkbenchRow>
        {
            new(
                result.SelectedRuleId ?? rule.RuleId,
                result.SelectedRuleVersion ?? rule.RuleVersion,
                $"{result.DryRunResult.SourceEventType} | priority match | correlation {correlationId:D}",
                $"{result.DryRunResult.RuleMatches.Count} dry-run match(es); source event {sourceEventId:D}",
                result.SelectedRuleId ?? rule.RuleId),
            new(
                "Draft command",
                result.PostingCommand?.ApprovalState.ToString() ?? "Blocked",
                result.JournalEntryId is null
                    ? "No journal draft candidate was created."
                    : $"Journal draft {result.JournalEntryId:D}; debits {result.TotalDebits:0.##}; credits {result.TotalCredits:0.##}; imbalance {result.Imbalance:0.##}",
                $"Submit for approval: {result.CanSubmitForApproval}; auto-post: {result.CanPostWithoutAdditionalApproval}; posting remains JE lifecycle-gated.",
                result.PostingCommand?.CommandId.ToString("D") ?? string.Empty),
            new(
                "Evidence",
                $"{result.EvidenceLinks.Count} retained link(s)",
                string.Join("; ", result.EvidenceLinks.Take(4)),
                result.EvidenceLinks.Count > 4 ? $"{result.EvidenceLinks.Count - 4} additional link(s)" : "All links shown",
                string.Empty)
        };

        rows.AddRange(result.GeneratedPostingLines.Select(line =>
            new AccountingWorkbenchRow(
                line.AccountPath,
                line.Side.ToString(),
                $"{line.Amount:0.##} {line.Currency} | dimensions {FormatLedgerDimensions(line.Dimensions)}",
                line.Description ?? line.AmountFormulaId,
                line.LineId)));

        rows.AddRange(result.Issues.Select(issue =>
            new AccountingWorkbenchRow(
                issue.Code,
                issue.BlocksCandidate ? "Blocking" : issue.Severity.ToString(),
                issue.Message,
                issue.SuggestedAction ?? string.Empty,
                issue.TargetId ?? string.Empty)));

        PostingCandidateRows.ReplaceWith(rows);
        PostingCandidateStatusText = result.HasBlockingIssues
            ? $"Posting candidate is blocked by {result.Issues.Count(issue => issue.BlocksCandidate)} issue(s); no posting command is executable."
            : $"Posting candidate built for {result.SelectedRuleId ?? rule.RuleId}; draft is {(result.CanSubmitForApproval ? "approval-ready" : "validation-gated")} and not posted.";
        PostingCandidateDetailText =
            $"{result.GeneratedPostingLines.Count} generated line(s), debits {result.TotalDebits:0.##}, credits {result.TotalCredits:0.##}, {result.EvidenceLinks.Count} evidence link(s); posting remains governed by JE lifecycle.";
    }

    private JournalEntryLifecycleActionRequestDto BuildManualJournalLifecycleRequest(
        ManualJournalEntryDraftDto draft,
        JournalEntryLifecycleActionDto action)
    {
        var notes = action switch
        {
            JournalEntryLifecycleActionDto.Approve => "Approved from WPF Accounting Configure with retained reviewer evidence.",
            JournalEntryLifecycleActionDto.Post => "Posted from WPF Accounting Configure after approval review.",
            JournalEntryLifecycleActionDto.Reverse => "Reverse from WPF Accounting Configure with retained correction review.",
            JournalEntryLifecycleActionDto.Rebook => "Rebook from WPF Accounting Configure with retained correction review.",
            JournalEntryLifecycleActionDto.LockAfterClose => "Close lock from WPF Accounting Configure with retained period-lock evidence.",
            _ => $"Lifecycle action {action} from WPF Accounting Configure."
        };
        return new JournalEntryLifecycleActionRequestDto(
            draft.JournalEntryId,
            draft.FundProfileId,
            action,
            DefaultLifecycleActor,
            draft.Version,
            notes,
            CorrelationId: $"wpf-manual-je-{action.ToString().ToLowerInvariant()}",
            EvidenceLinks: BuildManualJournalLifecycleEvidenceLinks(draft, action),
            RebookLines: action == JournalEntryLifecycleActionDto.Rebook
                ? BuildManualJournalRebookLines(draft)
                : [],
            LedgerBookId: draft.LedgerBookId);
    }

    private static IReadOnlyList<string> BuildManualJournalLifecycleEvidenceLinks(
        ManualJournalEntryDraftDto draft,
        JournalEntryLifecycleActionDto action)
    {
        var journalId = draft.JournalEntryId.ToString("D");
        var period = string.IsNullOrWhiteSpace(draft.PeriodId)
            ? draft.AccountingDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : draft.PeriodId;
        var actionToken = action.ToString().ToLowerInvariant();
        var ledgerBookScope = draft.LedgerBookId.HasValue
            ? $"ledger-book/{draft.LedgerBookId.Value:D}"
            : "ledger-book/fund-level";
        var required = action switch
        {
            JournalEntryLifecycleActionDto.Approve => $"approval://manual-je/{journalId}/review-approval/{period}/{ledgerBookScope}",
            JournalEntryLifecycleActionDto.Post => $"posting://manual-je/{journalId}/ledger-posting-review/{period}/{ledgerBookScope}",
            JournalEntryLifecycleActionDto.Reverse => $"correction://manual-je/{journalId}/reversal-review/{period}/{ledgerBookScope}",
            JournalEntryLifecycleActionDto.Rebook => $"correction://manual-je/{journalId}/rebook-review/{period}/{ledgerBookScope}",
            JournalEntryLifecycleActionDto.LockAfterClose => $"close://manual-je/{journalId}/period-lock-close-certification/{period}/{ledgerBookScope}",
            JournalEntryLifecycleActionDto.Reject => $"rejection://manual-je/{journalId}/review-rejection/{period}/{ledgerBookScope}",
            _ => $"review://manual-je/{journalId}/{actionToken}/{period}/{ledgerBookScope}"
        };

        return draft.EvidenceLinks
            .Append(required)
            .Where(static link => !string.IsNullOrWhiteSpace(link))
            .Select(static link => link.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<ManualJournalEntryLineDto> BuildManualJournalRebookLines(ManualJournalEntryDraftDto draft)
        => draft.Lines
            .Select(line => line with
            {
                LineId = $"rebook-{line.LineId}",
                Description = string.IsNullOrWhiteSpace(line.Description)
                    ? "Rebooked line from WPF Accounting Configure."
                    : $"{line.Description} Rebooked from WPF Accounting Configure.",
                EvidenceLink = line.EvidenceLink ?? draft.EvidenceLinks.FirstOrDefault()
            })
            .ToArray();

    private void ApplyManualJournalLifecycleRows(JournalEntryLifecycleActionResultDto result)
    {
        var rows = new List<AccountingWorkbenchRow>
        {
            new(
                result.Transition.Action.ToString(),
                $"{result.Transition.FromStatus} -> {result.Transition.ToStatus}",
                $"{result.Transition.Actor} at {result.Transition.RecordedAtUtc.ToLocalTime():g}",
                $"{result.Transition.EvidenceLinks.Count} evidence link(s); {result.Transition.Notes ?? "no notes"}",
                result.Transition.TransitionId)
        };
        rows.AddRange(result.GeneratedJournalEntries.Select(entry =>
            new AccountingWorkbenchRow(
                entry.JournalEntryId.ToString("D"),
                entry.Status.ToString(),
                $"{FormatManualJournalEntryType(entry.EntryType)} | {entry.Memo}",
                $"{entry.TotalDebits:0.##} / {entry.TotalCredits:0.##} {entry.Currency}; reversal {entry.ReversalOfJournalEntryId?.ToString("D") ?? "none"}; rebook {entry.RebookedFromJournalEntryId?.ToString("D") ?? "none"}",
                entry.JournalEntryId.ToString("D"))));
        rows.AddRange((result.JournalEntry.LifecycleTransitions ?? [])
            .Reverse()
            .Take(8)
            .Select(transition => new AccountingWorkbenchRow(
                transition.Action.ToString(),
                $"{transition.FromStatus} -> {transition.ToStatus}",
                $"{transition.Actor} at {transition.RecordedAtUtc.ToLocalTime():g}",
                $"{transition.EvidenceLinks.Count} evidence link(s); {transition.Notes ?? "no notes"}",
                transition.TransitionId)));

        ManualJournalLifecycleRows.ReplaceWith(rows);
    }

    private async Task LoadManualJournalWorkbenchAsync(CancellationToken ct)
    {
        if (_activeFundProfile is null)
        {
            return;
        }

        var workbench = await _manualJournalEntryWorkbenchService
            .GetWorkbenchAsync(_activeFundProfile.FundProfileId, _configuration?.LedgerBookId, ct)
            .ConfigureAwait(false);

        ManualJournalDraftRows.ReplaceWith(workbench.Drafts.Select(draft =>
            new AccountingWorkbenchRow(
                $"{FormatManualJournalEntryType(draft.EntryType)} | {draft.Memo}",
                draft.Status.ToString(),
                $"{draft.TotalDebits:0.##} / {draft.TotalCredits:0.##} {draft.Currency}",
                $"{draft.ValidationIssues.Count} issue(s) | v{draft.Version}",
                draft.JournalEntryId.ToString("D"))));
        ApplyPrivateCapitalActivityRows(workbench.PrivateCapitalActivity);
        await LoadCapitalAccountWorkbenchAsync(ct).ConfigureAwait(false);
        _selectedDraft ??= workbench.Drafts.FirstOrDefault();
        ApplyManualJournalLifecycleRows(_selectedDraft);
        ManualJournalStatusText = BuildManualJournalStatusText(workbench);
        RaisePropertyChanged(nameof(CanSubmitManualJournal));
        RaiseManualJournalDraftComputedProperties();
    }

    private void ApplyManualJournalLifecycleRows(ManualJournalEntryDraftDto? draft)
    {
        if (draft is null)
        {
            ManualJournalLifecycleRows.Clear();
            return;
        }

        ManualJournalLifecycleRows.ReplaceWith(draft.LifecycleTransitions
            .Reverse()
            .Take(10)
            .Select(transition => new AccountingWorkbenchRow(
                transition.Action.ToString(),
                $"{transition.FromStatus} -> {transition.ToStatus}",
                $"{transition.Actor} at {transition.RecordedAtUtc.ToLocalTime():g}",
                $"{transition.EvidenceLinks.Count} evidence link(s); {transition.Notes ?? "no notes"}",
                transition.TransitionId)));
    }

    private void RaiseManualJournalDraftComputedProperties()
    {
        RaisePropertyChanged(nameof(ManualJournalDraftReferenceText));
        RaisePropertyChanged(nameof(ManualJournalDraftDateText));
        RaisePropertyChanged(nameof(ManualJournalDraftPeriodText));
        RaisePropertyChanged(nameof(ManualJournalDraftBalanceText));
        RaisePropertyChanged(nameof(ManualJournalDraftBalanceBadgeText));
    }

    private async Task LoadManualJournalPeriodAsync(CancellationToken ct)
    {
        _selectedManualJournalPeriod = null;
        if (_ledgerBookService is null || _configuration is null)
        {
            RaisePropertyChanged(nameof(ManualJournalDraftPeriodText));
            return;
        }

        var ledgerBookId = ResolveActiveLedgerBook(_configuration)?.LedgerBookId ?? _configuration.LedgerBookId;
        if (!ledgerBookId.HasValue)
        {
            RaisePropertyChanged(nameof(ManualJournalDraftPeriodText));
            return;
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var openPeriods = await _ledgerBookService.ListOpenPeriodsAsync(ledgerBookId.Value, ct).ConfigureAwait(false);
        _selectedManualJournalPeriod = openPeriods
            .OrderByDescending(period => period.StartDate <= today && today <= period.EndDate)
            .ThenByDescending(period => period.StartDate)
            .ThenByDescending(period => period.PeriodNo)
            .FirstOrDefault();
        RaisePropertyChanged(nameof(ManualJournalDraftPeriodText));
    }

    private async Task LoadCapitalAccountWorkbenchAsync(CancellationToken ct)
    {
        if (_activeFundProfile is null)
        {
            ClearCapitalAccountWorkbenchRows();
            CapitalAccountWorkbenchStatusText = "Select a fund-linked context before loading the Capital Account Workbench.";
            return;
        }

        if (_capitalAccountWorkbenchService is null)
        {
            ClearCapitalAccountWorkbenchRows();
            CapitalAccountWorkbenchStatusText = "Capital Account Workbench service is not registered in this desktop session.";
            return;
        }

        var workbench = await _capitalAccountWorkbenchService
            .GetWorkbenchAsync(_activeFundProfile.FundProfileId, _configuration?.LedgerBookId, ct: ct)
            .ConfigureAwait(false);

        ApplyCapitalAccountWorkbenchRows(workbench);
    }

    private void ApplyCapitalAccountWorkbenchRows(CapitalAccountWorkbenchDto workbench)
    {
        CapitalAccountInvestorRows.ReplaceWith(workbench.InvestorAccounts.Select(account =>
        {
            var payment = account.PaymentIntentEvidence is null
                ? "payment evidence not included"
                : $"{account.PaymentIntentEvidence.Status} | {account.PaymentIntentEvidence.Summary}";
            return new AccountingWorkbenchRow(
                account.CapitalAccountId,
                account.ReadinessLabel,
                $"{FormatSignedCurrencyAmount(account.OpeningNetActivity, account.Currency)} opening -> {FormatSignedCurrencyAmount(account.EndingNetActivity, account.Currency)} ending | {FormatSignedCurrencyAmount(account.NetCapitalActivity, account.Currency)} net",
                $"{account.FundEventCount} event(s); {account.PostedFundEventCount} posted; {account.PublishedReportOutputCount} published statement(s); {account.EvidenceCategorySummary} | {account.EvidenceLinkCount} evidence | {payment}",
                account.ActivityRoute);
        }));

        CapitalAccountAllocationRuleRows.ReplaceWith(workbench.AllocationRules.Select(rule =>
            new AccountingWorkbenchRow(
                rule.Label,
                rule.IsSatisfied ? "Satisfied" : "Needs evidence",
                $"{rule.CapitalAccountId} | {rule.InvestorId ?? "unassigned investor"} | {rule.Basis}",
                $"{rule.Reason} | {rule.EvidenceLinkCount} evidence | required: {FormatRequiredEvidence(rule.RequiredEvidence)}",
                rule.Route ?? string.Empty)));

        CapitalAccountStatementLineageRows.ReplaceWith(workbench.StatementLineage.Select(lineage =>
        {
            var published = lineage.PublishedAtUtc is null
                ? lineage.ReportWorkflowState ?? "not published"
                : $"{lineage.ReportWorkflowState ?? "Published"} by {lineage.PublishedBy ?? "unknown"} at {lineage.PublishedAtUtc.Value.ToLocalTime():g}";
            var restatement = lineage.HasRestatementLineage
                ? $"{lineage.RestatementReasonCode ?? "Restated"} | {lineage.RestatementChangedLineCount} changed line(s) | {lineage.RestatementEvidenceLinkCount} evidence"
                : lineage.RestatementStatus;
            return new AccountingWorkbenchRow(
                lineage.DisplayName,
                lineage.HasRestatementLineage ? "Restated" : lineage.IsPublished ? "Published" : lineage.IsReportReady ? "Ready" : "Review",
                $"{lineage.ReportOutputType} | {published} | {lineage.ReportLineProvenanceCount} provenance line(s)",
                $"{restatement} | manifest {lineage.PublicationManifestId ?? "none"} | retained {lineage.RetainedManifestPath ?? "none"} | hash {lineage.PublicationEvidenceHash ?? "none"}",
                lineage.ReportOutputRoute ?? lineage.ReportRoute);
        }));

        CapitalAccountAuditDrillThroughRows.ReplaceWith(workbench.AuditDrillThroughs.Select(drill =>
            new AccountingWorkbenchRow(
                drill.Label,
                drill.IsAvailable ? "Available" : "Missing route",
                $"{drill.Kind} | {drill.Summary}",
                $"{drill.EvidenceLinkCount} evidence | related: {FormatRequiredEvidence(drill.RelatedIds)}",
                drill.Route ?? string.Empty)));

        CapitalAccountCapabilityRows.ReplaceWith(
            workbench.LiveCapabilities
                .Select(item => new AccountingWorkbenchRow(item, "Live", "v0.18 Capital Account Workbench slice", "Backed by shared service records and browser/WPF rows.", workbench.WorkbenchRoute))
                .Concat(workbench.PlannedCapabilities.Select(item => new AccountingWorkbenchRow(item, "Planned", "Deferred outside this narrow slice.", "Not represented as live runtime behavior.", string.Empty))));

        CapitalAccountWorkbenchStatusText =
            $"{workbench.StatusLabel}: {workbench.StatusReason} {workbench.InvestorAccountCount} investor account(s), {workbench.AllocationRules.Count} allocation rule(s), {workbench.StatementCount} statement lineage row(s), {workbench.RestatementLineageCount} restatement row(s), {workbench.AuditDrillThroughCount} audit drill-through(s).";
    }

    private async Task LoadOperationsPostureAsync(CancellationToken ct)
    {
        EvidenceRows.Clear();
        ReconciliationRows.Clear();
        if (_fundOperationsWorkspaceReadService is null || _activeFundProfile is null)
        {
            CloseReadinessText = "Operations continuity service is not registered.";
            CloseDetailText = "Close, evidence, and reconciliation posture cannot be loaded in this desktop session.";
            EvidencePostureText = "No accounting-record evidence summary loaded.";
            ReconciliationPostureText = "No reconciliation queue loaded.";
            return;
        }

        var workspace = await _fundOperationsWorkspaceReadService
            .GetWorkspaceAsync(new FundOperationsWorkspaceQuery(_activeFundProfile.FundProfileId, Currency: _activeFundProfile.BaseCurrency))
            .ConfigureAwait(false);

        var governance = workspace.Governance;
        CloseReadinessText = governance?.CloseReadiness ?? "Close readiness is not available.";
        CloseDetailText = governance is null
            ? "Shared governance lifecycle projection did not return close state."
            : $"{governance.DecisionPosture} {governance.SignoffPosture}".Trim();
        EvidencePostureText = governance?.AccountingRecordSummary is null
            ? $"{governance?.EvidenceReferenceCount ?? 0} evidence reference(s) linked to the governance lifecycle."
            : governance.AccountingRecordSummary.Summary;

        if (governance?.AccountingRecordSummary is { } record)
        {
            EvidenceRows.ReplaceWith(record.EvidenceCategories.Select(category =>
                new AccountingWorkbenchRow(
                    category.Label,
                    category.IsComplete ? "Complete" : "Required",
                    category.Status,
                    category.RouteHint ?? string.Empty,
                    category.Key)));
        }

        ReconciliationPostureText = $"{workspace.Reconciliation.OpenBreakCount} open break(s), {workspace.Reconciliation.SecurityCoverageIssueCount} security coverage issue(s), {workspace.Reconciliation.RunCount} run(s).";
        _reconciliationSourceRows = workspace.Reconciliation.BreakQueue?.Items ?? [];
        ApplyReconciliationView();
    }

    private IReadOnlyList<ReconciliationBreakQueueProjectionItemDto> _reconciliationSourceRows = [];

    private void ApplyReconciliationView()
    {
        var rows = _reconciliationSourceRows;
        rows = SelectedReconciliationView switch
        {
            "Awaiting evidence" => rows.Where(row => row.EvidenceCount == 0 || string.IsNullOrWhiteSpace(row.EvidenceReference)).ToArray(),
            "Critical or SLA" => rows.Where(row => row.Severity == ReconciliationBreakSeverity.Critical || row.SlaState != ReconciliationCaseSlaState.OnTrack).ToArray(),
            "All" => rows,
            _ => rows.Where(row => row.Status is ReconciliationBreakQueueStatus.Open or ReconciliationBreakQueueStatus.InReview).ToArray()
        };

        ReconciliationRows.ReplaceWith(rows.Take(100).Select(row =>
            new AccountingWorkbenchRow(
                row.BreakId,
                $"{row.Status} | {row.Severity}",
                row.Owner ?? "Unassigned",
                row.EvidenceReference ?? "Evidence required",
                row.RoutingTarget ?? string.Empty)));
        BatchReconciliationActionText = ReconciliationRows.Count == 0
            ? "No rows match this reconciliation view."
            : $"{ReconciliationRows.Count} row(s) prepared for batch owner/evidence review; mutations stay on the shared reconciliation endpoint.";
    }

    private async Task LoadPolicyRowsAsync(CancellationToken ct)
    {
        PolicyRows.Clear();
        if (_accountingPolicyService is null)
        {
            PolicyPostureText = "Accounting policy service is not registered.";
            return;
        }

        var policies = await _accountingPolicyService.ListPoliciesAsync(ct: ct).ConfigureAwait(false);
        PolicyRows.ReplaceWith(policies.Select(policy =>
            new AccountingWorkbenchRow(
                policy.DisplayName,
                policy.AccountingBasis.ToString(),
                policy.IsDefault ? "Default" : "Override",
                policy.FundProfileId ?? "Global",
                $"{policy.PolicyId}:{policy.Version}")));
        PolicyPostureText = $"{policies.Count} accounting basis policy record(s) available; create fund-scoped overrides from this workbench.";
    }

    private ManualJournalEntryDraftDto BuildManualJournalDraft()
    {
        var now = DateTimeOffset.UtcNow;
        var fundProfileId = _activeFundProfile?.FundProfileId ?? "default-fund";
        var currency = string.IsNullOrWhiteSpace(DraftCurrency) ? "USD" : DraftCurrency.Trim().ToUpperInvariant();
        var ledgerBookId = _configuration?.LedgerBookId ?? _configuration?.LedgerBooks.FirstOrDefault()?.LedgerBookId;
        var journalEntryId = _selectedDraft?.JournalEntryId ?? Guid.NewGuid();
        var accountingDate = DateOnly.FromDateTime(DateTime.UtcNow);
        var periodId = _selectedDraft?.PeriodId
            ?? _selectedManualJournalPeriod?.PeriodId.ToString("D", CultureInfo.InvariantCulture)
            ?? CreateStableGuid(
                "manual-je-period",
                fundProfileId,
                ledgerBookId?.ToString("D") ?? "fund-level",
                accountingDate.ToString("yyyy-MM", CultureInfo.InvariantCulture)).ToString("D");
        var entityId = (_activeFundProfile?.EntityIds ?? []).FirstOrDefault();
        var dimensions = new LedgerDimensionSetDto(
            FundId: fundProfileId,
            EntityId: entityId,
            SleeveId: (_activeFundProfile?.SleeveIds ?? []).FirstOrDefault());
        return new ManualJournalEntryDraftDto(
            journalEntryId,
            _selectedDraft?.Status ?? ManualJournalEntryStatusDto.Draft,
            fundProfileId,
            ledgerBookId,
            SelectedAccountingBasis,
            accountingDate,
            PeriodId: periodId,
            EntityId: entityId,
            FundNodeId: null,
            currency,
            DraftMemo,
            DefaultActor,
            _selectedDraft?.CreatedAtUtc ?? now,
            now,
            _selectedDraft?.Version ?? 0,
            [
                new ManualJournalEntryLineDto("debit-line", AccountingTemplateLineSideDto.Debit, DraftAmount, currency, DraftDebitAccountPath, EntityId: entityId, Description: "Desktop debit line", EvidenceLink: NormalizeEvidenceLink(DraftEvidenceLink).FirstOrDefault(), Dimensions: dimensions),
                new ManualJournalEntryLineDto("credit-line", AccountingTemplateLineSideDto.Credit, DraftAmount, currency, DraftCreditAccountPath, EntityId: entityId, Description: "Desktop credit line", EvidenceLink: NormalizeEvidenceLink(DraftEvidenceLink).FirstOrDefault(), Dimensions: dimensions)
            ],
            NormalizeEvidenceLink(DraftEvidenceLink),
            ValidationIssues: [],
            EvidenceAttachments: NormalizeEvidenceLink(DraftEvidenceLink)
                .Select(link => new ManualJournalEntryEvidenceAttachmentDto(
                    "desktop-evidence",
                    "Desktop evidence link",
                    "SourceDocument",
                    link,
                    "WPF",
                    now,
                    DefaultActor))
                .ToArray(),
            EntryType: SelectedEntryType,
            TreasuryContext: BuildManualJournalTreasuryContext(fundProfileId, SelectedEntryType, accountingDate, journalEntryId),
            Dimensions: dimensions);
    }

    private void ApplyManualJournalEntryTypePreset(ManualJournalEntryTypeDto entryType)
    {
        var preset = GetManualJournalEntryTypePreset(entryType);
        DraftMemo = preset.Memo;
        DraftDebitAccountPath = preset.DebitAccountPath;
        DraftCreditAccountPath = preset.CreditAccountPath;
        DraftEvidenceLink = preset.EvidenceLink;
    }

    private static IReadOnlyList<ChartOfAccountsNodeDto> BuildManualJournalEntryTypeChartNodes()
        =>
        [
            new ChartOfAccountsNodeDto("liabilities-accrued-expenses", "Liabilities:Accrued Expenses", "Accrued Expenses", "Liability"),
            new ChartOfAccountsNodeDto("assets-prepaid-expenses", "Assets:Prepaid Expenses", "Prepaid Expenses", "Asset"),
            new ChartOfAccountsNodeDto("expenses-operating-expenses", "Expenses:Operating Expenses", "Operating Expenses", "Expense"),
            new ChartOfAccountsNodeDto("expenses-amortization-expense", "Expenses:Amortization Expense", "Amortization Expense", "Expense"),
            new ChartOfAccountsNodeDto("assets-accumulated-amortization", "Assets:Accumulated Amortization", "Accumulated Amortization", "ContraAsset"),
            new ChartOfAccountsNodeDto("liabilities-deferred-revenue", "Liabilities:Deferred Revenue", "Deferred Revenue", "Liability"),
            new ChartOfAccountsNodeDto("equity-capital-contributions", "Equity:Capital Contributions", "Capital Contributions", "Equity"),
            new ChartOfAccountsNodeDto("equity-distributions", "Equity:Distributions", "Distributions", "Equity"),
            new ChartOfAccountsNodeDto("assets-subscription-receivable", "Assets:Subscription Receivable", "Subscription Receivable", "Asset"),
            new ChartOfAccountsNodeDto("equity-redemptions", "Equity:Redemptions", "Redemptions", "Equity"),
            new ChartOfAccountsNodeDto("equity-lp-transfer-out", "Equity:LP Transfer Out", "LP Transfer Out", "Equity"),
            new ChartOfAccountsNodeDto("equity-lp-transfer-in", "Equity:LP Transfer In", "LP Transfer In", "Equity"),
            new ChartOfAccountsNodeDto("expenses-management-fees", "Expenses:Management Fees", "Management Fees", "Expense")
        ];

    private static IReadOnlyList<JournalEntryTemplateDto> BuildManualJournalEntryTypeTemplates(string currency)
    {
        var normalizedCurrency = string.IsNullOrWhiteSpace(currency) ? "USD" : currency.Trim().ToUpperInvariant();
        return ManualJournalEntryTypePresets
            .Where(preset => preset.EntryType != ManualJournalEntryTypeDto.General)
            .Select(preset => new JournalEntryTemplateDto(
                GetManualJournalEntryTemplateId(preset.EntryType),
                $"{preset.Label} desktop template",
                preset.Description,
                [
                    new JournalEntryTemplateLineDto(
                        $"{GetManualJournalEntrySlug(preset.EntryType)}-debit",
                        preset.DebitAccountPath,
                        AccountingTemplateLineSideDto.Debit,
                        1m,
                        normalizedCurrency,
                        $"Debit {preset.DebitAccountPath}."),
                    new JournalEntryTemplateLineDto(
                        $"{GetManualJournalEntrySlug(preset.EntryType)}-credit",
                        preset.CreditAccountPath,
                        AccountingTemplateLineSideDto.Credit,
                        1m,
                        normalizedCurrency,
                        $"Credit {preset.CreditAccountPath}.")
                ]))
            .ToArray();
    }

    private static IReadOnlyList<PostingRuleDto> BuildManualJournalEntryTypePostingRules()
        => ManualJournalEntryTypePresets
            .Where(preset => preset.EntryType != ManualJournalEntryTypeDto.General)
            .Select(preset => new PostingRuleDto(
                GetManualJournalEntryPostingRuleId(preset.EntryType),
                $"{preset.Label} policy",
                $"ManualJournalEntry.{preset.EntryType}",
                GetManualJournalEntryTemplateId(preset.EntryType),
                Description: $"Fund/book configurable WPF policy route for {preset.Label.ToLowerInvariant()} manual journal entries."))
            .ToArray();

    private static ManualJournalEntryTypePreset GetManualJournalEntryTypePreset(ManualJournalEntryTypeDto entryType)
        => ManualJournalEntryTypePresets.FirstOrDefault(preset => preset.EntryType == entryType)
            ?? ManualJournalEntryTypePresets[0];

    private static string FormatManualJournalEntryType(ManualJournalEntryTypeDto entryType)
        => GetManualJournalEntryTypePreset(entryType).Label;

    private static string GetManualJournalEntryTemplateId(ManualJournalEntryTypeDto entryType)
        => entryType == ManualJournalEntryTypeDto.General
            ? "desktop-manual-adjustment-v1"
            : $"desktop-{GetManualJournalEntrySlug(entryType)}-v1";

    private static string GetManualJournalEntryPostingRuleId(ManualJournalEntryTypeDto entryType)
        => entryType == ManualJournalEntryTypeDto.General
            ? "manual-adjustment-policy-v1"
            : $"manual-{GetManualJournalEntrySlug(entryType)}-policy-v1";

    private static string GetManualJournalEntrySlug(ManualJournalEntryTypeDto entryType)
        => entryType switch
        {
            ManualJournalEntryTypeDto.AccruedBalance => "accrued-balance",
            ManualJournalEntryTypeDto.AccruedExpense => "accrued-expense",
            ManualJournalEntryTypeDto.PrepaidExpense => "prepaid-expense",
            ManualJournalEntryTypeDto.Expense => "expense",
            ManualJournalEntryTypeDto.Amortization => "amortization",
            ManualJournalEntryTypeDto.Deferral => "deferral",
            ManualJournalEntryTypeDto.Reclassification => "reclassification",
            ManualJournalEntryTypeDto.Reversal => "reversal",
            ManualJournalEntryTypeDto.CapitalCall => "capital-call",
            ManualJournalEntryTypeDto.Distribution => "distribution",
            ManualJournalEntryTypeDto.Subscription => "subscription",
            ManualJournalEntryTypeDto.Redemption => "redemption",
            ManualJournalEntryTypeDto.LpTransfer => "lp-transfer",
            ManualJournalEntryTypeDto.ManagementFee => "management-fee",
            _ => "manual-adjustment"
        };

    private static TreasuryLedgerContextDto? BuildManualJournalTreasuryContext(
        string fundProfileId,
        ManualJournalEntryTypeDto entryType,
        DateOnly accountingDate,
        Guid journalEntryId)
    {
        if (!RequiresPrivateCapitalTreasuryContext(entryType))
        {
            return null;
        }

        var normalizedFundProfileId = string.IsNullOrWhiteSpace(fundProfileId) ? "default-fund" : fundProfileId.Trim();
        var slug = GetManualJournalEntrySlug(entryType);
        var dateToken = accountingDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var entryToken = journalEntryId.ToString("N");
        return new TreasuryLedgerContextDto(
            EffectiveDate: accountingDate,
            IdempotencyKey: $"wpf:{normalizedFundProfileId}:{slug}:{entryToken}",
            FundEventId: $"fund-event:{normalizedFundProfileId}:{slug}:{dateToken}",
            FundEventType: entryType.ToString(),
            CapitalAccountId: $"capital-account:{normalizedFundProfileId}:default",
            InvestorId: $"investor:{normalizedFundProfileId}:default",
            PaymentIntentId: RequiresPaymentLinkage(entryType) ? $"payment:{normalizedFundProfileId}:{slug}:{entryToken}" : null,
            SettlementReference: RequiresPaymentLinkage(entryType) ? $"settlement:{normalizedFundProfileId}:{slug}:{dateToken}" : null);
    }

    private static bool RequiresPrivateCapitalTreasuryContext(ManualJournalEntryTypeDto entryType)
        => entryType is ManualJournalEntryTypeDto.CapitalCall
            or ManualJournalEntryTypeDto.Distribution
            or ManualJournalEntryTypeDto.Subscription
            or ManualJournalEntryTypeDto.Redemption
            or ManualJournalEntryTypeDto.LpTransfer
            or ManualJournalEntryTypeDto.ManagementFee;

    private static bool RequiresPaymentLinkage(ManualJournalEntryTypeDto entryType)
        => entryType is ManualJournalEntryTypeDto.CapitalCall
            or ManualJournalEntryTypeDto.Distribution
            or ManualJournalEntryTypeDto.Redemption
            or ManualJournalEntryTypeDto.ManagementFee;

    private static AccountingTreatmentKindDto MapPostingCandidateTreatment(ManualJournalEntryTypeDto entryType)
        => entryType switch
        {
            ManualJournalEntryTypeDto.AccruedBalance or ManualJournalEntryTypeDto.AccruedExpense => AccountingTreatmentKindDto.Accrual,
            ManualJournalEntryTypeDto.PrepaidExpense => AccountingTreatmentKindDto.PrepaidExpense,
            ManualJournalEntryTypeDto.Expense or ManualJournalEntryTypeDto.ManagementFee => AccountingTreatmentKindDto.Expense,
            ManualJournalEntryTypeDto.Amortization => AccountingTreatmentKindDto.Amortization,
            ManualJournalEntryTypeDto.Deferral => AccountingTreatmentKindDto.Deferral,
            ManualJournalEntryTypeDto.Reclassification => AccountingTreatmentKindDto.Reclassification,
            ManualJournalEntryTypeDto.Reversal => AccountingTreatmentKindDto.Reversal,
            _ => AccountingTreatmentKindDto.General
        };

    private void ApplyManualJournalValidationRows(IReadOnlyList<AccountingConfigurationValidationIssueDto> issues)
    {
        ValidationRows.ReplaceWith(issues.Select(issue =>
            new AccountingWorkbenchRow(issue.Code, issue.Severity.ToString(), issue.Message, issue.SuggestedAction ?? string.Empty, issue.TargetId ?? string.Empty)));
    }

    private void ApplyPrivateCapitalActivityRows(PrivateCapitalActivityProjectionDto? activity)
    {
        if (activity is null)
        {
            ManualJournalFundEventLedgerRecordRows.Clear();
            ManualJournalCapitalAccountRows.Clear();
            ManualJournalCapitalAccountSubledgerRows.Clear();
            ManualJournalPaymentIntentRows.Clear();
            ManualJournalFundEventRows.Clear();
            ManualJournalLedgerImpactRows.Clear();
            ManualJournalReportOutputRows.Clear();
            return;
        }

        ManualJournalFundEventLedgerRecordRows.ReplaceWith(activity.FundEventRecords.Select(record =>
        {
            var reportOutput = string.IsNullOrWhiteSpace(record.PrimaryReportOutputType)
                ? "no primary report"
                : record.PrimaryReportOutputType;
            var reportWorkflow = string.IsNullOrWhiteSpace(record.ReportWorkflowState)
                ? "no workflow"
                : record.ReportWorkflowState;
            var approval = string.IsNullOrWhiteSpace(record.ApprovalId)
                ? "no approval route"
                : $"{record.ApprovalId} via {record.ApprovalRoute ?? "approval route unavailable"}";
            var nextAction = string.IsNullOrWhiteSpace(record.NextActionRoute)
                ? record.NextAction
                : $"{record.NextAction} via {record.NextActionRoute}";
            return new AccountingWorkbenchRow(
                $"{record.FundEventType} | {record.Memo}",
                record.IsPosted ? "Posted" : record.ApprovalState.ToString(),
                $"{FormatSignedCurrencyAmount(record.NetCapitalActivity, record.Currency)} net / {FormatCurrencyAmount(record.GrossAmount, record.Currency)} gross | {FormatSignedCurrencyAmount(record.CapitalAccountOpeningNetActivity, record.Currency)} opening -> {FormatSignedCurrencyAmount(record.CapitalAccountEndingNetActivity, record.Currency)} ending | {record.EffectiveDate:yyyy-MM-dd}",
                $"{record.CapitalAccountSubledgerEntryCount} subledger | {record.LedgerImpactCount} ledger | {record.ReportOutputCount} report | {reportOutput} | {reportWorkflow} | {record.ReportLineProvenanceCount} provenance | readiness {record.ReadinessLabel}: {record.ReadinessReason} | next {nextAction} | {record.EvidenceLinkCount} evidence via {record.EvidenceRoute} | approval {approval} | {record.ValidationIssueCount} issue(s)",
                record.ActivityRoute);
        }));
        ManualJournalCapitalAccountRows.ReplaceWith(activity.CapitalAccounts.Select(account =>
            new AccountingWorkbenchRow(
                account.CapitalAccountId,
                FormatSignedCurrencyAmount(account.NetActivity, account.Currency),
                $"{account.FundEventCount} event(s); last {FormatFundEventDate(account.LastFundEventType, account.LastEffectiveDate)}",
                $"Calls {FormatCurrencyAmount(account.Contributions, account.Currency)}; distributions {FormatCurrencyAmount(account.Distributions, account.Currency)}",
                account.InvestorId ?? string.Empty)));
        ManualJournalCapitalAccountSubledgerRows.ReplaceWith(activity.CapitalAccountSubledgers.Select(subledger =>
            new AccountingWorkbenchRow(
                subledger.CapitalAccountId,
                BuildCapitalAccountSubledgerStatus(subledger),
                $"{FormatSignedCurrencyAmount(subledger.OpeningNetActivity, subledger.Currency)} opening -> {FormatSignedCurrencyAmount(subledger.EndingNetActivity, subledger.Currency)} ending | {FormatSignedCurrencyAmount(subledger.NetCapitalActivity, subledger.Currency)} net | {FormatSubledgerDateRange(subledger)}",
                $"Calls {FormatCurrencyAmount(subledger.Contributions, subledger.Currency)}; distributions {FormatCurrencyAmount(subledger.Distributions, subledger.Currency)}; subscriptions {FormatCurrencyAmount(subledger.Subscriptions, subledger.Currency)}; redemptions {FormatCurrencyAmount(subledger.Redemptions, subledger.Currency)}; fees {FormatCurrencyAmount(subledger.ManagementFees, subledger.Currency)} | {subledger.FundEventCount} event(s); {subledger.ApprovalQueueCount} approval queue; {subledger.PostedFundEventCount} posted; {subledger.PublishedReportOutputCount} published report(s) | readiness {subledger.ReadinessLabel}: {subledger.ReadinessReason}; next {FormatSubledgerNextAction(subledger)} | {subledger.EvidenceLinkCount} evidence; {BuildEvidenceCategoryReadinessSummary(subledger.EvidenceCategories)} | {subledger.ValidationIssueCount} issue(s)",
                subledger.ActivityRoute)));
        ManualJournalPaymentIntentRows.ReplaceWith(activity.PaymentIntents.Select(BuildManualJournalPaymentIntentRow));
        ManualJournalFundEventRows.ReplaceWith(activity.FundEvents.Select(item =>
            new AccountingWorkbenchRow(
                $"{item.FundEventType} | {item.Memo}",
                item.IsPosted ? "Posted" : item.JournalStatus.ToString(),
                $"{FormatSignedCurrencyAmount(item.NetCapitalActivity, item.Currency)} net / {FormatCurrencyAmount(item.GrossAmount, item.Currency)} gross",
                $"{item.CapitalAccountId} | {item.EvidenceLinks.Count} evidence",
                item.FundEventId)));
        ManualJournalLedgerImpactRows.ReplaceWith(activity.LedgerImpacts.Select(impact =>
            new AccountingWorkbenchRow(
                impact.FundEventType,
                impact.IsPostingReady ? "Posting ready" : impact.IsBalanced ? "Review" : "Unbalanced",
                $"{FormatCurrencyAmount(impact.TotalDebits, impact.Currency)} debit / {FormatCurrencyAmount(impact.TotalCredits, impact.Currency)} credit",
                $"{impact.LineCount} line(s) | {impact.EvidenceLinks.Count} evidence | {impact.ValidationIssues.Count} issue(s)",
                impact.CapitalAccountId)));
        ManualJournalReportOutputRows.ReplaceWith(activity.ReportOutputs.Select(output =>
        {
            var workflow = string.IsNullOrWhiteSpace(output.ReportWorkflowState)
                ? output.ReportOutputType
                : output.ReportWorkflowState;
            var reportPack = string.IsNullOrWhiteSpace(output.ReportPackId)
                ? workflow
                : $"{workflow} | {output.ReportPackId}";
            var publication = string.IsNullOrWhiteSpace(output.PublicationManifestId)
                ? "no publication manifest"
                : output.PublicationManifestId;
            var reportOutputRoute = string.IsNullOrWhiteSpace(output.ReportOutputRoute)
                ? output.ReportRoute
                : output.ReportOutputRoute;
            var nextActionRoute = string.IsNullOrWhiteSpace(output.NextActionRoute)
                ? reportOutputRoute
                : output.NextActionRoute;
            var nextAction = string.IsNullOrWhiteSpace(output.NextAction)
                ? "Open report output"
                : output.NextAction;
            var readinessLabel = string.IsNullOrWhiteSpace(output.ReadinessLabel)
                ? output.IsPublished ? "Published" : output.IsReportReady ? "Ready" : "Review"
                : output.ReadinessLabel;
            var readinessReason = string.IsNullOrWhiteSpace(output.ReadinessReason)
                ? "No report-output readiness reason"
                : output.ReadinessReason;

            return new AccountingWorkbenchRow(
                output.DisplayName,
                readinessLabel,
                $"{FormatSignedCurrencyAmount(output.NetCapitalActivity, output.Currency)} | {output.EffectiveDate:yyyy-MM-dd} | {reportPack}",
                $"{output.EvidenceLinkCount} evidence | {output.ValidationIssues.Count} issue(s) | {output.ReportLineProvenanceCount} provenance | {publication} | readiness {readinessLabel}: {readinessReason} | next {nextAction} via {nextActionRoute}",
                nextActionRoute);
        }));
    }

    private static string BuildManualJournalValidationText(ManualJournalEntryDraftDto draft)
        => draft.ValidationIssues.Any(issue => issue.Severity == AccountingConfigurationValidationSeverityDto.Critical)
            ? $"{draft.ValidationIssues.Count(issue => issue.Severity == AccountingConfigurationValidationSeverityDto.Critical)} critical issue(s) block approval submission."
            : $"Manual journal draft is balanced at {draft.TotalDebits:0.##} / {draft.TotalCredits:0.##} {draft.Currency} and ready for approval submission.";

    private static string BuildManualJournalStatusText(ManualJournalEntryWorkbenchDto workbench)
    {
        var draftText = workbench.Drafts.Count == 0
            ? "No manual journal drafts saved yet. Drafts are durable and approval-gated."
            : $"{workbench.Drafts.Count} manual journal draft(s) loaded; latest status {workbench.Drafts[0].Status}.";
        if (workbench.PrivateCapitalActivity is not { } activity)
        {
            return draftText;
        }

        var issueText = activity.ValidationIssues.Count == 0
            ? "no projection issue(s)"
            : $"{activity.ValidationIssues.Count} projection issue(s)";

        return string.Concat(
            draftText,
            " Private-capital projection: ",
            $"{activity.FundEventCount} event(s), ",
            $"{activity.PostedFundEventCount} posted, ",
            $"{activity.FundEventRecords.Count} event record(s), ",
            $"{activity.CapitalAccountCount} capital account(s), ",
            $"{activity.CapitalAccountSubledgers.Count} account subledger(s), ",
            $"{activity.CapitalAccountSubledgerEntries.Count} subledger movement(s), ",
            $"{activity.PaymentIntents.Count} payment intent(s), ",
            $"{activity.LedgerImpacts.Count} ledger impact(s), ",
            $"{activity.ReportOutputs.Count} report output(s), ",
            $"{activity.PublishedReportOutputCount} published, ",
            issueText,
            ".");
    }

    private static AccountingWorkbenchRow BuildManualJournalPaymentIntentRow(PaymentIntentWorkflowDto intent)
        => new(
            intent.PaymentIntentId,
            intent.StatusLabel,
            $"{intent.ExpectedCashMovement.Direction} {FormatCurrencyAmount(intent.ExpectedCashMovement.Amount, intent.ExpectedCashMovement.Currency)} | {intent.ExpectedCashMovement.EffectiveDate:yyyy-MM-dd} | requester {intent.Requester} | settlement {intent.SettlementReference ?? "not assigned"}",
            $"payee {intent.ExpectedCashMovement.Payee ?? "not assigned"} | scope {intent.ExpectedCashMovement.AccountScope ?? "not assigned"} | purpose {intent.ExpectedCashMovement.BusinessPurpose ?? intent.ExpectedCashMovement.Purpose} | policy {intent.ExpectedCashMovement.ApprovalPolicy ?? "not assigned"} | {intent.ExpectedCashMovement.SourceEvidenceLinks.Count} source evidence link(s) | {intent.ApprovalChain.Count} approval step(s); {intent.BankEvidence.Count} bank/cash evidence item(s); {BuildPaymentIntentBankEvidenceRecorderText(intent.BankEvidence)} | {intent.ReconciliationLinks.Count} reconciliation link(s); {intent.AuditHistory.Count} audit event(s) | {intent.ReadinessReason} | {intent.ExecutionDeferredReason}",
            intent.EvidenceRoute);

    private static string BuildPaymentIntentBankEvidenceRecorderText(IReadOnlyList<PaymentIntentBankEvidenceDto> bankEvidence)
    {
        var recorders = bankEvidence
            .Select(static item => item.RecordedBy)
            .Where(static recorder => !string.IsNullOrWhiteSpace(recorder))
            .Select(static recorder => recorder!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToArray();

        return recorders.Length == 0
            ? "no retained recorder"
            : $"recorded by {string.Join(", ", recorders)}";
    }

    private static string FormatCurrencyAmount(decimal amount, string currency)
    {
        var code = string.IsNullOrWhiteSpace(currency) ? string.Empty : $" {currency.Trim().ToUpperInvariant()}";
        return $"{amount:0.##}{code}";
    }

    private static string FormatSignedCurrencyAmount(decimal amount, string currency)
    {
        var prefix = amount > 0m ? "+" : string.Empty;
        return $"{prefix}{FormatCurrencyAmount(amount, currency)}";
    }

    private static string BuildCapitalAccountSubledgerStatus(PrivateCapitalCapitalAccountSubledgerDto subledger)
    {
        if (!string.IsNullOrWhiteSpace(subledger.ReadinessLabel))
        {
            return subledger.ReadinessLabel;
        }

        if (subledger.ValidationIssues.Any(static issue => issue.Severity == AccountingConfigurationValidationSeverityDto.Critical))
        {
            return "Blocked";
        }

        if (subledger.ValidationIssues.Count > 0 ||
            subledger.ApprovalQueueCount > 0 ||
            subledger.EvidenceCategories.Any(static category => !category.IsReady))
        {
            return "Review";
        }

        return subledger.FundEventCount > 0 ? "Ready" : "Empty";
    }

    private static string FormatSubledgerNextAction(PrivateCapitalCapitalAccountSubledgerDto subledger)
        => string.IsNullOrWhiteSpace(subledger.NextActionRoute)
            ? subledger.NextAction
            : $"{subledger.NextAction} via {subledger.NextActionRoute}";

    private static string BuildEvidenceCategoryReadinessSummary(IReadOnlyList<PrivateCapitalEvidenceCategoryDto> categories)
    {
        if (categories.Count == 0)
        {
            return "no evidence categories";
        }

        var readyCount = categories.Count(static category => category.IsReady);
        var categoryLabels = string.Join(
            "; ",
            categories.Select(static category => $"{category.Label} {(category.IsReady ? "Ready" : "Missing")}"));
        return $"{readyCount}/{categories.Count} evidence categories ready: {categoryLabels}";
    }

    private static string FormatSubledgerDateRange(PrivateCapitalCapitalAccountSubledgerDto subledger)
    {
        var first = subledger.FirstEffectiveDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "no first date";
        var last = subledger.LastEffectiveDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "no last date";
        return $"{first} -> {last} | {subledger.LastFundEventType ?? "no last event"}";
    }

    private static string FormatRequiredEvidence(IReadOnlyList<string> values)
        => values.Count == 0 ? "none" : string.Join("; ", values);

    private static string FormatFundEventDate(string? eventType, DateOnly? effectiveDate)
        => effectiveDate is null
            ? eventType ?? "No effective date"
            : $"{eventType ?? "Fund event"} {effectiveDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";

    private static string FormatLedgerDimensions(LedgerDimensionSetDto? dimensions)
    {
        if (dimensions is null)
        {
            return "none";
        }

        var values = new[]
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
                dimensions.CounterpartyId
            }
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value!.Trim())
            .Take(5)
            .ToArray();

        return values.Length == 0 ? "none" : string.Join(" / ", values);
    }

    private string BuildStoragePosture()
    {
        var configStore = _configurationStore?.GetType().Name ?? "not registered";
        var draftStore = _manualJournalEntryDraftStore?.GetType().Name ?? "not registered";
        return $"Configuration/audit store: {configStore}; manual JE draft store: {draftStore}.";
    }

    private void ClearRows()
    {
        ValidationRows.Clear();
        ChartRows.Clear();
        TemplateRows.Clear();
        PostingRuleRows.Clear();
        RulesStudioRuleRows.Clear();
        RulesStudioPromotionRows.Clear();
        RulesStudioActionRows.Clear();
        SetupReadinessRows.Clear();
        LedgerBookRows.Clear();
        AuditRows.Clear();
        ProviderRows.Clear();
        ExternalGlEvidencePackageRows.Clear();
        ExternalGlRows.Clear();
        ManualJournalDraftRows.Clear();
        ManualJournalFundEventLedgerRecordRows.Clear();
        ManualJournalCapitalAccountRows.Clear();
        ManualJournalCapitalAccountSubledgerRows.Clear();
        ManualJournalPaymentIntentRows.Clear();
        ManualJournalFundEventRows.Clear();
        ManualJournalLedgerImpactRows.Clear();
        ManualJournalReportOutputRows.Clear();
        ManualJournalLifecycleRows.Clear();
        ProductionReadinessComponentRows.Clear();
        ProductionReadinessIssueRows.Clear();
        ProductionReadinessWorkflowRows.Clear();
        ProductionReadinessDimensionalControlRows.Clear();
        ProductionReadinessGapRows.Clear();
        ProductionReadinessMigrationArtifactRows.Clear();
        ProductionReadinessMigrationPlanRows.Clear();
        ProductionReadinessMigrationWorkerPlanRows.Clear();
        TenantAdministrationControlRows.Clear();
        ClearCapitalAccountWorkbenchRows();
        EvidenceRows.Clear();
        ReconciliationRows.Clear();
        PolicyRows.Clear();
        PostingCandidateRows.Clear();
    }

    private void ClearCapitalAccountWorkbenchRows()
    {
        CapitalAccountInvestorRows.Clear();
        CapitalAccountAllocationRuleRows.Clear();
        CapitalAccountStatementLineageRows.Clear();
        CapitalAccountAuditDrillThroughRows.Clear();
        CapitalAccountCapabilityRows.Clear();
    }

    private static IReadOnlyList<string> NormalizeEvidenceLink(string? value)
        => string.IsNullOrWhiteSpace(value) ? [] : [value.Trim()];

    private string ResolveTenantAdministrationTenantId()
        => NormalizeTenantAdministrationScope(_activeFundProfile?.FundProfileId, "desktop-tenant");

    private string ResolveTenantAdministrationCompanyId()
        => NormalizeTenantAdministrationScope(
            _activeFundProfile?.LegalEntityName
            ?? _activeFundProfile?.EntityIds?.FirstOrDefault()
            ?? _activeFundProfile?.DisplayName,
            "desktop-company");

    private static string NormalizeTenantAdministrationScope(string? value, string fallback)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        return normalized
            .Replace('|', '-')
            .Replace(Environment.NewLine, " ", StringComparison.Ordinal);
    }

    private static IReadOnlyList<string> NormalizeTenantAdministrationEvidence(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? []
            : value
                .Split(['\r', '\n', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(static item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

    private IReadOnlyList<AccountingWorkflowCertificationArtifactDto> BuildWorkflowCertificationArtifacts(
        IReadOnlyList<string> evidence,
        string correlationId)
    {
        if (_activeFundProfile is null || _configuration?.LedgerBookId is not { } ledgerBookId)
        {
            return [];
        }

        var lanes = new[]
            {
                BuildWorkflowCertificationLane(PostingRulesLedgerBookNativeCertified, AccountingWorkflowCertificationLaneKindDto.PostingRules, evidence, "posting-candidate"),
                BuildWorkflowCertificationLane(JournalLifecycleLedgerBookNativeCertified, AccountingWorkflowCertificationLaneKindDto.JournalLifecycle, evidence, "journal-lifecycle"),
                BuildWorkflowCertificationLane(CloseReportingLedgerBookNativeCertified, AccountingWorkflowCertificationLaneKindDto.CloseReporting, evidence, "close-reporting"),
                BuildWorkflowCertificationLane(ClosePlanConfigurationLedgerBookNativeCertified, AccountingWorkflowCertificationLaneKindDto.ClosePlanConfiguration, evidence, "close-plan-configuration"),
                BuildWorkflowCertificationLane(ExternalGlLedgerBookNativeCertified, AccountingWorkflowCertificationLaneKindDto.ExternalGl, evidence, "external-gl"),
                BuildWorkflowCertificationLane(ReconciliationLedgerBookNativeCertified, AccountingWorkflowCertificationLaneKindDto.Reconciliation, evidence, "reconciliation"),
                BuildWorkflowCertificationLane(DirectLendingLedgerBookNativeCertified, AccountingWorkflowCertificationLaneKindDto.DirectLendingProjection, evidence, "direct-lending"),
                BuildWorkflowCertificationLane(StrategyLedgerReadLedgerBookNativeCertified, AccountingWorkflowCertificationLaneKindDto.StrategyLedgerReads, evidence, "strategy-ledger")
            }
            .OfType<AccountingWorkflowCertificationLaneDto>()
            .ToArray();

        if (lanes.Length == 0)
        {
            return [];
        }

        return
        [
            new AccountingWorkflowCertificationArtifactDto(
                $"{correlationId}-workflow",
                AccountingCertificationArtifactStatusDto.Certified,
                ResolveTenantAdministrationTenantId(),
                ResolveTenantAdministrationCompanyId(),
                _activeFundProfile.FundProfileId,
                ledgerBookId,
                DefaultLifecycleActor,
                DateTimeOffset.UtcNow,
                "wpf-accounting-configure",
                lanes,
                evidence,
                CorrelationId: correlationId)
        ];
    }

    private IReadOnlyList<AccountingDimensionalCertificationArtifactDto> BuildDimensionalCertificationArtifacts(
        IReadOnlyList<string> evidence,
        string correlationId)
    {
        if (_activeFundProfile is null || _configuration?.LedgerBookId is not { } ledgerBookId)
        {
            return [];
        }

        var lanes = new[]
            {
                BuildDimensionalCertificationLane(LedgerLineDimensionsPersistedCertified, AccountingDimensionalCertificationLaneKindDto.LedgerLinePersistence, evidence, "ledger-line"),
                BuildDimensionalCertificationLane(TrialBalanceDimensionFiltersCertified, AccountingDimensionalCertificationLaneKindDto.TrialBalanceFilters, evidence, "trial-balance-filter"),
                BuildDimensionalCertificationLane(PeriodReportDimensionQueriesCertified, AccountingDimensionalCertificationLaneKindDto.PeriodReports, evidence, "period-report"),
                BuildDimensionalCertificationLane(CrossPeriodReportDimensionQueriesCertified, AccountingDimensionalCertificationLaneKindDto.CrossPeriodReports, evidence, "cross-period"),
                BuildDimensionalCertificationLane(JournalQueryDimensionFiltersCertified, AccountingDimensionalCertificationLaneKindDto.JournalFilters, evidence, "journal-query"),
                BuildDimensionalCertificationLane(ReportPackageDimensionProvenanceCertified, AccountingDimensionalCertificationLaneKindDto.ReportPackageProvenance, evidence, "report-package-provenance"),
                BuildDimensionalCertificationLane(ExternalExportDimensionMappingCertified, AccountingDimensionalCertificationLaneKindDto.ExternalExportMappings, evidence, "external-export")
            }
            .OfType<AccountingDimensionalCertificationLaneDto>()
            .ToArray();

        if (lanes.Length == 0)
        {
            return [];
        }

        return
        [
            new AccountingDimensionalCertificationArtifactDto(
                $"{correlationId}-dimensions",
                AccountingCertificationArtifactStatusDto.Certified,
                ResolveTenantAdministrationTenantId(),
                ResolveTenantAdministrationCompanyId(),
                _activeFundProfile.FundProfileId,
                ledgerBookId,
                "canonical-production",
                DefaultLifecycleActor,
                DateTimeOffset.UtcNow,
                "wpf-accounting-configure",
                lanes,
                evidence,
                CorrelationId: correlationId)
        ];
    }

    private static AccountingWorkflowCertificationLaneDto? BuildWorkflowCertificationLane(
        bool certified,
        AccountingWorkflowCertificationLaneKindDto kind,
        IReadOnlyList<string> evidence,
        string marker)
        => certified
            ? new AccountingWorkflowCertificationLaneDto(
                kind,
                AccountingCertificationArtifactLaneStatusDto.Passed,
                FilterCertificationLaneEvidence(evidence, marker))
            : null;

    private static AccountingDimensionalCertificationLaneDto? BuildDimensionalCertificationLane(
        bool certified,
        AccountingDimensionalCertificationLaneKindDto kind,
        IReadOnlyList<string> evidence,
        string marker)
        => certified
            ? new AccountingDimensionalCertificationLaneDto(
                kind,
                AccountingCertificationArtifactLaneStatusDto.Passed,
                FilterCertificationLaneEvidence(evidence, marker))
            : null;

    private IReadOnlyList<AccountingTenantAdminCertificationArtifactDto> BuildTenantAdminCertificationArtifacts(
        IReadOnlyList<string> evidence,
        string correlationId)
    {
        if (_activeFundProfile is null)
        {
            return [];
        }

        var lanes = new[]
            {
                BuildTenantAdminCertificationLane(TenantScopeConfigured, AccountingTenantAdminCertificationLaneKindDto.TenantScope, evidence, "tenant-scope"),
                BuildTenantAdminCertificationLane(AdminRoleProfileConfigured, AccountingTenantAdminCertificationLaneKindDto.AdminRoleProfile, evidence, "admin-role"),
                BuildTenantAdminCertificationLane(ScopedAccessPoliciesConfigured, AccountingTenantAdminCertificationLaneKindDto.ScopedAccessPolicies, evidence, "scoped-access"),
                BuildTenantAdminCertificationLane(ReportingGroupsConfigured, AccountingTenantAdminCertificationLaneKindDto.ReportingGroups, evidence, "reporting-group"),
                BuildTenantAdminCertificationLane(AccountingAdminSurfaceConfigured, AccountingTenantAdminCertificationLaneKindDto.AccountingAdminSurface, evidence, "accounting-admin-surface"),
                BuildTenantAdminCertificationLane(false, AccountingTenantAdminCertificationLaneKindDto.BrowserAccountingAdminSurface, evidence, "browser-accounting-admin"),
                BuildTenantAdminCertificationLane(WpfAccountingAdminSurfaceConfigured, AccountingTenantAdminCertificationLaneKindDto.WpfAccountingAdminSurface, evidence, "wpf-admin-studio"),
                BuildTenantAdminCertificationLane(ChartAdministrationStudioConfigured, AccountingTenantAdminCertificationLaneKindDto.ChartAdministrationStudio, evidence, "chart-administration"),
                BuildTenantAdminCertificationLane(RuleTestPromotionStudioConfigured, AccountingTenantAdminCertificationLaneKindDto.RuleTestPromotionStudio, evidence, "rule-test-promotion"),
                BuildTenantAdminCertificationLane(CloseSetupStudioConfigured, AccountingTenantAdminCertificationLaneKindDto.CloseSetupStudio, evidence, "close-setup"),
                BuildTenantAdminCertificationLane(ProviderMappingStudioConfigured, AccountingTenantAdminCertificationLaneKindDto.ProviderMappingStudio, evidence, "provider-mapping"),
                BuildTenantAdminCertificationLane(TenantCompanyReportGroupSetupStudioConfigured, AccountingTenantAdminCertificationLaneKindDto.TenantCompanyReportGroupSetupStudio, evidence, "tenant-company-report-group"),
                BuildTenantAdminCertificationLane(AuditReviewToolingConfigured, AccountingTenantAdminCertificationLaneKindDto.AuditReviewTooling, evidence, "audit-review"),
                BuildTenantAdminCertificationLane(BulkImportExportSafeguardsConfigured, AccountingTenantAdminCertificationLaneKindDto.BulkImportExportSafeguards, evidence, "bulk-import-export"),
                BuildTenantAdminCertificationLane(PerformanceValidationConfigured, AccountingTenantAdminCertificationLaneKindDto.PerformanceValidation, evidence, "performance-validation"),
                BuildTenantAdminCertificationLane(DisasterRecoveryRunbookConfigured, AccountingTenantAdminCertificationLaneKindDto.DisasterRecoveryRunbook, evidence, "disaster-recovery"),
                BuildTenantAdminCertificationLane(LedgerBookAdministrationStudioConfigured, AccountingTenantAdminCertificationLaneKindDto.LedgerBookAdministrationStudio, evidence, "ledger-book-administration"),
                BuildTenantAdminCertificationLane(PostingRuleAuthoringStudioConfigured, AccountingTenantAdminCertificationLaneKindDto.PostingRuleAuthoringStudio, evidence, "posting-rule-authoring"),
                BuildTenantAdminCertificationLane(ApprovalQueueStudioConfigured, AccountingTenantAdminCertificationLaneKindDto.ApprovalQueueStudio, evidence, "approval-queue"),
                BuildTenantAdminCertificationLane(DimensionMappingStudioConfigured, AccountingTenantAdminCertificationLaneKindDto.DimensionMappingStudio, evidence, "dimension-mapping"),
                BuildTenantAdminCertificationLane(ImplementationSandboxConfigured, AccountingTenantAdminCertificationLaneKindDto.ImplementationSandbox, evidence, "implementation-sandbox")
            }
            .OfType<AccountingTenantAdminCertificationLaneDto>()
            .ToArray();

        if (lanes.Length == 0)
        {
            return [];
        }

        return
        [
            new AccountingTenantAdminCertificationArtifactDto(
                $"{correlationId}-tenant-admin",
                AccountingCertificationArtifactStatusDto.Certified,
                ResolveTenantAdministrationTenantId(),
                ResolveTenantAdministrationCompanyId(),
                _activeFundProfile.FundProfileId,
                _configuration?.LedgerBookId,
                DefaultLifecycleActor,
                DateTimeOffset.UtcNow,
                "wpf-accounting-configure",
                lanes,
                evidence,
                CorrelationId: correlationId)
        ];
    }

    private static AccountingTenantAdminCertificationLaneDto? BuildTenantAdminCertificationLane(
        bool certified,
        AccountingTenantAdminCertificationLaneKindDto kind,
        IReadOnlyList<string> evidence,
        string marker)
        => certified
            ? new AccountingTenantAdminCertificationLaneDto(
                kind,
                AccountingCertificationArtifactLaneStatusDto.Passed,
                FilterCertificationLaneEvidence(evidence, marker))
            : null;

    private static IReadOnlyList<string> FilterCertificationLaneEvidence(
        IReadOnlyList<string> evidence,
        string marker)
    {
        var scoped = evidence
            .Where(reference => reference.Contains(marker, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return scoped.Length > 0 ? scoped : evidence;
    }

    private void ApplyExternalGlMappingProfileDraft(ExternalGlMappingProfileDto profile)
    {
        ExternalGlMappingProviderId = profile.ProviderId;
        ExternalGlMappingProfileId = profile.ProfileId;
        ExternalGlMappingDisplayName = profile.DisplayName;
        ExternalGlMappingAccountMappingsText = string.Join(
            Environment.NewLine,
            profile.AccountMappings
                .OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Select(static pair => $"{pair.Key}={pair.Value}"));
        var dimensionMapping = profile.DimensionMappings.FirstOrDefault();
        ExternalGlMappingMeridianDimensionsText = FormatExternalGlLedgerDimensionSet(
            dimensionMapping?.MeridianDimensions ?? BuildDefaultMeridianExternalGlDimensions(profile.ProviderId, profile.ProfileId));
        ExternalGlMappingExternalDimensionsText = FormatExternalGlLedgerDimensionSet(
            dimensionMapping?.ExternalDimensions ?? BuildDefaultProviderExternalGlDimensions());
        ExternalGlMappingEvidenceText = $"approval:external-gl-mapping:{profile.ProfileId}";
        ExternalGlMappingProfileCertified = profile.CertificationState == AccountingCertificationStateDto.Certified;
    }

    private void ApplyDefaultExternalGlMappingDimensionDraft()
    {
        var providerId = string.IsNullOrWhiteSpace(ExternalGlMappingProviderId)
            ? "quickbooks-fixture"
            : ExternalGlMappingProviderId.Trim();
        var profileId = string.IsNullOrWhiteSpace(ExternalGlMappingProfileId)
            ? $"{providerId}-ledger-book-mapping"
            : ExternalGlMappingProfileId.Trim();
        ExternalGlMappingMeridianDimensionsText = FormatExternalGlLedgerDimensionSet(BuildDefaultMeridianExternalGlDimensions(providerId, profileId));
        ExternalGlMappingExternalDimensionsText = FormatExternalGlLedgerDimensionSet(BuildDefaultProviderExternalGlDimensions());
    }

    private DimensionMappingProfileDto BuildExternalGlDimensionMappingProfile(
        string providerId,
        string profileId,
        LedgerDimensionSetDto meridianDimensions,
        LedgerDimensionSetDto externalDimensions,
        bool certified)
    {
        return new DimensionMappingProfileDto(
            $"{profileId}-dimension-scope",
            "Canonical and provider dimension scope",
            providerId,
            meridianDimensions,
            externalDimensions,
            certified ? AccountingCertificationStateDto.Certified : AccountingCertificationStateDto.ReadyForReview);
    }

    private LedgerDimensionSetDto BuildDefaultMeridianExternalGlDimensions(string providerId, string profileId)
    {
        var ledgerBookText = _configuration?.LedgerBookId?.ToString("D", CultureInfo.InvariantCulture);
        var fundProfileId = _activeFundProfile?.FundProfileId ?? "fund-scope";
        var entityId = _configuration?.LedgerBookSetupCandidate?.FundStructureNodeId.ToString("D", CultureInfo.InvariantCulture)
            ?? _activeFundProfile?.EntityIds?.FirstOrDefault()
            ?? fundProfileId;
        return new LedgerDimensionSetDto(
            FundId: fundProfileId,
            EntityId: entityId,
            BookId: ledgerBookText,
            CostCenterId: "fund-accounting",
            ExternalGlDimensions: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Provider"] = providerId,
                ["MappingProfile"] = profileId
            });
    }

    private LedgerDimensionSetDto BuildDefaultProviderExternalGlDimensions()
    {
        var ledgerBookText = _configuration?.LedgerBookId?.ToString("D", CultureInfo.InvariantCulture);
        var fundProfileId = _activeFundProfile?.FundProfileId ?? "fund-scope";
        var entityId = _configuration?.LedgerBookSetupCandidate?.FundStructureNodeId.ToString("D", CultureInfo.InvariantCulture)
            ?? _activeFundProfile?.EntityIds?.FirstOrDefault()
            ?? fundProfileId;
        return new LedgerDimensionSetDto(
            FundId: fundProfileId,
            EntityId: entityId,
            BookId: ledgerBookText is null ? null : $"Book:{ledgerBookText}",
            CostCenterId: "FundAccounting",
            ExternalGlDimensions: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Class"] = fundProfileId,
                ["Book"] = ledgerBookText ?? "fund-scope"
            });
    }

    private static IReadOnlyDictionary<string, string> ParseExternalGlAccountMappings(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        return value
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(static line =>
            {
                var index = line.IndexOf('=');
                return index <= 0 || index >= line.Length - 1
                    ? default
                    : new KeyValuePair<string, string>(line[..index].Trim(), line[(index + 1)..].Trim());
            })
            .Where(static pair => !string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
            .GroupBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.First().Value, StringComparer.OrdinalIgnoreCase);
    }

    private static LedgerDimensionSetDto ParseLedgerDimensionSet(string? value, LedgerDimensionSetDto defaults)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaults;
        }

        var externalDimensions = new Dictionary<string, string>(defaults.ExternalGlDimensions ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
        var fundId = defaults.FundId;
        var entityId = defaults.EntityId;
        var sleeveId = defaults.SleeveId;
        var strategyId = defaults.StrategyId;
        var investorId = defaults.InvestorId;
        var capitalAccountId = defaults.CapitalAccountId;
        var instrumentId = defaults.InstrumentId;
        var taxLotId = defaults.TaxLotId;
        var costCenterId = defaults.CostCenterId;
        var counterpartyId = defaults.CounterpartyId;
        var organizationId = defaults.OrganizationId;
        var portfolioId = defaults.PortfolioId;
        var bookId = defaults.BookId;
        var accountId = defaults.AccountId;
        var customerId = defaults.CustomerId;
        var vendorId = defaults.VendorId;
        var projectId = defaults.ProjectId;

        foreach (var pair in ParseExternalGlAccountMappings(value))
        {
            switch (NormalizeDimensionKey(pair.Key))
            {
                case "fundid":
                    fundId = pair.Value;
                    break;
                case "entityid":
                    entityId = pair.Value;
                    break;
                case "sleeveid":
                    sleeveId = pair.Value;
                    break;
                case "strategyid":
                    strategyId = pair.Value;
                    break;
                case "investorid":
                    investorId = pair.Value;
                    break;
                case "capitalaccountid":
                    capitalAccountId = pair.Value;
                    break;
                case "instrumentid":
                    if (Guid.TryParse(pair.Value, out var parsedInstrumentId))
                    {
                        instrumentId = parsedInstrumentId;
                    }
                    break;
                case "taxlotid":
                    taxLotId = pair.Value;
                    break;
                case "costcenterid":
                    costCenterId = pair.Value;
                    break;
                case "counterpartyid":
                    counterpartyId = pair.Value;
                    break;
                case "organizationid":
                    organizationId = pair.Value;
                    break;
                case "portfolioid":
                    portfolioId = pair.Value;
                    break;
                case "bookid":
                    bookId = pair.Value;
                    break;
                case "accountid":
                    accountId = pair.Value;
                    break;
                case "customerid":
                    customerId = pair.Value;
                    break;
                case "vendorid":
                    vendorId = pair.Value;
                    break;
                case "projectid":
                    projectId = pair.Value;
                    break;
                default:
                    externalDimensions[pair.Key] = pair.Value;
                    break;
            }
        }

        return new LedgerDimensionSetDto(
            fundId,
            entityId,
            sleeveId,
            strategyId,
            investorId,
            capitalAccountId,
            instrumentId,
            taxLotId,
            costCenterId,
            counterpartyId,
            externalDimensions,
            organizationId,
            portfolioId,
            bookId,
            accountId,
            customerId,
            vendorId,
            projectId);
    }

    private static string NormalizeDimensionKey(string key)
    {
        return new string(key.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
    }

    private static string FormatExternalGlLedgerDimensionSet(LedgerDimensionSetDto dimensions)
    {
        var rows = new List<string>();
        Add("fundId", dimensions.FundId);
        Add("entityId", dimensions.EntityId);
        Add("sleeveId", dimensions.SleeveId);
        Add("strategyId", dimensions.StrategyId);
        Add("investorId", dimensions.InvestorId);
        Add("capitalAccountId", dimensions.CapitalAccountId);
        Add("instrumentId", dimensions.InstrumentId?.ToString("D", CultureInfo.InvariantCulture));
        Add("taxLotId", dimensions.TaxLotId);
        Add("costCenterId", dimensions.CostCenterId);
        Add("counterpartyId", dimensions.CounterpartyId);
        Add("organizationId", dimensions.OrganizationId);
        Add("portfolioId", dimensions.PortfolioId);
        Add("bookId", dimensions.BookId);
        Add("accountId", dimensions.AccountId);
        Add("customerId", dimensions.CustomerId);
        Add("vendorId", dimensions.VendorId);
        Add("projectId", dimensions.ProjectId);
        foreach (var pair in (dimensions.ExternalGlDimensions ?? new Dictionary<string, string>()).OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            Add(pair.Key, pair.Value);
        }

        return string.Join(Environment.NewLine, rows);

        void Add(string key, string? entryValue)
        {
            if (!string.IsNullOrWhiteSpace(entryValue))
            {
                rows.Add($"{key}={entryValue.Trim()}");
            }
        }
    }

    private static IReadOnlyList<string> WithExternalGlMappingProfileEvidence(
        IReadOnlyList<string> evidence,
        string providerId,
        string fundProfileId,
        string profileId,
        Guid? ledgerBookId)
        => evidence
            .Append($"approval:external-gl-mapping:{profileId}")
            .Append($"evidence://external-gl/mapping-certification/provider/{providerId}/fund/{fundProfileId}/profile/{profileId}")
            .Concat(ledgerBookId is { } bookId
                ? [$"evidence://ledger-book/{bookId:D}/external-gl/mapping-certification/{profileId}"]
                : [])
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Select(static item => item.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static IReadOnlyList<string> WithLedgerBookTenantAdministrationEvidence(
        IReadOnlyList<string> evidence,
        string tenantId,
        string companyId,
        Guid? ledgerBookId,
        bool ledgerBookAdministrationStudioConfigured,
        bool implementationSandboxConfigured)
    {
        if (ledgerBookId is not { } bookId)
        {
            return evidence;
        }

        var bookText = bookId.ToString("D", CultureInfo.InvariantCulture);
        var generated = new List<string>();
        if (ledgerBookAdministrationStudioConfigured
            && !HasTenantAdministrationLedgerBookEvidence(evidence, bookText, "ledger-book-administration"))
        {
            generated.Add($"evidence://tenant-admin/{tenantId}/{companyId}/ledger-book-administration/ledgerBookId={bookText}");
        }

        if (implementationSandboxConfigured)
        {
            generated.AddRange(BuildImplementationSandboxEvidence(tenantId, companyId, bookText));
        }

        return evidence
            .Concat(generated)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool HasTenantAdministrationLedgerBookEvidence(
        IReadOnlyList<string> evidence,
        string ledgerBookId,
        string marker)
        => evidence.Any(reference =>
            reference.Contains(ledgerBookId, StringComparison.OrdinalIgnoreCase) &&
            reference.Contains(marker, StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyList<string> BuildImplementationSandboxEvidence(
        string tenantId,
        string companyId,
        string ledgerBookId)
        =>
        [
            $"evidence://tenant-admin/{tenantId}/{companyId}/implementation-sandbox/ledgerBookId={ledgerBookId}",
            $"evidence://tenant-admin/{tenantId}/{companyId}/sandbox-validation/ledgerBookId={ledgerBookId}",
            $"evidence://tenant-admin/{tenantId}/{companyId}/fixture-validation/ledgerBookId={ledgerBookId}",
            $"evidence://tenant-admin/{tenantId}/{companyId}/implementation-fixture/ledgerBookId={ledgerBookId}"
        ];

}

public sealed record AccountingWorkbenchRow(
    string Name,
    string Status,
    string Detail,
    string Evidence,
    string Key = "");

internal sealed record ManualJournalEntryTypePreset(
    ManualJournalEntryTypeDto EntryType,
    string Label,
    string Description,
    string Memo,
    string DebitAccountPath,
    string CreditAccountPath,
    string EvidenceLink);

public sealed record ExternalGlEvidenceRow(
    string AccountCode,
    string AccountName,
    string Status,
    decimal ExternalDebit,
    decimal ExternalCredit,
    decimal MeridianDebit,
    decimal MeridianCredit,
    decimal Variance,
    string Detail,
    string Evidence);

internal static class AccountingConfigureCollectionExtensions
{
    public static void ReplaceWith<T>(this ObservableCollection<T> collection, IEnumerable<T> rows)
    {
        collection.Clear();
        foreach (var row in rows)
        {
            collection.Add(row);
        }
    }
}
