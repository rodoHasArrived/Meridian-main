namespace Meridian.FinancialOperations.Reconciliation;

public enum ReconciliationMatchTier
{
    Exact,
    Tolerance,
    Heuristic,
    Unmatched
}

public sealed record CanonicalReconciliationFeedRecord(
    string SourceSystem,
    string AccountCode,
    string Symbol,
    decimal Quantity,
    decimal Price,
    decimal CashAmount,
    DateOnly TradeDate,
    string ActivityType,
    string ExternalReference,
    DateTimeOffset CapturedAtUtc);

/// <summary>
/// Reads source feed data into canonical reconciliation records.
/// </summary>
public interface IReconciliationFeedAdapter
{
    string SourceSystem { get; }

    Task<IReadOnlyList<CanonicalReconciliationFeedRecord>> ReadAsync(
        string sourcePath,
        CancellationToken ct = default);
}
