using System.Text.Json.Serialization;

namespace Meridian.Contracts.Workstation;

[JsonConverter(typeof(JsonStringEnumConverter<InvestmentAccountingTransactionKindDto>))]
public enum InvestmentAccountingTransactionKindDto
{
    Trade = 0,
    Dividend = 1,
    Fee = 2,
    Accrual = 3,
    CorporateAction = 4,
    BrokerReconciliation = 5
}

[JsonConverter(typeof(JsonStringEnumConverter<InvestmentAccountingTradeSideDto>))]
public enum InvestmentAccountingTradeSideDto
{
    Buy = 0,
    Sell = 1
}

[JsonConverter(typeof(JsonStringEnumConverter<InvestmentAccountingPreviewModeDto>))]
public enum InvestmentAccountingPreviewModeDto
{
    TransactionLab = 0,
    BooksBeforeBroker = 1
}

public sealed record InvestmentAccountingTransactionLabRequestDto(
    InvestmentAccountingTransactionKindDto Kind,
    string FundAccountId,
    string Symbol,
    DateOnly EventDate,
    string Currency,
    decimal Amount,
    decimal Quantity = 0m,
    decimal Price = 0m,
    decimal FeeAmount = 0m,
    InvestmentAccountingTradeSideDto? Side = null,
    string? SourceRunId = null,
    string? SourceSessionId = null,
    string? BrokerStatementId = null,
    string? ReconciliationCaseId = null,
    IReadOnlyList<string>? EvidenceIds = null,
    InvestmentAccountingPreviewModeDto PreviewMode = InvestmentAccountingPreviewModeDto.TransactionLab);

public sealed record InvestmentAccountingTrialBalanceImpactDto(
    string AccountName,
    string AccountType,
    string? Symbol,
    decimal BalanceDelta,
    string Explanation);

public sealed record InvestmentAccountingReconciliationExpectationDto(
    string ExpectedState,
    string ExpectedBreakType,
    string Detail,
    IReadOnlyList<string> EvidenceIds,
    string? BrokerStatementId = null,
    string? ReconciliationCaseId = null);

public sealed record BooksBeforeBrokerReadinessDto(
    bool IsBooksBeforeBrokerMode,
    bool CanStageBrokerAction,
    string ExpectedBrokerAction,
    string BrokerInstructionSummary,
    IReadOnlyList<string> RequiredApprovals,
    IReadOnlyList<string> Blockers,
    IReadOnlyList<string> EvidenceIds);

public sealed record InvestmentAccountingTransactionLabPreviewDto(
    string PreviewId,
    InvestmentAccountingTransactionKindDto Kind,
    string FundAccountId,
    string Symbol,
    DateOnly EventDate,
    string Currency,
    ExpectedJournalPreviewDto JournalPreview,
    LedgerImpactPreviewDto LedgerImpact,
    IReadOnlyList<InvestmentAccountingTrialBalanceImpactDto> TrialBalanceImpact,
    InvestmentAccountingReconciliationExpectationDto ReconciliationExpectation,
    IReadOnlyList<string> EvidenceIds,
    string? SourceRunId = null,
    string? SourceSessionId = null,
    BooksBeforeBrokerReadinessDto? BooksBeforeBroker = null);
