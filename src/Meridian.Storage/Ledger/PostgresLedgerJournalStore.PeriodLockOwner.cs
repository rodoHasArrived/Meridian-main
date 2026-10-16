using System.Data;

namespace Meridian.Storage.Ledger;

public sealed partial class PostgresLedgerJournalStore
{
    /// <summary>Reads the retained close actor under the same tenant guard as the period.</summary>
    public async Task<string?> GetPeriodLockOwnerAsync(Guid periodId, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct).ConfigureAwait(false);
        var period = await LoadPeriodAsync(connection, transaction, periodId, false, ResolveCallerTenant(), ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Authoritative accounting period is unavailable.");
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"select closed_by from {Qualified("period_close_events")} where period_id = @period and new_status = @status and period_version = @version order by recorded_at desc, event_id desc limit 1";
        command.Parameters.AddWithValue("period", periodId);
        command.Parameters.AddWithValue("status", period.Status);
        command.Parameters.AddWithValue("version", period.Version);
        var result = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return result is string actor && !string.IsNullOrWhiteSpace(actor) ? actor : null;
    }
}
