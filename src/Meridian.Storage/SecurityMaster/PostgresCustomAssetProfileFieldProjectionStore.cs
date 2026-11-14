using System.Globalization;
using Meridian.Contracts.SecurityMaster;
using Npgsql;

namespace Meridian.Storage.SecurityMaster;

/// <summary>
/// Reads the profile-keyed CustomAsset projection from PostgreSQL. The table is written by the
/// specialized CustomAsset writer in <see cref="PostgresSecurityMasterStore"/>, not by a
/// <see cref="SecurityTermsProjectionRegistry"/> descriptor, so its column list is stated here.
/// </summary>
public sealed class PostgresCustomAssetProfileFieldProjectionStore : ICustomAssetProfileFieldProjectionStore
{
    internal const string AssetClass = PostgresSecurityMasterStore.CustomAssetAssetClass;

    private readonly PostgresReferenceProjectionReader _reader;

    public PostgresCustomAssetProfileFieldProjectionStore(SecurityMasterOptions options)
    {
        _reader = new PostgresReferenceProjectionReader(options);
    }

    public Task<IReadOnlyList<CustomAssetProfileFieldProjectionRow>> GetProfileFieldsAsync(Guid securityId, CancellationToken ct = default)
        => _reader.QueryAsync(
            SelectSql("where security_id = @security_id order by field_key"),
            [new NpgsqlParameter("security_id", securityId)],
            MapRow,
            ct);

    public Task<IReadOnlyList<CustomAssetProfileFieldProjectionRow>> GetByProfileAsync(
        string customProfileId,
        int? profileVersion = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customProfileId);
        return profileVersion is { } version
            ? _reader.QueryAsync(
                SelectSql("where custom_profile_id = @custom_profile_id and profile_version = @profile_version order by security_id, field_key"),
                [new NpgsqlParameter("custom_profile_id", customProfileId), new NpgsqlParameter("profile_version", version)],
                MapRow,
                ct)
            : _reader.QueryAsync(
                SelectSql("where custom_profile_id = @custom_profile_id order by security_id, field_key"),
                [new NpgsqlParameter("custom_profile_id", customProfileId)],
                MapRow,
                ct);
    }

    public Task<IReadOnlyList<CustomAssetProfileFieldProjectionRow>> FindByTextValueAsync(
        string customProfileId,
        string fieldKey,
        string textValue,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customProfileId);
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldKey);
        ArgumentNullException.ThrowIfNull(textValue);
        return _reader.QueryAsync(
            SelectSql(
                """
                where custom_profile_id = @custom_profile_id and field_key = @field_key
                  and md5(text_value) = md5(@text_value) and text_value = @text_value
                order by security_id
                """),
            [
                new NpgsqlParameter("custom_profile_id", customProfileId),
                new NpgsqlParameter("field_key", fieldKey),
                new NpgsqlParameter("text_value", textValue)
            ],
            MapRow,
            ct);
    }

    /// <summary>
    /// The select over the projection table. The number is read as text and parsed, so a value
    /// beyond <see cref="decimal"/> range reads as a null <c>NumericValue</c> rather than failing the
    /// whole query; <c>RawValueJson</c> keeps it at full precision.
    /// </summary>
    internal string SelectSql(string predicateSql)
        => "select security_id, custom_profile_id, profile_version, field_key, value_kind, text_value, "
           + "numeric_value::text as numeric_text, boolean_value, raw_value::text as raw_value_json, version "
           + $"from {_reader.Qualified(PostgresSecurityMasterStore.CustomAssetProfileFieldProjectionTable)} {predicateSql};";

    internal static CustomAssetProfileFieldProjectionRow MapRow(ProjectionRow row)
        => new(
            row.Guid("security_id"),
            row.String("custom_profile_id"),
            row.Int32("profile_version"),
            row.String("field_key"),
            row.String("value_kind"),
            row.NullableString("text_value"),
            ParseNumeric(row.NullableString("numeric_text")),
            row.NullableBoolean("boolean_value"),
            row.String("raw_value_json"),
            row.Int64("version"));

    internal static decimal? ParseNumeric(string? text)
        => text is not null
           && decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
}
