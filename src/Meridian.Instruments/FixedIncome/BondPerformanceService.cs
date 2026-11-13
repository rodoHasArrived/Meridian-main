using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.SecurityMaster;
using Meridian.Instruments.AssetOperations;

namespace Meridian.Instruments.FixedIncome;

/// <summary>
/// Closing-mark to closing-mark explanation from accepted account evidence. The sequential model
/// order is carry/roll, rates, spread, inflation, collection/costs and FX; the residual is explicit.
/// Accounting income is displayed separately and never added to economic return.
/// </summary>
public sealed class BondPerformanceService(IAccountBondHoldingsSource holdings, IBondWorkflowService bonds,
    IBondMonitoringService monitoring, ISecurityMasterQueryService securities,
    ISecurityCurveStore? curves = null, IAccountCashForecastService? forecasts = null,
    IBondIncomeService? accounting = null, IBondPerformanceTransactionSource? transactions = null,
    TimeProvider? clock = null, IBondPerformanceSourceVerifier? inputVerifier = null) : IBondPerformanceService
{
    public const string ModelVersion = "bond-performance-sequential-v1";
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public async Task<BondPerformanceResult> ExplainAsync(BondPerformanceRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!AccountCashForecastEngine.IsCompleteScope(request.Scope) || !Guid.TryParse(request.Scope.AccountId, out var accountId)
            || request.PeriodStart == default || request.PeriodEnd <= request.PeriodStart
            || request.KnownAt == default || request.KnownAt.Offset != TimeSpan.Zero || request.KnownAt > _clock.GetUtcNow()
            || request.PeriodEnd > DateOnly.FromDateTime(request.KnownAt.UtcDateTime)
            || request.TaxLotRecordIds is null || request.TaxLotRecordIds.Any(id => id == Guid.Empty)
            || request.TaxLotRecordIds.Distinct().Count() != request.TaxLotRecordIds.Count)
            throw new ArgumentException("Performance requires an exact account/book, two elapsed closing-mark dates, UTC cutoff and distinct lot selectors.");
        var opening = await holdings.GetHoldingsAsync(accountId, request.PeriodStart, request.KnownAt, ct).ConfigureAwait(false);
        var closing = await holdings.GetHoldingsAsync(accountId, request.PeriodEnd, request.KnownAt, ct).ConfigureAwait(false);
        if (opening is not null && (opening.AccountId != accountId || opening.AsOfDate > request.PeriodStart)
            || closing is not null && (closing.AccountId != accountId || closing.AsOfDate > request.PeriodEnd))
            throw new InvalidDataException("Performance holdings do not match the requested account and economic dates.");
        var gaps = new List<string>();
        if (opening is null)
            gaps.Add("Accepted opening holdings are unavailable.");
        else if (opening.AsOfDate != request.PeriodStart)
            gaps.Add("The opening holdings mark predates the requested closing-mark date.");
        if (closing is null)
            gaps.Add("Accepted closing holdings are unavailable.");
        else if (closing.AsOfDate != request.PeriodEnd)
            gaps.Add("The closing holdings mark predates the requested closing-mark date.");
        var history = await monitoring.GetHistoryAsync(request.Scope, ct).ConfigureAwait(false);
        var monitor = history.Where(row => row.Inputs.RecordedAtUtc <= request.KnownAt)
            .OrderBy(row => row.Inputs.RecordedAtUtc).LastOrDefault();
        if (monitor is not null && monitor.Inputs.Payments.Count > 0)
        {
            if (inputVerifier is null)
                throw new InvalidOperationException("Current retained cash-source verification is required for performance.");
            await inputVerifier.VerifyMonitoringAsync(monitor.Inputs, ct).ConfigureAwait(false);
        }
        var cash = CollectCash(monitor, request, gaps);
        var transactionEvidence = transactions is null ? null
            : await transactions.GetAsync(request.Scope, request.PeriodStart, request.PeriodEnd, request.KnownAt, ct).ConfigureAwait(false);
        if (transactionEvidence is null || !transactionEvidence.Complete)
            gaps.Add("Complete transaction and cash-observation coverage is unavailable; mark-and-observed-cash change is partial, not a certified investment return.");
        var paymentPopulationAvailable = monitor is not null || transactionEvidence is { CashPopulationComplete: true, PaymentVersionIds.Count: 0 };
        if (!paymentPopulationAvailable)
            gaps.Add("No retained payment population is available; an empty observation list does not prove zero income.");
        if (transactionEvidence is not null)
            gaps.AddRange(transactionEvidence.CoverageGaps);
        if (transactionEvidence?.CashPopulationComplete == true)
        {
            var allPayments = monitor?.Inputs.Payments ?? [];
            var supersededIds = allPayments.Select(row => row.SupersedesVersionId).Where(id => id is not null).ToHashSet(StringComparer.Ordinal);
            var consumed = allPayments.Where(row => row.Status == AccountCashFlowStatus.Actual && !supersededIds.Contains(row.VersionId)
                    && row.CashReceiptDate > request.PeriodStart && row.CashReceiptDate <= request.PeriodEnd
                    && row.Provenance.Evidence.RetainedAtUtc <= request.KnownAt)
                .Select(row => row.VersionId).Order(StringComparer.Ordinal).ToArray();
            if (!consumed.SequenceEqual(transactionEvidence.PaymentVersionIds.Order(StringComparer.Ordinal), StringComparer.Ordinal))
                gaps.Add("The cash population changed during performance assembly; reload the exact retained transaction/payment sources.");
        }
        var fxHistory = forecasts is null ? [] : await forecasts.GetHistoryAsync(request.Scope, ct).ConfigureAwait(false);
        var fx = fxHistory.Where(row => row.Inputs.RecordedAtUtc <= request.KnownAt).SelectMany(row => row.Inputs.FxConversions)
            .Where(row => row.Provenance.Evidence.RetainedAtUtc <= request.KnownAt && row.BaseCurrency == request.Scope.BaseCurrency)
            .GroupBy(row => (row.Currency, row.BaseCurrency, row.ConversionDate, row.Provenance.SourceDomain, row.Provenance.SourceEntityId))
            .SelectMany(group => group.Where(row => row.Provenance.Evidence.EvidenceVersion == group.Max(item => item.Provenance.Evidence.EvidenceVersion)))
            .ToArray();
        if (fx.Length > 0)
        {
            if (inputVerifier is null)
                throw new InvalidOperationException("Current retained FX-source verification is required for performance.");
            await inputVerifier.VerifyFxAsync(request.Scope, fx, ct).ConfigureAwait(false);
        }
        var first = Groups(opening);
        var last = Groups(closing);
        if (opening?.Positions.Any(row => row.Bond is null || row.SecurityId is null) == true
            || closing?.Positions.Any(row => row.Bond is null || row.SecurityId is null) == true)
            gaps.Add("Some accepted positions lack resolved bond economics and are excluded from bond performance.");
        var rows = new List<BondPerformanceSecurityDto>();
        foreach (var id in first.Keys.Union(last.Keys).Concat(cash.Select(row => row.SecurityId))
            .Concat(transactionEvidence?.TradeCash.Select(row => row.SecurityId) ?? []).Distinct().Order())
        {
            ct.ThrowIfCancellationRequested();
            var rowGaps = new List<string>();
            var startRows = first.GetValueOrDefault(id) ?? [];
            var endRows = last.GetValueOrDefault(id) ?? [];
            var currencies = startRows.Concat(endRows).Select(row => row.Currency)
                .Concat(cash.Where(row => row.SecurityId == id).Select(row => row.Currency))
                .Concat(transactionEvidence?.TradeCash.Where(row => row.SecurityId == id).Select(row => row.Currency) ?? []).Distinct().ToArray();
            if (currencies.Length != 1)
            { gaps.Add($"Security {id:D} has missing or competing native currencies."); continue; }
            var currency = currencies[0];
            var startMark = opening is null ? (decimal?)null : Mark(startRows);
            var endMark = closing is null ? (decimal?)null : Mark(endRows);
            var received = cash.Where(row => row.SecurityId == id).ToArray();
            var netCash = received.Sum(row => row.Net);
            var trades = transactionEvidence?.TradeCash.Where(row => row.SecurityId == id
                && row.Currency == currency && row.CashDate > request.PeriodStart && row.CashDate <= request.PeriodEnd).ToArray() ?? [];
            var tradeCash = trades.Sum(row => row.Amount);
            var observed = endMark - startMark + netCash + tradeCash;
            if (transactionEvidence?.TradeCash.Any(row => row.SecurityId == id && row.Currency != currency) == true)
                rowGaps.Add("Trade cash has a different currency and needs explicit transaction normalization.");
            if (trades.Any(row => row.OriginalFaceDelta is null)
                || Face(endRows) - Face(startRows) != trades.Sum(row => row.OriginalFaceDelta ?? 0m))
                rowGaps.Add("Boundary face changes are not explained by verified trade/redemption quantities; transfers and other external in-kind flows cannot be counted as investment return.");
            var dataComplete = rowGaps.Count == 0;
            var sourceInputs = new List<BondCalculationInput>();
            if (opening is not null)
                sourceInputs.Add(new("OpeningHoldings", opening.BatchId.ToString("D"), ContentSha256: opening.SourceContentHash));
            if (closing is not null)
                sourceInputs.Add(new("ClosingHoldings", closing.BatchId.ToString("D"), ContentSha256: closing.SourceContentHash));
            sourceInputs.AddRange(received.SelectMany(row => row.Inputs));
            sourceInputs.AddRange(transactionEvidence?.Inputs ?? []);
            sourceInputs.AddRange(trades.Select(row => row.Evidence));
            BondPerformanceAttributionDto attribution = Empty(currency) with { Residual = observed, FxEffect = 0m };
            if (startRows.Length == 0 || endRows.Length == 0)
                rowGaps.Add("A position enters or exits during the period; a constant-position model attribution is unavailable.");
            else
            {
                try
                {
                    var original = Face(startRows);
                    if (original == 0m || original != Face(endRows))
                        throw new ArgumentException("Original face changes between marks; trade and principal histories are required for model attribution.");
                    var start = await Calculate(id, startRows, request.PeriodStart, request, ct).ConfigureAwait(false);
                    var end = await Calculate(id, endRows, request.PeriodEnd, request, ct).ConfigureAwait(false);
                    sourceInputs.AddRange(start.Inputs.Concat(end.Inputs));
                    if (start.Context.SecurityVersion != end.Context.SecurityVersion)
                        throw new ArgumentException("Contract terms change between marks; the unexplained effect remains in residual coverage.");
                    var security = await securities.GetRecordedByIdAsOfAsync(id, request.KnownAt, ct).ConfigureAwait(false)
                        ?? throw new ArgumentException("Retained terms are unavailable for attribution classification.");
                    var kind = StructuredCashFlowTermsResolver.Resolve(security).CouponKind;
                    var startCurve = await DiscountCurve(start, ct).ConfigureAwait(false);
                    var endCurve = await DiscountCurve(end, ct).ConfigureAwait(false);
                    if (start.CalibratedSpread is not { } startSpread || end.CalibratedSpread is not { } endSpread)
                        throw new ArgumentException("Opening and closing quotes require uniquely calibrated spreads before model attribution.");
                    var direction = Math.Sign(original);
                    var remaining = start.CashFlows.Where(flow => flow.PaymentDate > request.PeriodEnd).ToArray();
                    var expectedCash = start.CashFlows.Where(flow => flow.PaymentDate > request.PeriodStart && flow.PaymentDate <= request.PeriodEnd).Sum(flow => direction * flow.Total);
                    var roll = direction * Value(remaining, request.PeriodEnd, startCurve, startSpread);
                    var rateFlows = kind == SecurityCouponKind.Floating ? end.CashFlows : remaining;
                    var rate = direction * Value(rateFlows, request.PeriodEnd, endCurve, startSpread);
                    var spread = direction * Value(rateFlows, request.PeriodEnd, endCurve, endSpread);
                    var indexed = kind == SecurityCouponKind.InflationLinked
                        ? direction * Value(end.CashFlows, request.PeriodEnd, endCurve, endSpread) : spread;
                    var carryEffect = roll + expectedCash - startMark!.Value;
                    var rateEffect = rate - roll;
                    var spreadEffect = spread - rate;
                    var inflationEffect = indexed - spread;
                    var collection = netCash - expectedCash;
                    var residual = observed!.Value - carryEffect - rateEffect - spreadEffect - inflationEffect - collection;
                    attribution = new(carryEffect, rateEffect, spreadEffect, inflationEffect, collection, 0m, residual, currency);
                }
                catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or ArithmeticException)
                { rowGaps.Add(exception.Message); }
            }
            var economic = transactionEvidence?.Complete == true && paymentPopulationAvailable && gaps.Count == 0 && dataComplete
                ? observed : null;
            var baseAttribution = Convert(attribution, startMark, endMark, received, trades, request, fx, sourceInputs, rowGaps,
                out var baseObserved);
            rows.Add(new(id, currency, startMark, endMark, netCash, received.Sum(row => row.GrossCoupon), received.Sum(row => row.Principal),
                received.Sum(row => row.Tax), received.Sum(row => row.Fee), observed, economic, attribution, baseAttribution,
                baseObserved, sourceInputs.Distinct().ToArray(), rowGaps)
            { TradeSettlementCash = tradeCash });
        }
        var accountingRows = await AccountingBridge(request, rows, closing, gaps, ct).ConfigureAwait(false);
        decimal? baseTotal = rows.Count > 0 && rows.All(row => row.BaseObservedMarkAndCashChange.HasValue)
            ? rows.Sum(row => row.BaseObservedMarkAndCashChange!.Value) : null;
        return new(request.Scope, request.PeriodStart, request.PeriodEnd, request.KnownAt, ModelVersion,
            opening?.BatchId, closing?.BatchId, rows, accountingRows, baseTotal,
            gaps.Count == 0 && rows.All(row => row.CoverageGaps.Count == 0), gaps.Distinct().ToArray());
    }

    private static Dictionary<Guid, CustodianPositionLineDto[]> Groups(AccountBondHoldingsSnapshotDto? snapshot)
        => (snapshot?.Positions ?? []).Where(row => row.Bond is not null && row.SecurityId is { } id && id != Guid.Empty)
            .GroupBy(row => row.SecurityId!.Value).ToDictionary(group => group.Key, group => group.ToArray());

    private static decimal Face(IEnumerable<CustodianPositionLineDto> rows)
        => rows.Sum(row => row.IsShort ? -Math.Abs(row.Bond!.OriginalFace) : row.Bond!.OriginalFace);

    private static decimal Mark(IEnumerable<CustodianPositionLineDto> rows, bool clean = false)
        => rows.Sum(row =>
        {
            var values = BondHoldingNormalization.Calculate(row.Bond!, row.Currency);
            var direction = row.IsShort && row.Bond!.OriginalFace > 0m ? -1m : 1m;
            return direction * (clean ? values.Clean : values.Dirty);
        });

    private async Task<BondWorkflowResult> Calculate(Guid securityId, CustodianPositionLineDto[] positions,
        DateOnly date, BondPerformanceRequest request, CancellationToken ct)
    {
        if (positions.Select(row => row.Bond!.PrincipalFactor).Distinct().Count() != 1
            || positions.Select(row => row.Bond!.InflationRatio).Distinct().Count() != 1
            || positions.Select(row => row.Bond!.InvoiceBasis).Distinct().Count() != 1)
            throw new ArgumentException("A security's held lots have conflicting factor or invoice bases.");
        var current = positions.Sum(row => row.IsShort ? -Math.Abs(row.Bond!.CurrentFace) : row.Bond!.CurrentFace);
        if (current == 0m)
            throw new ArgumentException("A fully repaid holding has no current-face quote basis.");
        var invoice = positions[0].Bond!;
        var ratio = invoice.InvoiceBasis == BondHoldingInvoiceBasis.IndexedPrincipal ? invoice.InflationRatio : 1m;
        var cleanQuote = Mark(positions, true) / current / ratio * 100m;
        var result = await bonds.CalculateAsync(new(securityId,
            new(new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero), date, request.KnownAt),
            Math.Abs(Face(positions)), cleanQuote, PriceBasis: invoice.InvoiceBasis == BondHoldingInvoiceBasis.IndexedPrincipal
                ? BondWorkflowPriceBasis.RealClean : BondWorkflowPriceBasis.NominalClean)
        { AccountId = request.Scope.AccountId }, ct).ConfigureAwait(false)
            ?? throw new ArgumentException("Retained bond model is unavailable.");
        if (result.IsBlocked || result.Currency != positions[0].Currency || result.CurrentFace != Math.Abs(current))
            throw new ArgumentException("Retained bond model cannot reproduce the accepted mark's currency and face/factor economics.");
        var acceptedDirty = Math.Abs(Mark(positions));
        if (result.Context.SettlementDate != date || DateOnly.FromDateTime(result.Context.AsOf.UtcDateTime) != date
            || result.DirtyConsideration is not { } modeledDirty
            || LedgerCurrencyRounding.RoundCurrency(modeledDirty, result.Currency)
                != LedgerCurrencyRounding.RoundCurrency(acceptedDirty, result.Currency)
            || invoice.InvoiceBasis == BondHoldingInvoiceBasis.IndexedPrincipal && result.SettlementIndexRatio != invoice.InflationRatio)
            throw new ArgumentException("Retained bond model cannot reproduce the accepted same-date dirty mark, accrued interest or indexed invoice ratio; model attribution is unavailable.");
        return result;
    }

    private async Task<SecurityCurveVersionDto> DiscountCurve(BondWorkflowResult model, CancellationToken ct)
    {
        var input = model.Inputs.SingleOrDefault(row => row.Kind == "Curve:Discount")
            ?? throw new ArgumentException("A retained discount curve is missing from the model evidence.");
        if (curves is null || input.VersionId is not { } versionId)
            throw new ArgumentException("Retained discount-curve authority is unavailable.");
        var definition = (await curves.ListDefinitionsAsync(SecurityCurveKind.Discount, model.Currency, ct: ct).ConfigureAwait(false))
            .SingleOrDefault(row => row.CurveKey == input.Key) ?? throw new ArgumentException("Selected discount-curve identity is unavailable.");
        var version = await curves.GetVersionByIdAsync(definition.CurveId, versionId, ct).ConfigureAwait(false)
            ?? throw new ArgumentException("Selected discount-curve version is unavailable.");
        if (version.ContentSha256 != input.ContentSha256 || version.RecordedAt > model.Context.KnownAt)
            throw new InvalidDataException("Retained discount-curve identity no longer matches model evidence.");
        return version;
    }

    private static decimal Value(IReadOnlyList<BondWorkflowCashFlow> flows, DateOnly date,
        SecurityCurveVersionDto curve, decimal spread)
    {
        if (flows.Count == 0)
            return 0m;
        if (curve.AsOf > date)
            throw new ArgumentException("Attribution date precedes the retained discount curve.");
        var rebasing = SecurityCurveRules.ValueAt(curve, SecurityCurveKind.Discount, date)
            ?? throw new ArgumentException("Attribution roll date exceeds curve coverage.");
        var discounted = flows.Where(flow => flow.PaymentDate > date).Select(flow => new BondWorkflowDiscountedFlow(flow.Total,
            (flow.PaymentDate.DayNumber - date.DayNumber) / 365m,
            (SecurityCurveRules.ValueAt(curve, SecurityCurveKind.Discount, flow.PaymentDate)
                ?? throw new ArgumentException("Attribution cash flow exceeds curve coverage.")) / rebasing)).ToArray();
        return discounted.Length == 0 ? 0m : BondWorkflowPricing.PresentValue(discounted, spread);
    }

    private static BondPerformanceAttributionDto Empty(string currency) => new(null, null, null, null, null, null, null, currency);

    private sealed record Cash(Guid SecurityId, DateOnly Date, decimal Net, decimal GrossCoupon, decimal Principal,
        decimal Tax, decimal Fee, string Currency, IReadOnlyList<BondCalculationInput> Inputs);

    private static IReadOnlyList<Cash> CollectCash(BondMonitoringSnapshotDto? snapshot, BondPerformanceRequest request, List<string> gaps)
    {
        if (snapshot is null)
            return [];
        if (snapshot.Inputs.Scope != request.Scope)
            throw new InvalidDataException("Monitoring payment scope differs from the performance account/book.");
        var payments = snapshot.Inputs.Payments;
        var superseded = payments.Select(row => row.SupersedesVersionId).Where(id => id is not null).ToHashSet(StringComparer.Ordinal);
        var obligations = snapshot.Inputs.Obligations.ToDictionary(row => row.ObligationId, StringComparer.Ordinal);
        var result = new List<Cash>();
        foreach (var payment in payments.Where(row => row.Status == AccountCashFlowStatus.Actual && !superseded.Contains(row.VersionId)
            && row.CashReceiptDate > request.PeriodStart && row.CashReceiptDate <= request.PeriodEnd
            && row.Provenance.Evidence.RetainedAtUtc <= request.KnownAt))
        {
            var allocations = snapshot.Inputs.Allocations.Where(row => row.ActualVersionId == payment.VersionId).ToArray();
            if (allocations.Sum(row => row.NetAmount) != payment.Amount)
                gaps.Add($"Payment {payment.VersionId} contains unallocated cash; bond-level income coverage is incomplete.");
            foreach (var allocation in allocations)
            {
                if (!obligations.TryGetValue(allocation.ExpectedFlowId, out var obligation))
                    throw new InvalidDataException("Retained payment allocation has no obligation identity.");
                result.Add(new(obligation.SecurityId, payment.CashReceiptDate!.Value, allocation.NetAmount,
                    obligation.FlowType == "Coupon" ? allocation.GrossAmount : 0m,
                    obligation.FlowType == "Principal" ? allocation.GrossAmount : 0m, allocation.WithholdingTax,
                    allocation.Fees, payment.Currency,
                    [new("ActualPayment", payment.VersionId, ContentSha256: payment.Provenance.SourceContentHash,
                        RecordedAt: payment.Provenance.Evidence.RetainedAtUtc)]));
            }
        }
        return result;
    }

    private static BondPerformanceAttributionDto? Convert(BondPerformanceAttributionDto native, decimal? opening, decimal? closing,
        Cash[] cash, BondPerformanceTradeCashDto[] trades, BondPerformanceRequest request, AccountCashFxConversionDto[] fx,
        List<BondCalculationInput> inputs, List<string> gaps, out decimal? baseObserved)
    {
        baseObserved = null;
        decimal Rate(DateOnly date)
        {
            if (native.Currency == request.Scope.BaseCurrency)
                return 1m;
            var matches = fx.Where(row => row.Currency == native.Currency && row.ConversionDate == date).ToArray();
            if (matches.Length == 0 || matches.Select(row => row.BaseUnitsPerCurrencyUnit).Distinct().Count() != 1
                || matches[0].BaseUnitsPerCurrencyUnit <= 0m)
                throw new ArgumentException($"Dated {native.Currency}/{request.Scope.BaseCurrency} FX evidence is missing or conflicting for {date:yyyy-MM-dd}.");
            inputs.AddRange(matches.Select(row => new BondCalculationInput("PerformanceFx", row.Provenance.SourceEntityId,
                ContentSha256: row.Provenance.SourceContentHash, RecordedAt: row.Provenance.Evidence.RetainedAtUtc)));
            return matches[0].BaseUnitsPerCurrencyUnit;
        }
        try
        {
            if (opening is null || closing is null)
                return null;
            var first = Rate(request.PeriodStart);
            var last = Rate(request.PeriodEnd);
            var receipts = cash.Sum(row => row.Net * Rate(row.Date));
            var traded = trades.Sum(row => row.Amount * Rate(row.CashDate));
            baseObserved = closing * last - opening * first + receipts + traded;
            var fxEffect = closing * (last - first) + cash.Sum(row => row.Net * (Rate(row.Date) - first))
                + trades.Sum(row => row.Amount * (Rate(row.CashDate) - first));
            return new(native.CarryAndRoll * first, native.RateEffect * first, native.SpreadEffect * first,
                native.InflationEffect * first, native.CollectionAndCosts * first, fxEffect, native.Residual * first,
                request.Scope.BaseCurrency);
        }
        catch (ArgumentException exception) { gaps.Add(exception.Message); return null; }
    }

    private async Task<IReadOnlyList<BondPerformanceAccountingBridgeDto>> AccountingBridge(BondPerformanceRequest request,
        IReadOnlyList<BondPerformanceSecurityDto> performance, AccountBondHoldingsSnapshotDto? closing,
        List<string> gaps, CancellationToken ct)
    {
        if (accounting is null || request.TaxLotRecordIds.Count == 0 || !Guid.TryParse(request.Scope.BookId, out var bookId))
        { gaps.Add("Accounting income needs authoritative book/lot selectors and its governed income policy; market return remains separate."); return []; }
        var values = new List<BondPerformanceAccountingBridgeDto>();
        var coveredFace = new Dictionary<Guid, decimal>();
        foreach (var lotId in request.TaxLotRecordIds)
        {
            try
            {
                var end = await accounting.PreviewAsync(new(bookId, lotId, request.PeriodEnd, request.KnownAt,
                    Guid.Parse(request.Scope.AccountId)), ct).ConfigureAwait(false);
                var lot = end.Instruction.ExpectedLot;
                if (!performance.Any(row => row.SecurityId == lot.SecurityId))
                    throw new ArgumentException("A selected accounting lot does not belong to the period's accepted bond population.");
                if (lot.AcquiredDate >= request.PeriodStart)
                    throw new ArgumentException("A selected accounting lot was acquired during the performance period; a separate acquisition bridge is required.");
                if (lot.OpenQuantity != lot.OriginalQuantity)
                    throw new ArgumentException("A selected lot has partial relief; historical quantity evidence is required before explaining its period accounting income.");
                var start = await accounting.PreviewAsync(new(bookId, lotId, request.PeriodStart, request.KnownAt,
                    Guid.Parse(request.Scope.AccountId)), ct).ConfigureAwait(false);
                var coupon = end.Income.CouponIncome - start.Income.CouponIncome;
                var incomeInstruction = end.Instruction.BondIncome
                    ?? throw new InvalidOperationException("The selected lot lacks the governed bond-income instruction.");
                coveredFace[lot.SecurityId] = coveredFace.GetValueOrDefault(lot.SecurityId) + incomeInstruction.PrincipalForIndexation;
                var cashCoupons = performance.Single(row => row.SecurityId == lot.SecurityId).CashGrossCoupons;
                values.Add(new(lot.SecurityId, [lotId], start.Income.ClosingCarryingBasis, end.Income.ClosingCarryingBasis,
                    coupon, end.Income.PremiumDiscountIncome - start.Income.PremiumDiscountIncome,
                    end.Income.IndexationIncome - start.Income.IndexationIncome, end.Income.ExpensedFees - start.Income.ExpensedFees,
                    cashCoupons, coupon - cashCoupons, lot.Acquisition.AcquisitionCurrency, [start.Fingerprint, end.Fingerprint]));
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or NotSupportedException)
            { gaps.Add($"Accounting lot {lotId:D}: {exception.Message}"); }
        }
        foreach (var group in (closing?.Positions ?? []).Where(row => row.Bond is not null && row.SecurityId is not null).GroupBy(row => row.SecurityId!.Value))
        {
            var expectedFace = group.Sum(row => row.IsShort ? -Math.Abs(row.Bond!.CurrentFace) : row.Bond!.CurrentFace);
            if (coveredFace.GetValueOrDefault(group.Key) != expectedFace)
                gaps.Add($"Accounting lot coverage for security {group.Key:D} does not reconcile to the accepted closing face; the bridge covers selected lots only.");
        }
        return values.GroupBy(row => (row.SecurityId, row.Currency)).Select(group => new BondPerformanceAccountingBridgeDto(
            group.Key.SecurityId, group.SelectMany(row => row.TaxLotRecordIds).ToArray(), group.Sum(row => row.OpeningCarryingBasis),
            group.Sum(row => row.ClosingCarryingBasis), group.Sum(row => row.CouponIncome), group.Sum(row => row.PremiumDiscountIncome),
            group.Sum(row => row.IndexationIncome), group.Sum(row => row.ExpensedFees), group.First().CashGrossCoupons,
            group.Sum(row => row.CouponIncome) - group.First().CashGrossCoupons, group.Key.Currency,
            group.SelectMany(row => row.InstructionFingerprints).ToArray())).ToArray();
    }
}
