using System.Text.Json;

namespace Meridian.ReferenceData.SecurityMaster;

/// <summary>
/// A named reference taxonomy: an allowed-value code list keyed by a stable vocabulary key that
/// both the built-in asset-class validators and the custom asset-profile field validators resolve
/// enum vocabularies from.
/// </summary>
public sealed record ReferenceTaxonomyDto(
    string Key,
    string Label,
    string? Description,
    IReadOnlyList<string> Values);

/// <summary>
/// Stable vocabulary keys shared across the two Security Master validation engines so enum
/// allowed-value lists resolve from one source instead of being duplicated as inline string arrays.
/// </summary>
public static class SecurityReferenceTaxonomyKeys
{
    public const string CollateralType = "collateral-type";
    public const string PropertyType = "property-type";
    public const string ReportingCadence = "reporting-cadence";
    public const string OptionPutCall = "option-put-call";
    public const string WarrantType = "warrant-type";
}

/// <summary>
/// Read access to the shared, data-driven Security Master reference taxonomies.
/// </summary>
public interface ISecurityReferenceTaxonomyCatalog
{
    IReadOnlyList<ReferenceTaxonomyDto> GetTaxonomies();
    bool TryGetValues(string key, out IReadOnlyList<string> values);
    IReadOnlyList<string> GetValues(string key);
}

/// <summary>
/// Shared reference taxonomies (named allowed-value code lists) consumed by both Security Master
/// validation engines. The authoritative data lives in the embedded
/// <c>SecurityMaster/Data/security-reference-taxonomies.json</c> document so the vocabularies are
/// loaded from data rather than hard-coded at each call site; an equivalent code fallback keeps
/// validation behaviour intact if the embedded resource is ever mis-wired.
/// </summary>
public sealed class SecurityReferenceTaxonomyCatalog : ISecurityReferenceTaxonomyCatalog
{
    private const string ResourceFileName = "security-reference-taxonomies.json";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IReadOnlyDictionary<string, ReferenceTaxonomyDto> _byKey;

    public SecurityReferenceTaxonomyCatalog(IEnumerable<ReferenceTaxonomyDto> taxonomies)
    {
        ArgumentNullException.ThrowIfNull(taxonomies);
        _byKey = taxonomies
            .Where(static taxonomy => !string.IsNullOrWhiteSpace(taxonomy.Key))
            .ToDictionary(static taxonomy => taxonomy.Key, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// True when <see cref="Default"/> was populated from the embedded taxonomy document rather
    /// than the in-code fallback. Surfaced so a test can pin the embedded-resource wiring.
    /// </summary>
    public static bool LoadedFromEmbeddedResource { get; private set; }

    /// <summary>Process-wide default catalog loaded from the embedded taxonomy document.</summary>
    public static SecurityReferenceTaxonomyCatalog Default { get; } = Load();

    public IReadOnlyList<ReferenceTaxonomyDto> GetTaxonomies()
        => _byKey.Values
            .OrderBy(static taxonomy => taxonomy.Key, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public bool TryGetValues(string key, out IReadOnlyList<string> values)
    {
        if (!string.IsNullOrWhiteSpace(key) && _byKey.TryGetValue(key, out var taxonomy))
        {
            values = taxonomy.Values;
            return true;
        }

        values = Array.Empty<string>();
        return false;
    }

    public IReadOnlyList<string> GetValues(string key)
        => TryGetValues(key, out var values)
            ? values
            : throw new KeyNotFoundException($"Reference taxonomy '{key}' is not registered.");

    private static SecurityReferenceTaxonomyCatalog Load()
    {
        var fromResource = TryLoadFromEmbeddedResource();
        if (fromResource is { Count: > 0 })
        {
            LoadedFromEmbeddedResource = true;
            return new SecurityReferenceTaxonomyCatalog(fromResource);
        }

        return new SecurityReferenceTaxonomyCatalog(SecurityReferenceTaxonomyFallback.Taxonomies);
    }

    private static IReadOnlyList<ReferenceTaxonomyDto>? TryLoadFromEmbeddedResource()
    {
        var assembly = typeof(SecurityReferenceTaxonomyCatalog).Assembly;
        var resourceName = Array.Find(
            assembly.GetManifestResourceNames(),
            static name => name.EndsWith(ResourceFileName, StringComparison.OrdinalIgnoreCase));
        if (resourceName is null)
        {
            return null;
        }

        try
        {
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream is null)
            {
                return null;
            }

            using var reader = new StreamReader(stream);
            var json = reader.ReadToEnd();
            return JsonSerializer.Deserialize<IReadOnlyList<ReferenceTaxonomyDto>>(json, SerializerOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>
/// In-code mirror of the embedded taxonomy document used only when the embedded resource cannot be
/// loaded, so validation vocabularies never regress even if the resource wiring drifts. Keep in
/// sync with <c>SecurityMaster/Data/security-reference-taxonomies.json</c> (the authoritative copy).
/// </summary>
internal static class SecurityReferenceTaxonomyFallback
{
    public static IReadOnlyList<ReferenceTaxonomyDto> Taxonomies { get; } =
    [
        new(
            SecurityReferenceTaxonomyKeys.CollateralType,
            "Collateral type",
            "Structured-credit pool or collateral classification.",
            ["MBS", "CMBS", "ABS", "CLO", "CDO", "Other"]),
        new(
            SecurityReferenceTaxonomyKeys.PropertyType,
            "Property type",
            "Real-estate holding property classification.",
            ["Office", "Industrial", "Retail", "Multifamily", "Hospitality", "Land", "Other"]),
        new(
            SecurityReferenceTaxonomyKeys.ReportingCadence,
            "Reporting cadence",
            "Private-vehicle reporting frequency.",
            ["Monthly", "Quarterly", "SemiAnnual", "Annual", "AdHoc"]),
        new(
            SecurityReferenceTaxonomyKeys.OptionPutCall,
            "Option put/call",
            "Listed option contract right.",
            ["Put", "Call"]),
        new(
            SecurityReferenceTaxonomyKeys.WarrantType,
            "Warrant type",
            "Warrant contract right.",
            ["Put", "Call"]),
    ];
}
