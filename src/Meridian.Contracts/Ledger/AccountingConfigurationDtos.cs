using System.Text.Json.Serialization;

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
    IReadOnlyList<string> EvidenceLinks);

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
    IReadOnlyList<string>? EvidenceLinks = null);

public sealed record UpsertJournalEntryTemplateRequest(
    string FundProfileId,
    JournalEntryTemplateDto Template,
    string Actor,
    string? CorrelationId = null,
    IReadOnlyList<string>? EvidenceLinks = null);

public sealed record UpsertPostingRuleRequest(
    string FundProfileId,
    PostingRuleDto Rule,
    string Actor,
    string? CorrelationId = null,
    IReadOnlyList<string>? EvidenceLinks = null);

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
    int ReportLineProvenanceCount = 0);

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
    IReadOnlyList<AccountingConfigurationValidationIssueDto> ValidationIssues);

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
    IReadOnlyList<AccountingConfigurationValidationIssueDto> ValidationIssues);

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
    IReadOnlyList<PrivateCapitalCapitalAccountSubledgerDto>? CapitalAccountSubledgers = null)
{
    public IReadOnlyList<PrivateCapitalFundEventLedgerRecordDto> FundEventRecords { get; init; } =
        FundEventRecords ?? [];

    public IReadOnlyList<PrivateCapitalCapitalAccountSubledgerDto> CapitalAccountSubledgers { get; init; } =
        CapitalAccountSubledgers ?? [];
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
    IReadOnlyList<string>? EvidenceLinks = null);

public sealed record ActivateAccountingConfigurationRequest(
    string FundProfileId,
    string Actor,
    Guid? LedgerBookId = null,
    string? CorrelationId = null,
    IReadOnlyList<string>? EvidenceLinks = null);

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

public interface IManualJournalEntryDraftStore
{
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
