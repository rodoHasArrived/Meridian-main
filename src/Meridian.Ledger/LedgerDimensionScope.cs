namespace Meridian.Ledger;

/// <summary>Shared dimension matching for journal replay and retained balance projections.</summary>
public static class LedgerDimensionScope
{
    public static bool Matches(LedgerLineDimensionSet? actual, LedgerLineDimensionSet? expected)
        => LedgerLineDimensionSetNormalizer.Matches(actual, expected);
}
