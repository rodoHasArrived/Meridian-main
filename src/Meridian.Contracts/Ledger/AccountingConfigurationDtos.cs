using System.Text.Json.Serialization;
using Meridian.Contracts.AccountingSystem;
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
    ManagementFee = 14
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
    IReadOnlyList<string>? ReportGroupPrincipalIds = null);

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
    int WarningIssueCount);

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
    LedgerBookSetupCandidateDto? LedgerBookSetupCandidate = null)
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
    Guid? LedgerBookId = null);

public sealed record UpsertJournalEntryTemplateRequest(
    string FundProfileId,
    JournalEntryTemplateDto Template,
    string Actor,
    string? CorrelationId = null,
    IReadOnlyList<string>? EvidenceLinks = null,
    string? CompanyId = null,
    IReadOnlyList<string>? ReportGroupPrincipalIds = null,
    Guid? LedgerBookId = null);

public sealed record UpsertPostingRuleRequest(
    string FundProfileId,
    PostingRuleDto Rule,
    string Actor,
    string? CorrelationId = null,
    IReadOnlyList<string>? EvidenceLinks = null,
    string? CompanyId = null,
    IReadOnlyList<string>? ReportGroupPrincipalIds = null,
    Guid? LedgerBookId = null);

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
    OperationsActionOriginDto ActionOrigin = OperationsActionOriginDto.HumanOperator)
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
    IReadOnlyList<string>? ReportGroupPrincipalIds = null);

public sealed record PreviewJournalTemplateRequest(
    string FundProfileId,
    string TemplateId,
    string Actor,
    Guid? LedgerBookId = null,
    string? CorrelationId = null);

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
    LedgerDimensionSetDto? Dimensions = null);

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
    JournalEntryRebookDto? Rebook = null)
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
    string? CorrelationId = null);

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
    IReadOnlyList<string>? EvidenceLinks = null)
{
    public IReadOnlyList<string> EvidenceLinks { get; init; } =
        EvidenceLinks ?? [];
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
    string? CorrelationId = null)
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
    Guid? LedgerBookId = null)
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

public sealed record JournalEntryLifecycleActionResultDto(
    ManualJournalEntryDraftDto JournalEntry,
    JournalEntryLifecycleTransitionDto Transition,
    IReadOnlyList<ManualJournalEntryDraftDto>? GeneratedJournalEntries = null)
{
    public IReadOnlyList<ManualJournalEntryDraftDto> GeneratedJournalEntries { get; init; } =
        GeneratedJournalEntries ?? [];
}

public sealed record PrivateCapitalFundEventDto(
    string FundEventId,
    string FundEventType,
    ManualJournalEntryTypeDto EntryType,
    ManualJournalEntryStatusDto JournalStatus,
    Guid JournalEntryId,
    DateOnly EffectiveDate,
    string CapitalAccountId,
    string? InvestorId,
    string Currency,
    decimal GrossAmount,
    decimal NetCapitalActivity,
    string Memo,
    string? PaymentIntentId,
    string? SettlementReference,
    IReadOnlyList<string> EvidenceLinks,
    IReadOnlyList<AccountingConfigurationValidationIssueDto> ValidationIssues,
    DateTimeOffset UpdatedAtUtc,
    bool IsPosted = false,
    string? ApprovalId = null);

public sealed record PrivateCapitalCapitalAccountActivityDto(
    string CapitalAccountId,
    string? InvestorId,
    string Currency,
    decimal Contributions,
    decimal Distributions,
    decimal Subscriptions,
    decimal Redemptions,
    decimal ManagementFees,
    decimal NetActivity,
    int FundEventCount,
    DateOnly? LastEffectiveDate,
    string? LastFundEventType,
    IReadOnlyList<string> FundEventIds);

public sealed record PrivateCapitalCapitalAccountSubledgerEntryDto(
    string SubledgerEntryId,
    string CapitalAccountId,
    string? InvestorId,
    string Currency,
    string FundEventId,
    string FundEventType,
    ManualJournalEntryTypeDto EntryType,
    ManualJournalEntryStatusDto ApprovalState,
    Guid JournalEntryId,
    DateOnly EffectiveDate,
    decimal GrossAmount,
    decimal NetCapitalActivity,
    decimal RunningNetActivity,
    string Memo,
    IReadOnlyList<string> EvidenceLinks,
    IReadOnlyList<AccountingConfigurationValidationIssueDto> ValidationIssues,
    DateTimeOffset UpdatedAtUtc,
    bool IsPosted = false);

public sealed record PrivateCapitalLedgerLineImpactDto(
    string LineId,
    string AccountPath,
    AccountingTemplateLineSideDto Side,
    decimal Amount,
    string Currency,
    string? EntityId,
    Guid? SecurityId,
    string? SecurityDisplayName,
    string? EvidenceLink);

public sealed record PrivateCapitalLedgerImpactDto(
    string LedgerImpactId,
    Guid JournalEntryId,
    string FundEventId,
    string FundEventType,
    string CapitalAccountId,
    string? InvestorId,
    ManualJournalEntryStatusDto ApprovalState,
    DateOnly EffectiveDate,
    string Currency,
    decimal TotalDebits,
    decimal TotalCredits,
    decimal Imbalance,
    int LineCount,
    bool IsBalanced,
    bool IsPostingReady,
    IReadOnlyList<string> EvidenceLinks,
    IReadOnlyList<PrivateCapitalLedgerLineImpactDto> Lines,
    IReadOnlyList<AccountingConfigurationValidationIssueDto> ValidationIssues);

public sealed record PrivateCapitalReportOutputDto(
    string ReportOutputId,
    string ReportOutputType,
    string DisplayName,
    string ReportRoute,
    string FundEventId,
    string FundEventType,
    string CapitalAccountId,
    string? InvestorId,
    ManualJournalEntryStatusDto ApprovalState,
    DateOnly EffectiveDate,
    string Currency,
    decimal NetCapitalActivity,
    int EvidenceLinkCount,
    IReadOnlyList<string> EvidenceLinks,
    bool IsReportReady,
    IReadOnlyList<AccountingConfigurationValidationIssueDto> ValidationIssues,
    bool IsPublished = false,
    string? ReportPackId = null,
    string? ReportWorkflowState = null,
    string? PublicationManifestId = null,
    string? RetainedManifestPath = null,
    string? PublicationEvidenceHash = null,
    DateTimeOffset? PublishedAtUtc = null,
    string? PublishedBy = null,
    int ReportLineProvenanceCount = 0,
    string? ReportOutputRoute = null,
    string? FundEventRecordRoute = null,
    string? CapitalAccountSubledgerRoute = null,
    string? EvidenceRoute = null,
    string? ApprovalRoute = null,
    string? ReadinessLabel = null,
    string? ReadinessReason = null,
    string? NextAction = null,
    string? NextActionRoute = null);

public sealed record PrivateCapitalEvidenceCategoryDto(
    string CategoryId,
    string Label,
    bool IsReady,
    string Summary,
    int EvidenceLinkCount,
    IReadOnlyList<string> EvidenceLinks,
    IReadOnlyList<string>? RequiredEvidence = null)
{
    public IReadOnlyList<string> RequiredEvidence { get; init; } =
        RequiredEvidence ?? [];
}

public sealed record PrivateCapitalPaymentIntentEvidenceDto(
    string? PaymentIntentId,
    string? SettlementReference,
    PrivateCapitalPaymentIntentEvidenceStatusDto Status,
    bool IsReady,
    PaymentIntentCashDirectionDto Direction,
    decimal Amount,
    string Currency,
    DateOnly EffectiveDate,
    string Summary,
    int CashEvidenceLinkCount,
    IReadOnlyList<string> CashEvidenceLinks,
    IReadOnlyList<string>? RequiredEvidence = null,
    string? EvidenceRoute = null)
{
    public IReadOnlyList<string> RequiredEvidence { get; init; } =
        RequiredEvidence ?? [];
}

public sealed record PaymentIntentExpectedCashMovementDto(
    string PaymentIntentId,
    PaymentIntentCashDirectionDto Direction,
    decimal Amount,
    string Currency,
    DateOnly EffectiveDate,
    string? SettlementReference,
    string? FundEventId,
    string? FundEventType,
    string? CapitalAccountId,
    string? InvestorId,
    string Purpose,
    string? Payee = null,
    string? AccountScope = null,
    string? BusinessPurpose = null,
    string? ApprovalPolicy = null,
    IReadOnlyList<string>? SourceEvidenceLinks = null)
{
    public IReadOnlyList<string> SourceEvidenceLinks { get; init; } =
        SourceEvidenceLinks ?? [];
}

public sealed record PaymentIntentApprovalStepDto(
    int Sequence,
    string Role,
    string Actor,
    string Status,
    DateTimeOffset? DecidedAtUtc = null,
    string? EvidenceRoute = null);

public sealed record PaymentIntentBankEvidenceDto(
    string EvidenceId,
    string EvidenceKind,
    string Status,
    string Summary,
    Guid? BankTransactionId = null,
    string? TransactionType = null,
    decimal? Amount = null,
    string? Currency = null,
    DateOnly? EffectiveDate = null,
    DateTimeOffset? RecordedAtUtc = null,
    string? ExternalRef = null,
    string? EvidenceRoute = null,
    string? RecordedBy = null);

public sealed record PaymentIntentReconciliationLinkDto(
    string LinkId,
    string Status,
    string Summary,
    string? EvidenceRoute = null,
    string? ReconciliationCaseId = null,
    string? ReconciliationRunId = null);

public sealed record PaymentIntentAuditEventDto(
    string AuditEventId,
    DateTimeOffset RecordedAtUtc,
    string Actor,
    string Action,
    string Summary,
    IReadOnlyList<string>? EvidenceLinks = null)
{
    public IReadOnlyList<string> EvidenceLinks { get; init; } =
        EvidenceLinks ?? [];
}

public sealed record PaymentIntentWorkflowDto(
    string PaymentIntentId,
    string? SettlementReference,
    string FundProfileId,
    Guid? LedgerBookId,
    string FundEventId,
    Guid JournalEntryId,
    string Requester,
    DateTimeOffset RequestedAtUtc,
    PaymentIntentWorkflowStatusDto Status,
    string StatusLabel,
    string ReadinessReason,
    string ExecutionDeferredReason,
    PaymentIntentExpectedCashMovementDto ExpectedCashMovement,
    string EvidenceRoute,
    string WorkbenchRoute,
    IReadOnlyList<PaymentIntentApprovalStepDto>? ApprovalChain = null,
    IReadOnlyList<PaymentIntentBankEvidenceDto>? BankEvidence = null,
    IReadOnlyList<PaymentIntentReconciliationLinkDto>? ReconciliationLinks = null,
    IReadOnlyList<PaymentIntentAuditEventDto>? AuditHistory = null)
{
    public IReadOnlyList<PaymentIntentApprovalStepDto> ApprovalChain { get; init; } =
        ApprovalChain ?? [];

    public IReadOnlyList<PaymentIntentBankEvidenceDto> BankEvidence { get; init; } =
        BankEvidence ?? [];

    public IReadOnlyList<PaymentIntentReconciliationLinkDto> ReconciliationLinks { get; init; } =
        ReconciliationLinks ?? [];

    public IReadOnlyList<PaymentIntentAuditEventDto> AuditHistory { get; init; } =
        AuditHistory ?? [];
}

public sealed record PrivateCapitalFundEventLedgerRecordDto(
    string FundEventRecordId,
    string FundEventId,
    string FundEventType,
    string CapitalAccountId,
    string? InvestorId,
    ManualJournalEntryStatusDto ApprovalState,
    Guid JournalEntryId,
    DateOnly EffectiveDate,
    string Currency,
    decimal GrossAmount,
    decimal NetCapitalActivity,
    decimal CapitalAccountOpeningNetActivity,
    decimal CapitalAccountEndingNetActivity,
    string Memo,
    string? PaymentIntentId,
    string? SettlementReference,
    string ActivityRoute,
    string EvidenceRoute,
    string? ApprovalId,
    string? ApprovalRoute,
    bool IsPosted,
    bool IsPostingReady,
    bool IsReportReady,
    bool IsPublished,
    PrivateCapitalFundEventLedgerReadinessDto Readiness,
    string ReadinessLabel,
    string ReadinessReason,
    string NextAction,
    string? NextActionRoute,
    int EvidenceLinkCount,
    int CapitalAccountSubledgerEntryCount,
    int LedgerImpactCount,
    int ReportOutputCount,
    int ValidationIssueCount,
    string? PrimaryReportOutputId,
    string? PrimaryReportOutputType,
    string? PrimaryReportRoute,
    string? ReportWorkflowState,
    string? PublicationManifestId,
    string? RetainedManifestPath,
    int ReportLineProvenanceCount,
    IReadOnlyList<string> EvidenceLinks,
    PrivateCapitalFundEventDto FundEvent,
    IReadOnlyList<PrivateCapitalCapitalAccountSubledgerEntryDto> CapitalAccountSubledgerEntries,
    IReadOnlyList<PrivateCapitalLedgerImpactDto> LedgerImpacts,
    IReadOnlyList<PrivateCapitalReportOutputDto> ReportOutputs,
    IReadOnlyList<AccountingConfigurationValidationIssueDto> ValidationIssues,
    IReadOnlyList<PrivateCapitalEvidenceCategoryDto>? EvidenceCategories = null,
    PrivateCapitalPaymentIntentEvidenceDto? PaymentIntentEvidence = null)
{
    public IReadOnlyList<PrivateCapitalEvidenceCategoryDto> EvidenceCategories { get; init; } =
        EvidenceCategories ?? [];
}

public sealed record PrivateCapitalFundEventCommandCenterLaneDto(
    string LaneId,
    string Label,
    string Status,
    bool IsReady,
    string Summary,
    string? Route,
    int EvidenceLinkCount,
    IReadOnlyList<string> EvidenceLinks,
    IReadOnlyList<string>? RequiredActions = null)
{
    public IReadOnlyList<string> RequiredActions { get; init; } =
        RequiredActions ?? [];
}

public sealed record PrivateCapitalFundEventCommandCenterSupportPackageDto(
    string PackageId,
    string Label,
    string Status,
    string? Route,
    int EvidenceLinkCount,
    IReadOnlyList<string> EvidenceLinks,
    IReadOnlyList<string>? RequiredActions = null)
{
    public IReadOnlyList<string> RequiredActions { get; init; } =
        RequiredActions ?? [];
}

public sealed record PrivateCapitalFundEventCommandCenterDto(
    string FundEventId,
    string FundEventType,
    string FundProfileId,
    Guid? LedgerBookId,
    DateTimeOffset ProjectedAtUtc,
    string CommandCenterRoute,
    PrivateCapitalFundEventLedgerReadinessDto Readiness,
    string ReadinessLabel,
    string ReadinessReason,
    string NextAction,
    string? NextActionRoute,
    int ReadyLaneCount,
    int BlockedLaneCount,
    PrivateCapitalFundEventLedgerRecordDto FundEventRecord,
    IReadOnlyList<PrivateCapitalFundEventCommandCenterLaneDto> Lanes,
    IReadOnlyList<PrivateCapitalFundEventCommandCenterSupportPackageDto> SupportPackages,
    IReadOnlyList<string> LiveCapabilities,
    IReadOnlyList<string> PlannedCapabilities);

public sealed record PrivateCapitalCapitalAccountSubledgerDto(
    string SubledgerId,
    string FundProfileId,
    Guid? LedgerBookId,
    DateTimeOffset ProjectedAtUtc,
    string CapitalAccountId,
    string? InvestorId,
    string Currency,
    string ActivityRoute,
    decimal Contributions,
    decimal Distributions,
    decimal Subscriptions,
    decimal Redemptions,
    decimal ManagementFees,
    decimal OpeningNetActivity,
    decimal EndingNetActivity,
    decimal NetCapitalActivity,
    int FundEventCount,
    int ApprovalQueueCount,
    int PostedFundEventCount,
    int PublishedReportOutputCount,
    int EvidenceLinkCount,
    int ValidationIssueCount,
    DateOnly? FirstEffectiveDate,
    DateOnly? LastEffectiveDate,
    string? LastFundEventType,
    IReadOnlyList<string> EvidenceLinks,
    PrivateCapitalCapitalAccountActivityDto? CapitalAccount,
    IReadOnlyList<PrivateCapitalFundEventLedgerRecordDto> FundEventRecords,
    IReadOnlyList<PrivateCapitalCapitalAccountSubledgerEntryDto> SubledgerEntries,
    IReadOnlyList<PrivateCapitalLedgerImpactDto> LedgerImpacts,
    IReadOnlyList<PrivateCapitalReportOutputDto> ReportOutputs,
    IReadOnlyList<AccountingConfigurationValidationIssueDto> ValidationIssues,
    PrivateCapitalFundEventLedgerReadinessDto Readiness = PrivateCapitalFundEventLedgerReadinessDto.EvidenceMissing,
    string ReadinessLabel = "",
    string ReadinessReason = "",
    string NextAction = "",
    string? NextActionRoute = null,
    IReadOnlyList<PrivateCapitalEvidenceCategoryDto>? EvidenceCategories = null,
    PrivateCapitalPaymentIntentEvidenceDto? PaymentIntentEvidence = null)
{
    public IReadOnlyList<PrivateCapitalEvidenceCategoryDto> EvidenceCategories { get; init; } =
        EvidenceCategories ?? [];
}

public sealed record PrivateCapitalActivityProjectionDto(
    string FundProfileId,
    Guid? LedgerBookId,
    DateTimeOffset ProjectedAtUtc,
    int FundEventCount,
    int CapitalAccountCount,
    int SubmittedFundEventCount,
    int ApprovalQueueCount,
    int PostedFundEventCount,
    int PublishedReportOutputCount,
    decimal NetCapitalActivity,
    string Currency,
    IReadOnlyList<PrivateCapitalFundEventDto> FundEvents,
    IReadOnlyList<PrivateCapitalCapitalAccountActivityDto> CapitalAccounts,
    IReadOnlyList<PrivateCapitalCapitalAccountSubledgerEntryDto> CapitalAccountSubledgerEntries,
    IReadOnlyList<PrivateCapitalLedgerImpactDto> LedgerImpacts,
    IReadOnlyList<PrivateCapitalReportOutputDto> ReportOutputs,
    IReadOnlyList<AccountingConfigurationValidationIssueDto> ValidationIssues,
    IReadOnlyList<PrivateCapitalFundEventLedgerRecordDto>? FundEventRecords = null,
    IReadOnlyList<PrivateCapitalCapitalAccountSubledgerDto>? CapitalAccountSubledgers = null,
    IReadOnlyList<PaymentIntentWorkflowDto>? PaymentIntents = null)
{
    public IReadOnlyList<PrivateCapitalFundEventLedgerRecordDto> FundEventRecords { get; init; } =
        FundEventRecords ?? [];

    public IReadOnlyList<PrivateCapitalCapitalAccountSubledgerDto> CapitalAccountSubledgers { get; init; } =
        CapitalAccountSubledgers ?? [];

    public IReadOnlyList<PaymentIntentWorkflowDto> PaymentIntents { get; init; } =
        PaymentIntents ?? [];
}

public sealed record CapitalAccountWorkbenchInvestorAccountDto(
    string AccountKey,
    string CapitalAccountId,
    string? InvestorId,
    string Currency,
    string ActivityRoute,
    PrivateCapitalFundEventLedgerReadinessDto Readiness,
    string ReadinessLabel,
    string ReadinessReason,
    string NextAction,
    string? NextActionRoute,
    decimal OpeningNetActivity,
    decimal EndingNetActivity,
    decimal NetCapitalActivity,
    decimal Contributions,
    decimal Distributions,
    decimal Subscriptions,
    decimal Redemptions,
    decimal ManagementFees,
    int FundEventCount,
    int PostedFundEventCount,
    int ApprovalQueueCount,
    int PublishedReportOutputCount,
    int EvidenceLinkCount,
    int ValidationIssueCount,
    string EvidenceCategorySummary,
    IReadOnlyList<string> EvidenceLinks,
    IReadOnlyList<PrivateCapitalEvidenceCategoryDto> EvidenceCategories,
    IReadOnlyList<PrivateCapitalFundEventLedgerRecordDto> FundEventRecords,
    IReadOnlyList<PrivateCapitalCapitalAccountSubledgerEntryDto> SubledgerEntries,
    IReadOnlyList<PrivateCapitalLedgerImpactDto> LedgerImpacts,
    IReadOnlyList<PrivateCapitalReportOutputDto> ReportOutputs,
    IReadOnlyList<AccountingConfigurationValidationIssueDto> ValidationIssues,
    PrivateCapitalPaymentIntentEvidenceDto? PaymentIntentEvidence = null);

public sealed record CapitalAccountWorkbenchAllocationRuleDto(
    string RuleId,
    string CapitalAccountId,
    string? InvestorId,
    string CategoryId,
    string Label,
    string Basis,
    bool IsSatisfied,
    string Reason,
    string? Route,
    int EvidenceLinkCount,
    IReadOnlyList<string> EvidenceLinks,
    IReadOnlyList<string> RequiredEvidence)
{
    public string RuleVersion { get; init; } = "";
    public DateOnly? EffectiveFrom { get; init; }
    public DateOnly? EffectiveTo { get; init; }
    public string Formula { get; init; } = "";
    public string ApprovalState { get; init; } = "";
    public string? ApprovalReference { get; init; }
    public string ReplayTrace { get; init; } = "";
    public IReadOnlyList<CapitalAccountWorkbenchAllocationInputDto> Inputs { get; init; } = [];
    public IReadOnlyList<string> RelatedFundEventIds { get; init; } = [];
}

public sealed record CapitalAccountWorkbenchAllocationInputDto(
    string InputId,
    string Kind,
    string SourceId,
    string Label,
    decimal? Amount,
    string? Currency,
    DateOnly? EffectiveDate,
    string? EvidenceRoute);

public sealed record CapitalAccountWorkbenchStatementLineageDto(
    string LineageId,
    string CapitalAccountId,
    string? InvestorId,
    string ReportOutputId,
    string ReportOutputType,
    string DisplayName,
    string ReportRoute,
    string? ReportPackId,
    string? ReportWorkflowState,
    bool IsPublished,
    bool IsReportReady,
    string? PublicationManifestId,
    string? RetainedManifestPath,
    string? PublicationEvidenceHash,
    DateTimeOffset? PublishedAtUtc,
    string? PublishedBy,
    int ReportLineProvenanceCount,
    bool HasRestatementLineage,
    string RestatementStatus,
    string? RestatementReasonCode,
    Guid? RestatementPriorVersionReportId,
    string? RestatementApprover,
    int RestatementChangedLineCount,
    int RestatementEvidenceLinkCount,
    string? ReportOutputRoute,
    string? EvidenceRoute,
    string? CapitalAccountSubledgerRoute,
    IReadOnlyList<string> EvidenceLinks,
    IReadOnlyList<string> RestatementEvidenceLinks)
{
    public IReadOnlyList<CapitalAccountWorkbenchRestatementChangedLineDto> RestatementChangedLines { get; init; } = [];
}

public sealed record CapitalAccountWorkbenchRestatementChangedLineDto(
    string LineKey,
    string PreviousValue,
    string CurrentValue,
    int EvidenceLinkCount,
    IReadOnlyList<string> EvidenceLinks);

public sealed record CapitalAccountWorkbenchAuditDrillThroughDto(
    string DrillThroughId,
    string Kind,
    string Label,
    string Summary,
    string? Route,
    bool IsAvailable,
    int EvidenceLinkCount,
    IReadOnlyList<string> EvidenceLinks,
    IReadOnlyList<string> RelatedIds);

public sealed record CapitalAccountWorkbenchDto(
    string FundProfileId,
    Guid? LedgerBookId,
    DateTimeOffset ProjectedAtUtc,
    string? CapitalAccountId,
    string? InvestorId,
    string Currency,
    string WorkbenchRoute,
    string StatusLabel,
    string StatusReason,
    int InvestorAccountCount,
    int FundEventCount,
    int StatementCount,
    int RestatementLineageCount,
    int AuditDrillThroughCount,
    decimal NetCapitalActivity,
    IReadOnlyList<CapitalAccountWorkbenchInvestorAccountDto> InvestorAccounts,
    IReadOnlyList<CapitalAccountWorkbenchAllocationRuleDto> AllocationRules,
    IReadOnlyList<CapitalAccountWorkbenchStatementLineageDto> StatementLineage,
    IReadOnlyList<CapitalAccountWorkbenchAuditDrillThroughDto> AuditDrillThroughs,
    IReadOnlyList<AccountingConfigurationValidationIssueDto> ValidationIssues,
    IReadOnlyList<string>? LiveCapabilities = null,
    IReadOnlyList<string>? PlannedCapabilities = null)
{
    public IReadOnlyList<string> LiveCapabilities { get; init; } =
        LiveCapabilities ?? [];

    public IReadOnlyList<string> PlannedCapabilities { get; init; } =
        PlannedCapabilities ?? [];
}

public sealed record DimensionMappingProfileDto(
    string ProfileId,
    string DisplayName,
    string ProviderId,
    LedgerDimensionSetDto MeridianDimensions,
    LedgerDimensionSetDto ExternalDimensions,
    AccountingCertificationStateDto CertificationState = AccountingCertificationStateDto.Draft,
    IReadOnlyList<AccountingConfigurationValidationIssueDto>? ValidationIssues = null)
{
    public IReadOnlyList<AccountingConfigurationValidationIssueDto> ValidationIssues { get; init; } =
        ValidationIssues ?? [];
}

public sealed record ExternalGlMappingProfileDto(
    string ProfileId,
    string ProviderId,
    string DisplayName,
    DateTimeOffset UpdatedAtUtc,
    IReadOnlyList<DimensionMappingProfileDto> DimensionMappings,
    IReadOnlyDictionary<string, string>? AccountMappings = null,
    AccountingCertificationStateDto CertificationState = AccountingCertificationStateDto.Draft)
{
    public IReadOnlyDictionary<string, string> AccountMappings { get; init; } =
        AccountMappings ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}

public sealed record ExternalGlExportCertificationDto(
    string CertificationId,
    AccountingCertificationStateDto State,
    string Actor,
    DateTimeOffset RecordedAtUtc,
    string Summary,
    IReadOnlyList<string>? EvidenceLinks = null)
{
    public IReadOnlyList<string> EvidenceLinks { get; init; } =
        EvidenceLinks ?? [];
}

public sealed record ExternalGlExportLineDto(
    string ExportLineId,
    string ReconciliationRowId,
    AccountingSystemReconciliationStatusDto SourceStatus,
    string MeridianAccountCode,
    string ExternalAccountId,
    string AccountName,
    string Currency,
    decimal Debit,
    decimal Credit,
    decimal NetAmount,
    LedgerDimensionSetDto? MeridianDimensions = null,
    LedgerDimensionSetDto? ExternalDimensions = null,
    IReadOnlyList<string>? EvidenceLinks = null)
{
    public IReadOnlyList<string> EvidenceLinks { get; init; } =
        EvidenceLinks ?? [];
}

public sealed record ExternalGlExportPackageDto(
    string ExportPackageId,
    string ProviderId,
    string FundProfileId,
    Guid? LedgerBookId,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    DateTimeOffset CreatedAtUtc,
    string CreatedBy,
    bool PostingEnabled,
    string PostingDisabledReason,
    IReadOnlyList<Guid> JournalEntryIds,
    IReadOnlyList<string> EvidenceLinks,
    ExternalGlExportCertificationDto? Certification = null,
    IReadOnlyList<AccountingConfigurationValidationIssueDto>? ValidationIssues = null,
    IReadOnlyList<ExternalGlExportLineDto>? GeneratedLines = null,
    string? MappingProfileId = null,
    string? ReconciliationId = null,
    bool RequireBalancedReconciliation = false)
{
    public IReadOnlyList<AccountingConfigurationValidationIssueDto> ValidationIssues { get; init; } =
        ValidationIssues ?? [];

    public IReadOnlyList<ExternalGlExportLineDto> GeneratedLines { get; init; } =
        GeneratedLines ?? [];
}

public sealed record ExternalGlExportPackageManifestDto(
    string ExportPackageId,
    string ProviderId,
    string FundProfileId,
    Guid? LedgerBookId,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    AccountingCertificationStateDto CertificationState,
    DateTimeOffset GeneratedAtUtc,
    string ContentHash,
    string ContentType,
    string FileName,
    bool ExternalPostingAllowed,
    string PostingDisabledReason,
    string Payload,
    IReadOnlyList<ExternalGlExportLineDto>? GeneratedLines = null,
    IReadOnlyList<string>? EvidenceLinks = null,
    IReadOnlyList<AccountingConfigurationValidationIssueDto>? ValidationIssues = null,
    string? MappingProfileId = null,
    string? ReconciliationId = null,
    bool RequireBalancedReconciliation = false)
{
    public IReadOnlyList<ExternalGlExportLineDto> GeneratedLines { get; init; } =
        GeneratedLines ?? [];

    public IReadOnlyList<string> EvidenceLinks { get; init; } =
        EvidenceLinks ?? [];

    public IReadOnlyList<AccountingConfigurationValidationIssueDto> ValidationIssues { get; init; } =
        ValidationIssues ?? [];
}

public sealed record CloseDependencyDto(
    string DependencyId,
    string DependsOnTaskId,
    string Reason);

public sealed record CloseSignOffDto(
    string SignOffId,
    string Role,
    string? Actor,
    ManualJournalEntryStatusDto ApprovalState,
    DateTimeOffset? SignedAtUtc = null,
    IReadOnlyList<string>? EvidenceLinks = null,
    string? Notes = null)
{
    public IReadOnlyList<string> EvidenceLinks { get; init; } =
        EvidenceLinks ?? [];
}

public sealed record CloseSignOffRequirementDto(
    string RequirementId,
    string Role,
    int RequiredApprovalCount,
    int ApprovedCount,
    bool IsSatisfied,
    string EvidenceRequirement);

public sealed record MaterialityPolicyDto(
    string PolicyId,
    decimal AmountThreshold,
    decimal PercentThreshold,
    string Currency,
    string ReviewRole,
    bool RequiresLateAdjustmentApproval);

public sealed record CloseTaskDto(
    string TaskId,
    string DisplayName,
    CloseTaskStatusDto Status,
    string Owner,
    DateOnly DueDate,
    IReadOnlyList<CloseDependencyDto> Dependencies,
    IReadOnlyList<CloseSignOffDto> SignOffs,
    IReadOnlyList<string> EvidenceLinks,
    string? BlockerReason = null,
    IReadOnlyList<CloseSignOffRequirementDto>? SignOffRequirements = null)
{
    public IReadOnlyList<CloseSignOffRequirementDto> SignOffRequirements { get; init; } =
        SignOffRequirements ?? [];
}

public sealed record CloseCalendarMilestoneDto(
    string MilestoneId,
    string TaskId,
    string DisplayName,
    string Owner,
    DateOnly DueDate,
    CloseTaskStatusDto Status,
    bool IsBlocked,
    bool IsSatisfied,
    bool IsPeriodLocked,
    int DependencyCount,
    int RequiredSignOffCount,
    int ApprovedSignOffCount,
    IReadOnlyList<string>? EvidenceLinks = null,
    string? BlockerReason = null)
{
    public IReadOnlyList<string> EvidenceLinks { get; init; } =
        EvidenceLinks ?? [];
}

public sealed record LateAdjustmentRequestDto(
    string RequestId,
    Guid JournalEntryId,
    string RequestedBy,
    DateTimeOffset RequestedAtUtc,
    decimal Amount,
    string Currency,
    string Reason,
    ManualJournalEntryStatusDto ApprovalState,
    MaterialityPolicyDto MaterialityPolicy,
    IReadOnlyList<string>? EvidenceLinks = null,
    string? DecidedBy = null,
    DateTimeOffset? DecidedAtUtc = null,
    string? DecisionNotes = null)
{
    public IReadOnlyList<string> EvidenceLinks { get; init; } =
        EvidenceLinks ?? [];
}

public sealed record CreateLateAdjustmentRequestDto(
    Guid WorkflowId,
    Guid JournalEntryId,
    decimal Amount,
    string Currency,
    string Reason,
    string RequestedBy,
    IReadOnlyList<string>? EvidenceLinks = null,
    string? CorrelationId = null,
    OperationsActionOriginDto ActionOrigin = OperationsActionOriginDto.HumanOperator)
{
    public IReadOnlyList<string> EvidenceLinks { get; init; } =
        EvidenceLinks ?? [];
}

public sealed record ReviewLateAdjustmentRequestDto(
    Guid WorkflowId,
    string RequestId,
    ManualJournalEntryStatusDto Decision,
    string Actor,
    string Notes,
    IReadOnlyList<string>? EvidenceLinks = null,
    string? CorrelationId = null,
    OperationsActionOriginDto ActionOrigin = OperationsActionOriginDto.HumanOperator)
{
    public IReadOnlyList<string> EvidenceLinks { get; init; } =
        EvidenceLinks ?? [];
}

public sealed record SignOffCloseTaskRequestDto(
    Guid WorkflowId,
    string TaskId,
    string Role,
    ManualJournalEntryStatusDto Decision,
    string Actor,
    string Notes,
    IReadOnlyList<string>? EvidenceLinks = null,
    string? CorrelationId = null,
    OperationsActionOriginDto ActionOrigin = OperationsActionOriginDto.HumanOperator)
{
    public IReadOnlyList<string> EvidenceLinks { get; init; } =
        EvidenceLinks ?? [];
}

public sealed record ClosePeriodPlanDto(
    string ClosePlanId,
    string FundProfileId,
    Guid? LedgerBookId,
    string PeriodId,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    DateOnly CloseDueDate,
    bool IsPeriodLocked,
    IReadOnlyList<CloseTaskDto> Tasks,
    IReadOnlyList<LateAdjustmentRequestDto> LateAdjustments,
    MaterialityPolicyDto MaterialityPolicy,
    IReadOnlyList<AccountingConfigurationValidationIssueDto>? ValidationIssues = null,
    IReadOnlyList<CloseCalendarMilestoneDto>? CloseCalendar = null)
{
    public IReadOnlyList<AccountingConfigurationValidationIssueDto> ValidationIssues { get; init; } =
        ValidationIssues ?? [];

    public IReadOnlyList<CloseCalendarMilestoneDto> CloseCalendar { get; init; } =
        CloseCalendar ?? [];
}

public sealed record ReportCertificationDto(
    string CertificationId,
    AccountingCertificationStateDto State,
    string Actor,
    DateTimeOffset RecordedAtUtc,
    string Summary,
    IReadOnlyList<string>? EvidenceLinks = null)
{
    public IReadOnlyList<string> EvidenceLinks { get; init; } =
        EvidenceLinks ?? [];
}

public sealed record RestatementWorkflowDto(
    string RestatementId,
    string PriorPackageId,
    string ReasonCode,
    ManualJournalEntryStatusDto ApprovalState,
    string RequestedBy,
    DateTimeOffset RequestedAtUtc,
    IReadOnlyList<string>? EvidenceLinks = null)
{
    public IReadOnlyList<string> EvidenceLinks { get; init; } =
        EvidenceLinks ?? [];
}

public sealed record ReportLineProvenanceDto(
    string StatementId,
    string LineId,
    string LineLabel,
    string SourceKind,
    decimal Amount,
    string Currency,
    LedgerDimensionSetDto Dimensions,
    IReadOnlyList<string>? EvidenceLinks = null)
{
    public IReadOnlyList<string> EvidenceLinks { get; init; } =
        EvidenceLinks ?? [];
}

public sealed record ReportExportArtifactDto(
    string ArtifactId,
    string ArtifactKind,
    string DisplayName,
    string Format,
    string Route,
    AccountingCertificationStateDto CertificationState,
    DateTimeOffset GeneratedAtUtc,
    string ContentHash,
    IReadOnlyList<string>? EvidenceLinks = null,
    string? SourceStatementId = null,
    Guid? LedgerBookId = null,
    LedgerDimensionSetDto? Dimensions = null)
{
    public IReadOnlyList<string> EvidenceLinks { get; init; } =
        EvidenceLinks ?? [];

    public LedgerDimensionSetDto Dimensions { get; init; } =
        Dimensions ?? new LedgerDimensionSetDto(BookId: LedgerBookId?.ToString("D"));
}

public sealed record ReportExportArtifactManifestDto(
    string PackageId,
    string ArtifactId,
    string ArtifactKind,
    string DisplayName,
    string Format,
    string Route,
    AccountingCertificationStateDto CertificationState,
    DateTimeOffset GeneratedAtUtc,
    string ContentHash,
    string ContentType,
    string FileName,
    bool ExternalPostingAllowed,
    string Payload,
    IReadOnlyList<string>? EvidenceLinks = null,
    string? SourceStatementId = null,
    Guid? LedgerBookId = null,
    LedgerDimensionSetDto? Dimensions = null)
{
    public IReadOnlyList<string> EvidenceLinks { get; init; } =
        EvidenceLinks ?? [];

    public LedgerDimensionSetDto Dimensions { get; init; } =
        Dimensions ?? new LedgerDimensionSetDto(BookId: LedgerBookId?.ToString("D"));
}

public sealed record FinancialStatementPackageDto(
    string PackageId,
    string FundProfileId,
    Guid? LedgerBookId,
    string PeriodId,
    AccountingCertificationStateDto CertificationState,
    IReadOnlyList<string> StatementIds,
    IReadOnlyList<string> EvidenceLinks,
    ReportCertificationDto? Certification = null,
    RestatementWorkflowDto? Restatement = null,
    IReadOnlyList<ReportLineProvenanceDto>? LineProvenance = null,
    LedgerDimensionSetDto? Dimensions = null)
{
    public IReadOnlyList<ReportLineProvenanceDto> LineProvenance { get; init; } =
        LineProvenance ?? [];

    public LedgerDimensionSetDto Dimensions { get; init; } =
        Dimensions ?? new LedgerDimensionSetDto(FundId: FundProfileId, BookId: LedgerBookId?.ToString("D"));
}

public sealed record InvestorCapitalStatementDto(
    string StatementId,
    string FundProfileId,
    Guid? LedgerBookId,
    string CapitalAccountId,
    string? InvestorId,
    string PeriodId,
    LedgerDimensionSetDto Dimensions,
    decimal BeginningCapital,
    decimal Contributions,
    decimal Distributions,
    decimal RealizedGainLoss,
    decimal EndingCapital,
    string Currency,
    AccountingCertificationStateDto CertificationState,
    IReadOnlyList<string> EvidenceLinks);

public sealed record RealizedGainLossReportDto(
    string ReportId,
    string FundProfileId,
    Guid? LedgerBookId,
    string PeriodId,
    LedgerDimensionSetDto Dimensions,
    decimal RealizedGainLoss,
    string Currency,
    AccountingCertificationStateDto CertificationState,
    IReadOnlyList<string> EvidenceLinks);

public sealed record NavPackageDto(
    string PackageId,
    string FundProfileId,
    Guid? LedgerBookId,
    string PeriodId,
    LedgerDimensionSetDto Dimensions,
    decimal Nav,
    string Currency,
    AccountingCertificationStateDto CertificationState,
    IReadOnlyList<string> EvidenceLinks,
    ReportCertificationDto? Certification = null,
    RestatementWorkflowDto? Restatement = null);

public sealed record AccountingReportPackageRequestDto(
    string FundProfileId,
    string PeriodId,
    string Actor,
    Guid? LedgerBookId = null,
    Guid? CloseWorkflowId = null,
    string? CapitalAccountId = null,
    string? InvestorId = null,
    decimal BeginningCapital = 0m,
    decimal Contributions = 0m,
    decimal Distributions = 0m,
    decimal RealizedGainLoss = 0m,
    decimal Nav = 0m,
    string Currency = "USD",
    string? RestatementReasonCode = null,
    string? PriorPackageId = null,
    IReadOnlyList<string>? EvidenceLinks = null,
    string? CorrelationId = null,
    LedgerDimensionSetDto? Dimensions = null)
{
    public IReadOnlyList<string> EvidenceLinks { get; init; } =
        EvidenceLinks ?? [];
}

public sealed record CertifyAccountingReportPackageRequestDto(
    string PackageId,
    string Actor,
    string Notes,
    IReadOnlyList<string>? EvidenceLinks = null,
    string? CorrelationId = null,
    OperationsActionOriginDto ActionOrigin = OperationsActionOriginDto.HumanOperator)
{
    public IReadOnlyList<string> EvidenceLinks { get; init; } =
        EvidenceLinks ?? [];
}

public sealed record AccountingReportPackageBundleDto(
    FinancialStatementPackageDto FinancialStatements,
    IReadOnlyList<InvestorCapitalStatementDto> InvestorCapitalStatements,
    RealizedGainLossReportDto RealizedGainLoss,
    NavPackageDto NavPackage,
    ReportCertificationDto Certification,
    IReadOnlyList<AccountingConfigurationValidationIssueDto>? ValidationIssues = null,
    IReadOnlyList<ReportExportArtifactDto>? ExportArtifacts = null,
    Guid? CloseWorkflowId = null)
{
    public IReadOnlyList<AccountingConfigurationValidationIssueDto> ValidationIssues { get; init; } =
        ValidationIssues ?? [];

    public IReadOnlyList<ReportExportArtifactDto> ExportArtifacts { get; init; } =
        ExportArtifacts ?? [];
}

public sealed record ManualJournalEntryWorkbenchDto(
    string FundProfileId,
    Guid? LedgerBookId,
    DateTimeOffset LoadedAtUtc,
    IReadOnlyList<LedgerBookDto> LedgerBooks,
    IReadOnlyList<ChartOfAccountsNodeDto> ChartOfAccounts,
    IReadOnlyList<ManualJournalEntryDraftDto> Drafts,
    IReadOnlyList<AccountingActionAuditEventDto> AuditTrail,
    PrivateCapitalActivityProjectionDto? PrivateCapitalActivity = null);

public sealed record SaveManualJournalEntryDraftRequest(
    ManualJournalEntryDraftDto Draft,
    string Actor,
    string? CorrelationId = null,
    IReadOnlyList<string>? EvidenceLinks = null,
    bool PeriodIsLocked = false,
    Guid? LedgerBookId = null);

public sealed record ValidateManualJournalEntryDraftRequest(
    ManualJournalEntryDraftDto Draft,
    string Actor,
    string? CorrelationId = null,
    bool PeriodIsLocked = false,
    Guid? LedgerBookId = null);

public sealed record SubmitManualJournalEntryApprovalRequest(
    Guid JournalEntryId,
    string FundProfileId,
    string Actor,
    int Version,
    string? Notes = null,
    string? CorrelationId = null,
    IReadOnlyList<string>? EvidenceLinks = null,
    OperationsActionOriginDto ActionOrigin = OperationsActionOriginDto.HumanOperator,
    bool PeriodIsLocked = false,
    Guid? LedgerBookId = null);

public sealed record AttachManualJournalEntryEvidenceRequest(
    Guid JournalEntryId,
    string FundProfileId,
    string Actor,
    int Version,
    ManualJournalEntryEvidenceAttachmentDto Attachment,
    string? CorrelationId = null,
    IReadOnlyList<string>? EvidenceLinks = null,
    OperationsActionOriginDto ActionOrigin = OperationsActionOriginDto.HumanOperator,
    bool PeriodIsLocked = false,
    Guid? LedgerBookId = null);

public sealed record ActivateAccountingConfigurationRequest(
    string FundProfileId,
    string Actor,
    Guid? LedgerBookId = null,
    string? CorrelationId = null,
    IReadOnlyList<string>? EvidenceLinks = null,
    string? CompanyId = null,
    IReadOnlyList<string>? ReportGroupPrincipalIds = null,
    OperationsActionOriginDto ActionOrigin = OperationsActionOriginDto.HumanOperator);

public interface IAccountingConfigurationService
{
    Task<AccountingConfigurationWorkspaceDto> GetWorkspaceAsync(
        string? fundProfileId = null,
        Guid? ledgerBookId = null,
        CancellationToken ct = default);

    Task<AccountingConfigurationWorkspaceDto> UpsertChartNodeAsync(
        UpsertChartOfAccountsNodeRequest request,
        CancellationToken ct = default);

    Task<AccountingConfigurationWorkspaceDto> UpsertTemplateAsync(
        UpsertJournalEntryTemplateRequest request,
        CancellationToken ct = default);

    Task<AccountingConfigurationWorkspaceDto> UpsertPostingRuleAsync(
        UpsertPostingRuleRequest request,
        CancellationToken ct = default);

    Task<AccountingConfigurationWorkspaceDto> ApprovePostingRulePromotionAsync(
        ApprovePostingRulePromotionRequest request,
        CancellationToken ct = default);

    Task<AccountingConfigurationWorkspaceDto> UpsertRuleTestCaseAsync(
        UpsertAccountingRuleTestCaseRequest request,
        CancellationToken ct = default);

    Task<AccountingJournalTemplatePreviewDto> PreviewTemplateAsync(
        PreviewJournalTemplateRequest request,
        CancellationToken ct = default);

    Task<RuleDryRunResultDto> DryRunPostingRuleAsync(
        RuleDryRunRequestDto request,
        CancellationToken ct = default);

    Task<AccountingRuleTestSuiteResultDto> ExecuteRuleTestCasesAsync(
        ExecuteAccountingRuleTestCasesRequestDto request,
        CancellationToken ct = default);

    Task<AccountingConfigurationWorkspaceDto> ActivateAsync(
        ActivateAccountingConfigurationRequest request,
        CancellationToken ct = default);

    Task<IReadOnlyList<AccountingActionAuditEventDto>> ListAuditAsync(
        string? fundProfileId = null,
        Guid? ledgerBookId = null,
        CancellationToken ct = default);
}

public interface IManualJournalEntryLifecycleService
{
    Task<JournalEntryLifecycleActionResultDto> ApplyLifecycleActionAsync(
        JournalEntryLifecycleActionRequestDto request,
        CancellationToken ct = default);
}

public interface IManualJournalEntryWorkbenchService
{
    Task<IReadOnlyList<string>> ListFundProfileIdsAsync(CancellationToken ct = default);

    Task<ManualJournalEntryWorkbenchDto> GetWorkbenchAsync(
        string? fundProfileId = null,
        Guid? ledgerBookId = null,
        CancellationToken ct = default);

    Task<PrivateCapitalActivityProjectionDto> GetPrivateCapitalActivityAsync(
        string? fundProfileId = null,
        Guid? ledgerBookId = null,
        CancellationToken ct = default);

    Task<ManualJournalEntryDraftDto> SaveDraftAsync(
        SaveManualJournalEntryDraftRequest request,
        CancellationToken ct = default);

    Task<ManualJournalEntryDraftDto> ValidateDraftAsync(
        ValidateManualJournalEntryDraftRequest request,
        CancellationToken ct = default);

    Task<ManualJournalEntryDraftDto> SubmitApprovalAsync(
        SubmitManualJournalEntryApprovalRequest request,
        CancellationToken ct = default);

    Task<ManualJournalEntryDraftDto> AttachEvidenceAsync(
        AttachManualJournalEntryEvidenceRequest request,
        CancellationToken ct = default)
    {
        return Task.FromException<ManualJournalEntryDraftDto>(
            new NotSupportedException("Manual journal evidence attachment is not available for this workbench."));
    }
}

public interface ICapitalAccountWorkbenchService
{
    Task<CapitalAccountWorkbenchDto> GetWorkbenchAsync(
        string? fundProfileId = null,
        Guid? ledgerBookId = null,
        string? fundEventId = null,
        string? capitalAccountId = null,
        string? investorId = null,
        string? currency = null,
        CancellationToken ct = default);
}

public interface IPrivateCapitalFundEventCommandCenterService
{
    Task<PrivateCapitalFundEventCommandCenterDto?> GetCommandCenterAsync(
        string? fundProfileId,
        Guid? ledgerBookId,
        string fundEventId,
        CancellationToken ct = default);
}

public interface IManualJournalEntryDraftStore
{
    Task<IReadOnlyList<string>> ListFundProfileIdsAsync(CancellationToken ct = default);

    Task<IReadOnlyList<ManualJournalEntryDraftDto>> ListAsync(
        string fundProfileId,
        Guid? ledgerBookId = null,
        CancellationToken ct = default);

    Task<ManualJournalEntryDraftDto?> GetAsync(
        string fundProfileId,
        Guid journalEntryId,
        CancellationToken ct = default);

    Task SaveAsync(ManualJournalEntryDraftDto draft, CancellationToken ct = default);
}

public interface IAccountingConfigurationStore
{
    Task<AccountingConfigurationWorkspaceDto?> GetAsync(
        string fundProfileId,
        Guid? ledgerBookId = null,
        CancellationToken ct = default);

    Task SaveAsync(AccountingConfigurationWorkspaceDto workspace, CancellationToken ct = default);
}

public interface IAccountingActionAuditStore
{
    Task AppendAsync(AccountingActionAuditEventDto auditEvent, CancellationToken ct = default);

    Task<IReadOnlyList<AccountingActionAuditEventDto>> ListAsync(
        string? fundProfileId = null,
        Guid? ledgerBookId = null,
        CancellationToken ct = default);
}
