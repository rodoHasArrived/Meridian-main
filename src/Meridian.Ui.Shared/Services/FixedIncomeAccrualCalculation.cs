using Meridian.Application.SecurityMaster;
using Meridian.Application.SecurityMaster.CashFlow;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.SecurityMaster;
using Meridian.Ledger;
using DomainLedger = Meridian.Ledger.Ledger;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Coupon accrual and premium/discount amortization for one Bond or StructuredCredit position, from the Security Master
/// cash-flow projection and the position's open lots. Each occurrence drafts the day-count share
/// of every coupon period that falls inside its accrual window, and the premium or discount
/// amortized over the same days. Projected principal paydowns are not drafted here: they are cash movements and
/// arrive through cash activity. Inputs: <c>securityId</c>, <c>ticker</c>, optional
/// <c>financialAccountId</c>.
/// </summary>
public sealed class FixedIncomeAccrualCalculation(
    IRecurringJournalCalculationInputs inputs,
    ISecurityMasterAmortizationLedgerBridge? bridge = null,
    ISecurityMasterCashFlowService? cashFlows = null,
    ISecurityMasterQueryService? securities = null) : IRecurringJournalCalculation
{
    public const string CalculationKind = "fixed-income-accrual";
    private const string AmortizationActivity = "FixedIncomeAmortization";

    public string Kind => CalculationKind;

    public bool ReadsClosingState => true;

    // The accrual runs after its effective day has ended, when only a book position lets the
    // holding be read for that day, so the position is not optional.
    public IReadOnlyList<string> RequiredInputs => ["securityId", "ticker", "bookPositionId"];

    public async Task<RecurringJournalCalculationResult> CalculateAsync(RecurringJournalCalculationContext context, CancellationToken ct)
    {
        if (bridge is null || cashFlows is null)
            throw new InvalidOperationException("The Security Master cash-flow service is unavailable; fixed-income accruals are blocked.");
        if (!Guid.TryParse(context.RequireInput("securityId"), out var securityId) || securityId == Guid.Empty)
            throw new InvalidOperationException("The calculated template input 'securityId' must be a security identifier.");
        var ticker = context.RequireInput("ticker").ToUpperInvariant();
        var financialAccountId = context.OptionalInput("financialAccountId");
        var windowStart = context.RequireWindowStart();
        var projectionAsOf = new DateTimeOffset(windowStart.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        var projection = await cashFlows.GetProjectionAsOfAsync(securityId, StructuredCashFlowScenario.Base, projectionAsOf, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"No cash-flow projection is available for {ticker}.");
        if (StructuredCashFlowLedgerGate.EvaluateBlockReason(projection) is { } blocked)
            throw new InvalidOperationException($"The cash-flow projection for {ticker} cannot be posted: {blocked}");

        // Projected amounts are in the bond's own currency and are drafted unconverted.
        if (securities is null)
            throw new InvalidOperationException("Security Master is unavailable, so the bond's currency cannot be confirmed; fixed-income accruals are blocked.");
        var security = await securities.GetByIdAsync(securityId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Security Master holds no security for {ticker}; its currency cannot be confirmed.");
        if (!SecurityAssetClassCatalog.TryGet(security.AssetClass, out var descriptor) || !descriptor.SupportsRecurringFixedIncomeAccrual)
            throw new InvalidOperationException(
                $"{ticker} is classified as '{security.AssetClass}'; recurring fixed-income accrual currently supports Bond and StructuredCredit only.");
        if (!string.Equals(security.Currency?.Trim(), context.Scope.Currency.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"{ticker} is denominated in {security.Currency}, not the book currency {context.Scope.Currency}; cross-currency bond accruals are not supported.");
        if (projection.SourceKind is not (StructuredCashFlowSourceKind.CalculatedBullet or StructuredCashFlowSourceKind.CalculatedSinker)
            || projection.TermsUsed?.HasLegs == true)
            throw new InvalidOperationException(
                $"The cash-flow projection for {ticker} is not a calculated fixed-rate schedule; recurring accrual requires retained fixed-rate interest evidence.");
        // Without its accrual interval a coupon could only be recognized whole on its payment date.
        if (projection.Schedule.Any(static entry => entry.InterestAmount > 0m &&
                (entry.InterestAccrualStart is not { } start || entry.InterestAccrualEnd is not { } end || end <= start)))
            throw new InvalidOperationException(
                $"The cash-flow projection for {ticker} states no accrual interval for a coupon period, so its interest cannot be accrued by day.");

        if (!Guid.TryParse(context.RequireInput("bookPositionId"), out var bookPositionId) || bookPositionId == Guid.Empty)
            throw new InvalidOperationException("The calculated template input 'bookPositionId' must be a book position identifier.");
        if (projection.Schedule.Any(static entry => entry.InterestSegments is { Count: > 0 }))
            return await CalculateRetainedAsync(context, security, securityId, bookPositionId, ticker,
                financialAccountId, projectionAsOf, descriptor.QuoteConvention, ct).ConfigureAwait(false);
        if (descriptor.AssetClass == "StructuredCredit"
            || projection.TermsUsed?.CurrentFactor is { } factor && factor != 1m
            || projection.TermsUsed?.FactorSchedule.Any(static point => point.Factor != 1m) == true)
            throw new InvalidOperationException(
                $"The cash-flow projection for {ticker} states no retained interest segments; StructuredCredit and factored accrual require explicit principal and factor evidence.");

        var securitiesAccount = LedgerAccounts.Securities(ticker, financialAccountId);
        async Task<LedgerTaxLot[]> LotsHeldAsync(DateOnly date)
            => (await inputs.ListOpenLotsAsync(context.Scope, securitiesAccount, securityId, bookPositionId, date, ct).ConfigureAwait(false))
                .Where(lot => lot.SecurityId == securityId)
                .OrderBy(lot => lot.LotId, StringComparer.Ordinal)
                .ToArray();

        // A holding can be sold or reduced inside the window, so the lots open at its end do not
        // say what was held on each day. The holding is read for every day and the window is
        // split wherever it changes; a lot accrues through the last day it is still open at the close.
        var holdings = new List<(DateOnly Start, DateOnly End, LedgerTaxLot[] Lots)>();
        {
            var from = windowStart;
            LedgerTaxLot[]? held = null;
            for (var day = windowStart; day <= context.EffectiveDate; day = day.AddDays(1))
            {
                var onDay = await LotsHeldAsync(day).ConfigureAwait(false);
                if (held is not null && !SameHolding(held, onDay))
                {
                    holdings.Add((from, day.AddDays(-1), held));
                    from = day;
                }
                held = onDay;
            }
            holdings.Add((from, context.EffectiveDate, held ?? []));
        }

        holdings.RemoveAll(static holding => holding.Lots.Length == 0);
        if (holdings.Count == 0)
            return RecurringJournalCalculationResult.NothingDue(
                $"The fund book holds no open lot of {ticker} between {windowStart:yyyy-MM-dd} and {context.EffectiveDate:yyyy-MM-dd}; no coupon or amortization accrues.");
        // Projected interest is for the security's whole principal face; the fund earns its share.
        if (projection.TermsUsed?.PrincipalFace is not { } principalFace || principalFace <= 0m)
            throw new InvalidOperationException(
                $"The cash-flow projection for {ticker} states no principal face, so its interest cannot be scaled to the position held.");

        // The bridge posts to a scratch ledger that is never persisted; its journals are the
        // calculation result and reach the real ledger only through the approved draft.
        var journals = new List<JournalEntry>();
        foreach (var (start, end, heldLots) in holdings)
        {
            var scratch = new DomainLedger();
            await bridge.PostProjectedCashFlowsAsync(securityId, ticker, scratch, new AmortizationLedgerPostingContext(
                heldLots, financialAccountId, StructuredCashFlowScenario.Base, ProjectionAsOf: projectionAsOf,
                AmortizedThrough: start.AddDays(-1),
                AccrualWindow: new AmortizationAccrualWindow(start, end),
                CouponPrincipalBasis: principalFace,
                QuoteConvention: descriptor.QuoteConvention), ct).ConfigureAwait(false);
            journals.AddRange(scratch.Journal.Where(journal => journal.Metadata.ActivityType == AmortizationActivity &&
                journal.Metadata.EffectiveDate is { } date && InWindow(date, start, end)));
        }

        var lotCount = holdings.SelectMany(static holding => holding.Lots).Select(static lot => lot.LotId).Distinct(StringComparer.Ordinal).Count();
        return BuildResult(context, securityId, ticker, financialAccountId, windowStart, journals, lotCount, holdings.Count > 1);
    }

    private async Task<RecurringJournalCalculationResult> CalculateRetainedAsync(
        RecurringJournalCalculationContext context, SecurityDetailDto security,
        Guid securityId, Guid bookPositionId, string ticker, string? financialAccountId,
        DateTimeOffset projectionAsOf, SecurityQuoteConvention quoteConvention, CancellationToken ct)
    {
        // Capture using the exact selected terms, then carry that immutable projection all the
        // way to the bridge. A second ordinary read could silently change the source or terms.
        var projection = await cashFlows!.GetProjectionAsync(security, StructuredCashFlowScenario.Base,
            new BondCalculationContext(projectionAsOf, SecurityVersion: security.Version), ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"No retained cash-flow projection is available for {ticker}.");
        if (StructuredCashFlowLedgerGate.EvaluateBlockReason(projection) is { } blocked)
            throw new InvalidOperationException($"The retained cash-flow projection for {ticker} cannot be posted: {blocked}");
        var receiptInputs = projection.InputManifest?.Where(static input => input.Kind == "CalculationReceipt").ToArray() ?? [];
        var modelInputs = projection.InputManifest?.Where(static input => input.Kind == "CalculationModel").ToArray() ?? [];
        if (receiptInputs.Length != 1 || !Sha256Digest.IsCanonical(receiptInputs[0].Key)
            || modelInputs.Length != 1 || modelInputs[0].Key != SecurityMasterCashFlowService.ContextCalculationVersion
            || projection.SecurityId != securityId || projection.AsOf != projectionAsOf)
            throw new InvalidOperationException(
                $"The cash-flow projection for {ticker} has no retained v5 calculation receipt bound to the selected security and cutoff; accrual is blocked.");
        var receiptHash = receiptInputs[0].Key;
        var receiptModel = modelInputs[0].Key;
        var windowStart = context.RequireWindowStart();
        // Rounding belongs to the coupon's cumulative owned interest, so a later accounting
        // window must read the earlier daily holdings in that same coupon period as well.
        var starts = projection.Schedule.SelectMany(static entry => entry.InterestSegments ?? [])
            .Where(segment => segment.End >= windowStart && segment.Start < context.EffectiveDate)
            .Select(static segment => segment.PeriodStart);
        var historyStart = starts.Append(windowStart).Min();
        var holdings = await inputs.ListAccrualHoldingsAsync(context.Scope, securityId, bookPositionId,
            historyStart, context.EffectiveDate, ct).ConfigureAwait(false);
        if (holdings.Any(holding => holding.Lot.SecurityId != securityId
                || holding.Date < historyStart || holding.Date > context.EffectiveDate))
            throw new InvalidOperationException("The retained accrual holdings do not match the requested security and date scope.");
        if (!holdings.Any(holding => holding.Date >= windowStart && holding.OpenShare > 0m))
            return RecurringJournalCalculationResult.NothingDue(
                $"The fund book holds no lot of {ticker} between {windowStart:yyyy-MM-dd} and {context.EffectiveDate:yyyy-MM-dd}; no coupon or amortization accrues.");

        var scratch = new DomainLedger();
        await bridge!.PostProjectedCashFlowsAsync(securityId, ticker, scratch, new AmortizationLedgerPostingContext(
            FinancialAccountId: financialAccountId,
            ProjectionAsOf: projectionAsOf,
            AmortizedThrough: windowStart.AddDays(-1),
            AccrualWindow: new AmortizationAccrualWindow(windowStart, context.EffectiveDate),
            CouponPrincipalBasis: projection.TermsUsed?.PrincipalFace,
            QuoteConvention: quoteConvention,
            AccrualHoldings: holdings,
            RetainedProjection: projection), ct).ConfigureAwait(false);
        // Principal cash remains outside recurring journals even when the projection carries
        // a same-day pool paydown. The real ledger receives only an approved accrual draft.
        var journals = scratch.Journal.Where(journal => journal.Metadata.ActivityType == AmortizationActivity
            && journal.Metadata.EffectiveDate is { } date && InWindow(date, windowStart, context.EffectiveDate)).ToArray();
        var lotCount = holdings.Where(holding => holding.Date >= windowStart)
            .Select(static holding => holding.Lot.LotId).Distinct(StringComparer.Ordinal).Count();
        var holdingChanged = holdings.GroupBy(static holding => holding.Lot.LotId)
            .Any(group => group.Select(holding => (holding.OpenShare, holding.AccrualFactor)).Distinct().Skip(1).Any());
        return BuildResult(context, securityId, ticker, financialAccountId, windowStart, journals, lotCount, holdingChanged,
            receiptHash, receiptModel);
    }

    private static RecurringJournalCalculationResult BuildResult(
        RecurringJournalCalculationContext context, Guid securityId, string ticker, string? financialAccountId,
        DateOnly windowStart, IReadOnlyList<JournalEntry> journals, int lotCount, bool holdingChanged,
        string? receiptHash = null, string? receiptModel = null)
    {
        var lines = journals.SelectMany(journal => journal.Lines)
            .GroupBy(line => line.Account)
            .SelectMany(group => new[]
            {
                (account: group.Key, debit: group.Sum(line => line.Debit), credit: 0m, dimensions: (LedgerLineDimensionSet?)null),
                (account: group.Key, debit: 0m, credit: group.Sum(line => line.Credit), dimensions: (LedgerLineDimensionSet?)null)
            })
            .Where(line => line.debit != 0m || line.credit != 0m)
            .ToArray();
        if (lines.Length == 0)
            return RecurringJournalCalculationResult.NothingDue(
                $"{ticker} accrues no interest or amortization between {windowStart:yyyy-MM-dd} and {context.EffectiveDate:yyyy-MM-dd}.");

        var total = lines.Sum(line => line.debit);
        var description = FormattableString.Invariant(
            $"Fixed-income accrual and amortization {ticker} for {windowStart:yyyy-MM-dd} to {context.EffectiveDate:yyyy-MM-dd} ({RecurringJournalCalculationMath.Money(total)} from {lotCount} lot(s){(holdingChanged ? ", holding changed during the window" : string.Empty)})");
        var tags = receiptHash is null ? null : new Dictionary<string, string>
        {
            ["cash-flow.receipt.sha256"] = receiptHash,
            ["cash-flow.calculationModel"] = receiptModel!
        };
        JournalEvidenceReference[] receiptEvidence = receiptHash is null ? [] :
        [
            new($"cash-flow-receipt:{receiptHash}", $"evidence://security-master/cash-flow-receipts/{receiptHash}",
                "cash-flow-calculation-receipt", "SecurityMaster", DateTimeOffset.UtcNow,
                nameof(SecurityMasterCashFlowService), SubjectId: securityId.ToString("D"), ContentHash: receiptHash,
                Description: "Retained fixed-income accrual calculation", SourceReference: receiptModel,
                EffectiveDate: context.EffectiveDate, SubjectType: "Security")
        ];
        var journalEvent = new AutomatedJournalEvent(AutomatedJournalEventKind.RecurringTemplate, ticker, total,
            context.OccurredAtUtc, financialAccountId, description, securityId, EffectiveDate: context.EffectiveDate);
        var metadata = new JournalEntryMetadata(ActivityType: AmortizationActivity, Symbol: ticker, SecurityId: securityId,
            FinancialAccountId: financialAccountId, EffectiveDate: context.EffectiveDate, Tags: tags,
            EvidenceReferences: receiptEvidence);
        return new(new AutomatedJournalDraft(journalEvent, description, lines, metadata), description);
    }

    private static bool InWindow(DateOnly date, DateOnly start, DateOnly end) => date >= start && date <= end;

    private static bool SameHolding(LedgerTaxLot[] left, LedgerTaxLot[] right)
        => left.SequenceEqual(right);
}
