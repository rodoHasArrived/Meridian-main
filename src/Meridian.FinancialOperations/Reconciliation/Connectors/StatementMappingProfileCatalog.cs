namespace Meridian.FinancialOperations.Reconciliation.Connectors;

/// <summary>
/// Live view over the built-in profile documents plus the operator-owned file store.
/// The connectors and import preview read profiles through this catalog, so a profile
/// saved by an operator is usable on the very next import without a restart.
/// Also owns format-drift detection against each profile's last accepted column set.
/// </summary>
public sealed class StatementMappingProfileCatalog(IStatementMappingProfileStore store)
{
    private const char FingerprintSeparator = '|';

    public async Task<IReadOnlyList<StatementMappingProfileDocument>> ListAsync(CancellationToken ct = default)
    {
        var stored = await store.ListAsync(ct).ConfigureAwait(false);
        var builtInIds = StatementBuiltInProfiles.All
            .Select(static profile => profile.ProfileId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        // Built-ins win on id collision so a hand-edited store file cannot shadow shipped behavior.
        var operatorProfiles = stored.Where(profile => !builtInIds.Contains(profile.ProfileId));
        return StatementBuiltInProfiles.All
            .Concat(operatorProfiles)
            .OrderBy(static profile => profile.ProfileId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<StatementMappingProfileDocument?> FindAsync(string? profileId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(profileId))
        {
            return null;
        }

        var profiles = await ListAsync(ct).ConfigureAwait(false);
        return profiles.FirstOrDefault(profile =>
            string.Equals(profile.ProfileId, profileId.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    public Task<StatementMappingProfileDocument> UpsertAsync(StatementMappingProfileDocument document, CancellationToken ct = default)
        => store.UpsertAsync(document, ct);

    public Task<bool> DeleteAsync(string profileId, CancellationToken ct = default)
        => store.DeleteAsync(profileId, ct);

    /// <summary>
    /// Builds the runtime registry consumed by the legacy reconciliation paths. Captured at
    /// startup by singleton services; the connector paths read the catalog live instead.
    /// </summary>
    public StatementMappingProfileRegistry BuildRegistry()
    {
        var documents = ListAsync().GetAwaiter().GetResult();
        var profiles = new List<StatementMappingProfile>(documents.Count);
        foreach (var document in documents)
        {
            if (StatementMappingProfileLoader.Validate(document).Count == 0)
            {
                profiles.Add(StatementMappingProfileLoader.ToRuntimeProfile(document));
            }
        }

        return new StatementMappingProfileRegistry(profiles);
    }

    /// <summary>
    /// Compares a parsed source's column set to the profile's last accepted fingerprint and
    /// reports added/removed columns, so a custodian silently changing its export layout is
    /// surfaced before rows map incorrectly.
    /// </summary>
    public static StatementParseIssue? CheckDrift(StatementMappingProfileDocument profile, StatementFormatFingerprint fingerprint)
    {
        if (string.IsNullOrWhiteSpace(profile.LastAcceptedFingerprint))
        {
            return null;
        }

        var accepted = profile.LastAcceptedFingerprint
            .Split(FingerprintSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var current = fingerprint.NormalizedColumns.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (accepted.SetEquals(current))
        {
            return null;
        }

        var added = current.Except(accepted, StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        var removed = accepted.Except(current, StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        var parts = new List<string>();
        if (added.Length > 0)
        {
            parts.Add($"new columns: {string.Join(", ", added)}");
        }

        if (removed.Length > 0)
        {
            parts.Add($"missing columns: {string.Join(", ", removed)}");
        }

        return StatementParseIssue.Warning(
            "FORMAT_DRIFT",
            $"Source layout differs from the last statement accepted with profile '{profile.ProfileId}' ({string.Join("; ", parts)}). Review the column mappings before committing.");
    }

    /// <summary>
    /// Records the accepted column set after a successful commit. Built-in profiles are
    /// immutable, so drift tracking applies to operator-owned profiles only.
    /// </summary>
    public async Task RecordAcceptedFingerprintAsync(string? profileId, StatementFormatFingerprint fingerprint, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(profileId))
        {
            return;
        }

        var profile = await FindAsync(profileId, ct).ConfigureAwait(false);
        if (profile is null || profile.IsBuiltIn)
        {
            return;
        }

        var accepted = string.Join(
            FingerprintSeparator,
            fingerprint.NormalizedColumns
                .Select(static column => column.Trim())
                .Where(static column => column.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase));
        if (string.Equals(profile.LastAcceptedFingerprint, accepted, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        await store.UpsertAsync(profile with { LastAcceptedFingerprint = accepted }, ct).ConfigureAwait(false);
    }
}
