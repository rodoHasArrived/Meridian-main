using System.Text.Json.Serialization;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Workstation;

namespace Meridian.Contracts.Ledger;

[JsonConverter(typeof(JsonStringEnumConverter<AccountingBasisKindDto>))]
public enum AccountingBasisKindDto
{
    Primary = 0,
    Gaap = 1,
    Cash = 2,
    Tax = 3,
    Statutory = 4
}

[JsonConverter(typeof(JsonStringEnumConverter<LedgerPeriodStatusDto>))]
public enum LedgerPeriodStatusDto
{
    Open = 0,
    SoftClosed = 1,
    HardClosed = 2
}

[JsonConverter(typeof(JsonStringEnumConverter<LedgerPeriodCloseKindDto>))]
public enum LedgerPeriodCloseKindDto
{
    SoftClose = 0,
    HardClose = 1
}

[JsonConverter(typeof(JsonStringEnumConverter<LedgerPeriodSignoffStatusDto>))]
public enum LedgerPeriodSignoffStatusDto
{
    NotRequired = 0,
    Pending = 1,
    SignedOff = 2,
    Rejected = 3
}

[JsonConverter(typeof(JsonStringEnumConverter<LedgerPostingKindDto>))]
public enum LedgerPostingKindDto
{
    Originating = 0,
    Adjustment = 1
}

[JsonConverter(typeof(JsonStringEnumConverter<LedgerAdjustmentApprovalStatusDto>))]
public enum LedgerAdjustmentApprovalStatusDto
{
    Pending = 0,
    Approved = 1,
    Rejected = 2,
    Superseded = 3
}

public sealed record LedgerAdjustmentApprovalMetadataDto(
    string ApprovalId,
    LedgerAdjustmentApprovalStatusDto Status,
    string ApprovedBy,
    DateTimeOffset ApprovedAt,
    string ReasonCode,
    string? GovernanceCaseId = null,
    string? EvidenceLink = null,
    string? Notes = null);

public sealed record CreateAccountingPolicyRequest(
    AccountingBasisKindDto AccountingBasis,
    string PolicyId,
    string Version,
    string DisplayName,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo = null,
    bool IsDefault = false,
    string RulesJson = "{}",
    string? FundProfileId = null,
    Guid? FundStructureNodeId = null,
    string? InstrumentId = null,
    Guid? SourceEventId = null);

public sealed record AccountingPolicyDto(
    string PolicyId,
    AccountingBasisKindDto AccountingBasis,
    string Version,
    string DisplayName,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    bool IsDefault,
    string RulesJson,
    DateTimeOffset CreatedAt,
    string? FundProfileId = null,
    Guid? FundStructureNodeId = null,
    string? InstrumentId = null,
    Guid? SourceEventId = null);

public sealed record AccountingPolicyQuery(
    AccountingBasisKindDto AccountingBasis = AccountingBasisKindDto.Primary,
    DateOnly? EffectiveDate = null,
    string? PolicyId = null,
    string? FundProfileId = null,
    Guid? FundStructureNodeId = null,
    string? InstrumentId = null,
    Guid? SourceEventId = null);

public sealed record CreateLedgerBookRequest(
    string FundProfileId,
    Guid FundStructureNodeId,
    FundStructureNodeKindDto FundStructureNodeKind,
    string DisplayName,
    string BaseCurrency,
    string? Description = null,
    AccountingBasisKindDto AccountingBasis = AccountingBasisKindDto.Primary,
    string AccountingPolicyId = "legacy-v1",
    string AccountingPolicyVersion = "legacy-v1");

public sealed record LedgerBookDto(
    Guid LedgerBookId,
    string FundProfileId,
    Guid FundStructureNodeId,
    FundStructureNodeKindDto FundStructureNodeKind,
    string DisplayName,
    string BaseCurrency,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? Description = null,
    AccountingBasisKindDto AccountingBasis = AccountingBasisKindDto.Primary,
    string AccountingPolicyId = "legacy-v1",
    string AccountingPolicyVersion = "legacy-v1");

public sealed record LedgerBookQuery(
    string? FundProfileId = null,
    Guid? FundStructureNodeId = null,
    FundStructureNodeKindDto? FundStructureNodeKind = null,
    AccountingBasisKindDto? AccountingBasis = null);

public sealed record CreateLedgerPeriodRequest(
    Guid LedgerBookId,
    int FiscalYear,
    int PeriodNo,
    string Label,
    DateOnly StartDate,
    DateOnly EndDate);

public sealed record LedgerPeriodDto(
    Guid PeriodId,
    Guid LedgerBookId,
    int FiscalYear,
    int PeriodNo,
    string Label,
    DateOnly StartDate,
    DateOnly EndDate,
    LedgerPeriodStatusDto Status,
    DateTimeOffset OpenedAt,
    DateTimeOffset? ClosedAt,
    long Version,
    AccountingBasisKindDto AccountingBasis = AccountingBasisKindDto.Primary,
    string AccountingPolicyId = "legacy-v1",
    string AccountingPolicyVersion = "legacy-v1");

public sealed record LedgerPeriodQuery(
    Guid? LedgerBookId = null,
    string? FundProfileId = null,
    Guid? FundStructureNodeId = null,
    LedgerPeriodStatusDto? Status = null,
    bool OpenOnly = false,
    AccountingBasisKindDto? AccountingBasis = null);

public sealed record CloseLedgerPeriodRequest(
    LedgerPeriodCloseKindDto CloseKind,
    string ClosedBy,
    string? Notes = null,
    string RequiredSignoffRole = "Fund Controller",
    string ToleranceProfileId = "standard-recon-tolerance");

public sealed record LedgerPeriodTrialBalanceLineDto(
    string AccountName,
    string AccountType,
    string? Symbol,
    string? FinancialAccountId,
    decimal DebitTotal,
    decimal CreditTotal,
    decimal Balance,
    int EntryCount,
    AccountingBasisKindDto AccountingBasis = AccountingBasisKindDto.Primary,
    string AccountingPolicyId = "legacy-v1",
    string AccountingPolicyVersion = "legacy-v1",
    string? RuleId = null,
    string? RuleVersion = null,
    string? SourceEventId = null,
    Guid? SourceJournalEntryId = null);

public sealed record LedgerPeriodSummaryDto(
    Guid PeriodId,
    Guid LedgerBookId,
    int FiscalYear,
    int PeriodNo,
    string Label,
    IReadOnlyList<LedgerPeriodTrialBalanceLineDto> TrialBalance,
    decimal TotalDebits,
    decimal TotalCredits,
    decimal NetIncome,
    decimal? PeriodOnPeriodVariance,
    int OpenBreakCount,
    LedgerPeriodSignoffStatusDto SignoffStatus,
    DateTimeOffset CompletedAt,
    AccountingBasisKindDto AccountingBasis = AccountingBasisKindDto.Primary,
    string AccountingPolicyId = "legacy-v1",
    string AccountingPolicyVersion = "legacy-v1");

public sealed record LedgerPeriodPnlSummaryDto(
    Guid PeriodId,
    Guid LedgerBookId,
    int FiscalYear,
    int PeriodNo,
    string Label,
    decimal TotalRevenue,
    decimal TotalExpenses,
    decimal NetIncome,
    decimal? PeriodOnPeriodVariance,
    int OpenBreakCount,
    LedgerPeriodSignoffStatusDto SignoffStatus,
    DateTimeOffset CompletedAt,
    IReadOnlyList<LedgerPeriodTrialBalanceLineDto> RevenueLines,
    IReadOnlyList<LedgerPeriodTrialBalanceLineDto> ExpenseLines,
    AccountingBasisKindDto AccountingBasis = AccountingBasisKindDto.Primary,
    string AccountingPolicyId = "legacy-v1",
    string AccountingPolicyVersion = "legacy-v1");

public sealed record LedgerPeriodCloseResultDto(
    LedgerPeriodDto Period,
    LedgerPeriodSummaryDto Summary,
    OperatorWorkItemDto WorkItem);

public interface ILedgerBookService
{
    Task<LedgerBookDto> CreateBookAsync(CreateLedgerBookRequest request, CancellationToken ct = default);

    Task<LedgerBookDto?> GetBookAsync(Guid ledgerBookId, CancellationToken ct = default);

    Task<IReadOnlyList<LedgerBookDto>> ListBooksAsync(LedgerBookQuery query, CancellationToken ct = default);

    Task<LedgerPeriodDto> CreatePeriodAsync(CreateLedgerPeriodRequest request, CancellationToken ct = default);

    Task<IReadOnlyList<LedgerPeriodDto>> ListPeriodsAsync(LedgerPeriodQuery query, CancellationToken ct = default);

    Task<IReadOnlyList<LedgerPeriodDto>> ListOpenPeriodsAsync(Guid? ledgerBookId = null, CancellationToken ct = default);

    Task<LedgerPeriodSummaryDto?> GetPeriodSummaryAsync(Guid periodId, CancellationToken ct = default);

    Task<LedgerPeriodCloseResultDto> ClosePeriodAsync(
        Guid periodId,
        CloseLedgerPeriodRequest request,
        CancellationToken ct = default);
}
