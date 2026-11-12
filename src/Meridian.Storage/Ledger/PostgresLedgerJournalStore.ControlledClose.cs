using System.Text.Json;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Ledger;

namespace Meridian.Storage.Ledger;

public sealed partial class PostgresLedgerJournalStore : IControlledCloseAuthorizationStore
{
    public async Task<ControlledCloseAuthorizationDto> SaveCloseAuthorizationAsync(ControlledCloseAuthorizationDto authorization, CancellationToken ct = default)
    {
        RequireWriteTenant();
        if (authorization.Operation is not (EntityLifecycleOperationDto.Settlement or EntityLifecycleOperationDto.ClosingExpense
            or EntityLifecycleOperationDto.Tax or EntityLifecycleOperationDto.Revaluation or EntityLifecycleOperationDto.Correction)
            || string.IsNullOrWhiteSpace(authorization.AuthorizedBy) || string.IsNullOrWhiteSpace(authorization.SourceObligationEvidence)
            || string.IsNullOrWhiteSpace(authorization.Reason) || string.IsNullOrWhiteSpace(authorization.ApprovalId)
            || (authorization.Operation == EntityLifecycleOperationDto.Correction && authorization.SourceJournalEntryId is null)
            || authorization.PostingFingerprint.Length != 64)
            throw new LedgerValidationException("Controlled close requires reviewed journal economics, retained obligation evidence and a supported close operation.");
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct).ConfigureAwait(false);
        await EnsureBookWriteAuthorityAsync(connection, transaction, authorization.LedgerBookId, ct).ConfigureAwait(false);
        var tenant = ResolveCallerTenant();
        if (!string.Equals(tenant?.Trim(), authorization.TenantId?.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Controlled-close authorization belongs to another tenant.");
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"insert into {Qualified("entity_controlled_close_authorization")}(authorization_id,ledger_book_id,journal_entry_id,draft_version,tenant_id,payload) values (@id,@book,@journal,@version,@tenant,@payload::jsonb) on conflict do nothing;";
        command.Parameters.AddWithValue("id", authorization.AuthorizationId);
        command.Parameters.AddWithValue("book", authorization.LedgerBookId);
        command.Parameters.AddWithValue("journal", authorization.JournalEntryId);
        command.Parameters.AddWithValue("version", authorization.DraftVersion);
        command.Parameters.AddWithValue("tenant", (object?)tenant ?? DBNull.Value);
        command.Parameters.AddWithValue("payload", JsonSerializer.Serialize(authorization, JsonOptions));
        if (await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) != 1)
            throw new LedgerValidationException("This controlled-close authorization identity is already retained.");
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return authorization;
    }

    public async Task<ControlledCloseAuthorizationDto?> GetCloseAuthorizationAsync(Guid authorizationId, CancellationToken ct = default)
        => await ReadCloseAuthorizationAsync(authorizationId, null, null, null, ct).ConfigureAwait(false);

    public async Task<ControlledCloseAuthorizationDto?> FindCloseAuthorizationAsync(Guid bookId, Guid journalId, int draftVersion, CancellationToken ct = default)
        => await ReadCloseAuthorizationAsync(null, bookId, journalId, draftVersion, ct).ConfigureAwait(false);

    private async Task<ControlledCloseAuthorizationDto?> ReadCloseAuthorizationAsync(Guid? id, Guid? book, Guid? journal, int? version, CancellationToken ct)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"select ledger_book_id,payload::text from {Qualified("entity_controlled_close_authorization")} where "
            + (id.HasValue ? "authorization_id=@id" : "ledger_book_id=@book and journal_entry_id=@journal and draft_version=@version")
            + " and (lower(trim(tenant_id))=lower(trim(@tenant)) or (tenant_id is null and @tenant='')) order by recorded_at_utc desc,authorization_id desc limit 1";
        command.Parameters.AddWithValue("tenant", ResolveCallerTenant() ?? string.Empty);
        if (id.HasValue) command.Parameters.AddWithValue("id", id.Value);
        else { command.Parameters.AddWithValue("book", book!.Value); command.Parameters.AddWithValue("journal", journal!.Value); command.Parameters.AddWithValue("version", version!.Value); }
        ControlledCloseAuthorizationDto? result;
        Guid foundBook;
        await using (var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            if (!await reader.ReadAsync(ct).ConfigureAwait(false)) return null;
            foundBook = reader.GetGuid(0);
            result = JsonSerializer.Deserialize<ControlledCloseAuthorizationDto>(reader.GetString(1), JsonOptions);
        }
        await EnsureTenantRowAsync(connection, null, "ledger_books", "ledger_book_id", foundBook, false, ct).ConfigureAwait(false);
        return result;
    }
}
