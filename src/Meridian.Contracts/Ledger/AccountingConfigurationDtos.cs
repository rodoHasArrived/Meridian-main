using System.Text.Json.Serialization;
using Meridian.Contracts.AccountingSystem;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Workstation;

namespace Meridian.Contracts.Ledger;

[JsonConverter(typeof(JsonStringEnumConverter<AccountingConfigurationStatusDto>))]
public enum AccountingConfigurationStatusDto
{
    Draft = 0,
    Active = 1,
    Archived = 2
}

[JsonConverter(typeof(JsonStringEnumConverter<AccountingConfigurationValidationSeverityDto>))]
public enum AccountingConfigurationValidationSeverityDto
{
    Info = 0,
    Warning = 1,
    Critical = 2
}

[JsonConverter(typeof(JsonStringEnumConverter<AccountingTemplateLineSideDto>))]
public enum AccountingTemplateLineSideDto
{
    Debit = 0,
    Credit = 1
}

[JsonConverter(typeof(JsonStringEnumConverter<ManualJournalEntryStatusDto>))]
public enum ManualJournalEntryStatusDto
{
    Draft = 0,
    NeedsFix = 1,
    Submitted = 2,
    Approved = 3,
    Rejected = 4,
    Posted = 5,
    Reversed = 6,
    Rebooked = 7,
    CloseLocked = 8
}

[JsonConverter(typeof(JsonStringEnumConverter<AccountingRuleConditionOperatorDto>))]
public enum AccountingRuleConditionOperatorDto
{
    Equals = 0,
    NotEquals = 1,
    Contains = 2,
    AmountGreaterThanOrEqual = 3,
    AmountLessThanOrEqual = 4,
    AmountBetween = 5,
    IsPresent = 6
}

[JsonConverter(typeof(JsonStringEnumConverter<AccountingRuleConditionGroupOperatorDto>))]
public enum AccountingRuleConditionGroupOperatorDto
{
    All = 0,
    Any = 1
}

[JsonConverter(typeof(JsonStringEnumConverter<AccountingRuleFormulaKindDto>))]
public enum AccountingRuleFormulaKindDto
{
    FixedAmount = 0,
    SourceAmount = 1,
    PercentageOfSourceAmount = 2,
    AllocationResidual = 3
}

[JsonConverter(typeof(JsonStringEnumConverter<AllocationRuleBasisDto>))]
public enum AllocationRuleBasisDto
{
    FixedPercent = 0,
    InvestorCommitment = 1,
    CapitalAccountBalance = 2,
    StrategyWeight = 3,
    CustomFormula = 4
}

[JsonConverter(typeof(JsonStringEnumConverter<JournalEntryLifecycleActionDto>))]
public enum JournalEntryLifecycleActionDto
{
    Validate = 0,
    Submit = 1,
    Approve = 2,
    Reject = 3,
    Post = 4,
    Reverse = 5,
    Rebook = 6,
    LockAfterClose = 7
}

[JsonConverter(typeof(JsonStringEnumConverter<AccountingCertificationStateDto>))]
public enum AccountingCertificationStateDto
{
    Draft = 0,
    ReadyForReview = 1,
    Certified = 2,
    Rejected = 3,
    Superseded = 4
}

[JsonConverter(typeof(JsonStringEnumConverter<AccountingReadinessStateDto>))]
public enum AccountingReadinessStateDto
{
    NotStarted = 0,
    NeedsAttention = 1,
    Blocked = 2,
    ReadyForReview = 3,
    Certified = 4
}

[JsonConverter(typeof(JsonStringEnumConverter<CloseTaskStatusDto>))]
public enum CloseTaskStatusDto
{
    NotStarted = 0,
    WaitingOnDependency = 1,
    InProgress = 2,
    ReadyForSignOff = 3,
    SignedOff = 4,
    Blocked = 5
}

[JsonConverter(typeof(JsonStringEnumConverter<ManualJournalEntryTypeDto>))]
public enum ManualJournalEntryTypeDto
{
    General = 0,
    AccruedBalance = 1,
    AccruedExpense = 2,
    PrepaidExpense = 3,
    Expense = 4,
    Amortization = 5,
    Deferral = 6,
    Reclassification = 7,
    Reversal = 8,
    CapitalCall = 9,
    Distribution = 10,
    Subscription = 11,
    Redemption = 12,
    LpTransfer = 13,
    ManagementFee = 14,

    /// <summary>Period-close closing entries rolling temporary balances into retained earnings.</summary>
    ClosingEntry = 15
}

[JsonConverter(typeof(JsonStringEnumConverter<PrivateCapitalFundEventLedgerReadinessDto>))]
public enum PrivateCapitalFundEventLedgerReadinessDto
{
    Blocked = 0,
    EvidenceMissing = 1,
    ApprovalPending = 2,
    PostingReview = 3,
    ReportReview = 4,
    Ready = 5,
    Published = 6
}

[JsonConverter(typeof(JsonStringEnumConverter<PaymentIntentCashDirectionDto>))]
public enum PaymentIntentCashDirectionDto
{
    Neutral = 0,
    Inflow = 1,
    Outflow = 2
}

[JsonConverter(typeof(JsonStringEnumConverter<PaymentIntentWorkflowStatusDto>))]
public enum PaymentIntentWorkflowStatusDto
{
    EvidenceMissing = 0,
    ApprovalPending = 1,
    BankEvidencePending = 2,
    BankReturned = 3,
    ReconciliationPending = 4,
    ExecutionDeferred = 5,
    Blocked = 6
}

[JsonConverter(typeof(JsonStringEnumConverter<PrivateCapitalPaymentIntentEvidenceStatusDto>))]
public enum PrivateCapitalPaymentIntentEvidenceStatusDto
{
    MissingIntent = 0,
    CashEvidenceMissing = 1,
    IntentCaptured = 2,
    SettlementMatched = 3
}

[JsonConverter(typeof(JsonStringEnumConverter<OperationalFinanceScopeKindDto>))]
public enum OperationalFinanceScopeKindDto
{
    Organization = 0,
    Entity = 1,
    Portfolio = 2,
    Account = 3,
    Book = 4,
    Period = 5,
    Fund = 6,
    Investor = 7,
    CapitalAccount = 8,
    TreasuryBook = 9,
    ExternalGl = 10
}

[JsonConverter(typeof(JsonStringEnumConverter<OperationalFinanceTraceStageDto>))]
public enum OperationalFinanceTraceStageDto
{
    OperationalEvent = 0,
    Evidence = 1,
    Validation = 2,
    Reconciliation = 3,
    PostingCandidate = 4,
    JournalLifecycle = 5,
    LedgerImpact = 6,
    ReportLine = 7,
    Package = 8,
    AuditEvent = 9
}

public sealed record ChartOfAccountsNodeDto(
    string NodeId,
    string Path,
    string AccountName,
    string AccountType,
    string? ParentPath = null,
    string? Symbol = null,
    string? FinancialAccountId = null,
    bool IsArchived = false);

public sealed record JournalEntryTemplateLineDto(
    string LineId,
    string AccountPath,
    AccountingTemplateLineSideDto Side,
    decimal Amount,
    string Currency = "USD",
    string? Description = null);

public sealed record JournalEntryTemplateDto(
    string TemplateId,
    string DisplayName,
    string Description,
    IReadOnlyList<JournalEntryTemplateLineDto> Lines,
    bool IsArchived = false,
    string Version = "v1");

public sealed record LedgerDimensionSetDto(
    string? FundId = null,
    string? EntityId = null,
    string? SleeveId = null,
    string? StrategyId = null,
    string? InvestorId = null,
    string? CapitalAccountId = null,
    Guid? InstrumentId = null,
    string? TaxLotId = null,
    string? CostCenterId = null,
    string? CounterpartyId = null,
    IReadOnlyDictionary<string, string>? ExternalGlDimensions = null,
    string? OrganizationId = null,
    string? PortfolioId = null,
    string? BookId = null,
    string? AccountId = null,
    string? CustomerId = null,
    string? VendorId = null,
    string? ProjectId = null)
{
    public IReadOnlyDictionary<string, string> ExternalGlDimensions { get; init; } =
        ExternalGlDimensions ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public Guid? PositionId { get; init; }
}

public sealed record OperationalFinanceScopeDto(
    string ScopeId,
    OperationalFinanceScopeKindDto ScopeKind,
    string DisplayName,
    LedgerDimensionSetDto? Dimensions = null,
    string? OrganizationId = null,
    string? EntityId = null,
    string? PortfolioId = null,
    string? BookId = null,
    string? AccountId = null);

public sealed record OperationalEventCommandContextDto(
    string OperationalEventId,
    string OperationalEventType,
    DateOnly EffectiveDate,
    string Actor,
    OperationalFinanceScopeDto Scope,
    string? SourceSystem = null,
    Guid? CorrelationId = null,
    Guid? CausationId = null,
    IReadOnlyList<string>? EvidenceLinks = null)
{
    public IReadOnlyList<string> EvidenceLinks { get; init; } =
        EvidenceLinks ?? [];
}

public sealed record OperationalFinanceTraceNodeDto(
    string NodeId,
    OperationalFinanceTraceStageDto Stage,
    string DisplayName,
    string Status,
    string? RecordId = null,
    string? Route = null,
    LedgerDimensionSetDto? Dimensions = null,
    IReadOnlyList<string>? EvidenceLinks = null)
{
    public IReadOnlyList<string> EvidenceLinks { get; init; } =
        EvidenceLinks ?? [];
}

public sealed record OperationalFinanceRecordTraceDto(
    string TraceId,
    OperationalEventCommandContextDto OperationalEvent,
    IReadOnlyList<OperationalFinanceTraceNodeDto> Nodes,
    ManualJournalEntryStatusDto? JournalStatus = null,
    bool RequiresFundScope = false,
    IReadOnlyList<string>? BlockedOutputs = null,
    IReadOnlyList<string>? EvidenceLinks = null)
{
    public IReadOnlyList<OperationalFinanceTraceNodeDto> Nodes { get; init; } =
        Nodes ?? [];

    public IReadOnlyList<string> BlockedOutputs { get; init; } =
        BlockedOutputs ?? [];

    public IReadOnlyList<string> EvidenceLinks { get; init; } =
        EvidenceLinks ?? [];
}

public sealed record AccountingRuleConditionDto(
    string ConditionId,
    string Field,
    AccountingRuleConditionOperatorDto Operator,
    string? Value = null,
    string? SecondValue = null,
    bool IsRequired = true,
    string? Description = null);

public sealed record AccountingRuleConditionGroupDto(
    string GroupId,
    AccountingRuleConditionGroupOperatorDto Operator,
    IReadOnlyList<AccountingRuleConditionDto>? Conditions = null,
    bool IsRequired = true,
    string? Description = null)
{
    public IReadOnlyList<AccountingRuleConditionDto> Conditions { get; init; } =
        Conditions ?? [];
}

public sealed record AccountingRuleFormulaDto(
    string FormulaId,
    AccountingRuleFormulaKindDto Kind,
    decimal Value,
    string Currency = "USD",
    string? Description = null);

public sealed record AllocationRuleDto(
    string AllocationRuleId,
    AllocationRuleBasisDto Basis,
    decimal Weight,
    LedgerDimensionSetDto? TargetDimensions = null,
    string? FormulaId = null,
    string? Description = null);

public sealed record GeneratedPostingLineDto(
    string LineId,
    string AccountPath,
    AccountingTemplateLineSideDto Side,
    string AmountFormulaId,
    decimal Amount,
    string Currency = "USD",
    LedgerDimensionSetDto? Dimensions = null,
    string? Description = null);

public sealed record RulePromotionApprovalDto(
    string ApprovalId,
    string RequestedBy,
    DateTimeOffset RequestedAtUtc,
    ManualJournalEntryStatusDto ApprovalState,
    string? ApprovedBy = null,
    DateTimeOffset? ApprovedAtUtc = null,
    string? Notes = null,
    IReadOnlyList<string>? EvidenceLinks = null)
{
    public IReadOnlyList<string> EvidenceLinks { get; init; } =
        EvidenceLinks ?? [];
}

public sealed record AccountingRuleVersionDto(
    string Version,
    DateTimeOffset CreatedAtUtc,
    string CreatedBy,
    string ChangeSummary,
    RulePromotionApprovalDto? PromotionApproval = null,
    IReadOnlyList<string>? EvidenceLinks = null)
{
    public IReadOnlyList<string> EvidenceLinks { get; init; } =
        EvidenceLinks ?? [];
}

public sealed record AccountingRuleDefinitionDto(
    string RuleId,
    string DisplayName,
    string SourceEventType,
    string RuleVersion,
    DateOnly? EffectiveFrom = null,
    DateOnly? EffectiveTo = null,
    int Priority = 0,
    LedgerDimensionSetDto? Scope = null,
    IReadOnlyList<AccountingRuleConditionDto>? Conditions = null,
    IReadOnlyList<AccountingRuleConditionGroupDto>? ConditionGroups = null,
    IReadOnlyList<AccountingRuleFormulaDto>? Formulas = null,
    IReadOnlyList<AllocationRuleDto>? Allocations = null,
    IReadOnlyList<GeneratedPostingLineDto>? GeneratedPostings = null,
    IReadOnlyList<AccountingRuleVersionDto>? Versions = null,
    RulePromotionApprovalDto? PromotionApproval = null,
    bool RequiresPromotionApproval = false,
    string? Description = null)
{
    public IReadOnlyList<AccountingRuleConditionDto> Conditions { get; init; } =
        Conditions ?? [];

    public IReadOnlyList<AccountingRuleConditionGroupDto> ConditionGroups { get; init; } =
        ConditionGroups ?? [];

    public IReadOnlyList<AccountingRuleFormulaDto> Formulas { get; init; } =
        Formulas ?? [];

    public IReadOnlyList<AllocationRuleDto> Allocations { get; init; } =
        Allocations ?? [];

    public IReadOnlyList<GeneratedPostingLineDto> GeneratedPostings { get; init; } =
        GeneratedPostings ?? [];

    public IReadOnlyList<AccountingRuleVersionDto> Versions { get; init; } =
        Versions ?? [];
}

public sealed record PostingRuleDto(
    string RuleId,
    string DisplayName,
    string SourceEventType,
    string TemplateId,
    string RuleVersion = "v1",
    bool IsArchived = false,
    string? Description = null,
    DateOnly? EffectiveFrom = null,
    DateOnly? EffectiveTo = null,
    int Priority = 0,
    LedgerDimensionSetDto? Scope = null,
    IReadOnlyList<AccountingRuleConditionDto>? Conditions = null,
    IReadOnlyList<AccountingRuleConditionGroupDto>? ConditionGroups = null,
    IReadOnlyList<AccountingRuleFormulaDto>? Formulas = null,
    IReadOnlyList<AllocationRuleDto>? Allocations = null,
    IReadOnlyList<GeneratedPostingLineDto>? GeneratedPostings = null,
    IReadOnlyList<AccountingRuleVersionDto>? Versions = null,
    RulePromotionApprovalDto? PromotionApproval = null,
    bool RequiresPromotionApproval = false)
{
    public IReadOnlyList<AccountingRuleConditionDto> Conditions { get; init; } =
        Conditions ?? [];

    public IReadOnlyList<AccountingRuleConditionGroupDto> ConditionGroups { get; init; } =
        ConditionGroups ?? [];

    public IReadOnlyList<AccountingRuleFormulaDto> Formulas { get; init; } =
        Formulas ?? [];

    public IReadOnlyList<AllocationRuleDto> Allocations { get; init; } =
        Allocations ?? [];

    public IReadOnlyList<GeneratedPostingLineDto> GeneratedPostings { get; init; } =
        GeneratedPostings ?? [];

    public IReadOnlyList<AccountingRuleVersionDto> Versions { get; init; } =
        Versions ?? [];
}

public sealed record AccountingConfigurationValidationIssueDto(
    string Code,
    AccountingConfigurationValidationSeverityDto Severity,
    string Message,
    string? TargetId = null,
    string? SuggestedAction = null);

public sealed record AccountingActionAuditEventDto(
    Guid AuditEventId,
    DateTimeOffset RecordedAtUtc,
    string Actor,
    string Action,
    string? FundProfileId,
    Guid? LedgerBookId,
    string? CorrelationId,
    string BeforeHash,
    string AfterHash,
    IReadOnlyList<AccountingConfigurationValidationIssueDto> ValidationIssues,
    IReadOnlyList<string> EvidenceLinks,
    string? CompanyId = null,
    IReadOnlyList<string>? ReportGroupPrincipalIds = null,
    string? TenantId = null);

public sealed record AccountingRulesStudioSummaryDto(
    int TotalRules,
    int ActiveRules,
    int ArchivedRules,
    int EffectiveDatedRules,
    int GeneratedPostingRules,
    int TemplateMappingRules,
    int RulesWithConditions,
    int RulesWithFormulas,
    int RulesWithAllocations,
    int RulesRequiringPromotionApproval,
    int ApprovedPromotionRules,
    int PendingPromotionApprovalRules,
    int SavedTestCaseCount,
    int RulesWithSavedRegressionTests,
    int RulesMissingCurrentVersionRegressionTests,
    int CriticalIssueCount,
    int WarningIssueCount,
    int RulesReadyForActivation = 0,
    int RulesBlockedByPromotionApproval = 0,
    int RulesBlockedByRegressionTests = 0,
    int RulesBlockedByCriticalIssues = 0,
    IReadOnlyList<string>? RequiredActions = null)
{
    public IReadOnlyList<string> RequiredActions { get; init; } =
        RequiredActions ?? [];
}

public sealed record AccountingRulesStudioRuleRowDto(
    string RuleId,
    string DisplayName,
    string SourceEventType,
    string RuleVersion,
    int Priority,
    DateOnly? EffectiveFrom,
    DateOnly? EffectiveTo,
    string TemplateId,
    bool IsArchived,
    bool UsesGeneratedPostings,
    int ConditionCount,
    int ConditionGroupCount,
    int FormulaCount,
    int AllocationCount,
    int GeneratedPostingLineCount,
    int VersionCount,
    int SavedTestCaseCount,
    int SavedTestEvidenceLinkCount,
    bool RequiresPromotionApproval,
    bool IsPromotionApproved,
    ManualJournalEntryStatusDto? PromotionApprovalState,
    string? PromotionApprovalId,
    int CriticalIssueCount,
    int WarningIssueCount,
    bool CanDryRun,
    bool CanRequestPromotion,
    bool CanActivate);

public sealed record AccountingRulesStudioPromotionQueueItemDto(
    string RuleId,
    string DisplayName,
    string RuleVersion,
    string RequestedBy,
    DateTimeOffset? RequestedAtUtc,
    ManualJournalEntryStatusDto? ApprovalState,
    string? ApprovalId,
    int RegressionTestCaseCount,
    int MissingRegressionEvidenceCount,
    int CriticalIssueCount,
    string SuggestedAction);

public sealed record AccountingRulesStudioDto(
    AccountingRulesStudioSummaryDto Summary,
    IReadOnlyList<AccountingRulesStudioRuleRowDto>? Rules = null,
    IReadOnlyList<AccountingRulesStudioPromotionQueueItemDto>? PromotionQueue = null)
{
    public IReadOnlyList<AccountingRulesStudioRuleRowDto> Rules { get; init; } =
        Rules ?? [];

    public IReadOnlyList<AccountingRulesStudioPromotionQueueItemDto> PromotionQueue { get; init; } =
        PromotionQueue ?? [];
}

public sealed record LedgerBookSetupCandidateDto(
    string FundProfileId,
    Guid FundStructureNodeId,
    FundStructureNodeKindDto FundStructureNodeKind,
    string DisplayName,
    string BaseCurrency,
    AccountingBasisKindDto AccountingBasis,
    string AccountingPolicyId,
    string AccountingPolicyVersion,
    string SuggestedAction,
    string? Description = null,
    Guid? SourceLedgerBookId = null,
    Guid? RequestedLedgerBookId = null);

public sealed record AccountingConfigurationWorkspaceDto(
    string FundProfileId,
    Guid? LedgerBookId,
    AccountingConfigurationStatusDto Status,
    string ConfigurationVersion,
    DateTimeOffset UpdatedAtUtc,
    IReadOnlyList<LedgerBookDto> LedgerBooks,
    IReadOnlyList<ChartOfAccountsNodeDto> ChartOfAccounts,
    IReadOnlyList<JournalEntryTemplateDto> JournalTemplates,
    IReadOnlyList<PostingRuleDto> PostingRules,
    IReadOnlyList<AccountingConfigurationValidationIssueDto> ValidationIssues,
    IReadOnlyList<AccountingActionAuditEventDto> AuditTrail,
    IReadOnlyList<AccountingRuleTestCaseDto>? RuleTestCases = null,
    AccountingRulesStudioDto? RulesStudio = null,
    LedgerBookSetupCandidateDto? LedgerBookSetupCandidate = null,
    string? TenantId = null,
    string? CompanyId = null)
{
    public IReadOnlyList<AccountingRuleTestCaseDto> RuleTestCases { get; init; } =
        RuleTestCases ?? [];
}

public sealed record UpsertChartOfAccountsNodeRequest(
    string FundProfileId,
    ChartOfAccountsNodeDto Node,
    string Actor,
    string? CorrelationId = null,
    IReadOnlyList<string>? EvidenceLinks = null,
    string? CompanyId = null,
    IReadOnlyList<string>? ReportGroupPrincipalIds = null,
    Guid? LedgerBookId = null,
    string? TenantId = null);

public sealed record UpsertJournalEntryTemplateRequest(
    string FundProfileId,
    JournalEntryTemplateDto Template,
    string Actor,
    string? CorrelationId = null,
    IReadOnlyList<string>? EvidenceLinks = null,
    string? CompanyId = null,
    IReadOnlyList<string>? ReportGroupPrincipalIds = null,
    Guid? LedgerBookId = null,
    string? TenantId = null);

public sealed record UpsertPostingRuleRequest(
    string FundProfileId,
    PostingRuleDto Rule,
    string Actor,
    string? CorrelationId = null,
    IReadOnlyList<string>? EvidenceLinks = null,
    string? CompanyId = null,
    IReadOnlyList<string>? ReportGroupPrincipalIds = null,
    Guid? LedgerBookId = null,
    string? TenantId = null);

public sealed record ApprovePostingRulePromotionRequest(
    string FundProfileId,
    string RuleId,
    string RuleVersion,
    string Actor,
    string ApprovalId,
    string Notes,
    IReadOnlyList<string>? EvidenceLinks = null,
    string? RequestedBy = null,
    DateTimeOffset? RequestedAtUtc = null,
    string? CorrelationId = null,
    string? CompanyId = null,
    IReadOnlyList<string>? ReportGroupPrincipalIds = null,
    Guid? LedgerBookId = null,
    OperationsActionOriginDto ActionOrigin = OperationsActionOriginDto.HumanOperator,
    string? TenantId = null)
{
    public IReadOnlyList<string> EvidenceLinks { get; init; } =
        EvidenceLinks ?? [];
}

public sealed record UpsertAccountingRuleTestCaseRequest(
    string FundProfileId,
    AccountingRuleTestCaseDto TestCase,
    string Actor,
    Guid? LedgerBookId = null,
    string? CorrelationId = null,
    IReadOnlyList<string>? EvidenceLinks = null,
    string? CompanyId = null,
    IReadOnlyList<string>? ReportGroupPrincipalIds = null,
    string? TenantId = null);

public sealed record PreviewJournalTemplateRequest(
    string FundProfileId,
    string TemplateId,
    string Actor,
    Guid? LedgerBookId = null,
    string? CorrelationId = null,
    string? TenantId = null,
    string? CompanyId = null);

public sealed record AccountingJournalPreviewLineDto(
    string AccountPath,
    string AccountName,
    AccountingTemplateLineSideDto Side,
    decimal Amount,
    string Currency,
    string? Description = null);

public sealed record AccountingJournalTemplatePreviewDto(
    string TemplateId,
    string DisplayName,
    bool IsBalanced,
    decimal TotalDebits,
    decimal TotalCredits,
    IReadOnlyList<AccountingJournalPreviewLineDto> Lines,
    IReadOnlyList<AccountingConfigurationValidationIssueDto> ValidationIssues);

public sealed record ManualJournalEntryLineDto(
    string LineId,
    AccountingTemplateLineSideDto Side,
    decimal Amount,
    string Currency,
    string AccountPath,
    string? EntityId = null,
    string? FundAllocationId = null,
    Guid? SecurityId = null,
    string? SecurityDisplayName = null,
    string? TaxLotId = null,
    string? Description = null,
    string? EvidenceLink = null,
    LedgerDimensionSetDto? Dimensions = null,
    // Structured ledger-account identity beyond the chart path. Automated drafts built from
    // scoped trial-balance rows (for example period-close entries on symbol- or
    // financial-account-scoped revenue/expense accounts) carry these so posting lands on the
    // scoped account and actually zeroes the scoped balance instead of an unscoped aggregate.
    string? LedgerAccountSymbol = null,
    string? LedgerAccountFinancialAccountId = null);

public sealed record ManualJournalEntryEvidenceAttachmentDto(
    string AttachmentId,
    string DisplayName,
    string EvidenceKind,
    string Uri,
    string SourceSystem,
    DateTimeOffset AddedAtUtc,
    string AddedBy,
    string? LineId = null,
    string? Description = null);

public sealed record TreasuryLedgerContextDto(
    DateOnly? EffectiveDate = null,
    string? IdempotencyKey = null,
    string? FundEventId = null,
    string? FundEventType = null,
    string? CapitalAccountId = null,
    string? InvestorId = null,
    string? PaymentIntentId = null,
    string? SettlementReference = null);

public sealed record ManualJournalEntryDraftDto(
    Guid JournalEntryId,
    ManualJournalEntryStatusDto Status,
    string FundProfileId,
    Guid? LedgerBookId,
    AccountingBasisKindDto AccountingBasis,
    DateOnly AccountingDate,
    string? PeriodId,
    string? EntityId,
    string? FundNodeId,
    string Currency,
    string Memo,
    string PreparedBy,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    int Version,
    IReadOnlyList<ManualJournalEntryLineDto> Lines,
    IReadOnlyList<string> EvidenceLinks,
    IReadOnlyList<AccountingConfigurationValidationIssueDto> ValidationIssues,
    IReadOnlyList<ManualJournalEntryEvidenceAttachmentDto>? EvidenceAttachments = null,
    decimal TotalDebits = 0m,
    decimal TotalCredits = 0m,
    decimal Imbalance = 0m,
    string? ApprovalId = null,
    DateTimeOffset? SubmittedAtUtc = null,
    string? SubmittedBy = null,
    ManualJournalEntryTypeDto EntryType = ManualJournalEntryTypeDto.General,
    TreasuryLedgerContextDto? TreasuryContext = null,
    LedgerDimensionSetDto? Dimensions = null,
    IReadOnlyList<JournalEntryLifecycleTransitionDto>? LifecycleTransitions = null,
    Guid? ReversalOfJournalEntryId = null,
    Guid? RebookedFromJournalEntryId = null,
    DateTimeOffset? ApprovedAtUtc = null,
    string? ApprovedBy = null,
    DateTimeOffset? PostedAtUtc = null,
    string? PostedBy = null,
    DateTimeOffset? ClosedLockedAtUtc = null,
    string? CloseLockedBy = null,
    JournalEntryReversalDto? Reversal = null,
    JournalEntryRebookDto? Rebook = null,
    string? TenantId = null,
    string? CompanyId = null,
    AutomatedJournalEvidenceAssessmentDto? AutomationEvidenceAssessment = null)
{
    public IReadOnlyList<JournalEntryLifecycleTransitionDto> LifecycleTransitions { get; init; } =
        LifecycleTransitions ?? [];
}

public sealed record RuleDryRunRequestDto(
    string FundProfileId,
    string SourceEventType,
    decimal EventAmount,
    string Currency,
    DateOnly EffectiveDate,
    string Actor,
    Guid? LedgerBookId = null,
    LedgerDimensionSetDto? Dimensions = null,
    string? CounterpartyId = null,
    string? InstrumentSymbol = null,
    string? CorrelationId = null,
    string? TenantId = null,
    string? CompanyId = null);

public sealed record AccountingRuleDryRunMatchDto(
    string RuleId,
    string DisplayName,
    string RuleVersion,
    int Priority,
    bool IsMatched,
    IReadOnlyList<string> Explanations,
    IReadOnlyList<AccountingConfigurationValidationIssueDto> ValidationIssues);

public sealed record RuleDryRunResultDto(
    string FundProfileId,
    Guid? LedgerBookId,
    string SourceEventType,
    DateOnly EffectiveDate,
    decimal EventAmount,
    string Currency,
    bool IsPostingBalanced,
    string? SelectedRuleId,
    IReadOnlyList<AccountingRuleDryRunMatchDto> RuleMatches,
    IReadOnlyList<AccountingJournalPreviewLineDto> GeneratedLines,
    IReadOnlyList<AccountingConfigurationValidationIssueDto> ValidationIssues,
    IReadOnlyList<GeneratedPostingLineDto>? GeneratedPostingLines = null)
{
    public IReadOnlyList<GeneratedPostingLineDto> GeneratedPostingLines { get; init; } =
        GeneratedPostingLines ?? [];
}

public sealed record PostingRuleJournalCandidateRequestDto(
    string FundProfileId,
    string SourceEventType,
    decimal EventAmount,
    string Currency,
    DateOnly EffectiveDate,
    string Actor,
    Guid AggregateId,
    Guid PeriodId,
    DateTimeOffset AccountingTimestamp,
    string Description,
    AccountingBasisKindDto AccountingBasis = AccountingBasisKindDto.Primary,
    Guid? LedgerBookId = null,
    LedgerDimensionSetDto? Dimensions = null,
    string? CounterpartyId = null,
    string? InstrumentSymbol = null,
    Guid? CorrelationId = null,
    Guid? SourceEventId = null,
    Guid? SourceJournalEntryId = null,
    string? PolicyId = null,
    AccountingTreatmentKindDto? TreatmentKind = null,
    LedgerPostingKindDto PostingKind = LedgerPostingKindDto.Originating,
    LedgerAdjustmentApprovalMetadataDto? AdjustmentApproval = null,
    TreasuryLedgerContextDto? TreasuryContext = null,
    IReadOnlyList<string>? EvidenceLinks = null,
    string? TenantId = null,
    string? CompanyId = null)
{
    public IReadOnlyList<string> EvidenceLinks { get; init; } =
        EvidenceLinks ?? [];

    public AccountingBookContextDto? BookContext { get; init; }

    public Guid? BookPositionId { get; init; }

    public EconomicEventReferenceDto? EconomicEvent { get; init; }

    public ProjectionLineageDto? ProjectionLineage { get; init; }

    public AccountingRulePackReferenceDto? RulePackReference { get; init; }
}

public sealed record PostingRuleJournalCandidateIssueDto(
    string Code,
    AccountingConfigurationValidationSeverityDto Severity,
    string Message,
    bool BlocksCandidate,
    string? TargetId = null,
    string? SuggestedAction = null);

public sealed record PostingRuleJournalCandidateResultDto(
    RuleDryRunResultDto DryRunResult,
    string? SelectedRuleId,
    string? SelectedRuleVersion,
    IReadOnlyList<GeneratedPostingLineDto> GeneratedPostingLines,
    AccountingPostingCommandDto? PostingCommand,
    Guid? JournalEntryId,
    decimal TotalDebits,
    decimal TotalCredits,
    decimal Imbalance,
    bool IsBalanced,
    bool HasBlockingIssues,
    bool CanSubmitForApproval,
    bool CanPostWithoutAdditionalApproval,
    IReadOnlyList<string> EvidenceLinks,
    IReadOnlyList<PostingRuleJournalCandidateIssueDto> Issues)
{
    public IReadOnlyList<GeneratedPostingLineDto> GeneratedPostingLines { get; init; } =
        GeneratedPostingLines ?? [];

    public IReadOnlyList<string> EvidenceLinks { get; init; } =
        EvidenceLinks ?? [];

    public IReadOnlyList<PostingRuleJournalCandidateIssueDto> Issues { get; init; } =
        Issues ?? [];

    public AccountingBookContextDto? BookContext { get; init; }

    public Guid? BookPositionId { get; init; }

    public EconomicEventReferenceDto? EconomicEvent { get; init; }

    public ProjectionLineageDto? ProjectionLineage { get; init; }

    public AccountingRulePackReferenceDto? RulePackReference { get; init; }
}

public sealed record PostPostingRuleJournalCandidateRequestDto(
    PostingRuleJournalCandidateRequestDto Candidate,
    string Actor,
    string ApprovalId,
    string? ApprovalNotes = null,
    IReadOnlyList<string>? EvidenceLinks = null,
    string? CorrelationId = null,
    OperationsActionOriginDto ActionOrigin = OperationsActionOriginDto.HumanOperator,
    string? TenantId = null,
    string? CompanyId = null)
{
    public IReadOnlyList<string> EvidenceLinks { get; init; } =
        EvidenceLinks ?? [];
}

public sealed record PostedPostingRuleJournalCandidateResultDto(
    PostingRuleJournalCandidateResultDto Candidate,
    PostedLedgerJournalEntryResultDto PostedJournal,
    bool WasReplay = false);

public sealed record AccountingBasisProjectionTargetDto(
    AccountingBasisKindDto AccountingBasis,
    Guid LedgerBookId,
    Guid PeriodId,
    string? PolicyId = null,
    AccountingTreatmentKindDto? TreatmentKind = null,
    LedgerDimensionSetDto? Dimensions = null);

public sealed record AccountingBasisProjectionSetRequestDto(
    string FundProfileId,
    string SourceEventType,
    decimal EventAmount,
    string Currency,
    DateOnly EffectiveDate,
    string Actor,
    Guid SourceEventId,
    DateTimeOffset AccountingTimestamp,
    string Description,
    IReadOnlyList<AccountingBasisProjectionTargetDto> Targets,
    Guid? CorrelationId = null,
    string? CounterpartyId = null,
    string? InstrumentSymbol = null,
    TreasuryLedgerContextDto? TreasuryContext = null,
    IReadOnlyList<string>? EvidenceLinks = null,
    string? TenantId = null,
    string? CompanyId = null)
{
    public IReadOnlyList<AccountingBasisProjectionTargetDto> Targets { get; init; } =
        Targets ?? [];

    public IReadOnlyList<string> EvidenceLinks { get; init; } =
        EvidenceLinks ?? [];
}

public sealed record AccountingBasisProjectionItemDto(
    AccountingBasisKindDto AccountingBasis,
    Guid LedgerBookId,
    Guid PeriodId,
    PostingRuleJournalCandidateResultDto Candidate,
    string Status,
    string? DisabledReason = null);

public sealed record AccountingBasisProjectionSetDto(
    Guid SourceEventId,
    string FundProfileId,
    string SourceEventType,
    DateOnly EffectiveDate,
    decimal EventAmount,
    string Currency,
    DateTimeOffset ProjectedAtUtc,
    IReadOnlyList<AccountingBasisProjectionItemDto>? Items = null)
{
    public IReadOnlyList<AccountingBasisProjectionItemDto> Items { get; init; } =
        Items ?? [];
}

public sealed record AccountingRuleTestCaseDto(
    string TestCaseId,
    string DisplayName,
    RuleDryRunRequestDto Request,
    string? ExpectedRuleId = null,
    string? ExpectedRuleVersion = null,
    bool ExpectBalancedPosting = true,
    IReadOnlyList<string>? ExpectedIssueCodes = null,
    IReadOnlyList<GeneratedPostingLineDto>? ExpectedGeneratedPostingLines = null,
    IReadOnlyList<string>? EvidenceLinks = null)
{
    public IReadOnlyList<string> ExpectedIssueCodes { get; init; } =
        ExpectedIssueCodes ?? [];

    public IReadOnlyList<GeneratedPostingLineDto> ExpectedGeneratedPostingLines { get; init; } =
        ExpectedGeneratedPostingLines ?? [];

    public IReadOnlyList<string> EvidenceLinks { get; init; } =
        EvidenceLinks ?? [];
}

public sealed record AccountingRuleTestCaseResultDto(
    string TestCaseId,
    string DisplayName,
    bool Passed,
    RuleDryRunResultDto DryRunResult,
    IReadOnlyList<AccountingConfigurationValidationIssueDto> AssertionIssues);

public sealed record AccountingRuleTestSuiteResultDto(
    string FundProfileId,
    Guid? LedgerBookId,
    DateTimeOffset ExecutedAtUtc,
    string Actor,
    int TotalCount,
    int PassedCount,
    int FailedCount,
    IReadOnlyList<AccountingRuleTestCaseResultDto> Results);

public sealed record ExecuteAccountingRuleTestCasesRequestDto(
    string FundProfileId,
    string Actor,
    IReadOnlyList<AccountingRuleTestCaseDto>? TestCases = null,
    Guid? LedgerBookId = null,
    string? CorrelationId = null,
    string? TenantId = null,
    string? CompanyId = null)
{
    public IReadOnlyList<AccountingRuleTestCaseDto> TestCases { get; init; } =
        TestCases ?? [];
}

public sealed record JournalEntryLifecycleTransitionDto(
    string TransitionId,
    ManualJournalEntryStatusDto FromStatus,
    ManualJournalEntryStatusDto ToStatus,
    JournalEntryLifecycleActionDto Action,
    string Actor,
    DateTimeOffset RecordedAtUtc,
    string? Notes = null,
    string? CorrelationId = null,
    IReadOnlyList<string>? EvidenceLinks = null)
{
    public IReadOnlyList<string> EvidenceLinks { get; init; } =
        EvidenceLinks ?? [];
}

public sealed record JournalEntryLifecycleActionRequestDto(
    Guid JournalEntryId,
    string FundProfileId,
    JournalEntryLifecycleActionDto Action,
    string Actor,
    int Version,
    string? Notes = null,
    string? CorrelationId = null,
    IReadOnlyList<string>? EvidenceLinks = null,
    OperationsActionOriginDto ActionOrigin = OperationsActionOriginDto.HumanOperator,
    bool PeriodIsLocked = false,
    IReadOnlyList<ManualJournalEntryLineDto>? RebookLines = null,
    Guid? LedgerBookId = null,
    string? TenantId = null,
    string? CompanyId = null,
    IReadOnlyList<string>? ReportGroupPrincipalIds = null)
{
    public IReadOnlyList<string> EvidenceLinks { get; init; } =
        EvidenceLinks ?? [];

    public IReadOnlyList<ManualJournalEntryLineDto> RebookLines { get; init; } =
        RebookLines ?? [];
}

public sealed record JournalEntryReversalDto(
    Guid OriginalJournalEntryId,
    Guid ReversalJournalEntryId,
    string Reason,
    DateTimeOffset CreatedAtUtc,
    string CreatedBy);

public sealed record JournalEntryRebookDto(
    Guid OriginalJournalEntryId,
    Guid RebookJournalEntryId,
    string Reason,
    DateTimeOffset CreatedAtUtc,
    string CreatedBy);

public sealed record PostedLedgerJournalEntryResultDto(
    Guid JournalEntryId,
    Guid LedgerBookId,
    AccountingBasisKindDto AccountingBasis,
    Guid PeriodId,
    Guid AggregateId,
    Guid? CommandId,
    Guid? SourceEventId,
    Guid? CorrelationId,
    long? GlobalSequence = null,
    DateTimeOffset? PostedAtUtc = null,
    string? IdempotencyKey = null);

public sealed record JournalEntryLifecycleActionResultDto(
    ManualJournalEntryDraftDto JournalEntry,
    JournalEntryLifecycleTransitionDto Transition,
    IReadOnlyList<ManualJournalEntryDraftDto>? GeneratedJournalEntries = null,
    PostedLedgerJournalEntryResultDto? PostedJournal = null)
{
    public IReadOnlyList<ManualJournalEntryDraftDto> GeneratedJournalEntries { get; init; } =
        GeneratedJournalEntries ?? [];
}
