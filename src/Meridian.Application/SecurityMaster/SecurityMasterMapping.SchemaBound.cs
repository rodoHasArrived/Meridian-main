using System.Collections.Frozen;
using System.Text.Json;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Application.SecurityMaster;

// ADR-027 stage 3 (S3-D): the schema-bound decode reader, and the ledger of the reads that stay
// outside it. The class ledger (SchemaBoundDecodeClasses) lives beside the decode switch.
internal static partial class SecurityMasterMapping
{
    /// <summary>
    /// One decode read the schema cannot bind: <paramref name="Path"/> (a root key, or a dotted
    /// path into a nested block) is not declared for <paramref name="AssetClass"/> with the type
    /// the codec reads it as (<paramref name="ReadAs"/>).
    /// </summary>
    public sealed record UnboundCompatibilityRead(
        string AssetClass, string Path, SecurityAssetTermFieldType ReadAs, string Reason);

    private const string EnvelopeLessCustomAssetSalvage =
        "read-only salvage of an envelope-less legacy row as OtherSecurity; the CustomAsset schema declares the profile envelope, not the OtherSecurity shape";

    private const string PreferredTermsShapeUndeclared =
        "the schema declares preferredTerms as an object but registers no inner shape for it";

    /// <summary>
    /// ADR-027 stage 3 (S3-D): every read a schema-bound class decode makes OUTSIDE the schema —
    /// a key (or nested path) <see cref="SecurityAssetTermsSchema"/> does not declare for the
    /// class with the type the codec reads it as. Binding them would mean declaring fields, which
    /// changes the write contract, so they stay unbound and are committed here instead. The
    /// ledger is enforced, not advisory: these reads go through
    /// <c>SchemaBoundTerms.Unbound</c>, which throws codec/schema drift when a read is not
    /// ledgered, and when a ledgered read becomes bindable (the schema now declares it as read),
    /// so an entry is retired by binding the read rather than by leaving it to rot.
    /// </summary>
    public static IReadOnlyList<UnboundCompatibilityRead> UnboundCompatibilityReads { get; } =
    [
        new("CustomAsset", "maturity", SecurityAssetTermFieldType.Date, EnvelopeLessCustomAssetSalvage),
        new("CustomAsset", "issuerName", SecurityAssetTermFieldType.String, EnvelopeLessCustomAssetSalvage),
        new("CustomAsset", "settlementType", SecurityAssetTermFieldType.String, EnvelopeLessCustomAssetSalvage),
        new("Equity", "preferredTerms.dividendRate", SecurityAssetTermFieldType.Decimal, PreferredTermsShapeUndeclared),
        new("Equity", "preferredTerms.dividendType", SecurityAssetTermFieldType.String, PreferredTermsShapeUndeclared),
        new("Equity", "preferredTerms.redemptionPrice", SecurityAssetTermFieldType.Decimal, PreferredTermsShapeUndeclared),
        new("Equity", "preferredTerms.redemptionDate", SecurityAssetTermFieldType.Date, PreferredTermsShapeUndeclared),
        new("Equity", "preferredTerms.callableDate", SecurityAssetTermFieldType.Date, PreferredTermsShapeUndeclared),
        new("Equity", "preferredTerms.participationTerms", SecurityAssetTermFieldType.Object, PreferredTermsShapeUndeclared),
        new("Equity", "preferredTerms.participationTerms.participatesInCommonDividends", SecurityAssetTermFieldType.Boolean, PreferredTermsShapeUndeclared),
        new("Equity", "preferredTerms.participationTerms.additionalDividendThreshold", SecurityAssetTermFieldType.Decimal, PreferredTermsShapeUndeclared),
        new("Equity", "preferredTerms.liquidationPreference", SecurityAssetTermFieldType.Object, PreferredTermsShapeUndeclared),
        new("Equity", "preferredTerms.liquidationPreference.kind", SecurityAssetTermFieldType.String, PreferredTermsShapeUndeclared),
        new("Equity", "preferredTerms.liquidationPreference.multiple", SecurityAssetTermFieldType.Decimal, PreferredTermsShapeUndeclared)
    ];

    private static readonly FrozenSet<(string AssetClass, string Path, SecurityAssetTermFieldType ReadAs)> LedgeredUnboundReads =
        UnboundCompatibilityReads.Select(static read => (read.AssetClass, read.Path, read.ReadAs)).ToFrozenSet();

    /// <summary>
    /// The schema-bound view of a submitted or stored terms document for one asset class: each
    /// read verifies, against <see cref="SecurityAssetTermsSchema"/>, that the key is declared
    /// and that its declared type matches the type the codec reads it as. Requiredness stays the
    /// codec's call — the tolerated absence defaults are declared on the schema fields
    /// (<see cref="SecurityAssetTermField.AbsentReadDefault"/>) — so this binds exactly the facts
    /// whose drift would corrupt a decode.
    /// <para>A nested object (<see cref="RequiredObject"/>, <see cref="OptionalObject"/>) is read
    /// through the same view, bound to the inner shape the schema registers for its path
    /// (<see cref="SecurityAssetTermsSchema.ElementFields"/>). The view never hands out the
    /// document itself, so a decoder that takes one cannot read past it.</para>
    /// </summary>
    private readonly struct SchemaBoundTerms(
        string assetClass,
        JsonElement json,
        SecurityKindMappingMode mode,
        string? container = null,
        IReadOnlyList<SecurityAssetTermField>? containerFields = null,
        bool unbound = false)
    {
        /// <summary>Whether the document is a JSON object at all; no read is valid on anything else.</summary>
        public bool IsObject => json.ValueKind == JsonValueKind.Object;

        /// <summary>The document verbatim, for the kind that carries its envelope whole.</summary>
        public string RawText => json.GetRawText();

        /// <summary>
        /// The same document read through <see cref="UnboundCompatibilityReads"/> instead of the
        /// schema: each read must be ledgered with the type it is read as, and must not be
        /// bindable. Nested objects reached from this view stay in it.
        /// </summary>
        public SchemaBoundTerms Unbound => new(assetClass, json, mode, container, containerFields, unbound: true);

        public string RequiredString(string key) => GetRequiredString(json, Declared(key, SecurityAssetTermFieldType.String));

        public string? OptionalString(string key) => GetOptionalString(json, Declared(key, SecurityAssetTermFieldType.String));

        public string? OptionalConventionString(string key)
            => GetOptionalString(json, Declared(key, SecurityAssetTermFieldType.String), mode);

        public decimal? OptionalDecimal(string key) => GetOptionalDecimal(json, Declared(key, SecurityAssetTermFieldType.Decimal));

        public int? OptionalInt(string key) => GetOptionalInt(json, Declared(key, SecurityAssetTermFieldType.Integer));

        public bool? OptionalBoolean(string key) => GetOptionalBoolean(json, Declared(key, SecurityAssetTermFieldType.Boolean));

        public DateOnly? OptionalDateOnly(string key) => GetOptionalDateOnly(json, Declared(key, SecurityAssetTermFieldType.Date));

        public DateOnly RequiredDateOnly(string key) => GetRequiredDateOnly(json, Declared(key, SecurityAssetTermFieldType.Date));

        public Guid RequiredGuid(string key) => GetRequiredGuid(json, Declared(key, SecurityAssetTermFieldType.Guid));

        public decimal RequiredDecimal(string key) => GetRequiredDecimal(json, Declared(key, SecurityAssetTermFieldType.Decimal));

        public int RequiredInt(string key) => GetRequiredInt(json, Declared(key, SecurityAssetTermFieldType.Integer));

        public decimal? OptionalStrictDecimal(string key) => GetOptionalDecimal(json, Declared(key, SecurityAssetTermFieldType.Decimal), mode);

        public DateOnly? OptionalStrictDateOnly(string key) => GetOptionalDateOnly(json, Declared(key, SecurityAssetTermFieldType.Date), mode);

        public int? OptionalStrictInt(string key) => GetOptionalInt(json, Declared(key, SecurityAssetTermFieldType.Integer), mode);

        public SchemaBoundTerms RequiredObject(string key)
            => Nested(key, GetRequiredObject(json, Declared(key, SecurityAssetTermFieldType.Object)));

        public SchemaBoundTerms? OptionalObject(string key)
            => GetOptionalObject(json, Declared(key, SecurityAssetTermFieldType.Object)) is { } value ? Nested(key, value) : null;

        private SchemaBoundTerms Nested(string key, JsonElement value)
        {
            var path = PathOf(key);
            return new(assetClass, value, mode, path, SecurityAssetTermsSchema.ElementFields(assetClass, path), unbound);
        }

        private string PathOf(string key) => container is null ? key : $"{container}.{key}";

        private string Declared(string key, SecurityAssetTermFieldType readAs)
        {
            var field = containerFields is null
                ? SecurityAssetTermsSchema.Field(assetClass, key)
                : ContainerField(containerFields, key);
            if (unbound)
            {
                return LedgeredUnbound(key, readAs, field);
            }

            if (field is null)
            {
                throw new InvalidOperationException(
                    $"Codec/schema drift: the codec reads '{assetClass}.{PathOf(key)}', which the schema does not declare.");
            }

            if (field.Type != readAs)
            {
                throw new InvalidOperationException(
                    $"Codec/schema drift: '{assetClass}.{PathOf(key)}' is declared {field.Type} but the codec reads it as {readAs}.");
            }

            return field.Key;
        }

        // The same case-insensitive match SecurityAssetTermsSchema.Field applies to root fields.
        private static SecurityAssetTermField? ContainerField(IReadOnlyList<SecurityAssetTermField> fields, string key)
        {
            foreach (var field in fields)
            {
                if (string.Equals(field.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    return field;
                }
            }

            return null;
        }

        private string LedgeredUnbound(string key, SecurityAssetTermFieldType readAs, SecurityAssetTermField? field)
        {
            var path = PathOf(key);
            if (field is not null && field.Type == readAs)
            {
                throw new InvalidOperationException(
                    $"Codec/schema drift: '{assetClass}.{path}' is read as an unbound compatibility read, but the schema " +
                    $"declares it {readAs}; bind the read and retire its {nameof(UnboundCompatibilityReads)} entry.");
            }

            if (!LedgeredUnboundReads.Contains((assetClass, path, readAs)))
            {
                throw new InvalidOperationException(
                    $"Codec/schema drift: the codec reads '{assetClass}.{path}' as {readAs} outside the schema, and " +
                    $"{nameof(UnboundCompatibilityReads)} does not ledger it.");
            }

            return key;
        }
    }
}
