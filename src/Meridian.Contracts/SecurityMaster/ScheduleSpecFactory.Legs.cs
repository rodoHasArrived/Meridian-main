namespace Meridian.Contracts.SecurityMaster;

public static partial class ScheduleSpecFactory
{
    internal static void ValidateLegConventionShape(string assetClass, ScheduleConventionTerms block)
    {
        if (!TryReadSpellings(assetClass, block, out var spellings, out var refusal, "legs.scheduleConventions"))
            throw new InvalidOperationException(refusal!.Refusal!.Message);
        if (FindConflict(block, spellings) is { } conflict)
            throw new InvalidOperationException(conflict);
    }
}
