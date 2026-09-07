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
        // Issuer-scoped kinds (CIK, LEI) are shared across one issuer's securities by contract
        // and detection unconditionally discards their pairs, so querying them would only load
        // every sibling projection of a large filer or fund complex to no effect.
        var keys = identifiers
            .Where(static identifier => !SecurityIdentifierNormalizer.IsIssuerScopedKind(identifier.Kind))
            .Select(static identifier => (
                Kind: identifier.Kind.ToString(),
                MatchesStoredKind: identifier.Kind != SecurityIdentifierKind.Unknown,
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
                // A kind written by a newer node degrades to Unknown when this node reads it
                // back while the stored row keeps the newer kind's original text, so matching
                // "Unknown" against identifier_kind would match nothing that exists. Such keys
                // match on normalized value alone: a cross-kind value collision can pull in an
                // extra candidate, but detection's own kind-aware keys discard it, which beats
                // silently skipping a forward-compatible row until a full-universe refresh.
                if (key.MatchesStoredKind)
                {
                    predicates[index] = key.IsScoped
                        ? $"(i.identifier_kind = @kind_{index} and i.normalized_identifier_value = @value_{index} and coalesce(i.normalized_provider, '') = @scope_{index})"
                        : $"(i.identifier_kind = @kind_{index} and i.normalized_identifier_value = @value_{index})";
                    command.Parameters.AddWithValue($"kind_{index}", key.Kind);
                }
                else
                {
                    predicates[index] = $"(i.normalized_identifier_value = @value_{index})";
                }

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
