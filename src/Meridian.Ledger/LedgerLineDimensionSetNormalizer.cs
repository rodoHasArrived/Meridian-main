using Meridian.Contracts.Text;

namespace Meridian.Ledger;

internal static class LedgerLineDimensionSetNormalizer
{
    internal static LedgerLineDimensionSet? Canonicalize(LedgerLineDimensionSet? dimensions)
    {
        if (dimensions is null)
            return null;

        var externalGlDimensions = NormalizeExternalGlDimensions(dimensions.ExternalGlDimensions);
        var canonical = new LedgerLineDimensionSet(
            FundId: TextPrimitives.NormalizeOptional(dimensions.FundId),
            EntityId: TextPrimitives.NormalizeOptional(dimensions.EntityId),
            SleeveId: TextPrimitives.NormalizeOptional(dimensions.SleeveId),
            StrategyId: TextPrimitives.NormalizeOptional(dimensions.StrategyId),
            InvestorId: TextPrimitives.NormalizeOptional(dimensions.InvestorId),
            CapitalAccountId: TextPrimitives.NormalizeOptional(dimensions.CapitalAccountId),
            InstrumentId: dimensions.InstrumentId,
            TaxLotId: TextPrimitives.NormalizeOptional(dimensions.TaxLotId),
            CostCenterId: TextPrimitives.NormalizeOptional(dimensions.CostCenterId),
            CounterpartyId: TextPrimitives.NormalizeOptional(dimensions.CounterpartyId),
            ExternalGlDimensions: externalGlDimensions,
            OrganizationId: TextPrimitives.NormalizeOptional(dimensions.OrganizationId),
            PortfolioId: TextPrimitives.NormalizeOptional(dimensions.PortfolioId),
            BookId: TextPrimitives.NormalizeOptional(dimensions.BookId),
            AccountId: TextPrimitives.NormalizeOptional(dimensions.AccountId),
            CustomerId: TextPrimitives.NormalizeOptional(dimensions.CustomerId),
            VendorId: TextPrimitives.NormalizeOptional(dimensions.VendorId),
            ProjectId: TextPrimitives.NormalizeOptional(dimensions.ProjectId))
        {
            PositionId = dimensions.PositionId
        };

        return HasAnyCanonical(canonical) ? canonical : null;
    }

    internal static bool HasAny(LedgerLineDimensionSet? dimensions)
        => Canonicalize(dimensions) is not null;

    private static bool HasAnyCanonical(LedgerLineDimensionSet dimensions)
        => dimensions.InstrumentId is not null
            || dimensions.PositionId is not null
            || dimensions.ExternalGlDimensions.Count > 0
            || LedgerLineDimensionSetFields.StringFields.Any(field => field.GetValue(dimensions) is not null);

    internal static bool Matches(LedgerLineDimensionSet? actual, LedgerLineDimensionSet? expected)
    {
        if (Canonicalize(expected) is not { } canonicalExpected)
            return true;

        if (Canonicalize(actual) is not { } canonicalActual)
            return false;

        return Matches(canonicalActual.InstrumentId, canonicalExpected.InstrumentId)
            && Matches(canonicalActual.PositionId, canonicalExpected.PositionId)
            && LedgerLineDimensionSetFields.StringFields.All(field =>
                Matches(field.GetValue(canonicalActual), field.GetValue(canonicalExpected)))
            && MatchesExternalGlDimensions(canonicalActual.ExternalGlDimensions, canonicalExpected.ExternalGlDimensions);
    }

    private static IReadOnlyDictionary<string, string> NormalizeExternalGlDimensions(IReadOnlyDictionary<string, string> dimensions)
        => dimensions
            .Select(static pair => new
            {
                Key = TextPrimitives.NormalizeOptional(pair.Key),
                Value = TextPrimitives.NormalizeOptional(pair.Value)
            })
            .Where(static pair => pair.Key is not null && pair.Value is not null)
            .GroupBy(static pair => pair.Key!, StringComparer.OrdinalIgnoreCase)
            .OrderBy(static group => group.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.First().Key!, static group => group.First().Value!, StringComparer.OrdinalIgnoreCase);

    private static bool MatchesExternalGlDimensions(
        IReadOnlyDictionary<string, string> actual,
        IReadOnlyDictionary<string, string> expected)
    {
        foreach (var (key, expectedValue) in expected)
        {
            if (!actual.TryGetValue(key, out var actualValue))
                return false;

            if (!string.Equals(actualValue, expectedValue, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    private static bool Matches(string? actual, string? expected)
        => expected is null || string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);

    private static bool Matches(Guid? actual, Guid? expected)
        => expected is null || actual == expected;
}
