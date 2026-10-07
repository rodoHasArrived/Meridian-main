using Meridian.Contracts.AssetOperations;
using Meridian.Ledger;
using Npgsql;

namespace Meridian.Storage.AssetOperations;

public sealed partial class PostgresAssetOperationsProjectionStore
{
    internal Task<BookPositionDto?> LockForLotPostingAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, Guid positionId, CancellationToken ct)
    {
        var configured = new NpgsqlConnectionStringBuilder(_options.ConnectionString);
        if (connection.Host != configured.Host || connection.Port != configured.Port || connection.Database != configured.Database)
            throw new LedgerValidationException("Atomic lot posting requires book positions in the same PostgreSQL database.");
        return ReadBookPositionAsync(connection, transaction, positionId, true, ct);
    }
}
