using System.Text.Json;
using System.Text.Json.Serialization;

namespace Meridian.Contracts.Operations;

/// <summary>Source and quality facts captured with the market observation. Null means unassessed.</summary>
public sealed record MarketDataInputEvidence(
    string? ProviderId = null,
    string? EventReference = null,
    DateTimeOffset? ObservedAt = null,
    string? DatasetId = null,
    string? BackfillRevisionId = null,
    string? ContentHash = null,
    bool? Repaired = null,
    bool? GapFilled = null,
    string? RepairReceiptId = null,
    double? QualityScore = null,
    DateTimeOffset? QualityAssessedAt = null,
    DateOnly? AssessmentFrom = null,
    DateOnly? AssessmentTo = null,
    double? QualityThreshold = null,
    string? EvidenceIssue = null);

/// <summary>Dependence warning for distinct fills, without attributing an invented P&amp;L amount.</summary>
public sealed record MarketDataQualityWarningDto(string Kind, string Message, int AffectedFillCount,
    IReadOnlyList<MarketDataInputEvidence> Evidence);

/// <summary>Immutable source snapshots retained inside existing journal metadata.</summary>
public static class MarketDataInputEvidenceTags
{
    public const string VersionTag = "marketData.input.version";
    public const string InputsTag = "marketData.input.v1";

    public static IReadOnlyList<MarketDataInputEvidence>? Freeze(IReadOnlyList<MarketDataInputEvidence>? inputs)
    {
        if (inputs is null) return null;
        // Optional provenance must not prevent the WAL or its fallback from retaining a fill
        // that has already executed. Invalid assertions become explicitly unassessed evidence.
        return Array.AsReadOnly(inputs.Select(static input =>
        {
            if (input is null) return new MarketDataInputEvidence(EvidenceIssue: "Missing source evidence.");
            if (string.Equals(input.ProviderId?.Trim(), ProviderProvenanceLabels.Unknown, StringComparison.OrdinalIgnoreCase))
                input = input with { ProviderId = null };
            var issues = new List<string>();
            if (input.EvidenceIssue is { Length: > 0 } existing) issues.Add(existing);
            if (input.QualityScore is { } score && (!double.IsFinite(score) || score < 0d || score > 1d))
            { input = input with { QualityScore = null }; issues.Add("Invalid quality score."); }
            if (input.QualityThreshold is { } threshold && (!double.IsFinite(threshold) || threshold < 0d || threshold > 1d))
            { input = input with { QualityThreshold = null }; issues.Add("Invalid quality threshold."); }
            if (input.AssessmentFrom is { } from && input.AssessmentTo is { } to && from > to)
            { input = input with { AssessmentFrom = null, AssessmentTo = null }; issues.Add("Invalid assessment range."); }
            return input with { EvidenceIssue = issues.Count > 0 ? string.Join(" ", issues) : null };
        }).Distinct().ToArray());
    }

    public static IReadOnlyDictionary<string, string> Create(IEnumerable<MarketDataInputEvidence>? inputs)
    {
        if (inputs is null) return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var frozen = Freeze(inputs?.ToArray()) ?? [];
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [VersionTag] = "1",
            [InputsTag] = JsonSerializer.Serialize(frozen.ToArray(), MarketDataInputEvidenceJsonContext.Default.MarketDataInputEvidenceArray)
        };
    }

    public static IReadOnlyDictionary<string, string> ToTags(IEnumerable<MarketDataInputEvidence>? inputs) => Create(inputs);

    /// <summary>A symbol/range assessment cannot certify an observation outside that retained window.</summary>
    public static MarketDataInputEvidence ForObservation(MarketDataInputEvidence input, DateOnly observedDate)
        => input.QualityScore is not null && (input.AssessmentFrom is not { } from
            || input.AssessmentTo is not { } to || observedDate < from || observedDate > to)
            ? input with { QualityScore = null, EvidenceIssue = "Quality assessment does not cover the observed date." }
            : input;

    /// <summary>Missing, unsupported or malformed evidence remains unassessed.</summary>
    public static IReadOnlyList<MarketDataInputEvidence> Read(IReadOnlyDictionary<string, string>? tags)
    {
        if (tags?.TryGetValue(VersionTag, out var version) != true || version != "1"
            || !tags.TryGetValue(InputsTag, out var json)) return [];
        try
        {
            return Freeze(JsonSerializer.Deserialize(json, MarketDataInputEvidenceJsonContext.Default.MarketDataInputEvidenceArray)) ?? [];
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException) { return []; }
    }

    /// <summary>The caller supplies one source population per logical fill, deduplicating cost journals.</summary>
    public static IReadOnlyList<MarketDataQualityWarningDto> BuildWarnings(
        IEnumerable<IReadOnlyList<MarketDataInputEvidence>?> logicalFills, double minimumQualityScore = .8d)
    {
        if (!double.IsFinite(minimumQualityScore) || minimumQualityScore < 0d || minimumQualityScore > 1d)
            minimumQualityScore = .8d;
        var fills = logicalFills.Select(static inputs => Freeze(inputs) ?? []).ToArray();
        var warnings = new List<MarketDataQualityWarningDto>();
        Add("Repaired", "Some fills may depend on repaired market data.", source => source.Repaired == true);
        Add("GapFilled", "Some fills may depend on gap-filled market data.", source => source.GapFilled == true);
        Add("LowQuality", "Some fills may depend on market data below the recorded quality threshold.",
            source => source.QualityScore is { } score && score < (source.QualityThreshold ?? minimumQualityScore));
        var unknown = fills.Where(static inputs => inputs.Count == 0 || inputs.Any(static source =>
            string.IsNullOrWhiteSpace(source.ProviderId) || source.Repaired is null || source.GapFilled is null
            || source.QualityScore is null || source.QualityAssessedAt is null || source.QualityThreshold is null || source.AssessmentFrom is null
            || source.AssessmentTo is null || source.EvidenceIssue is not null)).ToArray();
        if (unknown.Length > 0)
            warnings.Add(new("Unknown", "Source or quality coverage is unassessed for some fills.", unknown.Length,
                unknown.SelectMany(static inputs => inputs).Distinct().ToArray()));
        return warnings.AsReadOnly();

        void Add(string kind, string message, Func<MarketDataInputEvidence, bool> predicate)
        {
            var affected = fills.Where(inputs => inputs.Any(predicate)).ToArray();
            if (affected.Length > 0)
                warnings.Add(new(kind, message, affected.Length,
                    affected.SelectMany(static inputs => inputs).Where(predicate).Distinct().ToArray()));
        }
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(MarketDataInputEvidence[]))]
public partial class MarketDataInputEvidenceJsonContext : JsonSerializerContext;
