using System.Text.Json.Serialization;
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
    Rejected = 4
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
    FormationClosing = 15,
    SubscriptionPacket = 16,
    ContributionReceipt = 17,
    Investment = 18,
    Valuation = 19,
    FeeExpense = 20,
    TaxRequest = 21,
    AuditRequest = 22,
    WindDownSupport = 23
}

[JsonConverter(typeof(JsonStringEnumConverter<PrivateCapitalFundEventKindDto>))]
public enum PrivateCapitalFundEventKindDto
{
    FormationClosing = 0,
    SubscriptionPacket = 1,
    CapitalCall = 2,
    ContributionReceipt = 3,
    Investment = 4,
    Distribution = 5,
    Valuation = 6,
    FeeExpense = 7,
    TaxRequest = 8,
    AuditRequest = 9,
    WindDownSupport = 10,
    Other = 99
}

[JsonConverter(typeof(JsonStringEnumConverter<PrivateCapitalGovernedPackageKindDto>))]
public enum PrivateCapitalGovernedPackageKindDto
{
    CapitalNotice = 0,
    DistributionNotice = 1,
    Statement = 2,
    TaxSupport = 3,
    AuditSupport = 4,
    RecipientList = 5,
    DeliveryLog = 6,
    AmendmentRestatementTrail = 7,
    OperationalEvidence = 8
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

public sealed record PostingRuleDto(
    string RuleId,
    string DisplayName,
    string SourceEventType,
    string TemplateId,
    string RuleVersion = "v1",
    bool IsArchived = false,
    string? Description = null);

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
    IReadOnlyList<AccountingActionAuditEventDto> AuditTrail);

public sealed record UpsertChartOfAccountsNodeRequest(
    string FundProfileId,
    ChartOfAccountsNodeDto Node,
    string Actor,
    string? CorrelationId = null,
    IReadOnlyList<string>? EvidenceLinks = null,
    string? CompanyId = null,
    IReadOnlyList<string>? ReportGroupPrincipalIds = null);

public sealed record UpsertJournalEntryTemplateRequest(
    string FundProfileId,
    JournalEntryTemplateDto Template,
    string Actor,
    string? CorrelationId = null,
    IReadOnlyList<string>? EvidenceLinks = null,
    string? CompanyId = null,
    IReadOnlyList<string>? ReportGroupPrincipalIds = null);

public sealed record UpsertPostingRuleRequest(
    string FundProfileId,
    PostingRuleDto Rule,
    string Actor,
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
    string? EvidenceLink = null);

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
    TreasuryLedgerContextDto? TreasuryContext = null);


public sealed record PrivateCapitalOperationalRecordLinkageDto(
    PrivateCapitalFundEventKindDto EventKind,
    string NormalizedRecordStatus,
    string ReconciliationPosture,
    string LedgerImpactStatus,
    string ApprovalStatus,
    string ReportUsageStatus,
    string DeliveryEvidenceStatus,
    string AuditHistoryStatus,
    int SourceEvidenceLinkCount,
    int NormalizedRecordCount,
    int ReconciliationLinkCount,
    int LedgerImpactCount,
    int ApprovalEvidenceCount,
    int ReportUsageCount,
    int DeliveryEvidenceCount,
    int AuditHistoryCount,
    IReadOnlyList<string>? EvidenceLinks = null,
    IReadOnlyList<string>? RequiredActions = null)
{
    public IReadOnlyList<string> EvidenceLinks { get; init; } =
        EvidenceLinks ?? [];

    public IReadOnlyList<string> RequiredActions { get; init; } =
        RequiredActions ?? [];
}

public sealed record PrivateCapitalCapitalAccountProjectionDto(
    string ProjectionId,
    string CapitalAccountId,
    string? InvestorId,
    string Currency,
    decimal Commitment,
    decimal Contributions,
    decimal Distributions,
    decimal Allocations,
    decimal Nav,
    int StatementCount,
    int EvidenceLineageCount,
    string StatementStatus,
    string EvidenceLineageStatus,
    string? StatementRoute,
    string? EvidenceRoute,
    IReadOnlyList<string>? EvidenceLinks = null)
{
    public IReadOnlyList<string> EvidenceLinks { get; init; } =
        EvidenceLinks ?? [];
}

public sealed record PrivateCapitalGovernedPackageDto(
    string PackageId,
    PrivateCapitalGovernedPackageKindDto PackageKind,
    string Label,
    string Status,
    string? Route,
    int RecipientCount,
    int DeliveryLogCount,
    int AmendmentRestatementTrailCount,
    int EvidenceLinkCount,
    IReadOnlyList<string> EvidenceLinks,
    IReadOnlyList<string>? RecipientList = null,
    IReadOnlyList<string>? DeliveryLogs = null,
    IReadOnlyList<string>? AmendmentRestatementTrail = null,
    IReadOnlyList<string>? RequiredActions = null)
{
    public IReadOnlyList<string> RecipientList { get; init; } =
        RecipientList ?? [];

    public IReadOnlyList<string> DeliveryLogs { get; init; } =
        DeliveryLogs ?? [];

    public IReadOnlyList<string> AmendmentRestatementTrail { get; init; } =
        AmendmentRestatementTrail ?? [];

    public IReadOnlyList<string> RequiredActions { get; init; } =
        RequiredActions ?? [];
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
    PrivateCapitalPaymentIntentEvidenceDto? PaymentIntentEvidence = null,
    PrivateCapitalOperationalRecordLinkageDto? OperationalRecord = null)
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
    IReadOnlyList<string>? RequiredActions = null,
    PrivateCapitalGovernedPackageKindDto PackageKind = PrivateCapitalGovernedPackageKindDto.OperationalEvidence,
    IReadOnlyList<string>? RecipientList = null,
    IReadOnlyList<string>? DeliveryLogs = null,
    IReadOnlyList<string>? AmendmentRestatementTrail = null)
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
    IReadOnlyList<string>? RequiredActions = null,
    PrivateCapitalGovernedPackageKindDto PackageKind = PrivateCapitalGovernedPackageKindDto.OperationalEvidence,
    IReadOnlyList<string>? RecipientList = null,
    IReadOnlyList<string>? DeliveryLogs = null,
    IReadOnlyList<string>? AmendmentRestatementTrail = null)
{
    public IReadOnlyList<string> RequiredActions { get; init; } =
        RequiredActions ?? [];

    public IReadOnlyList<string> RecipientList { get; init; } =
        RecipientList ?? [];

    public IReadOnlyList<string> DeliveryLogs { get; init; } =
        DeliveryLogs ?? [];

    public IReadOnlyList<string> AmendmentRestatementTrail { get; init; } =
        AmendmentRestatementTrail ?? [];
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
    IReadOnlyList<PrivateCapitalCapitalAccountProjectionDto>? CapitalAccountProjections = null,
    IReadOnlyList<PrivateCapitalGovernedPackageDto>? GovernedPackages = null,
    IReadOnlyList<string>? LiveCapabilities = null,
    IReadOnlyList<string>? PlannedCapabilities = null)
{
    public IReadOnlyList<PrivateCapitalCapitalAccountProjectionDto> CapitalAccountProjections { get; init; } =
        CapitalAccountProjections ?? [];

    public IReadOnlyList<PrivateCapitalGovernedPackageDto> GovernedPackages { get; init; } =
        GovernedPackages ?? [];

    public IReadOnlyList<string> LiveCapabilities { get; init; } =
        LiveCapabilities ?? [];

    public IReadOnlyList<string> PlannedCapabilities { get; init; } =
        PlannedCapabilities ?? [];
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
    IReadOnlyList<string>? EvidenceLinks = null);

public sealed record ValidateManualJournalEntryDraftRequest(
    ManualJournalEntryDraftDto Draft,
    string Actor,
    string? CorrelationId = null);

public sealed record SubmitManualJournalEntryApprovalRequest(
    Guid JournalEntryId,
    string FundProfileId,
    string Actor,
    int Version,
    string? Notes = null,
    string? CorrelationId = null,
    IReadOnlyList<string>? EvidenceLinks = null,
    OperationsActionOriginDto ActionOrigin = OperationsActionOriginDto.HumanOperator);

public sealed record ActivateAccountingConfigurationRequest(
    string FundProfileId,
    string Actor,
    Guid? LedgerBookId = null,
    string? CorrelationId = null,
    IReadOnlyList<string>? EvidenceLinks = null,
    string? CompanyId = null,
    IReadOnlyList<string>? ReportGroupPrincipalIds = null);

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

    Task<AccountingJournalTemplatePreviewDto> PreviewTemplateAsync(
        PreviewJournalTemplateRequest request,
        CancellationToken ct = default);

    Task<AccountingConfigurationWorkspaceDto> ActivateAsync(
        ActivateAccountingConfigurationRequest request,
        CancellationToken ct = default);

    Task<IReadOnlyList<AccountingActionAuditEventDto>> ListAuditAsync(
        string? fundProfileId = null,
        Guid? ledgerBookId = null,
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
    Task<AccountingConfigurationWorkspaceDto?> GetAsync(string fundProfileId, CancellationToken ct = default);

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
