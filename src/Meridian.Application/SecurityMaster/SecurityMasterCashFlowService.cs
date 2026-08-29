using System.Text.Json;
using Meridian.Application.SecurityMaster.CashFlow;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging;



namespace Meridian.Application.SecurityMaster;

/// <summary>
/// Manages structured cash flow source assignments and delegates projections to
/// <see cref="IStructuredCashFlowProvider"/> implementations.
/// Client-provided sources take precedence and remain until explicitly changed,
/// consistent with the Clearwater cash flow governance model.
/// </summary>
/// <remarks>
/// Economic terms are resolved once, up front, into a typed <see cref="StructuredCashFlowTerms"/> via
/// <see cref="StructuredCashFlowTermsResolver"/> rather than probed inline; source freshness is
/// surfaced as a typed <see cref="StructuredCashFlowStaleness"/> on every projection; and a fresh
/// base-scenario projection can be routed to the ledger through
/// <see cref="IStructuredCashFlowLedgerBridge"/> instead of being display-only.
/// </remarks>
public sealed class SecurityMasterCashFlowService : ISecurityMasterCashFlowService
{
    private readonly ISecurityMasterCashFlowStore _store;
    private readonly IReadOnlyList<IStructuredCashFlowProvider> _providers;
    private readonly ISecurityMasterQueryService _queryService;
    private readonly IStructuredCashFlowLedgerBridge _ledgerBridge;
    private readonly ILogger<SecurityMasterCashFlowService> _logger;

    private static readonly TimeSpan StalenessThreshold = TimeSpan.FromDays(7);

    public SecurityMasterCashFlowService(
        ISecurityMasterCashFlowStore store,
        IEnumerable<IStructuredCashFlowProvider> providers,
        ISecurityMasterQueryService queryService,
        ILogger<SecurityMasterCashFlowService> logger,
        IStructuredCashFlowLedgerBridge? ledgerBridge = null)
    {
        _store = store;
        _providers = providers?.ToList() ?? [];
        _queryService = queryService;
        _logger = logger;
        _ledgerBridge = ledgerBridge ?? new StructuredCashFlowLedgerBridge();
    }

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

    public async Task<StructuredCashFlowProjectionDto?> GetProjectionAsync(
        Guid securityId, StructuredCashFlowScenario scenario, CancellationToken ct = default)
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
            return BuildCalculatedProjection(
                security, assignment.SourceKind, scenario, staleness, assignment.LastUpdatedUtc);
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
            securityId, isin, DateTimeOffset.UtcNow, scenario, ct).ConfigureAwait(false);

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

    private static StructuredCashFlowProjectionDto BuildCalculatedProjection(
        SecurityDetailDto security,
        StructuredCashFlowSourceKind sourceKind,
        StructuredCashFlowScenario scenario,
        StructuredCashFlowStaleness staleness,
        DateTimeOffset? sourceLastUpdatedUtc)
    {
        var asOf = DateTimeOffset.UtcNow;
        var terms = StructuredCashFlowTermsResolver.Resolve(security);
        var factorSchedule = terms.FactorSchedule;

        StructuredCashFlowProjectionDto Empty() => new(
            security.SecurityId, sourceKind, scenario, asOf, [], staleness, sourceLastUpdatedUtc, factorSchedule, terms);

        if (terms.MaturityDate is not DateOnly maturity)
        {
            return Empty();
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
                terms, factorSchedule, issueDate, maturity, asOf, asOfDate);
        }

        var principalBasis = terms.PrincipalFace is > 0m ? terms.PrincipalFace.Value : 100m;

        // Seed the outstanding factor from the typed factor schedule as of today, falling back to the
        // scalar current factor when no dated schedule point applies. The applied ENTRY (not just its
        // factor) is retained: a dated factor only reflects principal events up to its own as-of.
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
        StructuredFactorScheduleEntry? appliedFactorEntry = null;
        for (var i = terms.FactorSchedule.Count - 1; i >= 0; i--)
        {
            if (terms.FactorSchedule[i].AsOfDate <= asOfDate)
            {
                appliedFactorEntry = terms.FactorSchedule[i];
                break;
            }
        }

        // A scalar current factor is DATED evidence when a factorDate is retained — and dated
        // evidence from the FUTURE does not describe today's balance: a 0.8 factor effective next
        // month would understate today's outstanding (and every projected interest and principal
        // row) by 20%. Only an undated scalar, or one dated on or before the projection as-of,
        // may seed the balance; a future-dated scalar leaves the latest eligible schedule point
        // (already resolved above) or the unfactored walk in charge.
        var scalarFactorDate = SecurityTermReader.ReadDate(
            EnumerateFactorDateSources(security.AssetSpecificTerms), ["factorDate", "currentFactorDate"]);
        var scalarFactorApplies = scalarFactorDate is not DateOnly scalarAsOf || scalarAsOf <= asOfDate;
        var scheduledFactor = appliedFactorEntry?.Factor ?? (scalarFactorApplies ? terms.CurrentFactor : null);
        var hasAuthoritativeFactor = scheduledFactor is >= 0m;
        var factor = hasAuthoritativeFactor ? scheduledFactor.GetValueOrDefault() : 1m;
        var outstanding = RoundCash(principalBasis * factor);
        if (contractualPrincipal.Length > 0)
        {
            if (!hasAuthoritativeFactor)
            {
                // Without an as-of factor, completed contractual payments are the best retained
                // evidence of today's outstanding.
                var paidBeforeAsOf = contractualPrincipal
                    .Where(entry => entry.PaymentDate < asOfDate)
                    .Sum(static entry => entry.Amount);
                outstanding = RoundCash(decimal.Max(0m, outstanding - paidBeforeAsOf));
            }
            else if ((appliedFactorEntry?.AsOfDate ?? scalarFactorDate) is DateOnly factorEvidenceDate)
            {
                // A DATED factor reflects principal events only up to its as-of date: a January
                // factor of 0.8 does not know about February's contractual payment, so completed
                // payments dated after the factor and before today still reduce the opening
                // balance — otherwise later interest, maturity principal, and ledger postings are
                // overstated. A scalar currentFactor with a retained factorDate is dated evidence
                // too; only a genuinely undated scalar keeps the assumed-current fallback below.
                var paidAfterFactor = contractualPrincipal
                    .Where(entry => entry.PaymentDate > factorEvidenceDate && entry.PaymentDate < asOfDate)
                    .Sum(static entry => entry.Amount);
                outstanding = RoundCash(decimal.Max(0m, outstanding - paidAfterFactor));
            }
            // Undated scalar factor: assumed to already reflect every completed payment — reducing
            // again would double-count.
        }
        var annualRate = NormalizeAnnualRate(terms.CouponRate ?? 0m) + ScenarioRateShift(scenario);
        if (annualRate < 0m)
        {
            annualRate = 0m;
        }

        var periodMonths = Math.Max(1, 12 / ResolvePaymentFrequencyPerYear(terms.PaymentFrequency));
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
            var (accrualStart, date, isRegular) = periods[i];
            var interest = annualRate > 0m && outstanding > 0m
                ? RoundCash(outstanding * annualRate * PeriodAccrualFraction(
                    terms.DayCountConvention, accrualStart, date, isRegular, periodMonths))
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
        // Accrues between arbitrary event dates (interleaved principal and coupon dates), not whole
        // scheduled periods, so it keeps the true day count and ignores the regularity flag.
        IReadOnlyList<(DateOnly AccrualStart, DateOnly PaymentDate, bool IsRegular)> periods,
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
        DateOnly asOfDate)
    {
        var legs = terms.Legs!;
        var legSchedules = new List<StructuredCashFlowLegSchedule>(legs.Count);
        foreach (var leg in legs)
        {
            legSchedules.Add(new StructuredCashFlowLegSchedule(
                leg.LegId, leg.RateKind, leg.Direction,
                BuildLegSchedule(leg, terms, scenario, issueDate, maturity, asOfDate)));
        }

        // Net receive-minus-pay only when every leg's direction is known (a single directionless
        // leg projects as Receive). A multi-leg structure with an unknown direction cannot be
        // netted honestly, so the flat schedule stays empty (never posted) and consumers read the
        // per-leg schedules instead.
        var netSchedule = legs.Count == 1 || legs.All(static leg => leg.Direction is not null)
            ? BuildNetSchedule(legs, legSchedules)
            : [];

        return new StructuredCashFlowProjectionDto(
            security.SecurityId, sourceKind, scenario, asOf, netSchedule, staleness,
            sourceLastUpdatedUtc, factorSchedule, terms, legSchedules);
    }

    private static IReadOnlyList<StructuredCashFlowScheduleEntry> BuildLegSchedule(
        StructuredCashFlowLeg leg,
        StructuredCashFlowTerms terms,
        StructuredCashFlowScenario scenario,
        DateOnly issueDate,
        DateOnly maturity,
        DateOnly asOfDate)
    {
        var notional = leg.Notional ?? terms.PrincipalFace ?? 100m;
        if (notional <= 0m)
        {
            return [];
        }

        var annualRate = ResolveLegAnnualRate(leg, scenario);
        var dayCount = leg.DayCountConvention ?? terms.DayCountConvention;
        var periodMonths = Math.Max(1, 12 / ResolvePaymentFrequencyPerYear(leg.PaymentFrequency ?? terms.PaymentFrequency));
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
            var (accrualStart, date, isRegular) = periods[i];
            var interest = annualRate > 0m
                ? RoundCash(notional * annualRate * PeriodAccrualFraction(
                    dayCount, accrualStart, date, isRegular, periodMonths))
                : 0m;
            var isLast = i == periods.Length - 1;
            var principal = leg.ExchangesPrincipal && isLast ? RoundCash(notional) : 0m;
            entries.Add(new StructuredCashFlowScheduleEntry(
                ToUtcDateTimeOffset(date),
                principal,
                interest,
                leg.ExchangesPrincipal && isLast ? 0m : 1m));
        }

        return entries;
    }

    private static decimal ResolveLegAnnualRate(StructuredCashFlowLeg leg, StructuredCashFlowScenario scenario)
    {
        // Fixed legs pay their contractual rate regardless of scenario; floating legs reset with
        // rates, so the scenario shift applies to them only. Floating projects at the last fixing
        // plus spread — a deliberate flat-forward simplification, not a curve.
        var rate = leg.RateKind == CashFlowLegRateKind.Fixed
            ? NormalizeAnnualRate(leg.FixedRate ?? 0m)
            : NormalizeAnnualRate(leg.CurrentIndexRate ?? 0m) + ((leg.SpreadBps ?? 0m) / 10_000m) + ScenarioRateShift(scenario);
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
    /// True for the 30/360 family, whose defining property is that every regular period is a whole
    /// period — the convention exists so a regular semiannual coupon is always exactly half a year's
    /// interest. Actual-days conventions deliberately vary period to period and are excluded.
    /// </summary>
    private static bool IsThirtyDayMonthBasis(string? dayCountConvention)
        => DayCountConventions.Parse(dayCountConvention)
            is DayCountConvention.Thirty360
            or DayCountConvention.ThirtyE360
            or DayCountConvention.ThirtyE360Isda;

    /// <summary>
    /// Accrual fraction for one SCHEDULED period, as opposed to an arbitrary date range.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Under the 30/360 family a regular period accrues <c>periodMonths/12</c> rather than a day
    /// count re-derived from its endpoints. This matters only when the schedule roll clamps: an
    /// anchor on the 29th–31st rolls into a shorter month (29 Aug → 28 Feb), and 30/360 then counts
    /// 179 days for that period and 181 for the next. The year still totals 360, so annual interest
    /// was never wrong — but the individual coupons were unequal, which is precisely what 30/360
    /// exists to prevent. Where the anchor day is 28 or lower the roll never clamps and the two
    /// agree, so this changes nothing for the common case.
    /// </para>
    /// <para>
    /// Stub periods and every actual-days convention keep the true day count: there the variation is
    /// the intended answer, not an artefact.
    /// </para>
    /// </remarks>
    private static decimal PeriodAccrualFraction(
        string? dayCountConvention,
        DateOnly accrualStart,
        DateOnly paymentDate,
        bool isRegular,
        int periodMonths)
        => isRegular && IsThirtyDayMonthBasis(dayCountConvention)
            ? periodMonths / 12m
            : DayCountConventions.Fraction(dayCountConvention, accrualStart, paymentDate);

    /// <summary>
    /// Yields each coupon period as an accrual start and its payment date, ending on maturity, and
    /// reports whether the period is REGULAR — a whole period produced by the schedule roll — or a
    /// short final stub ending early at <paramref name="maturity"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every payment date is anchored on the issue date rather than compounded from the previous
    /// payment date. <see cref="DateOnly.AddMonths"/> clamps a month-end anchor into shorter months
    /// (31 Jan plus three months is 30 Apr), and compounding that clamp walks the schedule earlier
    /// every period. For a bond issued on the 31st that drift lands the final period one day before
    /// maturity and emits a spurious one-day stub, so a one-year quarterly bond yields five payments
    /// instead of four. Anchoring keeps each period on the issue day-of-month, clamping only where
    /// the target month is shorter.
    /// </para>
    /// <para>
    /// Regularity is a property of that roll, not of how far apart the dates sit: the same clamping
    /// can leave a regular period's endpoints fewer calendar days apart than its nominal term
    /// (29 Aug + 6M is 28 Feb) while it remains a whole period. Accrual reads this flag rather than
    /// re-deriving the answer from the endpoints — see <see cref="PeriodAccrualFraction"/>.
    /// </para>
    /// </remarks>
    private static IEnumerable<(DateOnly AccrualStart, DateOnly PaymentDate, bool IsRegular)> BuildPaymentPeriods(
        DateOnly issueDate,
        DateOnly maturity,
        int periodMonths)
    {
        // A non-positive period would never advance the anchor and would spin forever. Both callers
        // clamp with Math.Max(1, ...), so this guards the contract rather than a reachable path.
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(periodMonths);

        if (issueDate >= maturity)
        {
            yield break;
        }

        var accrualStart = issueDate;
        for (var period = 1; ; period++)
        {
            var paymentDate = issueDate.AddMonths(periodMonths * period);
            if (paymentDate >= maturity)
            {
                // Regular when the roll lands exactly on maturity; a maturity that falls short of
                // the next roll date leaves a stub, which accrues on its true day count.
                yield return (accrualStart, maturity, paymentDate == maturity);
                yield break;
            }

            yield return (accrualStart, paymentDate, true);
            accrualStart = paymentDate;
        }
    }

    private static int ResolvePaymentFrequencyPerYear(string? paymentFrequency)
    {
        if (string.IsNullOrWhiteSpace(paymentFrequency))
        {
            return 1;
        }

        var normalized = paymentFrequency
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Trim()
            .ToUpperInvariant();
        if (int.TryParse(normalized, out var parsed) && parsed > 0)
        {
            return Math.Clamp(parsed, 1, 12);
        }

        return normalized switch
        {
            "MONTHLY" => 12,
            "QUARTERLY" => 4,
            "SEMIANNUAL" or "SEMIANNUALLY" or "SEMIYEARLY" or "HALFYEARLY" => 2,
            "ANNUAL" or "ANNUALLY" or "YEARLY" => 1,
            _ => 1
        };
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

    /// <summary>
    /// Term sources for the scalar factor-date lookup, in the SAME nested-first precedence
    /// <see cref="StructuredCashFlowTermsResolver"/> uses for the factor and schedule themselves:
    /// a profile-backed record persists its governed scalar terms beneath
    /// <c>profileFields</c>, so probing only the envelope root would miss the retained date and
    /// misread a dated factor as current.
    /// </summary>
    private static IEnumerable<JsonElement> EnumerateFactorDateSources(JsonElement assetSpecificTerms)
    {
        if (assetSpecificTerms.ValueKind != JsonValueKind.Object)
        {
            yield break;
        }

        if (SecurityTermReader.TryGetProperty(assetSpecificTerms, "profileFields", out var profileFields)
            && profileFields.ValueKind == JsonValueKind.Object)
        {
            yield return profileFields;
        }

        yield return assetSpecificTerms;
    }

    private static decimal RoundCash(decimal amount)
        => decimal.Round(amount, 4, MidpointRounding.AwayFromZero);
}
