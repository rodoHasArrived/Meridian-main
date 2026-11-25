namespace Meridian.Core.Config;

/// <summary>
/// Save-time check that failover rules name configured data sources. A rule that points at an ID
/// with no <see cref="DataSourcesConfig.Sources"/> entry silently fails over to the wrong provider
/// (or none) at runtime, so it is rejected when saved rather than discovered during an outage.
/// </summary>
public static class FailoverRuleReferences
{
    /// <summary>
    /// Returns the first provider ID in <paramref name="rule"/> that is not a configured source, or
    /// <see langword="null"/> when every ID resolves. When no sources are configured, rules refer to
    /// built-in provider IDs and nothing is checked.
    /// </summary>
    public static string? FindUnknownProvider(DataSourcesConfig? config, FailoverRuleConfig rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        var sources = config?.Sources;
        if (sources is null || sources.Length == 0)
            return null;

        var known = new HashSet<string>(
            sources.Where(source => !string.IsNullOrWhiteSpace(source.Id)).Select(source => source.Id.Trim()),
            StringComparer.OrdinalIgnoreCase);
        return new[] { rule.PrimaryProviderId }
            .Concat(rule.BackupProviderIds ?? [])
            .FirstOrDefault(id => string.IsNullOrWhiteSpace(id) || !known.Contains(id.Trim()));
    }

    /// <summary>
    /// Checks only rules that are new or changed between <paramref name="current"/> and
    /// <paramref name="next"/>, so an unrelated save never fails because of a pre-existing rule.
    /// Returns a message naming the rule and the unknown provider, or <see langword="null"/>.
    /// </summary>
    public static string? FindUnknownInChangedRules(DataSourcesConfig? current, DataSourcesConfig? next)
    {
        var previous = (current?.FailoverRules ?? [])
            .GroupBy(rule => rule.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);
        foreach (var rule in next?.FailoverRules ?? [])
        {
            if (previous.TryGetValue(rule.Id, out var before) && SameProviders(before, rule) &&
                SameSources(current, next))
                continue;

            if (FindUnknownProvider(next, rule) is { } unknown)
                return $"Failover rule '{rule.Id}' references '{unknown}', which is not a configured data source ID.";
        }

        return null;
    }

    private static bool SameProviders(FailoverRuleConfig a, FailoverRuleConfig b)
        => string.Equals(a.PrimaryProviderId, b.PrimaryProviderId, StringComparison.OrdinalIgnoreCase)
            && (a.BackupProviderIds ?? []).SequenceEqual(b.BackupProviderIds ?? [], StringComparer.OrdinalIgnoreCase);

    private static bool SameSources(DataSourcesConfig? a, DataSourcesConfig? b)
        => (a?.Sources ?? []).Select(source => source.Id)
            .SequenceEqual((b?.Sources ?? []).Select(source => source.Id), StringComparer.OrdinalIgnoreCase);
}
