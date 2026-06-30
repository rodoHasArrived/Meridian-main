using System.Data;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Ledger;
using Npgsql;

namespace Meridian.Storage.Ledger;

public sealed class PostgresLedgerJournalStore : ITransactionalLedgerJournalStore
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private readonly LedgerJournalStoreOptions _options;

    public PostgresLedgerJournalStore(LedgerJournalStoreOptions options)
    {
        _options = options;
    }

    public async Task AppendAsync(LedgerJournalEntryWrite entry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(entry.Entry);
        entry = AccountingPostingCommandValidator.NormalizeAndValidate(entry);

        if (entry.AggregateId == Guid.Empty)
        {
            throw new ArgumentException("Aggregate id is required.", nameof(entry));
        }

        if (entry.PeriodId == Guid.Empty)
        {
            throw new ArgumentException("Period id is required.", nameof(entry));
        }

        if (!entry.Entry.IsBalanced)
        {
            throw new LedgerValidationException($"Journal entry '{entry.Entry.JournalEntryId}' is not balanced.");
        }

        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable, ct).ConfigureAwait(false);

        await AppendAsync(connection, transaction, entry, ct).ConfigureAwait(false);

        await transaction.CommitAsync(ct).ConfigureAwait(false);
    }

    public async Task AppendAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        LedgerJournalEntryWrite entry,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(entry.Entry);
        entry = AccountingPostingCommandValidator.NormalizeAndValidate(entry);

        if (entry.AggregateId == Guid.Empty)
        {
            throw new ArgumentException("Aggregate id is required.", nameof(entry));
        }

        if (entry.PeriodId == Guid.Empty)
        {
            throw new ArgumentException("Period id is required.", nameof(entry));
        }

        if (!entry.Entry.IsBalanced)
        {
            throw new LedgerValidationException($"Journal entry '{entry.Entry.JournalEntryId}' is not balanced.");
        }

        var period = await LoadPeriodAsync(
                connection,
                transaction,
                entry.PeriodId,
                forUpdate: _options.EnablePeriodLocking,
                ct)
            .ConfigureAwait(false);
        if (period is null)
        {
            throw new LedgerValidationException($"Ledger period '{entry.PeriodId}' was not found.");
        }

        LedgerPeriodPostingGuard.Validate(entry, period);
        await ValidateJournalBasisAsync(connection, transaction, entry, period, ct).ConfigureAwait(false);
        await InsertJournalEntryAsync(connection, transaction, entry, ct).ConfigureAwait(false);
        await InsertJournalLegsAsync(connection, transaction, entry, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<LedgerJournalEntryRecord>> QueryAsync(
        LedgerJournalEntryQuery query,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var lineDimensionsJson = BuildLineDimensionContainmentJson(query.LineDimensions);
        if (!query.LedgerBookId.HasValue
            && !query.PeriodId.HasValue
            && !query.AggregateId.HasValue
            && string.IsNullOrWhiteSpace(query.AccountName)
            && !query.OccurredFrom.HasValue
            && !query.OccurredTo.HasValue
            && lineDimensionsJson is null)
        {
            throw new ArgumentException("At least one journal query filter is required.", nameof(query));
        }

        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = CreateJournalEntryReadCommand(connection);
        command.CommandText += BuildJournalEntryQueryFilterSql(
            Qualified("journal_entries"),
            Qualified("journal_legs"),
            Qualified("accounting_periods"));

        if (query.LedgerBookId.HasValue)
        {
            command.CommandText += " and p_filter.ledger_book_id = @ledger_book_id";
            command.Parameters.AddWithValue("ledger_book_id", query.LedgerBookId.Value);
        }

        if (query.PeriodId.HasValue)
        {
            command.CommandText += " and je_filter.period_id = @period_id";
            command.Parameters.AddWithValue("period_id", query.PeriodId.Value);
        }

        if (query.AggregateId.HasValue)
        {
            command.CommandText += " and je_filter.aggregate_id = @aggregate_id";
            command.Parameters.AddWithValue("aggregate_id", query.AggregateId.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.AccountName))
        {
            command.CommandText += " and jl_filter.account_name = @account_name";
            command.Parameters.AddWithValue("account_name", query.AccountName.Trim());
        }

        if (query.OccurredFrom.HasValue)
        {
            command.CommandText += " and je_filter.occurred_at >= @occurred_from";
            command.Parameters.AddWithValue("occurred_from", query.OccurredFrom.Value.UtcDateTime);
        }

        if (query.OccurredTo.HasValue)
        {
            command.CommandText += " and je_filter.occurred_at <= @occurred_to";
            command.Parameters.AddWithValue("occurred_to", query.OccurredTo.Value.UtcDateTime);
        }

        if (lineDimensionsJson is not null)
        {
            command.CommandText += " and jl_filter.dimensions @> cast(@line_dimensions as jsonb)";
            command.Parameters.AddWithValue("line_dimensions", lineDimensionsJson);
        }

        command.CommandText += ") order by je.occurred_at, je.global_sequence, jl.line_no;";
        return await ReadJournalEntriesAsync(command, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<LedgerJournalEntryRecord>> GetByPeriodAsync(Guid periodId, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = CreateJournalEntryReadCommand(connection);
        command.CommandText +=
            $"""
            where je.period_id = @period_id
            order by je.occurred_at, je.global_sequence, jl.line_no;
            """;
        command.Parameters.AddWithValue("period_id", periodId);

        return await ReadJournalEntriesAsync(command, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<LedgerJournalEntryRecord>> GetByAggregateAsync(Guid aggregateId, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = CreateJournalEntryReadCommand(connection);
        command.CommandText +=
            $"""
            where je.aggregate_id = @aggregate_id
            order by je.occurred_at, je.global_sequence, jl.line_no;
            """;
        command.Parameters.AddWithValue("aggregate_id", aggregateId);

        return await ReadJournalEntriesAsync(command, ct).ConfigureAwait(false);
    }

    public async Task<LedgerAccountingPeriod?> GetPeriodAsync(Guid periodId, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        return await LoadPeriodAsync(connection, transaction: null, periodId, forUpdate: false, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<LedgerAccountingPeriod>> ListPeriodsAsync(
        Guid? ledgerBookId = null,
        string? status = null,
        string? fundProfileId = null,
        Guid? fundStructureNodeId = null,
        CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select p.period_id,
                   p.ledger_book_id,
                   p.fiscal_year,
                   p.period_no,
                   p.label,
                   p.start_date,
                   p.end_date,
                   p.status,
                   p.opened_at,
                   p.closed_at,
                   p.optimistic_version
            from {Qualified("accounting_periods")} p
            left join {Qualified("ledger_books")} b on b.ledger_book_id = p.ledger_book_id
            where 1 = 1
            """;

        if (ledgerBookId.HasValue)
        {
            command.CommandText += " and p.ledger_book_id = @ledger_book_id";
            command.Parameters.AddWithValue("ledger_book_id", ledgerBookId.Value);
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            command.CommandText += " and p.status = @status";
            command.Parameters.AddWithValue("status", status.Trim());
        }

        if (!string.IsNullOrWhiteSpace(fundProfileId))
        {
            command.CommandText += " and b.fund_profile_id = @fund_profile_id";
            command.Parameters.AddWithValue("fund_profile_id", fundProfileId.Trim());
        }

        if (fundStructureNodeId.HasValue)
        {
            command.CommandText += " and b.fund_structure_node_id = @fund_structure_node_id";
            command.Parameters.AddWithValue("fund_structure_node_id", fundStructureNodeId.Value);
        }

        command.CommandText += " order by p.start_date, p.period_no;";

        var periods = new List<LedgerAccountingPeriod>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            periods.Add(ReadPeriod(reader));
        }

        return periods;
    }

    public async Task<LedgerAccountingPeriod> SavePeriodAsync(
        LedgerAccountingPeriod period,
        long expectedVersion,
        PeriodCloseEventRecord? closeEvent = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(period);

        if (period.PeriodId == Guid.Empty)
        {
            throw new ArgumentException("Period id is required.", nameof(period));
        }

        if (expectedVersion < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedVersion), expectedVersion, "Expected version cannot be negative.");
        }

        if (closeEvent is not null && closeEvent.PeriodId != period.PeriodId)
        {
            throw new ArgumentException("Close event period id must match the accounting period id.", nameof(closeEvent));
        }

        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable, ct).ConfigureAwait(false);

        var current = await LoadPeriodAsync(
                connection,
                transaction,
                period.PeriodId,
                forUpdate: _options.EnablePeriodLocking,
                ct)
            .ConfigureAwait(false);

        LedgerAccountingPeriod saved;
        if (current is null)
        {
            if (expectedVersion != 0)
            {
                throw PeriodVersionConflict(period.PeriodId, expectedVersion, actualVersion: 0);
            }

            saved = period with { Version = 1 };
            await InsertPeriodAsync(connection, transaction, saved, ct).ConfigureAwait(false);
        }
        else
        {
            if (current.Version != expectedVersion)
            {
                throw PeriodVersionConflict(period.PeriodId, expectedVersion, current.Version);
            }

            saved = period with { Version = expectedVersion + 1 };
            var affected = await UpdatePeriodAsync(connection, transaction, saved, expectedVersion, ct).ConfigureAwait(false);
            if (affected != 1)
            {
                var actual = await LoadPeriodAsync(connection, transaction, period.PeriodId, forUpdate: false, ct).ConfigureAwait(false);
                throw PeriodVersionConflict(period.PeriodId, expectedVersion, actual?.Version ?? 0);
            }
        }

        if (closeEvent is not null)
        {
            await InsertCloseEventAsync(connection, transaction, closeEvent, saved.Version, ct).ConfigureAwait(false);
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return saved;
    }

    public async Task<LedgerBookRecord?> GetLedgerBookAsync(Guid ledgerBookId, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select ledger_book_id,
                   fund_profile_id,
                   fund_structure_node_id,
                   fund_structure_node_kind,
                   display_name,
                   base_currency,
                   accounting_basis,
                   accounting_policy_id,
                   accounting_policy_version,
                   description,
                   created_at,
                   updated_at
            from {Qualified("ledger_books")}
            where ledger_book_id = @ledger_book_id;
            """;
        command.Parameters.AddWithValue("ledger_book_id", ledgerBookId);

        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await reader.ReadAsync(ct).ConfigureAwait(false)
            ? ReadLedgerBook(reader)
            : null;
    }

    public async Task<IReadOnlyList<LedgerBookRecord>> ListLedgerBooksAsync(
        string? fundProfileId = null,
        Guid? fundStructureNodeId = null,
        FundStructureNodeKindDto? fundStructureNodeKind = null,
        CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select ledger_book_id,
                   fund_profile_id,
                   fund_structure_node_id,
                   fund_structure_node_kind,
                   display_name,
                   base_currency,
                   accounting_basis,
                   accounting_policy_id,
                   accounting_policy_version,
                   description,
                   created_at,
                   updated_at
            from {Qualified("ledger_books")}
            where 1 = 1
            """;

        if (!string.IsNullOrWhiteSpace(fundProfileId))
        {
            command.CommandText += " and fund_profile_id = @fund_profile_id";
            command.Parameters.AddWithValue("fund_profile_id", fundProfileId.Trim());
        }

        if (fundStructureNodeId.HasValue)
        {
            command.CommandText += " and fund_structure_node_id = @fund_structure_node_id";
            command.Parameters.AddWithValue("fund_structure_node_id", fundStructureNodeId.Value);
        }

        if (fundStructureNodeKind.HasValue)
        {
            command.CommandText += " and fund_structure_node_kind = @fund_structure_node_kind";
            command.Parameters.AddWithValue("fund_structure_node_kind", fundStructureNodeKind.Value.ToString());
        }

        command.CommandText += " order by fund_profile_id, display_name, ledger_book_id;";

        var books = new List<LedgerBookRecord>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            books.Add(ReadLedgerBook(reader));
        }

        return books;
    }

    public async Task<LedgerBookRecord> SaveLedgerBookAsync(LedgerBookRecord book, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(book);

        if (book.LedgerBookId == Guid.Empty)
        {
            throw new ArgumentException("Ledger book id is required.", nameof(book));
        }

        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            insert into {Qualified("ledger_books")} (
                ledger_book_id,
                fund_profile_id,
                fund_structure_node_id,
                fund_structure_node_kind,
                display_name,
                base_currency,
                accounting_basis,
                accounting_policy_id,
                accounting_policy_version,
                description,
                created_at,
                updated_at,
                tenant_id)
            values (
                @ledger_book_id,
                @fund_profile_id,
                @fund_structure_node_id,
                @fund_structure_node_kind,
                @display_name,
                @base_currency,
                @accounting_basis,
                @accounting_policy_id,
                @accounting_policy_version,
                @description,
                @created_at,
                @updated_at,
                -- SEC-005 slice 4c-ii: stamp the owning tenant from the authoritative fund_profile_tenancy
                -- registry (joined on the normalized fund key) so new books carry their partition tenant.
                -- An unbound fund resolves null and stays fail-open until it is claimed. tenant_id is left
                -- out of the on-conflict update below, so an already-stamped book keeps its first owner.
                (select t.tenant_id
                 from {Qualified("fund_profile_tenancy")} t
                 where t.fund_profile_id = lower(trim(@fund_profile_id))))
            on conflict (ledger_book_id) do update
            set fund_profile_id = excluded.fund_profile_id,
                fund_structure_node_id = excluded.fund_structure_node_id,
                fund_structure_node_kind = excluded.fund_structure_node_kind,
                display_name = excluded.display_name,
                base_currency = excluded.base_currency,
                accounting_basis = excluded.accounting_basis,
                accounting_policy_id = excluded.accounting_policy_id,
                accounting_policy_version = excluded.accounting_policy_version,
                description = excluded.description,
                updated_at = excluded.updated_at,
                -- SEC-005 slice 4c-ii: preserve an already-stamped owner (first-owner-wins) but FILL a null
                -- tenant from the re-resolved registry value, so a book first saved while its fund was
                -- unbound is stamped on the next save after the fund is claimed (excluded.tenant_id is the
                -- VALUES subquery above), rather than staying fail-open until a backfill.
                tenant_id = coalesce(ledger_books.tenant_id, excluded.tenant_id)
            returning ledger_book_id,
                      fund_profile_id,
                      fund_structure_node_id,
                      fund_structure_node_kind,
                      display_name,
                      base_currency,
                      accounting_basis,
                      accounting_policy_id,
                      accounting_policy_version,
                      description,
                      created_at,
                      updated_at;
            """;
        command.Parameters.AddWithValue("ledger_book_id", book.LedgerBookId);
        command.Parameters.AddWithValue("fund_profile_id", book.FundProfileId);
        command.Parameters.AddWithValue("fund_structure_node_id", book.FundStructureNodeId);
        command.Parameters.AddWithValue("fund_structure_node_kind", book.FundStructureNodeKind.ToString());
        command.Parameters.AddWithValue("display_name", book.DisplayName);
        command.Parameters.AddWithValue("base_currency", book.BaseCurrency);
        command.Parameters.AddWithValue("accounting_basis", book.AccountingBasis.ToString());
        command.Parameters.AddWithValue("accounting_policy_id", book.AccountingPolicyId);
        command.Parameters.AddWithValue("accounting_policy_version", book.AccountingPolicyVersion);
        command.Parameters.AddWithValue("description", (object?)book.Description ?? DBNull.Value);
        command.Parameters.AddWithValue("created_at", book.CreatedAt.UtcDateTime);
        command.Parameters.AddWithValue("updated_at", book.UpdatedAt.UtcDateTime);

        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            throw new InvalidOperationException($"Ledger book '{book.LedgerBookId}' was not saved.");
        }

        return ReadLedgerBook(reader);
    }

    public async Task<LedgerAccountTaxLotPolicyRecord> SaveTaxLotPolicyAsync(
        LedgerAccountTaxLotPolicyRecord policy,
        CancellationToken ct = default)
    {
        ValidateTaxLotPolicy(policy);

        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            insert into {Qualified("tax_lot_policies")} (
                policy_record_id,
                ledger_book_id,
                account_name,
                account_type,
                symbol,
                financial_account_id,
                relief_method,
                policy_id,
                effective_date,
                rationale,
                created_at,
                updated_at)
            values (
                @policy_record_id,
                @ledger_book_id,
                @account_name,
                @account_type,
                @symbol,
                @financial_account_id,
                @relief_method,
                @policy_id,
                @effective_date,
                @rationale,
                @created_at,
                @updated_at)
            on conflict (policy_record_id) do update
            set ledger_book_id = excluded.ledger_book_id,
                account_name = excluded.account_name,
                account_type = excluded.account_type,
                symbol = excluded.symbol,
                financial_account_id = excluded.financial_account_id,
                relief_method = excluded.relief_method,
                policy_id = excluded.policy_id,
                effective_date = excluded.effective_date,
                rationale = excluded.rationale,
                updated_at = excluded.updated_at
            returning policy_record_id,
                      ledger_book_id,
                      account_name,
                      account_type,
                      symbol,
                      financial_account_id,
                      relief_method,
                      policy_id,
                      effective_date,
                      rationale,
                      created_at,
                      updated_at;
            """;
        command.Parameters.AddWithValue("policy_record_id", policy.PolicyRecordId);
        command.Parameters.AddWithValue("ledger_book_id", policy.LedgerBookId);
        AddAccountParameters(command, policy.Account);
        command.Parameters.AddWithValue("relief_method", policy.ReliefMethod.ToString());
        command.Parameters.AddWithValue("policy_id", RequireLineageText(policy.PolicyId, nameof(policy.PolicyId)));
        command.Parameters.AddWithValue("effective_date", policy.EffectiveDate);
        command.Parameters.AddWithValue("rationale", (object?)NormalizeOptional(policy.Rationale) ?? DBNull.Value);
        command.Parameters.AddWithValue("created_at", policy.CreatedAt.UtcDateTime);
        command.Parameters.AddWithValue("updated_at", policy.UpdatedAt.UtcDateTime);

        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            throw new InvalidOperationException($"Ledger tax-lot policy '{policy.PolicyRecordId}' was not saved.");
        }

        return ReadTaxLotPolicy(reader);
    }

    public async Task<IReadOnlyList<LedgerAccountTaxLotPolicyRecord>> ListTaxLotPoliciesAsync(
        Guid ledgerBookId,
        CancellationToken ct = default)
    {
        if (ledgerBookId == Guid.Empty)
        {
            throw new ArgumentException("Ledger book id is required.", nameof(ledgerBookId));
        }

        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select policy_record_id,
                   ledger_book_id,
                   account_name,
                   account_type,
                   symbol,
                   financial_account_id,
                   relief_method,
                   policy_id,
                   effective_date,
                   rationale,
                   created_at,
                   updated_at
            from {Qualified("tax_lot_policies")}
            where ledger_book_id = @ledger_book_id
            order by account_name, effective_date desc, policy_id;
            """;
        command.Parameters.AddWithValue("ledger_book_id", ledgerBookId);

        var policies = new List<LedgerAccountTaxLotPolicyRecord>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            policies.Add(ReadTaxLotPolicy(reader));
        }

        return policies;
    }

    public async Task<LedgerTaxLotRecord> SaveTaxLotAsync(
        LedgerTaxLotRecord lot,
        CancellationToken ct = default)
    {
        ValidateTaxLot(lot);

        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            insert into {Qualified("tax_lots")} (
                tax_lot_record_id,
                ledger_book_id,
                account_name,
                account_type,
                symbol,
                financial_account_id,
                lot_id,
                acquired_date,
                original_quantity,
                open_quantity,
                unit_cost,
                currency,
                source_journal_entry_id,
                evidence_ref,
                created_at,
                updated_at)
            values (
                @tax_lot_record_id,
                @ledger_book_id,
                @account_name,
                @account_type,
                @symbol,
                @financial_account_id,
                @lot_id,
                @acquired_date,
                @original_quantity,
                @open_quantity,
                @unit_cost,
                @currency,
                @source_journal_entry_id,
                @evidence_ref,
                @created_at,
                @updated_at)
            on conflict (tax_lot_record_id) do update
            set ledger_book_id = excluded.ledger_book_id,
                account_name = excluded.account_name,
                account_type = excluded.account_type,
                symbol = excluded.symbol,
                financial_account_id = excluded.financial_account_id,
                lot_id = excluded.lot_id,
                acquired_date = excluded.acquired_date,
                original_quantity = excluded.original_quantity,
                open_quantity = excluded.open_quantity,
                unit_cost = excluded.unit_cost,
                currency = excluded.currency,
                source_journal_entry_id = excluded.source_journal_entry_id,
                evidence_ref = excluded.evidence_ref,
                updated_at = excluded.updated_at
            returning tax_lot_record_id,
                      ledger_book_id,
                      account_name,
                      account_type,
                      symbol,
                      financial_account_id,
                      lot_id,
                      acquired_date,
                      original_quantity,
                      open_quantity,
                      unit_cost,
                      currency,
                      source_journal_entry_id,
                      evidence_ref,
                      created_at,
                      updated_at;
            """;
        command.Parameters.AddWithValue("tax_lot_record_id", lot.TaxLotRecordId);
        command.Parameters.AddWithValue("ledger_book_id", lot.LedgerBookId);
        AddAccountParameters(command, lot.Account);
        command.Parameters.AddWithValue("lot_id", RequireLineageText(lot.LotId, nameof(lot.LotId)));
        command.Parameters.AddWithValue("acquired_date", lot.AcquiredDate);
        command.Parameters.AddWithValue("original_quantity", lot.OriginalQuantity);
        command.Parameters.AddWithValue("open_quantity", lot.OpenQuantity);
        command.Parameters.AddWithValue("unit_cost", lot.UnitCost);
        command.Parameters.AddWithValue("currency", RequireLineageText(lot.Currency, nameof(lot.Currency)).ToUpperInvariant());
        command.Parameters.AddWithValue("source_journal_entry_id", (object?)lot.SourceJournalEntryId ?? DBNull.Value);
        command.Parameters.AddWithValue("evidence_ref", (object?)NormalizeOptional(lot.EvidenceRef) ?? DBNull.Value);
        command.Parameters.AddWithValue("created_at", lot.CreatedAt.UtcDateTime);
        command.Parameters.AddWithValue("updated_at", lot.UpdatedAt.UtcDateTime);

        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            throw new InvalidOperationException($"Ledger tax lot '{lot.TaxLotRecordId}' was not saved.");
        }

        return ReadTaxLot(reader);
    }

    public async Task<IReadOnlyList<LedgerTaxLotRecord>> ListOpenTaxLotsAsync(
        Guid ledgerBookId,
        LedgerAccount account,
        CancellationToken ct = default)
    {
        if (ledgerBookId == Guid.Empty)
        {
            throw new ArgumentException("Ledger book id is required.", nameof(ledgerBookId));
        }

        ArgumentNullException.ThrowIfNull(account);

        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select tax_lot_record_id,
                   ledger_book_id,
                   account_name,
                   account_type,
                   symbol,
                   financial_account_id,
                   lot_id,
                   acquired_date,
                   original_quantity,
                   open_quantity,
                   unit_cost,
                   currency,
                   source_journal_entry_id,
                   evidence_ref,
                   created_at,
                   updated_at
            from {Qualified("tax_lots")}
            where ledger_book_id = @ledger_book_id
              and account_name = @account_name
              and account_type = @account_type
              and symbol is not distinct from @symbol
              and financial_account_id is not distinct from @financial_account_id
              and open_quantity > 0
            order by acquired_date, lot_id;
            """;
        command.Parameters.AddWithValue("ledger_book_id", ledgerBookId);
        AddAccountParameters(command, account);

        var lots = new List<LedgerTaxLotRecord>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            lots.Add(ReadTaxLot(reader));
        }

        return lots;
    }

    private async Task InsertJournalEntryAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        LedgerJournalEntryWrite entry,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            insert into {Qualified("journal_entries")} (
                journal_entry_id,
                aggregate_id,
                period_id,
                command_id,
                correlation_id,
                accounting_basis,
                accounting_policy_id,
                accounting_policy_version,
                rule_id,
                rule_version,
                source_event_id,
                source_journal_entry_id,
                posting_kind,
                adjustment_approval_metadata,
                occurred_at,
                description,
                metadata)
            values (
                @journal_entry_id,
                @aggregate_id,
                @period_id,
                @command_id,
                @correlation_id,
                @accounting_basis,
                @accounting_policy_id,
                @accounting_policy_version,
                @rule_id,
                @rule_version,
                @source_event_id,
                @source_journal_entry_id,
                @posting_kind,
                cast(@adjustment_approval_metadata as jsonb),
                @occurred_at,
                @description,
                cast(@metadata as jsonb));
            """;
        command.Parameters.AddWithValue("journal_entry_id", entry.Entry.JournalEntryId);
        command.Parameters.AddWithValue("aggregate_id", entry.AggregateId);
        command.Parameters.AddWithValue("period_id", entry.PeriodId);
        command.Parameters.AddWithValue("command_id", (object?)entry.CommandId ?? DBNull.Value);
        command.Parameters.AddWithValue("correlation_id", (object?)entry.CorrelationId ?? DBNull.Value);
        command.Parameters.AddWithValue("accounting_basis", entry.AccountingBasis.ToString());
        command.Parameters.AddWithValue("accounting_policy_id", RequireLineageText(entry.AccountingPolicyId, nameof(entry.AccountingPolicyId)));
        command.Parameters.AddWithValue("accounting_policy_version", RequireLineageText(entry.AccountingPolicyVersion, nameof(entry.AccountingPolicyVersion)));
        command.Parameters.AddWithValue("rule_id", (object?)NormalizeOptional(entry.RuleId) ?? DBNull.Value);
        command.Parameters.AddWithValue("rule_version", (object?)NormalizeOptional(entry.RuleVersion) ?? DBNull.Value);
        command.Parameters.AddWithValue("source_event_id", (object?)entry.SourceEventId ?? DBNull.Value);
        command.Parameters.AddWithValue("source_journal_entry_id", (object?)entry.SourceJournalEntryId ?? DBNull.Value);
        command.Parameters.AddWithValue("posting_kind", entry.PostingKind.ToString());
        command.Parameters.AddWithValue("adjustment_approval_metadata", SerializeAdjustmentApproval(entry.AdjustmentApproval));
        command.Parameters.AddWithValue("occurred_at", entry.Entry.Timestamp.UtcDateTime);
        command.Parameters.AddWithValue("description", entry.Entry.Description);
        command.Parameters.AddWithValue("metadata", JsonSerializer.Serialize(entry.Entry.Metadata.Normalize(), JsonOptions));
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private async Task ValidateJournalBasisAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        LedgerJournalEntryWrite entry,
        LedgerAccountingPeriod period,
        CancellationToken ct)
    {
        _ = RequireLineageText(entry.AccountingPolicyId, nameof(entry.AccountingPolicyId));
        _ = RequireLineageText(entry.AccountingPolicyVersion, nameof(entry.AccountingPolicyVersion));

        if (period.LedgerBookId is not { } ledgerBookId)
        {
            if (entry.LedgerBookId.HasValue)
            {
                throw new LedgerValidationException(
                    $"Journal entry '{entry.Entry.JournalEntryId}' targets ledger book '{entry.LedgerBookId.Value}' but period '{entry.PeriodId}' is not scoped to a ledger book.");
            }

            if (entry.AccountingBasis != AccountingBasisKindDto.Primary)
            {
                throw new LedgerValidationException(
                    $"Legacy period '{entry.PeriodId}' accepts only Primary basis postings.");
            }

            return;
        }

        if (entry.LedgerBookId.HasValue && entry.LedgerBookId.Value != ledgerBookId)
        {
            throw new LedgerValidationException(
                $"Journal entry '{entry.Entry.JournalEntryId}' ledger book '{entry.LedgerBookId.Value}' does not match period '{entry.PeriodId}' ledger book '{ledgerBookId}'.");
        }

        var book = await LoadLedgerBookAsync(connection, transaction, ledgerBookId, ct).ConfigureAwait(false)
            ?? throw new LedgerValidationException($"Ledger book '{ledgerBookId}' was not found for period '{entry.PeriodId}'.");

        if (book.AccountingBasis != entry.AccountingBasis)
        {
            throw new LedgerValidationException(
                $"Journal entry '{entry.Entry.JournalEntryId}' basis '{entry.AccountingBasis}' does not match ledger book '{book.DisplayName}' basis '{book.AccountingBasis}'.");
        }
    }

    private async Task InsertJournalLegsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        LedgerJournalEntryWrite entry,
        CancellationToken ct)
    {
        for (var i = 0; i < entry.Entry.Lines.Count; i++)
        {
            var leg = entry.Entry.Lines[i];
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                $"""
                insert into {Qualified("journal_legs")} (
                    entry_id,
                    journal_entry_id,
                    line_no,
                    aggregate_id,
                    period_id,
                    command_id,
                    correlation_id,
                    accounting_basis,
                    accounting_policy_id,
                    accounting_policy_version,
                    rule_id,
                    rule_version,
                    source_event_id,
                    source_journal_entry_id,
                    posting_kind,
                    adjustment_approval_metadata,
                    occurred_at,
                    account_name,
                    account_type,
                    symbol,
                    financial_account_id,
                    debit,
                    credit,
                    description,
                    dimensions)
                values (
                    @entry_id,
                    @journal_entry_id,
                    @line_no,
                    @aggregate_id,
                    @period_id,
                    @command_id,
                    @correlation_id,
                    @accounting_basis,
                    @accounting_policy_id,
                    @accounting_policy_version,
                    @rule_id,
                    @rule_version,
                    @source_event_id,
                    @source_journal_entry_id,
                    @posting_kind,
                    cast(@adjustment_approval_metadata as jsonb),
                    @occurred_at,
                    @account_name,
                    @account_type,
                    @symbol,
                    @financial_account_id,
                    @debit,
                    @credit,
                    @description,
                    cast(@dimensions as jsonb));
                """;
            command.Parameters.AddWithValue("entry_id", leg.EntryId);
            command.Parameters.AddWithValue("journal_entry_id", leg.JournalEntryId);
            command.Parameters.AddWithValue("line_no", i + 1);
            command.Parameters.AddWithValue("aggregate_id", entry.AggregateId);
            command.Parameters.AddWithValue("period_id", entry.PeriodId);
            command.Parameters.AddWithValue("command_id", (object?)entry.CommandId ?? DBNull.Value);
            command.Parameters.AddWithValue("correlation_id", (object?)entry.CorrelationId ?? DBNull.Value);
            command.Parameters.AddWithValue("accounting_basis", entry.AccountingBasis.ToString());
            command.Parameters.AddWithValue("accounting_policy_id", RequireLineageText(entry.AccountingPolicyId, nameof(entry.AccountingPolicyId)));
            command.Parameters.AddWithValue("accounting_policy_version", RequireLineageText(entry.AccountingPolicyVersion, nameof(entry.AccountingPolicyVersion)));
            command.Parameters.AddWithValue("rule_id", (object?)NormalizeOptional(entry.RuleId) ?? DBNull.Value);
            command.Parameters.AddWithValue("rule_version", (object?)NormalizeOptional(entry.RuleVersion) ?? DBNull.Value);
            command.Parameters.AddWithValue("source_event_id", (object?)entry.SourceEventId ?? DBNull.Value);
            command.Parameters.AddWithValue("source_journal_entry_id", (object?)entry.SourceJournalEntryId ?? DBNull.Value);
            command.Parameters.AddWithValue("posting_kind", entry.PostingKind.ToString());
            command.Parameters.AddWithValue("adjustment_approval_metadata", SerializeAdjustmentApproval(entry.AdjustmentApproval));
            command.Parameters.AddWithValue("occurred_at", leg.Timestamp.UtcDateTime);
            command.Parameters.AddWithValue("account_name", leg.Account.Name);
            command.Parameters.AddWithValue("account_type", leg.Account.AccountType.ToString());
            command.Parameters.AddWithValue("symbol", (object?)leg.Account.Symbol ?? DBNull.Value);
            command.Parameters.AddWithValue("financial_account_id", (object?)leg.Account.FinancialAccountId ?? DBNull.Value);
            command.Parameters.AddWithValue("debit", leg.Debit);
            command.Parameters.AddWithValue("credit", leg.Credit);
            command.Parameters.AddWithValue("description", leg.Description);
            command.Parameters.AddWithValue("dimensions", SerializeLineDimensions(leg.Dimensions));
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }

    private NpgsqlCommand CreateJournalEntryReadCommand(NpgsqlConnection connection)
    {
        var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select je.global_sequence,
                   je.journal_entry_id,
                   je.aggregate_id,
                   je.period_id,
                   je.command_id,
                   je.correlation_id,
                   je.accounting_basis,
                   je.accounting_policy_id,
                   je.accounting_policy_version,
                   je.rule_id,
                   je.rule_version,
                   je.source_event_id,
                   je.source_journal_entry_id,
                   je.posting_kind,
                   je.adjustment_approval_metadata::text,
                   je.occurred_at,
                   je.description,
                   je.metadata::text,
                   je.created_at,
                   jl.entry_id,
                   jl.account_name,
                   jl.account_type,
                   jl.symbol,
                   jl.financial_account_id,
                   jl.debit,
                   jl.credit,
                   jl.description,
                   jl.occurred_at,
                   jl.dimensions::text
            from {Qualified("journal_entries")} je
            join {Qualified("journal_legs")} jl on jl.journal_entry_id = je.journal_entry_id
            """ + "\n";
        return command;
    }

    private static async Task<IReadOnlyList<LedgerJournalEntryRecord>> ReadJournalEntriesAsync(
        NpgsqlCommand command,
        CancellationToken ct)
    {
        var results = new List<LedgerJournalEntryRecord>();
        JournalEntryBuilder? current = null;

        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var journalEntryId = reader.GetGuid(1);
            if (current is null || current.JournalEntryId != journalEntryId)
            {
                if (current is not null)
                {
                    results.Add(current.Build());
                }

                current = new JournalEntryBuilder(
                    GlobalSequence: reader.GetInt64(0),
                    JournalEntryId: journalEntryId,
                    AggregateId: reader.GetGuid(2),
                    PeriodId: reader.GetGuid(3),
                    CommandId: reader.IsDBNull(4) ? null : reader.GetGuid(4),
                    CorrelationId: reader.IsDBNull(5) ? null : reader.GetGuid(5),
                    AccountingBasis: Enum.Parse<AccountingBasisKindDto>(reader.GetString(6), ignoreCase: true),
                    AccountingPolicyId: reader.GetString(7),
                    AccountingPolicyVersion: reader.GetString(8),
                    RuleId: reader.IsDBNull(9) ? null : reader.GetString(9),
                    RuleVersion: reader.IsDBNull(10) ? null : reader.GetString(10),
                    SourceEventId: reader.IsDBNull(11) ? null : reader.GetGuid(11),
                    SourceJournalEntryId: reader.IsDBNull(12) ? null : reader.GetGuid(12),
                    PostingKind: Enum.Parse<LedgerPostingKindDto>(reader.GetString(13), ignoreCase: true),
                    AdjustmentApproval: reader.IsDBNull(14) ? null : DeserializeAdjustmentApproval(reader.GetString(14)),
                    Timestamp: ReadUtcDateTimeOffset(reader, 15),
                    Description: reader.GetString(16),
                    Metadata: DeserializeMetadata(reader.GetString(17)),
                    CreatedAt: ReadUtcDateTimeOffset(reader, 18));
            }

            var accountType = Enum.Parse<LedgerAccountType>(reader.GetString(21), ignoreCase: true);
            var account = new LedgerAccount(
                reader.GetString(20),
                accountType,
                reader.IsDBNull(22) ? null : reader.GetString(22),
                reader.IsDBNull(23) ? null : reader.GetString(23));
            current.Lines.Add(new LedgerEntry(
                reader.GetGuid(19),
                journalEntryId,
                ReadUtcDateTimeOffset(reader, 27),
                account,
                reader.GetDecimal(24),
                reader.GetDecimal(25),
                reader.GetString(26),
                reader.IsDBNull(28) ? null : DeserializeLineDimensions(reader.GetString(28))));
        }

        if (current is not null)
        {
            results.Add(current.Build());
        }

        return results;
    }

    private async Task<LedgerAccountingPeriod?> LoadPeriodAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        Guid periodId,
        bool forUpdate,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            select period_id,
                   ledger_book_id,
                   fiscal_year,
                   period_no,
                   label,
                   start_date,
                   end_date,
                   status,
                   opened_at,
                   closed_at,
                   optimistic_version
            from {Qualified("accounting_periods")}
            where period_id = @period_id
            {ForUpdateClause(forUpdate)};
            """;
        command.Parameters.AddWithValue("period_id", periodId);

        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            return null;
        }

        return ReadPeriod(reader);
    }

    private async Task InsertPeriodAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        LedgerAccountingPeriod period,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            insert into {Qualified("accounting_periods")} (
                period_id,
                ledger_book_id,
                fiscal_year,
                period_no,
                label,
                start_date,
                end_date,
                status,
                opened_at,
                closed_at,
                optimistic_version,
                updated_at,
                tenant_id)
            values (
                @period_id,
                @ledger_book_id,
                @fiscal_year,
                @period_no,
                @label,
                @start_date,
                @end_date,
                @status,
                @opened_at,
                @closed_at,
                @optimistic_version,
                now(),
                -- SEC-005 slice 4c-ii: resolve the period's partition tenant from the AUTHORITATIVE
                -- fund_profile_tenancy registry via its ledger book's fund, not the book's cached tenant_id
                -- column. A book saved before its fund was claimed has a null tenant_id (BindAsync does not
                -- backfill books), so copying b.tenant_id would leave a period created after the claim
                -- fail-open; joining the registry through b.fund_profile_id stamps it correctly. Only a
                -- genuinely unbound fund (no registry row) or absent book yields null and stays fail-open.
                (select t.tenant_id
                 from {Qualified("ledger_books")} b
                 join {Qualified("fund_profile_tenancy")} t
                   on t.fund_profile_id = lower(trim(b.fund_profile_id))
                 where b.ledger_book_id = @ledger_book_id));
            """;
        AddPeriodParameters(command, period);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private async Task<int> UpdatePeriodAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        LedgerAccountingPeriod period,
        long expectedVersion,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            update {Qualified("accounting_periods")}
            set fiscal_year = @fiscal_year,
                ledger_book_id = @ledger_book_id,
                period_no = @period_no,
                label = @label,
                start_date = @start_date,
                end_date = @end_date,
                status = @status,
                opened_at = @opened_at,
                closed_at = @closed_at,
                optimistic_version = @optimistic_version,
                updated_at = now(),
                -- SEC-005 slice 4c-ii: the book can change on this update — re-resolve the period's tenant
                -- from the authoritative registry via the (new) book's fund (not the book's cached tenant_id,
                -- which may be a stale null), and FILL a null while preserving an existing tenant when the
                -- fund is genuinely unbound, so a period moved onto a claimed fund stops being fail-open.
                tenant_id = coalesce(
                    (select t.tenant_id
                     from {Qualified("ledger_books")} b
                     join {Qualified("fund_profile_tenancy")} t
                       on t.fund_profile_id = lower(trim(b.fund_profile_id))
                     where b.ledger_book_id = @ledger_book_id),
                    tenant_id)
            where period_id = @period_id
              and optimistic_version = @expected_version;
            """;
        AddPeriodParameters(command, period);
        command.Parameters.AddWithValue("expected_version", expectedVersion);
        return await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private async Task InsertCloseEventAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        PeriodCloseEventRecord closeEvent,
        long periodVersion,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            insert into {Qualified("period_close_events")} (
                event_id,
                period_id,
                prior_status,
                new_status,
                closed_by,
                notes,
                recorded_at,
                period_version)
            values (
                @event_id,
                @period_id,
                @prior_status,
                @new_status,
                @closed_by,
                @notes,
                @recorded_at,
                @period_version);
            """;
        command.Parameters.AddWithValue("event_id", closeEvent.EventId);
        command.Parameters.AddWithValue("period_id", closeEvent.PeriodId);
        command.Parameters.AddWithValue("prior_status", closeEvent.PriorStatus);
        command.Parameters.AddWithValue("new_status", closeEvent.NewStatus);
        command.Parameters.AddWithValue("closed_by", closeEvent.ClosedBy);
        command.Parameters.AddWithValue("notes", closeEvent.Notes);
        command.Parameters.AddWithValue("recorded_at", closeEvent.RecordedAt.UtcDateTime);
        command.Parameters.AddWithValue("period_version", periodVersion);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static void AddPeriodParameters(NpgsqlCommand command, LedgerAccountingPeriod period)
    {
        command.Parameters.AddWithValue("period_id", period.PeriodId);
        command.Parameters.AddWithValue("ledger_book_id", (object?)period.LedgerBookId ?? DBNull.Value);
        command.Parameters.AddWithValue("fiscal_year", period.FiscalYear);
        command.Parameters.AddWithValue("period_no", period.PeriodNo);
        command.Parameters.AddWithValue("label", period.Label);
        command.Parameters.AddWithValue("start_date", period.StartDate);
        command.Parameters.AddWithValue("end_date", period.EndDate);
        command.Parameters.AddWithValue("status", period.Status);
        command.Parameters.AddWithValue("opened_at", period.OpenedAt.UtcDateTime);
        command.Parameters.AddWithValue("closed_at", (object?)period.ClosedAt?.UtcDateTime ?? DBNull.Value);
        command.Parameters.AddWithValue("optimistic_version", period.Version);
    }

    private static LedgerAccountingPeriod ReadPeriod(NpgsqlDataReader reader)
        => new(
            reader.GetGuid(0),
            reader.IsDBNull(1) ? null : reader.GetGuid(1),
            reader.GetInt32(2),
            reader.GetInt32(3),
            reader.GetString(4),
            DateOnly.FromDateTime(reader.GetDateTime(5)),
            DateOnly.FromDateTime(reader.GetDateTime(6)),
            reader.GetString(7),
            ReadUtcDateTimeOffset(reader, 8),
            reader.IsDBNull(9) ? null : ReadUtcDateTimeOffset(reader, 9),
            reader.GetInt64(10));

    private static LedgerBookRecord ReadLedgerBook(NpgsqlDataReader reader)
        => new(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetGuid(2),
            Enum.Parse<FundStructureNodeKindDto>(reader.GetString(3), ignoreCase: true),
            reader.GetString(4),
            reader.GetString(5),
            ReadUtcDateTimeOffset(reader, 10),
            ReadUtcDateTimeOffset(reader, 11),
            reader.IsDBNull(9) ? null : reader.GetString(9),
            Enum.Parse<AccountingBasisKindDto>(reader.GetString(6), ignoreCase: true),
            reader.GetString(7),
            reader.GetString(8));

    private async Task<LedgerBookRecord?> LoadLedgerBookAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        Guid ledgerBookId,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            select ledger_book_id,
                   fund_profile_id,
                   fund_structure_node_id,
                   fund_structure_node_kind,
                   display_name,
                   base_currency,
                   accounting_basis,
                   accounting_policy_id,
                   accounting_policy_version,
                   description,
                   created_at,
                   updated_at
            from {Qualified("ledger_books")}
            where ledger_book_id = @ledger_book_id;
            """;
        command.Parameters.AddWithValue("ledger_book_id", ledgerBookId);

        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await reader.ReadAsync(ct).ConfigureAwait(false)
            ? ReadLedgerBook(reader)
            : null;
    }

    private static void ValidateTaxLotPolicy(LedgerAccountTaxLotPolicyRecord policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(policy.Account);

        if (policy.PolicyRecordId == Guid.Empty)
        {
            throw new ArgumentException("Tax-lot policy record id is required.", nameof(policy));
        }

        if (policy.LedgerBookId == Guid.Empty)
        {
            throw new ArgumentException("Ledger book id is required.", nameof(policy));
        }

        _ = RequireLineageText(policy.PolicyId, nameof(policy.PolicyId));
    }

    private static void ValidateTaxLot(LedgerTaxLotRecord lot)
    {
        ArgumentNullException.ThrowIfNull(lot);
        ArgumentNullException.ThrowIfNull(lot.Account);

        if (lot.TaxLotRecordId == Guid.Empty)
        {
            throw new ArgumentException("Tax-lot record id is required.", nameof(lot));
        }

        if (lot.LedgerBookId == Guid.Empty)
        {
            throw new ArgumentException("Ledger book id is required.", nameof(lot));
        }

        _ = RequireLineageText(lot.LotId, nameof(lot.LotId));
        _ = RequireLineageText(lot.Currency, nameof(lot.Currency));

        if (lot.OriginalQuantity <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(lot), lot.OriginalQuantity, "Original tax-lot quantity must be positive.");
        }

        if (lot.OpenQuantity < 0m || lot.OpenQuantity > lot.OriginalQuantity)
        {
            throw new ArgumentOutOfRangeException(nameof(lot), lot.OpenQuantity, "Open tax-lot quantity must be between zero and original quantity.");
        }

        if (lot.UnitCost < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(lot), lot.UnitCost, "Tax-lot unit cost cannot be negative.");
        }
    }

    private static void AddAccountParameters(NpgsqlCommand command, LedgerAccount account)
    {
        command.Parameters.AddWithValue("account_name", RequireLineageText(account.Name, nameof(account.Name)));
        command.Parameters.AddWithValue("account_type", account.AccountType.ToString());
        command.Parameters.AddWithValue("symbol", (object?)NormalizeOptional(account.Symbol) ?? DBNull.Value);
        command.Parameters.AddWithValue("financial_account_id", (object?)NormalizeOptional(account.FinancialAccountId) ?? DBNull.Value);
    }

    private static LedgerAccount ReadLedgerAccount(NpgsqlDataReader reader, int nameOrdinal)
        => new(
            reader.GetString(nameOrdinal),
            Enum.Parse<LedgerAccountType>(reader.GetString(nameOrdinal + 1), ignoreCase: true),
            reader.IsDBNull(nameOrdinal + 2) ? null : reader.GetString(nameOrdinal + 2),
            reader.IsDBNull(nameOrdinal + 3) ? null : reader.GetString(nameOrdinal + 3));

    private static LedgerAccountTaxLotPolicyRecord ReadTaxLotPolicy(NpgsqlDataReader reader)
        => new(
            reader.GetGuid(0),
            reader.GetGuid(1),
            ReadLedgerAccount(reader, 2),
            Enum.Parse<LedgerTaxLotReliefMethod>(reader.GetString(6), ignoreCase: true),
            reader.GetString(7),
            DateOnly.FromDateTime(reader.GetDateTime(8)),
            ReadUtcDateTimeOffset(reader, 10),
            ReadUtcDateTimeOffset(reader, 11),
            reader.IsDBNull(9) ? null : reader.GetString(9));

    private static LedgerTaxLotRecord ReadTaxLot(NpgsqlDataReader reader)
        => new(
            reader.GetGuid(0),
            reader.GetGuid(1),
            ReadLedgerAccount(reader, 2),
            reader.GetString(6),
            DateOnly.FromDateTime(reader.GetDateTime(7)),
            reader.GetDecimal(8),
            reader.GetDecimal(9),
            reader.GetDecimal(10),
            reader.GetString(11),
            ReadUtcDateTimeOffset(reader, 14),
            ReadUtcDateTimeOffset(reader, 15),
            reader.IsDBNull(12) ? null : reader.GetGuid(12),
            reader.IsDBNull(13) ? null : reader.GetString(13));

    private async Task<NpgsqlConnection> OpenConnectionAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.ConnectionString))
        {
            throw new InvalidOperationException("LedgerJournalStoreOptions.ConnectionString is not configured.");
        }

        var connection = new NpgsqlConnection(_options.ConnectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);
        return connection;
    }

    private string Qualified(string table) => $"{ValidateIdentifier(_options.SchemaName, nameof(_options.SchemaName))}.{ValidateIdentifier(table, nameof(table))}";

    private static string ForUpdateClause(bool enabled) => enabled ? "for update" : string.Empty;

    private static InvalidOperationException PeriodVersionConflict(Guid periodId, long expectedVersion, long actualVersion)
        => new($"Ledger period version conflict for {periodId}. Expected {expectedVersion}, actual {actualVersion}.");

    private static string ValidateIdentifier(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{parameterName} is required.");
        }

        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (!(char.IsAsciiLetterOrDigit(c) || c == '_'))
            {
                throw new InvalidOperationException($"{parameterName} contains an invalid identifier character.");
            }
        }

        return value;
    }

    private static DateTimeOffset ReadUtcDateTimeOffset(NpgsqlDataReader reader, int ordinal)
    {
        var value = reader.GetDateTime(ordinal);
        if (value.Kind == DateTimeKind.Unspecified)
        {
            value = DateTime.SpecifyKind(value, DateTimeKind.Utc);
        }

        return new DateTimeOffset(value.ToUniversalTime(), TimeSpan.Zero);
    }

    private static JournalEntryMetadata DeserializeMetadata(string json)
        => JsonSerializer.Deserialize<JournalEntryMetadata>(json, JsonOptions) ?? new JournalEntryMetadata();

    private static object SerializeAdjustmentApproval(LedgerAdjustmentApprovalMetadataDto? approval)
        => approval is null ? DBNull.Value : JsonSerializer.Serialize(approval, JsonOptions);

    private static object SerializeLineDimensions(LedgerLineDimensionSet? dimensions)
    {
        var canonical = CanonicalizeLineDimensions(dimensions);
        return canonical is null ? DBNull.Value : JsonSerializer.Serialize(canonical, JsonOptions);
    }

    private static LedgerLineDimensionSet DeserializeLineDimensions(string json)
    {
        var dimensions = JsonSerializer.Deserialize<LedgerLineDimensionSet>(json, JsonOptions)
           ?? throw new LedgerValidationException("Stored ledger line dimensions are invalid.");
        return CanonicalizeLineDimensions(dimensions) ?? new LedgerLineDimensionSet();
    }

    internal static string? BuildLineDimensionContainmentJson(LedgerLineDimensionSet? dimensions)
    {
        dimensions = CanonicalizeLineDimensions(dimensions);
        if (dimensions is null)
        {
            return null;
        }

        var values = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["externalGlDimensions"] = dimensions.ExternalGlDimensions
        };
        AddDimension(values, "fundId", dimensions.FundId);
        AddDimension(values, "entityId", dimensions.EntityId);
        AddDimension(values, "sleeveId", dimensions.SleeveId);
        AddDimension(values, "strategyId", dimensions.StrategyId);
        AddDimension(values, "investorId", dimensions.InvestorId);
        AddDimension(values, "capitalAccountId", dimensions.CapitalAccountId);
        if (dimensions.InstrumentId.HasValue)
        {
            values["instrumentId"] = dimensions.InstrumentId.Value;
        }

        AddDimension(values, "taxLotId", dimensions.TaxLotId);
        AddDimension(values, "costCenterId", dimensions.CostCenterId);
        AddDimension(values, "counterpartyId", dimensions.CounterpartyId);
        AddDimension(values, "organizationId", dimensions.OrganizationId);
        AddDimension(values, "portfolioId", dimensions.PortfolioId);
        AddDimension(values, "bookId", dimensions.BookId);
        AddDimension(values, "accountId", dimensions.AccountId);
        AddDimension(values, "customerId", dimensions.CustomerId);
        AddDimension(values, "vendorId", dimensions.VendorId);
        AddDimension(values, "projectId", dimensions.ProjectId);

        if (values["externalGlDimensions"] is IReadOnlyDictionary<string, string> { Count: 0 })
        {
            values.Remove("externalGlDimensions");
        }

        return JsonSerializer.Serialize(values, JsonOptions);
    }

    internal static string BuildJournalEntryQueryFilterSql(
        string journalEntriesTable,
        string journalLegsTable,
        string accountingPeriodsTable)
        => $"""

            where je.journal_entry_id in (
                select distinct je_filter.journal_entry_id
                from {journalEntriesTable} je_filter
                join {journalLegsTable} jl_filter on jl_filter.journal_entry_id = je_filter.journal_entry_id
                join {accountingPeriodsTable} p_filter on p_filter.period_id = je_filter.period_id
                where 1 = 1
            """;

    private static void AddDimension(IDictionary<string, object> values, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            values[name] = value.Trim();
        }
    }

    internal static LedgerLineDimensionSet? CanonicalizeLineDimensions(LedgerLineDimensionSet? dimensions)
    {
        if (dimensions is null)
        {
            return null;
        }

        var externalGlDimensions = NormalizeExternalGlDimensions(dimensions.ExternalGlDimensions);
        var canonical = new LedgerLineDimensionSet(
            FundId: NormalizeOptional(dimensions.FundId),
            EntityId: NormalizeOptional(dimensions.EntityId),
            SleeveId: NormalizeOptional(dimensions.SleeveId),
            StrategyId: NormalizeOptional(dimensions.StrategyId),
            InvestorId: NormalizeOptional(dimensions.InvestorId),
            CapitalAccountId: NormalizeOptional(dimensions.CapitalAccountId),
            InstrumentId: dimensions.InstrumentId,
            TaxLotId: NormalizeOptional(dimensions.TaxLotId),
            CostCenterId: NormalizeOptional(dimensions.CostCenterId),
            CounterpartyId: NormalizeOptional(dimensions.CounterpartyId),
            ExternalGlDimensions: externalGlDimensions,
            OrganizationId: NormalizeOptional(dimensions.OrganizationId),
            PortfolioId: NormalizeOptional(dimensions.PortfolioId),
            BookId: NormalizeOptional(dimensions.BookId),
            AccountId: NormalizeOptional(dimensions.AccountId),
            CustomerId: NormalizeOptional(dimensions.CustomerId),
            VendorId: NormalizeOptional(dimensions.VendorId),
            ProjectId: NormalizeOptional(dimensions.ProjectId));

        return HasAnyLineDimension(canonical) ? canonical : null;
    }

    private static IReadOnlyDictionary<string, string> NormalizeExternalGlDimensions(IReadOnlyDictionary<string, string> dimensions)
        => dimensions
            .Select(static pair => new
            {
                Key = NormalizeOptional(pair.Key),
                Value = NormalizeOptional(pair.Value)
            })
            .Where(static pair => pair.Key is not null && pair.Value is not null)
            .GroupBy(static pair => pair.Key!, StringComparer.OrdinalIgnoreCase)
            .OrderBy(static group => group.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.First().Key!, static group => group.First().Value!, StringComparer.OrdinalIgnoreCase);

    private static bool HasAnyLineDimension(LedgerLineDimensionSet dimensions)
        => !string.IsNullOrWhiteSpace(dimensions.FundId) ||
           !string.IsNullOrWhiteSpace(dimensions.EntityId) ||
           !string.IsNullOrWhiteSpace(dimensions.SleeveId) ||
           !string.IsNullOrWhiteSpace(dimensions.StrategyId) ||
           !string.IsNullOrWhiteSpace(dimensions.InvestorId) ||
           !string.IsNullOrWhiteSpace(dimensions.CapitalAccountId) ||
           dimensions.InstrumentId.HasValue ||
           !string.IsNullOrWhiteSpace(dimensions.TaxLotId) ||
           !string.IsNullOrWhiteSpace(dimensions.CostCenterId) ||
           !string.IsNullOrWhiteSpace(dimensions.CounterpartyId) ||
           !string.IsNullOrWhiteSpace(dimensions.OrganizationId) ||
           !string.IsNullOrWhiteSpace(dimensions.PortfolioId) ||
           !string.IsNullOrWhiteSpace(dimensions.BookId) ||
           !string.IsNullOrWhiteSpace(dimensions.AccountId) ||
           !string.IsNullOrWhiteSpace(dimensions.CustomerId) ||
           !string.IsNullOrWhiteSpace(dimensions.VendorId) ||
           !string.IsNullOrWhiteSpace(dimensions.ProjectId) ||
           dimensions.ExternalGlDimensions.Any(static pair => !string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value));

    private static LedgerAdjustmentApprovalMetadataDto DeserializeAdjustmentApproval(string json)
        => JsonSerializer.Deserialize<LedgerAdjustmentApprovalMetadataDto>(json, JsonOptions)
           ?? throw new LedgerValidationException("Stored adjustment approval metadata is invalid.");

    private static string RequireLineageText(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new LedgerValidationException($"{parameterName} is required for basis-aware ledger lineage.");
        }

        return value.Trim();
    }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private sealed record JournalEntryBuilder(
        long GlobalSequence,
        Guid JournalEntryId,
        Guid AggregateId,
        Guid PeriodId,
        Guid? CommandId,
        Guid? CorrelationId,
        AccountingBasisKindDto AccountingBasis,
        string AccountingPolicyId,
        string AccountingPolicyVersion,
        string? RuleId,
        string? RuleVersion,
        Guid? SourceEventId,
        Guid? SourceJournalEntryId,
        LedgerPostingKindDto PostingKind,
        LedgerAdjustmentApprovalMetadataDto? AdjustmentApproval,
        DateTimeOffset Timestamp,
        string Description,
        JournalEntryMetadata Metadata,
        DateTimeOffset CreatedAt)
    {
        public List<LedgerEntry> Lines { get; } = [];

        public LedgerJournalEntryRecord Build()
            => new(
                new JournalEntry(JournalEntryId, Timestamp, Description, Lines, Metadata),
                AggregateId,
                PeriodId,
                CommandId,
                CorrelationId,
                GlobalSequence,
                CreatedAt,
                AccountingBasis,
                AccountingPolicyId,
                AccountingPolicyVersion,
                RuleId,
                RuleVersion,
                SourceEventId,
                SourceJournalEntryId,
                PostingKind,
                AdjustmentApproval);
    }
}
