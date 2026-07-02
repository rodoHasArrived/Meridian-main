using System.Text;

namespace Meridian.FinancialOperations.Reconciliation.Connectors;

/// <summary>
/// Scores how confidently each detected source column maps to a canonical field for a
/// given profile document: exact source-column match, declared alias, fuzzy match
/// (normalized tokens or small edit distance), or unmapped. Powers the per-column
/// confidence chips in the import preview and profile suggestion ranking.
/// </summary>
public static class StatementColumnConfidenceScorer
{
    private const decimal ExactScore = 1.0m;
    private const decimal AliasScore = 0.9m;
    private const decimal FuzzyScore = 0.6m;
    private const int MaxFuzzyEditDistance = 2;

    public static IReadOnlyList<StatementColumnMapping> MapColumns(
        IReadOnlyList<string> detectedColumns,
        StatementMappingProfileDocument profile)
    {
        var candidates = BuildCandidates(profile);
        var scored = detectedColumns.Select(column => ScoreColumn(column, candidates)).ToArray();

        // Assign best-score-first so a canonical field is claimed by its strongest column
        // and never twice; weaker duplicates fall back to unmapped with an explanation.
        var assignedFields = new HashSet<StatementCanonicalField>();
        var result = new StatementColumnMapping[scored.Length];
        foreach (var index in Enumerable.Range(0, scored.Length).OrderByDescending(i => scored[i].Score))
        {
            var mapping = scored[index];
            if (mapping.CanonicalField is { } field && !assignedFields.Add(field))
            {
                mapping = mapping with
                {
                    CanonicalField = null,
                    Confidence = StatementMappingConfidence.Unmapped,
                    Score = 0m,
                    Rationale = $"Canonical field '{field}' is already mapped by a higher-confidence column."
                };
            }

            result[index] = mapping;
        }

        return result;
    }

    /// <summary>
    /// Mean mapping score across a profile's required fields for the detected columns;
    /// used to rank profile suggestions in the import preview.
    /// </summary>
    public static decimal ScoreProfile(IReadOnlyList<string> detectedColumns, StatementMappingProfileDocument profile)
    {
        var requiredFields = profile.Fields.Where(static field => field.Required).ToArray();
        if (requiredFields.Length == 0)
        {
            requiredFields = profile.Fields.ToArray();
        }

        if (requiredFields.Length == 0)
        {
            return 0m;
        }

        var mappings = MapColumns(detectedColumns, profile);
        var total = 0m;
        foreach (var field in requiredFields)
        {
            if (!Enum.TryParse<StatementCanonicalField>(field.CanonicalField, ignoreCase: true, out var canonical))
            {
                continue;
            }

            var best = mappings.FirstOrDefault(mapping => mapping.CanonicalField == canonical);
            total += best?.Score ?? 0m;
        }

        return Math.Round(total / requiredFields.Length, 4);
    }

    private static StatementColumnMapping ScoreColumn(
        string sourceColumn,
        IReadOnlyList<(StatementCanonicalField Field, string Candidate, StatementMappingConfidence Confidence)> candidates)
    {
        var trimmed = sourceColumn.Trim();

        foreach (var (field, candidate, confidence) in candidates)
        {
            if (string.Equals(trimmed, candidate, StringComparison.OrdinalIgnoreCase))
            {
                return confidence == StatementMappingConfidence.Exact
                    ? new StatementColumnMapping(sourceColumn, field, StatementMappingConfidence.Exact, ExactScore,
                        $"Column matches the profile source column '{candidate}'.")
                    : new StatementColumnMapping(sourceColumn, field, StatementMappingConfidence.Alias, AliasScore,
                        $"Column matches the declared alias '{candidate}'.");
            }
        }

        var normalizedColumn = Normalize(trimmed);
        foreach (var (field, candidate, _) in candidates)
        {
            if (string.Equals(normalizedColumn, Normalize(candidate), StringComparison.Ordinal))
            {
                return new StatementColumnMapping(sourceColumn, field, StatementMappingConfidence.Fuzzy, FuzzyScore,
                    $"Column matches '{candidate}' after normalizing casing and separators.");
            }
        }

        foreach (var (field, candidate, _) in candidates)
        {
            var normalizedCandidate = Normalize(candidate);
            if (normalizedColumn.Length >= 3
                && normalizedCandidate.Length >= 3
                && EditDistance(normalizedColumn, normalizedCandidate) <= MaxFuzzyEditDistance)
            {
                return new StatementColumnMapping(sourceColumn, field, StatementMappingConfidence.Fuzzy, FuzzyScore,
                    $"Column is within edit distance {MaxFuzzyEditDistance} of '{candidate}'.");
            }
        }

        return new StatementColumnMapping(sourceColumn, null, StatementMappingConfidence.Unmapped, 0m,
            "Column does not match any mapped field or alias in the selected profile.");
    }

    private static List<(StatementCanonicalField Field, string Candidate, StatementMappingConfidence Confidence)> BuildCandidates(
        StatementMappingProfileDocument profile)
    {
        var candidates = new List<(StatementCanonicalField, string, StatementMappingConfidence)>();
        foreach (var field in profile.Fields)
        {
            if (!Enum.TryParse<StatementCanonicalField>(field.CanonicalField, ignoreCase: true, out var canonical))
            {
                continue;
            }

            candidates.Add((canonical, field.SourceColumn, StatementMappingConfidence.Exact));
            foreach (var alias in field.Aliases ?? [])
            {
                if (!string.IsNullOrWhiteSpace(alias))
                {
                    candidates.Add((canonical, alias, StatementMappingConfidence.Alias));
                }
            }

            // The canonical field name itself acts as an implicit alias so near-canonical
            // exports (e.g. "Trade Date" vs TradeDate) still land with alias confidence.
            candidates.Add((canonical, canonical.ToString(), StatementMappingConfidence.Alias));
        }

        return candidates;
    }

    private static string Normalize(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
            }
        }

        return builder.ToString();
    }

    private static int EditDistance(string left, string right)
    {
        if (Math.Abs(left.Length - right.Length) > MaxFuzzyEditDistance)
        {
            return MaxFuzzyEditDistance + 1;
        }

        var previous = new int[right.Length + 1];
        var current = new int[right.Length + 1];
        for (var j = 0; j <= right.Length; j++)
        {
            previous[j] = j;
        }

        for (var i = 1; i <= left.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= right.Length; j++)
            {
                var substitution = left[i - 1] == right[j - 1] ? 0 : 1;
                current[j] = Math.Min(
                    Math.Min(current[j - 1] + 1, previous[j] + 1),
                    previous[j - 1] + substitution);
            }

            (previous, current) = (current, previous);
        }

        return previous[right.Length];
    }
}
