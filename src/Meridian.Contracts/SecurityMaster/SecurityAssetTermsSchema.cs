using System.Text.Json;
using System.Text.Json.Nodes;

namespace Meridian.Contracts.SecurityMaster;

/// <summary>
/// The JSON value shape a Security Master asset-specific-term field takes in the flat
/// (<see cref="AssetSpecificTermsSchema.Legacy"/>) payload. This is the codec-level type, distinct
/// from the operator-facing <see cref="SecurityAssetProfileFieldTypeDto"/> used by custom profiles.
/// </summary>
public enum SecurityAssetTermFieldType
{
    /// <summary>A JSON string.</summary>
    String,

    /// <summary>A JSON number read as <see cref="decimal"/>.</summary>
    Decimal,

    /// <summary>A JSON number read as <see cref="int"/>.</summary>
    Integer,

    /// <summary>A JSON boolean.</summary>
    Boolean,

    /// <summary>A JSON string parsed as a <see cref="System.DateOnly"/> (or an ISO timestamp).</summary>
    Date,

    /// <summary>A JSON string parsed as a <see cref="System.Guid"/>.</summary>
    Guid,

    /// <summary>A JSON array.</summary>
    Array,

    /// <summary>A nested JSON object.</summary>
    Object
}

/// <summary>
/// The designated vocabulary member that absorbs a value outside the declared vocabulary:
/// <c>classification = "Other"</c> pairs with a <see cref="LabelKey"/> (<c>otherClassification</c>)
/// carrying the raw label verbatim, so an unrecognized value survives the codec loop under a
/// canonical discriminant instead of vanishing.
/// <para>The escape is lossless only while the record carries none of the
/// <see cref="DependentKeys"/>. Those are payload the IN-vocabulary cases own — an equity's
/// <c>preferredTerms</c> hangs off <c>classification = "Preferred"</c> — and the escape decode has
/// nowhere to reattach them, so it would silently delete them.</para>
/// </summary>
public sealed record SecurityAssetTermVocabularyEscape(
    string Value,
    string LabelKey,
    IReadOnlyList<string> DependentKeys);

/// <summary>
/// One declarative field in the per-asset-class asset-specific-terms contract: the canonical JSON
/// key, its value type, whether the serialize side always emits it, any legacy flat key aliases
/// a tolerant reader should also accept, and — for discriminant strings — the closed vocabulary its
/// value must come from.
/// </summary>
/// <remarks>
/// What the codec does with a value OUTSIDE the vocabulary decides whether an already-stored odd
/// value can be re-serialized safely, which is a different question from whether a write may assert
/// one. A discriminant is exactly one of three things, and every guard that re-serializes a record
/// has to tell them apart:
/// <list type="number">
/// <item><see cref="CarriesUndeclaredValueVerbatim"/> — the domain holds the value as a plain string
/// and writes it straight back (an option's <c>putCall</c>). The vocabulary constrains what a write
/// may assert; re-serializing an odd value already in the row loses nothing.</item>
/// <item><see cref="Escape"/> — the value is routed through a canonical escape member that preserves
/// the raw label in a companion key, lossless unless the escape's dependent blocks are present.</item>
/// <item>Neither — the value is DROPPED. An unknown <c>exerciseStyle</c> reads as None; an unknown
/// <c>couponType</c> collapses to <c>Fixed</c> and takes the structure-specific fields with it.
/// Re-serializing is always destructive.</item>
/// </list>
/// </remarks>
/// <summary>What a term field describes.</summary>
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<SecurityAssetTermRole>))]
public enum SecurityAssetTermRole
{
    /// <summary>The instrument's contractual economics.</summary>
    Economic,

    /// <summary>An operational or vendor reference (pricing source, data feed) carried with the terms.</summary>
    Operational
}

public sealed record SecurityAssetTermField(
    string Key,
    SecurityAssetTermFieldType Type,
    bool Required,
    IReadOnlyList<string> Aliases,
    IReadOnlyList<string> AllowedValues,
    SecurityAssetTermVocabularyEscape? Escape = null,
    bool CarriesUndeclaredValueVerbatim = false)
{
    /// <summary>False for readable calculation companions whose authoring has not been activated.</summary>
    public bool AuthoringEnabled { get; init; } = true;

    public SecurityAssetTermField ReadOnly() => this with { AuthoringEnabled = false };

    /// <summary>
    /// True when the field carries a CLOSED vocabulary: its value selects a domain case, and a value
    /// outside <see cref="AllowedValues"/> cannot be decoded back into the one it names. Fields whose
    /// domain type has an open "other" case (a bond <c>subclass</c>, a <c>paymentFrequency</c>) are
    /// deliberately not discriminants — they round-trip any label losslessly, so constraining them
    /// would reject values the domain accepts today.
    /// </summary>
    public bool IsDiscriminant => AllowedValues.Count > 0;

    /// <summary>
    /// True when <paramref name="value"/> is one of the declared vocabulary members, or the field
    /// declares no vocabulary at all. The comparison is ORDINAL and case-sensitive because the
    /// codecs are: the serializer emits one exact spelling per case and the deserializer switches on
    /// it, so <c>"fixed"</c> is as undecodable as <c>"Variable"</c>.
    /// </summary>
    public bool Allows(string? value)
        => !IsDiscriminant
           || (value is not null && AllowedValues.Contains(value, StringComparer.Ordinal));

    /// <summary>
    /// The shared rejection reason for a value outside this field's vocabulary, naming the declared
    /// members and the escape when one exists. Both the operator edit surface and the write-mode
    /// codec reject through this text so a rejected value reads the same wherever it is caught.
    /// </summary>
    public string DescribeUndeclaredValue(string assetClass, string? value)
    {
        var reason =
            $"Value '{value}' is not a declared '{Key}' value for asset class '{assetClass}'. " +
            $"Declared values: {string.Join(", ", AllowedValues)}.";

        return Escape is null
            ? reason
            : reason + $" Use '{Escape.Value}' with '{Escape.LabelKey}' to carry a value outside the vocabulary.";
    }

    /// <summary>A field the serialize side always emits (a non-optional domain field).</summary>
    public static SecurityAssetTermField Req(string key, SecurityAssetTermFieldType type, params string[] aliases)
        => new(key, type, Required: true, aliases, AllowedValues: []);

    /// <summary>A field emitted only when its optional domain value is present.</summary>
    public static SecurityAssetTermField Opt(string key, SecurityAssetTermFieldType type, params string[] aliases)
        => new(key, type, Required: false, aliases, AllowedValues: []);

    /// <summary>A required discriminant string constrained to a closed vocabulary.</summary>
    public static SecurityAssetTermField ReqOneOf(string key, params string[] allowedValues)
        => new(key, SecurityAssetTermFieldType.String, Required: true, Aliases: [], AllowedValues: allowedValues);

    /// <summary>An optional discriminant string constrained to a closed vocabulary.</summary>
    public static SecurityAssetTermField OptOneOf(string key, params string[] allowedValues)
        => new(key, SecurityAssetTermFieldType.String, Required: false, Aliases: [], AllowedValues: allowedValues);

    /// <summary>
    /// Declares the vocabulary member that absorbs an out-of-vocabulary label, the key carrying that
    /// raw label, and the keys the escape decode cannot reattach. See
    /// <see cref="SecurityAssetTermVocabularyEscape"/>.
    /// </summary>
    public SecurityAssetTermField WithEscape(string value, string labelKey, params string[] dependentKeys)
        => this with { Escape = new SecurityAssetTermVocabularyEscape(value, labelKey, dependentKeys) };

    /// <summary>
    /// Declares that the codec carries an out-of-vocabulary value VERBATIM in this same key, so the
    /// vocabulary is a write-time constraint only and never a reason to refuse re-serializing a
    /// record that already holds one. See <see cref="CarriesUndeclaredValueVerbatim"/>.
    /// </summary>
    public SecurityAssetTermField WithVerbatimUndeclaredValue()
        => this with { CarriesUndeclaredValueVerbatim = true };

    /// <summary>
    /// The typed convention this string field carries, or <see cref="SecurityTermConventionKind.None"/>.
    /// Unlike a closed vocabulary, a convention accepts every spelling its parser recognizes and
    /// preserves its meaning; day counts serialize as canonical market labels, while other conventions
    /// retain their original spelling. It rejects values the parser cannot type.
    /// </summary>
    public SecurityTermConventionKind Convention { get; init; }

    /// <summary>Declares the typed convention this string field carries.</summary>
    public SecurityAssetTermField WithConvention(SecurityTermConventionKind convention)
        => this with { Convention = convention };

    /// <summary>
    /// Whether the field is part of the instrument's economics or an operational reference (a pricing
    /// source, a vendor feed). Operational fields are kept beside the economics for compatibility but
    /// never feed cash-flow, accounting or economic-definition math.
    /// </summary>
    public SecurityAssetTermRole Role { get; init; } = SecurityAssetTermRole.Economic;

    /// <summary>Declares the field an operational reference rather than an economic term.</summary>
    public SecurityAssetTermField AsOperational()
        => this with { Role = SecurityAssetTermRole.Operational };

    /// <summary>
    /// The role the field's value plays in the underlying graph, or <see langword="null"/> when the
    /// field names nothing the instrument depends on. <see cref="SecurityUnderlyingLinkDeriver"/>
    /// builds the derived graph from these declarations alone: a value that is a security id links
    /// to that record, any other text is recorded as an unresolved reference.
    /// </summary>
    public SecurityUnderlyingRole? UnderlyingRole { get; init; }

    /// <summary>Declares the field a reference the underlying graph links in <paramref name="role"/>.</summary>
    public SecurityAssetTermField LinksUnderlying(SecurityUnderlyingRole role)
        => this with { UnderlyingRole = role };

    /// <summary>
    /// The asset-class terms version (<see cref="SecurityAssetClassDescriptor.TermsVersion"/>) that
    /// introduced this field; 1 for every field the class carried before per-class versioning. A
    /// payload stamped with an earlier version never carries it. The committed manifests under
    /// <c>tests/fixtures/security-master/terms-versions</c> are checked against it: a field must first
    /// appear in the manifest of this version. A field above the class's write version is readable
    /// but not writable (<see cref="SecurityAssetClassDescriptor.ReadsTermsThrough"/>): a node decodes
    /// it from payloads stamped at or above this version and refuses it on every write path. A class
    /// may read several versions ahead of what it writes, each introduced by its own top-level fields
    /// (Bond: <c>scheduleConventions</c> at 2, <c>callSchedule</c> and <c>putSchedule</c> at 3). The
    /// members of a container introduced above version 1 are frozen with it: a later member needs a
    /// new container key.
    /// </summary>
    public int SinceTermsVersion { get; init; } = SecurityTermsEnvelope.DefaultAssetClassTermsVersion;

    /// <summary>
    /// Declares the terms version that introduced the field. Like <see cref="WithConvention"/> and
    /// <see cref="AsOperational"/> it is a single-member <c>with</c>, so it composes with every other
    /// modifier in any order.
    /// </summary>
    public SecurityAssetTermField Since(int termsVersion)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(termsVersion, SecurityTermsEnvelope.DefaultAssetClassTermsVersion);
        return this with { SinceTermsVersion = termsVersion };
    }

    /// <summary>
    /// The <see cref="AbsentReadDefault"/> of the CustomAsset <c>customProfileId</c>: a stored row
    /// without it is not given a value, it is salvaged whole as <c>OtherSecurity</c>.
    /// </summary>
    public const string EnvelopeLessSalvage = "envelope-less row salvaged as OtherSecurity";

    /// <summary>
    /// What the codec reads in place of this <see cref="Required"/> field when a stored row omits
    /// it (<c>"false"</c> for the required booleans, <c>"Corporate"</c> for a bond's
    /// <c>subclass</c>, <see cref="EnvelopeLessSalvage"/> for a profile id), or
    /// <see langword="null"/> when its absence refuses the read. <see cref="Required"/> describes
    /// the WRITTEN form — the writer always emits the key, filling this default when a submission
    /// omits it — so this flag is what separates a required field a read may tolerate from one a
    /// read refuses (<see cref="RefusesAbsenceOnRead"/>). A field introduced above version 1 must
    /// declare one, because rows stamped before that version never carry it.
    /// </summary>
    public string? AbsentReadDefault { get; init; }

    /// <summary>Declares the value the codec reads when a stored row omits this field.</summary>
    public SecurityAssetTermField ReadsAbsentAs(string defaultValue)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultValue);
        return this with { AbsentReadDefault = defaultValue };
    }

    /// <summary>
    /// True when reconstructing a stored row without this field refuses the read: the field is
    /// <see cref="Required"/> and declares no <see cref="AbsentReadDefault"/>.
    /// </summary>
    public bool RefusesAbsenceOnRead => Required && AbsentReadDefault is null;

    /// <summary>
    /// True when <paramref name="value"/> satisfies this field's convention (or it declares none).
    /// Blank values are left to the required/optional rules.
    /// </summary>
    public bool SatisfiesConvention(string? value)
        => Convention == SecurityTermConventionKind.None
           || string.IsNullOrWhiteSpace(value)
           || SecurityTermConventions.IsRecognized(Convention, value);
}

/// <summary>
/// The single declarative source of truth for the flat, per-asset-class asset-specific-terms field
/// contract. Historically the same field/type table was hand-maintained three times — the F# serialize
/// side (<c>Interop.SecurityMaster.assetSpecificTermsJson</c>), the C# deserialize side
/// (<c>SecurityMasterMapping.ToSecurityKind</c>), and the relational projection decoders in the Postgres
/// store — which let them silently drift (e.g. the projection store reading a nested <c>coupon</c> object
/// the serializer never wrote, so bond coupon columns landed null). This table names each class's fields
/// once so those codec surfaces can be validated against a single contract instead of against each other.
/// It mirrors the data-driven pattern already proven by <c>AssetClassValidatorRegistry</c>.
/// </summary>
/// <remarks>
/// Keys and types are taken from the authoritative serialize contract (the F# <c>SecurityKind</c>
/// term records). Fields carrying nested/collection shapes are typed <see cref="SecurityAssetTermFieldType.Array"/>
/// or <see cref="SecurityAssetTermFieldType.Object"/>. Registered inner shapes are exposed through
/// <see cref="ElementFields"/> so codec guards can check the contents as well as the container.
/// <para>A field declared with <c>ReqOneOf</c>/<c>OptOneOf</c> carries a CLOSED vocabulary that the
/// write-mode codec and the operator edit surface enforce. Only the string fields whose domain type
/// genuinely cannot round-trip an unlisted value get one: <c>classification</c>, <c>putCall</c>,
/// <c>exerciseStyle</c>, and <c>couponType</c>. Deliberately NOT vocabularies are the labels whose
/// domain type has an open "other" case that preserves the raw string — <c>votingRightsCat</c>
/// (<c>OtherVotingRights</c>), <c>subclass</c> (<c>BondSubclass.Other</c>), <c>paymentFrequency</c>
/// (<c>OtherFrequency</c>), and <c>distributionPolicy</c> (<c>OtherDistribution</c>) — plus the
/// free-text labels the domain carries verbatim (<c>seniority</c>, <c>depositType</c>, and the
/// rest). Constraining those would reject values that persist and read back losslessly today.
/// Between the two sit the typed CONVENTIONS (<c>dayCount</c>, <c>settlementType</c>,
/// <c>paymentFrequency</c>, <c>resetFrequency</c>, <c>sweepFrequency</c>): day counts use the shared typed enum and
/// canonical market labels; the other conventions retain their spelling. Every stored value still
/// reads, but a write must name a spelling the convention's
/// parser (<see cref="SecurityTermConventions"/>) can type, so no value is accepted that every
/// cash-flow and accrual consumer would then read as an unknown cadence or basis.</para>
/// </remarks>
public static class SecurityAssetTermsSchema
{
    // Modules retain each class's declaration order. Required scalars that refuse absence are
    // checked in that order, preserving the historical first read refusal on incomplete rows.
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<SecurityAssetTermField>> FieldsByAssetClass =
        SecurityAssetClassModules.All.OrderBy(static module => module.SchemaOrder)
            .ToDictionary(static module => module.Descriptor.AssetClass, static module => module.Fields, StringComparer.OrdinalIgnoreCase);

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<SecurityAssetTermField>> ElementFieldsByPath =
        SecurityAssetClassModules.All
            .SelectMany(static module => module.ElementShapes.Select(shape => (module.Descriptor.AssetClass, Shape: shape)))
            .OrderBy(static contribution => contribution.Shape.NestedOrder)
            .ToDictionary(static contribution => $"{contribution.AssetClass}.{contribution.Shape.Key}",
                static contribution => contribution.Shape.Fields, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The declared fields within an array element or nested object, or an empty list when no
    /// inner shape is registered. These describe the serialized shape; optional legacy input may
    /// omit fields such as a swap leg's principal-exchange flag, which defaults to false.
    /// </summary>
    public static IReadOnlyList<SecurityAssetTermField> ElementFields(string assetClass, string key)
        => ElementFieldsByPath.TryGetValue($"{assetClass}.{key}", out var fields) ? fields : [];

    private static readonly IReadOnlySet<string> ProfileResolvedTermClasses = SecurityAssetClassModules.All
        .Where(static module => module.CodecBinding?.ReadsResolvedTerms == true)
        .Select(static module => module.Descriptor.AssetClass).ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// True when the flat decode of <paramref name="assetClass"/> reads its declared keys from the
    /// profile-resolved terms document (the <c>profileFields</c> bag when the row carries one, else
    /// the root) instead of the root. This is the decode's view, not the catalog's
    /// <c>SupportsProfileBackedTerms</c>: OtherSecurity accepts profile-backed terms yet reads its
    /// flat keys from the root.
    /// </summary>
    public static bool ReadsProfileResolvedTerms(string assetClass)
        => ProfileResolvedTermClasses.Contains(assetClass);

    /// <summary>
    /// The scalar fields of <paramref name="assetClass"/> whose absence refuses a read
    /// (<see cref="SecurityAssetTermField.RefusesAbsenceOnRead"/>), in declaration order — which is
    /// the order a read checks them, and so decides which key a refusal names when several are
    /// absent. Container fields (arrays and objects) are read by their own decoders and are not
    /// included.
    /// </summary>
    public static IReadOnlyList<SecurityAssetTermField> ReadRequiredScalarFields(string assetClass)
        => ReadRequiredScalarFieldsByAssetClass.TryGetValue(assetClass, out var fields) ? fields : [];

    // Computed once: the read path consults it for every reconstructed row.
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<SecurityAssetTermField>> ReadRequiredScalarFieldsByAssetClass =
        FieldsByAssetClass.ToDictionary(
            static entry => entry.Key,
            static entry => (IReadOnlyList<SecurityAssetTermField>)entry.Value
                .Where(static field => field.RefusesAbsenceOnRead
                    && field.Type is not (SecurityAssetTermFieldType.Array or SecurityAssetTermFieldType.Object))
                .ToArray(),
            StringComparer.OrdinalIgnoreCase);

    /// <summary>The asset classes with a declared terms schema.</summary>
    public static IReadOnlyCollection<string> AssetClasses { get; } = FieldsByAssetClass.Keys.ToArray();

    /// <summary>The declared fields for <paramref name="assetClass"/>, or an empty list when none is declared.</summary>
    public static IReadOnlyList<SecurityAssetTermField> Fields(string assetClass)
        => FieldsByAssetClass.TryGetValue(assetClass, out var fields) ? fields : [];

    /// <summary>
    /// The declared CLOSED-vocabulary fields for <paramref name="assetClass"/> — the discriminants
    /// whose value selects a domain case. Callers that must enforce or audit the vocabularies walk
    /// this instead of hard-coding a field list per asset class.
    /// </summary>
    public static IEnumerable<SecurityAssetTermField> DiscriminantFields(string assetClass)
        => Fields(assetClass).Where(static field => field.IsDiscriminant);

    /// <summary>Tries to resolve the declared fields for <paramref name="assetClass"/>.</summary>
    public static bool TryGetFields(string assetClass, out IReadOnlyList<SecurityAssetTermField> fields)
        => FieldsByAssetClass.TryGetValue(assetClass, out fields!);

    /// <summary>The declared field for <paramref name="key"/> in <paramref name="assetClass"/>, or <see langword="null"/>.</summary>
    public static SecurityAssetTermField? Field(string assetClass, string key)
    {
        if (!FieldsByAssetClass.TryGetValue(assetClass, out var fields))
        {
            return null;
        }

        foreach (var field in fields)
        {
            if (string.Equals(field.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                return field;
            }
        }

        return null;
    }

    /// <summary>
    /// Every spelling the schema declares for the top-level term <paramref name="key"/>: the key
    /// itself first, then each alias any class's top-level field with that key declares, in class
    /// declaration order, deduplicated case-insensitively. Element (array/nested) fields are not
    /// consulted, so a swap leg's per-leg aliases never widen a top-level term's spellings.
    /// Readers that accept "either spelling" of a term derive the list here instead of restating it.
    /// </summary>
    public static IReadOnlyList<string> DeclaredSpellings(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var spellings = new List<string> { key };
        foreach (var fields in FieldsByAssetClass.Values)
        {
            foreach (var field in fields)
            {
                if (!string.Equals(field.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                foreach (var alias in field.Aliases)
                {
                    if (!spellings.Contains(alias, StringComparer.OrdinalIgnoreCase))
                    {
                        spellings.Add(alias);
                    }
                }
            }
        }

        return spellings;
    }

    /// <summary>
    /// The declared fields of <paramref name="assetClass"/> a node writes: those introduced at or below
    /// its write version (<see cref="SecurityAssetClassCatalog.GetTermsVersion"/>). Authoring surfaces
    /// (the asset-class registry, term blocks, the field-edit validator) offer these, never a field
    /// the node only reads.
    /// </summary>
    public static IReadOnlyList<SecurityAssetTermField> WritableFields(string assetClass)
    {
        var writes = SecurityAssetClassCatalog.GetTermsVersion(assetClass);
        return Fields(assetClass).Where(field => field.AuthoringEnabled && field.SinceTermsVersion <= writes).ToArray();
    }

    /// <summary>
    /// The registered inner-shape paths (<c>Class.key</c>, or <c>Class.container.key</c> for a shape
    /// inside an element), so guards can check every registered shape.
    /// </summary>
    public static IReadOnlyCollection<string> ElementFieldPaths => ElementFieldsByPath.Keys.ToArray();

    /// <summary>
    /// Whether a term value is populated. An absent or null value is not, and an array or object is
    /// populated only when an element or member is; every other value is, including <c>false</c>,
    /// <c>0</c> and the empty string. Absent, null, <c>[]</c> and an all-null object therefore all mean
    /// the term is unset (ADR-024).
    /// </summary>
    public static bool IsPopulatedTermValue(JsonElement value)
        => value.ValueKind switch
        {
            JsonValueKind.Undefined or JsonValueKind.Null => false,
            JsonValueKind.Array => value.EnumerateArray().Any(IsPopulatedTermValue),
            JsonValueKind.Object => value.EnumerateObject().Any(static member => IsPopulatedTermValue(member.Value)),
            _ => true
        };

    /// <summary>
    /// The populated terms of a flat payload that were introduced above <paramref name="termsVersion"/>:
    /// top-level fields, and fields inside a declared object or array element (a swap leg's
    /// <c>legs[1].scheduleConventions</c>). Keys and aliases match case-insensitively, as the readers
    /// do. A profile-governed payload (<see cref="SecurityTermsEnvelope.IsProfileGovernedPayload"/>)
    /// has no versioned class terms and yields nothing.
    /// </summary>
    public static IReadOnlyList<(string Path, SecurityAssetTermField Field)> PopulatedTermsAbove(
        string assetClass,
        JsonElement payload,
        int termsVersion)
        => VersionedTermsAbove(assetClass, payload, termsVersion, int.MaxValue)
            .Where(static term => IsPopulatedTermValue(term.Value))
            .Select(static term => (term.Path, term.Field))
            .ToArray();

    /// <summary>
    /// The terms of a flat payload introduced above <paramref name="termsVersion"/>, keyed by canonical
    /// path (<c>scheduleConventions</c>, <c>legs[1].scheduleConventions</c>) with every null member,
    /// <c>[]</c> and <c>{}</c> removed, so two payloads that carry the same versioned content compare
    /// equal however they spelled its absence. A path more than one spelling populates is reported in
    /// <paramref name="conflictingPaths"/>. A profile-governed payload yields an empty object.
    /// </summary>
    public static JsonObject NormalizedTermsAbove(
        string assetClass,
        JsonElement payload,
        int termsVersion,
        out IReadOnlyList<string> conflictingPaths)
        => NormalizedTermsAbove(assetClass, payload, termsVersion, int.MaxValue, out conflictingPaths);

    /// <summary>
    /// <see cref="NormalizedTermsAbove(string, JsonElement, int, out IReadOnlyList{string})"/> bounded
    /// above: only the terms introduced after <paramref name="termsVersion"/> and at or before
    /// <paramref name="throughVersion"/>. The round-trip guard bounds it by a row's own stamp, because a
    /// term above the stamp is an undeclared key of that row, which no reader decodes.
    /// </summary>
    public static JsonObject NormalizedTermsAbove(
        string assetClass,
        JsonElement payload,
        int termsVersion,
        int throughVersion,
        out IReadOnlyList<string> conflictingPaths)
    {
        var normalized = new JsonObject();
        var conflicts = new List<string>();
        foreach (var term in VersionedTermsAbove(assetClass, payload, termsVersion, throughVersion))
        {
            if (NormalizeTermValue(JsonNode.Parse(term.Value.GetRawText())) is not { } value)
            {
                continue;
            }

            if (normalized.ContainsKey(term.CanonicalPath))
            {
                conflicts.Add(term.CanonicalPath);
                continue;
            }

            normalized[term.CanonicalPath] = value;
        }

        conflictingPaths = conflicts;
        return normalized;
    }

    private static IEnumerable<(string Path, string CanonicalPath, SecurityAssetTermField Field, JsonElement Value)> VersionedTermsAbove(
        string assetClass,
        JsonElement payload,
        int termsVersion,
        int throughVersion)
    {
        if (payload.ValueKind != JsonValueKind.Object || SecurityTermsEnvelope.IsProfileGovernedPayload(assetClass, payload))
        {
            yield break;
        }

        foreach (var field in Fields(assetClass))
        {
            if (field.SinceTermsVersion > throughVersion)
            {
                continue;
            }

            foreach (var property in MatchingProperties(payload, field))
            {
                if (field.SinceTermsVersion > termsVersion)
                {
                    yield return (property.Name, field.Key, field, property.Value);
                    continue;
                }

                var inner = ElementFields(assetClass, field.Key)
                    .Where(innerField => innerField.SinceTermsVersion > termsVersion && innerField.SinceTermsVersion <= throughVersion)
                    .ToArray();
                if (inner.Length == 0)
                {
                    continue;
                }

                if (field.Type == SecurityAssetTermFieldType.Object && property.Value.ValueKind == JsonValueKind.Object)
                {
                    foreach (var term in InnerTerms(property.Value, property.Name, field.Key, inner))
                    {
                        yield return term;
                    }
                }
                else if (field.Type == SecurityAssetTermFieldType.Array && property.Value.ValueKind == JsonValueKind.Array)
                {
                    var index = 0;
                    foreach (var element in property.Value.EnumerateArray())
                    {
                        if (element.ValueKind == JsonValueKind.Object)
                        {
                            foreach (var term in InnerTerms(element, $"{property.Name}[{index}]", $"{field.Key}[{index}]", inner))
                            {
                                yield return term;
                            }
                        }

                        index++;
                    }
                }
            }
        }
    }

    private static IEnumerable<(string Path, string CanonicalPath, SecurityAssetTermField Field, JsonElement Value)> InnerTerms(
        JsonElement container,
        string path,
        string canonicalPath,
        IReadOnlyList<SecurityAssetTermField> fields)
    {
        foreach (var field in fields)
        {
            foreach (var property in MatchingProperties(container, field))
            {
                yield return ($"{path}.{property.Name}", $"{canonicalPath}.{field.Key}", field, property.Value);
            }
        }
    }

    private static IEnumerable<JsonProperty> MatchingProperties(JsonElement container, SecurityAssetTermField field)
        => container.EnumerateObject().Where(property =>
            string.Equals(property.Name, field.Key, StringComparison.OrdinalIgnoreCase)
            || field.Aliases.Any(alias => string.Equals(property.Name, alias, StringComparison.OrdinalIgnoreCase)));

    private static JsonNode? NormalizeTermValue(JsonNode? value)
    {
        switch (value)
        {
            case null:
                return null;
            case JsonObject members:
                var normalizedObject = new JsonObject();
                foreach (var (key, member) in members)
                {
                    if (NormalizeTermValue(member) is { } normalizedMember)
                    {
                        normalizedObject[key] = normalizedMember;
                    }
                }

                return normalizedObject.Count == 0 ? null : normalizedObject;
            case JsonArray elements:
                var normalizedArray = new JsonArray();
                foreach (var element in elements)
                {
                    if (NormalizeTermValue(element) is { } normalizedElement)
                    {
                        normalizedArray.Add(normalizedElement);
                    }
                }

                return normalizedArray.Count == 0 ? null : normalizedArray;
            default:
                return value.GetValueKind() == JsonValueKind.Null ? null : value.DeepClone();
        }
    }
}
