using Npgsql;

namespace Meridian.Storage.Ledger;

public sealed partial class PostgresLedgerJournalStore
{
    private string? RequireWriteTenant()
    {
        var tenant = ResolveCallerTenant()?.Trim();
        if (_tenantScope.IsFailClosed && (string.IsNullOrWhiteSpace(tenant) ||
            tenant.Equals("all", StringComparison.OrdinalIgnoreCase)))
            throw new UnauthorizedAccessException("A tenant-scoped caller is required for ledger mutations.");
        return tenant;
    }

    private async Task EnsureTenantRowAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction,
        string table, string idColumn, Guid id, bool allowMissing, CancellationToken ct)
    {
        if (!_tenantScope.IsFailClosed)
            return;
        var caller = RequireWriteTenant();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"select tenant_id from {Qualified(table)} where {idColumn} = @id for share";
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            if (allowMissing)
                return;
            throw new UnauthorizedAccessException("The retained ledger scope is unavailable to this tenant.");
        }
        if (reader.IsDBNull(0) || !string.Equals(reader.GetString(0).Trim(), caller, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("The retained ledger scope does not belong to this tenant.");
    }

    private async Task EnsureBookWriteAuthorityAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction,
        Guid? bookId, CancellationToken ct)
    {
        if (!_tenantScope.IsFailClosed)
            return;
        var caller = RequireWriteTenant();
        if (bookId is null)
            throw new UnauthorizedAccessException("A retained tenant-owned ledger book is required.");
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            select b.tenant_id, t.tenant_id from {Qualified("ledger_books")} b
            join {Qualified("fund_profile_tenancy")} t on t.fund_profile_id = lower(trim(b.fund_profile_id))
            where b.ledger_book_id = @id for share of b, t
            """;
        command.Parameters.AddWithValue("id", bookId.Value);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false) || reader.IsDBNull(0) || reader.IsDBNull(1) ||
            !string.Equals(reader.GetString(0).Trim(), caller, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(reader.GetString(1).Trim(), caller, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("The ledger book and retained fund authority must belong to the caller's tenant.");
    }

    private async Task EnsureFundWriteAuthorityAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        string fundProfileId, CancellationToken ct)
    {
        if (!_tenantScope.IsFailClosed)
            return;
        var caller = RequireWriteTenant();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"select tenant_id from {Qualified("fund_profile_tenancy")} where fund_profile_id = lower(trim(@fund)) for share";
        command.Parameters.AddWithValue("fund", fundProfileId);
        var owner = await command.ExecuteScalarAsync(ct).ConfigureAwait(false) as string;
        if (string.IsNullOrWhiteSpace(owner) || !string.Equals(owner.Trim(), caller, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("The requested fund does not belong to the caller's tenant.");
    }

    private async Task EnsureBookReferenceAuthorityAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        string table, string idColumn, Guid id, Guid? expectedBookId, bool allowMissing, CancellationToken ct)
    {
        if (!_tenantScope.IsFailClosed)
            return;
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"select ledger_book_id from {Qualified(table)} where {idColumn} = @id for share";
        command.Parameters.AddWithValue("id", id);
        if (await command.ExecuteScalarAsync(ct).ConfigureAwait(false) is not Guid bookId)
        {
            if (allowMissing)
                return;
            throw new UnauthorizedAccessException("The retained ledger reference is unavailable to this tenant.");
        }
        if (expectedBookId.HasValue && expectedBookId != bookId)
            throw new UnauthorizedAccessException("The retained ledger reference must belong to the requested book.");
        await EnsureBookWriteAuthorityAsync(connection, transaction, bookId, ct).ConfigureAwait(false);
    }
}
