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
    Adjustment = 1,

    /// <summary>
    /// Period-close closing entries. Produced only by the governed period-close workflow after
    /// human approval, these are the sanctioned exception to the closed-period posting bar: they
    /// finalize the period being closed by zeroing temporary accounts and rolling net income to
    /// retained earnings, so the posting guard permits them into soft- and hard-closed periods.
    /// </summary>
    ClosingEntry = 2
}

[JsonConverter(typeof(JsonStringEnumConverter<LedgerAdjustmentApprovalStatusDto>))]
public enum LedgerAdjustmentApprovalStatusDto
{
    Pending = 0,
    Approved = 1,
    Rejected = 2,
    Superseded = 3
}

[JsonConverter(typeof(JsonStringEnumConverter<AccountingTreatmentKindDto>))]
public enum AccountingTreatmentKindDto
{
    General = 0,
    Accrual = 1,
    Expense = 2,
    PrepaidExpense = 3,
    Amortization = 4,
    Deferral = 5,
    Reclassification = 6,
    Reversal = 7,
    FxTranslation = 8,
    TaxLotRelief = 9,
    DirectLendingAccrual = 10,
    EquityMethodInvestment = 11,
    Intercompany = 12,
    ConsolidationElimination = 13
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

public sealed record AccountingPolicyRuleDto(
    string RuleId,
    AccountingTreatmentKindDto TreatmentKind,
    string RuleVersion = "v1",
    string? SourceEventType = null,
    string? JournalTemplateId = null,
    bool RequiresEvidence = true,
    bool RequiresApproval = true,
    bool AllowsAutoPosting = false,
    string? Description = null,
    IReadOnlyList<string>? Tags = null);

public sealed record AccountingPolicyRulePackDto(
    string RulePackId,
    string RulePackVersion,
    IReadOnlyList<AccountingPolicyRuleDto> Rules,
    string? Description = null);

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
    Guid? SourceEventId = null,
    AccountingPolicyRulePackDto? RulePack = null);

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
    Guid? SourceEventId = null,
    AccountingPolicyRulePackDto? RulePack = null);

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

[JsonConverter(typeof(JsonStringEnumConverter<LedgerBookRolloutIssueSeverityDto>))]
public enum LedgerBookRolloutIssueSeverityDto
{
    Info = 0,
    Warning = 1,
    Critical = 2
}

public sealed record LedgerBookRequiredScopeDto(
    Guid FundStructureNodeId,
    FundStructureNodeKindDto FundStructureNodeKind,
    AccountingBasisKindDto AccountingBasis = AccountingBasisKindDto.Primary,
    string? DisplayName = null);

public sealed record LedgerBookRolloutAssessmentRequest(
    string? FundProfileId = null,
    Guid? FundStructureNodeId = null,
    FundStructureNodeKindDto? FundStructureNodeKind = null,
    AccountingBasisKindDto? AccountingBasis = null,
    IReadOnlyList<LedgerBookRequiredScopeDto>? RequiredScopes = null)
{
    public IReadOnlyList<LedgerBookRequiredScopeDto> RequiredScopes { get; init; } = RequiredScopes ?? [];
}

public sealed record LedgerBookRolloutBookStatusDto(
    Guid LedgerBookId,
    string FundProfileId,
    Guid FundStructureNodeId,
    FundStructureNodeKindDto FundStructureNodeKind,
    AccountingBasisKindDto AccountingBasis,
    string AccountingPolicyId,
    string AccountingPolicyVersion,
    int PeriodCount,
    int OpenPeriodCount,
    int SoftClosedPeriodCount,
    int HardClosedPeriodCount,
    DateOnly? FirstPeriodStart,
    DateOnly? LastPeriodEnd);

public sealed record LedgerBookRolloutIssueDto(
    string Code,
    LedgerBookRolloutIssueSeverityDto Severity,
    string Message,
    string? Scope = null,
    Guid? LedgerBookId = null,
    Guid? FundStructureNodeId = null,
    AccountingBasisKindDto? AccountingBasis = null);

public sealed record LedgerBookRolloutAssessmentDto(
    DateTimeOffset GeneratedAtUtc,
    string? FundProfileId,
    Guid? FundStructureNodeId,
    FundStructureNodeKindDto? FundStructureNodeKind,
    AccountingBasisKindDto? AccountingBasis,
    IReadOnlyList<LedgerBookRolloutBookStatusDto> Books,
    IReadOnlyList<LedgerBookRolloutIssueDto> Issues)
{
    public IReadOnlyList<LedgerBookRolloutBookStatusDto> Books { get; init; } = Books ?? [];
    public IReadOnlyList<LedgerBookRolloutIssueDto> Issues { get; init; } = Issues ?? [];
    public bool IsReady => Issues.All(static issue => issue.Severity != LedgerBookRolloutIssueSeverityDto.Critical);
    public int CriticalIssueCount => Issues.Count(static issue => issue.Severity == LedgerBookRolloutIssueSeverityDto.Critical);
    public int WarningIssueCount => Issues.Count(static issue => issue.Severity == LedgerBookRolloutIssueSeverityDto.Warning);
    public int BookCount => Books.Count;
    public int OpenPeriodCount => Books.Sum(static book => book.OpenPeriodCount);
}

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
    string ToleranceProfileId = "standard-recon-tolerance",
    OperationsActionOriginDto ActionOrigin = OperationsActionOriginDto.HumanOperator);

public sealed record ReopenLedgerPeriodRequest(
    string ReopenedBy,
    string Role,
    string Reason,
    string ApprovalReference,
    IReadOnlyList<string>? EvidenceLinks = null,
    OperationsActionOriginDto ActionOrigin = OperationsActionOriginDto.HumanOperator)
{
    public IReadOnlyList<string> EvidenceLinks { get; init; } = EvidenceLinks ?? [];
}

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
    Guid? SourceJournalEntryId = null,
    LedgerDimensionSetDto? Dimensions = null);

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

public sealed record LedgerJournalEntryLineDto(
    Guid EntryId,
    Guid JournalEntryId,
    DateTimeOffset Timestamp,
    string AccountName,
    string AccountType,
    string? Symbol,
    string? FinancialAccountId,
    decimal Debit,
    decimal Credit,
    string Description,
    LedgerDimensionSetDto? Dimensions = null);

public sealed record LedgerJournalEntryDto(
    Guid JournalEntryId,
    Guid PeriodId,
    Guid? LedgerBookId,
    Guid AggregateId,
    Guid? CommandId,
    Guid? CorrelationId,
    long GlobalSequence,
    DateTimeOffset CreatedAt,
    DateTimeOffset Timestamp,
    string Description,
    decimal TotalDebits,
    decimal TotalCredits,
    bool IsBalanced,
    IReadOnlyList<LedgerJournalEntryLineDto> Lines,
    AccountingBasisKindDto AccountingBasis = AccountingBasisKindDto.Primary,
    string AccountingPolicyId = "legacy-v1",
    string AccountingPolicyVersion = "legacy-v1",
    string? RuleId = null,
    string? RuleVersion = null,
    Guid? SourceEventId = null,
    Guid? SourceJournalEntryId = null,
    LedgerPostingKindDto PostingKind = LedgerPostingKindDto.Originating,
    LedgerAdjustmentApprovalMetadataDto? AdjustmentApproval = null)
{
    public IReadOnlyList<LedgerJournalEntryLineDto> Lines { get; init; } = Lines ?? [];
}

public sealed record LedgerReportSignatureDto(
    string Algorithm,
    string PayloadChecksumSha256,
    string SignedBy,
    DateTimeOffset SignedAtUtc);

public sealed record LedgerTrialBalanceReportDto(
    Guid PeriodId,
    Guid LedgerBookId,
    int FiscalYear,
    int PeriodNo,
    string Label,
    bool IsPeriodLocked,
    decimal TotalDebits,
    decimal TotalCredits,
    decimal NetIncome,
    decimal? PeriodOnPeriodVariance,
    int OpenBreakCount,
    LedgerPeriodSignoffStatusDto SignoffStatus,
    DateTimeOffset CompletedAt,
    IReadOnlyList<LedgerPeriodTrialBalanceLineDto> Lines,
    LedgerReportSignatureDto Signature,
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
    string AccountingPolicyVersion = "legacy-v1",
    decimal RealizedRevenue = 0m,
    decimal RealizedExpenses = 0m,
    decimal RealizedNetIncome = 0m,
    decimal AccrualAdjustmentRevenue = 0m,
    decimal AccrualAdjustmentExpenses = 0m,
    decimal AccrualBasisAdjustmentNetImpact = 0m,
    IReadOnlyList<LedgerPeriodTrialBalanceLineDto>? AccrualAdjustmentLines = null);

public sealed record LedgerCrossPeriodTrialBalanceLineDto(
    Guid PeriodId,
    Guid LedgerBookId,
    int FiscalYear,
    int PeriodNo,
    string PeriodLabel,
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
    Guid? SourceJournalEntryId = null,
    LedgerDimensionSetDto? Dimensions = null);

public sealed record LedgerCrossPeriodTrialBalanceReportDto(
    DateTimeOffset GeneratedAtUtc,
    Guid? LedgerBookId,
    string? FundProfileId,
    Guid? FundStructureNodeId,
    AccountingBasisKindDto? AccountingBasis,
    DateOnly? StartDate,
    DateOnly? EndDate,
    IReadOnlyList<LedgerPeriodDto> Periods,
    IReadOnlyList<LedgerCrossPeriodTrialBalanceLineDto> Lines,
    decimal TotalDebits,
    decimal TotalCredits,
    decimal NetIncome);

public sealed record LedgerCrossPeriodPnlReportDto(
    DateTimeOffset GeneratedAtUtc,
    Guid? LedgerBookId,
    string? FundProfileId,
    Guid? FundStructureNodeId,
    AccountingBasisKindDto? AccountingBasis,
    DateOnly? StartDate,
    DateOnly? EndDate,
    IReadOnlyList<LedgerPeriodPnlSummaryDto> Periods,
    decimal TotalRevenue,
    decimal TotalExpenses,
    decimal NetIncome,
    decimal TotalRealizedNetIncome = 0m,
    decimal TotalAccrualBasisAdjustmentNetImpact = 0m);

public sealed record LedgerPeriodCloseResultDto(
    LedgerPeriodDto Period,
    LedgerPeriodSummaryDto Summary,
    OperatorWorkItemDto WorkItem);

public sealed record LedgerPeriodReopenResultDto(
    LedgerPeriodDto Period,
    string PriorStatus,
    string ReopenedBy,
    DateTimeOffset ReopenedAtUtc,
    string ApprovalReference,
    IReadOnlyList<string> EvidenceLinks);

public interface ILedgerBookService
{
    Task<LedgerBookDto> CreateBookAsync(CreateLedgerBookRequest request, CancellationToken ct = default);

    Task<LedgerBookDto?> GetBookAsync(Guid ledgerBookId, CancellationToken ct = default);

    Task<IReadOnlyList<LedgerBookDto>> ListBooksAsync(LedgerBookQuery query, CancellationToken ct = default);

    Task<LedgerBookRolloutAssessmentDto> AssessRolloutAsync(
        LedgerBookRolloutAssessmentRequest request,
        CancellationToken ct = default)
        => Task.FromException<LedgerBookRolloutAssessmentDto>(
            new NotSupportedException("This ledger book service does not support rollout assessment."));

    Task<LedgerPeriodDto> CreatePeriodAsync(CreateLedgerPeriodRequest request, CancellationToken ct = default);

    Task<IReadOnlyList<LedgerPeriodDto>> ListPeriodsAsync(LedgerPeriodQuery query, CancellationToken ct = default);

    Task<IReadOnlyList<LedgerPeriodDto>> ListOpenPeriodsAsync(Guid? ledgerBookId = null, CancellationToken ct = default);

    Task<LedgerPeriodSummaryDto?> GetPeriodSummaryAsync(Guid periodId, CancellationToken ct = default);

    Task<LedgerPeriodCloseResultDto> ClosePeriodAsync(
        Guid periodId,
        CloseLedgerPeriodRequest request,
        CancellationToken ct = default);

    Task<LedgerPeriodReopenResultDto> ReopenPeriodAsync(
        Guid periodId,
        ReopenLedgerPeriodRequest request,
        CancellationToken ct = default)
        => Task.FromException<LedgerPeriodReopenResultDto>(
            new NotSupportedException("This ledger book service does not support governed period reopen."));
}
