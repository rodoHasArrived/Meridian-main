using System.Text.Json;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Ledger;
using Meridian.Ledger;
using Meridian.Storage.AssetOperations;
using Meridian.Storage.SecurityMaster;
using Npgsql;

namespace Meridian.Storage.Ledger;

public sealed partial class PostgresLedgerJournalStore : IBondCouponRecognitionStore
{
    private static readonly JsonSerializerOptions CouponJson = new(JsonSerializerDefaults.Web);

    private static bool NeedsCouponHistoryBoundary(LedgerJournalEntryWrite entry)
        => entry.PostingCommand?.BondCouponRecognition is not null
            || entry.PostingCommand?.LotFactorPaydown is { NewFactor: 0m, ScheduledPrincipal: not null };

    private async Task LockCouponHistoryBoundaryAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        bool historySensitive, CancellationToken ct)
    {
        // Select the same highest mode at the outer boundary and AppendCore, before any read.
        // Ordinary writers coexist; coupon/terminal-principal history scans exclude them until
        // commit, including manual writes in other periods. No lock upgrade is needed, and LOCK
        // establishes no MVCC read snapshot, preserving existing serializable replay rules.
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"lock table {Qualified("journal_entries")} in {(historySensitive ? "share row exclusive" : "row exclusive")} mode";
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<BondCouponRecognitionReceiptDto?> GetLatestCouponRecognitionAsync(AccountCashForecastScopeDto scope,
        Guid taxLotRecordId, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await CheckCouponScopeAsync(connection, null, scope, ct).ConfigureAwait(false);
        return await ReadCouponReceiptAsync(connection, null, scope, taxLotRecordId, null, ct).ConfigureAwait(false);
    }

    public async Task<BondCouponRecognitionReceiptDto?> GetCouponRecognitionRunAsync(AccountCashForecastScopeDto scope,
        Guid taxLotRecordId, string runId, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await CheckCouponScopeAsync(connection, null, scope, ct).ConfigureAwait(false);
        return await ReadCouponReceiptAsync(connection, null, scope, taxLotRecordId, runId, ct).ConfigureAwait(false);
    }

    public async Task<decimal> GetConsumedCouponCashAsync(AccountCashForecastScopeDto scope, string paymentFlowId,
        CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await CheckCouponScopeAsync(connection, null, scope, ct).ConfigureAwait(false);
        return await CouponCashTotalAsync(connection, null, scope, paymentFlowId, ct).ConfigureAwait(false);
    }

    public async Task<decimal> GetConsumedCouponObligationAsync(AccountCashForecastScopeDto scope, Guid taxLotRecordId,
        string expectedFlowId, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await CheckCouponScopeAsync(connection, null, scope, ct).ConfigureAwait(false);
        return await CouponObligationTotalAsync(connection, null, scope, taxLotRecordId, expectedFlowId, ct).ConfigureAwait(false);
    }

    public async Task<BondCouponRecognitionReceiptDto?> GetCouponRecognitionReceiptAsync(AccountCashForecastScopeDto scope,
        Guid taxLotRecordId, string receiptId, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await CheckCouponScopeAsync(connection, null, scope, ct).ConfigureAwait(false);
        return await ReadCouponReceiptAsync(connection, null, scope, taxLotRecordId, null, ct, receiptId).ConfigureAwait(false);
    }

    public async Task<BondCouponAllocationConsumptionDto> GetConsumedCouponAllocationAsync(AccountCashForecastScopeDto scope,
        string allocationId, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await CheckCouponScopeAsync(connection, null, scope, ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            select coalesce(sum(net_amount),0),coalesce(sum(gross_amount),0),
                coalesce(sum(withholding_tax),0),coalesce(sum(fees),0)
            from {Qualified("bond_coupon_cash_consumption")}
            where ledger_book_id=@book and account_id=@account and allocation_id=@allocation
            """;
        command.Parameters.AddWithValue("book", Guid.Parse(scope.BookId));
        command.Parameters.AddWithValue("account", Guid.Parse(scope.AccountId));
        command.Parameters.AddWithValue("allocation", allocationId);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        await reader.ReadAsync(ct).ConfigureAwait(false);
        return new(reader.GetDecimal(0), reader.GetDecimal(1), reader.GetDecimal(2), reader.GetDecimal(3));
    }

    public async Task<IReadOnlyList<BondCouponObligationConsumptionDto>> GetConsumedCouponObligationsAsync(AccountCashForecastScopeDto scope,
        Guid taxLotRecordId, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await CheckCouponScopeAsync(connection, null, scope, ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            select expected_flow_id,sum(gross_amount) from {Qualified("bond_coupon_cash_consumption")}
            where ledger_book_id=@book and account_id=@account and tax_lot_record_id=@lot
            group by expected_flow_id having sum(gross_amount)<>0 order by expected_flow_id
            """;
        command.Parameters.AddWithValue("book", Guid.Parse(scope.BookId));
        command.Parameters.AddWithValue("account", Guid.Parse(scope.AccountId));
        command.Parameters.AddWithValue("lot", taxLotRecordId);
        var rows = new List<BondCouponObligationConsumptionDto>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            rows.Add(new(reader.GetString(0), reader.GetDecimal(1)));
        return rows;
    }

    private async Task CheckCouponScopeAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction,
        AccountCashForecastScopeDto scope, CancellationToken ct)
    {
        if (!Guid.TryParse(scope.BookId, out var book) || !Guid.TryParse(scope.AccountId, out _)
            || string.IsNullOrWhiteSpace(scope.TenantId) || string.IsNullOrWhiteSpace(scope.CompanyId)
            || _tenantScope.IsFailClosed && !string.Equals(scope.TenantId, ResolveCallerTenant(), StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Coupon receipts require exact authenticated account/book scope.");
        await EnsureTenantRowAsync(connection, transaction, "ledger_books", "ledger_book_id", book, false, ct).ConfigureAwait(false);
    }

    private async Task<BondCouponRecognitionReceiptDto?> ReadCouponReceiptAsync(NpgsqlConnection connection,
        NpgsqlTransaction? transaction, AccountCashForecastScopeDto scope, Guid lot, string? runId, CancellationToken ct,
        string? receiptId = null)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            select payload from {Qualified("bond_coupon_recognition")} r
            where ledger_book_id=@book and account_id=@account and tax_lot_record_id=@lot
            {(receiptId is not null ? "and receipt_id=@receipt" : runId is null ? $"and not exists(select 1 from {Qualified("bond_coupon_recognition")} child where child.previous_receipt_id=r.receipt_id)" : "and run_id=@run")}
            order by posted_at desc, receipt_id desc limit 1
            """;
        command.Parameters.AddWithValue("book", Guid.Parse(scope.BookId));
        command.Parameters.AddWithValue("account", Guid.Parse(scope.AccountId));
        command.Parameters.AddWithValue("lot", lot);
        if (runId is not null)
            command.Parameters.AddWithValue("run", runId);
        if (receiptId is not null)
            command.Parameters.AddWithValue("receipt", receiptId);
        if (await command.ExecuteScalarAsync(ct).ConfigureAwait(false) is not string json)
            return null;
        var receipt = JsonSerializer.Deserialize<BondCouponRecognitionReceiptDto>(json, CouponJson)
            ?? throw new InvalidDataException("Coupon recognition receipt is unreadable.");
        if (receipt.Instruction.Scope != scope || receipt.Instruction.IncomeInstruction.ExpectedLot.TaxLotRecordId != lot
            || receipt.ReceiptId != BondCouponRecognitionCalculator.Fingerprint(receipt.Instruction))
            throw new InvalidDataException("Coupon receipt scope or retained content identity is invalid.");
        return receipt;
    }

    private async Task<decimal> CouponCashTotalAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction,
        AccountCashForecastScopeDto scope, string version, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"select coalesce(sum(net_amount),0) from {Qualified("bond_coupon_cash_consumption")} where ledger_book_id=@book and account_id=@account and payment_flow_id=@version";
        command.Parameters.AddWithValue("book", Guid.Parse(scope.BookId));
        command.Parameters.AddWithValue("account", Guid.Parse(scope.AccountId));
        command.Parameters.AddWithValue("version", version);
        return (decimal)(await command.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
    }

    private async Task<decimal> CouponObligationTotalAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction,
        AccountCashForecastScopeDto scope, Guid lot, string flow, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"select coalesce(sum(gross_amount),0) from {Qualified("bond_coupon_cash_consumption")} where ledger_book_id=@book and account_id=@account and tax_lot_record_id=@lot and expected_flow_id=@flow";
        command.Parameters.AddWithValue("book", Guid.Parse(scope.BookId));
        command.Parameters.AddWithValue("account", Guid.Parse(scope.AccountId));
        command.Parameters.AddWithValue("lot", lot);
        command.Parameters.AddWithValue("flow", flow);
        return (decimal)(await command.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
    }

    private async Task ValidateAndRecordCouponRecognitionAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        LedgerJournalEntryWrite entry, CancellationToken ct)
    {
        if (entry.PostingCommand?.BondCouponRecognition is not { } instruction)
        {
            if (entry.PostingCommand?.EconomicEvent?.SourceDomain is "BondCouponRecognition" or "Meridian.BondCouponRecognition"
                || entry.PostingCommand?.ProjectionLineage?.ModelKey == BondCouponRecognitionCalculator.ModelVersion)
                throw new LedgerValidationException("Coupon source cannot omit its retained recognition instruction.");
            if ((entry.SourceJournalEntryId ?? entry.PostingCommand?.SourceJournalEntryId) is { } correctionSource)
            {
                await using var correctionCheck = connection.CreateCommand();
                correctionCheck.Transaction = transaction;
                correctionCheck.CommandText = $"select exists(select 1 from {Qualified("bond_coupon_recognition")} where journal_entry_id=@journal)";
                correctionCheck.Parameters.AddWithValue("journal", correctionSource);
                if (await correctionCheck.ExecuteScalarAsync(ct).ConfigureAwait(false) is true)
                    throw new LedgerValidationException("Posted coupon accrual and cash clearing require an explicit typed receipt reversal/rebook.");
            }
            return;
        }
        var posting = entry.PostingCommand;
        if (instruction.BookedTargets is not { PolicyVersion: BondIncomeBookedPrecision.PolicyVersion } targets
            || targets.BookedReceivableEarned != instruction.CumulativeReceivableEarned
            || targets.BookedPayableEarned != instruction.CumulativePayableEarned
            || targets.BookedReceivableEarned != BondIncomeBookedPrecision.RoundTarget(targets.RawReceivableEarned)
            || targets.BookedPayableEarned != BondIncomeBookedPrecision.RoundTarget(targets.RawPayableEarned)
            || targets.ReceivableRoundingResidual != targets.RawReceivableEarned - targets.BookedReceivableEarned
            || targets.PayableRoundingResidual != targets.RawPayableEarned - targets.BookedPayableEarned
            || instruction.CumulativeEarned != targets.BookedReceivableEarned - targets.BookedPayableEarned
            || entry.Entry.Lines.Any(line => line.Debit != BondIncomeBookedPrecision.RoundTarget(line.Debit)
                || line.Credit != BondIncomeBookedPrecision.RoundTarget(line.Credit)
                || line.Currency is { } currency && (currency.TransactionDebit != BondIncomeBookedPrecision.RoundTarget(currency.TransactionDebit)
                    || currency.TransactionCredit != BondIncomeBookedPrecision.RoundTarget(currency.TransactionCredit))))
            throw new LedgerValidationException("Coupon journals require exact adopted 10-decimal booked targets and retained raw rounding residuals.");
        if (transaction.IsolationLevel is not (System.Data.IsolationLevel.ReadCommitted or System.Data.IsolationLevel.Serializable))
            throw new LedgerValidationException("Coupon recognition requires ReadCommitted or Serializable isolation so locked source-cash consumption is current.");
        var lot = instruction.IncomeInstruction.ExpectedLot;
        if (posting.SourceEventType != AssetAccountingEventTypeNames.For(AssetAccountingEventKindDto.Income)
            || posting.ApprovalState != AccountingPostingApprovalStateDto.Approved
            || string.IsNullOrWhiteSpace(posting.ApprovalId) || posting.SourceEventId != instruction.EventId
            || posting.LedgerBookId != lot.LedgerBookId || posting.BookPositionId != lot.BookPositionId
            || entry.Entry.Metadata.EffectiveDate != instruction.Request.ThroughDate || entry.PeriodId != instruction.Request.PeriodId
            || (instruction.ReversalTarget is null ? entry.PostingKind != LedgerPostingKindDto.Originating || entry.SourceJournalEntryId is not null
                : entry.PostingKind != LedgerPostingKindDto.Adjustment || entry.SourceJournalEntryId != instruction.ReversalTarget.JournalEntryId)
            || instruction.ModelVersion != BondCouponRecognitionCalculator.ModelVersion
            || posting.LotAmortization is not null || posting.LotCorporateAction is not null || posting.LotFactorPaydown is not null)
            throw new LedgerValidationException("Coupon journals require independently approved canonical Income-event scope without a lot mutation.");
        await CheckCouponScopeAsync(connection, transaction, instruction.Scope, ct).ConfigureAwait(false);
        if (_backfillSecurityMaster?.Invoke() is not PostgresSecurityMasterStore securities
            || _backfillPositions?.Invoke() is not PostgresAssetOperationsProjectionStore positions)
            throw new LedgerValidationException("Coupon posting requires PostgreSQL reference and position authority.");
        var security = await securities.LockForLotPostingAsync(connection, transaction, lot.SecurityId, ct).ConfigureAwait(false);
        var position = await positions.LockForLotPostingAsync(connection, transaction, lot.BookPositionId, ct).ConfigureAwait(false);
        var lots = await LoadTaxLotsForUpdateAsync(connection, transaction, lot.LedgerBookId, [lot.TaxLotRecordId], ct).ConfigureAwait(false);
        if (security is null || instruction.ReversalTarget is null && instruction.TerminalCashAuthority is null
                && !OpenLotAmortization.SameSecurityHash(security, instruction.IncomeInstruction.Security)
            || position?.Version != instruction.IncomeInstruction.ExpectedBookPositionVersion
            || lots.Count != 1 || !OpenLotAmortization.SameLot(lots[0].ToOpenLot(), lot))
            throw new LedgerValidationException("Coupon lot, position or reference inputs changed after review.");
        var fingerprint = BondCouponRecognitionCalculator.Fingerprint(instruction);
        var existing = await ReadCouponReceiptAsync(connection, transaction, instruction.Scope, lot.TaxLotRecordId, instruction.Request.RunId, ct).ConfigureAwait(false);
        if (existing is not null)
        {
            if (existing.ReceiptId != fingerprint || existing.JournalEntryId != entry.Entry.JournalEntryId)
                throw new LedgerValidationException("Coupon recognition run already names another immutable journal or input.");
            return;
        }
        var authority = _couponRecognitionAuthority?.Invoke()
            ?? throw new LedgerValidationException("Coupon posting requires retained-income and observed-cash recomputation.");
        var source = posting.Evidence.SingleOrDefault(e => e.Kind == AccountingPostingEvidenceKindDto.Source
            && e.SubjectType == AssetAccountingEvidenceSubjects.Event && e.SubjectId == instruction.EventId.ToString("D"))
            ?? throw new LedgerValidationException("Coupon posting requires the exact retained operator-reviewed calculation source.");
        var retainedSource = new RetainedEvidenceIdentityDto(source.EvidenceId, source.Uri, source.ContentHash ?? "",
            source.SourceSystem, source.SourceReference ?? "", source.ReviewStatus ?? "", source.Reviewer ?? "",
            source.ReviewedAtUtc ?? default, source.EffectiveDate ?? default, source.EvidenceVersion ?? 0,
            source.RetainedAtUtc, source.RetainedBy, source.SubjectType ?? "", source.SubjectId ?? "");
        await authority.VerifyPreparedSourceAsync(instruction, retainedSource, ct).ConfigureAwait(false);
        var previous = await ReadCouponReceiptAsync(connection, transaction, instruction.Scope, lot.TaxLotRecordId, null, ct).ConfigureAwait(false);
        if (previous?.ReceiptId != instruction.PreviousReceiptId)
            throw new LedgerValidationException("Another coupon recognition has posted; rebuild the incremental candidate.");
        var expectedLines = instruction.Lines.Select(line => (Account: instruction.Accounts.Single(a => a.Path == line.AccountId).LedgerName,
            Type: instruction.Accounts.Single(a => a.Path == line.AccountId).AccountType, Debit: line.Debit, Credit: line.Credit)).OrderBy(line => line.Account, StringComparer.Ordinal).ToArray();
        var actualLines = entry.Entry.Lines.Select(line => (Account: line.Account.Name, Type: line.Account.AccountType.ToString(), line.Debit, line.Credit))
            .OrderBy(line => line.Account, StringComparer.Ordinal).ToArray();
        if (!expectedLines.SequenceEqual(actualLines) || entry.Entry.Lines.Any(line => line.Dimensions?.InstrumentId != lot.SecurityId
            || line.Dimensions.PositionId != lot.BookPositionId || line.Dimensions.AccountId != instruction.Scope.AccountId
            || line.Dimensions.BookId != instruction.Scope.BookId || line.Dimensions.TaxLotId != lot.LotId
            || line.Account.Symbol is not null
            || !string.Equals(line.Account.FinancialAccountId, instruction.Scope.AccountId, StringComparison.OrdinalIgnoreCase)
            || line.Currency is { } currency && (currency.TransactionCurrency != instruction.Scope.BaseCurrency
                || currency.FunctionalCurrency != instruction.Scope.BaseCurrency || currency.FxRateToFunctional != 1m)))
            throw new LedgerValidationException("Coupon journal lines differ from the exact adopted account mappings and signed deltas.");
        var cashClaims = instruction.ReversalTarget?.Instruction.Cash ?? instruction.Cash;
        var direction = instruction.ReversalTarget is null ? 1m : -1m;
        foreach (var allocationId in cashClaims.Select(c => c.Allocation.AllocationId).Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal))
        {
            await using var allocationLock = connection.CreateCommand();
            allocationLock.Transaction = transaction;
            allocationLock.CommandText = "select pg_advisory_xact_lock(hashtextextended(@key,0))";
            allocationLock.Parameters.AddWithValue("key", $"bond-coupon-allocation:{instruction.Scope.BookId}:{instruction.Scope.AccountId}:{allocationId}");
            await allocationLock.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        foreach (var group in cashClaims.GroupBy(c => c.ObservedPayment.FlowId).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            await using var sourceLock = connection.CreateCommand();
            sourceLock.Transaction = transaction;
            sourceLock.CommandText = "select pg_advisory_xact_lock(hashtextextended(@key,0))";
            sourceLock.Parameters.AddWithValue("key", $"bond-coupon:{instruction.Scope.BookId}:{instruction.Scope.AccountId}:{group.Key}");
            await sourceLock.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            var consumed = await CouponCashTotalAsync(connection, transaction, instruction.Scope, group.Key, ct).ConfigureAwait(false);
            var delta = direction * group.Sum(c => c.Allocation.NetAmount);
            if (direction > 0m)
            {
                if (group.Select(c => c.ObservedPaymentAmount).Distinct().Count() != 1
                    || consumed != 0m && Math.Sign(consumed) != Math.Sign(group.First().ObservedPaymentAmount)
                    || Math.Abs(consumed + delta) > Math.Abs(group.First().ObservedPaymentAmount))
                    throw new LedgerValidationException("Observed coupon cash is already consumed or overallocated.");
            }
            else if (Math.Abs(consumed + delta) > Math.Abs(consumed)
                || consumed + delta != 0m && Math.Sign(consumed + delta) != Math.Sign(consumed))
                throw new LedgerValidationException("Coupon reversal cannot release unconsumed source cash.");
            foreach (var claim in group)
            {
                await using var used = connection.CreateCommand();
                used.Transaction = transaction;
                used.CommandText = $"""
                    select coalesce(sum(net_amount),0),coalesce(sum(gross_amount),0),
                        coalesce(sum(withholding_tax),0),coalesce(sum(fees),0)
                    from {Qualified("bond_coupon_cash_consumption")}
                    where ledger_book_id=@book and account_id=@account and allocation_id=@allocation
                    """;
                used.Parameters.AddWithValue("book", lot.LedgerBookId);
                used.Parameters.AddWithValue("account", Guid.Parse(instruction.Scope.AccountId));
                used.Parameters.AddWithValue("allocation", claim.Allocation.AllocationId);
                decimal usedNet, usedGross, usedTax, usedFees;
                await using (var reader = await used.ExecuteReaderAsync(ct).ConfigureAwait(false))
                {
                    await reader.ReadAsync(ct).ConfigureAwait(false);
                    usedNet = reader.GetDecimal(0);
                    usedGross = reader.GetDecimal(1);
                    usedTax = reader.GetDecimal(2);
                    usedFees = reader.GetDecimal(3);
                }
                var shared = claim.RetainedAllocation;
                if (direction > 0m)
                {
                    if (!BondCouponRecognitionCalculator.IsAvailableShare(claim.Allocation, shared,
                        new(usedNet, usedGross, usedTax, usedFees)))
                        throw new LedgerValidationException("Coupon lot shares exceed the retained source net, gross, tax or fee allocation.");
                    used.CommandText = $"""
                        select exists(select 1 from {Qualified("bond_coupon_cash_consumption")}
                        where ledger_book_id=@book and account_id=@account and allocation_id=@allocation
                        group by payment_flow_id,expected_flow_id having sum(net_amount) <> 0
                        and (payment_flow_id <> @flow or expected_flow_id <> @expected))
                        """;
                    used.Parameters.AddWithValue("flow", claim.ObservedPayment.FlowId);
                    used.Parameters.AddWithValue("expected", shared.ExpectedFlowId);
                    if (await used.ExecuteScalarAsync(ct).ConfigureAwait(false) is true)
                        throw new LedgerValidationException("An active coupon allocation must be reversed before remapping its bank source or obligation.");
                }
                else if (!Within(claim.Allocation.NetAmount, usedNet) || !Within(claim.Allocation.GrossAmount, usedGross)
                    || claim.Allocation.WithholdingTax > usedTax || claim.Allocation.Fees > usedFees)
                    throw new LedgerValidationException("Coupon reversal cannot release an unavailable source allocation share.");
            }
        }
        var receipt = new BondCouponRecognitionReceiptDto(fingerprint, entry.Entry.JournalEntryId, entry.Entry.Timestamp, instruction);
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = $"""
                insert into {Qualified("bond_coupon_recognition")}(receipt_id,ledger_book_id,account_id,tax_lot_record_id,run_id,previous_receipt_id,reverses_receipt_id,journal_entry_id,posted_at,payload)
                values(@id,@book,@account,@lot,@run,@previous,@reverses,@journal,@at,@payload)
                """;
            command.Parameters.AddWithValue("id", fingerprint);
            command.Parameters.AddWithValue("book", lot.LedgerBookId);
            command.Parameters.AddWithValue("account", Guid.Parse(instruction.Scope.AccountId));
            command.Parameters.AddWithValue("lot", lot.TaxLotRecordId);
            command.Parameters.AddWithValue("run", instruction.Request.RunId);
            command.Parameters.AddWithValue("previous", instruction.PreviousReceiptId is { } parent ? (object)parent : DBNull.Value);
            command.Parameters.AddWithValue("reverses", instruction.ReversalTarget is { } reversed ? (object)reversed.ReceiptId : DBNull.Value);
            command.Parameters.AddWithValue("journal", entry.Entry.JournalEntryId);
            command.Parameters.AddWithValue("at", entry.Entry.Timestamp);
            command.Parameters.AddWithValue("payload", JsonSerializer.Serialize(receipt, CouponJson));
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        foreach (var cash in cashClaims)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"insert into {Qualified("bond_coupon_cash_consumption")}(ledger_book_id,account_id,payment_version_id,payment_flow_id,tax_lot_record_id,expected_flow_id,allocation_id,receipt_id,net_amount,gross_amount,withholding_tax,fees) values(@book,@account,@version,@flow,@lot,@expected,@allocation,@receipt,@net,@gross,@tax,@fees)";
            command.Parameters.AddWithValue("book", lot.LedgerBookId);
            command.Parameters.AddWithValue("account", Guid.Parse(instruction.Scope.AccountId));
            command.Parameters.AddWithValue("version", cash.Allocation.PaymentVersionId);
            command.Parameters.AddWithValue("flow", cash.ObservedPayment.FlowId);
            command.Parameters.AddWithValue("allocation", cash.Allocation.AllocationId);
            command.Parameters.AddWithValue("receipt", fingerprint);
            command.Parameters.AddWithValue("net", direction * cash.Allocation.NetAmount);
            command.Parameters.AddWithValue("lot", lot.TaxLotRecordId);
            command.Parameters.AddWithValue("expected", cash.RetainedAllocation.ExpectedFlowId);
            command.Parameters.AddWithValue("gross", direction * cash.Allocation.GrossAmount);
            command.Parameters.AddWithValue("tax", direction * cash.Allocation.WithholdingTax);
            command.Parameters.AddWithValue("fees", direction * cash.Allocation.Fees);
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }

    private static bool Within(decimal value, decimal limit)
        => value == 0m || Math.Sign(value) == Math.Sign(limit) && Math.Abs(value) <= Math.Abs(limit);
}
