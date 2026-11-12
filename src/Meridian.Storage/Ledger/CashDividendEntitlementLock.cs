using Npgsql;

namespace Meridian.Storage.Ledger;

/// <summary>Orders complete-book entitlement reads before native book, period and position locks.</summary>
internal static class CashDividendEntitlementLock
{
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
}
