using Meridian.Contracts.Ledger;
using Microsoft.AspNetCore.Http;
using static Meridian.Contracts.Ledger.LedgerDimensionTags;

namespace Meridian.Ui.Shared.Endpoints;

public static partial class LedgerEndpoints
{
    private static string BuildDimensionFilterSignature(LedgerDimensionReportFilter filter)
    {
        filter = CanonicalizeFilter(filter);
        if (!filter.HasCriteria)
        {
            return string.Empty;
        }

        var externalGl = filter.ExternalGlDimensions
            .OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(static pair => $"{pair.Key.Trim()}={pair.Value.Trim()}");

        var signature = string.Join(
            "|",
            filter.FundId ?? string.Empty,
            filter.EntityId ?? string.Empty,
            filter.SleeveId ?? string.Empty,
            filter.StrategyId ?? string.Empty,
            filter.InvestorId ?? string.Empty,
            filter.CapitalAccountId ?? string.Empty,
            filter.InstrumentId ?? string.Empty,
            filter.TaxLotId ?? string.Empty,
            filter.CostCenterId ?? string.Empty,
            filter.CounterpartyId ?? string.Empty,
            filter.OrganizationId ?? string.Empty,
            filter.PortfolioId ?? string.Empty,
            filter.BookId ?? string.Empty,
            filter.AccountId ?? string.Empty,
            filter.CustomerId ?? string.Empty,
            filter.VendorId ?? string.Empty,
            filter.ProjectId ?? string.Empty,
            string.Join(";", externalGl));

        return filter.PositionId is not null
            ? $"{signature}|positionId={filter.PositionId}"
            : signature;
    }

    private static string BuildDimensionSignature(LedgerDimensionSetDto? dimensions)
    {
        dimensions = CanonicalizeDimensions(dimensions);
        if (dimensions is null)
        {
            return string.Empty;
        }

        var externalGl = dimensions.ExternalGlDimensions
            .OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(static pair => $"{pair.Key.Trim()}={pair.Value.Trim()}");
        var signature = string.Join(
            "|",
            dimensions.FundId ?? string.Empty,
            dimensions.EntityId ?? string.Empty,
            dimensions.SleeveId ?? string.Empty,
            dimensions.StrategyId ?? string.Empty,
            dimensions.InvestorId ?? string.Empty,
            dimensions.CapitalAccountId ?? string.Empty,
            dimensions.InstrumentId?.ToString("D") ?? string.Empty,
            dimensions.TaxLotId ?? string.Empty,
            dimensions.CostCenterId ?? string.Empty,
            dimensions.CounterpartyId ?? string.Empty,
            dimensions.OrganizationId ?? string.Empty,
            dimensions.PortfolioId ?? string.Empty,
            dimensions.BookId ?? string.Empty,
            dimensions.AccountId ?? string.Empty,
            dimensions.CustomerId ?? string.Empty,
            dimensions.VendorId ?? string.Empty,
            dimensions.ProjectId ?? string.Empty,
            string.Join(";", externalGl));

        return dimensions.PositionId.HasValue
            ? $"{signature}|positionId={dimensions.PositionId.Value:D}"
            : signature;
    }

    private static LedgerDimensionReportFilter BuildDimensionReportFilter(IQueryCollection query)
    {
        var externalGlDimensions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in query)
        {
            const string prefix = "externalGl.";
            if (!pair.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var dimensionName = NormalizeOptional(pair.Key[prefix.Length..]);
            var dimensionValue = NormalizeOptional(pair.Value.ToString());
            if (dimensionName is not null && dimensionValue is not null)
            {
                externalGlDimensions[dimensionName] = dimensionValue;
            }
        }

        var externalGlDimensionKey = NormalizeOptional(GetQueryValue(query, "externalGlDimensionKey"));
        var externalGlDimensionValue = NormalizeOptional(GetQueryValue(query, "externalGlDimensionValue"));
        if (externalGlDimensionKey is not null && externalGlDimensionValue is not null)
        {
            externalGlDimensions[externalGlDimensionKey] = externalGlDimensionValue;
        }

        return CanonicalizeFilter(new LedgerDimensionReportFilter(
            FundId: NormalizeOptional(GetFirstQueryValue(query, "dimensionFundId", "fundId", "fundProfileId")),
            EntityId: NormalizeOptional(GetFirstQueryValue(query, "dimensionEntityId", "entityId")),
            SleeveId: NormalizeOptional(GetFirstQueryValue(query, "dimensionSleeveId", "sleeveId")),
            StrategyId: NormalizeOptional(GetFirstQueryValue(query, "dimensionStrategyId", "strategyId")),
            InvestorId: NormalizeOptional(GetFirstQueryValue(query, "dimensionInvestorId", "investorId")),
            CapitalAccountId: NormalizeOptional(GetFirstQueryValue(query, "dimensionCapitalAccountId", "capitalAccountId")),
            InstrumentId: NormalizeOptional(GetFirstQueryValue(query, "dimensionInstrumentId", "instrumentId")),
            PositionId: NormalizeOptional(GetFirstQueryValue(query, "dimensionPositionId", "positionId")),
            TaxLotId: NormalizeOptional(GetFirstQueryValue(query, "dimensionTaxLotId", "taxLotId")),
            CostCenterId: NormalizeOptional(GetFirstQueryValue(query, "dimensionCostCenterId", "costCenterId")),
            CounterpartyId: NormalizeOptional(GetFirstQueryValue(query, "dimensionCounterpartyId", "counterpartyId")),
            OrganizationId: NormalizeOptional(GetFirstQueryValue(query, "dimensionOrganizationId", "organizationId")),
            PortfolioId: NormalizeOptional(GetFirstQueryValue(query, "dimensionPortfolioId", "portfolioId")),
            BookId: NormalizeOptional(GetFirstQueryValue(query, "bookId", "ledgerBookDimensionId", "dimensionBookId")),
            AccountId: NormalizeOptional(GetFirstQueryValue(query, "dimensionAccountId", "accountId")),
            CustomerId: NormalizeOptional(GetFirstQueryValue(query, "dimensionCustomerId", "customerId")),
            VendorId: NormalizeOptional(GetFirstQueryValue(query, "dimensionVendorId", "vendorId")),
            ProjectId: NormalizeOptional(GetFirstQueryValue(query, "dimensionProjectId", "projectId")),
            ExternalGlDimensions: externalGlDimensions));
    }

    private static IReadOnlyDictionary<string, string> NormalizeExternalGlDimensions(
        IReadOnlyDictionary<string, string>? dimensions)
    {
        if (dimensions is null || dimensions.Count == 0)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var normalized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in dimensions.OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            var key = NormalizeOptional(pair.Key);
            var value = NormalizeOptional(pair.Value);
            if (key is not null && value is not null && !normalized.ContainsKey(key))
            {
                normalized[key] = value;
            }
        }

        return normalized;
    }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
