namespace Meridian.Contracts.SecurityMaster;

/// <summary>How the flat decode reads one bound key. The names follow the schema-bound reader.</summary>
public enum SecurityTermRead
{
    RequiredString,
    OptionalString,
    /// <summary>An optional string read through the convention-aware reader.</summary>
    ConventionString,
    RequiredDecimal,
    OptionalDecimal,
    /// <summary>An optional decimal a write refuses when malformed instead of reading as absent.</summary>
    StrictDecimal,
    RequiredInt,
    OptionalInt,
    StrictInt,
    RequiredDate,
    OptionalDate,
    StrictDate,
    /// <summary>A required guid held as a security id.</summary>
    RequiredSecurityId,
    OptionalBoolean,
    /// <summary>An optional boolean the codec reads and writes as <c>false</c> when absent.</summary>
    BooleanDefaultFalse,
    /// <summary>A day-count convention: read through the governed parser, written as its term label.</summary>
    DayCount,
}

/// <summary>
/// One member of a per-class record and the flat key that carries it.
/// </summary>
/// <param name="Path">
/// The member, as <c>Field.Member</c> over the class's record; or a whole module, as <c>Field</c>,
/// when <paramref name="DecodeExpression"/> builds the module through a helper.
/// </param>
/// <param name="Key">The flat key, or null for a member the decode sets without reading one.</param>
/// <param name="Read">How the decode reads the key; null when <paramref name="DecodeExpression"/> says.</param>
/// <param name="DecodeExpression">
/// The decode expression verbatim, for a member the row derives or fixes instead of reading plainly
/// (a constant issuer, a style parsed through its own vocabulary).
/// </param>
/// <param name="WriteVia">
/// The record's own computed property the writer reads instead of the member, where that property
/// supplies the written form (a required date written as its default when the record lacks it).
/// </param>
/// <param name="DecodeOnly">Whether the key is written by another row and only read here.</param>
/// <param name="WriteOnly">
/// Whether the key is only written here: read by a whole-module row of the same field, or by the
/// decode row of the same member when one member is written as several keys.
/// </param>
/// <param name="WriteExpression">
/// The written value verbatim, as an F# expression over <c>terms</c>, where the written form is a
/// conversion no record property states (a label of a union, one arm of a coupon structure).
/// </param>
/// <param name="Items">
/// For a list member, the keys each written entry carries and the entry members they come from.
/// </param>
public sealed record SecurityTermBinding(
    string Path,
    string? Key,
    SecurityTermRead? Read,
    string? DecodeExpression = null,
    string? WriteVia = null,
    bool DecodeOnly = false,
    bool WriteOnly = false,
    string? WriteExpression = null,
    IReadOnlyList<SecurityListItemBinding>? Items = null);

/// <summary>One key of a written list entry and the entry member it carries.</summary>
public sealed record SecurityListItemBinding(string Key, string Member);

/// <summary>Inputs a class's decode reads beyond the schema-bound terms.</summary>
[Flags]
public enum SecurityCodecInputs
{
    None = 0,
    /// <summary>The row's versioned-term decoding, for versioned containers inside a list entry.</summary>
    VersionedTerms = 1,
    /// <summary>The parsed and validated economic schedules (principal, factor, step coupon).</summary>
    EconomicSchedules = 2,
    /// <summary>The profile-resolved terms document, for a list read from it.</summary>
    ResolvedTermsDocument = 4,
}

/// <summary>The bindings of one asset class.</summary>
/// <param name="ReadsResolvedTerms">
/// Whether the class reads its keys from the profile-resolved terms document rather than the root.
/// </param>
/// <param name="VersionedSchedules">
/// Whether the class carries the declared-schedule containers (ADR-024: the
/// <c>scheduleConventions</c> block and the call and put schedules), which the decode reads only at
/// the row's terms version and the writer writes only from the version that declares them.
/// </param>
/// <param name="Inputs">What else the decode reads; see <see cref="SecurityCodecInputs"/>.</param>
/// <param name="WritesConventionsFlag">
/// Whether the writer takes the schedule-conventions version flag for a container nested in a
/// list entry (a swap leg's block) rather than at the root.
/// </param>
public sealed record SecurityClassCodecBinding(
    string AssetClass,
    bool ReadsResolvedTerms,
    IReadOnlyList<SecurityTermBinding> Members,
    bool VersionedSchedules = false,
    SecurityCodecInputs Inputs = SecurityCodecInputs.None,
    bool WritesConventionsFlag = false);

/// <summary>
/// The single statement of which flat key carries which member of a per-class record, for the
/// classes whose flat codec is generated (ADR-027 stage 3). Both codec arms are generated from
/// this table: the decode arm in <c>SecurityMasterMapping.GeneratedDecode.cs</c> and the writer arm
/// in <c>Interop.SecurityMaster.GeneratedWriter.fs</c>. Neither file is edited by hand;
/// <c>SecurityFlatCodecGenerationTests</c> regenerates both and fails when the committed source
/// differs.
/// <para>
/// A member with no row is one the class's flat row does not carry, and decodes as absent. Key
/// types and requiredness are the schema's (<see cref="SecurityAssetTermsSchema"/>); a row names
/// only what the schema does not say: the member, the reader's tolerance, and the few values a
/// class fixes or derives.
/// </para>
/// </summary>
public static class SecurityAssetTermsCodecBindings
{
    /// <summary>The generated classes, in their retained declaration order.</summary>
    public static IReadOnlyList<SecurityClassCodecBinding> Classes { get; } =
        SecurityAssetClassModules.All.Where(static module => module.CodecBinding is not null)
            .OrderBy(static module => module.CodecOrder)
            .Select(static module => module.CodecBinding!).ToArray();

    /// <summary>The generated classes by name.</summary>
    public static IReadOnlyDictionary<string, SecurityClassCodecBinding> ByAssetClass { get; } =
        Classes.ToDictionary(static binding => binding.AssetClass, StringComparer.Ordinal);
}
