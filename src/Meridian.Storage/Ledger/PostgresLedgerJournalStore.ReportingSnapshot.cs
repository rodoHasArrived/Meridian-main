using System.Data;
using Meridian.Ledger;
using Npgsql;

namespace Meridian.Storage.Ledger;

public sealed partial class PostgresLedgerJournalStore : ILedgerReportingSnapshotSource
{
    /// <inheritdoc />
    public async Task<LedgerReportingSnapshot> CaptureReportingSnapshotAsync(
        LedgerJournalEntryQuery query,
        Guid? accountingPeriodId = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.LedgerBookId is not { } ledgerBookId || ledgerBookId == Guid.Empty)
        {
            throw new ArgumentException("A ledger book is required for a reporting snapshot.", nameof(query));
        }

        var reportPeriodId = accountingPeriodId ?? query.PeriodId;
        if (reportPeriodId == Guid.Empty)
        {
            throw new ArgumentException("A valid accounting period is required when supplied.", nameof(accountingPeriodId));
        }

        RequireWriteTenant();
        var lineDimensionsJson = ValidateJournalQuery(query);
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct)
            .ConfigureAwait(false);
        await using (var readOnly = connection.CreateCommand())
        {
            readOnly.Transaction = transaction;
            readOnly.CommandText = "set transaction read only;";
            await readOnly.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await EnsureReportingBookReadAuthorityAsync(connection, transaction, ledgerBookId, ct).ConfigureAwait(false);
        LedgerAccountingPeriod? period = null;
        if (reportPeriodId is { } periodId)
        {
            period = await LoadPeriodAsync(connection, transaction, periodId, forUpdate: false,
                callerTenantId: ResolveCallerTenant(), ct: ct).ConfigureAwait(false);
            if (period is null || period.LedgerBookId != ledgerBookId)
            {
                throw new LedgerValidationException("The requested reporting period is unavailable in the ledger book.");
            }
        }

        var journals = await QueryAsync(connection, transaction, query, lineDimensionsJson, ct).ConfigureAwait(false);
        var history = await GetTaxLotDisposalHistoryAsync(connection, transaction, ledgerBookId,
                journals.Select(static record => record.Entry.JournalEntryId).Distinct().ToArray(), ct)
            .ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return new LedgerReportingSnapshot(journals, history, period);
    }

    private async Task EnsureReportingBookReadAuthorityAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid ledgerBookId,
        CancellationToken ct)
    {
        if (!_tenantScope.IsFailClosed)
        {
            return;
        }

        var caller = RequireWriteTenant();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        // Repeatable read preserves the retained ownership facts for the entire capture. A row
        // lock is unnecessary here and PostgreSQL forbids it in a read-only transaction.
        command.CommandText = $"""
            select b.tenant_id, t.tenant_id from {Qualified("ledger_books")} b
            join {Qualified("fund_profile_tenancy")} t on t.fund_profile_id = lower(trim(b.fund_profile_id))
            where b.ledger_book_id = @id;
            """;
        command.Parameters.AddWithValue("id", ledgerBookId);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false) || reader.IsDBNull(0) || reader.IsDBNull(1) ||
            !string.Equals(reader.GetString(0).Trim(), caller, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(reader.GetString(1).Trim(), caller, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("The ledger book and retained fund authority must belong to the caller's tenant.");
        }
    }
}
