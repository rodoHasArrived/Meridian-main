using Meridian.Ledger;
using Npgsql;

namespace Meridian.Storage.Ledger;

public sealed partial class PostgresLedgerJournalStore
{
    private async Task ValidateThreeWayFeeCorrectionOwnerAsync(NpgsqlConnection connection,
        NpgsqlTransaction transaction, LedgerJournalEntryWrite write, CancellationToken ct)
    {
        if (write.PostingCommand?.ThreeWayFeeCorrection is not { } correction)
            return;
        var tenant = RequireWriteTenant();
        if (tenant is not null && !string.Equals(tenant, correction.TenantId, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("The fee correction belongs to another tenant.");
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            select 1 from {Qualified("ledger_books")} b
            join {Qualified("fund_profile_tenancy")} t on t.fund_profile_id = lower(trim(b.fund_profile_id))
            where b.ledger_book_id = @book and b.tenant_id = @tenant and t.tenant_id = @tenant
              and t.company_id = @company and b.base_currency = 'USD';
            """;
        command.Parameters.AddWithValue("book", correction.LedgerBookId);
        command.Parameters.AddWithValue("tenant", correction.TenantId);
        command.Parameters.AddWithValue("company", correction.CompanyId);
        if (await command.ExecuteScalarAsync(ct).ConfigureAwait(false) is null)
            throw new LedgerValidationException("The fee correction requires its retained tenant/company-owned USD book.");
        // SourceEventId and idempotency are derived from the full economic identity, not the
        // draft/run identity. Existing aggregate-scoped unique indexes serialize concurrent copies.
    }
}
