namespace Meridian.Application.Backfill;

internal static class BackfillSymbolNormalizer
{
    public static string[] Normalize(IReadOnlyList<string>? symbols)
    {
        if (symbols is null || symbols.Count == 0)
        {
            return [];
        }

        var normalized = new List<string>(symbols.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var symbol in symbols)
        {
            if (string.IsNullOrWhiteSpace(symbol))
            {
                continue;
            }

            var trimmed = symbol.Trim();
            if (seen.Add(trimmed))
            {
                normalized.Add(trimmed);
            }
        }

        return normalized.ToArray();
    }
}
