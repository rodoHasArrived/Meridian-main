using Meridian.Strategies.Models;

namespace Meridian.Strategies.Services;

internal sealed record StrategyRunScopeMetadata(
    string? AccountId,
    string? AccountDisplayName,
    string? EntityId,
    string? EntityDisplayName,
    string? SleeveId,
    string? SleeveDisplayName,
    string? VehicleId,
    string? VehicleDisplayName);

internal static class StrategyRunScopeMetadataResolver
{
    public static StrategyRunScopeMetadata Resolve(StrategyRunEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var accountId = ReadScopeValue(entry, "accountScopeId", "accountId");
        var accountDisplayName = ReadScopeValue(entry, "accountScopeDisplayName", "accountDisplayName", "accountName");

        if (string.IsNullOrWhiteSpace(accountId))
        {
            accountId = entry.Metrics?.Fills
                .Select(static fill => fill.AccountId)
                .Where(static id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .SingleOrDefault();
        }

        if (string.IsNullOrWhiteSpace(accountDisplayName) && !string.IsNullOrWhiteSpace(accountId))
        {
            accountDisplayName = accountId;
        }

        return new StrategyRunScopeMetadata(
            AccountId: accountId,
            AccountDisplayName: accountDisplayName,
            EntityId: ReadScopeValue(entry, "entityScopeId", "entityId"),
            EntityDisplayName: ReadScopeValue(entry, "entityScopeDisplayName", "entityDisplayName", "entityName"),
            SleeveId: ReadScopeValue(entry, "sleeveScopeId", "sleeveId"),
            SleeveDisplayName: ReadScopeValue(entry, "sleeveScopeDisplayName", "sleeveDisplayName", "sleeveName"),
            VehicleId: ReadScopeValue(entry, "vehicleScopeId", "vehicleId"),
            VehicleDisplayName: ReadScopeValue(entry, "vehicleScopeDisplayName", "vehicleDisplayName", "vehicleName"));
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
}
