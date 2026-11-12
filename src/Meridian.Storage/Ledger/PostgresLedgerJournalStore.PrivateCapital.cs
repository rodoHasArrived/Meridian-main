using System.Data;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.Banking;
using Meridian.Contracts.Ledger;
using Meridian.Ledger;
using Npgsql;

namespace Meridian.Storage.Ledger;

public sealed partial class PostgresLedgerJournalStore
{
    private static readonly JsonSerializerOptions CapitalJsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private async Task ValidateAndRecordCapitalPostingAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction,
        LedgerJournalEntryWrite write, LedgerAccountingPeriod period, CancellationToken ct)
    {
        var metadata = write.Entry.Metadata;
        var json = metadata.Tags?.GetValueOrDefault(PrivateCapitalPostingAuthority.MetadataTag);
        if (json is null && !PrivateCapitalPostingAuthority.IsCapitalCall(metadata.IdempotencyKey, metadata.FundEventType)
            && !CapitalSame(metadata.Tags?.GetValueOrDefault("fundEventType"), "CapitalCall")
            && !CapitalSame(metadata.Tags?.GetValueOrDefault("manualJournalEntryType"), "CapitalCall")
            && !write.Entry.Lines.Any(line => CapitalSame(line.Account.Name, "Capital Call Receivable")))
            return;
        if (json is null)
            throw new LedgerValidationException("Capital-call posting requires retained approved commitment and call authority.");
        // An older RepeatableRead snapshot can hide a newly approved successor even after the
        // commitment advisory lock is acquired. ReadCommitted refreshes it; Serializable detects it.
        if (transaction.IsolationLevel is not (IsolationLevel.ReadCommitted or IsolationLevel.Serializable))
            throw new LedgerValidationException("Capital-call posting requires ReadCommitted or Serializable transaction isolation.");
        var authority = PrivateCapitalPostingAuthority.Deserialize(json);
        var scope = PrivateCapitalAuthorityRules.NormalizeScope(authority.Scope);
        if (scope != authority.Scope || scope.LedgerBookId != period.LedgerBookId
            || !CapitalSame(scope.TenantId, metadata.Tags?.GetValueOrDefault("tenantId"))
            || !CapitalSame(scope.CompanyId, metadata.Tags?.GetValueOrDefault("companyId"))
            || !CapitalSame(scope.FundProfileId, metadata.ProjectId)
            || authority.Amount <= 0m || authority.EffectiveDate != metadata.EffectiveDate
            || authority.Kind is not ("Issuance" or "Funding"))
            throw new LedgerValidationException("Capital-call posting scope, date or kind does not match retained authority.");
        await using (var bookScope = connection.CreateCommand())
        {
            bookScope.Transaction = transaction;
            bookScope.CommandText = $"""
                select b.ledger_book_id from {Qualified("ledger_books")} b
                join {Qualified("fund_profile_tenancy")} t on t.fund_profile_id=lower(btrim(b.fund_profile_id))
                where b.ledger_book_id=@book and lower(btrim(b.fund_profile_id))=@fund
                    and lower(btrim(b.tenant_id))=@tenant and lower(btrim(t.tenant_id))=@tenant
                    and lower(btrim(t.company_id))=@company and lower(b.fund_structure_node_id::text)=@entity
                    and lower(b.fund_structure_node_kind)='entity' and upper(b.base_currency)=@currency
                for share of b,t
                """;
            AddCapitalScopeParameters(bookScope, scope);
            bookScope.Parameters.AddWithValue("entity", scope.EntityId);
            bookScope.Parameters.AddWithValue("currency", scope.Currency);
            if (await bookScope.ExecuteScalarAsync(ct).ConfigureAwait(false) is not Guid)
                throw new LedgerValidationException("Capital-call authority does not match the owned legal-entity book and currency.");
        }

        // Match the lock used by commitment revision/approval. Financial ceilings are checked
        // under this database lock; competing drafts reserve no capacity.
        await using (var gate = connection.CreateCommand())
        {
            gate.Transaction = transaction;
            gate.CommandText = "select pg_advisory_xact_lock(hashtextextended(@key, 0))";
            gate.Parameters.AddWithValue("key", PrivateCapitalAuthorityRules.LockKey(scope, authority.CommitmentId));
            await gate.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        var call = await ReadCapitalCallForPostingAsync(connection, transaction, scope, authority.CallId, ct).ConfigureAwait(false);
        var line = call.Lines.SingleOrDefault(item => item.CommitmentId == authority.CommitmentId)
            ?? throw new LedgerValidationException("The commitment is absent from the retained call plan.");
        if (call.Scope != scope || call.PlanHash != authority.PlanHash || call.PlanHash != PrivateCapitalCallPlanHash.Compute(call)
            || line.CommitmentRevision != authority.CommitmentRevision || !Guid.TryParse(call.PeriodId, out var callPeriod)
            || callPeriod != write.PeriodId)
            throw new LedgerValidationException("Capital-call plan or accounting period changed; renew review.");

        var revisions = await ReadCapitalCommitmentsForPostingAsync(connection, transaction, scope, authority.CommitmentId, ct).ConfigureAwait(false);
        var commitment = revisions.SingleOrDefault(item => item.Revision == authority.CommitmentRevision)
            ?? throw new LedgerValidationException("Approved commitment revision is unavailable.");
        if (commitment.Scope != scope || commitment.ApprovalStatus != PrivateCapitalCommitmentApprovalStatus.Approved
            || commitment.InvestorId != line.InvestorId || commitment.CapitalAccountId != line.CapitalAccountId)
            throw new LedgerValidationException("The call is not backed by approved investor commitment authority.");
        ValidateCapitalJournalShape(write.Entry, authority, call, line);

        await using (var legacy = connection.CreateCommand())
        {
            legacy.Transaction = transaction;
            legacy.CommandText = $"""
                select exists (
                    select 1 from {Qualified("journal_entries")} je
                    join {Qualified("accounting_periods")} ap on ap.period_id = je.period_id
                    where ap.ledger_book_id = @book
                    and (lower(je.metadata ->> 'fundEventType') = 'capitalcall'
                        or lower(je.metadata -> 'tags' ->> 'fundEventType') = 'capitalcall')
                    and (lower(je.metadata ->> 'investorId') = @investor
                        or lower(je.metadata -> 'tags' ->> 'investorId') = @investor
                        or btrim(coalesce(je.metadata ->> 'investorId', '')) = ''
                            and btrim(coalesce(je.metadata -> 'tags' ->> 'investorId', '')) = '')
                    and not exists (select 1 from {Qualified("private_capital_postings")} pc where pc.journal_entry_id = je.journal_entry_id))
                """;
            legacy.Parameters.AddWithValue("book", scope.LedgerBookId);
            legacy.Parameters.AddWithValue("investor", line.InvestorId.ToLowerInvariant());
            if ((bool)(await legacy.ExecuteScalarAsync(ct).ConfigureAwait(false))!)
                throw new LedgerValidationException("Historical capital activity requires opening reconciliation before native posting.");
        }

        decimal called, issued, funded;
        await using (var totals = connection.CreateCommand())
        {
            totals.Transaction = transaction;
            totals.CommandText = $"""
                select coalesce(sum(amount) filter (where kind = 'Issuance' and effective_date <= @date),0),
                    coalesce(sum(amount) filter (where kind = 'Issuance' and call_id = @call),0),
                    coalesce(sum(amount) filter (where kind = 'Funding' and call_id = @call),0)
                from {Qualified("private_capital_postings")}
                where tenant_id = @tenant and company_id = @company and fund_profile_id = @fund
                    and ledger_book_id = @book and commitment_id = @commitment
                """;
            AddCapitalScopeParameters(totals, scope);
            totals.Parameters.AddWithValue("commitment", authority.CommitmentId);
            totals.Parameters.AddWithValue("call", authority.CallId);
            totals.Parameters.AddWithValue("date", authority.EffectiveDate);
            await using var reader = await totals.ExecuteReaderAsync(ct).ConfigureAwait(false);
            await reader.ReadAsync(ct).ConfigureAwait(false);
            called = reader.GetDecimal(0);
            issued = reader.GetDecimal(1);
            funded = reader.GetDecimal(2);
        }
        if (authority.Kind == "Issuance")
        {
            var postedIssuances = await ReadCapitalIssuanceTimelineAsync(connection, transaction, scope, authority.CommitmentId, ct).ConfigureAwait(false);
            var timelineIssue = PrivateCapitalAuthorityRules.FindIssuanceTimelineViolation(authority.CommitmentId,
                revisions, postedIssuances, authority.EffectiveDate, authority.Amount);
            if (timelineIssue is not null)
                throw new LedgerValidationException(timelineIssue);
            var current = revisions.Where(item => item.ApprovalStatus == PrivateCapitalCommitmentApprovalStatus.Approved
                    && item.EffectiveFrom <= authority.EffectiveDate)
                .OrderByDescending(item => item.EffectiveFrom).ThenByDescending(item => item.Revision).FirstOrDefault();
            if (current is null || current.Revision != commitment.Revision || !current.IsActive
                || authority.EffectiveDate < current.CommitmentDate || authority.EffectiveDate != call.NoticeDate
                || issued != 0m || authority.Amount != line.Amount || called + authority.Amount > commitment.TotalCommitment
                || commitment.TotalCommitment - called != line.UncalledBefore)
                throw new LedgerValidationException("Capital-call commitment revision or remaining capacity changed; review a fresh preview.");
        }
        else
        {
            if (issued != line.Amount || authority.Amount > issued - funded || authority.EffectiveDate < call.NoticeDate
                || string.IsNullOrWhiteSpace(authority.FundingReceiptId) || string.IsNullOrWhiteSpace(authority.BankLineId))
                throw new LedgerValidationException("Capital-call receipt exceeds the posted open receivable or lacks source identity.");
            try
            {
                await ValidateCapitalBankSourceAsync(connection, transaction, authority, ct).ConfigureAwait(false);
            }
            catch (PostgresException exception) when (exception.SqlState is PostgresErrorCodes.UndefinedTable or PostgresErrorCodes.InvalidSchemaName)
            {
                throw new LedgerValidationException("Same-database banking authority is unavailable; capital-call funding is unsupported in this configuration.");
            }
        }

        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = $"""
            insert into {Qualified("private_capital_postings")} (
                tenant_id,company_id,entity_id,fund_profile_id,ledger_book_id,currency,posting_id,
                commitment_id,commitment_revision,call_id,installment_id,kind,amount,funding_receipt_id,
                bank_line_id,journal_entry_id,effective_date)
            values (@tenant,@company,@entity,@fund,@book,@currency,@posting,@commitment,@revision,@call,
                @installment,@kind,@amount,@receipt,@bank,@journal,@date)
            """;
        AddCapitalScopeParameters(insert, scope);
        insert.Parameters.AddWithValue("entity", scope.EntityId);
        insert.Parameters.AddWithValue("currency", scope.Currency);
        insert.Parameters.AddWithValue("posting", metadata.IdempotencyKey!);
        insert.Parameters.AddWithValue("commitment", authority.CommitmentId);
        insert.Parameters.AddWithValue("revision", authority.CommitmentRevision);
        insert.Parameters.AddWithValue("call", authority.CallId);
        insert.Parameters.AddWithValue("installment", line.InstallmentId);
        insert.Parameters.AddWithValue("kind", authority.Kind);
        insert.Parameters.AddWithValue("amount", authority.Amount);
        insert.Parameters.AddWithValue("receipt", (object?)authority.FundingReceiptId ?? DBNull.Value);
        insert.Parameters.AddWithValue("bank", (object?)authority.BankLineId ?? DBNull.Value);
        insert.Parameters.AddWithValue("journal", write.Entry.JournalEntryId);
        insert.Parameters.AddWithValue("date", authority.EffectiveDate);
        await insert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private async Task<PrivateCapitalCallDto> ReadCapitalCallForPostingAsync(NpgsqlConnection connection,
        NpgsqlTransaction transaction, PrivateCapitalScopeDto scope, string callId, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"select document::text from {Qualified("private_capital_calls")} where tenant_id=@tenant and company_id=@company and fund_profile_id=@fund and ledger_book_id=@book and call_id=@call for update";
        AddCapitalScopeParameters(command, scope);
        command.Parameters.AddWithValue("call", callId);
        var json = await command.ExecuteScalarAsync(ct).ConfigureAwait(false) as string;
        return json is null ? throw new LedgerValidationException("The retained capital-call plan is unavailable.")
            : JsonSerializer.Deserialize<PrivateCapitalCallDto>(json, CapitalJsonOptions)
                ?? throw new LedgerValidationException("The retained capital-call plan is invalid.");
    }

    private async Task<IReadOnlyList<(DateOnly EffectiveDate, decimal Amount)>> ReadCapitalIssuanceTimelineAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, PrivateCapitalScopeDto scope, string commitmentId, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            select effective_date,amount from {Qualified("private_capital_postings")}
            where tenant_id=@tenant and company_id=@company and fund_profile_id=@fund
                and ledger_book_id=@book and commitment_id=@commitment and kind='Issuance'
            """;
        AddCapitalScopeParameters(command, scope);
        command.Parameters.AddWithValue("commitment", commitmentId);
        var result = new List<(DateOnly EffectiveDate, decimal Amount)>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            result.Add((reader.GetFieldValue<DateOnly>(0), reader.GetDecimal(1)));
        return result;
    }

    private async Task<IReadOnlyList<PrivateCapitalCommitmentDto>> ReadCapitalCommitmentsForPostingAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, PrivateCapitalScopeDto scope, string commitmentId, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"select document::text from {Qualified("private_capital_commitments")} where tenant_id=@tenant and company_id=@company and fund_profile_id=@fund and ledger_book_id=@book and commitment_id=@commitment for update";
        AddCapitalScopeParameters(command, scope);
        command.Parameters.AddWithValue("commitment", commitmentId);
        var result = new List<PrivateCapitalCommitmentDto>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            result.Add(JsonSerializer.Deserialize<PrivateCapitalCommitmentDto>(reader.GetString(0), CapitalJsonOptions)
                ?? throw new LedgerValidationException("Retained commitment authority is invalid."));
        return result;
    }

    private static void ValidateCapitalJournalShape(JournalEntry entry, PrivateCapitalPostingAuthorityDto authority,
        PrivateCapitalCallDto call, PrivateCapitalCallLineDto callLine)
    {
        var metadata = entry.Metadata;
        var expectedKey = authority.Kind == "Issuance"
            ? $"capital-call:{callLine.CommitmentId}:{callLine.InstallmentId}"
            : $"capital-call-funding:{callLine.CommitmentId}:{callLine.InstallmentId}:receipt:{Uri.EscapeDataString(authority.FundingReceiptId ?? "")}";
        if (metadata.IdempotencyKey != expectedKey || metadata.FundEventId != $"fund-event:{call.Scope.FundProfileId}:capital-call:{callLine.InstallmentId}"
            || metadata.InvestorId != callLine.InvestorId || metadata.CapitalAccountId != callLine.CapitalAccountId
            || !CapitalSame(metadata.FundEventType, "CapitalCall") || entry.Lines.Count != 2)
            throw new LedgerValidationException("Capital-call journal identity differs from approved authority.");
        foreach (var line in entry.Lines)
        {
            var dimensions = line.Dimensions;
            if (dimensions is null || dimensions.EntityId != call.Scope.EntityId || dimensions.FundId != call.Scope.FundProfileId
                || dimensions.InvestorId != callLine.InvestorId || dimensions.CapitalAccountId != callLine.CapitalAccountId
                || dimensions.ExternalGlDimensions.GetValueOrDefault("capitalCallId") != call.CallId
                || dimensions.ExternalGlDimensions.GetValueOrDefault("commitmentId") != callLine.CommitmentId
                || dimensions.ExternalGlDimensions.GetValueOrDefault("commitmentRevision") != callLine.CommitmentRevision.ToString(System.Globalization.CultureInfo.InvariantCulture)
                || dimensions.ExternalGlDimensions.GetValueOrDefault("capitalActivityKind") != (authority.Kind == "Issuance" ? "CapitalCallIssuance" : "CapitalCallFunding")
                || line.Currency is not null || line.Debit + line.Credit != authority.Amount)
                throw new LedgerValidationException("Capital-call journal lines differ from approved scope or amount.");
        }
        var debit = entry.Lines.SingleOrDefault(line => line.Debit > 0m);
        var credit = entry.Lines.SingleOrDefault(line => line.Credit > 0m);
        if (debit is null || credit is null || (authority.Kind == "Issuance"
                ? !CapitalSame(debit.Account.Name, "Capital Call Receivable") || debit.Account.AccountType != LedgerAccountType.Asset
                  || !CapitalSame(credit.Account.Name, "Investor Capital") || credit.Account.AccountType != LedgerAccountType.Equity
                  || debit.Account.FinancialAccountId != callLine.InvestorId || credit.Account.FinancialAccountId != callLine.InvestorId
                : !CapitalSame(debit.Account.Name, "Cash") || debit.Account.AccountType != LedgerAccountType.Asset
                  || !CapitalSame(credit.Account.Name, "Capital Call Receivable") || credit.Account.AccountType != LedgerAccountType.Asset
                  || debit.Account.FinancialAccountId != call.Scope.FundProfileId || credit.Account.FinancialAccountId != callLine.InvestorId))
            throw new LedgerValidationException("Capital-call journal accounting does not match its approved economic event.");
    }

    private async Task ValidateCapitalBankSourceAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        PrivateCapitalPostingAuthorityDto authority, CancellationToken ct)
    {
        if (!Guid.TryParse(authority.BankLineId, out var bankId) || authority.BankLineId != bankId.ToString("D")
            || authority.FundingReceiptId != PrivateCapitalAuthorityRules.Id(authority.FundingReceiptId!, nameof(authority.FundingReceiptId)))
            throw new LedgerValidationException("Canonical retained bank transaction and funding receipt identities are required.");
        var bankingSchema = ValidateIdentifier(_options.PrivateCapitalBankingSchema, nameof(_options.PrivateCapitalBankingSchema));
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            select bank_transaction_id,entity_id,transaction_type,effective_date,transaction_date,
                settlement_date,amount,currency,external_ref,recorded_at,is_voided,recorded_by,
                pending_payment_id,evidence_id,canonical_input_hash
            from {bankingSchema}.bank_transactions where bank_transaction_id=@bank for share
            """;
        command.Parameters.AddWithValue("bank", bankId);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
            throw new LedgerValidationException("Retained banking authority is unavailable in the ledger database.");
        var source = new BankTransactionDto(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2),
            reader.GetFieldValue<DateOnly>(3), reader.GetFieldValue<DateOnly>(4), reader.GetFieldValue<DateOnly>(5),
            reader.GetDecimal(6), reader.GetString(7), reader.IsDBNull(8) ? null : reader.GetString(8),
            reader.GetFieldValue<DateTimeOffset>(9), reader.GetBoolean(10), reader.IsDBNull(11) ? null : reader.GetString(11),
            reader.IsDBNull(12) ? null : reader.GetGuid(12), reader.IsDBNull(13) ? null : reader.GetString(13),
            reader.IsDBNull(14) ? null : reader.GetString(14));
        if (!PrivateCapitalPostingAuthority.MatchesBankTransaction(authority, source))
            throw new LedgerValidationException("Retained bank evidence changed or was voided; renew funding review.");
    }

    private static void AddCapitalScopeParameters(NpgsqlCommand command, PrivateCapitalScopeDto scope)
    {
        command.Parameters.AddWithValue("tenant", scope.TenantId);
        command.Parameters.AddWithValue("company", scope.CompanyId);
        command.Parameters.AddWithValue("fund", scope.FundProfileId);
        command.Parameters.AddWithValue("book", scope.LedgerBookId);
    }

    private static bool CapitalSame(string? first, string? second)
        => string.Equals(first?.Trim(), second?.Trim(), StringComparison.OrdinalIgnoreCase);
}
