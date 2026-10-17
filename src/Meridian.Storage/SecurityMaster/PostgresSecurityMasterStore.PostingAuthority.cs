using Meridian.Contracts.SecurityMaster;
using Meridian.Ledger;
using Npgsql;

namespace Meridian.Storage.SecurityMaster;

public sealed partial class PostgresSecurityMasterStore
{
    /// <summary>Hold every hashed reference input stable through the journal transaction.</summary>
    internal async Task<SecurityProjectionRecord?> LockForLotPostingAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, Guid securityId, CancellationToken ct)
    {
        var configured = new NpgsqlConnectionStringBuilder(_options.ConnectionString);
        if (connection.Host != configured.Host || connection.Port != configured.Port || connection.Database != configured.Database)
            throw new LedgerValidationException("Atomic lot posting requires Security Master in the same PostgreSQL database.");
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        // FOR UPDATE also conflicts with the foreign-key key-share lock used by child inserts.
        // FOR SHARE on the parent alone neither prevents new aliases/identifiers nor locks the
        // existing child rows included in SecurityHash. Keep the parent-before-children ordering
        // shared by projection replacement and standalone alias writes.
        command.CommandText = $"select security_id from {Qualified("securities")} where security_id = @id for update;";
        command.Parameters.AddWithValue("id", securityId);
        if (await command.ExecuteScalarAsync(ct).ConfigureAwait(false) is null)
            return null;

        command.CommandText = $"select security_id from {Qualified("security_identifiers")} where security_id = @id order by identifier_kind, identifier_value, valid_from for share;";
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        command.CommandText = $"select alias_id from {Qualified("security_aliases")} where security_id = @id order by alias_id for share;";
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        return await GetProjectionCoreAsync(connection, securityId, ct).ConfigureAwait(false);
    }
}
