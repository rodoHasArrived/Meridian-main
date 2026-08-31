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

        // A profile's explicit source-column mapping is authoritative. Search exact
        // candidates before aliases so an implicit canonical-name alias from another
        // field cannot steal a column that the profile deliberately remapped.
        foreach (var (field, candidate, confidence) in candidates.Where(
                     static candidate => candidate.Confidence == StatementMappingConfidence.Exact))
        {
            if (string.Equals(trimmed, candidate, StringComparison.OrdinalIgnoreCase))
            {
                return new StatementColumnMapping(sourceColumn, field, confidence, ExactScore,
                    $"Column matches the profile source column '{candidate}'.");
            }
        }

        foreach (var (field, candidate, confidence) in candidates.Where(
                     static candidate => candidate.Confidence == StatementMappingConfidence.Alias))
        {
            if (string.Equals(trimmed, candidate, StringComparison.OrdinalIgnoreCase))
            {
                return new StatementColumnMapping(sourceColumn, field, confidence, AliasScore,
                    $"Column matches the declared alias '{candidate}'.");
            }
        }

        var normalizedColumn = Normalize(trimmed);

        // Keep the profile's explicit source mapping authoritative when the broker
        // varies only casing or separators. Without the same exact-before-alias
        // ordering used above, an earlier field's implicit canonical alias can
        // capture a later field's deliberately remapped source column.
        foreach (var (field, candidate, _) in candidates.Where(
                     static candidate => candidate.Confidence == StatementMappingConfidence.Exact))
        {
            if (string.Equals(normalizedColumn, Normalize(candidate), StringComparison.Ordinal))
            {
                return new StatementColumnMapping(sourceColumn, field, StatementMappingConfidence.Fuzzy, FuzzyScore,
                    $"Column matches '{candidate}' after normalizing casing and separators.");
            }
        }

        foreach (var (field, candidate, _) in candidates.Where(
                     static candidate => candidate.Confidence == StatementMappingConfidence.Alias))
        {
            if (string.Equals(normalizedColumn, Normalize(candidate), StringComparison.Ordinal))
            {
                return new StatementColumnMapping(sourceColumn, field, StatementMappingConfidence.Fuzzy, FuzzyScore,
                    $"Column matches '{candidate}' after normalizing casing and separators.");
            }
        }

        if (normalizedColumn.Length >= 3)
        {
            // For true edit-distance matches, closeness is stronger evidence than
            // provenance. The explicit source column breaks ties, but it must not
            // outrank a declared alias that is objectively closer to the input.
            var editDistanceMatch = candidates
                .Select((candidate, index) => new
                {
                    candidate.Field,
                    candidate.Candidate,
                    candidate.Confidence,
                    Index = index,
                    Normalized = Normalize(candidate.Candidate)
                })
                .Where(static candidate => candidate.Normalized.Length >= 3)
                .Select(candidate => new
                {
                    candidate.Field,
                    candidate.Candidate,
                    candidate.Confidence,
                    candidate.Index,
                    Distance = EditDistance(normalizedColumn, candidate.Normalized)
                })
                .Where(static candidate => candidate.Distance <= MaxFuzzyEditDistance)
                .OrderBy(static candidate => candidate.Distance)
                .ThenBy(static candidate =>
                    candidate.Confidence == StatementMappingConfidence.Exact ? 0 : 1)
                .ThenBy(static candidate => candidate.Index)
                .FirstOrDefault();

            if (editDistanceMatch is not null)
            {
                return new StatementColumnMapping(
                    sourceColumn,
                    editDistanceMatch.Field,
                    StatementMappingConfidence.Fuzzy,
                    FuzzyScore,
                    $"Column is within edit distance {editDistanceMatch.Distance} of '{editDistanceMatch.Candidate}'.");
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
