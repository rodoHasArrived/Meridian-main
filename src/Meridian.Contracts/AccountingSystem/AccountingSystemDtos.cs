using System.Text.Json.Serialization;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;

namespace Meridian.Contracts.AccountingSystem;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AccountingSystemProviderStateDto
{
    Available,
    Planned,
    Disabled
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AccountingSystemImportStateDto
{
    NotStarted,
    Previewed,
    Imported,
    Failed
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AccountingSystemReconciliationStatusDto
{
    Matched,
    Variance,
    MissingExternal,
    MissingMeridian,
    ReviewRequired
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AccountingSystemEvidencePackageStatusDto
{
    Ready,
    ReviewRequired,
    Missing
}

public sealed record AccountingSystemProviderDto(
    string ProviderId,
    string DisplayName,
    AccountingSystemProviderStateDto State,
    bool RequiresCredentials,
    bool SupportsChartOfAccounts,
    bool SupportsJournalEntries,
    bool SupportsTrialBalance,
    bool SupportsPosting,
    string StatusLabel,
    string StatusDetail,
    IReadOnlyList<string> EvidenceKinds,
    AccountingSystemConnectionMetadataDto? Connection = null);

public sealed record AccountingSystemConnectionMetadataDto(
    string ProviderId,
    string? Environment,
    string? CompanyId,
    string? CompanyName,
    bool HasLocalConfig,
    bool HasRefreshToken,
    DateTimeOffset? LastConnectedAtUtc,
    string StatusLabel,
    string StatusDetail,
    IReadOnlyList<string> MissingFields);

public sealed record AccountingSystemOAuthStartRequestDto(
    string? ClientId = null,
    string? ClientSecret = null,
    string? RedirectUri = null,
    string? Environment = null,
    string? CompanyName = null,
    string? RequestedBy = null);

public sealed record AccountingSystemOAuthStartResultDto(
    string ProviderId,
    bool Success,
    string? AuthorizationUrl,
    string? State,
    string Environment,
    string RedirectUri,
    string? LastError,
    IReadOnlyList<string> Warnings);

public sealed record AccountingSystemOAuthCallbackResultDto(
    string ProviderId,
    bool Success,
    string? CompanyId,
    string? CompanyName,
    DateTimeOffset CompletedAtUtc,
    string? LastError,
    IReadOnlyList<string> Warnings);

public sealed record AccountingSystemImportRequestDto(
    string? ProviderId = null,
    string? FundProfileId = null,
    Guid? LedgerBookId = null,
    DateOnly? PeriodStart = null,
    DateOnly? PeriodEnd = null,
    bool PersistPreview = true);

public sealed record AccountingSystemImportSummaryDto(
    string ImportId,
    string ProviderId,
    string ProviderDisplayName,
    string FundProfileId,
    Guid? LedgerBookId,
    AccountingSystemImportStateDto State,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    DateTimeOffset ImportedAtUtc,
    int ChartAccountCount,
    int JournalEntryCount,
    int TrialBalanceLineCount,
    IReadOnlyList<string> EvidenceReferences,
    IReadOnlyList<string> Warnings);

public sealed record AccountingSystemChartAccountDto(
    string ExternalAccountId,
    string AccountCode,
    string DisplayName,
    string AccountType,
    string Currency,
    bool IsActive,
    string? ParentExternalAccountId = null,
    string? EvidenceRef = null);

public sealed record AccountingSystemJournalEntryDto(
    string ExternalJournalEntryId,
    DateOnly AccountingDate,
    string Description,
    string Currency,
    decimal TotalDebits,
    decimal TotalCredits,
    IReadOnlyList<AccountingSystemJournalLineDto> Lines,
    string? EvidenceRef = null);

public sealed record AccountingSystemJournalLineDto(
    string ExternalLineId,
    string ExternalAccountId,
    string AccountCode,
    string Description,
    decimal Debit,
    decimal Credit,
    string Currency,
    string? EvidenceRef = null);

public sealed record AccountingSystemTrialBalanceLineDto(
    string ExternalAccountId,
    string AccountCode,
    string AccountName,
    string AccountType,
    decimal Debit,
    decimal Credit,
    string Currency,
    DateOnly AsOfDate,
    string? EvidenceRef = null);

public sealed record AccountingSystemImportDetailDto(
    AccountingSystemImportSummaryDto Summary,
    IReadOnlyList<AccountingSystemChartAccountDto> ChartAccounts,
    IReadOnlyList<AccountingSystemJournalEntryDto> JournalEntries,
    IReadOnlyList<AccountingSystemTrialBalanceLineDto> TrialBalance);

public sealed record AccountingSystemReconciliationEvidencePackageDto(
    string PackageId,
    string Label,
    AccountingSystemEvidencePackageStatusDto Status,
    int EvidenceReferenceCount,
    IReadOnlyList<string> EvidenceReferences,
    IReadOnlyList<string> RequiredActions);

public sealed record AccountingSystemReconciliationSummaryDto(
    string ReconciliationId,
    string ImportId,
    string ProviderId,
    string FundProfileId,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    DateTimeOffset GeneratedAtUtc,
    int MatchedCount,
    int BreakCount,
    decimal TotalExternalDebits,
    decimal TotalExternalCredits,
    decimal TotalMeridianDebits,
    decimal TotalMeridianCredits,
    bool PostingEnabled,
    string PostingDisabledReason,
    IReadOnlyList<AccountingSystemReconciliationRowDto> Rows,
    IReadOnlyList<string> EvidenceReferences,
    Guid? LedgerBookId = null)
{
    public IReadOnlyList<AccountingSystemReconciliationEvidencePackageDto> EvidencePackages { get; init; } = [];
}

public sealed record AccountingSystemReconciliationRowDto(
    string RowId,
    string AccountCode,
    string AccountName,
    string Currency,
    AccountingSystemReconciliationStatusDto Status,
    decimal ExternalDebit,
    decimal ExternalCredit,
    decimal MeridianDebit,
    decimal MeridianCredit,
    decimal Variance,
    string Detail,
    string? EvidenceRef = null)
{
    public IReadOnlyList<string> ExternalEvidenceReferences { get; init; } = [];

    public IReadOnlyList<string> MeridianEvidenceReferences { get; init; } = [];

    public IReadOnlyList<string> EvidenceReferences { get; init; } = [];
}

public sealed record AccountingSystemMappingProfileUpsertRequestDto(
    ExternalGlMappingProfileDto Profile,
    string Actor,
    string? ProviderId = null,
    string? FundProfileId = null,
    Guid? LedgerBookId = null,
    string? CorrelationId = null,
    IReadOnlyList<string>? EvidenceLinks = null)
{
    public IReadOnlyList<string> EvidenceLinks { get; init; } =
        EvidenceLinks ?? [];
}

public sealed record AccountingSystemExportPackageRequestDto(
    string Actor,
    string? ProviderId = null,
    string? FundProfileId = null,
    Guid? LedgerBookId = null,
    DateOnly? PeriodStart = null,
    DateOnly? PeriodEnd = null,
    string? MappingProfileId = null,
    IReadOnlyList<Guid>? JournalEntryIds = null,
    bool RequireBalancedReconciliation = true,
    IReadOnlyList<string>? EvidenceLinks = null,
    string? CorrelationId = null)
{
    public IReadOnlyList<Guid> JournalEntryIds { get; init; } =
        JournalEntryIds ?? [];

    public IReadOnlyList<string> EvidenceLinks { get; init; } =
        EvidenceLinks ?? [];
}

public sealed record CertifyAccountingSystemExportPackageRequestDto(
    string ExportPackageId,
    string Actor,
    string Notes,
    IReadOnlyList<string>? EvidenceLinks = null,
    string? CorrelationId = null,
    OperationsActionOriginDto ActionOrigin = OperationsActionOriginDto.HumanOperator)
{
    public IReadOnlyList<string> EvidenceLinks { get; init; } =
        EvidenceLinks ?? [];
}
