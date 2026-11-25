using Meridian.Contracts.Workstation;
using Meridian.Ui.Shared.Services;

namespace Meridian.Tests.Ui;

public sealed class OpenBreakHistoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 17, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public void RebuildsEachDayEndFromDetectionAndResolutionTimesAndEndsOnTheLiveCount()
    {
        var items = new[]
        {
            Break("a", detected: Now.AddDays(-5), status: ReconciliationBreakQueueStatus.Resolved, resolved: Now.AddDays(-2)),
            Break("b", detected: Now.AddDays(-1), status: ReconciliationBreakQueueStatus.Open),
            Break("c", detected: Now.AddDays(-10), status: ReconciliationBreakQueueStatus.Dismissed, resolved: Now.AddDays(-8)),
            Break("d", detected: Now.AddDays(-3), status: ReconciliationBreakQueueStatus.InReview)
        };

        var history = OpenBreakHistory.Build(items, Now, days: 7);

        Assert.Equal(7, history.Count);
        Assert.Equal(new DateOnly(2026, 11, 11), history[0].Date);
        Assert.Equal(new DateOnly(2026, 11, 17), history[^1].Date);
        // Day ends Nov 11..16, then today's live count. "a" is open Nov 12 15:00 to Nov 15 15:00, "d" from
        // Nov 14 15:00, "b" from Nov 16 15:00; "c" closed before the window.
        //   Nov 11: 0 · Nov 12: a · Nov 13: a · Nov 14: a, d · Nov 15: d · Nov 16: d, b · today: d, b
        Assert.Equal(new decimal?[] { 0, 1, 1, 2, 1, 2, 2 }, history.Select(point => point.Value).ToArray());
        Assert.Equal(CrossScreenCountDefinitions.CountOpenBreaks(items), (int)history[^1].Value!);
    }

    [Fact]
    public void AClosedBreakWithoutAResolutionTimeLeavesItsDaysBlankRatherThanGuessed()
    {
        var items = new[]
        {
            Break("a", detected: Now.AddDays(-3), status: ReconciliationBreakQueueStatus.SignedOff, resolved: null),
            Break("b", detected: Now.AddDays(-10), status: ReconciliationBreakQueueStatus.Open)
        };

        var history = OpenBreakHistory.Build(items, Now, days: 6);

        // Before "a" was detected its state does not matter; from then on it cannot be placed.
        Assert.Equal(new decimal?[] { 1, 1, null, null, null, 1 }, history.Select(point => point.Value).ToArray());
    }

    [Fact]
    public void DaysBeforeAReopenAreUnknownBecauseTheEarlierResolutionTimeWasCleared()
    {
        var items = new[]
        {
            Break("a", detected: Now.AddDays(-6), status: ReconciliationBreakQueueStatus.Open) with
            {
                LifecycleState = ReconciliationCaseLifecycleState.Reopened,
                ReopenedAt = Now.AddDays(-2)
            }
        };

        var history = OpenBreakHistory.Build(items, Now, days: 5);

        // Nov 13 .. Nov 14 day ends fall before the Nov 15 reopen: unknown. After it: open.
        Assert.Equal(new decimal?[] { null, null, 1, 1, 1 }, history.Select(point => point.Value).ToArray());
    }

    [Fact]
    public void AReopenedStateWithoutAReopenTimeCannotEstablishAnyPastDay()
    {
        var item = Break("a", detected: Now.AddDays(-4), status: ReconciliationBreakQueueStatus.Open) with
        {
            LifecycleState = ReconciliationCaseLifecycleState.Reopened
        };

        var history = OpenBreakHistory.Build([item], Now, days: 3);

        Assert.Equal(new decimal?[] { null, null, 1 }, history.Select(point => point.Value).ToArray());
    }

    private static ReconciliationBreakQueueItem Break(string id, DateTimeOffset detected,
        ReconciliationBreakQueueStatus status, DateTimeOffset? resolved = null)
        => new(id, "synthetic-run", "Synthetic break", ReconciliationBreakCategory.ExternalStatementMismatch,
            status, 1m, "Synthetic difference.", null, detected, resolved ?? detected, ResolvedAt: resolved);
}
