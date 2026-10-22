using System.Text.Json;
using System.Text.Json.Serialization;

using Meridian.Contracts.Integrity;

namespace Meridian.Contracts.SecurityMaster;

/// <summary>What an underlying link says the target is to the instrument.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SecurityUnderlyingRole>))]
public enum SecurityUnderlyingRole
{
    /// <summary>The instrument's underlying: an option's or warrant's stock, a CFD's or note's reference asset.</summary>
    Underlying,

    /// <summary>The entity whose credit the instrument references (a CDS reference entity).</summary>
    ReferenceEntity,

    /// <summary>The obligation delivered or valued on a credit event (a CDS reference obligation).</summary>
    ReferenceObligation,

    /// <summary>A rate, inflation or credit index the instrument's payments read (a floating index, an inflation index, a CDS index).</summary>
    Index,

    /// <summary>One member of a basket the instrument references, carrying its weight.</summary>
    BasketConstituent,

    /// <summary>An asset pledged under the instrument (collateral).</summary>
    Collateral
}

/// <summary>Where a link came from.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SecurityUnderlyingLinkSource>))]
public enum SecurityUnderlyingLinkSource
{
    /// <summary>Declared through the graph API by an operator or an ingest, with its own validity window.</summary>
    Declared,

    /// <summary>Derived from the instrument's own terms on every projection write; replaced whenever the terms change.</summary>
    Derived
}

/// <summary>
/// One edge of the underlying graph: <paramref name="SecurityId"/> depends on
/// <paramref name="UnderlyingSecurityId"/> in <paramref name="Role"/>. A link whose target is not
/// a Security Master record carries the <paramref name="Reference"/> text the terms named (a CDS
/// reference entity, an index name, a structured note's free-text underlying) and a null target;
/// the graph records the dependency and reports it as a gap until the reference is resolved.
/// </summary>
public sealed record SecurityUnderlyingLinkDto(
    Guid LinkId,
    Guid SecurityId,
    SecurityUnderlyingRole Role,
    Guid? UnderlyingSecurityId,
    string? Reference,
    decimal? Weight,
    DateTimeOffset ValidFrom,
    DateTimeOffset? ValidTo,
    SecurityUnderlyingLinkSource Source,
    string? Notes,
    string CreatedBy,
    DateTimeOffset CreatedAt)
{
    /// <summary>True when the link names a Security Master record as its target.</summary>
    public bool IsResolved => UnderlyingSecurityId is not null;

    /// <summary>True when the link is valid at <paramref name="asOfUtc"/>.</summary>
    public bool IsActiveAt(DateTimeOffset asOfUtc)
        => ValidFrom <= asOfUtc && (ValidTo is null || ValidTo > asOfUtc);
}

/// <summary>A request to declare a link; the link id is derived from the security, role and target so a repeat is an upsert.</summary>
public sealed record DeclareSecurityUnderlyingLinkRequest(
    Guid SecurityId,
    SecurityUnderlyingRole Role,
    Guid? UnderlyingSecurityId,
    string? Reference,
    decimal? Weight,
    DateTimeOffset ValidFrom,
    DateTimeOffset? ValidTo,
    string? Notes,
    string? CreatedBy = null);

/// <summary>
/// The graph around one security: what it depends on, what depends on it, and the references
/// that name no Security Master record yet.
/// </summary>
public sealed record SecurityUnderlyingGraphDto(
    Guid SecurityId,
    DateTimeOffset AsOfUtc,
    IReadOnlyList<SecurityUnderlyingLinkDto> Underlyings,
    IReadOnlyList<SecurityUnderlyingLinkDto> Dependents,
    IReadOnlyList<string> Gaps);

/// <summary>
/// The underlying graph store: declared links are written through it, derived links are written by
/// the projection store on every security write, and both are read from it.
/// </summary>
public interface ISecurityUnderlyingGraphStore
{
    /// <summary>The links from <paramref name="securityId"/> to its underlyings, derived and declared.</summary>
    Task<IReadOnlyList<SecurityUnderlyingLinkDto>> GetUnderlyingsAsync(Guid securityId, CancellationToken ct = default);

    /// <summary>The links from every instrument that names <paramref name="underlyingSecurityId"/> as a target.</summary>
    Task<IReadOnlyList<SecurityUnderlyingLinkDto>> GetDependentsAsync(Guid underlyingSecurityId, CancellationToken ct = default);

    /// <summary>Declares (or re-declares) a link; the returned link carries the stored id and timestamps.</summary>
    Task<SecurityUnderlyingLinkDto> DeclareAsync(DeclareSecurityUnderlyingLinkRequest request, CancellationToken ct = default);

    /// <summary>Removes a declared link and records the actor. Derived links cannot be removed; they follow the terms.</summary>
    Task<bool> RemoveDeclaredAsync(Guid securityId, Guid linkId, string actor, CancellationToken ct = default);
}

/// <summary>
/// Derives the underlying links an instrument's terms state. Each asset class's reference fields
/// are read by the key the term schema declares (and the profile-backed fallback), so the graph
/// follows the terms rather than a second copy of the field list.
/// </summary>
public static class SecurityUnderlyingLinkDeriver
{
    private static readonly Guid Namespace = Guid.Parse("4c7a1c0e-8e2d-4a7e-9c0b-6d1b2f3a4e5f");

    /// <summary>The links the record's terms state, in a stable order.</summary>
    public static IReadOnlyList<SecurityUnderlyingLinkDto> Derive(SecurityProjectionRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return Derive(record.SecurityId, record.AssetClass, record.AssetSpecificTerms, record.EffectiveFrom, record.EffectiveTo);
    }

    /// <summary>The links the detail's terms state, in a stable order.</summary>
    public static IReadOnlyList<SecurityUnderlyingLinkDto> Derive(SecurityDetailDto detail)
    {
        ArgumentNullException.ThrowIfNull(detail);
        return Derive(detail.SecurityId, detail.AssetClass, detail.AssetSpecificTerms, detail.EffectiveFrom, detail.EffectiveTo);
    }

    private static IReadOnlyList<SecurityUnderlyingLinkDto> Derive(
        Guid securityId,
        string assetClass,
        JsonElement terms,
        DateTimeOffset validFrom,
        DateTimeOffset? validTo)
    {
        if (terms.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        var sources = SecurityTermReader.TryGetProperty(terms, "profileFields", out var profileFields) && profileFields.ValueKind == JsonValueKind.Object
            ? new[] { terms, profileFields }
            : [terms];
        var links = new List<SecurityUnderlyingLinkDto>();
        var seen = new HashSet<Guid>();

        if (SecurityAssetClassCatalog.GetOrDefault(assetClass).RequiresProfileReference)
        {
            // A custom profile has dynamic fields rather than a fixed class schema. Preserve the
            // existing reference-field fallback using the reference declarations of the term schema,
            // so adding a relationship still requires one role declaration, never another key list.
            foreach (var schemaClass in SecurityAssetTermsSchema.AssetClasses)
            {
                DeriveFields(schemaClass, sources, SecurityAssetTermsSchema.Fields(schemaClass), "");
            }
        }
        else
        {
            DeriveFields(assetClass, sources, SecurityAssetTermsSchema.Fields(assetClass), "");
        }

        return links;

        void DeriveFields(
            string schemaClass,
            IReadOnlyList<JsonElement> fieldSources,
            IReadOnlyList<SecurityAssetTermField> fields,
            string parentPath)
        {
            foreach (var field in fields)
            {
                if (field.UnderlyingRole is { } role && ReadReference(fieldSources, field) is { } reference)
                {
                    if (Guid.TryParse(reference, out var referencedId))
                    {
                        if (referencedId != Guid.Empty && referencedId != securityId)
                        {
                            Add(links, seen, securityId, role, referencedId, null, null, validFrom, validTo);
                        }
                    }
                    else if (field.Type != SecurityAssetTermFieldType.Guid)
                    {
                        Add(links, seen, securityId, role, null, reference, null, validFrom, validTo);
                    }
                }

                if (field.Type is not (SecurityAssetTermFieldType.Array or SecurityAssetTermFieldType.Object))
                {
                    continue;
                }

                var fieldPath = string.IsNullOrEmpty(parentPath) ? field.Key : parentPath + "." + field.Key;
                var elementFields = SecurityAssetTermsSchema.ElementFields(schemaClass, fieldPath);
                if (elementFields.Count == 0 || ReadContainer(fieldSources, field) is not { } container)
                {
                    continue;
                }

                if (container.ValueKind == JsonValueKind.Array)
                {
                    foreach (var element in container.EnumerateArray())
                    {
                        if (element.ValueKind == JsonValueKind.Object)
                        {
                            DeriveFields(schemaClass, [element], elementFields, fieldPath);
                        }
                    }
                }
                else
                {
                    DeriveFields(schemaClass, [container], elementFields, fieldPath);
                }
            }
        }
    }

    private static string? ReadReference(IReadOnlyList<JsonElement> sources, SecurityAssetTermField field)
    {
        foreach (var source in sources)
        {
            foreach (var key in new[] { field.Key }.Concat(field.Aliases).Concat(field.UnderlyingAliases))
            {
                if (!SecurityTermReader.TryGetProperty(source, key, out var value) || value.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var reference = value.GetString()?.Trim();
                if (!string.IsNullOrWhiteSpace(reference)
                    && (field.Type != SecurityAssetTermFieldType.Guid
                        || (Guid.TryParse(reference, out var parsed) && parsed != Guid.Empty)))
                {
                    return reference;
                }
            }
        }

        return null;
    }

    private static JsonElement? ReadContainer(IReadOnlyList<JsonElement> sources, SecurityAssetTermField field)
    {
        var expectedKind = field.Type == SecurityAssetTermFieldType.Array ? JsonValueKind.Array : JsonValueKind.Object;
        foreach (var source in sources)
        {
            foreach (var key in new[] { field.Key }.Concat(field.Aliases))
            {
                if (SecurityTermReader.TryGetProperty(source, key, out var value) && value.ValueKind == expectedKind)
                {
                    return value;
                }
            }
        }

        return null;
    }

    private static void Add(
        List<SecurityUnderlyingLinkDto> links,
        HashSet<Guid> seen,
        Guid securityId,
        SecurityUnderlyingRole role,
        Guid? target,
        string? reference,
        decimal? weight,
        DateTimeOffset validFrom,
        DateTimeOffset? validTo)
    {
        var linkId = LinkId(securityId, role, target, reference, SecurityUnderlyingLinkSource.Derived);
        if (!seen.Add(linkId))
        {
            return;
        }

        links.Add(new SecurityUnderlyingLinkDto(
            linkId,
            securityId,
            role,
            target,
            reference,
            weight,
            validFrom,
            validTo,
            SecurityUnderlyingLinkSource.Derived,
            Notes: null,
            CreatedBy: "security-master.terms",
            CreatedAt: validFrom));
    }

    /// <summary>
    /// A deterministic link id from the edge's identity (security, role, target or reference text,
    /// source), so re-deriving or re-declaring the same edge upserts rather than duplicates.
    /// </summary>
    public static Guid LinkId(Guid securityId, SecurityUnderlyingRole role, Guid? target, string? reference, SecurityUnderlyingLinkSource source)
    {
        var identity = string.Join(
            "|",
            securityId.ToString("D"),
            role.ToString(),
            target?.ToString("D") ?? string.Empty,
            reference?.Trim().ToUpperInvariant() ?? string.Empty,
            source.ToString());
        var hash = Sha256Digest.ComputeBytesUtf8(Namespace.ToString("D") + "|" + identity);
        var bytes = new byte[16];
        Array.Copy(hash, bytes, 16);
        // Mark as a name-based (version 5 style) GUID so it is never mistaken for a random one.
        bytes[7] = (byte)((bytes[7] & 0x0F) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes);
    }
}
