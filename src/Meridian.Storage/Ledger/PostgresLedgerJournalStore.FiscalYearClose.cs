using System.Data;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Ledger;
using Meridian.Ledger;
using Npgsql;
using NpgsqlTypes;

namespace Meridian.Storage.Ledger;

public sealed partial class PostgresLedgerJournalStore : IFiscalYearCloseReceiptStore
{
    public async Task<FiscalYearClosePlanDto> GetFiscalYearClosePlanAsync(
        Guid ledgerBookId, int fiscalYear, CancellationToken ct = default)
    {
        ValidateFiscalCloseScope(ledgerBookId, fiscalYear);
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct).ConfigureAwait(false);
        await EnsureReportingBookReadAuthorityAsync(connection, transaction, ledgerBookId, ct).ConfigureAwait(false);
        var book = await LoadLedgerBookAsync(connection, transaction, ledgerBookId, ct).ConfigureAwait(false)
            ?? throw new LedgerBookNotFoundException("The requested fiscal close book is unavailable.");
        var (plan, _) = await BuildFiscalCloseAsync(connection, transaction, book, fiscalYear, ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return plan;
    }

    public async Task<FiscalYearCloseReceiptDto?> GetFiscalYearCloseReceiptAsync(
        Guid ledgerBookId, int fiscalYear, Guid? receiptId = null, CancellationToken ct = default)
    {
        ValidateFiscalCloseScope(ledgerBookId, fiscalYear);
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        var retained = await ReadFiscalCloseReceiptAsync(connection, null, ledgerBookId, fiscalYear, receiptId, ct)
            .ConfigureAwait(false);
        return retained?.Receipt;
    }

    public async Task<FiscalYearCloseReceiptDto> FinalizeFiscalYearCloseAsync(
        Guid ledgerBookId, int fiscalYear, FinalizeFiscalYearCloseRequestDto request,
        string actor, string controllerRole, string tenantId, string companyId, CancellationToken ct = default)
    {
        ValidateFiscalCloseScope(ledgerBookId, fiscalYear);
        ArgumentNullException.ThrowIfNull(request);
        var expectedHash = request.ExpectedPlanHashSha256?.Trim().ToLowerInvariant();
        var reason = request.Reason?.Trim();
        if (request.CommandId == Guid.Empty || request.SupersedesReceiptId == Guid.Empty
            || expectedHash is not { Length: 64 } || !expectedHash.All(char.IsAsciiHexDigit)
            || reason is not { Length: > 0 and <= 2000 })
            throw new ArgumentException("Fiscal close requires an operation id, reviewed plan hash, and reason of at most 2,000 characters.", nameof(request));

        if (string.IsNullOrWhiteSpace(actor) || string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(companyId)
            || !string.Equals(tenantId.Trim(), RequireWriteTenant(), StringComparison.OrdinalIgnoreCase)
            || !(string.Equals(controllerRole, "Controller", StringComparison.OrdinalIgnoreCase)
                || string.Equals(controllerRole, "Fund Controller", StringComparison.OrdinalIgnoreCase)))
            throw new UnauthorizedAccessException("A tenant-scoped controller must authorize a fiscal close.");

        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        // Take the existing book fence exclusively. Period transitions and book/journal writes
        // take the same fence shared before their period or audit locks. ReadCommitted sees a
        // completed competing close or correction after the lock is granted.
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct).ConfigureAwait(false);
        await CashDividendEntitlementLock.AcquireAsync(connection, transaction, ledgerBookId, true, ct).ConfigureAwait(false);
        await RequireFiscalCloseOwnerAsync(connection, transaction, ledgerBookId, tenantId, companyId, ct).ConfigureAwait(false);

        var replay = await ReadFiscalCloseReceiptAsync(connection, transaction, ledgerBookId,
            fiscalYear, request.CommandId, ct).ConfigureAwait(false);
        if (replay is { Receipt: var retained })
        {
            if (retained.Plan.PlanHashSha256 != expectedHash || retained.SupersedesReceiptId != request.SupersedesReceiptId
                || retained.AuthorizedBy != actor.Trim() || retained.Reason != reason)
                throw new InvalidOperationException("This fiscal close operation id already retains different authorized inputs.");
            return retained;
        }

        var head = await ReadFiscalCloseReceiptAsync(connection, transaction, ledgerBookId, fiscalYear, null, ct)
            .ConfigureAwait(false);
        if (head?.Receipt.ReceiptId != request.SupersedesReceiptId)
            throw new InvalidOperationException("The latest fiscal close receipt changed. Reload it before authorizing a successor.");
        if (head?.Receipt.Plan.PlanHashSha256 == expectedHash)
            throw new InvalidOperationException("The reviewed fiscal close plan already has a receipt. Reload that receipt.");

        var book = await LoadLedgerBookAsync(connection, transaction, ledgerBookId, ct).ConfigureAwait(false)
            ?? throw new LedgerBookNotFoundException("The requested fiscal close book is unavailable.");
        var (plan, transferIds) = await BuildFiscalCloseAsync(connection, transaction, book, fiscalYear, ct).ConfigureAwait(false);
        if (!plan.IsReady || plan.PlanHashSha256 != expectedHash)
            throw new InvalidOperationException("The retained fiscal close inputs changed or have blockers. Review the current plan before authorizing it.");
        if (plan.ProposedClosingTransfers.Count > 0)
            throw new InvalidOperationException("The plan still needs closing transfers. Post them through a governed period re-close, then review the revised plan.");

        // Reusing an earlier, already-replaced population is an approval conflict, even when
        // the caller supplies the current head as the predecessor.
        var revision = checked((head?.Revision ?? 0) + 1);
        var receipt = new FiscalYearCloseReceiptDto(request.CommandId, request.SupersedesReceiptId, DateTimeOffset.UtcNow,
            actor.Trim(), controllerRole, reason, plan, book.BaseCurrency, book.AccountingBasis, book.AccountingPolicyId,
            book.AccountingPolicyVersion, transferIds);
        var json = JsonSerializer.Serialize(receipt, FiscalYearCloseReceiptJsonContext.Default.FiscalYearCloseReceiptDto);
        await using var write = connection.CreateCommand();
        write.Transaction = transaction;
        write.CommandText = $"""
            insert into {Qualified("fiscal_year_close_receipts")}
                (ledger_book_id, fiscal_year, receipt_id, supersedes_receipt_id, revision, tenant_id,
                 company_id, plan_hash_sha256, payload_json, content_hash_sha256)
            values (@book, @year, @receipt, @predecessor, @revision, @tenant, @company, @plan_hash, @payload, @digest)
            on conflict (ledger_book_id, fiscal_year, plan_hash_sha256) do nothing;
            """;
        write.Parameters.AddWithValue("book", ledgerBookId);
        write.Parameters.AddWithValue("year", fiscalYear);
        write.Parameters.AddWithValue("receipt", receipt.ReceiptId);
        write.Parameters.AddWithValue("predecessor", NpgsqlDbType.Uuid, (object?)receipt.SupersedesReceiptId ?? DBNull.Value);
        write.Parameters.AddWithValue("revision", revision);
        write.Parameters.AddWithValue("tenant", tenantId.Trim());
        write.Parameters.AddWithValue("company", companyId.Trim());
        write.Parameters.AddWithValue("plan_hash", expectedHash);
        write.Parameters.AddWithValue("payload", json);
        write.Parameters.AddWithValue("digest", Sha256Digest.ComputeUtf8(json));
        if (await write.ExecuteNonQueryAsync(ct).ConfigureAwait(false) != 1)
            throw new InvalidOperationException("The reviewed fiscal close plan already has a receipt.");
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return receipt;
    }

    private async Task<(FiscalYearClosePlanDto Plan, IReadOnlyList<Guid> TransferIds)> BuildFiscalCloseAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, LedgerBookRecord book, int fiscalYear, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            select p.period_id, p.ledger_book_id, p.fiscal_year, p.period_no, p.label, p.start_date,
                   p.end_date, p.status, p.opened_at, p.closed_at, p.optimistic_version
            from {Qualified("accounting_periods")} p where p.ledger_book_id = @book
            """;
        command.Parameters.AddWithValue("book", book.LedgerBookId);
        ApplyTenantReadFilter(command, "p.tenant_id", ResolveCallerTenant());
        var periods = new List<LedgerAccountingPeriod>();
        await using (var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
                periods.Add(ReadPeriod(reader));
        }

        var reads = new Dictionary<Guid, FiscalYearCloseRead>();
        foreach (var period in periods.Where(period => period.FiscalYear == fiscalYear && period.Status == "HardClosed"))
        {
            try
            {
                var snapshot = await LoadCloseSnapshotAsync(connection, transaction, period.PeriodId, period.Version, ct)
                    .ConfigureAwait(false);
                reads[period.PeriodId] = new(snapshot);
            }
            catch (Exception ex) when (ex is InvalidDataException or JsonException)
            {
                reads[period.PeriodId] = new(null, IntegrityFailed: true);
            }
        }
        var evidence = new FiscalYearCloseBookEvidenceDto(book.BaseCurrency, book.AccountingBasis, book.AccountingPolicyId,
            book.AccountingPolicyVersion, book.FundStructureNodeId, book.UpdatedAt);
        var plan = FiscalYearCloseProjection.Build(book.LedgerBookId, fiscalYear, periods, reads, evidence);
        if (!plan.IsReady)
            return (plan, []);
        var final = plan.Periods.MaxBy(period => period.EndDate)!;
        var ids = plan.Periods.Select(period => period.PeriodId).ToHashSet();
        var entries = reads[final.PeriodId].Snapshot!.VerifyIntegrity().Entries;
        return (plan, FiscalYearCloseProjection.GetClosingTransfers(entries.Where(entry => ids.Contains(entry.PeriodId)))
            .Select(entry => entry.Entry.JournalEntryId).ToArray());
    }

    private async Task RequireFiscalCloseOwnerAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        Guid bookId, string tenantId, string companyId, CancellationToken ct, bool retainLock = true)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            select b.ledger_book_id from {Qualified("ledger_books")} b
            join {Qualified("fund_profile_tenancy")} o on o.fund_profile_id = lower(trim(b.fund_profile_id))
            where b.ledger_book_id = @book and lower(trim(b.tenant_id)) = lower(@tenant)
              and lower(trim(o.tenant_id)) = lower(@tenant) and lower(trim(o.company_id)) = lower(@company)
            """ + (retainLock ? " for share of b, o;" : ";");
        command.Parameters.AddWithValue("book", bookId);
        command.Parameters.AddWithValue("tenant", tenantId.Trim());
        command.Parameters.AddWithValue("company", companyId.Trim());
        if (await command.ExecuteScalarAsync(ct).ConfigureAwait(false) is not Guid)
            throw new UnauthorizedAccessException("The ledger book must belong to the authorized tenant and company.");
    }

    private async Task<RetainedFiscalClose?> ReadFiscalCloseReceiptAsync(NpgsqlConnection connection,
        NpgsqlTransaction? transaction, Guid bookId, int year, Guid? receiptId, CancellationToken ct)
    {
        var tenant = RequireWriteTenant();
        if (string.IsNullOrWhiteSpace(tenant) || tenant.Equals("all", StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("A tenant-scoped caller is required to read a fiscal close receipt.");
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            select r.receipt_id, r.supersedes_receipt_id, r.revision, r.plan_hash_sha256, r.payload_json, r.content_hash_sha256
            from {Qualified("fiscal_year_close_receipts")} r
            join {Qualified("ledger_books")} b on b.ledger_book_id = r.ledger_book_id
            join {Qualified("fund_profile_tenancy")} o on o.fund_profile_id = lower(trim(b.fund_profile_id))
            where r.ledger_book_id = @book and r.fiscal_year = @year
              and lower(trim(r.tenant_id)) = lower(@tenant)
              and lower(trim(b.tenant_id)) = lower(@tenant)
              and lower(trim(o.tenant_id)) = lower(@tenant)
              and lower(trim(o.company_id)) = lower(trim(r.company_id))
            """;
        command.Parameters.AddWithValue("book", bookId);
        command.Parameters.AddWithValue("year", year);
        command.Parameters.AddWithValue("tenant", tenant);
        if (receiptId.HasValue)
        {
            command.CommandText += " and r.receipt_id = @receipt";
            command.Parameters.AddWithValue("receipt", receiptId.Value);
        }
        command.CommandText += " order by r.revision desc limit 1;";
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
            return null;

        var json = reader.GetString(4);
        if (Sha256Digest.ComputeUtf8(json) != reader.GetString(5))
            throw new InvalidDataException("Fiscal close receipt content failed its integrity check.");
        var receipt = JsonSerializer.Deserialize(json, FiscalYearCloseReceiptJsonContext.Default.FiscalYearCloseReceiptDto);
        if (receipt is null || receipt.ReceiptId != reader.GetGuid(0)
            || receipt.SupersedesReceiptId != (reader.IsDBNull(1) ? (Guid?)null : reader.GetGuid(1))
            || receipt.Plan.LedgerBookId != bookId || receipt.Plan.FiscalYear != year
            || !receipt.Plan.IsReady || receipt.Plan.PlanHashSha256 != reader.GetString(3))
            throw new InvalidDataException("Fiscal close receipt scope differs from its retained payload.");
        return new(receipt, reader.GetInt32(2));
    }

    private static void ValidateFiscalCloseScope(Guid bookId, int fiscalYear)
    {
        if (bookId == Guid.Empty || fiscalYear is < 1900 or > 9999)
            throw new ArgumentException("A ledger book and fiscal year between 1900 and 9999 are required.");
    }

    private sealed record RetainedFiscalClose(FiscalYearCloseReceiptDto Receipt, int Revision);
}

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(FiscalYearCloseReceiptDto))]
internal sealed partial class FiscalYearCloseReceiptJsonContext : JsonSerializerContext;
