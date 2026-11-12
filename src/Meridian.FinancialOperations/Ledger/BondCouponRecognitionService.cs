using System.Text.Json;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Ledger;
using Meridian.Storage.AssetOperations;
using Meridian.Storage.Ledger;

namespace Meridian.FinancialOperations.Ledger;

/// <summary>Coupon income and cash clearing have their own governed Income event, independent of lot basis.</summary>
public sealed class BondCouponRecognitionService(IBondIncomeService income, IBondCouponRecognitionStore receipts,
    ILedgerJournalStore ledger, IInstrumentPositionProjectionStore positions, IAccountingPolicyService policies,
    IAccountingConfigurationService configuration, IBondMonitoringService monitoring,
    IBondCouponRecognitionEvidenceVerifier? evidenceVerifier = null,
    IBondIncomePreparationEvidenceStore? preparationEvidence = null) : IBondCouponRecognitionService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<BondCouponRecognitionPreviewDto> PreviewAsync(AccountCashForecastScopeDto scope,
        BondCouponRecognitionRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(request);
        if (!Guid.TryParse(scope.BookId, out var bookId) || !Guid.TryParse(scope.AccountId, out var accountId)
            || string.IsNullOrWhiteSpace(scope.TenantId) || string.IsNullOrWhiteSpace(scope.CompanyId)
            || string.IsNullOrWhiteSpace(request.RunId) || request.RunId.Length > 128 || request.CashAllocations is null
            || request.CashAllocations.Any(a => a is null) || request.CashAllocations.Count > 1000)
            throw new ArgumentException("Coupon recognition requires exact authenticated scope and bounded retained allocations.");
        var replay = await receipts.GetCouponRecognitionRunAsync(scope, request.TaxLotRecordId, request.RunId, ct).ConfigureAwait(false);
        if (replay is not null)
        {
            if (JsonSerializer.Serialize(replay.Instruction.Request) != JsonSerializer.Serialize(request))
                throw new InvalidOperationException("Coupon recognition identity is already posted with different inputs.");
            return BuildPreview(replay.Instruction);
        }
        if (request.ReversesReceiptId is not null)
            return await BondCouponRecognitionReversal.PreviewAsync(ledger, receipts, positions, scope, request, ct).ConfigureAwait(false);
        if (request.CorrectionRationale is not null)
            throw new ArgumentException("A correction rationale requires an explicit receipt reversal.");
        var projected = await income.PreviewAsync(new(bookId, request.TaxLotRecordId, request.ThroughDate,
            request.KnownAt, accountId), ct).ConfigureAwait(false);
        var lot = projected.Instruction.ExpectedLot;
        // Historical earned coupons after partial disposal require an entitlement history, never today's share.
        if (lot.OpenQuantity != lot.OriginalQuantity)
            throw new InvalidOperationException("Coupon recognition after partial disposal requires a retained lot-entitlement history.");
        var book = await ledger.GetLedgerBookAsync(bookId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The coupon ledger book is unavailable.");
        if (lot.Acquisition.AcquisitionCurrency != book.BaseCurrency
            || projected.Instruction.BondIncome?.CurrentProjection.Currency != book.BaseCurrency)
            throw new InvalidOperationException("Coupon posting requires retained dated book FX translation for foreign acquisition currency; native cash forecasts remain available.");
        var period = await ledger.GetPeriodAsync(request.PeriodId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The coupon accounting period is unavailable.");
        if (period.LedgerBookId != bookId || period.Status != "Open" || request.ThroughDate < period.StartDate
            || request.ThroughDate > period.EndDate || book.BaseCurrency != scope.BaseCurrency
            || book.AccountingBasis != AccountingBasisKindDto.Gaap)
            throw new InvalidOperationException("Coupon recognition requires the owning open US GAAP book period and currency.");
        var policy = await policies.ResolvePolicyAsync(new(book.AccountingBasis, request.ThroughDate, book.AccountingPolicyId,
            book.FundProfileId, book.FundStructureNodeId, PolicyVersion: book.AccountingPolicyVersion), ct).ConfigureAwait(false);
        using var document = JsonDocument.Parse(policy.RulesJson);
        if (!document.RootElement.TryGetProperty("bondCouponRecognition", out var adopted)
            || !adopted.TryGetProperty("modelVersion", out var model) || model.GetString() != BondCouponRecognitionCalculator.ModelVersion
            || !adopted.TryGetProperty("accounts", out var mappings))
            throw new InvalidOperationException("The book must explicitly adopt the versioned coupon-recognition account mappings.");
        var accountPaths = mappings.Deserialize<BondCouponBookAccountsDto>(JsonOptions)
            ?? throw new InvalidOperationException("Coupon-recognition account mappings are unavailable.");
        var workspace = await configuration.GetWorkspaceAsync(book.FundProfileId, bookId, ct, scope.TenantId, scope.CompanyId).ConfigureAwait(false);
        var boundAccounts = new List<BondCouponRecognitionAccountDto>();
        Bind(accountPaths.Receivable, ["Asset"]);
        Bind(accountPaths.Payable, ["Liability"]);
        Bind(accountPaths.Income, ["Revenue"]);
        Bind(accountPaths.Cash, ["Asset"]);
        Bind(accountPaths.Tax, ["Asset", "Expense"]);
        Bind(accountPaths.Fees, ["Expense"]);
        if (boundAccounts.GroupBy(a => (a.LedgerName, a.AccountType)).Any(g => g.Count() != 1))
            throw new InvalidOperationException("Coupon chart paths must resolve to distinct durable ledger account identities.");
        if (new[] { accountPaths.Receivable, accountPaths.Payable, accountPaths.Income, accountPaths.Cash }.Distinct(StringComparer.Ordinal).Count() != 4)
            throw new InvalidOperationException("Coupon receivable, payable, income and cash must use distinct authoritative accounts.");
        if (new[] { accountPaths.Receivable, accountPaths.Payable, accountPaths.Income, accountPaths.Cash }.Contains(accountPaths.Tax)
            || new[] { accountPaths.Receivable, accountPaths.Payable, accountPaths.Income, accountPaths.Cash }.Contains(accountPaths.Fees))
            throw new InvalidOperationException("Coupon tax and expense mappings must remain distinct from cash, income, receivable and payable accounts.");
        var previous = await receipts.GetLatestCouponRecognitionAsync(scope, request.TaxLotRecordId, ct).ConfigureAwait(false);
        if (previous is not null && previous.Instruction.BookedTargets?.PolicyVersion != BondIncomeBookedPrecision.PolicyVersion)
            throw new InvalidOperationException("Prior coupon recognition requires an explicit booked-precision opening reconciliation.");
        if (previous is null)
            await BondCouponRecognitionOpeningGuard.ValidateAsync(ledger, scope, lot, accountPaths, boundAccounts, ct).ConfigureAwait(false);
        if (previous is not null && request.ThroughDate < previous.Instruction.Request.ThroughDate)
            throw new InvalidOperationException("Earlier coupon periods require the governed correction workflow.");
        var previousEarned = previous?.Instruction.CumulativeEarned ?? 0m;
        var priorSettled = (previous?.Instruction.PriorGrossSettled ?? 0m)
            + (previous?.Instruction.Cash.Sum(c => c.Allocation.GrossAmount) ?? 0m);
        var rawEarned = projected.Income.CouponIncome;
        var terms = projected.Instruction.BondIncome!;
        var payableFlows = terms.CurrentProjection.CashFlows.Where(f => f.PaymentDate > lot.AcquiredDate
            && f.PaymentDate <= request.ThroughDate && f.Interest != 0m)
            .GroupBy(f => (f.PaymentDate, f.AccrualStart))
            .Select(group => new
            {
                Flow = group.First(),
                Cash = BondIncomeCouponRounding.AllocatedCash(lot.TaxLotRecordId,
                terms, group.Key.PaymentDate, group.Key.AccrualStart)
            }).ToArray();
        var rawReceivableEarned = decimal.Round(payableFlows.Sum(f => Math.Max(f.Cash, 0m))
            + Math.Max(terms.CurrentAccruedInterest, 0m) - Math.Max(terms.AcquisitionAccruedInterest, 0m), 12, MidpointRounding.ToEven);
        var rawPayableEarned = decimal.Round(payableFlows.Sum(f => Math.Max(-f.Cash, 0m))
            + Math.Max(-terms.CurrentAccruedInterest, 0m) - Math.Max(-terms.AcquisitionAccruedInterest, 0m), 12, MidpointRounding.ToEven);
        var obligationCash = payableFlows.ToDictionary(flow => Meridian.Instruments.FixedIncome.AccountBondIncomePlanningService.ObligationIdentity(
            scope, lot.SecurityId, "Coupon", flow.Flow.PaymentDate, flow.Flow.AccrualStart), flow => flow.Cash, StringComparer.Ordinal);
        var settledObligations = (await receipts.GetConsumedCouponObligationsAsync(scope, lot.TaxLotRecordId, ct).ConfigureAwait(false)
            ?? []).ToDictionary(row => row.ExpectedFlowId, row => row.GrossAmount, StringComparer.Ordinal);
        if (settledObligations.Any(row => row.Value != 0m && (!obligationCash.TryGetValue(row.Key, out var due)
            || Math.Sign(row.Value) != Math.Sign(due) || Math.Abs(row.Value) > Math.Abs(due))))
            throw new InvalidOperationException("A revised, removed or reversed settled coupon obligation requires explicit reversal/rebook before further recognition.");
        if (Math.Abs(rawReceivableEarned - rawPayableEarned - rawEarned) > 0.000000000001m)
            throw new InvalidOperationException("Coupon side balances do not reproduce the retained accounting-income result.");
        var receivableEarned = BondIncomeBookedPrecision.RoundTarget(rawReceivableEarned);
        var payableEarned = BondIncomeBookedPrecision.RoundTarget(rawPayableEarned);
        var earned = receivableEarned - payableEarned;
        var priorReceivableSettled = (previous?.Instruction.PriorReceivableSettled ?? 0m)
            + (previous?.Instruction.Cash.Sum(c => Math.Max(c.Allocation.GrossAmount, 0m)) ?? 0m);
        var priorPayableSettled = (previous?.Instruction.PriorPayableSettled ?? 0m)
            + (previous?.Instruction.Cash.Sum(c => Math.Max(-c.Allocation.GrossAmount, 0m)) ?? 0m);
        if (previous is not null && previous.Instruction.ReversalTarget is null
            && request.ThroughDate == previous.Instruction.Request.ThroughDate && earned != previousEarned)
            throw new InvalidOperationException("Changing earned income on an already recognized date requires explicit reversal/rebook.");
        if (request.CashAllocations.Select(a => a.AllocationId).Distinct(StringComparer.Ordinal).Count() != request.CashAllocations.Count
            || request.CashAllocations.Any(a => string.IsNullOrWhiteSpace(a.AllocationId) || string.IsNullOrWhiteSpace(a.PaymentVersionId)))
            throw new ArgumentException("Cash allocations require unique stable identities and retained bank versions.");
        foreach (var amount in request.CashAllocations.SelectMany(a => new[] { a.NetAmount, a.GrossAmount, a.WithholdingTax, a.Fees }))
            BondIncomeBookedPrecision.RequireRepresentable(amount);
        var history = await monitoring.GetHistoryAsync(scope, ct).ConfigureAwait(false);
        var selected = history.LastOrDefault(s => s.Inputs.RecordedAtUtc <= request.KnownAt);
        var recognizedCash = new List<BondCouponRecognizedCashDto>();
        foreach (var allocation in request.CashAllocations.OrderBy(a => a.AllocationId, StringComparer.Ordinal))
        {
            var payment = selected?.Inputs.Payments.SingleOrDefault(p => p.VersionId == allocation.PaymentVersionId
                && !selected.Inputs.Payments.Any(next => next.SupersedesVersionId == p.VersionId));
            if (payment is null || payment.Status != AccountCashFlowStatus.Actual || payment.SecurityId != lot.SecurityId
                || payment.AccountId != scope.AccountId || payment.BookId != scope.BookId || payment.Currency != scope.BaseCurrency
                || payment.FlowType != "Coupon" || payment.CashReceiptDate > request.ThroughDate
                || payment.Provenance.Evidence.RetainedAtUtc > request.KnownAt
                || Math.Sign(payment.Amount) != Math.Sign(allocation.NetAmount))
                throw new InvalidOperationException("Coupon clearing requires an active, already observed account/book/security bank payment.");
            var retainedAllocation = selected!.Inputs.Allocations.SingleOrDefault(a => a.AllocationId == allocation.AllocationId
                && a.ActualVersionId == allocation.PaymentVersionId);
            if (retainedAllocation is null || JsonSerializer.Serialize(retainedAllocation.DeductionEvidence)
                != JsonSerializer.Serialize(allocation.DeductionEvidence)
                || !BondCouponRecognitionCalculator.IsBoundedShare(allocation, retainedAllocation))
                throw new InvalidOperationException("Coupon clearing must retain a bounded lot share of the reviewed source gross/net allocation and deduction evidence.");
            var sourceConsumption = await receipts.GetConsumedCouponAllocationAsync(scope, allocation.AllocationId, ct).ConfigureAwait(false);
            if (!BondCouponRecognitionCalculator.IsAvailableShare(allocation, retainedAllocation, sourceConsumption))
                throw new InvalidOperationException("Coupon lot shares exceed remaining retained source net, gross, tax or fees.");
            var obligation = payableFlows.SingleOrDefault(flow => Meridian.Instruments.FixedIncome.AccountBondIncomePlanningService.ObligationIdentity(
                scope, lot.SecurityId, "Coupon", flow.Flow.PaymentDate, flow.Flow.AccrualStart) == retainedAllocation.ExpectedFlowId);
            if (obligation is null || Math.Sign(obligation.Cash) != Math.Sign(allocation.GrossAmount))
                throw new InvalidOperationException("Cash clearing must name a coupon obligation actually earned by this retained lot.");
            var settledObligation = settledObligations.GetValueOrDefault(retainedAllocation.ExpectedFlowId);
            var requestedObligationGross = request.CashAllocations.Where(a => selected.Inputs.Allocations.Any(retained =>
                retained.AllocationId == a.AllocationId && retained.ExpectedFlowId == retainedAllocation.ExpectedFlowId)).Sum(a => a.GrossAmount);
            if (Math.Abs(settledObligation + requestedObligationGross) > Math.Abs(obligation.Cash))
                throw new InvalidOperationException("Coupon clearing exceeds this lot's specific earned obligation.");
            var consumed = await receipts.GetConsumedCouponCashAsync(scope, payment.FlowId, ct).ConfigureAwait(false);
            if (consumed != 0m && Math.Sign(consumed) != Math.Sign(payment.Amount))
                throw new InvalidOperationException("A bank correction reversing consumed cash direction requires an explicit correcting journal.");
            if (Math.Abs(consumed + request.CashAllocations.Where(a => a.PaymentVersionId == allocation.PaymentVersionId).Sum(a => a.NetAmount)) > Math.Abs(payment.Amount))
                throw new InvalidOperationException("Bank cash is already consumed or exceeds its observed amount.");
            recognizedCash.Add(new(allocation, payment.Amount, payment, retainedAllocation));
        }
        if (recognizedCash.Count > 0)
            await (evidenceVerifier ?? throw new InvalidOperationException("Coupon cash clearing requires current retained source verification."))
                .VerifyAsync(scope, recognizedCash, ct).ConfigureAwait(false);
        var lines = BondCouponRecognitionCalculator.LinesBySide(scope.BaseCurrency, accountPaths,
            receivableEarned - (previous?.Instruction.CumulativeReceivableEarned ?? 0m),
            payableEarned - (previous?.Instruction.CumulativePayableEarned ?? 0m),
            receivableEarned + Math.Max(BondIncomeBookedPrecision.RoundTarget(lot.Acquisition.BondIncome!.PurchasedAccruedInterest), 0m) - priorReceivableSettled,
            payableEarned + Math.Max(-BondIncomeBookedPrecision.RoundTarget(lot.Acquisition.BondIncome!.PurchasedAccruedInterest), 0m) - priorPayableSettled,
            request.CashAllocations);
        var position = await positions.GetBookPositionAsync(lot.BookPositionId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Coupon book position is unavailable.");
        var dimensions = (position.BookContext.Dimensions ?? new()) with
        { InstrumentId = lot.SecurityId, PositionId = lot.BookPositionId, TaxLotId = lot.LotId, AccountId = scope.AccountId, BookId = scope.BookId };
        var identity = Sha256Digest.ComputeBytesUtf8($"{scope.TenantId}/{scope.CompanyId}/{scope.BookId}/{scope.AccountId}/{lot.TaxLotRecordId:D}/{request.RunId}");
        var instruction = new BondCouponRecognitionInstructionDto(BondCouponRecognitionCalculator.ModelVersion, scope, request,
            projected.Instruction, new Guid(identity.AsSpan(0, 16)), previous?.ReceiptId, earned, previousEarned,
            priorSettled, recognizedCash, boundAccounts, lines.Select(l => l with { Dimensions = dimensions }).ToArray())
        {
            CumulativeReceivableEarned = receivableEarned,
            CumulativePayableEarned = payableEarned,
            PreviousReceivableEarned = previous?.Instruction.CumulativeReceivableEarned ?? 0m,
            PreviousPayableEarned = previous?.Instruction.CumulativePayableEarned ?? 0m,
            PriorReceivableSettled = priorReceivableSettled,
            PriorPayableSettled = priorPayableSettled,
            BookedTargets = new(BondIncomeBookedPrecision.PolicyVersion,
                rawReceivableEarned, receivableEarned, rawReceivableEarned - receivableEarned,
                rawPayableEarned, payableEarned, rawPayableEarned - payableEarned,
                previous?.Instruction.BookedTargets?.RawReceivableEarned ?? 0m,
                previous?.Instruction.BookedTargets?.RawPayableEarned ?? 0m)
        };
        return BuildPreview(instruction);

        void Bind(string path, string[] types)
        {
            var rows = workspace.ChartOfAccounts.Where(a => a.Path == path && !a.IsArchived && types.Contains(a.AccountType)).ToArray();
            if (rows.Length != 1)
                throw new InvalidOperationException("Coupon account mappings require unique active accounts of the declared type.");
            if (!boundAccounts.Any(a => a.Path == path))
                boundAccounts.Add(new(path, rows[0].AccountName, rows[0].AccountType));
        }
    }

    public async Task<BondCouponRecognitionPreparationDto> PrepareAsync(AccountCashForecastScopeDto scope,
        BondCouponRecognitionRequest request, string expectedFingerprint, string actor, DateTimeOffset projectedAtUtc,
        CancellationToken ct = default)
    {
        var preview = await PreviewAsync(scope, request, ct).ConfigureAwait(false);
        if (!Meridian.Contracts.Integrity.Sha256Digest.FixedEquals(preview.Fingerprint, expectedFingerprint))
            throw new InvalidOperationException("Coupon review is stale; rebuild from current posted recognition and retained inputs.");
        if (!preview.HasChanges)
            throw new InvalidOperationException("No coupon accrual or cash-clearing movement remains to prepare.");
        var instruction = preview.Instruction;
        var lot = instruction.IncomeInstruction.ExpectedLot;
        var period = await ledger.GetPeriodAsync(request.PeriodId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Coupon accounting period is unavailable.");
        var position = await positions.GetBookPositionAsync(lot.BookPositionId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Coupon book position is unavailable.");
        var artifact = new BondIncomePreparationArtifactDto(scope.TenantId, scope.CompanyId, Guid.Parse(scope.AccountId),
            lot.LedgerBookId, request.PeriodId, instruction.EventId, 1, preview.Fingerprint, instruction.IncomeInstruction)
        { CouponRecognition = instruction };
        var calculationSource = await (preparationEvidence ?? throw new InvalidOperationException("Coupon preparation requires durable operator-reviewed calculation evidence."))
            .RetainAsync(artifact, actor, projectedAtUtc, false, ct).ConfigureAwait(false);
        var evidence = lot.Acquisition.Evidence.Append(instruction.IncomeInstruction.SecurityEvidence)
            .Concat(instruction.Cash.Select(c => c.ObservedPayment.Provenance.Evidence))
            .Concat(instruction.Cash.Where(c => c.Allocation.DeductionEvidence is not null).Select(c => c.Allocation.DeductionEvidence!.Evidence))
            .Append(calculationSource).Distinct().ToArray();
        var economic = new EconomicEventReferenceDto(instruction.EventId, AssetAccountingEventTypeNames.For(AssetAccountingEventKindDto.Income),
            1, request.ThroughDate, calculationSource.RetainedAtUtc, calculationSource.SourceSystem,
            calculationSource.SourceReference, SourceContentHash: calculationSource.ContentHashSha256)
        { SecurityId = lot.SecurityId, BookPositionId = lot.BookPositionId, RetainedEvidence = evidence };
        var lineage = new ProjectionLineageDto(instruction.EventId, instruction.EventId, BondCouponRecognitionCalculator.ModelVersion,
            BondCouponRecognitionCalculator.ModelVersion, BondCouponRecognitionCalculator.ModelVersion, "ObservedCouponRecognition",
            request.ThroughDate, calculationSource.RetainedAtUtc, calculationSource.SourceSystem, calculationSource.SourceReference, economic,
            instruction.IncomeInstruction.Security.Version.ToString(), OpenLotAmortization.SecurityHash(instruction.IncomeInstruction.Security))
        { BookPositionId = lot.BookPositionId, RetainedEvidence = evidence };
        var effect = new ProjectedAccountingEffectDto(instruction.EventId, BondCouponRecognitionCalculator.ModelVersion,
            BondCouponRecognitionCalculator.ModelVersion, request.ThroughDate, instruction.Lines.Sum(l => l.Debit),
            instruction.Lines.Sum(l => l.Credit), scope.BaseCurrency, instruction.Lines);
        var source = new ProjectAssetAccountingEventRequestDto(AssetAccountingEventKindDto.Income,
            new(lot.SecurityId, instruction.IncomeInstruction.Security.Version, lot.BookPositionId, position.Version,
                lot.LedgerBookId, request.PeriodId, position.BookContext.AccountingBasis, position.BookContext.FundProfileId,
                scope.TenantId, scope.CompanyId, instruction.Lines.FirstOrDefault()?.Dimensions), economic, lineage, effect,
            effect.TotalDebits, scope.BaseCurrency, period.Version, calculationSource.ReviewedBy, calculationSource.RetainedAtUtc, evidence,
            Notes: request.CorrectionRationale,
            Correction: instruction.ReversalTarget is not { } target ? null : new(target.Instruction.EventId, 1,
                target.JournalEntryId, lot.LedgerBookId, target.Instruction.Request.PeriodId, position.BookContext.AccountingBasis))
        { BondCouponRecognition = instruction };
        return new(preview, source);
    }

    public async Task VerifyAsync(BondCouponRecognitionInstructionDto instruction, CancellationToken ct = default)
    {
        var expected = await PreviewAsync(instruction.Scope, instruction.Request, ct).ConfigureAwait(false);
        if (expected.Fingerprint != BondCouponRecognitionCalculator.Fingerprint(instruction))
            throw new InvalidOperationException("Coupon instruction differs from authoritative posted state, book mappings or observed inputs.");
    }

    public async Task VerifyPreparedSourceAsync(BondCouponRecognitionInstructionDto instruction,
        RetainedEvidenceIdentityDto source, CancellationToken ct = default)
    {
        await VerifyAsync(instruction, ct).ConfigureAwait(false);
        var artifact = new BondIncomePreparationArtifactDto(instruction.Scope.TenantId, instruction.Scope.CompanyId,
            Guid.Parse(instruction.Scope.AccountId), instruction.IncomeInstruction.ExpectedLot.LedgerBookId,
            instruction.Request.PeriodId, instruction.EventId, 1, BondCouponRecognitionCalculator.Fingerprint(instruction),
            instruction.IncomeInstruction)
        { CouponRecognition = instruction };
        var retained = await (preparationEvidence ?? throw new InvalidOperationException("Prepared coupon calculation evidence is unavailable."))
            .RetainAsync(artifact, source.ReviewedBy, source.RetainedAtUtc, true, ct).ConfigureAwait(false);
        if (retained != source)
            throw new InvalidOperationException("Coupon source differs from its exact retained operator-reviewed calculation.");
    }

    internal static BondCouponRecognitionPreviewDto BuildPreview(BondCouponRecognitionInstructionDto instruction)
    {
        var delta = instruction.CumulativeEarned - instruction.PreviousEarned;
        var cleared = instruction.Cash.Sum(c => c.Allocation.GrossAmount);
        var settlementDelta = instruction.ReversalTarget is { } reversed
            ? -reversed.Instruction.Cash.Sum(c => c.Allocation.GrossAmount) : cleared;
        var purchasedAccrued = instruction.IncomeInstruction.ExpectedLot.Acquisition.BondIncome!.PurchasedAccruedInterest;
        if (instruction.BookedTargets is not null)
            purchasedAccrued = BondIncomeBookedPrecision.RoundTarget(purchasedAccrued);
        return new(BondCouponRecognitionCalculator.Fingerprint(instruction), delta, settlementDelta,
            instruction.CumulativeEarned + purchasedAccrued
                - instruction.PriorGrossSettled - cleared, instruction.Lines.Count != 0, instruction)
        {
            RemainingReceivable = instruction.CumulativeReceivableEarned
                + Math.Max(purchasedAccrued, 0m)
                - instruction.PriorReceivableSettled - instruction.Cash.Sum(c => Math.Max(c.Allocation.GrossAmount, 0m)),
            RemainingPayable = instruction.CumulativePayableEarned
                + Math.Max(-purchasedAccrued, 0m)
                - instruction.PriorPayableSettled - instruction.Cash.Sum(c => Math.Max(-c.Allocation.GrossAmount, 0m))
        };
    }
}
