using System.Text.Json;
using Meridian.Contracts.SecurityMaster;
using Npgsql;
using NpgsqlTypes;

namespace Meridian.Storage.SecurityMaster;

public sealed partial class PostgresSecurityMasterStore
{
    internal const string CustomAssetAssetClass = "CustomAsset";
    internal const string CustomAssetProfileFieldProjectionTable = "custom_asset_profile_field_projection";

    /// <summary>
    /// One governed profile field as the projection stores it: the key, the JSON kind of the stored
    /// value (<c>string</c>, <c>number</c>, <c>boolean</c>, <c>null</c>, <c>object</c> or
    /// <c>array</c>, the <c>jsonb_typeof</c> vocabulary), and the stored value's raw JSON. The typed
    /// columns are derived from the raw value in SQL, so a value keeps its stored meaning: a string
    /// stays a string even when it looks like a date, and a number keeps its full precision.
    /// </summary>
    internal sealed record CustomAssetProfileFieldValue(string FieldKey, string ValueKind, string RawJson);

    /// <summary>The rows a profile-backed CustomAsset record projects, one per <c>profileFields</c> key.</summary>
    internal sealed record CustomAssetProfileFieldProjectionPlan(
        string CustomProfileId,
        int ProfileVersion,
        IReadOnlyList<CustomAssetProfileFieldValue> Fields);

    /// <summary>
    /// Decodes a CustomAsset record's profile envelope (<c>{customProfileId, profileVersion,
    /// profileFields:{…}}</c>) into its profile-field rows, or reports that it has none.
    /// <para>
    /// There is no projection when the record carries another class, or when the terms are not a
    /// complete envelope: a blank or non-string <c>customProfileId</c>, a <c>profileVersion</c> that
    /// is not a 32-bit integer, or a missing or non-object <c>profileFields</c>. A legacy or unmapped
    /// CustomAsset row without an envelope therefore projects nothing and keeps its terms JSON as the
    /// only copy; the caller deletes any rows it previously had. An envelope with an empty
    /// <c>profileFields</c> object projects zero rows.
    /// </para>
    /// <para>
    /// Field keys are read with <see cref="JsonElement.TryGetProperty(string, out JsonElement)"/>, the
    /// lookup every other reader of <c>profileFields</c> uses, so a document repeating a key projects
    /// the same value those readers see. Rows follow first-occurrence key order. The value is
    /// projected whatever its kind: the profile definition, not this writer, governs field types.
    /// </para>
    /// </summary>
    internal static bool TryBuildCustomAssetProfileFieldProjection(
        SecurityProjectionRecord record,
        out CustomAssetProfileFieldProjectionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(record);
        plan = null!;

        if (!string.Equals(record.AssetClass, CustomAssetAssetClass, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var terms = record.AssetSpecificTerms;
        if (SecurityAssetClassTermsUpgrader.TryUpgradeForRead(record.AssetClass, terms, out var upgradedTerms))
        {
            terms = upgradedTerms;
        }

        if (terms.ValueKind != JsonValueKind.Object
            || !terms.TryGetProperty("customProfileId", out var profileIdElement)
            || profileIdElement.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(profileIdElement.GetString())
            || !terms.TryGetProperty("profileVersion", out var versionElement)
            || versionElement.ValueKind != JsonValueKind.Number
            || !versionElement.TryGetInt32(out var profileVersion)
            || !terms.TryGetProperty("profileFields", out var profileFields)
            || profileFields.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var fields = new List<CustomAssetProfileFieldValue>();
        foreach (var property in profileFields.EnumerateObject())
        {
            if (!seen.Add(property.Name))
            {
                continue;
            }

            var value = profileFields.GetProperty(property.Name);
            fields.Add(new(property.Name, ValueKindName(value.ValueKind), value.GetRawText()));
        }

        plan = new(profileIdElement.GetString()!, profileVersion, fields);
        return true;
    }

    private static string ValueKindName(JsonValueKind kind)
        => kind switch
        {
            JsonValueKind.String => "string",
            JsonValueKind.Number => "number",
            JsonValueKind.True or JsonValueKind.False => "boolean",
            JsonValueKind.Null => "null",
            JsonValueKind.Object => "object",
            JsonValueKind.Array => "array",
            _ => throw new InvalidOperationException($"Unexpected JSON value kind '{kind}' in profileFields.")
        };

    /// <summary>
    /// The CustomAsset writer: replaces the security's profile-field rows with the ones its current
    /// envelope declares, or clears them when the record has no projection (another class after a
    /// class change, or a row without an envelope).
    /// </summary>
    private async Task UpsertCustomAssetProfileFieldProjectionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        SecurityProjectionRecord record,
        CancellationToken ct)
    {
        await using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = $"delete from {Qualified(CustomAssetProfileFieldProjectionTable)} where security_id = @security_id;";
            delete.Parameters.AddWithValue("security_id", record.SecurityId);
            await delete.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        if (!TryBuildCustomAssetProfileFieldProjection(record, out var plan) || plan.Fields.Count == 0)
        {
            return;
        }

        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText =
            $$"""
            insert into {{Qualified(CustomAssetProfileFieldProjectionTable)}} (
                security_id, custom_profile_id, profile_version, field_key, value_kind,
                text_value, numeric_value, boolean_value, raw_value, version)
            select
                @security_id, @custom_profile_id, @profile_version, f.field_key, f.value_kind,
                case when f.value_kind = 'string' then f.raw_value #>> '{}' end,
                case when f.value_kind = 'number' then (f.raw_value #>> '{}')::numeric end,
                case when f.value_kind = 'boolean' then (f.raw_value #>> '{}')::boolean end,
                f.raw_value,
                @version
            from unnest(@field_keys, @value_kinds, @raw_values) as f(field_key, value_kind, raw_value);
            """;
        insert.Parameters.AddWithValue("security_id", record.SecurityId);
        insert.Parameters.AddWithValue("custom_profile_id", plan.CustomProfileId);
        insert.Parameters.AddWithValue("profile_version", plan.ProfileVersion);
        insert.Parameters.AddWithValue("version", record.Version);
        insert.Parameters.Add(new NpgsqlParameter("field_keys", NpgsqlDbType.Array | NpgsqlDbType.Text)
        {
            Value = plan.Fields.Select(static field => field.FieldKey).ToArray()
        });
        insert.Parameters.Add(new NpgsqlParameter("value_kinds", NpgsqlDbType.Array | NpgsqlDbType.Text)
        {
            Value = plan.Fields.Select(static field => field.ValueKind).ToArray()
        });
        insert.Parameters.Add(new NpgsqlParameter("raw_values", NpgsqlDbType.Array | NpgsqlDbType.Jsonb)
        {
            Value = plan.Fields.Select(static field => field.RawJson).ToArray()
        });
        await insert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
}
