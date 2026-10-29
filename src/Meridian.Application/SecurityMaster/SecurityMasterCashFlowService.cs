using Meridian.Application.SecurityMaster.CashFlow;
using Meridian.Contracts.Calendars;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.SecurityMaster.Schedules;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging;



namespace Meridian.Application.SecurityMaster;

/// <summary>
/// Manages structured cash flow source assignments and delegates projections to
/// <see cref="IStructuredCashFlowProvider"/> implementations.
/// Client-provided sources take precedence and remain until explicitly changed,
/// consistent with the cash flow source governance model.
/// </summary>
/// <remarks>
/// Economic terms are resolved once, up front, into a typed <see cref="StructuredCashFlowTerms"/> via
/// <see cref="StructuredCashFlowTermsResolver"/> rather than probed inline; source freshness is
/// surfaced as a typed <see cref="StructuredCashFlowStaleness"/> on every projection; and a fresh
/// base-scenario projection can be routed to the ledger through
/// <see cref="IStructuredCashFlowLedgerBridge"/> instead of being display-only. A record that declares
/// schedule conventions (ADR-025) projects through the schedule kernel instead of the legacy coupon
/// walk (SecurityMasterCashFlowService.DeclaredConventions.cs).
/// </remarks>
public sealed partial class SecurityMasterCashFlowService : ISecurityMasterCashFlowService
{
    private readonly ISecurityMasterCashFlowStore _store;
    private readonly IReadOnlyList<IStructuredCashFlowProvider> _providers;
    private readonly ISecurityMasterQueryService _queryService;
    private readonly IStructuredCashFlowLedgerBridge _ledgerBridge;
    private readonly ILogger<SecurityMasterCashFlowService> _logger;
    private readonly ISecurityForwardCurveProvider? _forwardCurves;
    private readonly IBusinessDayCalendarSource? _calendars;

    private static readonly TimeSpan StalenessThreshold = TimeSpan.FromDays(7);

    public SecurityMasterCashFlowService(
        ISecurityMasterCashFlowStore store,
        IEnumerable<IStructuredCashFlowProvider> providers,
        ISecurityMasterQueryService queryService,
        ILogger<SecurityMasterCashFlowService> logger,
        IStructuredCashFlowLedgerBridge? ledgerBridge = null,
        ISecurityForwardCurveProvider? forwardCurves = null)
        : this(null, store, providers, queryService, logger, ledgerBridge, forwardCurves)
    {
    }

    /// <summary>Composes declared schedules with the host's explicit calendar source.</summary>
    public SecurityMasterCashFlowService(
        IBusinessDayCalendarSource? calendars,
        ISecurityMasterCashFlowStore store,
        IEnumerable<IStructuredCashFlowProvider> providers,
        ISecurityMasterQueryService queryService,
        ILogger<SecurityMasterCashFlowService> logger,
        IStructuredCashFlowLedgerBridge? ledgerBridge = null,
        ISecurityForwardCurveProvider? forwardCurves = null)
    {
        _store = store;
        _providers = providers?.ToList() ?? [];
        _queryService = queryService;
        _logger = logger;
        _ledgerBridge = ledgerBridge ?? new StructuredCashFlowLedgerBridge();
        _forwardCurves = forwardCurves;
        _calendars = calendars;
    }

    /// <summary>
    /// The business-day calendars declared schedule conventions resolve against (ADR-025), or null when
    /// the host composed none; an unknown calendar id never falls back to weekends only.
    /// </summary>
    internal IBusinessDayCalendarSource? CalendarSource => _calendars;

    public Task<SecurityCashFlowSourceDto?> GetCashFlowSourceAsync(Guid securityId, CancellationToken ct = default)
        => _store.GetSourceAsync(securityId, ct);

    public async Task UpsertCashFlowSourceAsync(UpsertCashFlowSourceRequest request, CancellationToken ct = default)
    {
        var existing = await _store.GetSourceAsync(request.SecurityId, ct).ConfigureAwait(false);

        // Client-provided sources remain authoritative against automated provider refreshes; an
        // operator can still clear them with an explicit Force update (e.g. when the client source
        // is stale or expired).
        if (existing is { IsClientOverride: true } && !request.IsClientOverride && !request.Force)
        {
            _logger.LogWarning(
                "Skipped cash flow source update for {SecurityId}: existing client-provided source is authoritative (use Force to override).",
                request.SecurityId);
            return;
        }

        var record = new SecurityCashFlowSourceDto(
            request.SecurityId,
            request.SourceKind,
            DateTimeOffset.UtcNow,
            request.IsClientOverride,
            request.ClientConfirmedBy,
            request.IsClientOverride ? DateTimeOffset.UtcNow : null);

        await _store.UpsertSourceAsync(record, ct).ConfigureAwait(false);
    }

    public Task<StructuredCashFlowProjectionDto?> GetProjectionAsync(
        Guid securityId, StructuredCashFlowScenario scenario, CancellationToken ct = default)
        => GetProjectionAsOfAsync(securityId, scenario, DateTimeOffset.UtcNow, ct);

    public async Task<StructuredCashFlowProjectionDto?> GetProjectionAsOfAsync(
        Guid securityId, StructuredCashFlowScenario scenario, DateTimeOffset asOf, CancellationToken ct = default)
    {
        var assignment = await _store.GetSourceAsync(securityId, ct).ConfigureAwait(false);
        if (assignment is null)
        {
            _logger.LogDebug("No cash flow source assigned for security {SecurityId}.", securityId);
            return null;
        }

        var staleness = EvaluateStaleness(assignment.LastUpdatedUtc);
        if (staleness == StructuredCashFlowStaleness.Stale)
        {
            _logger.LogWarning(
                "Cash flow source for security {SecurityId} was last updated {DaysAgo} days ago and is marked stale; ledger posting is gated.",
                securityId,
                (int)(DateTimeOffset.UtcNow - assignment.LastUpdatedUtc!.Value).TotalDays);
        }

        // Client-provided source: no external provider delegation; caller must supply projections directly.
        if (assignment.SourceKind == StructuredCashFlowSourceKind.ClientProvided)
        {
            _logger.LogDebug(
                "Security {SecurityId} uses client-provided cash flows; no provider projection available.",
                securityId);
            return null;
        }

        var security = await _queryService.GetByIdAsync(securityId, ct).ConfigureAwait(false);
        if (security is null)
        {
            _logger.LogWarning(
                "Security {SecurityId} not found when retrieving cash flow projections.", securityId);
            return null;
        }

        if (assignment.SourceKind is StructuredCashFlowSourceKind.CalculatedBullet
            or StructuredCashFlowSourceKind.CalculatedSinker)
        {
            // Curves are read as of the projection date, not the wall clock, so a valuation as of
            // another date projects floating legs from the curve in force on that date.
            var forwardCurves = await LoadForwardCurvesAsync(security, DateOnly.FromDateTime(asOf.UtcDateTime.Date), ct).ConfigureAwait(false);
            var projection = BuildCalculatedProjection(
                security, assignment.SourceKind, scenario, staleness, assignment.LastUpdatedUtc, asOf, _calendars, forwardCurves);
            if (projection.ScheduleEvidence is { CoverageGaps.Count: > 0 } evidence)
            {
                // Coverage gaps are informational (ADR-025 decision 2): the schedule still projects.
                var firstGap = evidence.CoverageGaps[0];
                _logger.LogDebug(
                    "Declared schedule for {SecurityId} used calendar dates outside coverage: {CoverageGapCount} calendar(s), first {FirstCalendarId} from {FirstDate}",
                    securityId,
                    evidence.CoverageGaps.Count,
                    firstGap.CalendarId,
                    firstGap.FirstDate);
            }

            var terms = projection.TermsUsed!;
            var usesNormalizedBasis = terms.PrincipalFace is not > 0m
                && (!terms.HasLegs || terms.Legs!.Any(leg => leg.Notional is not > 0m));
            return usesNormalizedBasis ? projection with
            {
                IsNormalizedPer100 = true,
                BlockedReason = projection.BlockedReason ?? "Schedule is normalized per 100 units; retained principal or leg notional is required before ledger posting."
            } : projection;
        }

        var isin = security.Identifiers
            .FirstOrDefault(i => i.Kind == SecurityIdentifierKind.Isin)?.Value;

        var provider = _providers.FirstOrDefault(
            p => MapSourceKindToProviderId(assignment.SourceKind) == p.ProviderId);

        if (provider is null)
        {
            _logger.LogDebug(
                "No registered provider for source kind {SourceKind} for security {SecurityId}.",
                assignment.SourceKind, securityId);
            return null;
        }

        var providerProjection = await provider.GetProjectedCashFlowsAsync(
            securityId, isin, asOf, scenario, ct).ConfigureAwait(false);

        // Stamp freshness onto provider projections too so every consumer sees a consistent gate.
        return providerProjection is null
            ? null
            : providerProjection with
            {
                Staleness = staleness,
                SourceLastUpdatedUtc = assignment.LastUpdatedUtc
            };
    }

    public async Task<StructuredCashFlowLedgerPostingResult> BuildLedgerPostingsAsync(
        Guid securityId,
        string? financialAccountId = null,
        DateOnly? through = null,
        CancellationToken ct = default)
    {
        // Accruals post from actual (base-scenario) cash flows only; rate-shocked scenarios are
        // analysis-only and are additionally rejected by the bridge.
        var projection = await GetProjectionAsync(securityId, StructuredCashFlowScenario.Base, ct).ConfigureAwait(false);
        if (projection is null)
        {
            return StructuredCashFlowLedgerPostingResult.Blocked(
                securityId, "No cash flow projection is available for this security.");
        }

        var security = await _queryService.GetByIdAsync(securityId, ct).ConfigureAwait(false);
        var symbol = ResolveSymbol(security, securityId);
        return _ledgerBridge.BuildCouponAccrualPostings(projection, symbol, financialAccountId, through);
    }

    private static StructuredCashFlowStaleness EvaluateStaleness(DateTimeOffset? lastUpdatedUtc)
    {
        if (lastUpdatedUtc is not DateTimeOffset lastUpdated)
        {
            return StructuredCashFlowStaleness.Unknown;
        }

        return DateTimeOffset.UtcNow - lastUpdated > StalenessThreshold
            ? StructuredCashFlowStaleness.Stale
            : StructuredCashFlowStaleness.Fresh;
    }

    private static string ResolveSymbol(SecurityDetailDto? security, Guid securityId)
    {
        if (security is not null)
        {
            var ticker = security.Identifiers
                .FirstOrDefault(i => i.Kind == SecurityIdentifierKind.Ticker)?.Value;
            if (!string.IsNullOrWhiteSpace(ticker))
            {
                return ticker.Trim().ToUpperInvariant();
            }

            var anyIdentifier = security.Identifiers.FirstOrDefault()?.Value;
            if (!string.IsNullOrWhiteSpace(anyIdentifier))
            {
                return anyIdentifier.Trim().ToUpperInvariant();
            }

            if (!string.IsNullOrWhiteSpace(security.DisplayName))
            {
                return security.DisplayName.Trim().ToUpperInvariant();
            }
        }

        return securityId.ToString("N");
    }

    /// <summary>
    /// Loads the forward curve for each floating leg's index. Without a provider, or for an index
    /// the provider has no curve for, the leg keeps the flat-forward projection.
    /// </summary>
    private async Task<IReadOnlyDictionary<string, SecurityForwardCurve>> LoadForwardCurvesAsync(
        SecurityDetailDto security, DateOnly asOfDate, CancellationToken ct)
    {
        var curves = new Dictionary<string, SecurityForwardCurve>(StringComparer.OrdinalIgnoreCase);
        if (_forwardCurves is null)
        {
            return curves;
        }

        var legs = StructuredCashFlowTermsResolver.Resolve(security).Legs ?? [];
        foreach (var indexName in legs
                     .Where(static leg => leg.RateKind == CashFlowLegRateKind.Floating && !string.IsNullOrWhiteSpace(leg.IndexName))
                     .Select(static leg => leg.IndexName!.Trim())
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var curve = await _forwardCurves.GetForwardCurveAsync(indexName, asOfDate, ct).ConfigureAwait(false);
            if (curve is { Points.Count: > 0 })
            {
                curves[indexName] = curve;
            }
        }

        return curves;
    }

    private static StructuredCashFlowProjectionDto BuildCalculatedProjection(
        SecurityDetailDto security,
        StructuredCashFlowSourceKind sourceKind,
        StructuredCashFlowScenario scenario,
        StructuredCashFlowStaleness staleness,
        DateTimeOffset? sourceLastUpdatedUtc,
        DateTimeOffset asOf,
        IBusinessDayCalendarSource? calendars,
        IReadOnlyDictionary<string, SecurityForwardCurve>? forwardCurves = null)
    {
        var terms = StructuredCashFlowTermsResolver.Resolve(security);
        var factorSchedule = terms.FactorSchedule;

        StructuredCashFlowProjectionDto Empty(string? reason = null) => new(
            security.SecurityId, sourceKind, scenario, asOf, [], staleness, sourceLastUpdatedUtc, factorSchedule, terms, BlockedReason: reason);

        if (Meridian.Contracts.CashFlows.SecurityContractualCashFlowTerms.IsDeclared(security.AssetClass, security.AssetSpecificTerms))
            return Empty("This record declares a versioned cashFlowContract; use the contractual cash-flow preview. The legacy projection cannot represent these economics.");

        if (!SecurityAssetClassCatalog.GetOrDefault(security.AssetClass).SupportsCashflowScheduleByDefault)
            return Empty($"Calculated cash flows are unsupported for {security.AssetClass}; provide an authoritative schedule.");

        if (terms.MaturityDate is not DateOnly maturity)
        {
            return Empty("Maturity is missing; no calculated cash flows may post.");
        }

        // Declared schedule conventions (ADR-025) are resolved before the issue-date fallback below, so
        // a declaring record without its issue date is refused with the reason rather than anchored on
        // its effective-from date. An undeclared record takes every step below exactly as before.
        var declared = Meridian.Contracts.SecurityMaster.ScheduleSpecFactory.Resolve(security, terms, calendars);
        if (declared.Status == DeclaredScheduleStatus.Refused)
        {
            return Empty(declared.Refusal!.Message);
        }

        var issueDate = terms.IssueDate ?? DateOnly.FromDateTime(security.EffectiveFrom.UtcDateTime.Date);
        var asOfDate = DateOnly.FromDateTime(asOf.UtcDateTime.Date);
        if (issueDate > maturity)
        {
            return Empty();
        }

        // Leg-based structures (swaps, floating-rate notes) project per leg; the single-stream
        // coupon walk below cannot represent them and previously produced a meaningless bullet.
        if (terms.HasLegs)
        {
            return BuildLegBasedProjection(
                security, sourceKind, scenario, staleness, sourceLastUpdatedUtc,
                terms, factorSchedule, issueDate, maturity, asOf, asOfDate, forwardCurves);
        }

        if (terms.HasStepCouponSchedule || terms.InflationIndex is not null)
            return Empty("Step-coupon or inflation-linked terms require a supported provider schedule; the fixed-rate calculation cannot represent them.");

        if (terms.CouponRate is null)
            return Empty("Contractual coupon rate is missing or unresolved; confirm the rate, including an explicit zero, before projection.");

        var principalBasis = terms.PrincipalFace is > 0m ? terms.PrincipalFace.Value : 100m;

        // A contractual schedule needs a RESOLVABLE principal basis: without par/face/notional
        // (DirectLoan carries none) the 100-unit fallback above would cap a real 1,000,000
        // instalment at 100 and feed that distortion to the ledger bridge — so basis-less records
        // keep the calculated bullet/sinker walk instead of treating the rows as contractual.
        // Schedule PRESENCE is retained separately from row count AND from the principal basis:
        // the resolver keeps an asserted principalSchedule: [] (a contractual bullet claim)
        // distinct from a missing property, and collapsing both to an empty array would let a
        // bullet record misclassified as CalculatedSinker amortize principal its schedule says is
        // not contractually paid. Presence must not require par/face either — DirectLoan requires
        // the schedule but exposes no face amount, and only NONEMPTY contractual amounts need a
        // resolvable basis (without one, the 100-unit fallback would cap real instalments, so
        // basis-less rows keep the calculated walk).
        var hasAssertedPrincipalSchedule = terms.PrincipalSchedule is not null;
        var inWindowContractual = hasAssertedPrincipalSchedule
            ? terms.PrincipalSchedule!
                .Where(entry => entry.PaymentDate >= issueDate && entry.PaymentDate <= maturity)
                .ToArray()
            : [];
        var contractualPrincipal = terms.PrincipalFace is > 0m ? inWindowContractual : [];
        var outstanding = SecurityPrincipalBalance.OpeningOutstanding(security, terms, asOfDate);

        // A declared schedule shares the principal basis and opening balance above and replaces the
        // coupon walk below.
        if (declared.IsDeclared)
        {
            return BuildDeclaredProjection(
                security, sourceKind, scenario, staleness, sourceLastUpdatedUtc, asOf, asOfDate, terms,
                declared, principalBasis, outstanding, contractualPrincipal.Length > 0,
                hasAssertedPrincipalSchedule && inWindowContractual.Length == 0);
        }

        var annualRate = NormalizeAnnualRate(terms.CouponRate.Value) + ScenarioRateShift(scenario);
        if (annualRate < 0m)
        {
            annualRate = 0m;
        }

        var periodMonths = Math.Max(1, 12 / SecurityTermConventions.LegacyCouponPaymentsPerYear(terms.PaymentFrequency));
        var periods = BuildPaymentPeriods(issueDate, maturity, periodMonths)
            .Where(period => period.PaymentDate >= asOfDate)
            .ToArray();
        if (periods.Length == 0)
        {
            return Empty();
        }

        if (contractualPrincipal.Length > 0)
        {
            var contractualEntries = BuildContractualPrincipalSchedule(
                contractualPrincipal,
                periods,
                asOfDate,
                maturity,
                principalBasis,
                outstanding,
                annualRate,
                terms.DayCountConvention);
            return new StructuredCashFlowProjectionDto(
                security.SecurityId, sourceKind, scenario, asOf, contractualEntries, staleness,
                sourceLastUpdatedUtc, factorSchedule, terms);
        }

        // An ASSERTED empty schedule (zero in-window contractual rows on a record that retains
        // the schedule property) is a contractual bullet structure: whatever the assigned source
        // kind says, the record's own terms assert no instalments, so principal pays at maturity —
        // a CalculatedSinker assignment must not synthesize amortization the schedule contradicts.
        // Keyed on the raw in-window rows, not the basis-gated contractual set, so a basis-less
        // record's NONEMPTY schedule (unprojectable rows) still keeps the calculated walk.
        var assertedBulletSchedule = hasAssertedPrincipalSchedule && inWindowContractual.Length == 0;
        var entries = new List<StructuredCashFlowScheduleEntry>(periods.Length);
        var sinkerPrincipal = sourceKind == StructuredCashFlowSourceKind.CalculatedSinker && !assertedBulletSchedule
            ? RoundCash(outstanding / periods.Length)
            : 0m;
        for (var i = 0; i < periods.Length; i++)
        {
            var (accrualStart, date) = periods[i];
            var interest = annualRate > 0m && outstanding > 0m
                ? RoundCash(outstanding * annualRate * DayCountConventions.Fraction(terms.DayCountConvention, accrualStart, date))
                : 0m;
            var principal = 0m;
            var isLast = i == periods.Length - 1;
            if ((sourceKind == StructuredCashFlowSourceKind.CalculatedBullet || assertedBulletSchedule) && isLast)
            {
                principal = outstanding;
            }
            else if (sourceKind == StructuredCashFlowSourceKind.CalculatedSinker && !assertedBulletSchedule)
            {
                principal = isLast ? outstanding : decimal.Min(outstanding, sinkerPrincipal);
            }

            outstanding = RoundCash(outstanding - principal);
            entries.Add(new StructuredCashFlowScheduleEntry(
                ToUtcDateTimeOffset(date),
                RoundCash(principal),
                interest,
                principalBasis == 0m ? 0m : RoundCash(outstanding / principalBasis)));
        }

        return new StructuredCashFlowProjectionDto(
            security.SecurityId, sourceKind, scenario, asOf, entries, staleness, sourceLastUpdatedUtc, factorSchedule, terms);
    }

    private static IReadOnlyList<StructuredCashFlowScheduleEntry> BuildContractualPrincipalSchedule(
        IReadOnlyList<StructuredPrincipalScheduleEntry> contractualPrincipal,
        IReadOnlyList<(DateOnly AccrualStart, DateOnly PaymentDate)> periods,
        DateOnly asOfDate,
        DateOnly maturity,
        decimal principalBasis,
        decimal openingOutstanding,
        decimal annualRate,
        string? dayCountConvention)
    {
        var couponDates = periods
            .Select(static period => period.PaymentDate)
            .ToHashSet();
        var principalByDate = contractualPrincipal
            .Where(entry => entry.PaymentDate >= asOfDate)
            .GroupBy(static entry => entry.PaymentDate)
            .ToDictionary(
                static group => group.Key,
                static group => group.Sum(static entry => entry.Amount));
        var eventDates = couponDates
            .Concat(principalByDate.Keys)
            .Distinct()
            .OrderBy(static date => date)
            .ToArray();

        var entries = new List<StructuredCashFlowScheduleEntry>(eventDates.Length);
        var outstanding = openingOutstanding;
        var accrualCursor = periods[0].AccrualStart;
        var completedInPeriod = contractualPrincipal
            .Where(entry => entry.PaymentDate >= accrualCursor && entry.PaymentDate < asOfDate)
            .OrderBy(static entry => entry.PaymentDate)
            .ToArray();
        var accrualOutstanding = decimal.Min(
            principalBasis,
            openingOutstanding + completedInPeriod.Sum(static entry => entry.Amount));
        var accruedInterest = 0m;
        foreach (var completed in completedInPeriod)
        {
            if (completed.PaymentDate > accrualCursor && annualRate > 0m && accrualOutstanding > 0m)
            {
                accruedInterest += accrualOutstanding * annualRate
                    * DayCountConventions.Fraction(dayCountConvention, accrualCursor, completed.PaymentDate);
            }

            accrualOutstanding = RoundCash(decimal.Max(0m, accrualOutstanding - completed.Amount));
            accrualCursor = completed.PaymentDate;
        }

        outstanding = accrualOutstanding;

        foreach (var date in eventDates)
        {
            if (date > accrualCursor && annualRate > 0m && outstanding > 0m)
            {
                accruedInterest += outstanding * annualRate
                    * DayCountConventions.Fraction(dayCountConvention, accrualCursor, date);
            }

            var isCouponDate = couponDates.Contains(date);
            var interest = isCouponDate ? RoundCash(accruedInterest) : 0m;
            if (isCouponDate)
            {
                accruedInterest = 0m;
            }

            var scheduledPrincipal = principalByDate.TryGetValue(date, out var scheduledAmount)
                ? RoundCash(scheduledAmount)
                : 0m;
            var principal = date == maturity
                ? outstanding
                : decimal.Min(outstanding, scheduledPrincipal);
            principal = RoundCash(principal);
            outstanding = RoundCash(decimal.Max(0m, outstanding - principal));

            if (isCouponDate || principal > 0m)
            {
                entries.Add(new StructuredCashFlowScheduleEntry(
                    ToUtcDateTimeOffset(date),
                    principal,
                    interest,
                    principalBasis == 0m ? 0m : RoundCash(outstanding / principalBasis)));
            }

            // Contractual principal is paid after any coupon accrued through the same date, then
            // the reduced balance accrues from that date forward.
            accrualCursor = date;
        }

        return entries;
    }

    private static StructuredCashFlowProjectionDto BuildLegBasedProjection(
        SecurityDetailDto security,
        StructuredCashFlowSourceKind sourceKind,
        StructuredCashFlowScenario scenario,
        StructuredCashFlowStaleness staleness,
        DateTimeOffset? sourceLastUpdatedUtc,
        StructuredCashFlowTerms terms,
        IReadOnlyList<StructuredFactorScheduleEntry> factorSchedule,
        DateOnly issueDate,
        DateOnly maturity,
        DateTimeOffset asOf,
        DateOnly asOfDate,
        IReadOnlyDictionary<string, SecurityForwardCurve>? forwardCurves)
    {
        var legs = terms.Legs!;
        var missingLegRate = legs.Any(leg => IsLegRateMissing(leg, ResolveCurve(leg, forwardCurves)));
        var legSchedules = new List<StructuredCashFlowLegSchedule>(legs.Count);
        foreach (var leg in legs)
        {
            var curve = ResolveCurve(leg, forwardCurves);
            legSchedules.Add(new StructuredCashFlowLegSchedule(
                leg.LegId, leg.RateKind, leg.Direction,
                BuildLegSchedule(leg, terms, scenario, issueDate, maturity, asOfDate, curve),
                DescribeRateSource(leg, curve)));
        }

        // Net receive-minus-pay only when every leg's direction is known (a single directionless
        // leg projects as Receive). A multi-leg structure with an unknown direction cannot be
        // netted honestly, so the flat schedule stays empty (never posted) and consumers read the
        // per-leg schedules instead.
        var netSchedule = !missingLegRate && (legs.Count == 1 || legs.All(static leg => leg.Direction is not null))
            ? BuildNetSchedule(legs, legSchedules)
            : [];

        return new StructuredCashFlowProjectionDto(
            security.SecurityId, sourceKind, scenario, asOf, netSchedule, staleness,
            sourceLastUpdatedUtc, factorSchedule, terms, legSchedules,
            missingLegRate ? "A cash-flow leg is missing its fixed rate or index fixing; zero is not a substitute for missing terms." : null);
    }

    private static SecurityForwardCurve? ResolveCurve(
        StructuredCashFlowLeg leg,
        IReadOnlyDictionary<string, SecurityForwardCurve>? forwardCurves)
        => leg.RateKind == CashFlowLegRateKind.Floating
           && !string.IsNullOrWhiteSpace(leg.IndexName)
           && forwardCurves is not null
           && forwardCurves.TryGetValue(leg.IndexName.Trim(), out var curve)
            ? curve
            : null;

    // A floating leg with a forward curve can project without a retained fixing: the curve supplies
    // every reset. Without one, the last fixing is required and zero is never substituted.
    private static bool IsLegRateMissing(StructuredCashFlowLeg leg, SecurityForwardCurve? curve)
        => leg.RateKind == CashFlowLegRateKind.Fixed
            ? leg.FixedRate is null
            : leg.CurrentIndexRate is null && curve is null;

    private static string DescribeRateSource(StructuredCashFlowLeg leg, SecurityForwardCurve? curve)
        => leg.RateKind == CashFlowLegRateKind.Fixed
            ? "fixed"
            : curve is not null ? curve.Describe() : "flatForward:lastFixing";

    private static IReadOnlyList<StructuredCashFlowScheduleEntry> BuildLegSchedule(
        StructuredCashFlowLeg leg,
        StructuredCashFlowTerms terms,
        StructuredCashFlowScenario scenario,
        DateOnly issueDate,
        DateOnly maturity,
        DateOnly asOfDate,
        SecurityForwardCurve? curve = null)
    {
        if (IsLegRateMissing(leg, curve))
            return [];

        var notional = leg.Notional ?? terms.PrincipalFace ?? 100m;
        if (notional <= 0m)
        {
            return [];
        }

        var dayCount = leg.DayCountConvention ?? terms.DayCountConvention;
        var periodMonths = Math.Max(1, 12 / SecurityTermConventions.LegacyCouponPaymentsPerYear(leg.PaymentFrequency ?? terms.PaymentFrequency));
        var periods = BuildPaymentPeriods(issueDate, maturity, periodMonths)
            .Where(period => period.PaymentDate >= asOfDate)
            .ToArray();
        if (periods.Length == 0)
        {
            return [];
        }

        var entries = new List<StructuredCashFlowScheduleEntry>(periods.Length);
        for (var i = 0; i < periods.Length; i++)
        {
            var (accrualStart, date) = periods[i];
            // An amortizing or accreting leg accrues on the notional in effect at the period start;
            // without a schedule this is the leg notional for every period, as before.
            var periodNotional = leg.NotionalOn(accrualStart, notional);
            var annualRate = ResolveLegAnnualRate(leg, scenario, curve, accrualStart, asOfDate);
            var interest = annualRate > 0m
                ? RoundCash(periodNotional * annualRate * DayCountConventions.Fraction(dayCount, accrualStart, date))
                : 0m;
            var isLast = i == periods.Length - 1;
            // With principal exchange, a notional step-down between periods pays the reduction at
            // the period end, and the final period returns what is left outstanding.
            var principal = !leg.ExchangesPrincipal
                ? 0m
                : isLast
                    ? RoundCash(periodNotional)
                    : RoundCash(Math.Max(0m, periodNotional - leg.NotionalOn(date, notional)));
            entries.Add(new StructuredCashFlowScheduleEntry(
                ToUtcDateTimeOffset(date),
                principal,
                interest,
                leg.ExchangesPrincipal && isLast ? 0m : 1m));
        }

        return entries;
    }

    private static decimal ResolveLegAnnualRate(
        StructuredCashFlowLeg leg,
        StructuredCashFlowScenario scenario,
        SecurityForwardCurve? curve,
        DateOnly accrualStart,
        DateOnly asOfDate)
    {
        // Fixed legs pay their contractual rate regardless of scenario; floating legs reset with
        // rates, so the scenario shift applies to them only. A period that has already reset (its
        // accrual started on or before the as-of date) keeps the retained fixing. Later resets read
        // the forward curve at the accrual start when one is supplied, and otherwise fall back to
        // the last fixing (flat forward).
        if (leg.RateKind == CashFlowLegRateKind.Fixed)
        {
            var fixedRate = NormalizeAnnualRate(leg.FixedRate ?? 0m);
            return fixedRate < 0m ? 0m : fixedRate;
        }

        // A period that has already reset uses the fixing recorded for its reset date when the leg
        // carries a reset schedule; otherwise the retained current fixing, as before.
        var recordedFixing = accrualStart <= asOfDate ? leg.FixingOn(accrualStart) : null;
        var hasReset = accrualStart <= asOfDate && leg.CurrentIndexRate is not null;
        var index = recordedFixing
            ?? (!hasReset && curve?.RateAt(accrualStart) is decimal projected
                ? projected
                : leg.CurrentIndexRate ?? curve?.RateAt(accrualStart) ?? 0m);
        var rate = NormalizeAnnualRate(index) + ((leg.SpreadBps ?? 0m) / 10_000m) + ScenarioRateShift(scenario);
        return rate < 0m ? 0m : rate;
    }

    private static IReadOnlyList<StructuredCashFlowScheduleEntry> BuildNetSchedule(
        IReadOnlyList<StructuredCashFlowLeg> legs,
        IReadOnlyList<StructuredCashFlowLegSchedule> legSchedules)
    {
        var byDate = new SortedDictionary<DateTimeOffset, (decimal Principal, decimal Interest)>();
        for (var i = 0; i < legSchedules.Count; i++)
        {
            var sign = legs[i].Direction == CashFlowLegDirection.Pay ? -1m : 1m;
            foreach (var entry in legSchedules[i].Schedule)
            {
                var current = byDate.TryGetValue(entry.PeriodDate, out var existing) ? existing : (0m, 0m);
                byDate[entry.PeriodDate] = (
                    current.Item1 + (sign * entry.PrincipalAmount),
                    current.Item2 + (sign * entry.InterestAmount));
            }
        }

        if (byDate.Count == 0)
        {
            return [];
        }

        var lastDate = byDate.Keys.Last();
        return byDate
            .Select(pair => new StructuredCashFlowScheduleEntry(
                pair.Key,
                pair.Value.Principal,
                pair.Value.Interest,
                pair.Key == lastDate ? 0m : 1m))
            .ToList();
    }

    private static string MapSourceKindToProviderId(StructuredCashFlowSourceKind kind) => kind switch
    {
        StructuredCashFlowSourceKind.MIAC => "miac",
        StructuredCashFlowSourceKind.MoodysAnalytics => "moodys-analytics",
        _ => string.Empty
    };

    /// <summary>
    /// Each coupon period's accrual start and payment date, ending on maturity: the kernel's <see cref="ScheduleSpec.IssueAnchoredForward"/> schedule.
    /// </summary>
    private static IEnumerable<(DateOnly AccrualStart, DateOnly PaymentDate)> BuildPaymentPeriods(
        DateOnly issueDate,
        DateOnly maturity,
        int periodMonths)
    {
        // Both callers clamp with Math.Max(1, ...), so this guards the contract rather than a reachable path.
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(periodMonths);

        var tenor = ScheduleTenor.Months(periodMonths);
        var schedule = ScheduleGenerator.Generate(ScheduleSpec.IssueAnchoredForward(issueDate, maturity, tenor));
        if (!schedule.Succeeded)
        {
            throw new InvalidOperationException(schedule.Failure.Message);
        }

        // The legacy loop evaluated the grid date that closed the schedule, so a maturity whose
        // closing grid date lies beyond the supported date range (a far-dated or perpetual sentinel
        // such as 9999-12-31 off the grid) threw ArgumentOutOfRangeException. The kernel treats that
        // date as unbounded and closes at maturity instead; evaluate it to keep the legacy failure
        // (SCH-DEF-010). Advance throws the same exception type for an out-of-range date.
        _ = tenor.Advance(issueDate, schedule.Periods.Count);

        return schedule.Periods.Select(static period => (period.AccrualStart, period.PaymentDate));
    }

    private static decimal NormalizeAnnualRate(decimal coupon)
        => coupon > 1m ? coupon / 100m : coupon;

    private static decimal ScenarioRateShift(StructuredCashFlowScenario scenario)
        => scenario switch
        {
            StructuredCashFlowScenario.Up100 => 0.01m,
            StructuredCashFlowScenario.Up200 => 0.02m,
            StructuredCashFlowScenario.Up300 => 0.03m,
            StructuredCashFlowScenario.Down100 => -0.01m,
            StructuredCashFlowScenario.Down200 => -0.02m,
            StructuredCashFlowScenario.Down300 => -0.03m,
            StructuredCashFlowScenario.Stress => 0.03m,
            _ => 0m
        };

    private static DateTimeOffset ToUtcDateTimeOffset(DateOnly date)
        => new(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

    private static decimal RoundCash(decimal amount)
        => decimal.Round(amount, 4, MidpointRounding.AwayFromZero);
}
