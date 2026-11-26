using Meridian.Ledger;

namespace Meridian.Tests.Ledger;

/// <summary>
/// Test-only shortcut for projecting closing entries from an in-memory ledger. The in-memory
/// trial balance is dimension-flat, so these projections carry no dimensional scope; production
/// closes project from the retained, dimension-scoped period trial balance instead
/// (<c>AutomatedJournalIntakeRunner.PreviewPeriodCloseAsync</c>).
/// </summary>
internal static class PeriodCloseTestProjections
{
    public static PeriodCloseProjection FromLedger(
        Meridian.Ledger.Ledger ledger,
        string periodId,
        DateTimeOffset closedAtUtc,
        string closedBy)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        return PeriodCloseProjector.Project(new PeriodCloseInput(
            periodId,
            closedAtUtc,
            ledger.TrialBalanceAsOf(closedAtUtc),
            closedBy));
    }
}
