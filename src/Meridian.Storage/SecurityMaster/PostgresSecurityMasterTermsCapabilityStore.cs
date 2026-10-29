using Meridian.Contracts.SecurityMaster;
using Npgsql;

namespace Meridian.Storage.SecurityMaster;

/// <summary>Reads the capability floor without migrating, activating or modifying it.</summary>
public sealed class PostgresSecurityMasterTermsCapabilityStore : ISecurityMasterTermsCapabilityStore
{
    private readonly SecurityMasterOptions _options;

    /// <summary>Uses the same connection and schema as the configured Security Master writer.</summary>
    public PostgresSecurityMasterTermsCapabilityStore(SecurityMasterOptions options)
        => _options = options ?? throw new ArgumentNullException(nameof(options));

    /// <inheritdoc />
    public async Task<SecurityMasterTermsCapabilityState> ReadAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ConnectionString))
            throw new InvalidOperationException("Security Master capability-floor persistence is not configured.");
        await using var connection = new NpgsqlConnection(_options.ConnectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        // Resolve the configured identifier with PostgreSQL's own quoting/case rules, then quote
        // the returned literal name. The configuration text is never interpolated into SQL.
        command.CommandText = """
            select nspname,
                   to_regclass(pg_catalog.format('%I.security_master_terms_capability_floor', nspname)) is not null
            from pg_catalog.pg_namespace where oid = to_regnamespace(@schema);
            """;
        command.Parameters.AddWithValue("schema", _options.Schema);
        string schema;
        await using (var lookup = await command.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            if (!await lookup.ReadAsync(ct).ConfigureAwait(false) || !lookup.GetBoolean(1))
                return new SecurityMasterTermsCapabilityState(false, null);
            schema = lookup.GetString(0);
        }
        command.Parameters.Clear();
        using var identifierBuilder = new NpgsqlCommandBuilder();
        var qualified = identifierBuilder.QuoteIdentifier(schema) + ".security_master_terms_capability_floor";
        command.CommandText = $"""
            select singleton, minimum_declared_mapping_version, minimum_kernel_version,
                   change_record, updated_by, updated_at
            from {qualified};
            """;
        // A drop/change after the catalog check is a verification failure, not a pre-migration
        // state. Do not translate database errors into permission to start a writer.
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
            return new SecurityMasterTermsCapabilityState(true, null);
        if (!reader.GetBoolean(0))
            throw new InvalidDataException("Security Master capability-floor singleton is invalid.");
        var floor = new SecurityMasterTermsCapabilityFloor(
            reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4),
            new DateTimeOffset(reader.GetDateTime(5), TimeSpan.Zero));
        if (await reader.ReadAsync(ct).ConfigureAwait(false))
            throw new InvalidDataException("Security Master capability floor contains more than one row.");
        return new SecurityMasterTermsCapabilityState(true, floor);
    }
}
