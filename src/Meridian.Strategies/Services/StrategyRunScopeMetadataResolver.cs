using Meridian.Strategies.Models;

namespace Meridian.Strategies.Services;

internal sealed record StrategyRunScopeMetadata(
    string? FundId,
    string? StrategyId,
    string? PortfolioId,
    string? BookId,
    string? AccountId,
    string? AccountDisplayName,
    string? EntityId,
    string? EntityDisplayName,
    string? SleeveId,
    string? SleeveDisplayName,
    string? VehicleId,
    string? VehicleDisplayName,
    string? OrganizationId,
    string? CustomerId,
    string? VendorId,
    string? ProjectId,
    IReadOnlyDictionary<string, string> ExternalGlDimensions);

internal static class StrategyRunScopeMetadataResolver
{
    public static StrategyRunScopeMetadata Resolve(StrategyRunEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var accountId = ReadScopeValue(entry, "accountScopeId", "accountId");
        var accountDisplayName = ReadScopeValue(entry, "accountScopeDisplayName", "accountDisplayName", "accountName");

        if (string.IsNullOrWhiteSpace(accountId))
        {
            accountId = InferAccountIdFromFills(entry);
        }

        if (string.IsNullOrWhiteSpace(accountDisplayName) && !string.IsNullOrWhiteSpace(accountId))
        {
            accountDisplayName = accountId;
        }

        return new StrategyRunScopeMetadata(
            FundId: entry.FundProfileId,
            StrategyId: entry.StrategyId,
            PortfolioId: entry.PortfolioId,
            BookId: ReadScopeValue(entry, "ledgerBookId", "bookId", "ledgerBookScopeId", "bookScopeId"),
            AccountId: accountId,
            AccountDisplayName: accountDisplayName,
            EntityId: ReadScopeValue(entry, "entityScopeId", "entityId"),
            EntityDisplayName: ReadScopeValue(entry, "entityScopeDisplayName", "entityDisplayName", "entityName"),
            SleeveId: ReadScopeValue(entry, "sleeveScopeId", "sleeveId"),
            SleeveDisplayName: ReadScopeValue(entry, "sleeveScopeDisplayName", "sleeveDisplayName", "sleeveName"),
            VehicleId: ReadScopeValue(entry, "vehicleScopeId", "vehicleId"),
            VehicleDisplayName: ReadScopeValue(entry, "vehicleScopeDisplayName", "vehicleDisplayName", "vehicleName"),
            OrganizationId: ReadScopeValue(entry, "organizationScopeId", "organizationId"),
            CustomerId: ReadScopeValue(entry, "customerScopeId", "customerId"),
            VendorId: ReadScopeValue(entry, "vendorScopeId", "vendorId"),
            ProjectId: ReadScopeValue(entry, "projectScopeId", "projectId"),
            ExternalGlDimensions: ReadExternalGlDimensions(entry));
    }

    private static string? ReadScopeValue(StrategyRunEntry entry, params string[] keys)
    {
        if (entry.ParameterSet is null || entry.ParameterSet.Count == 0)
        {
            return null;
        }

        foreach (var key in keys)
        {
            if (entry.ParameterSet.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private static IReadOnlyDictionary<string, string> ReadExternalGlDimensions(StrategyRunEntry entry)
    {
        if (entry.ParameterSet is null || entry.ParameterSet.Count == 0)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var dimensions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in entry.ParameterSet)
        {
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            const string prefix = "externalGl.";
            if (key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                key.Length > prefix.Length)
            {
                dimensions[key[prefix.Length..].Trim()] = value.Trim();
            }
        }

        return dimensions;
    }

    private static string? InferAccountIdFromFills(StrategyRunEntry entry)
    {
        if (entry.Metrics?.Fills is null)
        {
            return null;
        }

        using var distinctAccountIds = entry.Metrics.Fills
            .Select(static fill => fill.AccountId)
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .Take(2)
            .GetEnumerator();

        if (!distinctAccountIds.MoveNext())
        {
            return null;
        }

        var firstAccountId = distinctAccountIds.Current;
        return distinctAccountIds.MoveNext() ? null : firstAccountId;
    }
}
