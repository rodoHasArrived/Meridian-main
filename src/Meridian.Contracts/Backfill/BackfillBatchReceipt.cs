namespace Meridian.Contracts.Backfill;

/// <summary>Immutable evidence for a backfill response whose accepted rows reached storage.</summary>
public sealed record BackfillBatchReceipt(
    string RevisionId,
    string RequestedProvider,
    string? ServingProvider,
    string Feed,
    string Instrument,
    string Granularity,
    DateOnly? From,
    DateOnly? To,
    string Status,
    int RowCount,
    string ContentHash,
    string? Calendar,
    IReadOnlyList<DateOnly>? ExpectedSessions,
    IReadOnlyList<DateOnly> ReceivedSessions)
{
    /// <summary>Optional source quality snapshot bound by the content-addressed receipt.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public Meridian.Contracts.Operations.MarketDataInputEvidence? InputEvidence { get; init; }
}

/// <summary>Batch evidence for execution history. The revision retains the underlying session dates.</summary>
public sealed record BackfillBatchReceiptSummary(
    string RevisionId,
    string RequestedProvider,
    string? ServingProvider,
    string Feed,
    string Instrument,
    string Granularity,
    DateOnly? From,
    DateOnly? To,
    string Status,
    int RowCount,
    string ContentHash,
    string? Calendar,
    int? ExpectedSessionCount,
    int ReceivedSessionCount)
{
    public static BackfillBatchReceiptSummary FromReceipt(BackfillBatchReceipt receipt) =>
        new(receipt.RevisionId, receipt.RequestedProvider, receipt.ServingProvider, receipt.Feed,
            receipt.Instrument, receipt.Granularity, receipt.From, receipt.To, receipt.Status, receipt.RowCount,
            receipt.ContentHash, receipt.Calendar, receipt.ExpectedSessions?.Count, receipt.ReceivedSessions.Count);
}

/// <summary>Sources of expected exchange sessions for historical coverage, when available.</summary>
public static class HistoricalSessionEvidence
{
    public const string UsEquities = "us-equity-sessions-v1";

    public static bool IsUsEquityListing(string? mic) => mic is "XNYS" or "XNAS" or "ARCX" or "XASE" or "BATS" or "IEXG";
}

/// <summary>The session dates supported by a positively identified exchange calendar.</summary>
public sealed record HistoricalSessionSchedule(string Calendar, IReadOnlyList<DateOnly> Sessions);
