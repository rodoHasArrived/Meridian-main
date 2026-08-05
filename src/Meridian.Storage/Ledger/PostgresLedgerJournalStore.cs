using System.Data;
using System.Text.Json;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Tenancy;
using Meridian.Ledger;
using Npgsql;

namespace Meridian.Storage.Ledger;

public sealed partial class PostgresLedgerJournalStore :
    ITransactionalLedgerJournalStore,
    IAtomicTaxLotJournalStore,
    ILedgerPostingIdentityCollisionLookup
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private readonly LedgerJournalStoreOptions _options;

    // SEC-005 slice 4c-ii: ambient caller-tenant resolver. Optional so existing construction sites
    // (tests, in-memory wiring) keep compiling and stay fail-open; the workstation host injects an
    // IHttpContextAccessor-backed implementation. When it resolves null (no tenant in scope — a
    // background/worker caller, or the single-company runtime) reads are not tenant-scoped.
    private readonly IFundScopeTenantAccessor? _tenantAccessor;

    public PostgresLedgerJournalStore(
        LedgerJournalStoreOptions options,
        IFundScopeTenantAccessor? tenantAccessor = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _tenantAccessor = tenantAccessor;
    }

    // SEC-005 slice 4c-ii: the caller's tenant for the current ambient scope, or null (fail-open).
    private string? ResolveCallerTenant() => _tenantAccessor?.ResolveCallerTenant();

    public async Task AppendAsync(LedgerJournalEntryWrite entry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(entry.Entry);
        entry = AccountingPostingCommandValidator.NormalizeAndValidate(
            entry,
            _options.RequireGovernedPostingCommand,
            _options.RequireExpectedVersion);

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
        entry = AccountingPostingCommandValidator.NormalizeAndValidate(
            entry,
            _options.RequireGovernedPostingCommand,
            _options.RequireExpectedVersion);

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
                ct: ct)
            .ConfigureAwait(false);
        if (period is null)
        {
            throw new LedgerValidationException($"Ledger period '{entry.PeriodId}' was not found.");
        }

        if (entry.PostingCommand?.ExpectedVersion is { } expectedVersion && expectedVersion != period.Version)
        {
            throw new LedgerValidationException(
                $"Accounting period version {period.Version} does not match expected version {expectedVersion}.");
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
            && !query.SourceEventId.HasValue
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
            Qualified("accounting_periods"),
            query);

        if (query.SourceEventId.HasValue)
        {
            command.Parameters.AddWithValue("source_event_id", query.SourceEventId.Value);
        }

        // SEC-005 slice 4c-ii: scope the entry filter to the caller's tenant via the period's stamped
        // tenant_id (p_filter aliases accounting_periods inside the filter subquery). Fail-open when the
        // caller is tenantless or the period is unbound.
        ApplyTenantReadFilter(command, "p_filter.tenant_id", ResolveCallerTenant());

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
        command.CommandText += " where je.period_id = @period_id";
        command.Parameters.AddWithValue("period_id", periodId);
        // SEC-005 slice 4c-ii: scope by the tenant stamped on the entry's period (the main read has no
        // period join). Fail-open for a tenantless caller or an unbound period.
        ApplyTenantPeriodReadFilter(command, "je.period_id", ResolveCallerTenant());
        command.CommandText += " order by je.occurred_at, je.global_sequence, jl.line_no;";

        return await ReadJournalEntriesAsync(command, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<LedgerJournalEntryRecord>> GetByAggregateAsync(Guid aggregateId, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = CreateJournalEntryReadCommand(connection);
        command.CommandText += " where je.aggregate_id = @aggregate_id";
        command.Parameters.AddWithValue("aggregate_id", aggregateId);
        // SEC-005 slice 4c-ii: scope by the tenant stamped on the entry's period; fail-open otherwise.
        ApplyTenantPeriodReadFilter(command, "je.period_id", ResolveCallerTenant());
        command.CommandText += " order by je.occurred_at, je.global_sequence, jl.line_no;";

        return await ReadJournalEntriesAsync(command, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<LedgerJournalEntryRecord>> FindPostingIdentityCollisionsAsync(
        LedgerPostingIdentity identity,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(identity);
        if (identity.JournalEntryId == Guid.Empty)
            throw new ArgumentException("Journal entry id is required.", nameof(identity));
        if (identity.AggregateId == Guid.Empty)
            throw new ArgumentException("Aggregate id is required.", nameof(identity));

        // Deliberately do not apply the ambient tenant read filter here. Journal-entry and command
        // ids are database-global write identities, so a cross-tenant collision must fail closed
        // rather than reaching the unique constraint as an unexplained append failure.
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = CreateJournalEntryReadCommand(connection);
        var predicates = new List<string>
        {
            "je.journal_entry_id = @identity_journal_entry_id"
        };
        command.Parameters.AddWithValue("identity_journal_entry_id", identity.JournalEntryId);

        if (identity.CommandId.HasValue)
        {
            predicates.Add("je.command_id = @identity_command_id");
            command.Parameters.AddWithValue("identity_command_id", identity.CommandId.Value);
        }

        var aggregatePredicates = new List<string>();
        if (identity.SourceEventId.HasValue)
        {
            aggregatePredicates.Add("je.source_event_id = @identity_source_event_id");
            command.Parameters.AddWithValue("identity_source_event_id", identity.SourceEventId.Value);
        }

        if (!string.IsNullOrWhiteSpace(identity.IdempotencyKey))
        {
            aggregatePredicates.Add(
                "lower(btrim(je.metadata ->> 'idempotencyKey')) = lower(@identity_idempotency_key)");
            command.Parameters.AddWithValue("identity_idempotency_key", identity.IdempotencyKey.Trim());
        }

        if (aggregatePredicates.Count > 0)
        {
            predicates.Add(
                $"(je.aggregate_id = @identity_aggregate_id and ({string.Join(" or ", aggregatePredicates)}))");
            command.Parameters.AddWithValue("identity_aggregate_id", identity.AggregateId);
        }

        command.CommandText += $" where ({string.Join(" or ", predicates)})";
        command.CommandText += " order by je.global_sequence, jl.line_no;";
        return await ReadJournalEntriesAsync(command, ct).ConfigureAwait(false);
    }

    public async Task<LedgerAccountingPeriod?> GetPeriodAsync(Guid periodId, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        return await LoadPeriodAsync(connection, transaction: null, periodId, forUpdate: false, ResolveCallerTenant(), ct).ConfigureAwait(false);
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

        // SEC-005 slice 4c-ii: scope by the period's stamped tenant_id (closes the ledgerBookId /
        // fundStructureNodeId alternate-identifier residual when fundProfileId is omitted). Filter
        // p.tenant_id directly so a legacy book-less period is still scoped by its own stamp.
        ApplyTenantReadFilter(command, "p.tenant_id", ResolveCallerTenant());

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
        => await SavePeriodCoreAsync(
                period,
                expectedVersion,
                closeEvent,
                requireClosedTemporaryAccounts: false,
                ct)
            .ConfigureAwait(false);

    public async Task<LedgerAccountingPeriod> SaveHardClosedPeriodAsync(
        LedgerAccountingPeriod period,
        long expectedVersion,
        PeriodCloseEventRecord closeEvent,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(period);
        ArgumentNullException.ThrowIfNull(closeEvent);
        if (!string.Equals(period.Status, "HardClosed", StringComparison.Ordinal))
        {
            throw new ArgumentException("Atomic hard-close persistence requires a HardClosed target period.", nameof(period));
        }

        if (!_options.EnablePeriodLocking)
        {
            throw new LedgerValidationException(
                "Atomic hard-close persistence requires ledger period row locking to be enabled.");
        }

        return await SavePeriodCoreAsync(
                period,
                expectedVersion,
                closeEvent,
                requireClosedTemporaryAccounts: true,
                ct)
            .ConfigureAwait(false);
    }

    private async Task<LedgerAccountingPeriod> SavePeriodCoreAsync(
        LedgerAccountingPeriod period,
        long expectedVersion,
        PeriodCloseEventRecord? closeEvent,
        bool requireClosedTemporaryAccounts,
        CancellationToken ct)
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
        // Hard-close relies on the shared period-row lock for exclusion. ReadCommitted gives the
        // balance recheck a fresh snapshot after any earlier append that held the row lock commits;
        // using one transaction-wide snapshot here could otherwise hide that just-completed append.
        var isolationLevel = requireClosedTemporaryAccounts
            ? IsolationLevel.ReadCommitted
            : IsolationLevel.Serializable;
        await using var transaction = await connection.BeginTransactionAsync(isolationLevel, ct).ConfigureAwait(false);

        var current = await LoadPeriodAsync(
                connection,
                transaction,
                period.PeriodId,
                forUpdate: requireClosedTemporaryAccounts || _options.EnablePeriodLocking,
                ct: ct)
            .ConfigureAwait(false);

        LedgerAccountingPeriod saved;
        if (current is null)
        {
            if (requireClosedTemporaryAccounts)
            {
                throw new LedgerBookNotFoundException(
                    $"Ledger period '{period.PeriodId}' was not found for atomic hard-close.");
            }

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

            if (requireClosedTemporaryAccounts)
            {
                await EnsureTemporaryAccountsClosedAsync(
                        connection,
                        transaction,
                        current,
                        ct)
                    .ConfigureAwait(false);
            }

            saved = period with { Version = expectedVersion + 1 };
            var affected = await UpdatePeriodAsync(connection, transaction, saved, expectedVersion, ct).ConfigureAwait(false);
            if (affected != 1)
            {
                var actual = await LoadPeriodAsync(connection, transaction, period.PeriodId, forUpdate: false, ct: ct).ConfigureAwait(false);
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
            where ledger_book_id = @ledger_book_id
            """;
        command.Parameters.AddWithValue("ledger_book_id", ledgerBookId);
        // SEC-005 slice 4c-ii: scope by the book's stamped tenant_id (closes the ledgerBookId
        // alternate-identifier residual). Fail-open for a tenantless caller or an unbound book.
        ApplyTenantReadFilter(command, "tenant_id", ResolveCallerTenant());

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

        // SEC-005 slice 4c-ii: scope by the book's stamped tenant_id (closes the fundStructureNodeId
        // alternate-identifier residual when fundProfileId is omitted). Fail-open otherwise.
        ApplyTenantReadFilter(command, "tenant_id", ResolveCallerTenant());

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
                updated_at,
                wash_sale_enabled,
                wash_sale_window_days,
                wash_sale_scope,
                wash_sale_effective_date)
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
                @updated_at,
                @wash_sale_enabled,
                @wash_sale_window_days,
                @wash_sale_scope,
                @wash_sale_effective_date)
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
                updated_at = excluded.updated_at,
                wash_sale_enabled = excluded.wash_sale_enabled,
                wash_sale_window_days = excluded.wash_sale_window_days,
                wash_sale_scope = excluded.wash_sale_scope,
                wash_sale_effective_date = excluded.wash_sale_effective_date
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
                      updated_at,
                      wash_sale_enabled,
                      wash_sale_window_days,
                      wash_sale_scope,
                      wash_sale_effective_date;
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
        AddWashSalePolicyParameters(command, policy.EffectiveWashSalePolicy);

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
                   updated_at,
                   wash_sale_enabled,
                   wash_sale_window_days,
                   wash_sale_scope,
                   wash_sale_effective_date
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
            insert into {Qualified("tax_lots")} as retained (
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
                version,
                originating_mutation_batch_id,
                last_mutation_batch_id,
                created_at,
                updated_at,
                security_id,
                book_position_id)
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
                @version,
                null,
                null,
                @created_at,
                @updated_at,
                @security_id,
                @book_position_id)
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
                security_id = excluded.security_id,
                book_position_id = excluded.book_position_id,
                version = retained.version + 1,
                updated_at = excluded.updated_at
            where retained.originating_mutation_batch_id is null
              and @expected_version > 0
              and retained.version = @expected_version
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
                      version,
                      originating_mutation_batch_id,
                      last_mutation_batch_id,
                      created_at,
                      updated_at,
                      security_id,
                      book_position_id;
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
        command.Parameters.AddWithValue("version", lot.Version <= 0 ? 1 : lot.Version);
        command.Parameters.AddWithValue("expected_version", Math.Max(0, lot.Version));
        command.Parameters.AddWithValue("created_at", lot.CreatedAt.UtcDateTime);
        command.Parameters.AddWithValue("updated_at", lot.UpdatedAt.UtcDateTime);
        command.Parameters.AddWithValue(
            "security_id",
            lot.SecurityId == Guid.Empty ? DBNull.Value : lot.SecurityId);
        command.Parameters.AddWithValue(
            "book_position_id",
            lot.BookPositionId == Guid.Empty ? DBNull.Value : lot.BookPositionId);

        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            throw new InvalidOperationException(
                $"Ledger tax lot '{lot.TaxLotRecordId}' was not saved because its version was stale or it is managed by an atomic posting batch.");
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
                   version,
                   originating_mutation_batch_id,
                   last_mutation_batch_id,
                   created_at,
                   updated_at,
                   security_id,
                   book_position_id
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

    public async Task<IReadOnlyList<LedgerTaxLotRecord>> GetTaxLotsByIdsAsync(
        Guid ledgerBookId,
        IReadOnlyList<Guid> taxLotRecordIds,
        CancellationToken ct = default)
    {
        if (ledgerBookId == Guid.Empty)
        {
            throw new ArgumentException("Ledger book id is required.", nameof(ledgerBookId));
        }

        ArgumentNullException.ThrowIfNull(taxLotRecordIds);
        var ids = taxLotRecordIds.Distinct().ToArray();
        if (ids.Length == 0 || ids.Any(static id => id == Guid.Empty))
        {
            throw new ArgumentException(
                "At least one distinct non-empty tax-lot record id is required.",
                nameof(taxLotRecordIds));
        }

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
                   version,
                   originating_mutation_batch_id,
                   last_mutation_batch_id,
                   created_at,
                   updated_at,
                   security_id,
                   book_position_id
            from {Qualified("tax_lots")}
            where ledger_book_id = @ledger_book_id
              and tax_lot_record_id = any(@tax_lot_record_ids)
            order by tax_lot_record_id;
            """;
        command.Parameters.AddWithValue("ledger_book_id", ledgerBookId);
        command.Parameters.AddWithValue("tax_lot_record_ids", ids);

        var lots = new List<LedgerTaxLotRecord>(ids.Length);
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

        ValidateAuthoritativeBookContext(entry, book);
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
                    dimensions,
                    transaction_currency,
                    functional_currency,
                    transaction_debit,
                    transaction_credit,
                    fx_rate_to_functional)
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
                    cast(@dimensions as jsonb),
                    @transaction_currency,
                    @functional_currency,
                    @transaction_debit,
                    @transaction_credit,
                    @fx_rate_to_functional);
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
            command.Parameters.AddWithValue("transaction_currency", (object?)leg.Currency?.TransactionCurrency ?? DBNull.Value);
            command.Parameters.AddWithValue("functional_currency", (object?)leg.Currency?.FunctionalCurrency ?? DBNull.Value);
            command.Parameters.AddWithValue("transaction_debit", (object?)leg.Currency?.TransactionDebit ?? DBNull.Value);
            command.Parameters.AddWithValue("transaction_credit", (object?)leg.Currency?.TransactionCredit ?? DBNull.Value);
            command.Parameters.AddWithValue("fx_rate_to_functional", (object?)leg.Currency?.FxRateToFunctional ?? DBNull.Value);
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
                   jl.dimensions::text,
                   jl.transaction_currency,
                   jl.functional_currency,
                   jl.transaction_debit,
                   jl.transaction_credit,
                   jl.fx_rate_to_functional
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
                reader.IsDBNull(28) ? null : DeserializeLineDimensions(reader.GetString(28)),
                ReadLegCurrency(reader, 29, 30, 31, 32, 33)));
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
        string? callerTenantId = null,
        CancellationToken ct = default)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        // SEC-005 slice 4c-ii: the tenant filter (when any) must precede the FOR UPDATE clause, which
        // Postgres requires last. Internal write callers pass callerTenantId = null (no filter); only
        // the GetPeriodAsync read path scopes by the period's stamped tenant_id, fail-open otherwise.
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
            """;
        command.Parameters.AddWithValue("period_id", periodId);
        ApplyTenantReadFilter(command, "tenant_id", callerTenantId);
        command.CommandText += $" {ForUpdateClause(forUpdate)};";

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

    private async Task EnsureTemporaryAccountsClosedAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        LedgerAccountingPeriod period,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            select jl.account_name,
                   case
                       when jl.account_type = 'Revenue' then sum(jl.credit) - sum(jl.debit)
                       else sum(jl.debit) - sum(jl.credit)
                   end as balance
            from {Qualified("journal_entries")} je
            join {Qualified("journal_legs")} jl on jl.journal_entry_id = je.journal_entry_id
            where je.period_id = @period_id
              and jl.account_type in ('Revenue', 'Expense')
            group by jl.account_name,
                     jl.account_type,
                     jl.symbol,
                     jl.financial_account_id,
                     jl.dimensions
            having sum(jl.debit) <> sum(jl.credit)
            order by jl.account_name,
                     jl.financial_account_id,
                     jl.dimensions::text;
            """;
        command.Parameters.AddWithValue("period_id", period.PeriodId);

        var residuals = new List<(string AccountName, decimal Balance)>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            residuals.Add((reader.GetString(0), reader.GetDecimal(1)));
        }

        if (residuals.Count == 0)
        {
            return;
        }

        var preview = string.Join(
            "; ",
            residuals.Take(5).Select(static row =>
                FormattableString.Invariant($"{row.AccountName}={row.Balance}")));
        throw new LedgerBookValidationException(
            $"Accounting period '{period.Label}' cannot be hard-closed while {residuals.Count} revenue/expense balance(s) remain non-zero ({preview}). Post and approve the closing-entry draft before period lock.");
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

        if (lot.Version < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(lot), lot.Version, "Tax-lot version cannot be negative.");
        }

        if ((lot.SecurityId == Guid.Empty) != (lot.BookPositionId == Guid.Empty))
        {
            throw new LedgerValidationException(
                "Tax-lot Security Master and book-position identities must either both be supplied or both be absent.");
        }

        if (lot.OriginatingMutationBatchId.HasValue || lot.LastMutationBatchId.HasValue)
        {
            throw new LedgerValidationException(
                "Atomic tax-lot mutation lineage can only be written through IAtomicTaxLotJournalStore.");
        }
    }

    private static void AddAccountParameters(NpgsqlCommand command, LedgerAccount account)
    {
        command.Parameters.AddWithValue("account_name", RequireLineageText(account.Name, nameof(account.Name)));
        command.Parameters.AddWithValue("account_type", account.AccountType.ToString());
        command.Parameters.AddWithValue("symbol", (object?)NormalizeOptional(account.Symbol) ?? DBNull.Value);
        command.Parameters.AddWithValue("financial_account_id", (object?)NormalizeOptional(account.FinancialAccountId) ?? DBNull.Value);
    }

    private static void AddWashSalePolicyParameters(NpgsqlCommand command, WashSalePolicy policy)
    {
        policy.EnsureValid();
        command.Parameters.AddWithValue("wash_sale_enabled", policy.Enabled);
        command.Parameters.AddWithValue("wash_sale_window_days", policy.WindowDays);
        command.Parameters.AddWithValue("wash_sale_scope", policy.Scope.ToString());
        command.Parameters.AddWithValue(
            "wash_sale_effective_date",
            policy.EffectiveDate is { } effectiveDate ? effectiveDate : (object)DBNull.Value);
    }

    private static LedgerAccount ReadLedgerAccount(NpgsqlDataReader reader, int nameOrdinal)
        => new(
            reader.GetString(nameOrdinal),
            Enum.Parse<LedgerAccountType>(reader.GetString(nameOrdinal + 1), ignoreCase: true),
            reader.IsDBNull(nameOrdinal + 2) ? null : reader.GetString(nameOrdinal + 2),
            reader.IsDBNull(nameOrdinal + 3) ? null : reader.GetString(nameOrdinal + 3));

    // Wash-sale columns are appended after the original twelve so every existing ordinal keeps its
    // meaning; callers that do not select them pass a reader with only 12 fields, which
    // TryReadWashSalePolicy detects and treats as an unconfigured (disabled) policy.
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
            reader.IsDBNull(9) ? null : reader.GetString(9),
            TryReadWashSalePolicy(reader));

    private static WashSalePolicy? TryReadWashSalePolicy(NpgsqlDataReader reader)
    {
        const int enabledOrdinal = 12;
        if (reader.FieldCount <= enabledOrdinal || reader.IsDBNull(enabledOrdinal))
        {
            return null;
        }

        return new WashSalePolicy(
            reader.GetBoolean(enabledOrdinal),
            reader.GetInt32(enabledOrdinal + 1),
            Enum.Parse<WashSaleReplacementScope>(reader.GetString(enabledOrdinal + 2), ignoreCase: true),
            reader.IsDBNull(enabledOrdinal + 3)
                ? null
                : DateOnly.FromDateTime(reader.GetDateTime(enabledOrdinal + 3)))
            .EnsureValid();
    }

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
            ReadUtcDateTimeOffset(reader, 17),
            ReadUtcDateTimeOffset(reader, 18),
            reader.IsDBNull(12) ? null : reader.GetGuid(12),
            reader.IsDBNull(13) ? null : reader.GetString(13),
            reader.GetInt64(14),
            reader.IsDBNull(15) ? null : reader.GetGuid(15),
            reader.IsDBNull(16) ? null : reader.GetGuid(16),
            reader.IsDBNull(19) ? Guid.Empty : reader.GetGuid(19),
            reader.IsDBNull(20) ? Guid.Empty : reader.GetGuid(20));

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

    // SEC-005 slice 4c-ii: append the fail-open tenant predicate (and bind its parameter) to a read
    // command, but only when the caller has a resolved tenant. A tenantless caller adds nothing, so
    // every row passes — identical behavior under one-company-per-deployment. The column expression is
    // table-qualified by the caller (e.g. "tenant_id" or "p.tenant_id").
    private static void ApplyTenantReadFilter(NpgsqlCommand command, string tenantColumnExpression, string? callerTenantId)
    {
        if (!TenantReadPredicate.ShouldFilter(callerTenantId))
        {
            return;
        }

        command.CommandText += TenantReadPredicate.FilterClause(tenantColumnExpression);
        command.Parameters.AddWithValue(
            TenantReadPredicate.ParameterName,
            TenantReadPredicate.NormalizeParameter(callerTenantId!));
    }

    // SEC-005 slice 4c-ii: scope a journal-entry read (which has no period join in its main query) by
    // the tenant stamped on the entry's accounting period, via an EXISTS subquery. Fail-open otherwise.
    private void ApplyTenantPeriodReadFilter(NpgsqlCommand command, string periodIdColumnExpression, string? callerTenantId)
    {
        if (!TenantReadPredicate.ShouldFilter(callerTenantId))
        {
            return;
        }

        command.CommandText += TenantReadPredicate.PeriodExistsClause(Qualified("accounting_periods"), periodIdColumnExpression);
        command.Parameters.AddWithValue(
            TenantReadPredicate.ParameterName,
            TenantReadPredicate.NormalizeParameter(callerTenantId!));
    }

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

}
