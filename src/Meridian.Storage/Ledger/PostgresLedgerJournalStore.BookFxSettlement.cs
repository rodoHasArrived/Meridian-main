using System.Data;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Ledger;
using Meridian.Ledger;
using Npgsql;

namespace Meridian.Storage.Ledger;

public sealed partial class PostgresLedgerJournalStore : ILedgerBookFxSettlementStore
{
    public async Task<BookFxSettlementSetDto?> GetBookFxSettlementSetAsync(Guid setId,
        string tenantId, string companyId, CancellationToken ct = default)
    {
        var scope = ResolveBookFxScope(setId, tenantId, companyId);
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct).ConfigureAwait(false);
        var plan = await ReadBookFxPlanAsync(connection, transaction, scope, ct).ConfigureAwait(false);
        if (plan is null)
            return null;
        await RequireBookFxOwnersAsync(connection, transaction, scope, plan.Input, ct, retainLock: false).ConfigureAwait(false);
        var receipts = await ReadBookFxReceiptsAsync(connection, transaction, scope, plan, ct).ConfigureAwait(false);
        var result = await ReadBookFxStatusAsync(connection, transaction, plan, receipts, ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return result;
    }

    public async Task<BookFxSettlementSetDto> RetainBookFxSettlementSetAsync(RetainBookFxSettlementSetRequestDto request,
        string actor, string controllerRole, string tenantId, string companyId, CancellationToken ct = default)
    {
        var input = BookFxSettlementProjection.Normalize(request);
        var scope = ResolveBookFxScope(input.SetId, tenantId, companyId);
        actor = RequireBookFxController(actor, controllerRole);
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct).ConfigureAwait(false);
        await LockBookFxBooksAsync(connection, transaction, input, ct).ConfigureAwait(false);
        await RequireBookFxOwnersAsync(connection, transaction, scope, input, ct).ConfigureAwait(false);
        var previous = await ReadBookFxPlanAsync(connection, transaction, scope, ct).ConfigureAwait(false);
        if (previous is not null)
        {
            if (previous.Input != input || previous.ApprovedBy != actor || previous.ControllerRole != controllerRole)
                throw new InvalidOperationException("The settlement set id already retains different approved inputs.");
            var receipts = await ReadBookFxReceiptsAsync(connection, transaction, scope, previous, ct).ConfigureAwait(false);
            return await ReadBookFxStatusAsync(connection, transaction, previous, receipts, ct).ConfigureAwait(false);
        }

        var gaap = await LoadLedgerBookAsync(connection, transaction, input.GaapBookId, ct).ConfigureAwait(false);
        var cash = await LoadLedgerBookAsync(connection, transaction, input.CashBookId, ct).ConfigureAwait(false);
        if (gaap is null || cash is null)
            throw new LedgerValidationException("Both settlement books must be retained.");
        foreach (var leg in Enum.GetValues<BookFxSettlementLegDto>())
        {
            await RequireBookFxPeriodAsync(connection, transaction, input, leg, leg == BookFxSettlementLegDto.CashSettlement ? cash : gaap, ct)
                .ConfigureAwait(false);
        }
        var rates = await new ConsolidationFxPostgresStore(_options).GetRateSetAsync(scope.Tenant, scope.Company,
            input.RateSetId, input.RateSetRevision, ct).ConfigureAwait(false)
            ?? throw new LedgerValidationException("The selected approved FX rate-set revision is unavailable.");
        var plan = BookFxSettlementProjection.Build(input, gaap, cash, rates, actor, controllerRole, DateTimeOffset.UtcNow);
        var payload = JsonSerializer.Serialize(plan, BookFxSettlementJsonContext.Default.BookFxSettlementSetPlanDto);
        var hash = Sha256Digest.ComputeUtf8(payload);
        await using var write = connection.CreateCommand();
        write.Transaction = transaction;
        write.CommandText = $"""
            insert into {Qualified("book_fx_settlement_sets")}
                (tenant_id, company_id, set_id, gaap_book_id, cash_book_id, trade_event_id, settlement_event_id, plan_hash_sha256, payload_json)
            values (@tenant, @company, @set, @gaap, @cash, @trade, @settlement, @hash, @payload)
            on conflict do nothing;
            """;
        BindBookFxScope(write, scope);
        write.Parameters.AddWithValue("gaap", input.GaapBookId);
        write.Parameters.AddWithValue("cash", input.CashBookId);
        write.Parameters.AddWithValue("trade", input.TradeEventId);
        write.Parameters.AddWithValue("settlement", input.SettlementEventId);
        write.Parameters.AddWithValue("hash", hash);
        write.Parameters.AddWithValue("payload", payload);
        if (await write.ExecuteNonQueryAsync(ct).ConfigureAwait(false) != 1)
            throw new InvalidOperationException("The settlement event is already bound to a retained set.");
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return new(plan with { PlanHashSha256 = hash }, [], Enum.GetValues<BookFxSettlementLegDto>(), []);
    }

    public async Task<BookFxSettlementLegReceiptDto> CaptureBookFxSettlementLegAsync(Guid setId,
        CaptureBookFxSettlementLegRequestDto request, string actor, string controllerRole, string tenantId,
        string companyId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.CommandId == Guid.Empty || !Enum.IsDefined(request.Leg)
            || !Sha256Digest.IsCanonical(request.ExpectedPlanHashSha256))
            throw new ArgumentException("A retain command requires an operation id, reviewed plan hash and supported leg.", nameof(request));
        var scope = ResolveBookFxScope(setId, tenantId, companyId);
        actor = RequireBookFxController(actor, controllerRole);
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct).ConfigureAwait(false);
        var plan = await ReadBookFxPlanAsync(connection, transaction, scope, ct).ConfigureAwait(false)
            ?? throw new LedgerBookNotFoundException("The reviewed settlement set is unavailable.");
        await LockBookFxBooksAsync(connection, transaction, plan.Input, ct).ConfigureAwait(false);
        await RequireBookFxOwnersAsync(connection, transaction, scope, plan.Input, ct).ConfigureAwait(false);
        if (!Sha256Digest.FixedEquals(plan.PlanHashSha256, request.ExpectedPlanHashSha256))
            throw new InvalidOperationException("The approved settlement plan hash differs from this capture command.");
        var receipts = await ReadBookFxReceiptsAsync(connection, transaction, scope, plan, ct).ConfigureAwait(false);
        if (receipts.SingleOrDefault(receipt => receipt.CommandId == request.CommandId) is { } replay)
        {
            if (replay.Leg != request.Leg || replay.CapturedBy != actor)
                throw new InvalidOperationException("The receipt operation id already retains different authorized inputs.");
            return replay;
        }
        if (receipts.Any(receipt => receipt.Leg == request.Leg))
            throw new InvalidOperationException("This settlement leg already has a retained receipt.");
        if (request.Leg != BookFxSettlementLegDto.GaapTrade)
        {
            var trade = receipts.SingleOrDefault(receipt => receipt.Leg == BookFxSettlementLegDto.GaapTrade);
            if (trade is null || await HasBookFxCorrectionsAsync(connection, transaction, trade, ct).ConfigureAwait(false))
                throw new LedgerValidationException("Capture the current GAAP trade receipt before either settlement.");
        }

        var target = BookFxSettlementProjection.Resolve(plan.Input, request.Leg);
        var book = await LoadLedgerBookAsync(connection, transaction, target.BookId, ct).ConfigureAwait(false);
        if (book is null || !MatchesBookFxRevision(plan, target.Basis, book))
            throw new LedgerValidationException("Accounting book or policy changed after settlement approval.");
        await RequireBookFxPeriodAsync(connection, transaction, plan.Input, request.Leg, book, ct).ConfigureAwait(false);
        var query = new LedgerJournalEntryQuery(
            LedgerBookId: target.BookId, PeriodId: target.Journal.PeriodId, JournalEntryId: target.Journal.JournalEntryId);
        var journal = (await QueryAsync(connection, transaction, query, null, ct).ConfigureAwait(false)).SingleOrDefault()
            ?? throw new LedgerValidationException("The reviewed settlement journal is still missing from the selected accounting book.");
        BookFxSettlementProjection.ValidateJournal(plan, request.Leg, journal);
        Guid? lotBatch = null;
        if (request.Leg != BookFxSettlementLegDto.GaapSettlement)
        {
            var history = await GetTaxLotDisposalHistoryAsync(connection, transaction, target.BookId,
                [target.Journal.JournalEntryId], ct).ConfigureAwait(false);
            lotBatch = BookFxSettlementProjection.ValidateLotRelief(plan, request.Leg, history);
        }

        var receipt = new BookFxSettlementLegReceiptDto(request.CommandId, request.Leg, target.BookId,
            target.Journal.PeriodId, target.Journal.JournalEntryId, journal.GlobalSequence, lotBatch,
            plan.PlanHashSha256, DateTimeOffset.UtcNow, actor);
        if (await HasBookFxCorrectionsAsync(connection, transaction, receipt, ct).ConfigureAwait(false))
            throw new LedgerValidationException("The reviewed journal already has a later correction.");
        var payload = JsonSerializer.Serialize(receipt, BookFxSettlementJsonContext.Default.BookFxSettlementLegReceiptDto);
        await using var write = connection.CreateCommand();
        write.Transaction = transaction;
        write.CommandText = $"""
            insert into {Qualified("book_fx_settlement_receipts")}
                (tenant_id, company_id, set_id, leg, command_id, journal_entry_id, content_hash_sha256, payload_json)
            values (@tenant, @company, @set, @leg, @command, @journal, @hash, @payload)
            on conflict do nothing;
            """;
        BindBookFxScope(write, scope);
        write.Parameters.AddWithValue("leg", receipt.Leg.ToString());
        write.Parameters.AddWithValue("command", receipt.CommandId);
        write.Parameters.AddWithValue("journal", receipt.JournalEntryId);
        write.Parameters.AddWithValue("hash", Sha256Digest.ComputeUtf8(payload));
        write.Parameters.AddWithValue("payload", payload);
        if (await write.ExecuteNonQueryAsync(ct).ConfigureAwait(false) != 1)
            throw new InvalidOperationException("This journal or operation is already bound to a retained settlement receipt.");
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return receipt;
    }

    private async Task<BookFxSettlementSetDto> ReadBookFxStatusAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        BookFxSettlementSetPlanDto plan, IReadOnlyList<BookFxSettlementLegReceiptDto> receipts, CancellationToken ct)
    {
        var gaap = await LoadLedgerBookAsync(connection, transaction, plan.Input.GaapBookId, ct).ConfigureAwait(false);
        var cash = await LoadLedgerBookAsync(connection, transaction, plan.Input.CashBookId, ct).ConfigureAwait(false);
        var invalidated = new List<BookFxSettlementLegDto>();
        foreach (var receipt in receipts)
        {
            var basis = BookFxSettlementProjection.Resolve(plan.Input, receipt.Leg).Basis;
            var book = basis == AccountingBasisKindDto.Gaap ? gaap : cash;
            if (book is null || !MatchesBookFxRevision(plan, basis, book)
                || await HasBookFxCorrectionsAsync(connection, transaction, receipt, ct).ConfigureAwait(false))
                invalidated.Add(receipt.Leg);
        }
        return new(plan, receipts, Enum.GetValues<BookFxSettlementLegDto>().Except(receipts.Select(receipt => receipt.Leg)).ToArray(),
            invalidated);
    }

    private static bool MatchesBookFxRevision(BookFxSettlementSetPlanDto plan, AccountingBasisKindDto basis, LedgerBookRecord book)
        => book.AccountingBasis == basis && book.FundStructureNodeId == plan.EntityId && book.FundProfileId == plan.FundProfileId
            && string.Equals(book.BaseCurrency, plan.FunctionalCurrency, StringComparison.OrdinalIgnoreCase)
            && book.UpdatedAt == (basis == AccountingBasisKindDto.Gaap ? plan.GaapBookRevision : plan.CashBookRevision);

    private async Task RequireBookFxPeriodAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        RetainBookFxSettlementSetRequestDto input, BookFxSettlementLegDto leg, LedgerBookRecord book, CancellationToken ct)
    {
        var target = BookFxSettlementProjection.Resolve(input, leg);
        var period = await LoadPeriodAsync(connection, transaction, target.Journal.PeriodId, forUpdate: false,
            callerTenantId: ResolveCallerTenant(), ct: ct).ConfigureAwait(false);
        if (period is null || period.LedgerBookId != book.LedgerBookId || period.Version != target.Journal.PeriodVersion
            || target.Date < period.StartDate || target.Date > period.EndDate || book.AccountingBasis != target.Basis
            || book.AccountingPolicyId != target.Journal.AccountingPolicyId || book.AccountingPolicyVersion != target.Journal.AccountingPolicyVersion)
            throw new LedgerValidationException("The accounting period, date or policy no longer matches the reviewed settlement leg.");
    }

    private async Task<bool> HasBookFxCorrectionsAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        BookFxSettlementLegReceiptDto receipt, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            select exists (
                select 1 from {Qualified("journal_entries")} j
                join {Qualified("accounting_periods")} p on p.period_id = j.period_id
                where p.ledger_book_id = @book and j.source_journal_entry_id = @journal
            );
            """;
        command.Parameters.AddWithValue("book", receipt.LedgerBookId);
        command.Parameters.AddWithValue("journal", receipt.JournalEntryId);
        return await command.ExecuteScalarAsync(ct).ConfigureAwait(false) is true;
    }

    private async Task<BookFxSettlementSetPlanDto?> ReadBookFxPlanAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        BookFxScope scope, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            select gaap_book_id, cash_book_id, trade_event_id, settlement_event_id, plan_hash_sha256, payload_json
            from {Qualified("book_fx_settlement_sets")} where tenant_id = @tenant and company_id = @company and set_id = @set;
            """;
        BindBookFxScope(command, scope);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
            return null;
        var payload = reader.GetString(5);
        var hash = reader.GetString(4);
        if (!Sha256Digest.FixedEquals(hash, Sha256Digest.ComputeUtf8(payload)))
            throw new InvalidDataException("The retained settlement set failed its content integrity check.");
        var plan = JsonSerializer.Deserialize(payload, BookFxSettlementJsonContext.Default.BookFxSettlementSetPlanDto);
        if (plan?.Input is null || plan.Input.SetId != scope.Id || plan.Input.GaapBookId != reader.GetGuid(0)
            || plan.Input.CashBookId != reader.GetGuid(1) || plan.Input.TradeEventId != reader.GetGuid(2)
            || plan.Input.SettlementEventId != reader.GetGuid(3) || plan.PlanHashSha256 != string.Empty)
            throw new InvalidDataException("The retained settlement set scope is inconsistent.");
        return plan with { PlanHashSha256 = hash };
    }

    private async Task<IReadOnlyList<BookFxSettlementLegReceiptDto>> ReadBookFxReceiptsAsync(NpgsqlConnection connection,
        NpgsqlTransaction transaction, BookFxScope scope, BookFxSettlementSetPlanDto plan, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            select leg, command_id, journal_entry_id, content_hash_sha256, payload_json
            from {Qualified("book_fx_settlement_receipts")} where tenant_id = @tenant and company_id = @company and set_id = @set;
            """;
        BindBookFxScope(command, scope);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var receipts = new List<BookFxSettlementLegReceiptDto>();
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var payload = reader.GetString(4);
            if (!Sha256Digest.FixedEquals(reader.GetString(3), Sha256Digest.ComputeUtf8(payload)))
                throw new InvalidDataException("A retained settlement receipt failed its content integrity check.");
            var receipt = JsonSerializer.Deserialize(payload, BookFxSettlementJsonContext.Default.BookFxSettlementLegReceiptDto);
            if (receipt is null || !Enum.IsDefined(receipt.Leg) || receipt.Leg.ToString() != reader.GetString(0)
                || receipt.CommandId != reader.GetGuid(1) || receipt.JournalEntryId != reader.GetGuid(2)
                || !Sha256Digest.FixedEquals(receipt.PlanHashSha256, plan.PlanHashSha256))
                throw new InvalidDataException("A retained settlement receipt is outside its approved scope.");
            var target = BookFxSettlementProjection.Resolve(plan.Input, receipt.Leg);
            if (receipt.LedgerBookId != target.BookId || receipt.PeriodId != target.Journal.PeriodId
                || receipt.JournalEntryId != target.Journal.JournalEntryId || receipt.JournalSequence <= 0
                || (receipt.LotMutationBatchId is null) != (receipt.Leg == BookFxSettlementLegDto.GaapSettlement))
                throw new InvalidDataException("A retained settlement receipt differs from its approved journal binding.");
            receipts.Add(receipt);
        }
        return receipts.OrderBy(receipt => receipt.Leg).ToArray();
    }

    private async Task RequireBookFxOwnersAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        BookFxScope scope, RetainBookFxSettlementSetRequestDto input, CancellationToken ct, bool retainLock = true)
    {
        foreach (var id in new[] { input.GaapBookId, input.CashBookId }.Order())
            await RequireFiscalCloseOwnerAsync(connection, transaction, id, scope.Tenant, scope.Company, ct, retainLock).ConfigureAwait(false);
    }

    private static async Task LockBookFxBooksAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        RetainBookFxSettlementSetRequestDto input, CancellationToken ct)
    {
        foreach (var id in new[] { input.GaapBookId, input.CashBookId }.Order())
            await CashDividendEntitlementLock.AcquireAsync(connection, transaction, id, true, ct).ConfigureAwait(false);
    }

    private BookFxScope ResolveBookFxScope(Guid setId, string tenantId, string companyId)
    {
        if (setId == Guid.Empty)
            throw new ArgumentException("A retained settlement set id is required.");
        if (string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(companyId)
            || tenantId.Trim().Equals("all", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(tenantId.Trim(), RequireWriteTenant(), StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("A tenant and company are required for settlement evidence.");
        return new(tenantId.Trim(), companyId.Trim(), setId);
    }

    private static string RequireBookFxController(string actor, string role)
    {
        if (actor?.Trim() is not { Length: > 0 and <= 200 }
            || !(string.Equals(role, "Controller", StringComparison.OrdinalIgnoreCase)
              || string.Equals(role, "Fund Controller", StringComparison.OrdinalIgnoreCase)))
            throw new UnauthorizedAccessException("A scoped controller must authorize a settlement set.");
        return actor.Trim();
    }

    private static void BindBookFxScope(NpgsqlCommand command, BookFxScope scope)
    {
        command.Parameters.AddWithValue("tenant", scope.Tenant);
        command.Parameters.AddWithValue("company", scope.Company);
        command.Parameters.AddWithValue("set", scope.Id);
    }

    private sealed record BookFxScope(string Tenant, string Company, Guid Id);
}

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(BookFxSettlementSetPlanDto))]
[JsonSerializable(typeof(BookFxSettlementLegReceiptDto))]
internal sealed partial class BookFxSettlementJsonContext : JsonSerializerContext;
