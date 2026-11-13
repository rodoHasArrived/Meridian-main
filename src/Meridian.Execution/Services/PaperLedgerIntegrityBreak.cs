namespace Meridian.Execution.Services;

/// <summary>A difference between the paper portfolio and its posted double-entry record.</summary>
public sealed record PaperLedgerIntegrityBreak(
    string AccountId,
    string Check,
    decimal Expected,
    decimal Actual)
{
    public decimal Difference => Actual - Expected;
}
