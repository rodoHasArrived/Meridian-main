using System;
using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using Meridian.Contracts.AccountingSystem;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.AccountingSystem;
using Meridian.FinancialOperations.Ledger;
using Meridian.Ui.Shared.Services;
using Meridian.Ui.Services.Services.Accounting;
using Meridian.Wpf.Models;
using Meridian.Wpf.Services;
using Meridian.Contracts.Integrity;
using UtcFormat = Meridian.Ui.Services.FormatHelpers;

namespace Meridian.Wpf.ViewModels.Accounting;

/// <summary>Exposes bindable state and commands to review books, chart, rules, manual journals, GL evidence, validation, audit readiness.</summary>
public sealed partial class AccountingConfigureViewModel : Meridian.Wpf.ViewModels.BindableBase
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

    private readonly AccountingProductionCertificationCommandService? _productionCertificationCommandService;

    private readonly IAccountingTenantAdministrationProfileStore? _tenantAdministrationProfileStore;

    private readonly IAccountingMigrationRunWorkerPlanStore? _migrationRunWorkerPlanStore;

    private AccountingConfigurationWorkspaceDto? _configuration;

    private ManualJournalEntryDraftDto? _selectedDraft;

    private string? _selectedRecurringJournalEntityId;

    private string? _recurringJournalFundProfileId;

    private Guid? _recurringJournalLedgerBookId;

    private Guid _newManualJournalDraftId = Guid.NewGuid();

    private DateTimeOffset _newManualJournalDraftTimestamp = DateTimeOffset.UtcNow;

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

    private readonly DesktopAuthenticationSession? _authenticationSession;

    /// <summary>
    /// Audit finding P8: governance records carry the authenticated desktop operator when a
    /// session exists; the historical constant remains only as an anonymous-session fallback.
    /// </summary>
    private string ResolveActor() =>
        _authenticationSession?.CurrentActor is { Length: > 0 } actor ? actor : DefaultActor;

    public AccountingConfigureViewModel(
        FundContextService fundContextService,
        IWorkstationAccountingApiClient configurationService,
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
        AccountingProductionCertificationCommandService? productionCertificationCommandService = null,
        IAccountingTenantAdministrationProfileStore? tenantAdministrationProfileStore = null,
        IAccountingMigrationRunWorkerPlanStore? migrationRunWorkerPlanStore = null,
        DesktopAuthenticationSession? authenticationSession = null,
        IRecurringJournalQueueSource? recurringJournalQueueSource = null,
        IAccountingAuditIntegrityApiClient? auditIntegrityClient = null,
        IFactorPaydownApiClient? factorPaydownClient = null,
        IWorkstationEntityConsolidationApiClient? entityConsolidationClient = null,
        IWorkstationManualJournalApiClient? manualJournalApiClient = null)
    {
        _fundContextService = fundContextService ?? throw new ArgumentNullException(nameof(fundContextService));
        _authenticationSession = authenticationSession;
        RecurringJournalQueue = new RecurringJournalQueueViewModel(recurringJournalQueueSource);
        AuditChainIntegrity = new AuditChainIntegrityViewModel(auditIntegrityClient);
        FactorPaydown = new FactorPaydownViewModel(factorPaydownClient);
        EntityConsolidation = new EntityConsolidationViewModel(entityConsolidationClient);
        _configurationService = configurationService ?? throw new ArgumentNullException(nameof(configurationService));
        _manualJournalEntryWorkbenchService = manualJournalApiClient ?? manualJournalEntryWorkbenchService ?? throw new ArgumentNullException(nameof(manualJournalEntryWorkbenchService));
        _capitalAccountWorkbenchService = capitalAccountWorkbenchService;
        _configurationStore = configurationStore;
        _manualJournalEntryDraftStore = manualJournalEntryDraftStore;
        _accountingSystemIntegrationService = accountingSystemIntegrationService;
        _fundOperationsWorkspaceReadService = fundOperationsWorkspaceReadService;
        _accountingPolicyService = accountingPolicyService;
        _accountingPostingCandidateService = accountingPostingCandidateService;
        _manualJournalEntryLifecycleService = manualJournalApiClient ?? manualJournalEntryLifecycleService
            ?? manualJournalEntryWorkbenchService as IManualJournalEntryLifecycleService;
        _ledgerBookService = ledgerBookService;
        _accountingProductionReadinessService = accountingProductionReadinessService;
        _productionCertificationProfileStore = productionCertificationProfileStore;
        _productionCertificationCommandService = productionCertificationCommandService;
        _tenantAdministrationProfileStore = tenantAdministrationProfileStore;
        _migrationRunWorkerPlanStore = migrationRunWorkerPlanStore;

        RefreshCommand = new AsyncRelayCommand(() => RefreshAsync());
        SeedBaselineConfigurationCommand = new AsyncRelayCommand(() => SeedBaselineConfigurationAsync());
        ActivateConfigurationCommand = new AsyncRelayCommand(() => ActivateConfigurationAsync());
        SaveManualJournalDraftCommand = new AsyncRelayCommand(() => SaveManualJournalDraftAsync(), () => CanEditManualJournalDraft);
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
        ApproveManualJournalCommand = new AsyncRelayCommand(() => ApplyManualJournalLifecycleActionAsync(JournalEntryLifecycleActionDto.Approve), () => CanApplySelectedJournalAction(JournalEntryLifecycleActionDto.Approve));
        PostManualJournalCommand = new AsyncRelayCommand(() => ApplyManualJournalLifecycleActionAsync(JournalEntryLifecycleActionDto.Post), () => CanApplySelectedJournalAction(JournalEntryLifecycleActionDto.Post));
        ReverseManualJournalCommand = new AsyncRelayCommand(() => ApplyManualJournalLifecycleActionAsync(JournalEntryLifecycleActionDto.Reverse), () => CanApplySelectedJournalAction(JournalEntryLifecycleActionDto.Reverse));
        RebookManualJournalCommand = new AsyncRelayCommand(() => ApplyManualJournalLifecycleActionAsync(JournalEntryLifecycleActionDto.Rebook), () => CanApplySelectedJournalAction(JournalEntryLifecycleActionDto.Rebook));
        LockManualJournalAfterCloseCommand = new AsyncRelayCommand(() => ApplyManualJournalLifecycleActionAsync(JournalEntryLifecycleActionDto.LockAfterClose), () => CanApplySelectedJournalAction(JournalEntryLifecycleActionDto.LockAfterClose));

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

    public RecurringJournalQueueViewModel RecurringJournalQueue { get; }

    /// <summary>On-demand audit-chain verification, matching the browser Configuration panel.</summary>

    public ObservableCollection<string> RecurringJournalEntityOptions { get; } = [];

    public string? SelectedRecurringJournalEntityId
    {
        get => _selectedRecurringJournalEntityId;
        set
        {
            if (SetProperty(ref _selectedRecurringJournalEntityId, value))
                _ = RecurringJournalQueue.LoadAsync(_recurringJournalFundProfileId, _recurringJournalLedgerBookId, value);
        }
    }

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
        && _productionCertificationCommandService is not null
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
        _recurringJournalFundProfileId = null;
        _recurringJournalLedgerBookId = null;
        await ResetGovernedQueuesAsync(ct).ConfigureAwait(false);
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

    private void ApplyLockedState()
    {
        _configuration = null;
        _selectedDraft = null;
        _newManualJournalDraftId = Guid.NewGuid();
        _newManualJournalDraftTimestamp = DateTimeOffset.UtcNow;
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
        RememberManualJournalDrafts([]);
        _ = ResetGovernedQueuesAsync();
        _recurringJournalFundProfileId = null;
        _recurringJournalLedgerBookId = null;
        RecurringJournalEntityOptions.Clear();
        SetProperty(ref _selectedRecurringJournalEntityId, null, nameof(SelectedRecurringJournalEntityId));
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
}

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
