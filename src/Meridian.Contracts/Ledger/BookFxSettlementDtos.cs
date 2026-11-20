using System.Text.Json.Serialization;

namespace Meridian.Contracts.Ledger;

[JsonConverter(typeof(JsonStringEnumConverter<BookFxSettlementLegDto>))]
public enum BookFxSettlementLegDto
{
    GaapTrade,
    GaapSettlement,
    CashSettlement
}

/// <summary>Exact scope of one journal expected by the reviewed settlement set.</summary>
public sealed record BookFxSettlementJournalDto(
    Guid JournalEntryId, Guid PeriodId, long PeriodVersion, string AccountingPolicyId, string AccountingPolicyVersion);

/// <summary>Economic facts of one settled tranche of a foreign-currency securities sale.</summary>
public sealed record RetainBookFxSettlementSetRequestDto(
    Guid SetId,
    Guid GaapBookId,
    Guid CashBookId,
    Guid TradeEventId,
    Guid SettlementEventId,
    Guid SecurityId,
    string Symbol,
    string FinancialAccountId,
    string ReceivableAccountName,
    string TransactionCurrency,
    DateOnly TradeDate,
    DateOnly SettlementDate,
    decimal TradeQuantity,
    decimal SettledQuantity,
    decimal GrossProceeds,
    decimal SettledProceeds,
    decimal GaapCostBasis,
    decimal CashRelievedBasis,
    Guid RateSetId,
    int RateSetRevision,
    string EvidenceReference,
    BookFxSettlementJournalDto GaapTrade,
    BookFxSettlementJournalDto GaapSettlement,
    BookFxSettlementJournalDto CashSettlement);

/// <summary>Functional amounts derived from directed approved rates and retained basis.</summary>
public sealed record BookFxSettlementAmountsDto(
    decimal GaapTradeReceivable,
    decimal GaapMarketResult,
    decimal ReceivableRelieved,
    decimal FunctionalCash,
    decimal GaapOutstandingReceivable,
    decimal CashMarketResult,
    decimal SettlementFxResult);

/// <summary>The approved input revision for a cross-book settlement evidence set.</summary>
public sealed record BookFxSettlementSetPlanDto(
    RetainBookFxSettlementSetRequestDto Input,
    Guid EntityId,
    string FundProfileId,
    string FunctionalCurrency,
    DateTimeOffset GaapBookRevision,
    DateTimeOffset CashBookRevision,
    ConsolidationFxRateDto TradeRate,
    ConsolidationFxRateDto SettlementRate,
    string RateSetHashSha256,
    BookFxSettlementAmountsDto Amounts,
    DateTimeOffset ApprovedAt,
    string ApprovedBy,
    string ControllerRole,
    string PlanHashSha256)
{
    public string RuleVersion { get; init; } = "gaap-cash-partial-sale/v1";
}

public sealed record CaptureBookFxSettlementLegRequestDto(
    Guid CommandId, string ExpectedPlanHashSha256, BookFxSettlementLegDto Leg);

/// <summary>Immutable proof of one approved posted consequence. Journal lines remain authoritative.</summary>
public sealed record BookFxSettlementLegReceiptDto(
    Guid CommandId,
    BookFxSettlementLegDto Leg,
    Guid LedgerBookId,
    Guid PeriodId,
    Guid JournalEntryId,
    long JournalSequence,
    Guid? LotMutationBatchId,
    string PlanHashSha256,
    DateTimeOffset CapturedAt,
    string CapturedBy);

public sealed record BookFxSettlementSetDto(
    BookFxSettlementSetPlanDto Plan,
    IReadOnlyList<BookFxSettlementLegReceiptDto> Receipts,
    IReadOnlyList<BookFxSettlementLegDto> MissingLegs,
    IReadOnlyList<BookFxSettlementLegDto> InvalidatedLegs)
{
    public bool IsComplete => MissingLegs.Count == 0 && InvalidatedLegs.Count == 0;
}
