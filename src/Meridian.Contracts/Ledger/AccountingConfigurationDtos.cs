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
    Reversal = 8
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
    ManualJournalEntryTypeDto EntryType = ManualJournalEntryTypeDto.General);

public sealed record ManualJournalEntryWorkbenchDto(
    string FundProfileId,
    Guid? LedgerBookId,
    DateTimeOffset LoadedAtUtc,
    IReadOnlyList<LedgerBookDto> LedgerBooks,
    IReadOnlyList<ChartOfAccountsNodeDto> ChartOfAccounts,
    IReadOnlyList<ManualJournalEntryDraftDto> Drafts,
    IReadOnlyList<AccountingActionAuditEventDto> AuditTrail);

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
