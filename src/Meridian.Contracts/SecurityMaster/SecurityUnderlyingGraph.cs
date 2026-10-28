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
/// the projection store on every security write, and both are read from it. Declared links are
/// operator-entered state, so every declaration, amendment and removal is recorded in the Security
/// Master governance event log with its actor, in the same transaction as the change.
/// </summary>
public interface ISecurityUnderlyingGraphStore
{
    /// <summary>The links from <paramref name="securityId"/> to its underlyings, derived and declared.</summary>
    Task<IReadOnlyList<SecurityUnderlyingLinkDto>> GetUnderlyingsAsync(Guid securityId, CancellationToken ct = default);

    /// <summary>The links from every instrument that names <paramref name="underlyingSecurityId"/> as a target.</summary>
    Task<IReadOnlyList<SecurityUnderlyingLinkDto>> GetDependentsAsync(Guid underlyingSecurityId, CancellationToken ct = default);

    /// <summary>
    /// Declares (or re-declares) a link; the returned link carries the stored id and timestamps. A
    /// new edge, or a change to an existing one's weight, window or notes, is recorded as a
    /// governance event naming <see cref="DeclareSecurityUnderlyingLinkRequest.CreatedBy"/> with the
    /// state it replaced. Re-declaring an edge unchanged writes nothing and returns it as stored.
    /// </summary>
    Task<SecurityUnderlyingLinkDto> DeclareAsync(DeclareSecurityUnderlyingLinkRequest request, CancellationToken ct = default);

    /// <summary>
    /// Removes a declared link and records the removal, with the removed link and
    /// <paramref name="removedBy"/>, as a governance event. Derived links cannot be removed; they
    /// follow the terms.
    /// </summary>
    Task<bool> RemoveDeclaredAsync(Guid securityId, Guid linkId, string removedBy, CancellationToken ct = default);
}

/// <summary>
/// Derives the underlying links an instrument's terms state, from the roles the term schema
/// declares on its fields (<see cref="SecurityAssetTermField.UnderlyingRole"/>): every role-marked
/// field of the record's class, under its key or any alias, and every role-marked field of each
/// element of an array or nested object the class declares (a swap's legs, an equity's conversion
/// terms). Each field yields its own link, so a bond with a floating and an inflation index links
/// both. The deriver keeps no key list of its own; a reference field links the moment its role is
/// declared in the schema.
/// </summary>
/// <remarks>
/// Profile-backed terms carry their dynamic fields under <c>profileFields</c>, governed by a
/// profile version the schema does not describe. Those are read against every spelling the schema
/// marks on any class (top-level or in an element shape), in the role it marks it, so a profile
/// field spelled like a declared reference links the same way.
/// </remarks>
public static class SecurityUnderlyingLinkDeriver
{
    private static readonly Guid Namespace = Guid.Parse("4c7a1c0e-8e2d-4a7e-9c0b-6d1b2f3a4e5f");

    private const string ProfileFieldsKey = "profileFields";

    /// <summary>
    /// Every spelling (key or alias) of every role-marked field in the schema, top-level or in an
    /// element shape, once per spelling and role, for reading profile fields.
    /// </summary>
    private static readonly IReadOnlyList<SecurityAssetTermField> ProfileReferenceFields = SecurityAssetTermsSchema.AssetClasses
        .SelectMany(static assetClass => SecurityAssetTermsSchema.Fields(assetClass)
            .SelectMany(field => SecurityAssetTermsSchema.ElementFields(assetClass, field.Key).Prepend(field)))
        .Where(static field => field.UnderlyingRole is not null)
        .SelectMany(static field => KeyAndAliases(field).Select(spelling => field with { Key = spelling, Aliases = [] }))
        .DistinctBy(static field => (field.Key.ToUpperInvariant(), field.UnderlyingRole))
        .ToArray();

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

        var hasProfileFields = SecurityTermReader.TryGetProperty(terms, ProfileFieldsKey, out var profileFields)
            && profileFields.ValueKind == JsonValueKind.Object;
        JsonElement[] sources = hasProfileFields ? [terms, profileFields] : [terms];

        var links = new List<SecurityUnderlyingLinkDto>();
        var seen = new HashSet<Guid>();
        var canonicalClass = SecurityAssetClassCatalog.GetOrDefault(assetClass).AssetClass;

        foreach (var field in SecurityAssetTermsSchema.Fields(canonicalClass))
        {
            if (field.UnderlyingRole is { } role)
            {
                AddReference(links, seen, securityId, role, field, sources, validFrom, validTo);
            }

            var elementFields = SecurityAssetTermsSchema.ElementFields(canonicalClass, field.Key)
                .Where(static element => element.UnderlyingRole is not null)
                .ToArray();
            if (elementFields.Length == 0)
            {
                continue;
            }

            foreach (var element in Elements(sources, field))
            {
                foreach (var elementField in elementFields)
                {
                    AddReference(links, seen, securityId, elementField.UnderlyingRole!.Value, elementField, [element], validFrom, validTo);
                }
            }
        }

        if (hasProfileFields)
        {
            foreach (var field in ProfileReferenceFields)
            {
                AddReference(links, seen, securityId, field.UnderlyingRole!.Value, field, [profileFields], validFrom, validTo);
            }
        }

        return links;
    }

    /// <summary>
    /// The objects a container field holds: each object element of an array, or the nested object
    /// itself, read from the first source and spelling that carries the container.
    /// </summary>
    private static IEnumerable<JsonElement> Elements(IReadOnlyList<JsonElement> sources, SecurityAssetTermField container)
    {
        foreach (var source in sources)
        {
            foreach (var key in KeyAndAliases(container))
            {
                if (!SecurityTermReader.TryGetProperty(source, key, out var value))
                {
                    continue;
                }

                if (value.ValueKind == JsonValueKind.Array)
                {
                    return value.EnumerateArray().Where(static item => item.ValueKind == JsonValueKind.Object).ToArray();
                }

                if (value.ValueKind == JsonValueKind.Object)
                {
                    return [value];
                }
            }
        }

        return [];
    }

    /// <summary>
    /// Adds the link one role-marked field states. A Guid-typed field links only to the security
    /// id it carries; any other field resolves when its value is a security id and is otherwise
    /// recorded as reference text. A self-reference is never a link.
    /// </summary>
    private static void AddReference(
        List<SecurityUnderlyingLinkDto> links,
        HashSet<Guid> seen,
        Guid securityId,
        SecurityUnderlyingRole role,
        SecurityAssetTermField field,
        IReadOnlyList<JsonElement> sources,
        DateTimeOffset validFrom,
        DateTimeOffset? validTo)
    {
        var value = SecurityTermReader.ReadString(sources, KeyAndAliases(field))?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        if (Guid.TryParse(value, out var target) && target != Guid.Empty)
        {
            if (target != securityId)
            {
                Add(links, seen, securityId, role, target, null, null, validFrom, validTo);
            }

            return;
        }

        if (field.Type != SecurityAssetTermFieldType.Guid)
        {
            Add(links, seen, securityId, role, null, value, null, validFrom, validTo);
        }
    }

    private static string[] KeyAndAliases(SecurityAssetTermField field)
        => [field.Key, .. field.Aliases];

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
