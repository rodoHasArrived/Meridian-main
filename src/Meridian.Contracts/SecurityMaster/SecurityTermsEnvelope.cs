using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Meridian.Contracts.SecurityMaster;

/// <summary>
/// The payload families a Security Master terms document can belong to. Written into every flat
/// terms payload as <see cref="SecurityTermsEnvelope.FormatKey"/> so readers dispatch on what the
/// document says it is instead of on its bare <c>schemaVersion</c> integer.
/// </summary>
public static class SecurityTermsFormats
{
    /// <summary>Flat per-asset-class terms (<see cref="AssetSpecificTermsSchema.Legacy"/>).</summary>
    public const string Flat = "flat";

    /// <summary>Structured economic-terms modules (<see cref="EconomicTermsSchema.Current"/>).</summary>
    public const string Economic = "economic";

    /// <summary>Custom-asset profile envelope (<see cref="AssetSpecificTermsSchema.CustomAssetProfile"/>).</summary>
    public const string CustomProfile = "customProfile";

    /// <summary>Every format name this node understands.</summary>
    public static IReadOnlyList<string> All { get; } = [Flat, Economic, CustomProfile];

    /// <summary>Whether <paramref name="format"/> names a known format (case-sensitive, as written).</summary>
    public static bool IsKnown(string? format)
        => format is Flat or Economic or CustomProfile;
}

/// <summary>
/// The envelope keys a Security Master terms payload carries alongside its terms, and the single
/// place that decides which payload family a stored document belongs to.
/// <para>
/// The asset-specific-terms slot historically held two payload families behind one integer
/// <c>schemaVersion</c> key, so readers had to guess the family from the integer plus the presence
/// of module keys. Every payload now states its family in <see cref="FormatKey"/> and, for flat
/// payloads, the asset-class terms contract version it was written against in
/// <see cref="AssetClassTermsVersionKey"/>. Live readers dispatch on the declared tag alone and refuse
/// an untagged payload with a precise error (ADR-024 decision 5, amendment 2026-11-13 step 5):
/// migration 068 declared the family on every current-state row and its check constraint refuses new
/// untagged rows. Untagged retained terms in append-only event history are classified only by
/// <see cref="SecurityTermsLegacyEventClassifier"/>, during event and snapshot decoding.
/// </para>
/// </summary>
public static class SecurityTermsEnvelope
{
    /// <summary>The codec version of the payload family.</summary>
    public const string SchemaVersionKey = "schemaVersion";

    /// <summary>The payload family (<see cref="SecurityTermsFormats"/>).</summary>
    public const string FormatKey = "termsFormat";

    /// <summary>
    /// The asset-class terms contract version a flat payload was written against
    /// (<see cref="SecurityAssetClassDescriptor.TermsVersion"/> of the writing node, or the newer
    /// version its populated terms need). Absent means version 1. An older version is read through
    /// <see cref="SecurityAssetClassTermsUpgrader"/>; a version above the write version and within
    /// <see cref="SecurityAssetClassDescriptor.ReadsTermsThrough"/> is readable but not writable.
    /// </summary>
    public const string AssetClassTermsVersionKey = "assetClassTermsVersion";

    /// <summary>
    /// The verbatim economic-terms document a flat payload was reconstructed from. Written only by
    /// the economic → flat bridge so that nothing the economic document carried is lost when the
    /// flat family cannot express it.
    /// </summary>
    public const string RetainedEconomicTermsKey = "retainedEconomicTerms";

    /// <summary>The asset-class terms version a flat payload without an explicit version is read as.</summary>
    public const int DefaultAssetClassTermsVersion = 1;

    /// <summary>
    /// Envelope keys that describe the payload rather than the security. They are never term fields,
    /// never operator-editable, and are excluded from term-schema key comparisons.
    /// </summary>
    public static IReadOnlySet<string> ReservedKeys { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        SchemaVersionKey,
        FormatKey,
        AssetClassTermsVersionKey,
        RetainedEconomicTermsKey,
        SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.FlattenedFromMarkerProperty
    };

    /// <summary>Whether <paramref name="key"/> is an envelope key rather than a term field.</summary>
    public static bool IsReservedKey(string? key)
        => key is not null && ReservedKeys.Contains(key);

    /// <summary>
    /// Returns the explicit <see cref="FormatKey"/> value, or <see langword="null"/> when the payload
    /// predates the tag (or is not an object).
    /// </summary>
    public static string? GetDeclaredFormat(JsonElement payload)
        => payload.ValueKind == JsonValueKind.Object
           && payload.TryGetProperty(FormatKey, out var format)
           && format.ValueKind == JsonValueKind.String
            ? format.GetString()
            : null;

    /// <summary>
    /// The declared payload family. Live code never infers a family from a payload's shape: a payload
    /// without a string <see cref="FormatKey"/> (or a non-object) is refused with
    /// <see cref="SecurityTermsFormatUndeclaredException"/>, which names the remedy. A declared family
    /// this node does not know is returned as declared, so the caller can refuse it with its own
    /// context. Untagged retained terms from event history are classified by
    /// <see cref="SecurityTermsLegacyEventClassifier.Classify"/> instead.
    /// </summary>
    public static string ResolveFormat(JsonElement payload)
        => GetDeclaredFormat(payload) ?? throw new SecurityTermsFormatUndeclaredException(payload.ValueKind);

    /// <summary>
    /// Whether a terms object is a custom-asset profile envelope as a writer submits it: a string
    /// <c>customProfileId</c> and a <c>profileFields</c> object. Stricter than
    /// <see cref="CarriesProfileEnvelope"/>, which accepts either signal on its own.
    /// </summary>
    public static bool IsProfileEnvelopeDocument(JsonElement payload)
        => payload.ValueKind == JsonValueKind.Object
           && payload.TryGetProperty("customProfileId", out var profileId)
           && profileId.ValueKind == JsonValueKind.String
           && payload.TryGetProperty("profileFields", out var profileFields)
           && profileFields.ValueKind == JsonValueKind.Object;

    /// <summary>
    /// The family a writer declares on untagged asset-specific terms it is about to persist:
    /// <see cref="SecurityTermsFormats.CustomProfile"/> for a profile envelope
    /// (<see cref="IsProfileEnvelopeDocument"/>), otherwise <see cref="SecurityTermsFormats.Flat"/>.
    /// A write accepts only those two families (<see cref="EnsureWritableAsAssetSpecificTerms"/>), so
    /// the profile envelope is the write contract's only discriminator; this never decides
    /// <see cref="SecurityTermsFormats.Economic"/> and never consults <c>schemaVersion</c>. A flat
    /// declaration on a document stamped with a codec version the flat family does not accept is read
    /// as flat and refused by the schema-version guard, never routed into the economic bridge.
    /// <see langword="null"/> for a payload that already declares a family, or a non-object.
    /// </summary>
    public static string? GetStampableFormat(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object || GetDeclaredFormat(payload) is not null)
        {
            return null;
        }

        return IsProfileEnvelopeDocument(payload) ? SecurityTermsFormats.CustomProfile : SecurityTermsFormats.Flat;
    }

    /// <summary>
    /// Declares <see cref="FormatKey"/> on untagged asset-specific terms a writer is about to persist, as
    /// <see cref="GetStampableFormat"/> decides (ADR-024 decision 1, amendment 2026-11-13). Only
    /// <see cref="FormatKey"/> is added: an absent <see cref="AssetClassTermsVersionKey"/> keeps meaning
    /// version 1, and every other member is copied byte for byte, except that a non-string
    /// <see cref="FormatKey"/> member (which declares nothing) is replaced. The key goes immediately
    /// after <c>schemaVersion</c>, or last when the document carries none. A payload that already
    /// declares a family and a non-object are returned unchanged, so stamping is idempotent. Replay of
    /// untagged event history uses <see cref="SecurityTermsLegacyEventClassifier.StampDeclaredFormat"/>.
    /// </summary>
    public static JsonElement StampDeclaredFormat(JsonElement payload)
        => GetStampableFormat(payload) is { } format ? DeclareFormat(payload, format) : payload;

    /// <summary>Text form of <see cref="StampDeclaredFormat(JsonElement)"/>; non-JSON text is returned unchanged.</summary>
    public static string StampDeclaredFormat(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return json;
        }

        using (document)
        {
            return GetStampableFormat(document.RootElement) is { } format
                ? BuildStamped(document.RootElement, format)
                : json;
        }
    }

    /// <summary>
    /// Adds <paramref name="format"/> as <see cref="FormatKey"/> to an object, keeping every other
    /// member byte for byte (see <see cref="StampDeclaredFormat(JsonElement)"/> for placement).
    /// </summary>
    internal static JsonElement DeclareFormat(JsonElement payload, string format)
    {
        using var document = JsonDocument.Parse(BuildStamped(payload, format));
        return document.RootElement.Clone();
    }

    private static string BuildStamped(JsonElement payload, string format)
    {
        // A text splice, not a rebuild: the declaration is inserted into the payload's own bytes and
        // every other byte (including the payload's own whitespace and separators) is kept, so
        // RemoveRedundantDeclaredFormat is its exact inverse and a hash taken over the stamp-free text
        // equals the hash of the same terms before they were stamped.
        var utf8 = System.Text.Encoding.UTF8.GetBytes(payload.GetRawText());

        // A non-string termsFormat member (for example null) declares nothing; it is replaced,
        // never kept, so the stamped document carries exactly one termsFormat key.
        while (ReadTopLevelMembers(utf8, out _) is var members
               && members.FindIndex(static member => member.IsFormat) is var index and >= 0)
        {
            utf8 = RemoveMember(utf8, members, index);
        }

        var remaining = ReadTopLevelMembers(utf8, out var openEnd);
        var schemaVersion = remaining.FindIndex(static member => member.IsSchemaVersion);
        var anchor = schemaVersion >= 0 ? remaining[schemaVersion].ValueEnd
            : remaining.Count > 0 ? remaining[^1].ValueEnd
            : openEnd;
        var declaration = (remaining.Count > 0 ? "," : string.Empty) + "\"" + FormatKey + "\":\"" + format + "\"";
        return System.Text.Encoding.UTF8.GetString(utf8, 0, anchor)
            + declaration
            + System.Text.Encoding.UTF8.GetString(utf8, anchor, utf8.Length - anchor);
    }

    /// <summary>
    /// The raw text of asset-specific terms without a top-level <see cref="FormatKey"/> member that
    /// only restates the family the frozen shape rule infers for the rest of the document
    /// (<see cref="SecurityTermsLegacyEventClassifier.GetStampableFormat"/>) — exactly the declaration a
    /// writer stamp, a replay stamp or migration 068 adds to untagged terms. The member is cut out of
    /// the payload's own text together with one adjacent separator, so every other byte is kept and the
    /// result is the text the same terms had before they were stamped (by <see cref="StampDeclaredFormat(JsonElement)"/>,
    /// or by <c>jsonb_set</c> in PostgreSQL's canonical <c>jsonb</c> text form). Any other payload —
    /// untagged, a non-object, a duplicate or non-string <see cref="FormatKey"/>, or a declaration that
    /// differs from what the shape rule infers (for example <see cref="SecurityTermsFormats.Economic"/>) —
    /// is returned as its raw text unchanged, so a declaration that could change what the terms decode
    /// to always stays in the text.
    /// </summary>
    public static string RemoveRedundantDeclaredFormat(JsonElement payload)
    {
        var raw = payload.GetRawText();
        if (payload.ValueKind != JsonValueKind.Object || GetDeclaredFormat(payload) is not { } declared)
        {
            return raw;
        }

        var utf8 = System.Text.Encoding.UTF8.GetBytes(raw);
        var members = ReadTopLevelMembers(utf8, out _);
        var formatMembers = members.FindAll(static member => member.IsFormat);
        if (formatMembers.Count != 1)
        {
            return raw;
        }

        var stripped = System.Text.Encoding.UTF8.GetString(RemoveMember(utf8, members, members.IndexOf(formatMembers[0])));
        using var document = JsonDocument.Parse(stripped);
        return string.Equals(SecurityTermsLegacyEventClassifier.GetStampableFormat(document.RootElement), declared, StringComparison.Ordinal)
            ? stripped
            : raw;
    }

    /// <summary>
    /// <see cref="RemoveRedundantDeclaredFormat"/> as an element: the same payload when it carries no
    /// redundant declaration, otherwise a detached element over the stamp-free text.
    /// </summary>
    public static JsonElement WithoutRedundantDeclaredFormat(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object || GetDeclaredFormat(payload) is null)
        {
            return payload;
        }

        var stripped = RemoveRedundantDeclaredFormat(payload);
        if (string.Equals(stripped, payload.GetRawText(), StringComparison.Ordinal))
        {
            return payload;
        }

        using var document = JsonDocument.Parse(stripped);
        return document.RootElement.Clone();
    }

    private readonly record struct TopLevelMember(int NameStart, int ValueEnd, bool IsFormat, bool IsSchemaVersion);

    private static List<TopLevelMember> ReadTopLevelMembers(byte[] utf8, out int openEnd)
    {
        var reader = new Utf8JsonReader(utf8);
        reader.Read();
        openEnd = checked((int)reader.BytesConsumed);
        var members = new List<TopLevelMember>();
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            var nameStart = checked((int)reader.TokenStartIndex);
            var isFormat = reader.ValueTextEquals(FormatKey);
            var isSchemaVersion = reader.ValueTextEquals(SchemaVersionKey);
            reader.Read();
            reader.Skip();
            members.Add(new(nameStart, checked((int)reader.BytesConsumed), isFormat, isSchemaVersion));
        }

        return members;
    }

    /// <summary>
    /// Cuts one top-level member out of an object's text with one adjacent separator: the separator
    /// before it when it has a predecessor, otherwise the separator (and whitespace) after it.
    /// </summary>
    private static byte[] RemoveMember(byte[] utf8, List<TopLevelMember> members, int index)
    {
        var (start, end) = index > 0 ? (members[index - 1].ValueEnd, members[index].ValueEnd)
            : members.Count > 1 ? (members[0].NameStart, members[1].NameStart)
            : (members[0].NameStart, members[0].ValueEnd);
        var result = new byte[utf8.Length - (end - start)];
        utf8.AsSpan(0, start).CopyTo(result);
        utf8.AsSpan(end).CopyTo(result.AsSpan(start));
        return result;
    }

    /// <summary>
    /// The asset-class terms version a flat payload declares, or
    /// <see cref="DefaultAssetClassTermsVersion"/> when it predates the key.
    /// </summary>
    public static int ResolveAssetClassTermsVersion(JsonElement payload)
        => payload.ValueKind == JsonValueKind.Object
           && payload.TryGetProperty(AssetClassTermsVersionKey, out var version)
           && version.ValueKind == JsonValueKind.Number
           && version.TryGetInt32(out var value)
            ? value
            : DefaultAssetClassTermsVersion;

    /// <summary>
    /// Stamps <see cref="FormatKey"/> = <see cref="SecurityTermsFormats.Flat"/> and the asset class's
    /// S2 floor <see cref="AssetClassTermsVersionKey"/> onto a scalar-only flat terms document.
    /// A writer holding versioned declarations supplies their actual version through the explicit overload.
    /// The keys appear immediately after its <c>schemaVersion</c>. Keys the document already carries are replaced, so
    /// stamping is idempotent. Non-object JSON is returned unchanged.
    /// </summary>
    public static string StampFlat(string json, string assetClass)
        => StampFlat(json, SecurityAssetClassCatalog.GetStampFloor(assetClass));

    /// <summary>
    /// The S2 stamp this node would write for the terms a retained document actually decodes.
    /// Containers above its own stamp remain stray keys and never raise the result.
    /// </summary>
    public static int GetDocumentStampVersion(string assetClass, JsonElement payload)
    {
        var floor = SecurityAssetClassCatalog.GetStampFloor(assetClass);
        var decoding = SecurityVersionedTerms.Decoding(assetClass, payload, writeMode: false);
        return SecurityAssetTermsSchema.PopulatedTermsAbove(assetClass, payload, floor)
            .Where(term => decoding.Decodes(term.Field))
            .Select(term => term.Field.SinceTermsVersion)
            .DefaultIfEmpty(floor).Max();
    }

    /// <summary>
    /// <see cref="StampFlat(string, string)"/> at the version a writer's populated terms need: the
    /// document's highest held versioned-term version, floored at its S2 stamp floor. Throws
    /// when the version is below the stamp floor or above <see cref="SecurityAssetClassCatalog.GetReadsTermsThrough"/>.
    /// </summary>
    public static string StampFlat(string json, string assetClass, int assetClassTermsVersion)
    {
        var writes = SecurityAssetClassCatalog.GetTermsVersion(assetClass);
        var floor = SecurityAssetClassCatalog.GetStampFloor(assetClass);
        var reads = SecurityAssetClassCatalog.GetReadsTermsThrough(assetClass);
        if (assetClassTermsVersion < floor || assetClassTermsVersion > reads)
        {
            throw new ArgumentOutOfRangeException(
                nameof(assetClassTermsVersion),
                assetClassTermsVersion,
                $"Asset class '{assetClass}' writes {AssetClassTermsVersionKey} {writes} and reads versions {DefaultAssetClassTermsVersion} through {reads}.");
        }

        return StampFlat(json, assetClassTermsVersion);
    }

    /// <summary>
    /// <see cref="StampFlat(string, string)"/> with an explicit asset-class terms version: the stamping
    /// routine every flat writer and the upgrade hook's transform path share
    /// (<see cref="SecurityAssetClassTermsUpgrader"/>). The envelope keys go immediately after
    /// <c>schemaVersion</c> when the document carries one, and last otherwise; keys the document
    /// already carries are replaced, so stamping is idempotent. Non-object JSON is returned unchanged.
    /// </summary>
    public static string StampFlat(string json, int assetClassTermsVersion)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentOutOfRangeException.ThrowIfLessThan(assetClassTermsVersion, DefaultAssetClassTermsVersion);
        if (JsonNode.Parse(json) is not JsonObject source)
        {
            return json;
        }

        var stamped = new JsonObject();
        var wroteEnvelope = false;
        void WriteEnvelope()
        {
            stamped[FormatKey] = SecurityTermsFormats.Flat;
            stamped[AssetClassTermsVersionKey] = assetClassTermsVersion;
            wroteEnvelope = true;
        }

        foreach (var (key, value) in source.ToArray())
        {
            if (key is FormatKey or AssetClassTermsVersionKey)
            {
                continue;
            }

            source.Remove(key);
            stamped[key] = value;
            if (key == SchemaVersionKey)
            {
                WriteEnvelope();
            }
        }

        if (!wroteEnvelope)
        {
            WriteEnvelope();
        }

        return stamped.ToJsonString();
    }

    /// <summary>
    /// Fails when a flat payload declares an asset-class terms version newer than this node reads for
    /// <paramref name="assetClass"/> (<see cref="SecurityAssetClassCatalog.GetReadsTermsThrough"/>), or
    /// declares a format other than flat or custom profile. Reading such a payload with the current
    /// decoder would silently drop whatever the newer contract added, so the read is refused with a
    /// precise reason instead.
    /// </summary>
    public static void EnsureReadableAsAssetSpecificTerms(string assetClass, JsonElement payload)
    {
        if (TryGetReadRefusal(assetClass, payload, out var reason))
        {
            throw new InvalidOperationException(reason);
        }
    }

    /// <summary>
    /// Why <see cref="EnsureReadableAsAssetSpecificTerms"/> refuses a payload, without throwing: it
    /// declares a format other than flat or custom profile, or its asset-class terms version is below 1
    /// or above <see cref="SecurityAssetClassCatalog.GetReadsTermsThrough"/>. The format is checked
    /// first, and the reason is the exact text the read guard throws.
    /// </summary>
    public static bool TryGetReadRefusal(string assetClass, JsonElement payload, [NotNullWhen(true)] out string? reason)
        => TryGetFormatReadRefusal(assetClass, payload, out reason)
           || TryGetVersionReadRefusal(assetClass, payload, out reason);

    /// <summary>The format half of <see cref="TryGetReadRefusal"/>: a declared format that is not an asset-specific-terms format.</summary>
    internal static bool TryGetFormatReadRefusal(string assetClass, JsonElement payload, [NotNullWhen(true)] out string? reason)
    {
        var declaredFormat = GetDeclaredFormat(payload);
        if (declaredFormat is not null
            && declaredFormat is not (SecurityTermsFormats.Flat or SecurityTermsFormats.CustomProfile))
        {
            reason =
                $"Terms payload for asset class '{assetClass}' declares {FormatKey} '{declaredFormat}', which is not an " +
                "asset-specific-terms format; route it through SecurityAssetSpecificTermsUpcasterChain before reading.";
            return true;
        }

        reason = null;
        return false;
    }

    /// <summary>The stamp half of <see cref="TryGetReadRefusal"/>: an asset-class terms version this node does not read.</summary>
    internal static bool TryGetVersionReadRefusal(string assetClass, JsonElement payload, [NotNullWhen(true)] out string? reason)
    {
        var supported = SecurityAssetClassCatalog.GetReadsTermsThrough(assetClass);
        var declaredVersion = ResolveAssetClassTermsVersion(payload);
        if (declaredVersion > supported || declaredVersion < DefaultAssetClassTermsVersion)
        {
            reason =
                $"Unsupported {AssetClassTermsVersionKey} '{declaredVersion}' for asset class '{assetClass}'; " +
                $"this node reads versions {DefaultAssetClassTermsVersion} through {supported}.";
            return true;
        }

        reason = null;
        return false;
    }

    /// <summary>
    /// <see cref="EnsureReadableAsAssetSpecificTerms"/> for a write: also fails when the payload is
    /// readable but not writable on this node (<see cref="TryGetWriteRefusal"/>). Every create, amend,
    /// import, term-block amend and approved field-edit merge maps its terms through this check.
    /// </summary>
    public static void EnsureWritableAsAssetSpecificTerms(string assetClass, JsonElement payload)
    {
        // A write reaches the caller, so it names what to submit rather than the read path's conversion.
        var declaredFormat = GetDeclaredFormat(payload);
        if (declaredFormat is not null
            && declaredFormat is not (SecurityTermsFormats.Flat or SecurityTermsFormats.CustomProfile))
        {
            throw new InvalidOperationException(
                $"Terms for asset class '{assetClass}' declare {FormatKey} '{declaredFormat}', which a write does not accept. " +
                $"Submit asset-specific terms: omit {FormatKey}, or set it to '{SecurityTermsFormats.Flat}' or " +
                $"'{SecurityTermsFormats.CustomProfile}'.");
        }

        EnsureReadableAsAssetSpecificTerms(assetClass, payload);
        if (TryGetWriteRefusal(assetClass, payload, out var reason))
        {
            throw new InvalidOperationException(reason);
        }
    }

    /// <summary>
    /// Why a payload this node reads cannot be written by it: it is stamped above the class's write
    /// version, or it populates a term introduced above that version (ADR-024, readable before
    /// writable). Absent, null, <c>[]</c> and all-null objects mean unset and are accepted. A
    /// profile-governed payload (<see cref="IsProfileGovernedPayload"/>) has no versioned class terms,
    /// so only its stamp is checked. A class that reads exactly what it writes is never refused here,
    /// and neither is a payload stamped above what this node reads: that is the read guard's refusal
    /// (<see cref="EnsureReadableAsAssetSpecificTerms"/>), which names the newer version.
    /// </summary>
    public static bool TryGetWriteRefusal(string assetClass, JsonElement payload, [NotNullWhen(true)] out string? reason)
    {
        reason = null;
        var writes = SecurityAssetClassCatalog.GetTermsVersion(assetClass);
        var reads = SecurityAssetClassCatalog.GetReadsTermsThrough(assetClass);
        var declaredVersion = ResolveAssetClassTermsVersion(payload);
        if (declaredVersion > reads)
        {
            return false;
        }

        if (!IsProfileGovernedPayload(assetClass, payload))
        {
            var unavailable = SecurityAssetTermsSchema.PopulatedTermsAbove(assetClass, payload, 1)
                .FirstOrDefault(term => !term.Field.AuthoringEnabled);
            if (unavailable.Field is not null)
            {
                reason = DescribeAuthoringDisabled(unavailable.Path);
                return true;
            }
        }
        if (reads == writes)
            return false;

        if (declaredVersion > writes)
        {
            // A flat payload can populate a term introduced above its own stamp; only a node that
            // writes that term's version can apply it.
            var needed = declaredVersion;
            if (!IsProfileGovernedPayload(assetClass, payload))
            {
                foreach (var (_, field) in SecurityAssetTermsSchema.PopulatedTermsAbove(assetClass, payload, declaredVersion))
                {
                    needed = Math.Max(needed, field.SinceTermsVersion);
                }
            }

            reason =
                $"{AssetClassTermsVersionKey} '{declaredVersion}' for asset class '{assetClass}' is readable but not writable on this node: " +
                $"it reads versions {DefaultAssetClassTermsVersion} through {reads} and writes version {writes}. " +
                $"Submit the terms at version {writes}, or apply the change from a node that writes version {needed}.";
            return true;
        }

        if (IsProfileGovernedPayload(assetClass, payload))
        {
            return false;
        }

        var populated = SecurityAssetTermsSchema.PopulatedTermsAbove(assetClass, payload, writes);
        if (populated.Count == 0)
        {
            return false;
        }

        reason = DescribeNotYetWritable(assetClass, populated[0].Path, populated[0].Field.SinceTermsVersion);
        return true;
    }

    /// <summary>
    /// The shared reason a term this node reads cannot be set by it: the refusal every write path,
    /// term-block amend and field edit gives for a term introduced above the class's write version.
    /// </summary>
    public static string DescribeAuthoringDisabled(string path)
        => $"'{path}' is readable but authoring is not enabled on this node; a rate-unit declaration does not activate calculation-only terms.";

    public static string DescribeNotYetWritable(string assetClass, string path, int sinceTermsVersion)
        => $"'{path}' is an asset-class terms version {sinceTermsVersion} term of '{assetClass}'. This node reads it but writes " +
           $"{AssetClassTermsVersionKey} {SecurityAssetClassCatalog.GetTermsVersion(assetClass)}, so a create, amend, import or field edit " +
           "cannot set it yet. Leave it absent, null or empty (ADR-024).";

    /// <summary>
    /// Whether a payload of <paramref name="assetClass"/> is a profile envelope: the class supports
    /// profile-backed terms and the payload carries a <c>customProfileId</c> or a <c>profileFields</c>
    /// object. Its fields belong to the approved profile, not to the class's terms version, so
    /// versioned class terms are never walked, decoded or validated in it; only its stamp is checked.
    /// </summary>
    public static bool IsProfileGovernedPayload(string assetClass, JsonElement payload)
        => SecurityAssetClassCatalog.GetOrDefault(assetClass).SupportsProfileBackedTerms
           && CarriesProfileEnvelope(payload);

    /// <summary>
    /// Whether a terms object carries a profile envelope signal: a non-blank string
    /// <c>customProfileId</c> or a <c>profileFields</c> object. Non-objects carry none.
    /// </summary>
    public static bool CarriesProfileEnvelope(JsonElement payload)
        => payload.ValueKind == JsonValueKind.Object
           && ((payload.TryGetProperty("customProfileId", out var profileId)
                && profileId.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(profileId.GetString()))
               || (payload.TryGetProperty("profileFields", out var profileFields)
                   && profileFields.ValueKind == JsonValueKind.Object));

    /// <summary>
    /// Returns the economic-terms document a flattened payload retained under
    /// <see cref="RetainedEconomicTermsKey"/>, when it carries one.
    /// </summary>
    public static bool TryGetRetainedEconomicTerms(JsonElement flatTerms, out JsonElement retained)
    {
        if (flatTerms.ValueKind == JsonValueKind.Object
            && flatTerms.TryGetProperty(RetainedEconomicTermsKey, out var value)
            && value.ValueKind == JsonValueKind.Object)
        {
            retained = value;
            return true;
        }

        retained = default;
        return false;
    }

    /// <summary>
    /// Carries a retained economic-terms document from <paramref name="previousFlatTerms"/> onto
    /// <paramref name="nextFlatTerms"/> when the next revision was rebuilt through the domain codec
    /// (which knows only the declared flat keys) and so no longer carries it. Returns
    /// <paramref name="nextFlatTerms"/> unchanged when there is nothing to carry or it already has
    /// its own retained document.
    /// </summary>
    public static JsonElement CarryRetainedEconomicTerms(JsonElement previousFlatTerms, JsonElement nextFlatTerms)
    {
        if (!TryGetRetainedEconomicTerms(previousFlatTerms, out var retained)
            || nextFlatTerms.ValueKind != JsonValueKind.Object
            || TryGetRetainedEconomicTerms(nextFlatTerms, out _)
            || JsonNode.Parse(nextFlatTerms.GetRawText()) is not JsonObject next)
        {
            return nextFlatTerms;
        }

        next[RetainedEconomicTermsKey] = JsonNode.Parse(retained.GetRawText());
        if (previousFlatTerms.TryGetProperty(
                SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.FlattenedFromMarkerProperty,
                out var marker)
            && !next.ContainsKey(SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.FlattenedFromMarkerProperty))
        {
            next[SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.FlattenedFromMarkerProperty] = JsonNode.Parse(marker.GetRawText());
        }

        using var document = JsonDocument.Parse(next.ToJsonString());
        return document.RootElement.Clone();
    }

    /// <summary>
    /// Folds a retained economic-terms document back into economic terms rebuilt from flat terms.
    /// Rebuilt values always win; the retained document only fills what the flat family could not
    /// carry: a module the rebuilt document has as null or absent, a field a rebuilt module has as
    /// null or absent, or a schedule it has as an empty array. Envelope keys of the retained document
    /// are never folded. The result is the economic record the original economic event described,
    /// updated by whatever the flat revision changed.
    /// <para>
    /// <paramref name="flatAuthoritativeFields"/> names the module fields the flat row carries
    /// exactly (<see cref="SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.FlatAuthoritativeFields"/>).
    /// The retained document never fills one of those: the row could carry it, so an empty value in
    /// the rebuild means the flat revision cleared it — covenants removed, a principal schedule
    /// dropped — and putting the old value back would make the economic record contradict its
    /// flat terms and resurrect the cleared value on the next replay. A retained module that
    /// holds nothing else is not folded at all.
    /// </para>
    /// </summary>
    public static JsonElement FoldRetainedEconomicTerms(
        JsonElement rebuiltEconomicTerms,
        JsonElement retainedEconomicTerms,
        IReadOnlyCollection<(string Module, string Field)>? flatAuthoritativeFields = null)
    {
        if (rebuiltEconomicTerms.ValueKind != JsonValueKind.Object
            || retainedEconomicTerms.ValueKind != JsonValueKind.Object
            || JsonNode.Parse(rebuiltEconomicTerms.GetRawText()) is not JsonObject rebuilt)
        {
            return rebuiltEconomicTerms;
        }

        foreach (var module in retainedEconomicTerms.EnumerateObject())
        {
            if (IsReservedKey(module.Name) || module.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                continue;
            }

            var retainedModule = JsonNode.Parse(module.Value.GetRawText());
            if (retainedModule is JsonObject retainedFields && flatAuthoritativeFields is { Count: > 0 })
            {
                var cleared = false;
                foreach (var (authoritativeModule, authoritativeField) in flatAuthoritativeFields)
                {
                    if (string.Equals(authoritativeModule, module.Name, StringComparison.Ordinal)
                        && retainedFields.TryGetPropertyValue(authoritativeField, out var held)
                        && held is not null)
                    {
                        retainedFields[authoritativeField] = held is JsonArray ? new JsonArray() : null;
                        cleared = true;
                    }
                }

                if (cleared && HoldsNothing(retainedFields))
                {
                    continue;
                }
            }

            var existing = rebuilt[module.Name];
            if (existing is null)
            {
                rebuilt[module.Name] = retainedModule;
                continue;
            }

            if (existing is not JsonObject rebuiltModule || retainedModule is not JsonObject retainedObject)
            {
                continue;
            }

            foreach (var field in retainedObject.ToArray())
            {
                if (field.Value is null)
                {
                    continue;
                }

                var current = rebuiltModule[field.Key];
                var fill = current is null
                           || (current is JsonArray currentArray
                               && currentArray.Count == 0
                               && field.Value is JsonArray { Count: > 0 });
                if (fill)
                {
                    rebuiltModule[field.Key] = field.Value.DeepClone();
                }
            }
        }

        using var document = JsonDocument.Parse(rebuilt.ToJsonString());
        return document.RootElement.Clone();
    }

    /// <summary>
    /// Whether a retained module is left with no content once its flat-authoritative fields are
    /// set aside: every member null, false, an empty array or an empty object.
    /// </summary>
    private static bool HoldsNothing(JsonObject module)
    {
        foreach (var (_, value) in module)
        {
            switch (value)
            {
                case null:
                case JsonArray { Count: 0 }:
                case JsonObject { Count: 0 }:
                    continue;
                case JsonValue scalar when scalar.GetValueKind() == JsonValueKind.False:
                    continue;
                default:
                    return false;
            }
        }

        return true;
    }
}

/// <summary>
/// Raised when live code meets an asset-specific-terms payload that declares no
/// <see cref="SecurityTermsEnvelope.FormatKey"/>. Live readers and writers dispatch on the declared
/// family alone (ADR-024 decision 5, amendment 2026-11-13 step 5); an untagged payload is refused
/// instead of being classified by shape.
/// </summary>
public sealed class SecurityTermsFormatUndeclaredException : InvalidOperationException
{
    /// <summary>Creates the refusal for a payload of the given JSON kind.</summary>
    public SecurityTermsFormatUndeclaredException(JsonValueKind payloadKind)
        : base(
            payloadKind == JsonValueKind.Object
                ? $"Asset-specific terms declare no {SecurityTermsEnvelope.FormatKey}; this node no longer infers the payload " +
                  $"family by shape. Declare {SecurityTermsEnvelope.FormatKey} as '{SecurityTermsFormats.Flat}' or '{SecurityTermsFormats.CustomProfile}' " +
                  $"('{SecurityTermsFormats.Economic}' is not accepted on writes) " +
                  "(writers stamp it through SecurityTermsEnvelope.StampDeclaredFormat or StampFlat), or run migration 068 on stored rows."
                : $"Asset-specific terms are a JSON {payloadKind}, not an object, so they cannot declare {SecurityTermsEnvelope.FormatKey}.")
    {
        PayloadKind = payloadKind;
    }

    /// <summary>The JSON kind of the refused payload.</summary>
    public JsonValueKind PayloadKind { get; }
}
