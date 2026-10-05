using Meridian.Contracts.SecurityMaster;
using Meridian.Ledger;
using Npgsql;

namespace Meridian.Storage.SecurityMaster;

public sealed partial class PostgresSecurityMasterStore
{
    /// <summary>Hold reference authority through the journal transaction, blocking concurrent projection replacement.</summary>
    internal async Task<SecurityProjectionRecord?> LockForLotPostingAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, Guid securityId, CancellationToken ct)
    {
        var configured = new NpgsqlConnectionStringBuilder(_options.ConnectionString);
        if (connection.Host != configured.Host || connection.Port != configured.Port || connection.Database != configured.Database)
            throw new LedgerValidationException("Atomic lot posting requires Security Master in the same PostgreSQL database.");
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"select security_id from {Qualified("securities")} where security_id = @id for share;";
        command.Parameters.AddWithValue("id", securityId);
        if (await command.ExecuteScalarAsync(ct).ConfigureAwait(false) is null)
            return null;
        return await GetProjectionCoreAsync(connection, securityId, ct).ConfigureAwait(false);
    }
}
