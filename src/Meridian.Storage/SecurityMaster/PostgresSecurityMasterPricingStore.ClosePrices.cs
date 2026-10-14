using System.Text.Json;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.SecurityMaster;
using Npgsql;

namespace Meridian.Storage.SecurityMaster;

public sealed partial class PostgresSecurityMasterPricingStore
{
    public async Task RetainClosePriceSnapshotAsync(SecurityClosePriceSnapshotDto snapshot, CancellationToken ct = default)
    {
        SecurityClosePriceEvidence.Validate(snapshot);
        // Price-selection receipts are already immutable. A close snapshot can retain only those
        // exact scoped inputs, never a caller-supplied price that impersonates a receipt.
        foreach (var line in snapshot.Lines.Where(l => l.Selection is not null))
        {
            var selection = line.Selection!;
            var retained = await GetPriceSelectionAsync(line.SecurityId, snapshot.AccountId, selection.SelectionReceiptId!.Value, ct).ConfigureAwait(false);
            if (retained is null || JsonSerializer.Serialize(retained, SecurityMasterClosePricesJsonContext.Default.SecurityPriceGoldenCopyDto)
                != JsonSerializer.Serialize(selection, SecurityMasterClosePricesJsonContext.Default.SecurityPriceGoldenCopyDto))
                throw new InvalidOperationException("Close prices require the exact retained selection receipt in the same account scope.");
        }
        var payload = JsonSerializer.Serialize(snapshot, SecurityMasterClosePricesJsonContext.Default.SecurityClosePriceSnapshotDto);
        var digest = Sha256Digest.ComputeUtf8(payload);
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        await LockCloseScopeAsync(connection, transaction, snapshot.AccountId, snapshot.BookId, snapshot.ValuationDate, ct).ConfigureAwait(false);
        var existing = await ReadCloseSetAsync(connection, transaction, snapshot.AccountId, snapshot.BookId, snapshot.ValuationDate, snapshot.SnapshotId, ct).ConfigureAwait(false);
        if (existing is not null)
        {
            if (!Sha256Digest.FixedEquals(SecurityClosePriceEvidence.Digest(existing.Snapshot), digest))
                throw new InvalidOperationException("An immutable close-price snapshot cannot be replaced.");
            await transaction.CommitAsync(ct).ConfigureAwait(false);
            return;
        }
        if (snapshot.CorrectsSnapshotId is { } previousId)
        {
            var previous = await ReadCloseSetAsync(connection, transaction, snapshot.AccountId, snapshot.BookId, snapshot.ValuationDate, previousId, ct).ConfigureAwait(false);
            if (previous is null || previous.Snapshot.Version + 1 != snapshot.Version
                || !previous.Snapshot.Lines.Select(l => l.SecurityId).Order().SequenceEqual(snapshot.Lines.Select(l => l.SecurityId).Order()))
                throw new InvalidOperationException("A close-price correction requires a contiguous predecessor with the same scope and security coverage.");
        }
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            insert into {Qualified("security_close_price_snapshots")}
                (snapshot_id, account_id, book_id, valuation_date, version, corrects_snapshot_id, payload, payload_sha256)
            values (@id, @account, @book, @date, @version, @previous, @payload, @digest);
            """;
        AddCloseScope(command, snapshot.AccountId, snapshot.BookId, snapshot.ValuationDate, snapshot.SnapshotId);
        command.Parameters.AddWithValue("version", snapshot.Version);
        command.Parameters.AddWithValue("previous", NpgsqlTypes.NpgsqlDbType.Uuid, (object?)snapshot.CorrectsSnapshotId ?? DBNull.Value);
        command.Parameters.AddWithValue("payload", payload);
        command.Parameters.AddWithValue("digest", digest);
        try
        { await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false); }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        { throw new InvalidOperationException("A close-price version already exists; corrections must extend its latest retained version.", ex); }
        await transaction.CommitAsync(ct).ConfigureAwait(false);
    }

    public async Task RetainClosePriceApprovalAsync(string accountId, string bookId, DateOnly valuationDate,
        SecurityClosePriceApprovalDto approval, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        await LockCloseScopeAsync(connection, transaction, accountId, bookId, valuationDate, ct).ConfigureAwait(false);
        var retained = await ReadCloseSetAsync(connection, transaction, accountId, bookId, valuationDate, approval.SnapshotId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The close-price snapshot does not exist in the requested scope.");
        SecurityClosePriceEvidence.ValidateApproval(retained.Snapshot, approval);
        var payload = JsonSerializer.Serialize(approval, SecurityMasterClosePricesJsonContext.Default.SecurityClosePriceApprovalDto);
        var digest = Sha256Digest.ComputeUtf8(payload);
        if (retained.Approval is { } existing)
        {
            if (JsonSerializer.Serialize(existing, SecurityMasterClosePricesJsonContext.Default.SecurityClosePriceApprovalDto) != payload)
                throw new InvalidOperationException("An immutable close-price approval cannot be replaced.");
            await transaction.CommitAsync(ct).ConfigureAwait(false);
            return;
        }
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"select 1 from {Qualified("security_close_price_snapshots")} where corrects_snapshot_id = @id;";
        command.Parameters.AddWithValue("id", approval.SnapshotId);
        if (await command.ExecuteScalarAsync(ct).ConfigureAwait(false) is not null)
            throw new InvalidOperationException("A superseded draft cannot be approved; review its linked correction.");
        command.CommandText = $"""
            insert into {Qualified("security_close_price_approvals")} (snapshot_id, payload, payload_sha256)
            values (@id, @payload, @digest);
            """;
        command.Parameters.AddWithValue("payload", payload);
        command.Parameters.AddWithValue("digest", digest);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
    }

    public async Task<SecurityClosePriceSetDto?> GetClosePriceSetAsync(
        string accountId, string bookId, DateOnly valuationDate, Guid snapshotId, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        return await ReadCloseSetAsync(connection, null, accountId, bookId, valuationDate, snapshotId, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<SecurityClosePriceSetDto>> GetClosePriceSetsAsync(
        string accountId, string bookId, DateOnly valuationDate, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(accountId) || string.IsNullOrWhiteSpace(bookId) || valuationDate == default)
            throw new ArgumentException("An exact account/book/date scope is required.");
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        // The existing unique (account_id, book_id, valuation_date, version) index covers
        // this exact scope and ordering; approval lookup uses its snapshot primary key.
        command.CommandText = $"""
            select s.payload, s.payload_sha256, s.version, s.corrects_snapshot_id, a.payload, a.payload_sha256, s.snapshot_id
            from {Qualified("security_close_price_snapshots")} s
            left join {Qualified("security_close_price_approvals")} a on a.snapshot_id = s.snapshot_id
            where s.account_id = @account and s.book_id = @book and s.valuation_date = @date
            order by s.version;
            """;
        command.Parameters.AddWithValue("account", accountId);
        command.Parameters.AddWithValue("book", bookId);
        command.Parameters.AddWithValue("date", valuationDate);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var result = new List<SecurityClosePriceSetDto>();
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            result.Add(ReadCloseSet(reader, accountId, bookId, valuationDate, reader.GetGuid(6)));
        return result;
    }

    private async Task<SecurityClosePriceSetDto?> ReadCloseSetAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction,
        string accountId, string bookId, DateOnly valuationDate, Guid snapshotId, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            select s.payload, s.payload_sha256, s.version, s.corrects_snapshot_id, a.payload, a.payload_sha256
            from {Qualified("security_close_price_snapshots")} s
            left join {Qualified("security_close_price_approvals")} a on a.snapshot_id = s.snapshot_id
            where s.snapshot_id = @id and s.account_id = @account and s.book_id = @book and s.valuation_date = @date;
            """;
        AddCloseScope(command, accountId, bookId, valuationDate, snapshotId);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
            return null;
        return ReadCloseSet(reader, accountId, bookId, valuationDate, snapshotId);
    }

    private static SecurityClosePriceSetDto ReadCloseSet(NpgsqlDataReader reader,
        string accountId, string bookId, DateOnly valuationDate, Guid snapshotId)
    {
        var payload = reader.GetString(0);
        if (!Sha256Digest.FixedEquals(Sha256Digest.ComputeUtf8(payload), reader.GetString(1)))
            throw new InvalidOperationException("The retained close-price snapshot failed its integrity check.");
        var snapshot = JsonSerializer.Deserialize(payload, SecurityMasterClosePricesJsonContext.Default.SecurityClosePriceSnapshotDto)
            ?? throw new InvalidOperationException("The retained close-price snapshot is unreadable.");
        if (snapshot.SnapshotId != snapshotId || snapshot.AccountId != accountId || snapshot.BookId != bookId
            || snapshot.ValuationDate != valuationDate || snapshot.Version != reader.GetInt32(2)
            || snapshot.CorrectsSnapshotId != (reader.IsDBNull(3) ? null : reader.GetGuid(3)))
            throw new InvalidOperationException("The retained close-price snapshot does not match its indexed scope and version.");
        SecurityClosePriceEvidence.Validate(snapshot);
        SecurityClosePriceApprovalDto? approval = null;
        if (!reader.IsDBNull(4))
        {
            var approvalPayload = reader.GetString(4);
            if (!Sha256Digest.FixedEquals(Sha256Digest.ComputeUtf8(approvalPayload), reader.GetString(5)))
                throw new InvalidOperationException("The retained close-price approval failed its integrity check.");
            approval = JsonSerializer.Deserialize(approvalPayload, SecurityMasterClosePricesJsonContext.Default.SecurityClosePriceApprovalDto)
                ?? throw new InvalidOperationException("The retained close-price approval is unreadable.");
            SecurityClosePriceEvidence.ValidateApproval(snapshot, approval);
        }
        return new(snapshot, approval);
    }

    private static void AddCloseScope(NpgsqlCommand command, string accountId, string bookId, DateOnly valuationDate, Guid snapshotId)
    {
        command.Parameters.AddWithValue("id", snapshotId);
        command.Parameters.AddWithValue("account", accountId);
        command.Parameters.AddWithValue("book", bookId);
        command.Parameters.AddWithValue("date", valuationDate);
    }

    private static async Task LockCloseScopeAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        string accountId, string bookId, DateOnly valuationDate, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        // Match the database append guard's exact key; PostgreSQL builds its own unambiguous
        // JSON array so account/book delimiters cannot couple unrelated scopes.
        command.CommandText = "select pg_advisory_xact_lock(hashtextextended(json_build_array(@account::text, @book::text, @date::date)::text, 0));";
        command.Parameters.AddWithValue("account", accountId);
        command.Parameters.AddWithValue("book", bookId);
        command.Parameters.AddWithValue("date", valuationDate);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
}
