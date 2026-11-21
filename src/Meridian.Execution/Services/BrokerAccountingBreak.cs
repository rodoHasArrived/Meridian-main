namespace Meridian.Execution.Services;

public enum BrokerAccountingBreakCategory
{
    TimingDifference,
    MissingEntry,
    AmountMismatch,
    UnknownActivity
}

/// <summary>Reconciliation evidence only; never authorizes a corrective or broker transaction.</summary>
public sealed record BrokerAccountingBreak(
    BrokerAccountingBreakCategory Category,
    string Reference,
    decimal? LocalAmount,
    decimal? BrokerAmount,
    string? Detail = null);
