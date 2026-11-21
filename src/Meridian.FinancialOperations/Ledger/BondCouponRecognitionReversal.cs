using System.Text.Json;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Ledger;
using Meridian.Storage.AssetOperations;
using Meridian.Storage.Ledger;

namespace Meridian.FinancialOperations.Ledger;

internal static class BondCouponRecognitionReversal
{
    internal static async Task<BondCouponRecognitionPreviewDto> PreviewAsync(ILedgerJournalStore ledger,
        IBondCouponRecognitionStore receipts, IInstrumentPositionProjectionStore positions,
        AccountCashForecastScopeDto scope, BondCouponRecognitionRequest request, CancellationToken ct)
    {
        if (request.ReversesReceiptId is null || string.IsNullOrWhiteSpace(request.CorrectionRationale)
            || request.CashAllocations.Count != 0 || request.KnownAt == default || request.KnownAt.Offset != TimeSpan.Zero
            || request.ThroughDate == default || request.ThroughDate > DateOnly.FromDateTime(request.KnownAt.UtcDateTime))
            throw new ArgumentException("Coupon reversal requires an exact posted receipt, correction rationale and open-period date without new cash allocations.");
        var target = await receipts.GetCouponRecognitionReceiptAsync(scope, request.TaxLotRecordId, request.ReversesReceiptId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The original posted coupon receipt is unavailable in this account/book.");
        var latest = await receipts.GetLatestCouponRecognitionAsync(scope, request.TaxLotRecordId, ct).ConfigureAwait(false);
        var active = await LatestActiveAsync(receipts, scope, request.TaxLotRecordId, latest, ct).ConfigureAwait(false);
        if (active?.ReceiptId != target.ReceiptId || latest is null || target.Instruction.ReversalTarget is not null
            || latest.PostedAtUtc > request.KnownAt || target.Instruction.Scope != scope)
            throw new InvalidOperationException("Only the latest unreversed coupon receipt can be reversed; unwind later recognition first.");
        var book = await ledger.GetLedgerBookAsync(Guid.Parse(scope.BookId), ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The reversal ledger book is unavailable.");
        var period = await ledger.GetPeriodAsync(request.PeriodId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The reversal accounting period is unavailable.");
        if (period.LedgerBookId != book.LedgerBookId || period.Status != "Open" || request.ThroughDate < period.StartDate
            || request.ThroughDate > period.EndDate || book.BaseCurrency != scope.BaseCurrency)
            throw new InvalidOperationException("Coupon reversal requires an authorized open period in the original book and currency.");
        var originals = await ledger.QueryAsync(new LedgerJournalEntryQuery(JournalEntryId: target.JournalEntryId,
            LedgerBookId: book.LedgerBookId), ct).ConfigureAwait(false);
        var original = originals.SingleOrDefault()
            ?? throw new InvalidOperationException("The original immutable coupon journal is unavailable.");
        if (original.SourceEventId != target.Instruction.EventId || original.PeriodId != target.Instruction.Request.PeriodId)
            throw new InvalidDataException("Coupon reversal receipt does not identify its exact posted journal/event.");
        var expected = target.Instruction.Lines.Select(line => (Name: target.Instruction.Accounts.Single(a => a.Path == line.AccountId).LedgerName,
            line.Debit, line.Credit)).OrderBy(line => line.Name, StringComparer.Ordinal).ToArray();
        var actual = original.Entry.Lines.Select(line => (Name: line.Account.Name, line.Debit, line.Credit))
            .OrderBy(line => line.Name, StringComparer.Ordinal).ToArray();
        if (!expected.SequenceEqual(actual))
            throw new InvalidDataException("Original coupon journal lines differ from the retained exact recognition.");
        var current = (await ledger.GetTaxLotsByIdsAsync(book.LedgerBookId, [request.TaxLotRecordId], ct).ConfigureAwait(false)).SingleOrDefault()
            ?? throw new InvalidOperationException("The reversal lot authority is unavailable.");
        var previous = target.Instruction.IncomeInstruction.ExpectedLot;
        if (current.SecurityId != previous.SecurityId || current.BookPositionId != previous.BookPositionId
            || !Guid.TryParse(current.Account.FinancialAccountId, out var financial) || financial.ToString("D") != scope.AccountId
            || JsonSerializer.Serialize(current.Acquisition?.BondIncome) != JsonSerializer.Serialize(previous.Acquisition.BondIncome))
            throw new InvalidOperationException("Coupon reversal must retain the original security/account and immutable acquisition facts.");
        var position = await positions.GetBookPositionAsync(current.BookPositionId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The reversal book position is unavailable.");
        if (position.SecurityId != current.SecurityId || position.BookContext.LedgerBookId != book.LedgerBookId)
            throw new InvalidOperationException("Coupon reversal position no longer belongs to the original security/book.");
        var terminal = current.OpenQuantity == 0m
            ? await BondCouponClosedLotAuthority.ResolveAsync(ledger, receipts, positions, scope, request,
                current, latest, ct).ConfigureAwait(false) : null;
        await BondCouponRecognitionOpeningGuard.ValidateRecognizedHistoryAsync(ledger, receipts, scope,
            current.ToOpenLot(), latest.Instruction.AccountMappings
                ?? throw new InvalidOperationException("Coupon reversal requires explicit retained account mappings and opening reconciliation."),
            latest.Instruction.Accounts, latest, terminal, request.KnownAt, ct).ConfigureAwait(false);
        var identity = Sha256Digest.ComputeBytesUtf8($"{scope.TenantId}/{scope.CompanyId}/{scope.BookId}/{scope.AccountId}/{request.TaxLotRecordId:D}/{request.RunId}");
        var source = target.Instruction;
        if (source.BookedTargets is not { PolicyVersion: BondIncomeBookedPrecision.PolicyVersion } precision
            || latest.Instruction.BookedTargets is not { PolicyVersion: BondIncomeBookedPrecision.PolicyVersion } latestPrecision)
            throw new InvalidOperationException("Coupon reversal requires retained booked-precision targets; reconcile legacy recognition first.");
        var instruction = new BondCouponRecognitionInstructionDto(BondCouponRecognitionCalculator.ModelVersion, scope, request,
            source.IncomeInstruction with { ExpectedLot = current.ToOpenLot(), ExpectedBookPositionVersion = position.Version },
            new Guid(identity.AsSpan(0, 16)), latest.ReceiptId, source.PreviousEarned, latest.Instruction.CumulativeEarned,
            source.PriorGrossSettled, [], source.Accounts,
            source.Lines.Select(line => line with { Debit = line.Credit, Credit = line.Debit }).ToArray())
        {
            CumulativeReceivableEarned = source.PreviousReceivableEarned,
            CumulativePayableEarned = source.PreviousPayableEarned,
            PreviousReceivableEarned = latest.Instruction.CumulativeReceivableEarned,
            PreviousPayableEarned = latest.Instruction.CumulativePayableEarned,
            PriorReceivableSettled = source.PriorReceivableSettled,
            PriorPayableSettled = source.PriorPayableSettled,
            AccountMappings = source.AccountMappings,
            ReversalTarget = target,
            BookedTargets = new(BondIncomeBookedPrecision.PolicyVersion,
                precision.PreviousRawReceivableEarned, source.PreviousReceivableEarned,
                precision.PreviousRawReceivableEarned - source.PreviousReceivableEarned,
                precision.PreviousRawPayableEarned, source.PreviousPayableEarned,
                precision.PreviousRawPayableEarned - source.PreviousPayableEarned,
                latestPrecision.RawReceivableEarned, latestPrecision.RawPayableEarned),
            TerminalCashAuthority = terminal is not null && (source.CumulativeReceivableEarned != source.PreviousReceivableEarned
                || source.CumulativePayableEarned != source.PreviousPayableEarned)
                ? terminal with { Mode = "ReverseFrozenEarnings" } : terminal
        };
        return BondCouponRecognitionService.BuildPreview(instruction);
    }

    private static async Task<BondCouponRecognitionReceiptDto?> LatestActiveAsync(IBondCouponRecognitionStore receipts,
        AccountCashForecastScopeDto scope, Guid lot, BondCouponRecognitionReceiptDto? cursor, CancellationToken ct)
    {
        var reversed = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (cursor is not null)
        {
            if (!visited.Add(cursor.ReceiptId) || visited.Count > 10000)
                throw new InvalidDataException("Coupon correction receipt lineage contains a cycle or exceeds its review bound.");
            if (cursor.Instruction.ReversalTarget is { } inverse)
                reversed.Add(inverse.ReceiptId);
            else if (!reversed.Contains(cursor.ReceiptId))
                return cursor;
            if (cursor.Instruction.PreviousReceiptId is not { } previous)
                return null;
            cursor = await receipts.GetCouponRecognitionReceiptAsync(scope, lot, previous, ct).ConfigureAwait(false)
                ?? throw new InvalidDataException("Coupon correction receipt lineage has a missing predecessor.");
        }
        return null;
    }
}
