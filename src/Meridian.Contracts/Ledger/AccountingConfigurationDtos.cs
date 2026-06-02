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
