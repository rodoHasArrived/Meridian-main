using static Meridian.Contracts.Text.TextPrimitives;

namespace Meridian.Execution.Services;

internal static class ReconciliationSetComparer
{
    public static ReconciliationSetComparison<TLocal, TExternal> Compare<TLocal, TExternal>(
        IEnumerable<TLocal> localItems,
        IEnumerable<TExternal> externalItems,
        Func<TLocal, string?> localKeySelector,
        Func<TExternal, string?> externalKeySelector,
        IEqualityComparer<string>? comparer = null)
    {
        ArgumentNullException.ThrowIfNull(localItems);
        ArgumentNullException.ThrowIfNull(externalItems);
        ArgumentNullException.ThrowIfNull(localKeySelector);
        ArgumentNullException.ThrowIfNull(externalKeySelector);

        comparer ??= StringComparer.OrdinalIgnoreCase;
        var localByKey = localItems
            .Select(item => new KeyValuePair<string?, TLocal>(NormalizeOptional(localKeySelector(item)), item))
            .Where(static pair => pair.Key is not null)
            .ToDictionary(pair => pair.Key!, pair => pair.Value, comparer);

        var matchedLocalKeys = new HashSet<string>(comparer);
        var matches = new List<ReconciliationSetMatch<TLocal, TExternal>>();
        var missingLocal = new List<TExternal>();

        foreach (var externalItem in externalItems)
        {
            var externalKey = NormalizeOptional(externalKeySelector(externalItem));
            if (externalKey is not null && localByKey.TryGetValue(externalKey, out var localItem))
            {
                matchedLocalKeys.Add(externalKey);
                matches.Add(new ReconciliationSetMatch<TLocal, TExternal>(
                    Key: externalKey,
                    Local: localItem,
                    External: externalItem));
                continue;
            }

            missingLocal.Add(externalItem);
        }

        var missingExternal = localByKey
            .Where(pair => !matchedLocalKeys.Contains(pair.Key))
            .Select(static pair => pair.Value)
            .ToArray();

        return new ReconciliationSetComparison<TLocal, TExternal>(
            Matches: matches,
            MissingLocal: missingLocal,
            MissingExternal: missingExternal);
    }
}

internal sealed record ReconciliationSetComparison<TLocal, TExternal>(
    IReadOnlyList<ReconciliationSetMatch<TLocal, TExternal>> Matches,
    IReadOnlyList<TExternal> MissingLocal,
    IReadOnlyList<TLocal> MissingExternal);

internal sealed record ReconciliationSetMatch<TLocal, TExternal>(
    string Key,
    TLocal Local,
    TExternal External);
