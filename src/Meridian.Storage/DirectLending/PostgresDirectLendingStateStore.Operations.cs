using Meridian.Contracts.DirectLending;
using Npgsql;

namespace Meridian.Storage.DirectLending;

public sealed partial class PostgresDirectLendingStateStore
{
    public async Task<IReadOnlyList<CashTransactionDto>> GetCashTransactionsAsync(Guid loanId, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select cash_txn_id,
                   loan_id,
                   transaction_type,
                   effective_date,
                   transaction_date,
                   settlement_date,
                   amount,
                   currency,
                   external_ref,
                   source_event_id,
                   recorded_at,
                   voided_at
            from {Qualified("cash_transaction")}
            where loan_id = @loan_id
            order by settlement_date desc, recorded_at desc;
            """;
        command.Parameters.AddWithValue("loan_id", loanId);

        var results = new List<CashTransactionDto>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            results.Add(new CashTransactionDto(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetString(2),
                DateOnly.FromDateTime(reader.GetDateTime(3)),
                DateOnly.FromDateTime(reader.GetDateTime(4)),
                DateOnly.FromDateTime(reader.GetDateTime(5)),
                reader.GetDecimal(6),
                ParseEnum<CurrencyCode>(reader.GetString(7)),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                reader.GetGuid(9),
                new DateTimeOffset(reader.GetDateTime(10), TimeSpan.Zero),
                !reader.IsDBNull(11)));
        }

        return results;
    }

    public async Task<IReadOnlyList<PaymentAllocationDto>> GetPaymentAllocationsAsync(Guid loanId, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select allocation_id,
                   loan_id,
                   cash_transaction_id,
                   allocation_seq_no,
                   target_type,
                   target_id,
                   allocated_amount,
                   allocation_rule,
                   source_event_id,
                   created_at
            from {Qualified("payment_allocation")}
            where loan_id = @loan_id
            order by created_at, allocation_seq_no;
            """;
        command.Parameters.AddWithValue("loan_id", loanId);

        var results = new List<PaymentAllocationDto>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            results.Add(new PaymentAllocationDto(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetGuid(2),
                reader.GetInt32(3),
                reader.GetString(4),
                reader.GetGuid(5).ToString("D"),
                reader.GetDecimal(6),
                reader.GetString(7),
                reader.GetGuid(8),
                new DateTimeOffset(reader.GetDateTime(9), TimeSpan.Zero)));
        }

        return results;
    }

    public async Task<IReadOnlyList<FeeBalanceDto>> GetFeeBalancesAsync(Guid loanId, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select fee_balance_id,
                   loan_id,
                   fee_type,
                   effective_date,
                   original_amount,
                   unpaid_amount,
                   source_event_id,
                   note,
                   created_at
            from {Qualified("fee_balance")}
            where loan_id = @loan_id
            order by effective_date desc, created_at desc;
            """;
        command.Parameters.AddWithValue("loan_id", loanId);

        var results = new List<FeeBalanceDto>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            results.Add(new FeeBalanceDto(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetString(2),
                DateOnly.FromDateTime(reader.GetDateTime(3)),
                reader.GetDecimal(4),
                reader.GetDecimal(5),
                reader.GetGuid(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                new DateTimeOffset(reader.GetDateTime(8), TimeSpan.Zero)));
        }

        return results;
    }

    public async Task<ProjectionRunDto> SaveProjectionRunAsync(ProjectionRunDto run, IReadOnlyList<ProjectedCashFlowDto> flows, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

        if (run.SupersedesProjectionRunId is Guid supersededId)
        {
            await using var supersede = connection.CreateCommand();
            supersede.Transaction = transaction;
            supersede.CommandText = $"update {Qualified("projection_run")} set status = @status where projection_run_id = @projection_run_id;";
            supersede.Parameters.AddWithValue("status", ProjectionRunStatus.Superseded.ToString());
            supersede.Parameters.AddWithValue("projection_run_id", supersededId);
            await supersede.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText =
                $"""
                insert into {Qualified("projection_run")} (
                    projection_run_id,
                    loan_id,
                    loan_terms_version,
                    servicing_revision,
                    projection_as_of,
                    market_data_as_of,
                    trigger_event_id,
                    trigger_type,
                    terms_hash,
                    engine_version,
                    status,
                    supersedes_projection_run_id,
                    generated_at)
                values (
                    @projection_run_id,
                    @loan_id,
                    @loan_terms_version,
                    @servicing_revision,
                    @projection_as_of,
                    @market_data_as_of,
                    @trigger_event_id,
                    @trigger_type,
                    @terms_hash,
                    @engine_version,
                    @status,
                    @supersedes_projection_run_id,
                    @generated_at)
                on conflict (projection_run_id) do update
                set status = excluded.status,
                    supersedes_projection_run_id = excluded.supersedes_projection_run_id,
                    generated_at = excluded.generated_at;
                """;
            insert.Parameters.AddWithValue("projection_run_id", run.ProjectionRunId);
            insert.Parameters.AddWithValue("loan_id", run.LoanId);
            insert.Parameters.AddWithValue("loan_terms_version", run.LoanTermsVersion);
            insert.Parameters.AddWithValue("servicing_revision", run.ServicingRevision);
            insert.Parameters.AddWithValue("projection_as_of", run.ProjectionAsOf);
            insert.Parameters.AddWithValue("market_data_as_of", (object?)run.MarketDataAsOf ?? DBNull.Value);
            insert.Parameters.AddWithValue("trigger_event_id", (object?)run.TriggerEventId ?? DBNull.Value);
            insert.Parameters.AddWithValue("trigger_type", run.TriggerType);
            insert.Parameters.AddWithValue("terms_hash", run.TermsHash);
            insert.Parameters.AddWithValue("engine_version", run.EngineVersion);
            insert.Parameters.AddWithValue("status", run.Status.ToString());
            insert.Parameters.AddWithValue("supersedes_projection_run_id", (object?)run.SupersedesProjectionRunId ?? DBNull.Value);
            insert.Parameters.AddWithValue("generated_at", run.GeneratedAt.UtcDateTime);
            await insert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = $"delete from {Qualified("projected_cash_flow")} where projection_run_id = @projection_run_id;";
            delete.Parameters.AddWithValue("projection_run_id", run.ProjectionRunId);
            await delete.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        foreach (var flow in flows)
        {
            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText =
                $"""
                insert into {Qualified("projected_cash_flow")} (
                    projected_cash_flow_id,
                    projection_run_id,
                    loan_id,
                    flow_seq_no,
                    flow_type,
                    due_date,
                    accrual_start_date,
                    accrual_end_date,
                    amount,
                    currency,
                    principal_basis,
                    annual_rate,
                    formula_trace_json,
                    created_at)
                values (
                    @projected_cash_flow_id,
                    @projection_run_id,
                    @loan_id,
                    @flow_seq_no,
                    @flow_type,
                    @due_date,
                    @accrual_start_date,
                    @accrual_end_date,
                    @amount,
                    @currency,
                    @principal_basis,
                    @annual_rate,
                    cast(@formula_trace_json as jsonb),
                    now());
                """;
            insert.Parameters.AddWithValue("projected_cash_flow_id", flow.ProjectedCashFlowId);
            insert.Parameters.AddWithValue("projection_run_id", flow.ProjectionRunId);
            insert.Parameters.AddWithValue("loan_id", flow.LoanId);
            insert.Parameters.AddWithValue("flow_seq_no", flow.FlowSequenceNumber);
            insert.Parameters.AddWithValue("flow_type", flow.FlowType);
            insert.Parameters.AddWithValue("due_date", flow.DueDate);
            insert.Parameters.AddWithValue("accrual_start_date", (object?)flow.AccrualStartDate ?? DBNull.Value);
            insert.Parameters.AddWithValue("accrual_end_date", (object?)flow.AccrualEndDate ?? DBNull.Value);
            insert.Parameters.AddWithValue("amount", flow.Amount);
            insert.Parameters.AddWithValue("currency", flow.Currency.ToString());
            insert.Parameters.AddWithValue("principal_basis", (object?)flow.PrincipalBasis ?? DBNull.Value);
            insert.Parameters.AddWithValue("annual_rate", (object?)flow.AnnualRate ?? DBNull.Value);
            insert.Parameters.AddWithValue("formula_trace_json", flow.FormulaTraceJson ?? "null");
            await insert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return run;
    }

    public async Task<IReadOnlyList<ProjectionRunDto>> GetProjectionRunsAsync(Guid loanId, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select projection_run_id,
                   loan_id,
                   loan_terms_version,
                   servicing_revision,
                   projection_as_of,
                   market_data_as_of,
                   trigger_event_id,
                   trigger_type,
                   terms_hash,
                   engine_version,
                   status,
                   supersedes_projection_run_id,
                   generated_at
            from {Qualified("projection_run")}
            where loan_id = @loan_id
            order by generated_at desc;
            """;
        command.Parameters.AddWithValue("loan_id", loanId);

        return await ReadProjectionRunsAsync(command, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ProjectedCashFlowDto>> GetProjectedCashFlowsAsync(Guid projectionRunId, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select projected_cash_flow_id,
                   projection_run_id,
                   loan_id,
                   flow_seq_no,
                   flow_type,
                   due_date,
                   accrual_start_date,
                   accrual_end_date,
                   amount,
                   currency,
                   principal_basis,
                   annual_rate,
                   formula_trace_json::text,
                   created_at
            from {Qualified("projected_cash_flow")}
            where projection_run_id = @projection_run_id
            order by flow_seq_no;
            """;
        command.Parameters.AddWithValue("projection_run_id", projectionRunId);

        var results = new List<ProjectedCashFlowDto>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            results.Add(new ProjectedCashFlowDto(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetGuid(2),
                reader.GetInt32(3),
                reader.GetString(4),
                DateOnly.FromDateTime(reader.GetDateTime(5)),
                reader.IsDBNull(6) ? null : DateOnly.FromDateTime(reader.GetDateTime(6)),
                reader.IsDBNull(7) ? null : DateOnly.FromDateTime(reader.GetDateTime(7)),
                reader.GetDecimal(8),
                ParseEnum<CurrencyCode>(reader.GetString(9)),
                reader.IsDBNull(10) ? null : reader.GetDecimal(10),
                reader.IsDBNull(11) ? null : reader.GetDecimal(11),
                NormalizeJsonText(reader.IsDBNull(12) ? null : reader.GetString(12)),
                new DateTimeOffset(reader.GetDateTime(13), TimeSpan.Zero)));
        }

        return results;
    }

    public async Task<JournalEntryDto> SaveJournalEntryAsync(JournalEntryDto entry, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText =
                $"""
                insert into {Qualified("journal_entry")} (
                    journal_entry_id,
                    loan_id,
                    accounting_date,
                    effective_date,
                    source_event_id,
                    entry_type,
                    ledger_basis,
                    description,
                    recorded_at,
                    posted_at,
                    status)
                values (
                    @journal_entry_id,
                    @loan_id,
                    @accounting_date,
                    @effective_date,
                    @source_event_id,
                    @entry_type,
                    @ledger_basis,
                    @description,
                    @recorded_at,
                    @posted_at,
                    @status)
                on conflict (journal_entry_id) do update
                set accounting_date = excluded.accounting_date,
                    effective_date = excluded.effective_date,
                    source_event_id = excluded.source_event_id,
                    entry_type = excluded.entry_type,
                    ledger_basis = excluded.ledger_basis,
                    description = excluded.description,
                    recorded_at = excluded.recorded_at,
                    posted_at = excluded.posted_at,
                    status = excluded.status;
                """;
            insert.Parameters.AddWithValue("journal_entry_id", entry.JournalEntryId);
            insert.Parameters.AddWithValue("loan_id", (object?)entry.LoanId ?? DBNull.Value);
            insert.Parameters.AddWithValue("accounting_date", entry.AccountingDate);
            insert.Parameters.AddWithValue("effective_date", entry.EffectiveDate);
            insert.Parameters.AddWithValue("source_event_id", entry.SourceEventId);
            insert.Parameters.AddWithValue("entry_type", entry.EntryType);
            insert.Parameters.AddWithValue("ledger_basis", entry.LedgerBasis);
            insert.Parameters.AddWithValue("description", entry.Description);
            insert.Parameters.AddWithValue("recorded_at", entry.RecordedAt.UtcDateTime);
            insert.Parameters.AddWithValue("posted_at", (object?)entry.PostedAt?.UtcDateTime ?? DBNull.Value);
            insert.Parameters.AddWithValue("status", entry.Status.ToString());
            await insert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = $"delete from {Qualified("journal_line")} where journal_entry_id = @journal_entry_id;";
            delete.Parameters.AddWithValue("journal_entry_id", entry.JournalEntryId);
            await delete.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        foreach (var line in entry.Lines)
        {
            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText =
                $"""
                insert into {Qualified("journal_line")} (
                    journal_line_id,
                    journal_entry_id,
                    line_no,
                    account_code,
                    debit_amount,
                    credit_amount,
                    currency,
                    dimensions_json,
                    created_at)
                values (
                    @journal_line_id,
                    @journal_entry_id,
                    @line_no,
                    @account_code,
                    @debit_amount,
                    @credit_amount,
                    @currency,
                    cast(@dimensions_json as jsonb),
                    now());
                """;
            insert.Parameters.AddWithValue("journal_line_id", line.JournalLineId);
            insert.Parameters.AddWithValue("journal_entry_id", entry.JournalEntryId);
            insert.Parameters.AddWithValue("line_no", line.LineNumber);
            insert.Parameters.AddWithValue("account_code", line.AccountCode);
            insert.Parameters.AddWithValue("debit_amount", line.DebitAmount);
            insert.Parameters.AddWithValue("credit_amount", line.CreditAmount);
            insert.Parameters.AddWithValue("currency", line.Currency.ToString());
            insert.Parameters.AddWithValue("dimensions_json", line.DimensionsJson ?? "null");
            await insert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return entry;
    }

    public async Task<IReadOnlyList<JournalEntryDto>> GetJournalEntriesAsync(Guid loanId, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select journal_entry_id,
                   loan_id,
                   accounting_date,
                   effective_date,
                   source_event_id,
                   entry_type,
                   ledger_basis,
                   description,
                   recorded_at,
                   posted_at,
                   status
            from {Qualified("journal_entry")}
            where loan_id = @loan_id
            order by accounting_date desc, recorded_at desc;
            """;
        command.Parameters.AddWithValue("loan_id", loanId);
        return await LoadJournalEntriesAsync(connection, command, ct).ConfigureAwait(false);
    }

    public async Task<JournalEntryDto?> GetJournalEntryAsync(Guid journalEntryId, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select journal_entry_id,
                   loan_id,
                   accounting_date,
                   effective_date,
                   source_event_id,
                   entry_type,
                   ledger_basis,
                   description,
                   recorded_at,
                   posted_at,
                   status
            from {Qualified("journal_entry")}
            where journal_entry_id = @journal_entry_id;
            """;
        command.Parameters.AddWithValue("journal_entry_id", journalEntryId);
        return (await LoadJournalEntriesAsync(connection, command, ct).ConfigureAwait(false)).FirstOrDefault();
    }

    public async Task<JournalEntryDto?> MarkJournalPostedAsync(Guid journalEntryId, DateTimeOffset postedAt, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var update = connection.CreateCommand();
        update.CommandText =
            $"""
            update {Qualified("journal_entry")}
            set posted_at = @posted_at,
                status = @status
            where journal_entry_id = @journal_entry_id;
            """;
        update.Parameters.AddWithValue("posted_at", postedAt.UtcDateTime);
        update.Parameters.AddWithValue("status", JournalEntryStatus.Posted.ToString());
        update.Parameters.AddWithValue("journal_entry_id", journalEntryId);
        await update.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        return await GetJournalEntryAsync(journalEntryId, ct).ConfigureAwait(false);
    }

    public async Task<ReconciliationRunDto> SaveReconciliationRunAsync(ReconciliationRunDto run, IReadOnlyList<ReconciliationResultDto> results, IReadOnlyList<ReconciliationExceptionDto> exceptions, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

        await using (var deleteExceptions = connection.CreateCommand())
        {
            deleteExceptions.Transaction = transaction;
            deleteExceptions.CommandText =
                $"""
                delete from {Qualified("reconciliation_exception")}
                where reconciliation_result_id in (
                    select reconciliation_result_id
                    from {Qualified("reconciliation_result")}
                    where reconciliation_run_id = @reconciliation_run_id);
                """;
            deleteExceptions.Parameters.AddWithValue("reconciliation_run_id", run.ReconciliationRunId);
            await deleteExceptions.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await using (var deleteResults = connection.CreateCommand())
        {
            deleteResults.Transaction = transaction;
            deleteResults.CommandText = $"delete from {Qualified("reconciliation_result")} where reconciliation_run_id = @reconciliation_run_id;";
            deleteResults.Parameters.AddWithValue("reconciliation_run_id", run.ReconciliationRunId);
            await deleteResults.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText =
                $"""
                insert into {Qualified("reconciliation_run")} (
                    reconciliation_run_id,
                    loan_id,
                    projection_run_id,
                    requested_at,
                    completed_at,
                    status)
                values (
                    @reconciliation_run_id,
                    @loan_id,
                    @projection_run_id,
                    @requested_at,
                    @completed_at,
                    @status)
                on conflict (reconciliation_run_id) do update
                set projection_run_id = excluded.projection_run_id,
                    requested_at = excluded.requested_at,
                    completed_at = excluded.completed_at,
                    status = excluded.status;
                """;
            insert.Parameters.AddWithValue("reconciliation_run_id", run.ReconciliationRunId);
            insert.Parameters.AddWithValue("loan_id", run.LoanId);
            insert.Parameters.AddWithValue("projection_run_id", (object?)run.ProjectionRunId ?? DBNull.Value);
            insert.Parameters.AddWithValue("requested_at", run.RequestedAt.UtcDateTime);
            insert.Parameters.AddWithValue("completed_at", (object?)run.CompletedAt?.UtcDateTime ?? DBNull.Value);
            insert.Parameters.AddWithValue("status", run.Status);
            await insert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        foreach (var result in results)
        {
            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText =
                $"""
                insert into {Qualified("reconciliation_result")} (
                    reconciliation_result_id,
                    reconciliation_run_id,
                    loan_id,
                    projected_cash_flow_id,
                    cash_transaction_id,
                    match_status,
                    expected_amount,
                    actual_amount,
                    variance_amount,
                    expected_date,
                    actual_date,
                    match_rule,
                    tolerance_json,
                    notes,
                    created_at)
                values (
                    @reconciliation_result_id,
                    @reconciliation_run_id,
                    @loan_id,
                    @projected_cash_flow_id,
                    @cash_transaction_id,
                    @match_status,
                    @expected_amount,
                    @actual_amount,
                    @variance_amount,
                    @expected_date,
                    @actual_date,
                    @match_rule,
                    cast(@tolerance_json as jsonb),
                    @notes,
                    now());
                """;
            insert.Parameters.AddWithValue("reconciliation_result_id", result.ReconciliationResultId);
            insert.Parameters.AddWithValue("reconciliation_run_id", result.ReconciliationRunId);
            insert.Parameters.AddWithValue("loan_id", result.LoanId);
            insert.Parameters.AddWithValue("projected_cash_flow_id", (object?)result.ProjectedCashFlowId ?? DBNull.Value);
            insert.Parameters.AddWithValue("cash_transaction_id", (object?)result.CashTransactionId ?? DBNull.Value);
            insert.Parameters.AddWithValue("match_status", result.MatchStatus);
            insert.Parameters.AddWithValue("expected_amount", (object?)result.ExpectedAmount ?? DBNull.Value);
            insert.Parameters.AddWithValue("actual_amount", (object?)result.ActualAmount ?? DBNull.Value);
            insert.Parameters.AddWithValue("variance_amount", (object?)result.VarianceAmount ?? DBNull.Value);
            insert.Parameters.AddWithValue("expected_date", (object?)result.ExpectedDate ?? DBNull.Value);
            insert.Parameters.AddWithValue("actual_date", (object?)result.ActualDate ?? DBNull.Value);
            insert.Parameters.AddWithValue("match_rule", (object?)result.MatchRule ?? DBNull.Value);
            insert.Parameters.AddWithValue("tolerance_json", result.ToleranceJson ?? "null");
            insert.Parameters.AddWithValue("notes", result.Notes.ToArray());
            await insert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        foreach (var exception in exceptions)
        {
            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText =
                $"""
                insert into {Qualified("reconciliation_exception")} (
                    exception_id,
                    reconciliation_result_id,
                    exception_type,
                    severity,
                    status,
                    assigned_to,
                    resolution_note,
                    created_at,
                    resolved_at)
                values (
                    @exception_id,
                    @reconciliation_result_id,
                    @exception_type,
                    @severity,
                    @status,
                    @assigned_to,
                    @resolution_note,
                    @created_at,
                    @resolved_at);
                """;
            insert.Parameters.AddWithValue("exception_id", exception.ExceptionId);
            insert.Parameters.AddWithValue("reconciliation_result_id", exception.ReconciliationResultId);
            insert.Parameters.AddWithValue("exception_type", exception.ExceptionType);
            insert.Parameters.AddWithValue("severity", exception.Severity);
            insert.Parameters.AddWithValue("status", exception.Status);
            insert.Parameters.AddWithValue("assigned_to", (object?)exception.AssignedTo ?? DBNull.Value);
            insert.Parameters.AddWithValue("resolution_note", (object?)exception.ResolutionNote ?? DBNull.Value);
            insert.Parameters.AddWithValue("created_at", exception.CreatedAt.UtcDateTime);
            insert.Parameters.AddWithValue("resolved_at", (object?)exception.ResolvedAt?.UtcDateTime ?? DBNull.Value);
            await insert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return run;
    }

    public async Task<IReadOnlyList<ReconciliationRunDto>> GetReconciliationRunsAsync(Guid loanId, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select reconciliation_run_id,
                   loan_id,
                   projection_run_id,
                   requested_at,
                   completed_at,
                   status
            from {Qualified("reconciliation_run")}
            where loan_id = @loan_id
            order by requested_at desc;
            """;
        command.Parameters.AddWithValue("loan_id", loanId);

        var results = new List<ReconciliationRunDto>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            results.Add(new ReconciliationRunDto(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.IsDBNull(2) ? null : reader.GetGuid(2),
                new DateTimeOffset(reader.GetDateTime(3), TimeSpan.Zero),
                reader.IsDBNull(4) ? null : new DateTimeOffset(reader.GetDateTime(4), TimeSpan.Zero),
                reader.GetString(5)));
        }

        return results;
    }

    public async Task<IReadOnlyList<ReconciliationResultDto>> GetReconciliationResultsAsync(Guid reconciliationRunId, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select reconciliation_result_id,
                   reconciliation_run_id,
                   loan_id,
                   projected_cash_flow_id,
                   cash_transaction_id,
                   match_status,
                   expected_amount,
                   actual_amount,
                   variance_amount,
                   expected_date,
                   actual_date,
                   match_rule,
                   tolerance_json::text,
                   notes,
                   created_at
            from {Qualified("reconciliation_result")}
            where reconciliation_run_id = @reconciliation_run_id
            order by created_at, reconciliation_result_id;
            """;
        command.Parameters.AddWithValue("reconciliation_run_id", reconciliationRunId);

        var results = new List<ReconciliationResultDto>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            results.Add(new ReconciliationResultDto(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetGuid(2),
                reader.IsDBNull(3) ? null : reader.GetGuid(3),
                reader.IsDBNull(4) ? null : reader.GetGuid(4),
                reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetDecimal(6),
                reader.IsDBNull(7) ? null : reader.GetDecimal(7),
                reader.IsDBNull(8) ? null : reader.GetDecimal(8),
                reader.IsDBNull(9) ? null : DateOnly.FromDateTime(reader.GetDateTime(9)),
                reader.IsDBNull(10) ? null : DateOnly.FromDateTime(reader.GetDateTime(10)),
                reader.IsDBNull(11) ? null : reader.GetString(11),
                NormalizeJsonText(reader.IsDBNull(12) ? null : reader.GetString(12)),
                reader.IsDBNull(13) ? [] : reader.GetFieldValue<string[]>(13),
                new DateTimeOffset(reader.GetDateTime(14), TimeSpan.Zero)));
        }

        return results;
    }

    public async Task<IReadOnlyList<ReconciliationExceptionDto>> GetReconciliationExceptionsAsync(CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select exception_id,
                   reconciliation_result_id,
                   exception_type,
                   severity,
                   status,
                   assigned_to,
                   resolution_note,
                   created_at,
                   resolved_at
            from {Qualified("reconciliation_exception")}
            order by created_at desc;
            """;

        var results = new List<ReconciliationExceptionDto>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            results.Add(new ReconciliationExceptionDto(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                new DateTimeOffset(reader.GetDateTime(7), TimeSpan.Zero),
                reader.IsDBNull(8) ? null : new DateTimeOffset(reader.GetDateTime(8), TimeSpan.Zero)));
        }

        return results;
    }

    public async Task<ReconciliationExceptionDto?> ResolveReconciliationExceptionAsync(Guid exceptionId, string resolutionNote, string? assignedTo, CancellationToken ct = default)
    {
        var resolvedAt = DateTimeOffset.UtcNow;
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            update {Qualified("reconciliation_exception")}
            set status = @status,
                assigned_to = @assigned_to,
                resolution_note = @resolution_note,
                resolved_at = @resolved_at
            where exception_id = @exception_id;
            """;
        command.Parameters.AddWithValue("status", "Resolved");
        command.Parameters.AddWithValue("assigned_to", (object?)assignedTo ?? DBNull.Value);
        command.Parameters.AddWithValue("resolution_note", resolutionNote);
        command.Parameters.AddWithValue("resolved_at", resolvedAt.UtcDateTime);
        command.Parameters.AddWithValue("exception_id", exceptionId);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

        return (await GetReconciliationExceptionsAsync(ct).ConfigureAwait(false)).FirstOrDefault(item => item.ExceptionId == exceptionId);
    }

    public async Task<ServicerReportBatchDto> SaveServicerBatchAsync(ServicerReportBatchDto batch, IReadOnlyList<ServicerPositionReportLineDto> positionLines, IReadOnlyList<ServicerTransactionReportLineDto> transactionLines, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText =
                $"""
                insert into {Qualified("servicer_report_batch")} (
                    servicer_report_batch_id,
                    servicer_name,
                    report_type,
                    source_format,
                    report_as_of_date,
                    received_at,
                    file_name,
                    file_hash,
                    row_count,
                    status,
                    loaded_by,
                    notes)
                values (
                    @servicer_report_batch_id,
                    @servicer_name,
                    @report_type,
                    @source_format,
                    @report_as_of_date,
                    @received_at,
                    @file_name,
                    @file_hash,
                    @row_count,
                    @status,
                    @loaded_by,
                    @notes)
                on conflict (servicer_report_batch_id) do update
                set row_count = excluded.row_count,
                    status = excluded.status,
                    loaded_by = excluded.loaded_by,
                    notes = excluded.notes;
                """;
            insert.Parameters.AddWithValue("servicer_report_batch_id", batch.ServicerReportBatchId);
            insert.Parameters.AddWithValue("servicer_name", batch.ServicerName);
            insert.Parameters.AddWithValue("report_type", batch.ReportType);
            insert.Parameters.AddWithValue("source_format", batch.SourceFormat);
            insert.Parameters.AddWithValue("report_as_of_date", batch.ReportAsOfDate);
            insert.Parameters.AddWithValue("received_at", batch.ReceivedAt.UtcDateTime);
            insert.Parameters.AddWithValue("file_name", (object?)batch.FileName ?? DBNull.Value);
            insert.Parameters.AddWithValue("file_hash", (object?)batch.FileHash ?? DBNull.Value);
            insert.Parameters.AddWithValue("row_count", batch.RowCount);
            insert.Parameters.AddWithValue("status", batch.Status);
            insert.Parameters.AddWithValue("loaded_by", (object?)batch.LoadedBy ?? DBNull.Value);
            insert.Parameters.AddWithValue("notes", (object?)batch.Notes ?? DBNull.Value);
            await insert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await using (var deletePosition = connection.CreateCommand())
        {
            deletePosition.Transaction = transaction;
            deletePosition.CommandText = $"delete from {Qualified("servicer_position_report_line")} where servicer_report_batch_id = @servicer_report_batch_id;";
            deletePosition.Parameters.AddWithValue("servicer_report_batch_id", batch.ServicerReportBatchId);
            await deletePosition.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await using (var deleteTxn = connection.CreateCommand())
        {
            deleteTxn.Transaction = transaction;
            deleteTxn.CommandText = $"delete from {Qualified("servicer_transaction_report_line")} where servicer_report_batch_id = @servicer_report_batch_id;";
            deleteTxn.Parameters.AddWithValue("servicer_report_batch_id", batch.ServicerReportBatchId);
            await deleteTxn.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        foreach (var line in positionLines)
        {
            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText =
                $"""
                insert into {Qualified("servicer_position_report_line")} (
                    servicer_report_line_id,
                    servicer_report_batch_id,
                    loan_id,
                    report_as_of_date,
                    principal_outstanding,
                    interest_accrued_unpaid,
                    fees_accrued_unpaid,
                    penalty_accrued_unpaid,
                    commitment_available,
                    next_due_date,
                    next_due_amount,
                    delinquency_status,
                    raw_payload,
                    created_at)
                values (
                    @servicer_report_line_id,
                    @servicer_report_batch_id,
                    @loan_id,
                    @report_as_of_date,
                    @principal_outstanding,
                    @interest_accrued_unpaid,
                    @fees_accrued_unpaid,
                    @penalty_accrued_unpaid,
                    @commitment_available,
                    @next_due_date,
                    @next_due_amount,
                    @delinquency_status,
                    cast(@raw_payload as jsonb),
                    now());
                """;
            insert.Parameters.AddWithValue("servicer_report_line_id", line.ServicerReportLineId);
            insert.Parameters.AddWithValue("servicer_report_batch_id", batch.ServicerReportBatchId);
            insert.Parameters.AddWithValue("loan_id", line.LoanId);
            insert.Parameters.AddWithValue("report_as_of_date", line.ReportAsOfDate);
            insert.Parameters.AddWithValue("principal_outstanding", (object?)line.PrincipalOutstanding ?? DBNull.Value);
            insert.Parameters.AddWithValue("interest_accrued_unpaid", (object?)line.InterestAccruedUnpaid ?? DBNull.Value);
            insert.Parameters.AddWithValue("fees_accrued_unpaid", (object?)line.FeesAccruedUnpaid ?? DBNull.Value);
            insert.Parameters.AddWithValue("penalty_accrued_unpaid", (object?)line.PenaltyAccruedUnpaid ?? DBNull.Value);
            insert.Parameters.AddWithValue("commitment_available", (object?)line.CommitmentAvailable ?? DBNull.Value);
            insert.Parameters.AddWithValue("next_due_date", (object?)line.NextDueDate ?? DBNull.Value);
            insert.Parameters.AddWithValue("next_due_amount", (object?)line.NextDueAmount ?? DBNull.Value);
            insert.Parameters.AddWithValue("delinquency_status", (object?)line.DelinquencyStatus ?? DBNull.Value);
            insert.Parameters.AddWithValue("raw_payload", line.RawPayloadJson);
            await insert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        foreach (var line in transactionLines)
        {
            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText =
                $"""
                insert into {Qualified("servicer_transaction_report_line")} (
                    servicer_transaction_line_id,
                    servicer_report_batch_id,
                    loan_id,
                    servicer_transaction_id,
                    transaction_type,
                    effective_date,
                    transaction_date,
                    settlement_date,
                    gross_amount,
                    principal_amount,
                    interest_amount,
                    fee_amount,
                    penalty_amount,
                    currency,
                    external_ref,
                    raw_payload,
                    created_at)
                values (
                    @servicer_transaction_line_id,
                    @servicer_report_batch_id,
                    @loan_id,
                    @servicer_transaction_id,
                    @transaction_type,
                    @effective_date,
                    @transaction_date,
                    @settlement_date,
                    @gross_amount,
                    @principal_amount,
                    @interest_amount,
                    @fee_amount,
                    @penalty_amount,
                    @currency,
                    @external_ref,
                    cast(@raw_payload as jsonb),
                    now());
                """;
            insert.Parameters.AddWithValue("servicer_transaction_line_id", line.ServicerTransactionLineId);
            insert.Parameters.AddWithValue("servicer_report_batch_id", batch.ServicerReportBatchId);
            insert.Parameters.AddWithValue("loan_id", line.LoanId);
            insert.Parameters.AddWithValue("servicer_transaction_id", (object?)line.ServicerTransactionId ?? DBNull.Value);
            insert.Parameters.AddWithValue("transaction_type", line.TransactionType);
            insert.Parameters.AddWithValue("effective_date", line.EffectiveDate);
            insert.Parameters.AddWithValue("transaction_date", (object?)line.TransactionDate ?? DBNull.Value);
            insert.Parameters.AddWithValue("settlement_date", (object?)line.SettlementDate ?? DBNull.Value);
            insert.Parameters.AddWithValue("gross_amount", line.GrossAmount);
            insert.Parameters.AddWithValue("principal_amount", (object?)line.PrincipalAmount ?? DBNull.Value);
            insert.Parameters.AddWithValue("interest_amount", (object?)line.InterestAmount ?? DBNull.Value);
            insert.Parameters.AddWithValue("fee_amount", (object?)line.FeeAmount ?? DBNull.Value);
            insert.Parameters.AddWithValue("penalty_amount", (object?)line.PenaltyAmount ?? DBNull.Value);
            insert.Parameters.AddWithValue("currency", (object?)line.Currency?.ToString() ?? DBNull.Value);
            insert.Parameters.AddWithValue("external_ref", (object?)line.ExternalRef ?? DBNull.Value);
            insert.Parameters.AddWithValue("raw_payload", line.RawPayloadJson);
            await insert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return batch;
    }

    public async Task<ServicerReportBatchDto?> GetServicerBatchAsync(Guid batchId, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select servicer_report_batch_id,
                   servicer_name,
                   report_type,
                   source_format,
                   report_as_of_date,
                   received_at,
                   file_name,
                   file_hash,
                   row_count,
                   status,
                   loaded_by,
                   notes
            from {Qualified("servicer_report_batch")}
            where servicer_report_batch_id = @servicer_report_batch_id;
            """;
        command.Parameters.AddWithValue("servicer_report_batch_id", batchId);

        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            return null;
        }

        return new ServicerReportBatchDto(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            DateOnly.FromDateTime(reader.GetDateTime(4)),
            new DateTimeOffset(reader.GetDateTime(5), TimeSpan.Zero),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            reader.IsDBNull(7) ? null : reader.GetString(7),
            reader.GetInt32(8),
            reader.GetString(9),
            reader.IsDBNull(10) ? null : reader.GetString(10),
            reader.IsDBNull(11) ? null : reader.GetString(11));
    }

    public async Task<IReadOnlyList<ServicerPositionReportLineDto>> GetServicerPositionLinesAsync(Guid batchId, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select servicer_report_line_id,
                   servicer_report_batch_id,
                   loan_id,
                   report_as_of_date,
                   principal_outstanding,
                   interest_accrued_unpaid,
                   fees_accrued_unpaid,
                   penalty_accrued_unpaid,
                   commitment_available,
                   next_due_date,
                   next_due_amount,
                   delinquency_status,
                   raw_payload::text
            from {Qualified("servicer_position_report_line")}
            where servicer_report_batch_id = @servicer_report_batch_id
            order by loan_id, servicer_report_line_id;
            """;
        command.Parameters.AddWithValue("servicer_report_batch_id", batchId);

        var results = new List<ServicerPositionReportLineDto>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            results.Add(new ServicerPositionReportLineDto(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetGuid(2),
                DateOnly.FromDateTime(reader.GetDateTime(3)),
                reader.IsDBNull(4) ? null : reader.GetDecimal(4),
                reader.IsDBNull(5) ? null : reader.GetDecimal(5),
                reader.IsDBNull(6) ? null : reader.GetDecimal(6),
                reader.IsDBNull(7) ? null : reader.GetDecimal(7),
                reader.IsDBNull(8) ? null : reader.GetDecimal(8),
                reader.IsDBNull(9) ? null : DateOnly.FromDateTime(reader.GetDateTime(9)),
                reader.IsDBNull(10) ? null : reader.GetDecimal(10),
                reader.IsDBNull(11) ? null : reader.GetString(11),
                reader.GetString(12)));
        }

        return results;
    }

    public async Task<IReadOnlyList<ServicerTransactionReportLineDto>> GetServicerTransactionLinesAsync(Guid batchId, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select servicer_transaction_line_id,
                   servicer_report_batch_id,
                   loan_id,
                   servicer_transaction_id,
                   transaction_type,
                   effective_date,
                   transaction_date,
                   settlement_date,
                   gross_amount,
                   principal_amount,
                   interest_amount,
                   fee_amount,
                   penalty_amount,
                   currency,
                   external_ref,
                   raw_payload::text
            from {Qualified("servicer_transaction_report_line")}
            where servicer_report_batch_id = @servicer_report_batch_id
            order by effective_date, servicer_transaction_line_id;
            """;
        command.Parameters.AddWithValue("servicer_report_batch_id", batchId);

        var results = new List<ServicerTransactionReportLineDto>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            results.Add(new ServicerTransactionReportLineDto(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetGuid(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetString(4),
                DateOnly.FromDateTime(reader.GetDateTime(5)),
                reader.IsDBNull(6) ? null : DateOnly.FromDateTime(reader.GetDateTime(6)),
                reader.IsDBNull(7) ? null : DateOnly.FromDateTime(reader.GetDateTime(7)),
                reader.GetDecimal(8),
                reader.IsDBNull(9) ? null : reader.GetDecimal(9),
                reader.IsDBNull(10) ? null : reader.GetDecimal(10),
                reader.IsDBNull(11) ? null : reader.GetDecimal(11),
                reader.IsDBNull(12) ? null : reader.GetDecimal(12),
                reader.IsDBNull(13) ? null : ParseEnum<CurrencyCode>(reader.GetString(13)),
                reader.IsDBNull(14) ? null : reader.GetString(14),
                reader.GetString(15)));
        }

        return results;
    }

    public async Task<IReadOnlyList<DirectLendingOutboxMessage>> GetPendingOutboxMessagesAsync(int take, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select outbox_message_id,
                   topic,
                   message_key,
                   payload::text,
                   headers::text,
                   occurred_at,
                   visible_after,
                   processed_at,
                   error_count,
                   last_error
            from {Qualified("outbox_message")}
            where processed_at is null
              and visible_after <= now()
            order by occurred_at
            limit @take;
            """;
        command.Parameters.AddWithValue("take", take);

        var results = new List<DirectLendingOutboxMessage>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            results.Add(new DirectLendingOutboxMessage(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                NormalizeJsonText(reader.IsDBNull(4) ? null : reader.GetString(4)),
                new DateTimeOffset(reader.GetDateTime(5), TimeSpan.Zero),
                new DateTimeOffset(reader.GetDateTime(6), TimeSpan.Zero),
                reader.IsDBNull(7) ? null : new DateTimeOffset(reader.GetDateTime(7), TimeSpan.Zero),
                reader.GetInt32(8),
                reader.IsDBNull(9) ? null : reader.GetString(9)));
        }

        return results;
    }

    public async Task MarkOutboxProcessedAsync(Guid outboxMessageId, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"update {Qualified("outbox_message")} set processed_at = now(), last_error = null where outbox_message_id = @outbox_message_id;";
        command.Parameters.AddWithValue("outbox_message_id", outboxMessageId);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task MarkOutboxFailedAsync(Guid outboxMessageId, string error, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            update {Qualified("outbox_message")}
            set error_count = error_count + 1,
                last_error = @last_error,
                visible_after = now() + interval '30 seconds'
            where outbox_message_id = @outbox_message_id;
            """;
        command.Parameters.AddWithValue("last_error", error);
        command.Parameters.AddWithValue("outbox_message_id", outboxMessageId);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Guid>> GetLoanIdsAsync(CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"select loan_id from {Qualified("loan_state")} order by loan_id;";
        var results = new List<Guid>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            results.Add(reader.GetGuid(0));
        }

        return results;
    }

    public async Task<long> GetLatestEventPositionAsync(CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"select coalesce(max(event_position), 0) from {Qualified("loan_event")};";
        return (long)(await command.ExecuteScalarAsync(ct).ConfigureAwait(false) ?? 0L);
    }

    public async Task<IReadOnlyList<Guid>> GetLoanIdsSinceEventPositionAsync(long lastProcessedPosition, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select distinct loan_id
            from {Qualified("loan_event")}
            where event_position > @last_processed_position
            order by loan_id;
            """;
        command.Parameters.AddWithValue("last_processed_position", lastProcessedPosition);

        var results = new List<Guid>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            results.Add(reader.GetGuid(0));
        }

        return results;
    }

    public async Task<IReadOnlyList<RebuildCheckpointDto>> GetCheckpointsAsync(CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select projection_name,
                   last_processed_position,
                   last_event_id,
                   last_rebuilt_at,
                   status,
                   details
            from {Qualified("read_model_checkpoint")}
            order by projection_name;
            """;

        var results = new List<RebuildCheckpointDto>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            results.Add(new RebuildCheckpointDto(
                reader.GetString(0),
                reader.GetInt64(1),
                reader.IsDBNull(2) ? null : reader.GetGuid(2),
                reader.IsDBNull(3) ? null : new DateTimeOffset(reader.GetDateTime(3), TimeSpan.Zero),
                reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5)));
        }

        return results;
    }

    public async Task UpsertCheckpointAsync(RebuildCheckpointDto checkpoint, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            insert into {Qualified("read_model_checkpoint")} (
                projection_name,
                last_processed_position,
                last_event_id,
                last_rebuilt_at,
                status,
                details)
            values (
                @projection_name,
                @last_processed_position,
                @last_event_id,
                @last_rebuilt_at,
                @status,
                @details)
            on conflict (projection_name) do update
            set last_processed_position = excluded.last_processed_position,
                last_event_id = excluded.last_event_id,
                last_rebuilt_at = excluded.last_rebuilt_at,
                status = excluded.status,
                details = excluded.details;
            """;
        command.Parameters.AddWithValue("projection_name", checkpoint.ProjectionName);
        command.Parameters.AddWithValue("last_processed_position", checkpoint.LastProcessedPosition);
        command.Parameters.AddWithValue("last_event_id", (object?)checkpoint.LastEventId ?? DBNull.Value);
        command.Parameters.AddWithValue("last_rebuilt_at", (object?)checkpoint.LastRebuiltAt?.UtcDateTime ?? DBNull.Value);
        command.Parameters.AddWithValue("status", checkpoint.Status);
        command.Parameters.AddWithValue("details", (object?)checkpoint.Details ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<ProjectionRunDto>> ReadProjectionRunsAsync(NpgsqlCommand command, CancellationToken ct)
    {
        var results = new List<ProjectionRunDto>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            results.Add(new ProjectionRunDto(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetInt32(2),
                reader.GetInt64(3),
                DateOnly.FromDateTime(reader.GetDateTime(4)),
                reader.IsDBNull(5) ? null : DateOnly.FromDateTime(reader.GetDateTime(5)),
                reader.IsDBNull(6) ? null : reader.GetGuid(6),
                reader.GetString(7),
                reader.GetString(8),
                reader.GetString(9),
                ParseEnum<ProjectionRunStatus>(reader.GetString(10)),
                reader.IsDBNull(11) ? null : reader.GetGuid(11),
                new DateTimeOffset(reader.GetDateTime(12), TimeSpan.Zero)));
        }

        return results;
    }

    private async Task<IReadOnlyList<JournalEntryDto>> LoadJournalEntriesAsync(NpgsqlConnection connection, NpgsqlCommand command, CancellationToken ct)
    {
        var headers = new List<(Guid JournalEntryId, Guid? LoanId, DateOnly AccountingDate, DateOnly EffectiveDate, Guid SourceEventId, string EntryType, string LedgerBasis, string Description, DateTimeOffset RecordedAt, DateTimeOffset? PostedAt, JournalEntryStatus Status)>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            headers.Add((
                reader.GetGuid(0),
                reader.IsDBNull(1) ? null : reader.GetGuid(1),
                DateOnly.FromDateTime(reader.GetDateTime(2)),
                DateOnly.FromDateTime(reader.GetDateTime(3)),
                reader.GetGuid(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetString(7),
                new DateTimeOffset(reader.GetDateTime(8), TimeSpan.Zero),
                reader.IsDBNull(9) ? null : new DateTimeOffset(reader.GetDateTime(9), TimeSpan.Zero),
                ParseEnum<JournalEntryStatus>(reader.GetString(10))));
        }

        await reader.CloseAsync().ConfigureAwait(false);

        var results = new List<JournalEntryDto>();
        foreach (var header in headers)
        {
            await using var linesCommand = connection.CreateCommand();
            linesCommand.CommandText =
                $"""
                select journal_line_id,
                       line_no,
                       account_code,
                       debit_amount,
                       credit_amount,
                       currency,
                       dimensions_json::text
                from {Qualified("journal_line")}
                where journal_entry_id = @journal_entry_id
                order by line_no;
                """;
            linesCommand.Parameters.AddWithValue("journal_entry_id", header.JournalEntryId);

            var lines = new List<JournalLineDto>();
            await using var linesReader = await linesCommand.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await linesReader.ReadAsync(ct).ConfigureAwait(false))
            {
                lines.Add(new JournalLineDto(
                    linesReader.GetGuid(0),
                    linesReader.GetInt32(1),
                    linesReader.GetString(2),
                    linesReader.GetDecimal(3),
                    linesReader.GetDecimal(4),
                    ParseEnum<CurrencyCode>(linesReader.GetString(5)),
                    NormalizeJsonText(linesReader.IsDBNull(6) ? null : linesReader.GetString(6))));
            }

            results.Add(new JournalEntryDto(
                header.JournalEntryId,
                header.LoanId,
                header.AccountingDate,
                header.EffectiveDate,
                header.SourceEventId,
                header.EntryType,
                header.LedgerBasis,
                header.Description,
                header.RecordedAt,
                header.PostedAt,
                header.Status,
                lines));
        }

        return results;
    }
}
