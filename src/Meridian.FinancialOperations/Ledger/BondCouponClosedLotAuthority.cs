using System.Text.Json;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Ledger;
using Meridian.Storage.AssetOperations;
using Meridian.Storage.Ledger;

namespace Meridian.FinancialOperations.Ledger;

/// <summary>Reopens the exact principal and earned-coupon receipts; never accrues on zero holdings.</summary>
internal static class BondCouponClosedLotAuthority
{
    internal const string ModelVersion = "bond-terminal-coupon-cash-v1";

    internal static async Task<BondCouponTerminalCashAuthorityDto> ResolveAsync(ILedgerJournalStore ledger,
        IBondCouponRecognitionStore receipts, IInstrumentPositionProjectionStore positions,
        AccountCashForecastScopeDto scope, BondCouponRecognitionRequest request, LedgerTaxLotRecord physical,
        BondCouponRecognitionReceiptDto? previous, CancellationToken ct)
    {
        if (previous is not null && (previous.Instruction.Scope != scope || previous.PostedAtUtc > request.KnownAt)
            || request.KnownAt == default || request.KnownAt.Offset != TimeSpan.Zero
            || request.ThroughDate == default || request.ThroughDate > DateOnly.FromDateTime(request.KnownAt.UtcDateTime)
            || physical.TaxLotRecordId != request.TaxLotRecordId || physical.OpenQuantity != 0m || physical.CurrentFactor != 0m
            || physical.LedgerBookId.ToString("D") != scope.BookId
            || !Guid.TryParse(physical.Account.FinancialAccountId, out var owner) || owner.ToString("D") != scope.AccountId
            || physical.Acquisition is not { Direction: OpenLotDirection.Long, QuantityBasis: LotQuantityBasis.Face, BondIncome: not null }
            || physical.LastMutationBatchId is not { } batchId || batchId == Guid.Empty
            || physical.BasisAdjustment is not { Reason: OpenLotBasisAdjustmentReasons.FactorPaydown, FactorPaydown: { } retainedPrincipal }
            || physical.BasisAdjustment.MutationBatchId != batchId)
            throw new InvalidOperationException("Closed coupon cash requires exact governed terminal-principal and frozen earned/opening authority.");
        var current = physical.ToOpenLot();
        if (current.OpenTransactionCostBasis != 0m || current.OpenFunctionalCostBasis != 0m)
            throw new InvalidOperationException("Terminal coupon clearing cannot alter or conceal residual closed-lot basis.");
        var closure = await ledger.GetAtomicTaxLotPostingAsync(batchId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The retained terminal-principal posting is unavailable.");
        if (closure.MutationBatchId != batchId || closure.MutationKind != AtomicTaxLotMutationKind.FactorPaydown
            || closure.FactorPaydown is not { NewFactor: 0m, ScheduledPrincipal: { } scheduled } principal
            || scheduled.ModelVersion != BondIncomeScheduledPrincipal.ModelVersion
            || JsonSerializer.Serialize(principal) != JsonSerializer.Serialize(retainedPrincipal)
            || principal.Scope.LedgerBookId != current.LedgerBookId || principal.Scope.SecurityId != current.SecurityId
            || principal.Scope.BookPositionId != current.BookPositionId
            || principal.Scope.TenantId != scope.TenantId || principal.Scope.CompanyId != scope.CompanyId
            || closure.Journal.AggregateId != current.LedgerBookId || closure.Journal.AccountingBasis != AccountingBasisKindDto.Gaap
            || !closure.Journal.Entry.IsBalanced || closure.Journal.CreatedAt > request.KnownAt
            || closure.Journal.Entry.Timestamp > request.KnownAt
            || closure.Journal.Entry.Metadata.EffectiveDate is not { } principalDate || principalDate > request.ThroughDate
            || principalDate != principal.EconomicEvent.EffectiveDate)
            throw new InvalidOperationException("Closed coupon cash must bind the original scheduled-principal journal and its exact scope/date.");
        var mutation = closure.Mutations.SingleOrDefault(row => row.TaxLotRecordId == current.TaxLotRecordId);
        if (mutation is null || mutation.MutationBatchId != batchId || mutation.MutationKind != AtomicTaxLotMutationKind.FactorPaydown
            || mutation.LotBefore is not { OpenQuantity: > 0m } || mutation.LotAfter.OpenQuantity != 0m
            || mutation.RecordedAt > request.KnownAt || mutation.JournalEntryId != closure.Journal.Entry.JournalEntryId
            || mutation.LotAfter.Account != physical.Account
            || !OpenLotAmortization.SameLot(mutation.LotAfter.ToOpenLot(), current))
            throw new InvalidOperationException("The current closed lot differs from the immutable terminal-principal outcome.");
        var captured = scheduled.TerminalCouponReceipts?.SingleOrDefault(row => row.TaxLotRecordId == current.TaxLotRecordId);
        var opening = scheduled.TerminalCouponOpenings?.SingleOrDefault(row => row.TaxLotRecordId == current.TaxLotRecordId);
        if ((captured is null) == (opening is null))
            throw new InvalidOperationException("Terminal principal must retain exactly one native earned or actual acquisition-opening authority for this lot.");
        BondCouponRecognitionReceiptDto? earned = null;
        OpenLotAmortizationInstructionDto frozenInputs;
        BondCouponBookedTargetsDto targets;
        if (captured is not null)
        {
            earned = await receipts.GetCouponRecognitionReceiptAsync(scope, current.TaxLotRecordId, captured.ReceiptId, ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The principal-bound earned coupon receipt is unavailable.");
            var source = earned.Instruction;
            if (previous is null || captured.JournalEntryId != earned.JournalEntryId || earned.PostedAtUtc > request.KnownAt
                || !Sha256Digest.IsCanonical(captured.EntitlementSha256)
                || captured.EntitlementSha256 != BondIncomeTerminalCoupon.EntitlementHash(source)
                || earned.ReceiptId != BondCouponRecognitionCalculator.Fingerprint(source) || source.Scope != scope
                || source.ModelVersion != BondCouponRecognitionCalculator.ModelVersion
                || source.BookedTargets?.PolicyVersion != BondIncomeBookedPrecision.PolicyVersion
                || source.Request.ThroughDate < principalDate || source.Request.ThroughDate > request.ThroughDate)
                throw new InvalidOperationException("The principal-bound earned receipt differs from its immutable authority.");
            frozenInputs = source.IncomeInstruction;
            targets = source.BookedTargets;
        }
        else
        {
            frozenInputs = scheduled.IncomeInstructions.Single(row => row.ExpectedLot.TaxLotRecordId == current.TaxLotRecordId);
            if (physical.OriginatingMutationBatchId != opening!.AcquisitionMutationBatchId
                || physical.SourceJournalEntryId != opening.AcquisitionJournalEntryId
                || BondIncomeTerminalCoupon.Opening(frozenInputs, opening.AcquisitionMutationBatchId, opening.AcquisitionJournalEntryId) != opening)
                throw new InvalidOperationException("Terminal coupon opening must bind its exact original acquisition journal and frozen zero-earned inputs.");
            await VerifyOpeningBirthAsync(ledger, scope, frozenInputs, opening, request.KnownAt, ct).ConfigureAwait(false);
            targets = BondIncomeTerminalCoupon.EarnedTargets(frozenInputs);
        }
        var origin = frozenInputs.ExpectedLot;
        if (frozenInputs.BondIncome is not { } frozen || frozen.ThroughDate < principalDate || frozen.KnownAt > request.KnownAt
            || origin.TaxLotRecordId != current.TaxLotRecordId || origin.SecurityId != current.SecurityId
            || origin.BookPositionId != current.BookPositionId || origin.OriginalQuantity != current.OriginalQuantity
            || JsonSerializer.Serialize(origin.Acquisition) != JsonSerializer.Serialize(current.Acquisition))
            throw new InvalidOperationException("Closed coupon clearing requires unchanged principal-bound earned terms and booked targets.");
        var proofReceipts = new List<BondCouponRecognitionReceiptDto>();
        if (earned is not null)
            proofReceipts.Add(earned);
        if (previous is not null)
            proofReceipts.Add(previous);
        foreach (var receipt in proofReceipts.DistinctBy(row => row.JournalEntryId))
        {
            var journal = (await ledger.QueryAsync(new LedgerJournalEntryQuery(LedgerBookId: current.LedgerBookId,
                JournalEntryId: receipt.JournalEntryId), ct).ConfigureAwait(false)).SingleOrDefault();
            if (journal is null || journal.CreatedAt > request.KnownAt || journal.Entry.Timestamp > request.KnownAt
                || journal.SourceEventId != receipt.Instruction.EventId || journal.PeriodId != receipt.Instruction.Request.PeriodId)
                throw new InvalidOperationException("A coupon journal recorded after the selected knowledge cutoff cannot authorize closed-lot cash.");
        }
        var book = await ledger.GetLedgerBookAsync(current.LedgerBookId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The closed coupon book authority is unavailable.");
        var position = await positions.GetBookPositionAsync(current.BookPositionId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The closed coupon position authority is unavailable.");
        if (book.BaseCurrency != scope.BaseCurrency || book.AccountingBasis != AccountingBasisKindDto.Gaap
            || book.FundStructureNodeKind == FundStructureNodeKindDto.Account && book.FundStructureNodeId != owner
            || position.Version <= 0
            || position.SecurityId != current.SecurityId || position.BookContext.LedgerBookId != book.LedgerBookId
            || position.BookContext.FundStructureNodeId != book.FundStructureNodeId
            || position.BookContext.FundStructureNodeKind != book.FundStructureNodeKind
            || position.BookContext.FundProfileId != book.FundProfileId || position.BookContext.BaseCurrency != book.BaseCurrency
            || position.BookContext.AccountingBasis != book.AccountingBasis
            || position.BookContext.AccountingPolicyId != book.AccountingPolicyId
            || position.BookContext.AccountingPolicyVersion != book.AccountingPolicyVersion
            || position.PositionSide is not (BookPositionSides.Long or BookPositionSides.Asset))
            throw new InvalidOperationException("Closed coupon clearing must retain current account/book/position authority.");
        var unchanged = (previous?.Instruction.CumulativeReceivableEarned ?? 0m) == targets.BookedReceivableEarned
            && (previous?.Instruction.CumulativePayableEarned ?? 0m) == targets.BookedPayableEarned;
        if (!unchanged && previous?.Instruction.ReversalTarget is null)
            throw new InvalidOperationException("A closed lot cannot generate new earned coupon targets; only explicit frozen-earnings rebooking follows a typed inverse.");
        return new(ModelVersion, unchanged ? "CashOnly" : "RebookFrozenEarnings", batchId,
            closure.Journal.Entry.JournalEntryId, principalDate, earned)
        { Opening = opening, OpeningIncomeInstruction = opening is null ? null : frozenInputs, OpeningTargets = opening is null ? null : targets };
    }

    internal static async Task<AtomicTaxLotJournalResult> VerifyOpeningBirthAsync(ILedgerJournalStore ledger,
        AccountCashForecastScopeDto scope, OpenLotAmortizationInstructionDto inputs,
        BondIncomeTerminalCouponOpeningDto opening, DateTimeOffset? knownAt, CancellationToken ct)
    {
        var lot = inputs.ExpectedLot;
        var birth = await ledger.GetAtomicTaxLotPostingAsync(opening.AcquisitionMutationBatchId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The actual terminal coupon acquisition opening is unavailable.");
        var mutation = birth.Mutations.SingleOrDefault();
        if (birth.MutationKind != AtomicTaxLotMutationKind.Acquisition || birth.MutationBatchId != opening.AcquisitionMutationBatchId
            || birth.CorrectsMutationBatchId is not null || birth.CorporateAction is not null
            || birth.Journal.Entry.JournalEntryId != opening.AcquisitionJournalEntryId || birth.Journal.AggregateId != lot.LedgerBookId
            || birth.Journal.PostingKind != LedgerPostingKindDto.Originating
            || birth.Journal.AccountingBasis != AccountingBasisKindDto.Gaap || !birth.Journal.Entry.IsBalanced
            || knownAt is { } cutoff && (birth.Journal.CreatedAt > cutoff || birth.Journal.Entry.Timestamp > cutoff)
            || mutation is null || mutation.LotBefore is not null || mutation.TaxLotRecordId != lot.TaxLotRecordId
            || mutation.MutationBatchId != opening.AcquisitionMutationBatchId || mutation.MutationKind != AtomicTaxLotMutationKind.Acquisition
            || mutation.CorrectsMutationBatchId is not null || mutation.LotAfter.TaxLotRecordId != lot.TaxLotRecordId
            || mutation.LotAfter.LedgerBookId != lot.LedgerBookId || mutation.LotAfter.LotId != lot.LotId
            || mutation.LotAfter.AcquiredDate != lot.AcquiredDate
            || mutation.LotAfter.OriginatingMutationBatchId != opening.AcquisitionMutationBatchId
            || mutation.LotAfter.SourceJournalEntryId != opening.AcquisitionJournalEntryId
            || mutation.LotAfter.ToOpenLot().OriginalQuantity != lot.OriginalQuantity || mutation.LotAfter.OpenQuantity <= 0m
            || mutation.LotAfter.SecurityId != lot.SecurityId || mutation.LotAfter.BookPositionId != lot.BookPositionId
            || mutation.JournalEntryId != opening.AcquisitionJournalEntryId
            || knownAt is { } recordedCutoff && mutation.RecordedAt > recordedCutoff
            || JsonSerializer.Serialize(mutation.LotAfter.Acquisition) != JsonSerializer.Serialize(lot.Acquisition)
            || birth.Journal.Entry.Lines.Any(line => line.Account.AccountType == Meridian.Ledger.LedgerAccountType.Revenue
                || line.Account.FinancialAccountId != scope.AccountId
                || line.Account.Symbol is not null && line.Account != mutation.LotAfter.Account
                || line.Dimensions?.BookId != scope.BookId || line.Dimensions.AccountId != scope.AccountId
                || line.Dimensions.InstrumentId != lot.SecurityId || line.Dimensions.PositionId != lot.BookPositionId
                || line.Dimensions.TaxLotId != lot.LotId))
            throw new InvalidOperationException("The terminal coupon opening differs from the actual scoped acquisition journal.");
        return birth;
    }

    internal static async Task VerifyOpeningAccountsAsync(ILedgerJournalStore ledger, AccountCashForecastScopeDto scope,
        OpenLotAmortizationInstructionDto inputs, BondIncomeTerminalCouponOpeningDto opening,
        BondCouponBookAccountsDto paths, IReadOnlyList<BondCouponRecognitionAccountDto> accounts, CancellationToken ct)
    {
        var birth = await VerifyOpeningBirthAsync(ledger, scope, inputs, opening, null, ct).ConfigureAwait(false);
        var purchased = BondIncomeBookedPrecision.RoundTarget(inputs.ExpectedLot.Acquisition.BondIncome!.PurchasedAccruedInterest
            * inputs.ExpectedLot.Acquisition.AcquisitionFxRateToFunctional);
        var receivable = accounts.Single(row => row.Path == paths.Receivable);
        var payable = accounts.Single(row => row.Path == paths.Payable);
        var actualReceivable = birth.Journal.Entry.Lines.Where(line => line.Account.Name == receivable.LedgerName
            && line.Account.AccountType.ToString() == receivable.AccountType).Sum(line => line.Debit - line.Credit);
        var actualPayable = birth.Journal.Entry.Lines.Where(line => line.Account.Name == payable.LedgerName
            && line.Account.AccountType.ToString() == payable.AccountType).Sum(line => line.Credit - line.Debit);
        if (actualReceivable != Math.Max(purchased, 0m) || actualPayable != Math.Max(-purchased, 0m))
            throw new InvalidOperationException("Current coupon accounts do not identify the actual purchased-accrued opening; explicit mapping reconciliation is required.");
    }
}
