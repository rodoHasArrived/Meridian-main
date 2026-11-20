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
    IReadOnlyList<DateOnly> ReceivedSessions);

/// <summary>Sources of expected exchange sessions for historical coverage, when available.</summary>
public static class HistoricalSessionEvidence
{
    public const string UsEquities = "us-equity-sessions-v1";

    public static bool IsUsEquityListing(string? mic) => mic is "XNYS" or "XNAS" or "ARCX" or "XASE" or "BATS" or "IEXG";
}

/// <summary>The session dates supported by a positively identified exchange calendar.</summary>
public sealed record HistoricalSessionSchedule(string Calendar, IReadOnlyList<DateOnly> Sessions);
