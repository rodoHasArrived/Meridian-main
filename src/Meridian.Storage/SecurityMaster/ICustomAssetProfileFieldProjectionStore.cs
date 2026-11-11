namespace Meridian.Storage.SecurityMaster;

/// <summary>
/// Queries the profile-keyed CustomAsset projection (<c>custom_asset_profile_field_projection</c>,
/// migration 064): one row per governed <c>profileFields</c> key of a profile-backed CustomAsset.
/// A CustomAsset without a complete profile envelope has no rows; its terms JSON remains the only copy.
/// </summary>
public interface ICustomAssetProfileFieldProjectionStore
{
    /// <summary>The security's projected profile fields, ordered by field key; empty when it has none.</summary>
    Task<IReadOnlyList<CustomAssetProfileFieldProjectionRow>> GetProfileFieldsAsync(Guid securityId, CancellationToken ct = default);

    /// <summary>
    /// Every projected field of securities pinned to <paramref name="customProfileId"/>, optionally
    /// one <paramref name="profileVersion"/>, ordered by security then field key. The profile id
    /// matches exactly, as stored.
    /// </summary>
    Task<IReadOnlyList<CustomAssetProfileFieldProjectionRow>> GetByProfileAsync(
        string customProfileId,
        int? profileVersion = null,
        CancellationToken ct = default);

    /// <summary>
    /// The rows whose string-valued field <paramref name="fieldKey"/> of <paramref name="customProfileId"/>
    /// equals <paramref name="textValue"/> exactly (case-sensitive, as stored).
    /// </summary>
    Task<IReadOnlyList<CustomAssetProfileFieldProjectionRow>> FindByTextValueAsync(
        string customProfileId,
        string fieldKey,
        string textValue,
        CancellationToken ct = default);
}

/// <param name="ValueKind">The stored value's JSON kind: string, number, boolean, null, object or array.</param>
/// <param name="TextValue">The string value when <paramref name="ValueKind"/> is <c>string</c>, otherwise null.</param>
/// <param name="NumericValue">
/// The number when <paramref name="ValueKind"/> is <c>number</c>, as a <see cref="decimal"/>
/// (rounded beyond decimal precision); null for other kinds and for a number outside decimal range.
/// <paramref name="RawValueJson"/> and the table's unbounded <c>numeric_value</c> keep full precision.
/// </param>
/// <param name="BooleanValue">The boolean when <paramref name="ValueKind"/> is <c>boolean</c>, otherwise null.</param>
/// <param name="RawValueJson">The stored JSON value as PostgreSQL renders the <c>jsonb</c>.</param>
public sealed record CustomAssetProfileFieldProjectionRow(
    Guid SecurityId,
    string CustomProfileId,
    int ProfileVersion,
    string FieldKey,
    string ValueKind,
    string? TextValue,
    decimal? NumericValue,
    bool? BooleanValue,
    string RawValueJson,
    long Version);
