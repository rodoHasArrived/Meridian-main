using Meridian.Contracts.SecurityMaster;

namespace Meridian.Application.SecurityMaster;

/// <summary>
/// Pure golden-record conflict detection over the Security Master projection universe. Kept storage
/// agnostic so both the in-memory and the durable Postgres conflict stores detect identical conflicts;
/// the store layer decides how detected candidates are persisted and whether existing resolution state
/// is preserved.
/// </summary>
internal static class SecurityMasterConflictDetection
{
    private const string IdentifierAmbiguityKind = "IdentifierAmbiguity";
    private const string FieldValueKind = SecurityMasterGoldenRecordMerge.FieldValueConflictKind;
    private const string UnknownProvider = "Unknown";

    /// <summary>
    /// Detects every identifier-ambiguity conflict across the universe: an identifier that multiple
    /// distinct securities claim from different providers. Every returned conflict has status
    /// <c>Open</c> and a deterministic id, so re-detecting the same pair yields the same id.
    /// </summary>
    public static IReadOnlyList<SecurityMasterConflict> DetectAll(
        IReadOnlyList<SecurityProjectionRecord> universe,
        DateTimeOffset detectedAt)
    {
        var byIdentifier = new Dictionary<string, List<(Guid SecurityId, string Provider)>>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var record in universe)
        {
            foreach (var id in record.Identifiers)
            {
                var key = $"{id.Kind}|{id.Value}";
                if (!byIdentifier.TryGetValue(key, out var entries))
                {
                    entries = new List<(Guid, string)>();
                    byIdentifier[key] = entries;
                }

                var provider = id.Provider ?? UnknownProvider;
                if (!entries.Any(e => e.SecurityId == record.SecurityId))
                {
                    entries.Add((record.SecurityId, provider));
                }
            }
        }

        var conflicts = new List<SecurityMasterConflict>();
        foreach (var (key, entries) in byIdentifier)
        {
            if (entries.Count < 2)
            {
                continue;
            }

            var distinctSecurities = entries.DistinctBy(e => e.SecurityId).ToList();
            if (distinctSecurities.Count < 2)
            {
                continue;
            }

            var parts = key.Split('|', 2);
            var kind = parts[0];
            var value = parts.Length > 1 ? parts[1] : string.Empty;

            var a = distinctSecurities[0];
            var b = distinctSecurities[1];

            conflicts.Add(new SecurityMasterConflict(
                ConflictId: DeterministicConflictId(kind, value, a.SecurityId, b.SecurityId),
                SecurityId: a.SecurityId,
                ConflictKind: IdentifierAmbiguityKind,
                FieldPath: $"Identifiers.{kind}",
                ProviderA: a.Provider,
                ValueA: a.SecurityId.ToString(),
                ProviderB: b.Provider,
                ValueB: b.SecurityId.ToString(),
                DetectedAt: detectedAt,
                Status: "Open"));
        }

        return conflicts;
    }

    /// <summary>
    /// Detects conflicts a freshly written projection introduces: identifiers on the new projection
    /// that another existing security already claims. Used at ingest time so a create/amend/import
    /// records new conflicts immediately without a full-universe rescan.
    /// </summary>
    public static IReadOnlyList<SecurityMasterConflict> DetectForProjection(
        SecurityProjectionRecord projection,
        IReadOnlyList<SecurityProjectionRecord> universe,
        DateTimeOffset detectedAt)
    {
        var byIdentifier = new Dictionary<string, (Guid SecurityId, string Provider)>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var existing in universe)
        {
            if (existing.SecurityId == projection.SecurityId)
            {
                continue;
            }

            foreach (var id in existing.Identifiers)
            {
                var key = $"{id.Kind}|{id.Value}";
                // Track only the first record we encounter for each identifier (deterministic).
                byIdentifier.TryAdd(key, (existing.SecurityId, id.Provider ?? UnknownProvider));
            }
        }

        var conflicts = new List<SecurityMasterConflict>();
        foreach (var id in projection.Identifiers)
        {
            var key = $"{id.Kind}|{id.Value}";
            if (!byIdentifier.TryGetValue(key, out var conflicting))
            {
                continue;
            }

            conflicts.Add(new SecurityMasterConflict(
                ConflictId: DeterministicConflictId(id.Kind.ToString(), id.Value, projection.SecurityId, conflicting.SecurityId),
                SecurityId: projection.SecurityId,
                ConflictKind: IdentifierAmbiguityKind,
                FieldPath: $"Identifiers.{id.Kind}",
                ProviderA: id.Provider ?? UnknownProvider,
                ValueA: projection.SecurityId.ToString(),
                ProviderB: conflicting.Provider,
                ValueB: conflicting.SecurityId.ToString(),
                DetectedAt: detectedAt,
                Status: "Open"));
        }

        return conflicts;
    }

    /// <summary>
    /// Detects field-value conflicts a freshly written projection introduces: two distinct securities
    /// that share an identifier are the same instrument mastered from different sources, so a divergent
    /// authoritative field between them (e.g. currency) is a genuine golden-record disagreement. Emits
    /// <c>ConflictKind = "FieldValue"</c> with real field values (not SecurityIds) and a canonical
    /// dotted <c>FieldPath</c>, so resolving the conflict can merge the winning value into the stored
    /// terms rather than only annotate a winner. Scoped to the projection-write path (not the
    /// full-universe rescan) to bound the field-comparison cost.
    /// </summary>
    public static IReadOnlyList<SecurityMasterConflict> DetectFieldConflictsForProjection(
        SecurityProjectionRecord projection,
        IReadOnlyList<SecurityProjectionRecord> universe,
        DateTimeOffset detectedAt)
    {
        var projectionKeys = projection.Identifiers
            .Select(id => $"{id.Kind}|{id.Value}")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (projectionKeys.Count == 0)
        {
            return Array.Empty<SecurityMasterConflict>();
        }

        var conflicts = new List<SecurityMasterConflict>();
        var compared = new HashSet<Guid>();
        foreach (var existing in universe)
        {
            if (existing.SecurityId == projection.SecurityId || !compared.Add(existing.SecurityId))
            {
                continue;
            }

            var sharesIdentifier = existing.Identifiers
                .Any(id => projectionKeys.Contains($"{id.Kind}|{id.Value}"));
            if (!sharesIdentifier)
            {
                continue;
            }

            AddFieldConflictIfDiffers(
                conflicts, projection, existing, "common.currency", projection.Currency, existing.Currency, detectedAt);
        }

        return conflicts;
    }

    private static void AddFieldConflictIfDiffers(
        List<SecurityMasterConflict> conflicts,
        SecurityProjectionRecord a,
        SecurityProjectionRecord b,
        string fieldPath,
        string? valueA,
        string? valueB,
        DateTimeOffset detectedAt)
    {
        if (string.IsNullOrWhiteSpace(valueA)
            || string.IsNullOrWhiteSpace(valueB)
            || string.Equals(valueA, valueB, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        conflicts.Add(new SecurityMasterConflict(
            ConflictId: DeterministicFieldConflictId(fieldPath, a.SecurityId, b.SecurityId),
            SecurityId: a.SecurityId,
            ConflictKind: FieldValueKind,
            FieldPath: fieldPath,
            ProviderA: ProviderOf(a),
            ValueA: valueA!,
            ProviderB: ProviderOf(b),
            ValueB: valueB!,
            DetectedAt: detectedAt,
            Status: "Open"));
    }

    private static string ProviderOf(SecurityProjectionRecord record)
        => record.Identifiers.FirstOrDefault(id => id.IsPrimary)?.Provider
           ?? record.Identifiers.FirstOrDefault()?.Provider
           ?? UnknownProvider;

    /// <summary>Stable id for a field-value conflict between two securities on a given field path.</summary>
    public static Guid DeterministicFieldConflictId(string fieldPath, Guid secA, Guid secB)
    {
        var ordered = secA.CompareTo(secB) <= 0
            ? $"field|{fieldPath}|{secA}|{secB}"
            : $"field|{fieldPath}|{secB}|{secA}";

        var bytes = System.Security.Cryptography.MD5.HashData(
            System.Text.Encoding.UTF8.GetBytes(ordered));
        return new Guid(bytes);
    }

    /// <summary>
    /// Generates a stable conflict id from the identifier tuple so that re-detection of the same
    /// conflict yields the same id regardless of which security was encountered first.
    /// </summary>
    public static Guid DeterministicConflictId(string kind, string value, Guid secA, Guid secB)
    {
        var ordered = secA.CompareTo(secB) <= 0
            ? $"{kind}|{value}|{secA}|{secB}"
            : $"{kind}|{value}|{secB}|{secA}";

        var bytes = System.Security.Cryptography.MD5.HashData(
            System.Text.Encoding.UTF8.GetBytes(ordered));
        return new Guid(bytes);
    }
}
