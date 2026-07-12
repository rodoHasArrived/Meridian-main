namespace Meridian.Ledger;

internal static class LedgerLineDimensionSetNormalizer
{
    internal static LedgerLineDimensionSet? Canonicalize(LedgerLineDimensionSet? dimensions)
    {
        if (dimensions is null)
            return null;

        var externalGlDimensions = NormalizeExternalGlDimensions(dimensions.ExternalGlDimensions);
        var canonical = new LedgerLineDimensionSet(
            FundId: Normalize(dimensions.FundId),
            EntityId: Normalize(dimensions.EntityId),
            SleeveId: Normalize(dimensions.SleeveId),
            StrategyId: Normalize(dimensions.StrategyId),
            InvestorId: Normalize(dimensions.InvestorId),
            CapitalAccountId: Normalize(dimensions.CapitalAccountId),
            InstrumentId: dimensions.InstrumentId,
            TaxLotId: Normalize(dimensions.TaxLotId),
            CostCenterId: Normalize(dimensions.CostCenterId),
            CounterpartyId: Normalize(dimensions.CounterpartyId),
            ExternalGlDimensions: externalGlDimensions,
            OrganizationId: Normalize(dimensions.OrganizationId),
            PortfolioId: Normalize(dimensions.PortfolioId),
            BookId: Normalize(dimensions.BookId),
            AccountId: Normalize(dimensions.AccountId),
            CustomerId: Normalize(dimensions.CustomerId),
            VendorId: Normalize(dimensions.VendorId),
            ProjectId: Normalize(dimensions.ProjectId))
        {
            PositionId = dimensions.PositionId
        };

        return HasAnyCanonical(canonical) ? canonical : null;
    }

    internal static bool HasAny(LedgerLineDimensionSet? dimensions)
        => Canonicalize(dimensions) is not null;

    private static bool HasAnyCanonical(LedgerLineDimensionSet dimensions)
        => dimensions.FundId is not null
            || dimensions.EntityId is not null
            || dimensions.SleeveId is not null
            || dimensions.StrategyId is not null
            || dimensions.InvestorId is not null
            || dimensions.CapitalAccountId is not null
            || dimensions.InstrumentId is not null
            || dimensions.PositionId is not null
            || dimensions.TaxLotId is not null
            || dimensions.CostCenterId is not null
            || dimensions.CounterpartyId is not null
            || dimensions.ExternalGlDimensions.Count > 0
            || dimensions.OrganizationId is not null
            || dimensions.PortfolioId is not null
            || dimensions.BookId is not null
            || dimensions.AccountId is not null
            || dimensions.CustomerId is not null
            || dimensions.VendorId is not null
            || dimensions.ProjectId is not null;

    internal static bool Matches(LedgerLineDimensionSet? actual, LedgerLineDimensionSet? expected)
    {
        expected = Canonicalize(expected);
        if (expected is null)
            return true;

        actual = Canonicalize(actual);
        if (actual is null)
            return false;

        return Matches(actual.FundId, expected.FundId)
            && Matches(actual.EntityId, expected.EntityId)
            && Matches(actual.SleeveId, expected.SleeveId)
            && Matches(actual.StrategyId, expected.StrategyId)
            && Matches(actual.InvestorId, expected.InvestorId)
            && Matches(actual.CapitalAccountId, expected.CapitalAccountId)
            && Matches(actual.InstrumentId, expected.InstrumentId)
            && Matches(actual.PositionId, expected.PositionId)
            && Matches(actual.TaxLotId, expected.TaxLotId)
            && Matches(actual.CostCenterId, expected.CostCenterId)
            && Matches(actual.CounterpartyId, expected.CounterpartyId)
            && Matches(actual.OrganizationId, expected.OrganizationId)
            && Matches(actual.PortfolioId, expected.PortfolioId)
            && Matches(actual.BookId, expected.BookId)
            && Matches(actual.AccountId, expected.AccountId)
            && Matches(actual.CustomerId, expected.CustomerId)
            && Matches(actual.VendorId, expected.VendorId)
            && Matches(actual.ProjectId, expected.ProjectId)
            && MatchesExternalGlDimensions(actual.ExternalGlDimensions, expected.ExternalGlDimensions);
    }

    private static IReadOnlyDictionary<string, string> NormalizeExternalGlDimensions(IReadOnlyDictionary<string, string> dimensions)
        => dimensions
            .Select(static pair => new
            {
                Key = Normalize(pair.Key),
                Value = Normalize(pair.Value)
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

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
