using Meridian.Contracts.SecurityMaster;
using NpgsqlTypes;

namespace Meridian.Storage.SecurityMaster;

public sealed partial class PostgresSecurityMasterStore
{
    public async Task<IReadOnlyList<SecurityProjectionRecord>> FindIdentifierCandidatesAsync(
        IReadOnlyList<SecurityIdentifierDto> identifiers,
        IReadOnlyCollection<Guid> excludedSecurityIds,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(identifiers);
        ArgumentNullException.ThrowIfNull(excludedSecurityIds);

        // Normalize before reaching SQL so this lookup has exactly the same identity semantics as
        // GetByIdentifierAsync and can use ix_security_identifiers_normalized_lookup. Chunking
        // bounds command text and parameter counts during a full projection rebuild.
        var keys = identifiers
            .Select(static identifier => (
                Kind: identifier.Kind.ToString(),
                Value: SecurityIdentifierNormalizer.GetOrComputeNormalizedValue(identifier),
                Scope: SecurityIdentifierNormalizer.GetIdentityScope(identifier),
                IsScoped: SecurityIdentifierNormalizer.IsProviderScoped(identifier.Kind)))
            .Where(static key => key.Value.Length > 0)
            .Distinct()
            .ToArray();
        if (keys.Length == 0)
        {
            return Array.Empty<SecurityProjectionRecord>();
        }

        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        var candidateIds = new HashSet<Guid>();
        const int keysPerQuery = 200;
        for (var offset = 0; offset < keys.Length; offset += keysPerQuery)
        {
            var count = Math.Min(keysPerQuery, keys.Length - offset);
            await using var command = connection.CreateCommand();
            var predicates = new string[count];
            for (var index = 0; index < count; index++)
            {
                var key = keys[offset + index];
                predicates[index] = key.IsScoped
                    ? $"(i.identifier_kind = @kind_{index} and i.normalized_identifier_value = @value_{index} and coalesce(i.normalized_provider, '') = @scope_{index})"
                    : $"(i.identifier_kind = @kind_{index} and i.normalized_identifier_value = @value_{index})";
                command.Parameters.AddWithValue($"kind_{index}", key.Kind);
                command.Parameters.AddWithValue($"value_{index}", key.Value);
                if (key.IsScoped)
                {
                    command.Parameters.AddWithValue($"scope_{index}", key.Scope);
                }
            }

            command.CommandText =
                $"""
                select distinct i.security_id
                from {Qualified("security_identifiers")} i
                where ({string.Join(" or ", predicates)})
                  and not (i.security_id = any(@excluded_security_ids));
                """;
            command.Parameters.AddWithValue(
                "excluded_security_ids",
                NpgsqlDbType.Array | NpgsqlDbType.Uuid,
                excludedSecurityIds.ToArray());

            await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                candidateIds.Add(reader.GetGuid(0));
            }
        }

        var results = new List<SecurityProjectionRecord>(candidateIds.Count);
        foreach (var securityId in candidateIds.Order())
        {
            var projection = await GetProjectionCoreAsync(connection, securityId, ct).ConfigureAwait(false);
            if (projection is not null)
            {
                results.Add(projection);
            }
        }

        return results;
    }
}
