using System.Text.Json;
using NpgsqlTypes;

namespace Meridian.Storage.SecurityMaster;

public sealed partial class PostgresSecurityMasterStore : ISecurityAssetProfileUsageStore
{
    // Match the existing .NET required-field rule, including tabs and Unicode whitespace.
    private static readonly string ProfilePresenceWhitespace = new string(Enumerable.Range(0, char.MaxValue + 1)
        .Select(static value => (char)value).Where(char.IsWhiteSpace).ToArray());
    /// <summary>
    /// Counts securities, including complete zero-field envelopes. The per-field projection cannot
    /// supply this population. One SQL statement observes one database snapshot and returns no values.
    /// </summary>
    public Task<IReadOnlyList<SecurityAssetProfileUsageCount>> GetProfileUsageCountsAsync(CancellationToken ct = default)
        => GetProfileUsageSnapshotAsync([], ct);

    public async Task<IReadOnlyList<SecurityAssetProfileUsageCount>> GetProfileUsageSnapshotAsync(
        IReadOnlyList<SecurityAssetProfileUsageRequirements> requirements, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            with requirements as (
                select value ->> 'ProfileId' as profile_id, (value ->> 'ProfileVersion')::integer as profile_version,
                    value -> 'RequiredFieldKeys' as required_keys
                from jsonb_array_elements(@requirements::jsonb)
            ), envelopes as (
                select status, asset_specific_terms as terms,
                    case when jsonb_typeof(asset_specific_terms -> 'customProfileId') = 'string'
                         and btrim(asset_specific_terms ->> 'customProfileId', @blank_characters) <> ''
                         then asset_specific_terms ->> 'customProfileId' end as profile_id,
                    case when jsonb_typeof(asset_specific_terms -> 'profileVersion') = 'number'
                         and (asset_specific_terms ->> 'profileVersion') ~ '^[0-9]+$'
                         and length(asset_specific_terms ->> 'profileVersion') <= 10
                         then case when (asset_specific_terms ->> 'profileVersion')::numeric between 1 and 2147483647
                              then (asset_specific_terms ->> 'profileVersion')::integer end end as profile_version
                from {Qualified("securities")} where lower(asset_class) = lower(@asset_class)
            ), classified as (
                select profile_id, profile_version, status, terms,
                    case when profile_id is null then 'Missing or malformed profile identity'
                         when profile_version is null then 'Missing or malformed profile version'
                         when jsonb_typeof(terms -> 'profileFields') is distinct from 'object'
                              then 'Missing or malformed profile fields envelope' end as envelope_issue
                from envelopes
            ), quality as (
                select c.*, r.required_keys,
                    case when c.envelope_issue is null and r.profile_id is not null then
                        array(select key from jsonb_array_elements_text(r.required_keys) key
                            where c.terms -> 'profileFields' -> key is null
                                or jsonb_typeof(c.terms -> 'profileFields' -> key) = 'null'
                                or (jsonb_typeof(c.terms -> 'profileFields' -> key) = 'string'
                                    and btrim(c.terms -> 'profileFields' ->> key, @blank_characters) = ''))
                    end as missing_keys
                from classified c left join requirements r on lower(r.profile_id) = lower(c.profile_id)
                    and r.profile_version = c.profile_version
            ), missing_counts as (
                select profile_id, profile_version, envelope_issue, key, count(*) as missing_count
                from quality cross join lateral unnest(missing_keys) key
                where status = 'Active' group by profile_id, profile_version, envelope_issue, key
            ), missing_fields as (
                select profile_id, profile_version, envelope_issue, jsonb_object_agg(key, missing_count) as fields
                from missing_counts group by profile_id, profile_version, envelope_issue
            )
            select q.profile_id, q.profile_version, q.envelope_issue,
                count(*) filter (where q.status = 'Active'),
                count(*) filter (where q.status = 'Inactive'),
                count(*) filter (where q.status not in ('Active', 'Inactive') or q.status is null),
                count(*) filter (where q.status = 'Active' and cardinality(q.missing_keys) = 0),
                count(*) filter (where q.status = 'Active' and cardinality(q.missing_keys) > 0),
                count(*) filter (where q.status = 'Active' and q.missing_keys is null),
                coalesce(m.fields, jsonb_build_object())::text, bool_or(q.required_keys is not null)
            from quality q left join missing_fields m on m.profile_id is not distinct from q.profile_id
                and m.profile_version is not distinct from q.profile_version and m.envelope_issue is not distinct from q.envelope_issue
            group by q.profile_id, q.profile_version, q.envelope_issue, m.fields
            order by q.profile_id nulls first, q.profile_version nulls first, q.envelope_issue;
            """;
        command.Parameters.AddWithValue("asset_class", CustomAssetAssetClass);
        command.Parameters.AddWithValue("requirements", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(requirements));
        command.Parameters.AddWithValue("blank_characters", ProfilePresenceWhitespace);
        var counts = new List<SecurityAssetProfileUsageCount>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            counts.Add(new(reader.IsDBNull(0) ? null : reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetInt32(1),
                reader.IsDBNull(2) ? null : reader.GetString(2), reader.GetInt64(3), reader.GetInt64(4), reader.GetInt64(5))
            {
                CompleteActiveRecordCount = reader.GetBoolean(10) ? reader.GetInt64(6) : null,
                IncompleteActiveRecordCount = reader.GetBoolean(10) ? reader.GetInt64(7) : null,
                UnknownActiveRecordCount = reader.GetInt64(8),
                MissingRequiredFieldCounts = reader.GetBoolean(10) ? JsonSerializer.Deserialize<Dictionary<string, long>>(reader.GetString(9)) : null
            });
        return counts;
    }
}
