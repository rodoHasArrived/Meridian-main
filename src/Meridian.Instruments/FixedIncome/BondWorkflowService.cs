using System.Text.Json;
using Meridian.Contracts.Calendars;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.SecurityMaster.Schedules;

namespace Meridian.Instruments.FixedIncome;

/// <summary>Retained-term adapter for signed bond workflows; calculations grant no posting authority.</summary>
public sealed class BondWorkflowService(ISecurityMasterQueryService securities,
    IBusinessDayCalendarSource? calendars = null, ISecurityCurveStore? curves = null,
    IBondWorkflowObservationSource? observations = null, IBondCalculationPolicy? policy = null,
    IBondWorkflowQuoteSource? quotes = null, ISecurityMasterPricingService? pricing = null) : IBondWorkflowService
{
    public const string ModelVersion = BondWorkflowModelVersions.Current;
    public const string LegacyModelVersion = BondWorkflowModelVersions.Legacy;

    public Task<BondWorkflowResult?> CalculateAsync(BondWorkflowRequest request, CancellationToken ct = default)
        => CalculateCoreAsync(request, null, ct);

    public Task<BondWorkflowResult?> CalculateIncomeScheduleAsync(BondWorkflowRequest request, DateOnly freezeIndexAt,
        CancellationToken ct = default) => CalculateCoreAsync(request, freezeIndexAt, ct);

    public async Task<BondWorkflowResult?> ReplayAsync(BondWorkflowResult retained, CancellationToken ct = default)
    {
        if (retained.Request is not { } request || retained.ModelVersion is not (ModelVersion or LegacyModelVersion)
            || request.ModelVersion is { } pinned && pinned != retained.ModelVersion)
            throw new ArgumentException("Retained workflow replay requires its original request and exact supported model version.");
        if (request.Context.AsOf != retained.Context.AsOf
            || request.Context.KnownAt is { } known && known != retained.Context.KnownAt
            || request.Context.SettlementDate is { } settlement && settlement != retained.Context.SettlementDate
            || request.Context.SecurityVersion is { } securityVersion && securityVersion != retained.Context.SecurityVersion)
            throw new ArgumentException("Retained workflow request cutoffs or terms version disagree with its resolved context.");
        request = request with
        {
            Context = request.Context with
            {
                KnownAt = request.Context.KnownAt ?? retained.Context.KnownAt,
                SettlementDate = request.Context.SettlementDate ?? retained.Context.SettlementDate,
                SecurityVersion = request.Context.SecurityVersion ?? retained.Context.SecurityVersion
            }
        };
        request = await PinRetainedCurvesAsync(retained, request, ct).ConfigureAwait(false);
        var replay = await CalculateCoreAsync(request with { ModelVersion = retained.ModelVersion }, retained.FrozenIndexAt, ct).ConfigureAwait(false);
        // Historical receipts omitted resolved pins and, in v1, their model selector. Recalculate
        // their exact retained sources, then preserve the original request/context serialization.
        return replay is null ? null : replay with { Request = retained.Request, Context = retained.Context };
    }

    private async Task<BondWorkflowResult?> CalculateCoreAsync(BondWorkflowRequest request, DateOnly? freezeIndexAt, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Context);
        var modelVersion = request.ModelVersion ?? ModelVersion;
        var legacyAccrual = modelVersion == LegacyModelVersion;
        var context = request.Context with
        {
            SettlementDate = request.Context.SettlementDate ?? DateOnly.FromDateTime(request.Context.AsOf.UtcDateTime),
            KnownAt = request.Context.KnownAt ?? DateTimeOffset.UtcNow
        };
        var security = request.Context.KnownAt is { } cutoff
            ? await securities.GetRecordedByIdAsOfAsync(request.SecurityId, cutoff, ct).ConfigureAwait(false)
            : await securities.GetByIdAsync(request.SecurityId, ct).ConfigureAwait(false);
        if (security is null)
            return null;
        var inputs = new List<BondCalculationInput>
        {
            new("SecurityTerms", security.SecurityId.ToString("D"), security.Version,
                Sha256Digest.ComputeUtf8(security.CommonTerms.GetRawText() + "\n" + security.AssetSpecificTerms.GetRawText())),
            new("CalculationModel", modelVersion), new("CashFlowModel", legacyAccrual ? BondWorkflowProjection.LegacyModelVersion : BondWorkflowProjection.ModelVersion),
            new("PricingModel", BondWorkflowPricing.ModelVersion), new("CurvePolicy", "signed-positive-discount-v1")
        };
        var originalRequest = legacyAccrual ? request : request with { ModelVersion = modelVersion };
        if (freezeIndexAt is { } frozenDate)
            inputs.Add(new("IncomeProjectionPolicy", "FrozenIndex:" + frozenDate.ToString("yyyy-MM-dd")));
        var issues = new List<BondWorkflowIssue>();
        BondWorkflowResult Block(string code, string reason) => new(request.SecurityId, security.Currency, modelVersion,
            context, request.OriginalFace, null, null, null, null, request.Fees, null, null, null, null,
            null, null, null, [], inputs, [new("Projection", code, reason, true)], [])
        { Request = originalRequest with { Context = context } };
        if (modelVersion is not (ModelVersion or LegacyModelVersion))
            return Block("UnsupportedModelVersion", "Choose the current workflow model or an exact retained legacy version.");
        if (context.AsOf == default || context.AsOf.Offset != TimeSpan.Zero || context.KnownAt?.Offset != TimeSpan.Zero
            || request.OriginalFace <= 0m || request.Fees < 0m || request.CleanPricePer100 < 0m
            || !Enum.IsDefined(request.PriceBasis))
            return Block("InvalidRequest", "Explicit UTC cutoffs, positive original face and nonnegative quote/fees are required.");
        if (context.ObservedFixingReceiptIds is { Count: > 0 })
            return Block("UnsupportedObservedFixingSelection", "This workflow cannot consume legacy observed-fixing receipt pins. Replay the selected receipts with floating v3 analytics; this workflow requires retained typed bond-market observations.");
        if (security.EffectiveFrom > context.AsOf || security.EffectiveTo is { } end && context.AsOf >= end
            || context.SecurityVersion is { } version && security.Version != version)
            return Block("TermsNotEffective", "Retained terms do not match the requested economic date/version.");
        context = context with { SecurityVersion = security.Version };
        if (!SecurityAssetClassCatalog.GetOrDefault(security.AssetClass).CarriesBondCouponStructure)
            return Block("UnsupportedSecurity", "Bond workflows require retained bond terms.");
        if (policy is not null && await policy.GetBlockerAsync(security, ct).ConfigureAwait(false) is { } refused)
            return Block("CalculationPolicyBlocked", refused);
        try
        {
            var quoteIsDirty = false;
            if (request.CleanPricePer100 is not null && (request.QuoteObservationId is not null || request.PriceSelectionReceiptId is not null))
                return Block("ConflictingQuoteInputs", "Choose a retained quote or an explicitly entered clean price.");
            BondMarketObservationDto? selectedQuote = null;
            DateTimeOffset? quoteObservedAt = null;
            if (request.QuoteObservationId is { } quoteId)
            {
                selectedQuote = quotes is null ? null : await quotes.GetAsync(quoteId, ct).ConfigureAwait(false);
                quoteObservedAt = selectedQuote?.Input.Quote?.ObservedAtUtc ?? selectedQuote?.Input.PublishedAtUtc;
                if (selectedQuote is null || selectedQuote.Input.Kind != BondMarketObservationKind.Quote
                    || selectedQuote.Input.SecurityId != request.SecurityId || selectedQuote.Input.Currency != security.Currency
                    || selectedQuote.Input.Unit != BondMarketValueUnit.PercentOfPar || selectedQuote.Input.Quote is null
                    || selectedQuote.RecordedAtUtc > context.KnownAt || selectedQuote.Input.PublishedAtUtc > context.KnownAt
                    || selectedQuote.Input.EconomicDate > DateOnly.FromDateTime(context.AsOf.UtcDateTime)
                    || quoteObservedAt is null || quoteObservedAt > context.AsOf)
                    return Block("RetainedQuoteUnavailable", "The selected quote is absent, outside scope/cutoff or has incompatible units/currency.");
                request = request with
                {
                    CleanPricePer100 = selectedQuote.Input.Value,
                    QuoteSettlementDate = selectedQuote.Input.Quote.SettlementDate
                };
                quoteIsDirty = selectedQuote.Input.Quote.PriceBasis == BondQuotePriceBasis.Dirty;
                inputs.Add(new("QuoteObservation", quoteId.ToString("D"), selectedQuote.Input.Revision,
                    selectedQuote.ContentSha256, quoteId, quoteObservedAt, selectedQuote.RecordedAtUtc));
                if (selectedQuote.Input.Quote.ExpiresAtUtc < context.AsOf)
                    issues.Add(new("Quote", "QuoteExpired", "The retained executable quote expired before the analysis cutoff; it does not establish current executable liquidity."));
            }
            if (request.PriceSelectionReceiptId is { } receiptId)
            {
                var selection = pricing is null ? null : await pricing.GetGoldenCopySelectionAsync(request.SecurityId,
                    request.AccountId, receiptId, ct).ConfigureAwait(false);
                if (selection is null || selection.SecurityId != request.SecurityId || selection.SelectionReceiptId != receiptId
                    || selection.KnowledgeAsOf is null || selection.EvaluatedAsOf is null
                    || selection.EvaluatedAsOf > context.AsOf || selection.Unit != SecurityPriceUnit.PercentOfPar || selectedQuote is null
                    || selection.GoldenCopyPrice != selectedQuote.Input.Value || selection.PriceAsOf > context.AsOf
                    || selection.KnowledgeAsOf > context.KnownAt || selection.PriceAsOf != quoteObservedAt
                    || !(selectedQuote.Input.Quote?.PricingSourceId is { } pricingSource
                        ? selection.SelectedSource == pricingSource
                        : selection.SelectedSource == selectedQuote.Input.ProviderId || selection.SelectedSource == selectedQuote.Input.SourceName))
                    return Block("PriceSelectionUnavailable", "An exact scoped price selection and matching typed quote metadata are required.");
                if (selection.Quotation is { } quotation
                    && (quotation.Convention != SecurityQuotationConvention.Per100Face
                        || quotation.Basis != (quoteIsDirty ? SecurityPriceBasis.Dirty : SecurityPriceBasis.Clean)
                        || quotation.SettlementDate != selectedQuote.Input.Quote!.SettlementDate
                        || string.IsNullOrWhiteSpace(quotation.SettlementBasis)
                        || string.IsNullOrWhiteSpace(selectedQuote.Input.Quote.SettlementBasis)
                        || !string.Equals(quotation.SettlementBasis, selectedQuote.Input.Quote.SettlementBasis, StringComparison.Ordinal)))
                    return Block("PriceSelectionQuotationMismatch", "The retained selection's clean/dirty basis, per-100 convention and exact settlement date/basis must match the selected typed quote; retain matching settlement metadata before replay.");
                inputs.Add(new("PriceSelectionReceipt", receiptId.ToString("D"), VersionId: receiptId,
                    AsOf: selection.PriceAsOf, RecordedAt: selection.KnowledgeAsOf));
            }
            if (Meridian.Contracts.CashFlows.SecurityContractualCashFlowTerms.IsDeclared(security.AssetClass, security.AssetSpecificTerms))
                return Block("ContractCompanionUnsupported", "The declared cash-flow companion must be evaluated through its contractual projection model; scalar bond workflows cannot ignore its economics.");
            var terms = StructuredCashFlowTermsResolver.Resolve(security);
            var declared = ScheduleSpecFactory.Resolve(security, terms, calendars);
            if (declared.Status != DeclaredScheduleStatus.Declared)
                return Block("DeclaredScheduleRequired", declared.Refusal?.Message ?? "Explicit contractual schedule conventions are required.");
            var schedule = ScheduleGenerator.Generate(declared.Spec!);
            if (!schedule.Succeeded)
                return Block("ScheduleRefused", schedule.Failure.Message);
            if (schedule.Diagnostics.Any(d => d.Code == ScheduleDiagnosticCode.CalendarCoverageExceeded))
                return Block("CalendarCoverageMissing", "Contractual schedule falls outside retained calendar coverage.");
            foreach (var calendar in schedule.CalendarsUsed)
                inputs.Add(new("Calendar", calendar.CalendarId, ContentSha256: calendar.ContentHash));
            var settlement = context.SettlementDate!.Value;
            var quoteDate = request.QuoteSettlementDate ?? settlement;
            var first = settlement < quoteDate ? settlement : quoteDate;
            var lastIncomeDate = !legacyAccrual && freezeIndexAt is not null
                ? schedule.Periods.Max(period => period.PaymentDate) : terms.MaturityDate!.Value;
            if (lastIncomeDate < terms.MaturityDate)
                lastIncomeDate = terms.MaturityDate.Value;
            if (first < terms.IssueDate || settlement > lastIncomeDate || quoteDate > lastIncomeDate
                || freezeIndexAt is null && (settlement == terms.MaturityDate || quoteDate == terms.MaturityDate))
                return Block("SettlementOutsideLife", "Both settlement dates must be inside the retained bond life.");
            var raw = security.AssetSpecificTerms;
            if (raw.TryGetProperty("assetClassTermsVersion", out var workflowStamp) && workflowStamp.TryGetInt32(out var workflowVersion)
                && workflowVersion >= 5 && raw.TryGetProperty("bondWorkflow", out var workflowValue)
                && !SecurityAssetTermsFieldEditValidator.TryValidate("Bond", "assetSpecificTerms.bondWorkflow",
                    workflowValue.GetRawText(), out _, out var workflowIssue))
                return Block("WorkflowTermsInvalid", workflowIssue!);
            var block = raw.TryGetProperty("assetClassTermsVersion", out var stamp) && stamp.TryGetInt32(out var termsVersion) && termsVersion >= 5
                && raw.TryGetProperty("bondWorkflow", out var workflow) && workflow.ValueKind == JsonValueKind.Object
                ? workflow : default;
            var kind = terms.CouponKind.ToString();
            if (kind is not ("Fixed" or "ZeroCoupon" or "Floating" or "InflationLinked"))
                return Block("CouponModelUnavailable", "Only fixed, zero, simple/overnight floating and capital-indexed coupon models are supported.");
            var rateUnit = declared.Conventions.CouponRateUnit;
            if (kind != "ZeroCoupon" && rateUnit is not ("Percent" or "Fraction"))
                return Block("RateUnitRequired", "The retained schedule must declare Percent or Fraction coupon units.");
            decimal Rate(decimal value) => rateUnit == "Percent" ? value / 100m : value;
            if (kind is "Fixed" or "InflationLinked" && terms.CouponRate is null)
                return Block("CouponMissing", "A missing coupon cannot become zero.");
            if (kind is "Fixed" or "InflationLinked" && declared.Conventions.CouponCashBasis is not ("Level" or "DayCount"))
                return Block("CouponCashBasisMissing", "Fixed and indexed coupons require an explicit Level or DayCount cash basis.");
            if (!DayCountConventions.TryParse(terms.DayCountConvention, out var dayCount))
                return Block("DayCountMissing", "The bond requires a recognized contractual day count.");
            var economicDate = DateOnly.FromDateTime(context.AsOf.UtcDateTime);
            var knownAt = context.KnownAt!.Value;
            if (request.RatePillars is null || request.RatePillars.Any(p => p is null || p.Date == default)
                || request.RatePillars.GroupBy(p => p.Date).Any(g => g.Count() > 1))
                return Block("InvalidRatePillars", "Rate scenario pillars require distinct explicit dates.");
            var applyScenario = false;
            var additionalRateBps = 0m;
            if (freezeIndexAt is { } freeze && (freeze == default || freeze > economicDate))
                return Block("IncomeFreezeDateInvalid", "Income index assumptions must be frozen at an evidenced date on or before the economic cutoff.");
            var index = terms.Legs?.FirstOrDefault()?.IndexName;
            if (kind == "Floating" && string.IsNullOrWhiteSpace(index))
                return Block("FloatingIndexMissing", "Floating coupons require a retained named benchmark index.");
            var forward = kind == "Floating" && index is not null
                ? await ResolveCurve("Forward:" + index, SecurityCurveKind.Forward, index, security.Currency, context, inputs, ct).ConfigureAwait(false) : null;
            context = PinResolvedCurve(context, "Forward:" + index, forward);
            var discount = await ResolveCurve("Discount", SecurityCurveKind.Discount, null, security.Currency, context, inputs, ct).ConfigureAwait(false);
            context = PinResolvedCurve(context, "Discount", discount);
            var inflationCurve = kind == "InflationLinked" && terms.InflationIndex is { } inflationIndex
                ? await ResolveCurve("Inflation:" + inflationIndex, SecurityCurveKind.Inflation, inflationIndex, security.Currency, context, inputs, ct).ConfigureAwait(false) : null;
            context = PinResolvedCurve(context, "Inflation:" + terms.InflationIndex, inflationCurve);
            var fixingRows = index is not null && observations is not null
                ? await RoleObservations(index, BondMarketObservationKind.Fixing, first.AddDays(-400), terms.MaturityDate!.Value.AddMonths(1), knownAt, ct).ConfigureAwait(false) : [];
            var inflationRows = terms.InflationIndex is { } inflationSeries && observations is not null
                ? await RoleObservations(inflationSeries, BondMarketObservationKind.InflationIndex, first.AddMonths(-25), terms.MaturityDate!.Value.AddMonths(1), knownAt, ct).ConfigureAwait(false) : [];
            BondWorkflowFixing Fixing(DateOnly date)
            {
                if (freezeIndexAt is { } frozenAt && date > frozenAt)
                {
                    var frozen = fixingRows.Where(r => r.Date <= frozenAt && r.PublishedAt <= knownAt && r.RecordedAt <= knownAt)
                        .OrderByDescending(r => r.Date).ThenByDescending(r => r.RecordedAt).FirstOrDefault();
                    if (frozen is not null)
                    {
                        AddObservation(frozen, inputs);
                        return new(frozen.Value, false, "IncomePolicyFrozen:" + frozen.Source);
                    }
                    var frozenReset = terms.Legs?.FirstOrDefault()?.ResetSchedule?.Where(r => r.ResetDate <= frozenAt && r.FixingRate is not null)
                        .OrderByDescending(r => r.ResetDate).FirstOrDefault();
                    if (frozenReset?.FixingRate is { } frozenRate)
                        return new(Rate(frozenRate), false, $"IncomePolicyFrozen:SecurityTerms:{security.SecurityId:D}:{security.Version}:{frozenReset.ResetDate:yyyy-MM-dd}");
                    throw new ArgumentException("IncomeFrozenIndexUnavailable: no retained observed index is eligible at the policy date.");
                }
                var observed = date <= economicDate ? Eligible(fixingRows, date, knownAt) : null;
                if (observed is not null)
                {
                    AddObservation(observed, inputs);
                    return new(observed.Value, true, observed.Source);
                }
                var retained = terms.Legs?.FirstOrDefault();
                var reset = retained?.ResetSchedule?.SingleOrDefault(r => r.ResetDate == date);
                if (reset?.FixingRate is { } recorded && date <= economicDate)
                    return new(Rate(recorded), true, $"SecurityTerms:{security.SecurityId:D}:{security.Version}");
                if (retained?.CurrentIndexResetDate == date && retained.CurrentIndexRate is { } current
                    && !retained.HasDeclaredResetSchedule && !string.IsNullOrWhiteSpace(retained.CurrentIndexRateSource) && date <= economicDate)
                    return new(Rate(current), true, retained.CurrentIndexRateSource);
                if (date <= economicDate)
                    throw new ArgumentException($"MissingObservedFixing: {index} on {date:yyyy-MM-dd}.");
                if (forward is null || CurveValue(forward, SecurityCurveKind.Forward, date) is not { } forecast)
                    throw new ArgumentException($"ForwardCurveMissing: {index} on {date:yyyy-MM-dd}.");
                return new(forecast + (applyScenario ? request.RateShiftBps + PillarShift(request.RatePillars, date) + additionalRateBps : 0m) / 10_000m,
                    false, $"Curve:{forward.CurveVersionId:D}");
            }
            BondWorkflowFixing Inflation(DateOnly date)
            {
                if (freezeIndexAt is { } frozenAt)
                {
                    var frozen = inflationRows.Where(r => r.Date <= frozenAt && r.PublishedAt <= knownAt && r.RecordedAt <= knownAt)
                        .OrderByDescending(r => r.Date).ThenByDescending(r => r.RecordedAt).FirstOrDefault();
                    if (frozen is not null && date > frozen.Date)
                    {
                        AddObservation(frozen, inputs);
                        return new(frozen.Value, false, "IncomePolicyFrozen:" + frozen.Source);
                    }
                }
                var observed = Eligible(inflationRows, date, knownAt);
                if (observed is not null)
                { AddObservation(observed, inputs); return new(observed.Value, true, observed.Source); }
                if (date <= economicDate)
                    throw new ArgumentException($"InflationObservationMissing: {terms.InflationIndex} on {date:yyyy-MM-dd}.");
                if (inflationCurve is null || CurveValue(inflationCurve, SecurityCurveKind.Inflation, date) is not { } forecast)
                    throw new ArgumentException($"InflationCurveMissing: {terms.InflationIndex} on {date:yyyy-MM-dd}.");
                return new(forecast, false, $"Curve:{inflationCurve.CurveVersionId:D}");
            }
            BondWorkflowOvernightConvention? overnight = null;
            if (Text(block, "floatingMethod") is { } floatingMethod && floatingMethod is not ("Simple" or "CompoundedOvernight"))
                return Block("OvernightConventionUnsupported", "The retained floating method is unsupported.");
            if (Text(block, "floatingMethod") == "CompoundedOvernight")
            {
                if (kind != "Floating" || Text(block, "fixingCalendarId") is not { } calendarId || calendars is null
                    || !calendars.TryGet(calendarId, out var fixingCalendar)
                    || Number(block, "lookbackBusinessDays") is not { } lookback || Number(block, "lockoutBusinessDays") is not { } lockout
                    || Text(block, "observationMethod") is not ("LookbackWithoutShift" or "ObservationShift")
                    || Text(block, "observationWeighting") is not ("ObservationPeriod" or "InterestPeriod"))
                    return Block("OvernightConventionsMissing", "Overnight coupons require retained fixing calendar, lookback, lockout, observation and weighting conventions.");
                if (lookback != decimal.Truncate(lookback) || lockout != decimal.Truncate(lockout))
                    return Block("OvernightConventionsInvalid", "Lookback and lockout must be whole business-day counts.");
                overnight = new(fixingCalendar, checked((int)lookback), Text(block, "observationMethod") == "ObservationShift",
                    Text(block, "observationWeighting") == "InterestPeriod", checked((int)lockout));
                inputs.Add(new("FixingCalendar", calendarId, ContentSha256: fixingCalendar.ContentHash));
            }
            BondWorkflowInflationConvention? inflation = null;
            if (kind == "InflationLinked")
            {
                if (terms.InflationIndex is null || terms.InflationBaseIndexValue is not > 0m
                    || Number(block, "lagMonths") is not { } lag || Boolean(block, "interpolateMonthly") is not { } interpolate)
                    return Block("InflationConventionsMissing", "Index-linked bonds require retained index/base level, lag months and monthly interpolation convention.");
                if (lag != decimal.Truncate(lag))
                    return Block("InflationConventionsInvalid", "Inflation lag must be a whole number of months.");
                inflation = new(terms.InflationIndex, terms.InflationBaseIndexValue.Value, checked((int)lag), interpolate,
                    Number(block, "couponFloorRatio"), Number(block, "redemptionFloorRatio"));
            }
            var principalBasis = terms.PrincipalFace ?? 100m;
            if (principalBasis <= 0m)
                return Block("PrincipalBasisMissing", "A positive contractual face basis is required.");
            var principal = (terms.PrincipalSchedule ?? []).Select(p => new StructuredPrincipalScheduleEntry(p.PaymentDate,
                p.Amount * request.OriginalFace / principalBasis)).ToArray();
            var factorEvidence = terms.FactorSchedule.ToList();
            if (terms.CurrentFactor is { } scalarFactor)
            {
                var factorDate = SecurityTermReader.ReadDate(StructuredCashFlowTermsResolver.EnumerateTermSources(security).ToArray(),
                    "factorDate", "currentFactorDate");
                if (factorDate is null && scalarFactor != 1m)
                    return Block("FactorDateMissing", "A non-unit principal factor requires its retained economic date.");
                factorEvidence.Add(new(factorDate ?? terms.IssueDate!.Value, scalarFactor));
            }
            if (factorEvidence.GroupBy(f => f.AsOfDate).Any(g => g.Select(f => f.Factor).Distinct().Count() > 1))
                return Block("CompetingFactors", "Conflicting principal factors on the same date require source selection.");
            decimal Factor(DateOnly date)
            {
                var evidence = factorEvidence.Where(f => f.AsOfDate <= date).OrderByDescending(f => f.AsOfDate).FirstOrDefault();
                var from = evidence?.AsOfDate ?? terms.IssueDate!.Value.AddDays(-1);
                return (evidence?.Factor ?? 1m) - principal.Where(p => p.PaymentDate > from && p.PaymentDate <= date)
                    .Sum(p => p.Amount) / request.OriginalFace;
            }
            BondWorkflowProjectionTerms ProjectionTerms(DateOnly date) => new(kind, Rate(terms.CouponRate ?? 0m),
                (terms.Legs?.FirstOrDefault()?.SpreadBps ?? 0m) / 10_000m,
                terms.CapRate is { } cap ? Rate(cap) : null, terms.FloorRate is { } floor ? Rate(floor) : null,
                dayCount, declared.Conventions.CouponCashBasis ?? "DayCount", request.OriginalFace, Factor(date), principal, overnight, inflation);
            var projectionTerms = ProjectionTerms(first);
            foreach (var factor in factorEvidence.Where(f => f.AsOfDate > first))
            {
                var expected = Factor(first) - principal.Where(p => p.PaymentDate > first && p.PaymentDate <= factor.AsOfDate)
                    .Sum(p => p.Amount) / request.OriginalFace;
                if (expected != factor.Factor)
                    return Block("UnexplainedFactorMovement", "Dated factor changes must reconcile to dated contractual principal cash.");
            }
            var flows = BondWorkflowProjection.Project(schedule, projectionTerms, first, Fixing, Inflation);
            bool PaidActiveCoupon(DateOnly date) => kind != "ZeroCoupon" && (kind == "Floating" || terms.CouponRate is not (null or 0m))
                && schedule.Periods.Any(period => period.AccrualStart < date && period.PaymentDate <= date && date < period.AccrualEnd);
            decimal ActiveAccrued(DateOnly date) => legacyAccrual
                ? BondWorkflowProjection.LegacyAccrued(schedule, ProjectionTerms(date), date, Fixing, Inflation)
                : BondWorkflowProjection.Accrued(schedule, ProjectionTerms(date), date, Fixing, Inflation);
            decimal Deferred(DateOnly date) => legacyAccrual ? 0m : flows.Where(f => f.AccrualEnd <= date && f.PaymentDate > date).Sum(f => f.Interest);
            var contractualAccrued = ActiveAccrued(settlement);
            var deferredCoupon = Deferred(settlement);
            var accrued = (legacyAccrual || !PaidActiveCoupon(settlement) ? contractualAccrued : 0m) + deferredCoupon;
            var quoteAccrued = (legacyAccrual || !PaidActiveCoupon(quoteDate) ? ActiveAccrued(quoteDate) : 0m) + Deferred(quoteDate);
            var currentFace = request.OriginalFace * Factor(settlement);
            var quoteFace = request.OriginalFace * Factor(quoteDate);
            var settlementIndex = inflation is null ? new BondWorkflowFixing(1m, true, "contract")
                : BondWorkflowProjection.IndexRatio(settlement, inflation, Inflation);
            decimal? elapsedFraction = 0m, fullPeriodFraction = 0m, elapsedUnits = 0m, fullUnits = 0m;
            var activePeriod = schedule.Periods.FirstOrDefault(p => p.AccrualStart <= settlement && settlement < p.AccrualEnd);
            if (activePeriod is not null)
            {
                elapsedFraction = ScheduleDayCount.TryFraction(dayCount, activePeriod, schedule, activePeriod.AccrualStart,
                    settlement, out var elapsed, out _) ? elapsed : null;
                fullPeriodFraction = ScheduleDayCount.TryPeriodFraction(dayCount, activePeriod, schedule,
                    out var full, out _) ? full : null;
                elapsedUnits = BondContractualEconomics.TryReferenceUnits(activePeriod, schedule, dayCount,
                    activePeriod.AccrualStart, settlement, out var partialUnits, out _) ? partialUnits : null;
                fullUnits = BondContractualEconomics.TryReferenceUnits(activePeriod, schedule, dayCount,
                    activePeriod.AccrualStart, activePeriod.AccrualEnd, out var periodUnits, out _) ? periodUnits : null;
            }
            if (request.PriceBasis == BondWorkflowPriceBasis.RealClean && inflation is null)
                return Block("PriceBasisMismatch", "Real clean quotation requires an indexed bond.");
            var quoteRatio = request.PriceBasis == BondWorkflowPriceBasis.RealClean
                ? BondWorkflowProjection.IndexRatio(quoteDate, inflation!, Inflation).Value : 1m;
            decimal? clean = request.CleanPricePer100 * quoteFace * quoteRatio / 100m;
            decimal? dirty = clean + quoteAccrued;
            if (quoteIsDirty)
            { dirty = clean; clean = dirty - quoteAccrued; }
            if (request.CleanPricePer100 is null)
                issues.Add(new("SettlementPreview", "QuoteMissing", "Select a retained quote or enter an explicit clean price to calculate purchase cash and yield."));
            BondWorkflowCashFlow[] Entitled(IReadOnlyList<BondWorkflowCashFlow> source, DateOnly date, out decimal detachedCoupon,
                out bool detachedAny)
            {
                detachedCoupon = 0m;
                detachedAny = false;
                var rows = source.Where(f => f.PaymentDate > date).ToArray();
                if (Number(block, "exCouponBusinessDays") is not { } days || days == 0m)
                    return rows;
                if (days < 0m || days > 30m || decimal.Truncate(days) != days)
                    throw new ArgumentException("Ex-coupon business days must be an integer from 0 to 30.");
                var calendar = schedule.Spec.PaymentAdjustment?.Calendar;
                if (calendar is null)
                    throw new ArgumentException("ExCouponCalendarMissing: an explicit payment calendar is required.");
                for (var i = 0; i < rows.Length; i++)
                    if (rows[i].Interest != 0m && date >= calendar.AddBusinessDays(rows[i].PaymentDate, -(int)days))
                    {
                        detachedAny = true;
                        detachedCoupon += rows[i].Interest;
                        rows[i] = rows[i] with { Interest = 0m };
                    }
                return rows;
            }
            var entitled = Entitled(flows, settlement, out var settlementDetached, out var hasDetachedCoupon);
            var quoteEntitled = Entitled(flows, quoteDate, out var quoteDetached, out _);
            accrued -= settlementDetached;
            quoteAccrued -= quoteDetached;
            clean = request.CleanPricePer100 * quoteFace * quoteRatio / 100m;
            dirty = quoteIsDirty ? clean : clean + quoteAccrued;
            if (quoteIsDirty)
                clean = dirty - quoteAccrued;
            var originalCleanConsideration = clean;
            var originalDirtyConsideration = dirty;
            decimal? yield = null, spread = null, pv = null, dv01 = null, cs01 = null, duration = null;
            if (dirty is { } dirtyCash && quoteEntitled.Length > 0)
            {
                var yieldFlows = Discounted(quoteEntitled, quoteDate, null, 0m);
                if (BondWorkflowPricing.TrySolveSpread(yieldFlows, dirtyCash, out var continuous, out var issue))
                {
                    var effective = Math.Exp((double)continuous) - 1d;
                    if (double.IsFinite(effective) && effective <= (double)decimal.MaxValue && effective >= (double)decimal.MinValue)
                        yield = (decimal)effective;
                }
                else
                    issues.Add(new("Yield", issue!, "A globally unique admissible yield was not established; cash projections remain available."));
            }
            try
            {
                if (discount is not null && entitled.Length > 0)
                {
                    if (first < discount.AsOf)
                        throw new ArgumentException("DiscountCurveCoverage: modeled carry cannot extrapolate before the retained curve's economic date.");
                    if (dirty is { } target)
                    {
                        var original = Discounted(quoteEntitled, quoteDate, discount, 0m);
                        if (BondWorkflowPricing.TrySolveSpread(original, target, out var calibrated, out var issue))
                            spread = calibrated;
                        else
                            issues.Add(new("SettlementAdjustment", issue!, "Quote spread cannot be uniquely calibrated; original-settlement cash remains inspectable."));
                    }
                    if (spread is null)
                    {
                        issues.Add(new("CreditRisk", "CalibratedSpreadUnavailable", "Benchmark valuation is available, but credit-spread risk requires a uniquely calibrated retained quote spread."));
                        if (request.SpreadShiftBps != 0m)
                            throw new ArgumentException("CreditSpreadScenarioUnavailable: no retained calibrated spread establishes the scenario baseline.");
                    }
                    applyScenario = true;
                    entitled = Entitled(BondWorkflowProjection.Project(schedule, projectionTerms, first, Fixing, Inflation), settlement, out _, out _);
                    var baseline = Discounted(entitled, settlement, discount, request.RateShiftBps / 10_000m,
                        date => PillarShift(request.RatePillars, date) / 10_000m);
                    var risk = BondWorkflowPricing.Risk(baseline, (spread ?? 0m) + request.SpreadShiftBps / 10_000m);
                    pv = risk.PresentValue;
                    dv01 = risk.Dv01;
                    cs01 = spread is null ? null : risk.Dv01;
                    duration = risk.Duration;
                    // Rate DV01 reprojects future floating coupons; CS01 changes discount spread only.
                    additionalRateBps = -1m;
                    var downFlows = Entitled(BondWorkflowProjection.Project(schedule, projectionTerms, first, Fixing, Inflation), settlement, out _, out _);
                    var down = BondWorkflowPricing.PresentValue(Discounted(downFlows, settlement, discount,
                        (request.RateShiftBps - 1m) / 10_000m, date => PillarShift(request.RatePillars, date) / 10_000m),
                        (spread ?? 0m) + request.SpreadShiftBps / 10_000m);
                    additionalRateBps = 1m;
                    var upFlows = Entitled(BondWorkflowProjection.Project(schedule, projectionTerms, first, Fixing, Inflation), settlement, out _, out _);
                    var up = BondWorkflowPricing.PresentValue(Discounted(upFlows, settlement, discount,
                        (request.RateShiftBps + 1m) / 10_000m, date => PillarShift(request.RatePillars, date) / 10_000m),
                        (spread ?? 0m) + request.SpreadShiftBps / 10_000m);
                    additionalRateBps = 0m;
                    dv01 = (down - up) / 2m;
                    if (Math.Abs(pv.Value) > Math.Max(0.000000000001m, baseline.Sum(f => Math.Abs(f.Amount * f.DiscountFactor)) * 0.000000000001m))
                        duration = dv01 / 0.0001m / pv;
                    else
                        duration = null;
                    if (quoteDate != settlement)
                    {
                        if (spread is { })
                        { dirty = pv; clean = dirty - accrued; }
                        else
                        { clean = null; dirty = null; }
                    }
                }
                else
                {
                    issues.Add(new("Valuation", "DiscountCurveMissing", "A retained discount curve with payment-date coverage is required for valuation and risk."));
                    if (quoteDate != settlement)
                    { clean = null; dirty = null; }
                }
            }
            catch (Exception exception) when (exception is ArgumentException or ArithmeticException or InvalidOperationException)
            {
                additionalRateBps = 0m;
                issues.Add(new("Valuation", "ValuationUnavailable", exception.Message));
                pv = dv01 = cs01 = duration = null;
                if (quoteDate != settlement)
                { clean = null; dirty = null; }
            }
            if (quoteDate != settlement)
            {
                yield = null;
                if (dirty is { } targetCash && BondWorkflowPricing.TrySolveSpread(Discounted(entitled, settlement, null, 0m),
                        targetCash, out var targetContinuous, out _))
                    yield = (decimal)(Math.Exp((double)targetContinuous) - 1d);
            }
            var metrics = new[]
            {
                new BondWorkflowMetric("Projection", true), new("Yield", yield is not null),
                new("Valuation", pv is not null), new("Risk", dv01 is not null),
                new("RateRisk", dv01 is not null), new("SpreadRisk", cs01 is not null),
                new("SettlementPreview", dirty is not null), new("SettlementAdjustment", quoteDate == settlement || spread is not null)
            };
            var money = CurrencyMinorUnits.GetPrecision(security.Currency);
            decimal? Round(decimal? amount) => amount is { } cash ? decimal.Round(cash, money, MidpointRounding.AwayFromZero) : null;
            var roundedAccrued = Round(accrued);
            var roundedClean = Round(clean);
            var roundedDirty = roundedClean + roundedAccrued;
            var roundedFees = Round(request.Fees)!.Value;
            var settlementDifference = request.ObservedDirtyCash - (roundedDirty + roundedFees);
            if (settlementDifference is not null and not 0m)
                issues.Add(new("SettlementPreview", "ConfirmationDifference", "Observed confirmation cash differs from the calculated rounded consideration, accrued interest and fees."));
            var exerciseResults = new List<BondWorkflowExerciseResult>();
            decimal? realYield = null;
            if (inflation is not null && dirty is { } indexedDirty && settlementIndex.Value > 0m && entitled.Length > 0)
            {
                var realFlows = entitled.Select(f => new BondWorkflowDiscountedFlow(f.Total / f.IndexRatio,
                    (f.PaymentDate.DayNumber - settlement.DayNumber) / 365m, 1m)).ToArray();
                if (BondWorkflowPricing.TrySolveSpread(realFlows, indexedDirty / settlementIndex.Value, out var realContinuous, out var realIssue))
                    realYield = (decimal)(Math.Exp((double)realContinuous) - 1d);
                else
                    issues.Add(new("RealYield", realIssue!, "A unique real cash-flow yield was not established."));
            }
            decimal? worst = yield;
            if (BondExerciseEconomics.TryResolveDeclaredDates(security, terms.MaturityDate!.Value, declared, settlement,
                    calendars, out var exercises, out var exerciseIssue))
            {
                foreach (var exercise in exercises)
                {
                    decimal? exerciseYield = null;
                    string? unavailable = null;
                    if (exercise.LatestNoticeDate < economicDate)
                        unavailable = "NoticeEvidenceRequired";
                    else if (dirty is null)
                        unavailable = "QuoteRequired";
                    else if (flows.Any(f => f.AccrualEnd <= exercise.ExerciseDate && f.PaymentDate > exercise.ExerciseDate && f.Interest != 0m))
                        unavailable = "DelayedCouponEntitlementUnsupported";
                    else
                    {
                        var exerciseTerms = ProjectionTerms(exercise.ExerciseDate);
                        var accruedOnExercise = legacyAccrual
                            ? BondWorkflowProjection.LegacyAccrued(schedule, exerciseTerms, exercise.ExerciseDate, Fixing, Inflation)
                            : PaidActiveCoupon(exercise.ExerciseDate) ? 0m
                                : BondWorkflowProjection.Accrued(schedule, exerciseTerms, exercise.ExerciseDate, Fixing, Inflation);
                        var ratio = inflation is null ? 1m : BondWorkflowProjection.IndexRatio(exercise.ExerciseDate, inflation, Inflation).Value;
                        ratio = Math.Max(ratio, inflation?.RedemptionFloorRatio ?? ratio);
                        var redemption = request.OriginalFace * Factor(exercise.ExerciseDate) * ratio * exercise.RedemptionPrice / 100m;
                        var exerciseFlows = entitled.Where(f => f.PaymentDate <= exercise.ExerciseDate)
                            .Select(f => new BondWorkflowDiscountedFlow(f.Total, (f.PaymentDate.DayNumber - settlement.DayNumber) / 365m, 1m)).ToList();
                        exerciseFlows.Add(new(redemption + accruedOnExercise,
                            (exercise.ExerciseDate.DayNumber - settlement.DayNumber) / 365m, 1m));
                        if (BondWorkflowPricing.TrySolveSpread(exerciseFlows, dirty.Value, out var continuous, out unavailable))
                            exerciseYield = (decimal)(Math.Exp((double)continuous) - 1d);
                    }
                    exerciseResults.Add(new(exercise.Kind, exercise.ExerciseDate, exercise.RedemptionPrice,
                        exerciseYield, unavailable, exercise.LatestNoticeDate));
                    if (exercise.Kind == "Call")
                        worst = worst is { } previous && exerciseYield is { } candidate ? Math.Min(previous, candidate) : null;
                }
            }
            else
            { worst = null; issues.Add(new("YieldToWorst", "ExerciseUnsupported", exerciseIssue!)); }
            if (raw.TryGetProperty("isCallable", out var callable) && callable.ValueKind == JsonValueKind.True
                && exerciseResults.All(e => e.Kind != "Call"))
            { worst = null; issues.Add(new("YieldToWorst", "CallScheduleMissing", "Callable bonds require complete retained fixed-price exercise terms.")); }
            return new(security.SecurityId, security.Currency, modelVersion, context, request.OriginalFace, currentFace,
                roundedAccrued, roundedClean, roundedDirty, roundedFees, roundedDirty + roundedFees, pv, yield,
                spread, dv01, cs01, duration, entitled, inputs.Distinct().ToArray(), issues, metrics)
            {
                QuoteObservationId = request.QuoteObservationId,
                PriceSelectionReceiptId = request.PriceSelectionReceiptId,
                YieldToWorst = worst,
                ExerciseScenarios = exerciseResults,
                SettlementIndexRatio = settlementIndex.Value,
                SettlementIndexIsObserved = settlementIndex.IsObserved,
                ContractualAccruedInterest = legacyAccrual ? Round(contractualAccrued)
                    : decimal.Round(contractualAccrued, 12, MidpointRounding.ToEven),
                DeferredCouponInterest = legacyAccrual ? null : decimal.Round(deferredCoupon, 12, MidpointRounding.ToEven),
                HasDetachedCoupon = legacyAccrual ? null : hasDetachedCoupon,
                HasPaidActiveCoupon = legacyAccrual ? null : PaidActiveCoupon(settlement),
                ProviderYield = selectedQuote?.Input.Quote?.ProviderYield,
                ProviderYieldConvention = selectedQuote?.Input.Quote?.ProviderYieldConvention,
                YieldBasis = kind is "Floating" or "InflationLinked" ? "NominalForecastCashFlowAnnualEffective" : "AnnualEffective",
                QuoteSource = selectedQuote?.Input.SourceName,
                QuoteCategory = selectedQuote?.Input.Quote?.Category,
                QuoteObservedAt = quoteObservedAt,
                QuoteIsExpired = selectedQuote?.Input.Quote?.ExpiresAtUtc < context.AsOf,
                Request = originalRequest with { Context = context },
                FrozenIndexAt = freezeIndexAt,
                QuoteSettlementDate = quoteDate,
                OriginalCleanConsideration = Round(originalCleanConsideration),
                OriginalDirtyConsideration = Round(originalDirtyConsideration),
                SettlementDifference = settlementDifference,
                RealYield = realYield,
                RealYieldBasis = inflation is null ? null : "RealForecastCashFlowAnnualEffective",
                DiscountMargin = kind == "Floating" ? spread : null,
                DiscountMarginConvention = kind == "Floating" ? "ContinuousSpreadOverRetainedDiscountCurve" : null,
                SettlementAccrualFraction = elapsedFraction,
                SettlementPeriodAccrualFraction = fullPeriodFraction,
                SettlementAccrualPeriodUnits = elapsedUnits,
                SettlementPeriodUnits = fullUnits
            };
        }
        catch (Exception exception) when (exception is ArgumentException or ArithmeticException or InvalidOperationException)
        { return Block("CalculationRefused", exception.Message); }
    }

    private async Task<SecurityCurveVersionDto?> ResolveCurve(string role, SecurityCurveKind kind, string? index,
        string currency, BondCalculationContext context, List<BondCalculationInput> inputs, CancellationToken ct)
    {
        if (curves is null)
            return null;
        var pins = context.Curves?.Where(p => p.Role == role).ToArray() ?? [];
        if (pins.Length > 1)
            throw new ArgumentException("Ambiguous curve role: " + role);
        SecurityCurveDefinitionDto? definition;
        SecurityCurveVersionDto? version;
        if (pins.Length == 1)
        {
            definition = await curves.GetDefinitionAsync(pins[0].CurveId, ct).ConfigureAwait(false);
            version = await curves.GetVersionByIdAsync(pins[0].CurveId, pins[0].CurveVersionId, ct).ConfigureAwait(false);
        }
        else
        {
            var choices = (await curves.ListDefinitionsAsync(kind, currency, index, ct).ConfigureAwait(false))
                .Where(choice => choice.CreatedAt <= context.KnownAt).ToArray();
            if (choices.Length != 1)
                return null;
            definition = choices[0];
            version = await curves.GetLatestVersionAsOfAsync(definition.CurveId,
                DateOnly.FromDateTime(context.AsOf.UtcDateTime), context.KnownAt, ct).ConfigureAwait(false);
        }
        if (definition is null || version is null)
            return null;
        if (definition.Kind != kind || definition.Currency != currency || definition.IndexName != index
            || definition.CreatedAt > context.KnownAt || version.CurveId != definition.CurveId || version.CurveKey != definition.CurveKey
            || pins.Length == 1 && (definition.CurveId != pins[0].CurveId || version.CurveVersionId != pins[0].CurveVersionId)
            || version.RecordedAt > context.KnownAt || version.AsOf > DateOnly.FromDateTime(context.AsOf.UtcDateTime))
            throw new ArgumentException("Pinned curve is incompatible with its role or economic/knowledge cutoff: " + role);
        inputs.Add(new("Curve:" + role, definition.CurveKey, version.Version, version.ContentSha256,
            version.CurveVersionId, new DateTimeOffset(version.AsOf.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero), version.RecordedAt));
        inputs.Add(new("CurveRatePolicy:" + role, definition.RatePolicy.ToString()));
        return version;
    }

    private static BondCalculationContext PinResolvedCurve(BondCalculationContext context, string role, SecurityCurveVersionDto? version)
        => version is null || context.Curves?.Any(pin => pin.Role == role) == true ? context : context with
        { Curves = (context.Curves ?? []).Append(new BondPinnedCurveInput(role, version.CurveId, version.CurveVersionId)).ToArray() };

    private async Task<BondWorkflowRequest> PinRetainedCurvesAsync(BondWorkflowResult retained, BondWorkflowRequest request, CancellationToken ct)
    {
        var dependencies = retained.Inputs.Where(input => input.Kind.StartsWith("Curve:", StringComparison.Ordinal)).ToArray();
        if (dependencies.Length == 0)
            return request;
        if (curves is null || request.Context.KnownAt is not { } knownAt
            || dependencies.GroupBy(input => input.Kind, StringComparer.Ordinal).Any(group => group.Count() != 1))
            throw new ArgumentException("Retained curve replay requires an exact knowledge cutoff, curve source and one dependency per role.");
        var context = request.Context;
        foreach (var dependency in dependencies)
        {
            var role = dependency.Kind["Curve:".Length..];
            var kind = role == "Discount" ? SecurityCurveKind.Discount
                : role.StartsWith("Forward:", StringComparison.Ordinal) ? SecurityCurveKind.Forward
                : role.StartsWith("Inflation:", StringComparison.Ordinal) ? SecurityCurveKind.Inflation
                : throw new ArgumentException("Retained curve replay has an unsupported role: " + role);
            var index = role == "Discount" ? null : role[(role.IndexOf(':') + 1)..];
            var pins = context.Curves?.Where(pin => pin.Role == role).ToArray() ?? [];
            if (pins.Length > 1 || dependency.VersionId is not { } versionId
                || pins.Length == 1 && pins[0].CurveVersionId != versionId)
                throw new ArgumentException("Retained curve pins disagree with their exact input dependency: " + role);
            var definitions = pins.Length == 1
                ? new[] { await curves.GetDefinitionAsync(pins[0].CurveId, ct).ConfigureAwait(false) }
                : (await curves.ListDefinitionsAsync(kind, retained.Currency, index, ct).ConfigureAwait(false))
                    .Where(candidate => candidate.CurveKey == dependency.Key).ToArray();
            if (definitions.Length != 1 || definitions[0] is not { } definition)
                throw new ArgumentException("The exact retained curve definition is unavailable or ambiguous: " + role);
            var version = await curves.GetVersionByIdAsync(definition.CurveId, versionId, ct).ConfigureAwait(false);
            if (version is null || definition.Kind != kind || definition.Currency != retained.Currency || definition.IndexName != index
                || definition.CreatedAt > knownAt || version.RecordedAt > knownAt
                || version.AsOf > DateOnly.FromDateTime(context.AsOf.UtcDateTime)
                || version.CurveId != definition.CurveId || version.CurveVersionId != versionId || version.CurveKey != definition.CurveKey
                || dependency != new BondCalculationInput("Curve:" + role, definition.CurveKey, version.Version, version.ContentSha256,
                    version.CurveVersionId, new DateTimeOffset(version.AsOf.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero), version.RecordedAt))
                throw new ArgumentException("Retained curve identity, version, content or cutoff differs from its immutable input dependency: " + role);
            context = PinResolvedCurve(context, role, version);
        }
        return request with { Context = context };
    }

    private static decimal? CurveValue(SecurityCurveVersionDto curve, SecurityCurveKind kind, DateOnly date)
    {
        if (curve.Points.Count == 0 || date > curve.Points.Max(p => p.Date))
            return null;
        var value = SecurityCurveRules.ValueAt(curve, kind, date);
        if (kind is SecurityCurveKind.Discount or SecurityCurveKind.Inflation && value <= 0m)
            throw new ArgumentException("Discount factors and inflation index levels must be positive.");
        return value;
    }

    private static BondWorkflowDiscountedFlow[] Discounted(IReadOnlyList<BondWorkflowCashFlow> flows,
        DateOnly settlement, SecurityCurveVersionDto? curve, decimal rateShift, Func<DateOnly, decimal>? pillarShift = null)
        => flows.Where(f => f.PaymentDate > settlement).Select(f =>
        {
            var years = (f.PaymentDate.DayNumber - settlement.DayNumber) / 365m;
            var factor = curve is null ? 1m : CurveValue(curve, SecurityCurveKind.Discount, f.PaymentDate)
                / CurveValue(curve, SecurityCurveKind.Discount, settlement)
                ?? throw new ArgumentException("DiscountCurveCoverage: payment or settlement lies beyond retained curve coverage.");
            var shift = Math.Exp(-(double)((rateShift + (pillarShift?.Invoke(f.PaymentDate) ?? 0m)) * years));
            if (!double.IsFinite(shift) || shift <= 0d || shift > (double)decimal.MaxValue)
                throw new ArgumentException("Scenario discount factor exceeds supported bounds.");
            return new BondWorkflowDiscountedFlow(f.Total, years, factor * (decimal)shift);
        }).ToArray();

    private static decimal PillarShift(IReadOnlyList<BondWorkflowRatePillar> pillars, DateOnly date)
    {
        if (pillars.Count == 0)
            return 0m;
        var ordered = pillars.OrderBy(p => p.Date).ToArray();
        if (date <= ordered[0].Date)
            return ordered[0].ShiftBps;
        for (var i = 1; i < ordered.Length; i++)
            if (date <= ordered[i].Date)
            {
                var weight = (decimal)(date.DayNumber - ordered[i - 1].Date.DayNumber)
                    / (ordered[i].Date.DayNumber - ordered[i - 1].Date.DayNumber);
                return ordered[i - 1].ShiftBps + weight * (ordered[i].ShiftBps - ordered[i - 1].ShiftBps);
            }
        return ordered[^1].ShiftBps;
    }

    private Task<IReadOnlyList<BondWorkflowRateObservation>> RoleObservations(string indexName,
        BondMarketObservationKind kind, DateOnly from, DateOnly to, DateTimeOffset knownAt, CancellationToken ct)
        => observations is IBondWorkflowTypedObservationSource typed
            ? typed.GetAsync(indexName, kind, from, to, knownAt, ct)
            : observations!.GetAsync(indexName, from, to, knownAt, ct);

    private static BondWorkflowRateObservation? Eligible(IReadOnlyList<BondWorkflowRateObservation> rows, DateOnly date, DateTimeOffset cutoff)
    {
        var eligible = rows.Where(r => r.Date == date && r.PublishedAt <= cutoff && r.RecordedAt <= cutoff).ToArray();
        if (eligible.Select(r => r.Source).Distinct().Count() > 1)
            throw new ArgumentException("Competing observations require an explicit selected source.");
        return eligible.OrderByDescending(r => r.RecordedAt).FirstOrDefault();
    }

    private static void AddObservation(BondWorkflowRateObservation row, ICollection<BondCalculationInput> inputs)
        => inputs.Add(new("Observation", row.IndexName + ":" + row.Date.ToString("yyyy-MM-dd"),
            ContentSha256: row.ContentSha256, AsOf: row.PublishedAt, RecordedAt: row.RecordedAt));
    private static string? Text(JsonElement value, string key) => value.ValueKind == JsonValueKind.Object
        && value.TryGetProperty(key, out var property) && property.ValueKind == JsonValueKind.String ? property.GetString() : null;
    private static decimal? Number(JsonElement value, string key) => value.ValueKind == JsonValueKind.Object
        && value.TryGetProperty(key, out var property) && property.TryGetDecimal(out var number) ? number : null;
    private static bool? Boolean(JsonElement value, string key) => value.ValueKind == JsonValueKind.Object
        && value.TryGetProperty(key, out var property) && property.ValueKind is JsonValueKind.True or JsonValueKind.False ? property.GetBoolean() : null;
}
