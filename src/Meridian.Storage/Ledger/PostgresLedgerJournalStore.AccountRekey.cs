using Meridian.Contracts.Accounting.Lots;
using Meridian.Ledger;
using Npgsql;

namespace Meridian.Storage.Ledger;

/// <summary>
/// Security Master review F2: the governed <see cref="AtomicTaxLotMutationKind.AccountRekey"/> batch
/// moves a legacy ticker-keyed per-security account's balance and every one of its open tax lots onto
/// the SecurityId-keyed account in one serializable transaction. Lots are re-keyed in place, so their
/// record id, label, acquisition date, basis, acquisition facts, lineage and wash-sale links are kept;
/// as-of lot reads (which rebuild from the current row plus dated quantity mutations) therefore see
/// the lot's whole history under the SecurityId-keyed account and nothing under the legacy key.
/// </summary>
public sealed partial class PostgresLedgerJournalStore
{
    private async Task<IReadOnlyList<LedgerTaxLotRecord>> LockAccountRekeyAuthorityAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        AtomicTaxLotJournalCommand command,
        CancellationToken ct)
    {
        var instruction = command.AccountRekey!;
        _ = ResolveAtomicAssetScope(command.Journal);
        var lotIds = instruction.Lots.Select(static lot => lot.TaxLotRecordId).ToArray();
        var locked = (await LoadTaxLotsForUpdateAsync(connection, transaction, command.LedgerBookId, lotIds, ct)
                .ConfigureAwait(false))
            .ToDictionary(static lot => lot.TaxLotRecordId);
        var before = new LedgerTaxLotRecord[lotIds.Length];
        for (var index = 0; index < lotIds.Length; index++)
        {
            var expected = instruction.Lots[index];
            if (!locked.TryGetValue(expected.TaxLotRecordId, out var lot))
                throw new LedgerValidationException("Account re-key lot no longer exists in the ledger book.");
            if (lot.Account != instruction.LegacyAccount || lot.Account.SecurityId is not null
                || lot.SecurityId != instruction.TargetAccount.SecurityId
                || lot.BookPositionId != expected.BookPositionId
                || !string.Equals(lot.LotId, expected.LotId, StringComparison.Ordinal)
                || lot.Version != expected.ExpectedVersion || lot.OpenQuantity != expected.ExpectedOpenQuantity
                || lot.OpenQuantity <= 0m
                || LedgerAccountRekeyRules.OpenBasis(lot) != expected.ExpectedCostBasis)
                throw new LedgerValidationException(
                    "Account re-key lot account, instrument, position, version, open quantity or basis changed after review.");
            before[index] = lot;
        }

        // A partial re-key would split one holding across the ticker and SecurityId keys, which is the
        // failure F2 exists to prevent: the instruction must name every open lot on the legacy account.
        var openOnLegacy = await LoadOpenLotIdsOnAccountAsync(connection, transaction, command.LedgerBookId,
            instruction.LegacyAccount, ct).ConfigureAwait(false);
        if (openOnLegacy.Count != lotIds.Length || openOnLegacy.Any(id => !locked.ContainsKey(id)))
            throw new LedgerValidationException(
                "Account re-key must move every open lot on the legacy account, across all of its book positions.");

        // Labels are unique per account key across open and closed lots. A label already taken on the
        // SecurityId-keyed account must have been reviewed into its relabel form; refuse otherwise
        // rather than fail on the unique index.
        var finalLabels = instruction.Lots.Select(LedgerAccountRekeyRules.FinalLotId).ToArray();
        var taken = await LoadTaxLotLabelsOnAccountAsync(connection, transaction, command.LedgerBookId,
            instruction.TargetAccount, finalLabels, ct).ConfigureAwait(false);
        if (taken.Count > 0)
            throw new LedgerValidationException(
                $"Account re-key lot label(s) {string.Join(", ", taken.Order(StringComparer.Ordinal))} already exist on the SecurityId-keyed account; plan the re-key again so they are relabeled.");

        var balance = await ReadAccountBalanceAsync(connection, transaction, command.LedgerBookId,
            instruction.LegacyAccount, ct).ConfigureAwait(false);
        if (balance != instruction.LegacyBalance)
            throw new LedgerValidationException(
                $"Legacy account balance {balance} no longer matches the reviewed re-key amount {instruction.LegacyBalance}.");
        return before;
    }

    private async Task<IReadOnlyList<LedgerTaxLotMutationRecord>> ApplyAccountRekeyAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        AtomicTaxLotJournalCommand command,
        IReadOnlyList<LedgerTaxLotRecord> lots,
        DateTimeOffset recordedAt,
        CancellationToken ct)
    {
        var instruction = command.AccountRekey!;
        var targetSecurityId = instruction.TargetAccount.SecurityId!.Value;
        var evidenceId = command.RetainedEvidence
            .First(static evidence => evidence.SubjectType == LedgerAccountRekeyRules.EvidenceSubjectType).EvidenceId;
        var mutations = new List<LedgerTaxLotMutationRecord>(lots.Count);
        for (var index = 0; index < lots.Count; index++)
        {
            var before = lots[index];
            await using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = $"""
                update {Qualified("tax_lots")}
                set account_security_id = @target_security_id, lot_id = @target_lot_id, version = version + 1,
                    last_mutation_batch_id = @batch, updated_at = @at
                where tax_lot_record_id = @id and ledger_book_id = @book and account_security_id is null
                  and security_id = @target_security_id and version = @version and open_quantity = @quantity
                returning {AverageCostLotColumns};
                """;
            update.Parameters.AddWithValue("target_security_id", targetSecurityId);
            update.Parameters.AddWithValue("batch", command.MutationBatchId);
            update.Parameters.AddWithValue("at", recordedAt.UtcDateTime);
            update.Parameters.AddWithValue("id", before.TaxLotRecordId);
            update.Parameters.AddWithValue("book", command.LedgerBookId);
            update.Parameters.AddWithValue("version", before.Version);
            update.Parameters.AddWithValue("quantity", before.OpenQuantity);
            update.Parameters.AddWithValue("target_lot_id", LedgerAccountRekeyRules.FinalLotId(instruction.Lots[index]));
            LedgerTaxLotRecord after;
            await using (var reader = await update.ExecuteReaderAsync(ct).ConfigureAwait(false))
            {
                if (!await reader.ReadAsync(ct).ConfigureAwait(false))
                    throw new LedgerValidationException("Account re-key lot compare-and-swap failed.");
                after = ReadTaxLot(reader);
            }

            if (after.Account != instruction.TargetAccount || after.OpenQuantity != before.OpenQuantity
                || after.LotId != LedgerAccountRekeyRules.FinalLotId(instruction.Lots[index])
                || LedgerAccountRekeyRules.OpenBasis(after) != LedgerAccountRekeyRules.OpenBasis(before))
                throw new LedgerValidationException("Account re-key changed lot economics or did not land on the target account.");
            mutations.Add(BuildMutationRecord(command, after, before, index, 0m, before.Version, evidenceId, recordedAt,
                costBasis: instruction.Lots[index].ExpectedCostBasis));
        }

        await CopyTaxLotPoliciesAsync(connection, transaction, command, recordedAt, ct).ConfigureAwait(false);
        return mutations;
    }

    // The relief policy history of the legacy account is carried onto the target key with the same
    // effective dates, so relief and as-of policy reads behave identically after the re-key. A target
    // that already states a policy for an effective date keeps it.
    private async Task CopyTaxLotPoliciesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        AtomicTaxLotJournalCommand command,
        DateTimeOffset recordedAt,
        CancellationToken ct)
    {
        var instruction = command.AccountRekey!;
        await using var copy = connection.CreateCommand();
        copy.Transaction = transaction;
        copy.CommandText = $"""
            insert into {Qualified("tax_lot_policies")} (
                policy_record_id, ledger_book_id, account_name, account_type, symbol, financial_account_id,
                relief_method, policy_id, effective_date, rationale, created_at, updated_at,
                wash_sale_enabled, wash_sale_window_days, wash_sale_scope, wash_sale_effective_date,
                account_security_id)
            select md5(@batch::text || ':' || legacy.policy_record_id::text)::uuid,
                   legacy.ledger_book_id, legacy.account_name, legacy.account_type, legacy.symbol,
                   legacy.financial_account_id, legacy.relief_method, legacy.policy_id, legacy.effective_date,
                   legacy.rationale, @at, @at, legacy.wash_sale_enabled, legacy.wash_sale_window_days,
                   legacy.wash_sale_scope, legacy.wash_sale_effective_date, @target_security_id
            from {Qualified("tax_lot_policies")} legacy
            where legacy.ledger_book_id = @ledger_book_id
              and legacy.account_name = @account_name
              and legacy.account_type = @account_type
              and {AccountInstrumentMatch("legacy")}
              and lower(legacy.financial_account_id) is not distinct from lower(@financial_account_id)
              and not exists (
                  select 1 from {Qualified("tax_lot_policies")} target
                  where target.ledger_book_id = legacy.ledger_book_id
                    and target.account_name = legacy.account_name
                    and target.account_type = legacy.account_type
                    and target.account_security_id = @target_security_id
                    and lower(target.financial_account_id) is not distinct from lower(legacy.financial_account_id)
                    and target.effective_date = legacy.effective_date)
            on conflict do nothing;
            """;
        copy.Parameters.AddWithValue("batch", command.MutationBatchId);
        copy.Parameters.AddWithValue("at", recordedAt.UtcDateTime);
        copy.Parameters.AddWithValue("target_security_id", instruction.TargetAccount.SecurityId!.Value);
        copy.Parameters.AddWithValue("ledger_book_id", command.LedgerBookId);
        AddAccountParameters(copy, instruction.LegacyAccount);
        await copy.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<string>> LoadTaxLotLabelsOnAccountAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid ledgerBookId,
        LedgerAccount account,
        IReadOnlyList<string> labels,
        CancellationToken ct)
    {
        await using var query = connection.CreateCommand();
        query.Transaction = transaction;
        query.CommandText = $"""
            select lot_id
            from {Qualified("tax_lots")}
            where ledger_book_id = @ledger_book_id
              and account_name = @account_name
              and account_type = @account_type
              and {AccountInstrumentMatch()}
              and lower(financial_account_id) is not distinct from lower(@financial_account_id)
              and lot_id = any(@labels);
            """;
        query.Parameters.AddWithValue("ledger_book_id", ledgerBookId);
        AddAccountParameters(query, account);
        query.Parameters.AddWithValue("labels", labels.ToArray());
        var taken = new List<string>();
        await using var reader = await query.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            taken.Add(reader.GetString(0));
        return taken;
    }

    private async Task<IReadOnlyList<Guid>> LoadOpenLotIdsOnAccountAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid ledgerBookId,
        LedgerAccount account,
        CancellationToken ct)
    {
        await using var query = connection.CreateCommand();
        query.Transaction = transaction;
        query.CommandText = $"""
            select tax_lot_record_id
            from {Qualified("tax_lots")}
            where ledger_book_id = @ledger_book_id
              and account_name = @account_name
              and account_type = @account_type
              and {AccountInstrumentMatch()}
              and lower(financial_account_id) is not distinct from lower(@financial_account_id)
              and open_quantity > 0
            order by tax_lot_record_id
            for update;
            """;
        query.Parameters.AddWithValue("ledger_book_id", ledgerBookId);
        AddAccountParameters(query, account);
        var ids = new List<Guid>();
        await using var reader = await query.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            ids.Add(reader.GetGuid(0));
        return ids;
    }

    // Normal-balance (debit-normal for assets and expenses) balance of one exact account across every
    // posted journal of the book, read inside the posting transaction before the reclass is appended.
    private async Task<decimal> ReadAccountBalanceAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid ledgerBookId,
        LedgerAccount account,
        CancellationToken ct)
    {
        await using var query = connection.CreateCommand();
        query.Transaction = transaction;
        query.CommandText = $"""
            select coalesce(sum(jl.debit), 0), coalesce(sum(jl.credit), 0)
            from {Qualified("journal_entries")} je
            join {Qualified("accounting_periods")} p on p.period_id = je.period_id
            join {Qualified("journal_legs")} jl on jl.journal_entry_id = je.journal_entry_id
            where p.ledger_book_id = @ledger_book_id
              and jl.account_name = @account_name
              and jl.account_type = @account_type
              and {AccountInstrumentMatch("jl")}
              and lower(jl.financial_account_id) is not distinct from lower(@financial_account_id);
            """;
        query.Parameters.AddWithValue("ledger_book_id", ledgerBookId);
        AddAccountParameters(query, account);
        await using var reader = await query.ExecuteReaderAsync(ct).ConfigureAwait(false);
        await reader.ReadAsync(ct).ConfigureAwait(false);
        var debit = reader.GetDecimal(0);
        var credit = reader.GetDecimal(1);
        return Meridian.Ledger.Ledger.CalculateNetBalance(account.AccountType, debit, credit);
    }
}
