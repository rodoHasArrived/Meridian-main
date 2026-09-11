using Meridian.Contracts.SecurityMaster;

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
        // Kinds detection excludes from ambiguity pairing are skipped here through the same
        // shared contract, so querying them could only load projections to no effect: distinct
        // securities of one issuer legitimately share issuer-scoped kinds (CIK, LEI), and an
        // Unknown kind is this node's degraded reading of ANY newer node's kind — ambiguity
        // among a future kind's claims belongs to the newer nodes that still read the kind.
        var keys = identifiers
            .Where(static identifier => !SecurityIdentifierNormalizer.IsExcludedFromAmbiguityPairing(identifier.Kind))
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
        // The exclusion is a pure set subtraction on the returned ids, so it is applied
        // client-side rather than shipped as a query parameter: during a full projection warm
        // the exclusion set is the entire security universe, and resending that N-element
        // UUID array with every 200-key chunk made the warm quadratic in transfer and
        // allocation. Each chunk returns at most its keys' owners, so filtering here is
        // linear in the rows actually read.
        var excluded = excludedSecurityIds as IReadOnlySet<Guid> ?? new HashSet<Guid>(excludedSecurityIds);
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
                where {string.Join(" or ", predicates)};
                """;

            await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                var securityId = reader.GetGuid(0);
                if (!excluded.Contains(securityId))
                {
                    candidateIds.Add(securityId);
                }
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
