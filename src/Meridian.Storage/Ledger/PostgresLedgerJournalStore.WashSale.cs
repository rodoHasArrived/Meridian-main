using Meridian.Ledger;
using Npgsql;

namespace Meridian.Storage.Ledger;

/// <summary>
/// Wash-sale activation: resolves the replacement acquisitions the relief engine needs and retains
/// the deferrals it produces. Without this the engine's policy gate is never satisfied in
/// production, so every realized loss is recognized in full regardless of a repurchase.
/// </summary>
public sealed partial class PostgresLedgerJournalStore : IWashSaleReplacementResolver, IWashSaleDeferralStore
{
    /// <inheritdoc />
    public async Task<WashSaleReplacementLookup> ResolveAsync(
        WashSaleReplacementQuery query,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.LedgerBookId == Guid.Empty)
        {
            throw new ArgumentException("Ledger book id is required.", nameof(query));
        }

        // A policy that does not govern this sale date must not change the numbers the sale would
        // otherwise report, so the search is skipped rather than run and discarded.
        if (!query.Policy.AppliesOn(query.SaleDate))
        {
            return WashSaleReplacementLookup.Empty;
        }

        // Matching requires an unambiguous "substantially identical" identity. An unidentified
        // security cannot supply one, so no deferral is asserted rather than guessing by symbol.
        if (query.SecurityId == Guid.Empty)
        {
            return WashSaleReplacementLookup.Empty;
        }

        query.Policy.EnsureValid();

        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        var replacements = await LoadReplacementAcquisitionsAsync(connection, query, ct).ConfigureAwait(false);
        var priorDeferrals = await LoadPriorDeferralAdjustmentsAsync(connection, query, ct).ConfigureAwait(false);
        return new WashSaleReplacementLookup(replacements, priorDeferrals);
    }

    private async Task<IReadOnlyList<WashSaleReplacementAcquisition>> LoadReplacementAcquisitionsAsync(
        NpgsqlConnection connection,
        WashSaleReplacementQuery query,
        CancellationToken ct)
    {
        // Scope predicate is composed rather than branched into two queries so the window, security,
        // and self-exclusion rules cannot drift between the two scopes.
        var accountScoped = query.Policy.Scope == WashSaleReplacementScope.DisposingAccount;
        var scopePredicate = accountScoped
            ? "and account_name = @account_name "
                + "and account_type = @account_type "
                + "and symbol is not distinct from @symbol "
                + "and financial_account_id is not distinct from @financial_account_id"
            : string.Empty;

        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select lot_id,
                   acquired_date,
                   original_quantity,
                   security_id,
                   account_name,
                   account_type,
                   symbol,
                   financial_account_id
            from {Qualified("tax_lots")}
            where ledger_book_id = @ledger_book_id
              and security_id = @security_id
              and acquired_date >= @window_start
              and acquired_date <= @window_end
              and original_quantity > 0
              and lower(lot_id) <> all(@relieved_lot_ids)
            {scopePredicate}
            order by acquired_date, lot_id;
            """;
        command.Parameters.AddWithValue("ledger_book_id", query.LedgerBookId);
        command.Parameters.AddWithValue("security_id", query.SecurityId);
        command.Parameters.AddWithValue("window_start", query.SaleDate.AddDays(-query.Policy.WindowDays));
        command.Parameters.AddWithValue("window_end", query.SaleDate.AddDays(query.Policy.WindowDays));
        command.Parameters.AddWithValue("relieved_lot_ids", NormalizeLotIds(query.RelievedLotIds));
        if (accountScoped)
        {
            AddAccountParameters(command, query.DisposingAccount);
        }

        var replacements = new List<WashSaleReplacementAcquisition>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            replacements.Add(new WashSaleReplacementAcquisition(
                reader.GetString(0),
                DateOnly.FromDateTime(reader.GetDateTime(1)),
                // Replacement magnitude is the quantity *acquired*, not what remains open: a lot
                // bought and partly sold again inside the window still replaced the loss shares.
                reader.GetDecimal(2),
                reader.IsDBNull(3) ? null : reader.GetGuid(3),
                ReadLedgerAccount(reader, 4)));
        }

        return replacements;
    }

    private async Task<IReadOnlyList<LedgerTaxLotBasisAdjustment>> LoadPriorDeferralAdjustmentsAsync(
        NpgsqlConnection connection,
        WashSaleReplacementQuery query,
        CancellationToken ct)
    {
        var relievedLotIds = NormalizeLotIds(query.RelievedLotIds);
        if (relievedLotIds.Length == 0)
        {
            return [];
        }

        // Deferrals previously capitalized into the lots this disposal is about to relieve. Replaying
        // them as basis adjustments is what finally recognizes a deferred loss: the replacement is
        // relieved at its increased basis and with the holding period it inherited, rather than at
        // the raw price it was bought for.
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select deferral.deferral_id,
                   deferral.replacement_lot_id,
                   deferral.disallowed_amount,
                   deferral.sale_date,
                   deferral.holding_period_carry_date,
                   deferral.security_id
            from {Qualified("wash_sale_deferrals")} deferral
            join {Qualified("tax_lots")} lot
              on lot.tax_lot_record_id = deferral.replacement_tax_lot_record_id
            where deferral.ledger_book_id = @ledger_book_id
              and deferral.security_id = @security_id
              and lower(lot.lot_id) = any(@relieved_lot_ids)
            order by deferral.sale_date, deferral.deferral_id;
            """;
        command.Parameters.AddWithValue("ledger_book_id", query.LedgerBookId);
        command.Parameters.AddWithValue("security_id", query.SecurityId);
        command.Parameters.AddWithValue("relieved_lot_ids", relievedLotIds);

        var adjustments = new List<LedgerTaxLotBasisAdjustment>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            adjustments.Add(new LedgerTaxLotBasisAdjustment(
                LedgerTaxLotBasisAdjustmentKind.WashSale,
                reader.GetDecimal(2),
                DateOnly.FromDateTime(reader.GetDateTime(3)),
                reader.GetGuid(5),
                reader.GetString(1),
                $"wash-sale-deferral:{reader.GetGuid(0):D}",
                DateOnly.FromDateTime(reader.GetDateTime(4)))
                .EnsureValid());
        }

        return adjustments;
    }

    /// <inheritdoc />
    public async Task SaveWashSaleDeferralsAsync(
        IReadOnlyList<WashSaleDeferralRecord> deferrals,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(deferrals);
        if (deferrals.Count == 0)
        {
            return;
        }

        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        foreach (var deferral in deferrals)
        {
            ValidateWashSaleDeferral(deferral);
            await InsertWashSaleDeferralAsync(connection, transaction, deferral, ct).ConfigureAwait(false);
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);
    }

    private async Task InsertWashSaleDeferralAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        WashSaleDeferralRecord deferral,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            insert into {Qualified("wash_sale_deferrals")} (
                deferral_id,
                ledger_book_id,
                disposal_mutation_batch_id,
                security_id,
                sale_date,
                disposal_account_name,
                disposal_account_type,
                disposal_symbol,
                disposal_financial_account_id,
                replacement_tax_lot_record_id,
                replacement_lot_id,
                disallowed_amount,
                holding_period_carry_date,
                policy_id,
                window_days,
                scope,
                recorded_at)
            values (
                @deferral_id,
                @ledger_book_id,
                @disposal_mutation_batch_id,
                @security_id,
                @sale_date,
                @account_name,
                @account_type,
                @symbol,
                @financial_account_id,
                @replacement_tax_lot_record_id,
                @replacement_lot_id,
                @disallowed_amount,
                @holding_period_carry_date,
                @policy_id,
                @window_days,
                @scope,
                @recorded_at)
            on conflict (disposal_mutation_batch_id, replacement_tax_lot_record_id) do nothing;
            """;
        command.Parameters.AddWithValue("deferral_id", deferral.DeferralId);
        command.Parameters.AddWithValue("ledger_book_id", deferral.LedgerBookId);
        command.Parameters.AddWithValue("disposal_mutation_batch_id", deferral.DisposalMutationBatchId);
        command.Parameters.AddWithValue("security_id", deferral.SecurityId);
        command.Parameters.AddWithValue("sale_date", deferral.SaleDate);
        AddAccountParameters(command, deferral.DisposalAccount);
        command.Parameters.AddWithValue("replacement_tax_lot_record_id", deferral.ReplacementTaxLotRecordId);
        command.Parameters.AddWithValue("replacement_lot_id", deferral.ReplacementLotId.Trim());
        command.Parameters.AddWithValue("disallowed_amount", deferral.DisallowedAmount);
        command.Parameters.AddWithValue("holding_period_carry_date", deferral.HoldingPeriodCarryDate);
        command.Parameters.AddWithValue("policy_id", RequireLineageText(deferral.PolicyId, nameof(deferral.PolicyId)));
        command.Parameters.AddWithValue("window_days", deferral.WindowDays);
        command.Parameters.AddWithValue("scope", deferral.Scope.ToString());
        command.Parameters.AddWithValue("recorded_at", deferral.RecordedAt.UtcDateTime);

        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<WashSaleDeferralRecord>> ListWashSaleDeferralsAsync(
        Guid ledgerBookId,
        DateOnly fromSaleDate,
        DateOnly toSaleDate,
        CancellationToken ct = default)
    {
        if (ledgerBookId == Guid.Empty)
        {
            throw new ArgumentException("Ledger book id is required.", nameof(ledgerBookId));
        }

        if (toSaleDate < fromSaleDate)
        {
            throw new ArgumentException(
                "The deferral sale-date range end cannot precede its start.",
                nameof(toSaleDate));
        }

        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select deferral_id,
                   ledger_book_id,
                   disposal_mutation_batch_id,
                   security_id,
                   sale_date,
                   disposal_account_name,
                   disposal_account_type,
                   disposal_symbol,
                   disposal_financial_account_id,
                   replacement_tax_lot_record_id,
                   replacement_lot_id,
                   disallowed_amount,
                   holding_period_carry_date,
                   policy_id,
                   window_days,
                   scope,
                   recorded_at
            from {Qualified("wash_sale_deferrals")}
            where ledger_book_id = @ledger_book_id
              and sale_date >= @from_sale_date
              and sale_date <= @to_sale_date
            order by sale_date, disposal_mutation_batch_id, replacement_lot_id;
            """;
        command.Parameters.AddWithValue("ledger_book_id", ledgerBookId);
        command.Parameters.AddWithValue("from_sale_date", fromSaleDate);
        command.Parameters.AddWithValue("to_sale_date", toSaleDate);

        var deferrals = new List<WashSaleDeferralRecord>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            deferrals.Add(new WashSaleDeferralRecord(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetGuid(2),
                reader.GetGuid(3),
                DateOnly.FromDateTime(reader.GetDateTime(4)),
                ReadLedgerAccount(reader, 5),
                reader.GetGuid(9),
                reader.GetString(10),
                reader.GetDecimal(11),
                DateOnly.FromDateTime(reader.GetDateTime(12)),
                reader.GetString(13),
                reader.GetInt32(14),
                Enum.Parse<WashSaleReplacementScope>(reader.GetString(15), ignoreCase: true),
                ReadUtcDateTimeOffset(reader, 16)));
        }

        return deferrals;
    }

    private static void ValidateWashSaleDeferral(WashSaleDeferralRecord deferral)
    {
        ArgumentNullException.ThrowIfNull(deferral);
        if (deferral.DeferralId == Guid.Empty)
        {
            throw new ArgumentException("Deferral id is required.", nameof(deferral));
        }

        if (deferral.LedgerBookId == Guid.Empty)
        {
            throw new ArgumentException("Ledger book id is required.", nameof(deferral));
        }

        if (deferral.DisposalMutationBatchId == Guid.Empty)
        {
            throw new ArgumentException("Disposal mutation batch id is required.", nameof(deferral));
        }

        if (deferral.SecurityId == Guid.Empty)
        {
            throw new ArgumentException(
                "A wash-sale deferral must identify the security it defers a loss on.",
                nameof(deferral));
        }

        if (deferral.ReplacementTaxLotRecordId == Guid.Empty)
        {
            throw new ArgumentException(
                "A wash-sale deferral must identify the replacement lot whose basis absorbed the loss.",
                nameof(deferral));
        }

        if (string.IsNullOrWhiteSpace(deferral.ReplacementLotId))
        {
            throw new ArgumentException("Replacement lot identifier is required.", nameof(deferral));
        }

        if (deferral.DisallowedAmount <= 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(deferral),
                deferral.DisallowedAmount,
                "A retained wash-sale deferral must carry a positive disallowed amount.");
        }

        if (deferral.WindowDays < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(deferral),
                deferral.WindowDays,
                "Wash-sale window days cannot be negative.");
        }
    }

    /// <summary>
    /// Lower-cases and de-duplicates lot identifiers for SQL array comparison. Lot identity is
    /// compared case-insensitively everywhere else in relief, so the database predicate has to
    /// agree or a replacement would match itself under a different casing.
    /// </summary>
    private static string[] NormalizeLotIds(IReadOnlyList<string>? lotIds)
        => lotIds is null
            ? []
            : lotIds
                .Where(static lotId => !string.IsNullOrWhiteSpace(lotId))
                .Select(static lotId => lotId.Trim().ToLowerInvariant())
                .Distinct(StringComparer.Ordinal)
                .ToArray();
}
