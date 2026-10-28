using Meridian.Contracts.SecurityMaster.Schedules;

namespace Meridian.Application.DirectLending;

/// <summary>Legacy direct-lending projection periods on the shared schedule kernel's compounded grid.</summary>
internal static class DirectLendingSchedulePeriods
{
    internal static IEnumerable<(DateOnly Start, DateOnly End)> LegacyCompounded(
        DateOnly start, DateOnly maturity, int months, DateOnly? projectionThrough = null)
    {
        if (start >= maturity || projectionThrough is { } through && start >= through)
            yield break;
        var tenor = ScheduleTenor.Months(months);
        var cursor = start;
        foreach (var gridDate in ScheduleGenerator.RollDates(start, tenor, ScheduleAnchorMode.Compounded).Skip(1))
        {
            var end = gridDate < maturity ? gridDate : maturity;
            yield return (cursor, end);
            cursor = end;
            // A cutoff within a period includes that whole period, exactly as both old loops did.
            if (cursor >= maturity || projectionThrough is { } limit && cursor >= limit)
                yield break;
        }

        // The legacy loops evaluate the closing step even when it lies beyond DateOnly's range.
        // Preserve that failure for existing engine versions; the kernel grid itself stops safely.
        _ = tenor.Advance(cursor, 1);
    }
}
