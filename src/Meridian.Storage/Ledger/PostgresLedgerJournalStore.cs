using System.Data;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Ledger;
using Npgsql;

namespace Meridian.Storage.Ledger;

public sealed class PostgresLedgerJournalStore : ILedgerJournalStore
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

        await ValidateJournalBasisAsync(connection, transaction, entry, ct).ConfigureAwait(false);
        await InsertJournalEntryAsync(connection, transaction, entry, ct).ConfigureAwait(false);
        await InsertJournalLegsAsync(connection, transaction, entry, ct).ConfigureAwait(false);

        await transaction.CommitAsync(ct).ConfigureAwait(false);
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
                updated_at)
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
                @updated_at)
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
                updated_at = excluded.updated_at
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
        command.Parameters.AddWithValue("occurred_at", entry.Entry.Timestamp.UtcDateTime);
        command.Parameters.AddWithValue("description", entry.Entry.Description);
        command.Parameters.AddWithValue("metadata", JsonSerializer.Serialize(entry.Entry.Metadata.Normalize(), JsonOptions));
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private async Task ValidateJournalBasisAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        LedgerJournalEntryWrite entry,
        CancellationToken ct)
    {
        _ = RequireLineageText(entry.AccountingPolicyId, nameof(entry.AccountingPolicyId));
        _ = RequireLineageText(entry.AccountingPolicyVersion, nameof(entry.AccountingPolicyVersion));

        var period = await LoadPeriodAsync(
                connection,
                transaction,
                entry.PeriodId,
                forUpdate: false,
                ct)
            .ConfigureAwait(false);
        if (period is null)
        {
            throw new LedgerValidationException($"Ledger period '{entry.PeriodId}' was not found.");
        }

        if (period.LedgerBookId is not { } ledgerBookId)
        {
            if (entry.AccountingBasis != AccountingBasisKindDto.Primary)
            {
                throw new LedgerValidationException(
                    $"Legacy period '{entry.PeriodId}' accepts only Primary basis postings.");
            }

            return;
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
                    occurred_at,
                    account_name,
                    account_type,
                    symbol,
                    financial_account_id,
                    debit,
                    credit,
                    description)
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
                    @occurred_at,
                    @account_name,
                    @account_type,
                    @symbol,
                    @financial_account_id,
                    @debit,
                    @credit,
                    @description);
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
            command.Parameters.AddWithValue("occurred_at", leg.Timestamp.UtcDateTime);
            command.Parameters.AddWithValue("account_name", leg.Account.Name);
            command.Parameters.AddWithValue("account_type", leg.Account.AccountType.ToString());
            command.Parameters.AddWithValue("symbol", (object?)leg.Account.Symbol ?? DBNull.Value);
            command.Parameters.AddWithValue("financial_account_id", (object?)leg.Account.FinancialAccountId ?? DBNull.Value);
            command.Parameters.AddWithValue("debit", leg.Debit);
            command.Parameters.AddWithValue("credit", leg.Credit);
            command.Parameters.AddWithValue("description", leg.Description);
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
                   jl.occurred_at
            from {Qualified("journal_entries")} je
            join {Qualified("journal_legs")} jl on jl.journal_entry_id = je.journal_entry_id
            """;
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
                    Timestamp: ReadUtcDateTimeOffset(reader, 13),
                    Description: reader.GetString(14),
                    Metadata: DeserializeMetadata(reader.GetString(15)),
                    CreatedAt: ReadUtcDateTimeOffset(reader, 16));
            }

            var accountType = Enum.Parse<LedgerAccountType>(reader.GetString(19), ignoreCase: true);
            var account = new LedgerAccount(
                reader.GetString(18),
                accountType,
                reader.IsDBNull(20) ? null : reader.GetString(20),
                reader.IsDBNull(21) ? null : reader.GetString(21));
            current.Lines.Add(new LedgerEntry(
                reader.GetGuid(17),
                journalEntryId,
                ReadUtcDateTimeOffset(reader, 25),
                account,
                reader.GetDecimal(22),
                reader.GetDecimal(23),
                reader.GetString(24)));
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
                updated_at)
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
                now());
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
                updated_at = now()
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
                SourceJournalEntryId);
    }
}
