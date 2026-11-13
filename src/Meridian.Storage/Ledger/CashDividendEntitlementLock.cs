using Npgsql;
using System.Data;

namespace Meridian.Storage.Ledger;

/// <summary>Orders complete-book entitlement reads before native book, period and position locks.</summary>
internal static class CashDividendEntitlementLock
{
    /// <summary>Acquires entitlement before starting a Serializable transaction without fixing its snapshot.</summary>
    internal static async Task<IAsyncDisposable> AcquireSessionSharedAsync(NpgsqlConnection connection, Guid bookId, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "select pg_advisory_lock_shared(hashtextextended(@scope, 0))";
        command.Parameters.AddWithValue("scope", $"cash-dividend-entitlement-book:{bookId:D}");
        var lease = new SessionLease(connection, bookId);
        try
        {
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            return lease;
        }
        catch
        {
            // Cancellation can race the backend's successful lock grant. Clean that grant
            // without the canceled request token before the connection may return to its pool.
            await lease.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    internal static async Task AcquireAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        Guid bookId, bool exclusive, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = exclusive
            ? "select pg_advisory_xact_lock(hashtextextended(@scope, 0))"
            : "select pg_advisory_xact_lock_shared(hashtextextended(@scope, 0))";
        command.Parameters.AddWithValue("scope", $"cash-dividend-entitlement-book:{bookId:D}");
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private sealed class SessionLease(NpgsqlConnection connection, Guid bookId) : IAsyncDisposable
    {
        private int _disposed;
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0 || connection.State != ConnectionState.Open) return;
            await using var command = connection.CreateCommand();
            command.CommandTimeout = 5;
            command.CommandText = "select pg_advisory_unlock_shared(hashtextextended(@scope, 0))";
            command.Parameters.AddWithValue("scope", $"cash-dividend-entitlement-book:{bookId:D}");
            try { _ = await command.ExecuteScalarAsync(CancellationToken.None).ConfigureAwait(false); }
            catch
            {
                // Do not let an uncertain session lock survive on a pooled connector, including
                // deployments which explicitly disabled normal connection reset-on-close.
                NpgsqlConnection.ClearPool(connection);
                await connection.CloseAsync().ConfigureAwait(false);
                throw;
            }
        }
    }
}
