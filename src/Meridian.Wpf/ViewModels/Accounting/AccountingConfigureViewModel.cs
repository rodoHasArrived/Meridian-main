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
using Meridian.Wpf.Models;
using Meridian.Wpf.Services;

namespace Meridian.Wpf.ViewModels.Accounting;

public sealed class AccountingConfigureViewModel : Meridian.Wpf.ViewModels.BindableBase
{
    private const string DefaultActor = "desktop-user";

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

    private AccountingConfigurationWorkspaceDto? _configuration;
    private ManualJournalEntryDraftDto? _selectedDraft;
    private FundProfileDetail? _activeFundProfile;
    private bool _isLoading;
    private string _statusText = "Select a fund-linked context to load accounting configuration.";
    private string _activeFundText = "No fund selected";
    private string _storagePostureText = "Storage has not been inspected.";
    private string _configurationStatusText = "Not loaded";
    private string _configurationDetailText = "Configuration readiness loads from shared accounting DTOs.";
    private string _manualJournalStatusText = "Manual journal workbench has not loaded.";
    private string _capitalAccountWorkbenchStatusText = "Capital Account Workbench has not loaded.";
    private string _externalGlStatusText = "External GL evidence has not loaded.";
    private string _postingPostureText = "Posting/export posture has not loaded.";
    private string _closeReadinessText = "Close readiness has not loaded.";
    private string _closeDetailText = "Close posture is projected from shared operations continuity state.";
    private string _evidencePostureText = "Evidence posture has not loaded.";
    private string _reconciliationPostureText = "Reconciliation posture has not loaded.";
    private string _policyPostureText = "Accounting policy posture has not loaded.";
    private string _postingCandidateStatusText = "Posting-rule journal candidate has not been built.";
    private string _postingCandidateDetailText = "Build a governed draft candidate from the selected posting rule without posting to the ledger.";
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
        IAccountingPostingCandidateService? accountingPostingCandidateService = null)
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

        RefreshCommand = new AsyncRelayCommand(() => RefreshAsync());
        SeedBaselineConfigurationCommand = new AsyncRelayCommand(() => SeedBaselineConfigurationAsync());
        ActivateConfigurationCommand = new AsyncRelayCommand(() => ActivateConfigurationAsync());
        SaveManualJournalDraftCommand = new AsyncRelayCommand(() => SaveManualJournalDraftAsync());
        ValidateManualJournalDraftCommand = new AsyncRelayCommand(() => ValidateManualJournalDraftAsync());
        SubmitManualJournalDraftCommand = new AsyncRelayCommand(() => SubmitManualJournalDraftAsync());
        RefreshExternalGlCommand = new AsyncRelayCommand(() => RefreshExternalGlAsync());
        CreateFundScopedPolicyCommand = new AsyncRelayCommand(() => CreateFundScopedPolicyAsync());
        BuildPostingCandidateCommand = new AsyncRelayCommand(() => BuildPostingCandidateAsync());

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
    public IAsyncRelayCommand CreateFundScopedPolicyCommand { get; }
    public IAsyncRelayCommand BuildPostingCandidateCommand { get; }

    public ObservableCollection<AccountingWorkbenchRow> ValidationRows { get; } = [];
    public ObservableCollection<AccountingWorkbenchRow> ChartRows { get; } = [];
    public ObservableCollection<AccountingWorkbenchRow> TemplateRows { get; } = [];
    public ObservableCollection<AccountingWorkbenchRow> PostingRuleRows { get; } = [];
    public ObservableCollection<AccountingWorkbenchRow> AuditRows { get; } = [];
    public ObservableCollection<AccountingWorkbenchRow> ProviderRows { get; } = [];
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
            ApplyConfiguration(_configuration);
            await LoadManualJournalWorkbenchAsync(ct).ConfigureAwait(false);
            await LoadPolicyRowsAsync(ct).ConfigureAwait(false);
            await LoadOperationsPostureAsync(ct).ConfigureAwait(false);
            await RefreshExternalGlAsync(ct).ConfigureAwait(false);
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
            await _configurationService.UpsertChartNodeAsync(
                new UpsertChartOfAccountsNodeRequest(
                    fundProfileId,
                    new ChartOfAccountsNodeDto("assets-cash", "Assets:Cash", "Cash", "Asset"),
                    DefaultActor,
                    CorrelationId: "wpf-accounting-config-seed",
                    EvidenceLinks: evidence),
                ct).ConfigureAwait(false);
            await _configurationService.UpsertChartNodeAsync(
                new UpsertChartOfAccountsNodeRequest(
                    fundProfileId,
                    new ChartOfAccountsNodeDto("income-investment", "Income:Investment Income", "Investment Income", "Revenue"),
                    DefaultActor,
                    CorrelationId: "wpf-accounting-config-seed",
                    EvidenceLinks: evidence),
                ct).ConfigureAwait(false);
            await _configurationService.UpsertChartNodeAsync(
                new UpsertChartOfAccountsNodeRequest(
                    fundProfileId,
                    new ChartOfAccountsNodeDto("expenses-investment-fees", "Expenses:Investment Fees", "Investment Fees", "Expense"),
                    DefaultActor,
                    CorrelationId: "wpf-accounting-config-seed",
                    EvidenceLinks: evidence),
                ct).ConfigureAwait(false);
            foreach (var node in BuildManualJournalEntryTypeChartNodes())
            {
                await _configurationService.UpsertChartNodeAsync(
                    new UpsertChartOfAccountsNodeRequest(
                        fundProfileId,
                        node,
                        DefaultActor,
                        CorrelationId: "wpf-accounting-config-seed",
                        EvidenceLinks: evidence),
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
                    EvidenceLinks: evidence),
                ct).ConfigureAwait(false);
            foreach (var template in BuildManualJournalEntryTypeTemplates(_activeFundProfile.BaseCurrency))
            {
                await _configurationService.UpsertTemplateAsync(
                    new UpsertJournalEntryTemplateRequest(
                        fundProfileId,
                        template,
                        DefaultActor,
                        CorrelationId: "wpf-accounting-config-seed",
                        EvidenceLinks: evidence),
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
                    EvidenceLinks: evidence),
                ct).ConfigureAwait(false);
            foreach (var rule in BuildManualJournalEntryTypePostingRules())
            {
                await _configurationService.UpsertPostingRuleAsync(
                    new UpsertPostingRuleRequest(
                        fundProfileId,
                        rule,
                        DefaultActor,
                        CorrelationId: "wpf-accounting-config-seed",
                        EvidenceLinks: evidence),
                    ct).ConfigureAwait(false);
            }

            _configuration = await _configurationService.GetWorkspaceAsync(fundProfileId, ct: ct).ConfigureAwait(false);
            ApplyConfiguration(_configuration);
            await LoadManualJournalWorkbenchAsync(ct).ConfigureAwait(false);
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
            StatusText = "Accounting configuration is active.";
        }
        catch (Exception ex)
        {
            StatusText = $"Accounting configuration activation is blocked: {ex.Message}";
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
                    EvidenceLinks: NormalizeEvidenceLink(DraftEvidenceLink)),
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
                new ValidateManualJournalEntryDraftRequest(BuildManualJournalDraft(), DefaultActor, CorrelationId: "wpf-manual-je-validate"),
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
                    EvidenceLinks: NormalizeEvidenceLink(DraftEvidenceLink)),
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

    public async Task RefreshExternalGlAsync(CancellationToken ct = default)
    {
        ProviderRows.Clear();
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
                ProviderRows.Add(new AccountingWorkbenchRow(
                    provider.DisplayName,
                    provider.State.ToString(),
                    provider.StatusDetail,
                    provider.ProviderId));
            }

            if (_activeFundProfile is null)
            {
                ExternalGlStatusText = "Select a fund-linked context before importing external GL evidence.";
                PostingPostureText = "Posting/export remains disabled until Meridian-owned ledger entries are approved.";
                return;
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
        ManualJournalStatusText = "Locked until a fund context is selected.";
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
        BatchReconciliationActionText = "No reconciliation rows are loaded.";
        ClearRows();
        StatusText = "Select a fund-linked context to unlock Accounting Configure.";
        RaisePropertyChanged(nameof(CanSubmitManualJournal));
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
        ApplyManualJournalValidationRows(workspace.ValidationIssues);

        var critical = workspace.ValidationIssues.Count(issue => issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);
        ConfigurationStatusText = $"{workspace.Status} {workspace.ConfigurationVersion}";
        ConfigurationDetailText = critical > 0
            ? $"{critical} critical configuration issue(s) block activation."
            : "Chart, templates, posting rules, validation, and audit are shared-contract backed.";
    }

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
        ManualJournalStatusText = BuildManualJournalStatusText(workbench);
        RaisePropertyChanged(nameof(CanSubmitManualJournal));
        RaiseManualJournalDraftComputedProperties();
    }

    private void RaiseManualJournalDraftComputedProperties()
    {
        RaisePropertyChanged(nameof(ManualJournalDraftReferenceText));
        RaisePropertyChanged(nameof(ManualJournalDraftDateText));
        RaisePropertyChanged(nameof(ManualJournalDraftBalanceText));
        RaisePropertyChanged(nameof(ManualJournalDraftBalanceBadgeText));
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
        return new ManualJournalEntryDraftDto(
            journalEntryId,
            _selectedDraft?.Status ?? ManualJournalEntryStatusDto.Draft,
            fundProfileId,
            ledgerBookId,
            SelectedAccountingBasis,
            accountingDate,
            PeriodId: null,
            EntityId: null,
            FundNodeId: null,
            currency,
            DraftMemo,
            DefaultActor,
            _selectedDraft?.CreatedAtUtc ?? now,
            now,
            _selectedDraft?.Version ?? 0,
            [
                new ManualJournalEntryLineDto("debit-line", AccountingTemplateLineSideDto.Debit, DraftAmount, currency, DraftDebitAccountPath, Description: "Desktop debit line", EvidenceLink: NormalizeEvidenceLink(DraftEvidenceLink).FirstOrDefault()),
                new ManualJournalEntryLineDto("credit-line", AccountingTemplateLineSideDto.Credit, DraftAmount, currency, DraftCreditAccountPath, Description: "Desktop credit line", EvidenceLink: NormalizeEvidenceLink(DraftEvidenceLink).FirstOrDefault())
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
            TreasuryContext: BuildManualJournalTreasuryContext(fundProfileId, SelectedEntryType, accountingDate, journalEntryId));
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
