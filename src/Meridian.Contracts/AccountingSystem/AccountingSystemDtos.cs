using System.Text.Json.Serialization;

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
    IReadOnlyList<string> EvidenceKinds);

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
    IReadOnlyList<string> EvidenceReferences);

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
    string? EvidenceRef = null);
