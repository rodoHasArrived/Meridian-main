using System.Security.Cryptography;
using System.Text;
using Meridian.Application.SecurityMaster.CashFlow;
using Meridian.Contracts.SecurityMaster;
using Meridian.Ledger;
using Microsoft.Extensions.Logging;
using DomainLedger = Meridian.Ledger.Ledger;

namespace Meridian.Application.SecurityMaster;

/// <summary>
/// Posts structured cash flow / accrual / amortization projections from
/// <see cref="ISecurityMasterCashFlowService"/> into a <see cref="Ledger"/> using
/// <see cref="LedgerViewKind.SecurityMaster"/>, so projected coupon accrual, premium
/// amortization / discount accretion, and principal paydowns become balanced journal entries
/// instead of remaining display-only. Idempotent: entries whose deterministic journal id already
/// appears in the ledger are skipped.
/// </summary>
public interface ISecurityMasterAmortizationLedgerBridge
{
    /// <summary>
    /// Projects <paramref name="securityId"/>'s cash flow schedule and posts one balanced
    /// accrual/amortization entry per schedule period (coupon accrual plus premium/discount
    /// amortization derived from the supplied open lots), and a separate principal-paydown
    /// entry per period with a principal amount. Returns the number of journal entries posted.
    /// </summary>
    Task<int> PostProjectedCashFlowsAsync(
        Guid securityId,
        string ticker,
        DomainLedger ledger,
        AmortizationLedgerPostingContext? context = null,
        CancellationToken ct = default);
}

/// <summary>
/// Position-level context for posting projected cash flows. Coupon and principal amounts are
/// taken from the projection as-is (in the projection's own par basis); the premium/discount is
/// derived from the actual open tax lots (bond lots record <see cref="LedgerTaxLot.UnitCost"/> as a
/// price per 100 of par) and amortized by the projection's own day-count year fraction — the same
/// method the cost-basis relief engine uses — so GL postings and cost-basis relief tie by
/// construction instead of amortizing a caller-supplied purchase price on a different schedule.
/// </summary>
/// <param name="OpenLots">Open tax lots of the position; lots linked to the posted security drive premium/discount amortization. Null or empty disables it.</param>
/// <param name="FinancialAccountId">Optional account scope for the ledger accounts.</param>
/// <param name="Scenario">Rate scenario used to request the projection.</param>
/// <param name="MaxPeriods">Optional cap on how many schedule periods to post (null = all).</param>
/// <param name="ProjectionAsOf">
/// Instant the projection is requested for. The projection only contains flows on or after its
/// as-of date, so a caller posting one accounting period asks for the projection as of that
/// period's start. Null requests the projection as of now.
/// </param>
/// <param name="AmortizedThrough">
/// Date through which premium/discount amortization has already been recognized. Amortization up
/// to and including this date is excluded from the first posted period, so a caller posting
/// period by period never books the same amortization twice. Null amortizes from acquisition.
/// </param>
/// <param name="CouponPrincipalBasis">
/// The principal face the projection's interest amounts are computed on. When set together with an
/// accrual window, coupon is accrued only for the open lots: each lot earns its face share of the
/// projected interest from the later of the coupon period's start and its acquisition date. With
/// no open lot nothing accrues. Null accrues the projection's interest as-is.
/// </param>
/// <param name="AccrualWindow">
/// When set, posts one accrual entry dated the window's end instead of one entry per coupon
/// period: each coupon period contributes the day-count share of its interest that falls inside
/// the window, and premium/discount amortizes through the window's end. A coupon period with no
/// accrual dates is recognized whole when its period date falls in the window. Consecutive
/// windows therefore accrue every coupon exactly once, a little each period.
/// </param>
public sealed record AmortizationLedgerPostingContext(
    IReadOnlyList<LedgerTaxLot>? OpenLots = null,
    string? FinancialAccountId = null,
    StructuredCashFlowScenario Scenario = StructuredCashFlowScenario.Base,
    int? MaxPeriods = null,
    DateTimeOffset? ProjectionAsOf = null,
    DateOnly? AmortizedThrough = null,
    AmortizationAccrualWindow? AccrualWindow = null,
    decimal? CouponPrincipalBasis = null);

/// <summary>An inclusive range of accounting dates whose accrual is posted as one entry.</summary>
public sealed record AmortizationAccrualWindow
{
    public AmortizationAccrualWindow(DateOnly start, DateOnly end)
    {
        if (end < start)
            throw new ArgumentException("An accrual window cannot end before it starts.", nameof(end));
        Start = start;
        End = end;
    }

    public DateOnly Start { get; }

    public DateOnly End { get; }
}

/// <inheritdoc />
public sealed class SecurityMasterAmortizationLedgerBridge : ISecurityMasterAmortizationLedgerBridge
{
    private const decimal ParPrice = 100m;

    private readonly ISecurityMasterCashFlowService _cashFlowService;
    private readonly ILogger<SecurityMasterAmortizationLedgerBridge> _logger;

    public SecurityMasterAmortizationLedgerBridge(
        ISecurityMasterCashFlowService cashFlowService,
        ILogger<SecurityMasterAmortizationLedgerBridge> logger)
    {
        _cashFlowService = cashFlowService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<int> PostProjectedCashFlowsAsync(
        Guid securityId,
        string ticker,
        DomainLedger ledger,
        AmortizationLedgerPostingContext? context = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentException.ThrowIfNullOrWhiteSpace(ticker);

        var postingContext = context ?? new AmortizationLedgerPostingContext();
        var projection = postingContext.ProjectionAsOf is { } projectionAsOf
            ? await _cashFlowService.GetProjectionAsOfAsync(securityId, postingContext.Scenario, projectionAsOf, ct).ConfigureAwait(false)
            : await _cashFlowService.GetProjectionAsync(securityId, postingContext.Scenario, ct).ConfigureAwait(false);
        if (projection is null || projection.Schedule.Count == 0)
        {
            _logger.LogDebug(
                "SecurityMasterAmortizationLedgerBridge: no cash flow projection for {SecurityId} under scenario {Scenario}.",
                securityId, postingContext.Scenario);
            return 0;
        }

        // Shared freshness/scenario gate: never post a stale source or a rate-shocked what-if
        // projection to the general ledger, matching the preview bridge's posting rules.
        if (StructuredCashFlowLedgerGate.EvaluateBlockReason(projection) is { } blockReason)
        {
            _logger.LogWarning(
                "SecurityMasterAmortizationLedgerBridge: cash flow projection for {SecurityId} is not postable ({Reason}); no ledger entries posted.",
                securityId, blockReason);
            return 0;
        }

        var normalizedTicker = ticker.Trim().ToUpperInvariant();
        var financialAccountId = string.IsNullOrWhiteSpace(postingContext.FinancialAccountId)
            ? null
            : postingContext.FinancialAccountId.Trim();
        var carrying = LedgerAccounts.Securities(normalizedTicker, financialAccountId);
        var existingIds = ledger.Journal.Select(static j => j.JournalEntryId).ToHashSet();

        var schedule = projection.Schedule.OrderBy(static entry => entry.PeriodDate).ToList();
        if (postingContext.MaxPeriods is { } max && max >= 0 && schedule.Count > max)
            schedule = schedule.Take(max).ToList();
        if (schedule.Count == 0)
            return 0;

        // Premium/discount comes from the actual open lots and amortizes by the day-count year
        // fraction of the same terms the schedule was projected from; positive = premium
        // (write-down), negative = discount (write-up).
        var convention = DayCountConventions.Parse(projection.TermsUsed?.DayCountConvention);
        var maturity = projection.TermsUsed?.MaturityDate
            ?? DateOnly.FromDateTime(schedule[^1].PeriodDate.UtcDateTime.Date);
        var amortizingLots = BuildAmortizingLots(postingContext.OpenLots, securityId, convention, maturity);

        var allocatedPremiumDiscount = postingContext.AmortizedThrough is { } amortizedThrough
            ? RoundCash(amortizingLots.Sum(lot => lot.Total * lot.WeightThrough(convention, amortizedThrough)))
            : 0m;
        var posted = 0;

        if (postingContext.AccrualWindow is { } window)
        {
            // Without an explicit boundary, amortization before the window belongs to earlier windows.
            if (postingContext.AmortizedThrough is null)
                allocatedPremiumDiscount = RoundCash(amortizingLots.Sum(lot => lot.Total * lot.WeightThrough(convention, window.Start.AddDays(-1))));
            var windowTarget = RoundCash(amortizingLots.Sum(lot => lot.Total * lot.WeightThrough(convention, window.End)));
            var windowShare = windowTarget - allocatedPremiumDiscount;
            var windowCoupon = postingContext.CouponPrincipalBasis is { } principalBasis
                ? HeldCouponAccruedInWindow(schedule, convention, window, postingContext.OpenLots, securityId, principalBasis)
                : schedule.Sum(entry => CouponAccruedInWindow(entry, convention, window, 1m, null));
            if ((windowCoupon > 0m || windowShare != 0m) &&
                PostAmortizationEntry(ledger, existingIds, securityId, normalizedTicker, carrying, financialAccountId,
                    window.End, ToUtcStartOfDay(window.End), windowCoupon,
                    windowShare < 0m ? -windowShare : 0m, windowShare > 0m ? windowShare : 0m))
            {
                posted++;
            }

            foreach (var entry in schedule)
            {
                var paydownDate = DateOnly.FromDateTime(entry.PeriodDate.UtcDateTime.Date);
                var paydown = Math.Max(0m, RoundCash(entry.PrincipalAmount));
                if (paydown > 0m && paydownDate >= window.Start && paydownDate <= window.End &&
                    PostPrincipalPaydown(ledger, existingIds, securityId, normalizedTicker, carrying, financialAccountId,
                        paydownDate, ToUtcStartOfDay(paydownDate), paydown))
                {
                    posted++;
                }
            }

            return posted;
        }

        for (var i = 0; i < schedule.Count; i++)
        {
            var entry = schedule[i];
            var effectiveDate = DateOnly.FromDateTime(entry.PeriodDate.UtcDateTime.Date);
            var timestamp = ToUtcStartOfDay(effectiveDate);

            // Cumulative-target distribution: each period's share is the rounded cumulative
            // amortization target through that period minus what has already been allocated. Per-lot
            // weights grow monotonically toward 1, so accumulated per-period rounding can never flip
            // a single-direction premium (or discount) into the opposite posting.
            var cumulativeTarget = RoundCash(amortizingLots.Sum(lot => lot.Total * lot.WeightThrough(convention, effectiveDate)));
            var premiumDiscountShare = cumulativeTarget - allocatedPremiumDiscount;
            allocatedPremiumDiscount = cumulativeTarget;

            var couponAccrual = RoundCash(entry.InterestAmount);
            var premiumAmortization = premiumDiscountShare > 0m ? premiumDiscountShare : 0m;
            var discountAccretion = premiumDiscountShare < 0m ? -premiumDiscountShare : 0m;

            if (couponAccrual > 0m || premiumAmortization > 0m || discountAccretion > 0m)
            {
                if (PostAmortizationEntry(
                        ledger, existingIds, securityId, normalizedTicker, carrying, financialAccountId,
                        effectiveDate, timestamp, couponAccrual, discountAccretion, premiumAmortization))
                {
                    posted++;
                }
            }

            var principalPaydown = Math.Max(0m, RoundCash(entry.PrincipalAmount));
            if (principalPaydown > 0m &&
                PostPrincipalPaydown(
                    ledger, existingIds, securityId, normalizedTicker, carrying, financialAccountId,
                    effectiveDate, timestamp, principalPaydown))
            {
                posted++;
            }
        }

        if (posted > 0)
        {
            _logger.LogInformation(
                "SecurityMasterAmortizationLedgerBridge posted {Count} accrual/amortization entries for {Ticker}.",
                posted, normalizedTicker);
        }

        return posted;
    }

    /// <summary>
    /// The part of one coupon period's interest that accrues inside <paramref name="window"/>: the
    /// rounded amount accrued through the window's end less the rounded amount accrued through
    /// the day before it starts, so the shares of consecutive windows sum to the coupon exactly.
    /// </summary>
    /// <summary>
    /// Coupon accrued in the window on the lots actually held: every lot linked to the security
    /// earns its face share of each coupon period's interest, and only from the day it was acquired.
    /// </summary>
    private static decimal HeldCouponAccruedInWindow(
        IReadOnlyList<StructuredCashFlowScheduleEntry> schedule, DayCountConvention convention,
        AmortizationAccrualWindow window, IReadOnlyList<LedgerTaxLot>? openLots, Guid securityId, decimal principalBasis)
    {
        if (principalBasis <= 0m)
            throw new ArgumentOutOfRangeException(nameof(principalBasis), principalBasis, "Coupon principal basis must be positive.");

        var total = 0m;
        foreach (var lot in openLots ?? [])
        {
            if (lot.SecurityId != securityId || lot.AcquiredDate > window.End)
                continue;

            // Bond lots hold quantity in units of 100 of par.
            var share = lot.Quantity * ParPrice / principalBasis;
            total += schedule.Sum(entry => CouponAccruedInWindow(entry, convention, window, share, lot.AcquiredDate));
        }

        return total;
    }

    private static decimal CouponAccruedInWindow(
        StructuredCashFlowScheduleEntry entry, DayCountConvention convention, AmortizationAccrualWindow window,
        decimal share, DateOnly? heldFrom)
    {
        var interest = Math.Max(0m, RoundCash(entry.InterestAmount * share));
        if (interest == 0m)
            return 0m;

        if (entry.AccrualStart is not { } accrualStart || entry.AccrualEnd is not { } accrualEnd || accrualEnd <= accrualStart)
        {
            var periodDate = DateOnly.FromDateTime(entry.PeriodDate.UtcDateTime.Date);
            return periodDate >= window.Start && periodDate <= window.End && (heldFrom is null || heldFrom <= periodDate) ? interest : 0m;
        }

        var period = DayCountConventions.Fraction(convention, accrualStart, accrualEnd);
        if (period <= 0m)
            return 0m;

        decimal AccruedThrough(DateOnly date)
        {
            if (date <= accrualStart)
                return 0m;
            return date >= accrualEnd
                ? interest
                : RoundCash(interest * DayCountConventions.Fraction(convention, accrualStart, date) / period);
        }

        // Nothing accrues to this holder before the window or before the lot was acquired.
        var from = window.Start.AddDays(-1);
        if (heldFrom is { } acquired && acquired > from)
            from = acquired;
        return Math.Max(0m, AccruedThrough(window.End) - AccruedThrough(from));
    }

    private static bool PostAmortizationEntry(
        DomainLedger ledger,
        HashSet<Guid> existingIds,
        Guid securityId,
        string ticker,
        LedgerAccount carrying,
        string? financialAccountId,
        DateOnly effectiveDate,
        DateTimeOffset timestamp,
        decimal couponAccrual,
        decimal discountAccretion,
        decimal premiumAmortization)
    {
        var journalId = DeriveJournalId(securityId, effectiveDate, "amort");
        if (!existingIds.Add(journalId))
            return false;

        var description = $"Fixed-income accrual/amortization {ticker} {effectiveDate:yyyy-MM-dd}";
        var projection = FixedIncomeAmortizationProjector.Project(new FixedIncomeAmortizationInput(
            ticker,
            carrying,
            couponAccrual,
            discountAccretion,
            premiumAmortization,
            financialAccountId,
            description));

        var lines = projection.Lines
            .Select(line => new LedgerEntry(Guid.NewGuid(), journalId, timestamp, line.account, line.debit, line.credit, description))
            .ToList();

        ledger.Post(new JournalEntry(journalId, timestamp, description, lines, BuildMetadata(
            "FixedIncomeAmortization", ticker, securityId, financialAccountId, effectiveDate)));
        return true;
    }

    private static bool PostPrincipalPaydown(
        DomainLedger ledger,
        HashSet<Guid> existingIds,
        Guid securityId,
        string ticker,
        LedgerAccount carrying,
        string? financialAccountId,
        DateOnly effectiveDate,
        DateTimeOffset timestamp,
        decimal principalPaydown)
    {
        var journalId = DeriveJournalId(securityId, effectiveDate, "principal");
        if (!existingIds.Add(journalId))
            return false;

        var cash = string.IsNullOrWhiteSpace(financialAccountId)
            ? LedgerAccounts.Cash
            : LedgerAccounts.CashAccount(financialAccountId);
        var description = $"Principal paydown {ticker} {effectiveDate:yyyy-MM-dd}";

        var lines = new List<LedgerEntry>
        {
            new(Guid.NewGuid(), journalId, timestamp, cash, principalPaydown, 0m, description),
            new(Guid.NewGuid(), journalId, timestamp, carrying, 0m, principalPaydown, description),
        };

        ledger.Post(new JournalEntry(journalId, timestamp, description, lines, BuildMetadata(
            "PrincipalPaydown", ticker, securityId, financialAccountId, effectiveDate)));
        return true;
    }

    private static JournalEntryMetadata BuildMetadata(
        string activityType,
        string ticker,
        Guid securityId,
        string? financialAccountId,
        DateOnly effectiveDate)
        => new(
            ActivityType: activityType,
            Symbol: ticker,
            SecurityId: securityId,
            LedgerView: LedgerViewKind.SecurityMaster,
            FinancialAccountId: financialAccountId,
            EffectiveDate: effectiveDate);

    /// <summary>
    /// One open lot's contribution to premium/discount amortization: the signed dollar total to
    /// amortize over the lot's remaining life and the day-count weighting that spreads it.
    /// </summary>
    private sealed record AmortizingLot(decimal Total, DateOnly AcquiredDate, DateOnly Maturity, decimal Life)
    {
        /// <summary>
        /// The fraction of <see cref="Total"/> amortized from acquisition through
        /// <paramref name="periodDate"/> (clamped to maturity): elapsed day-count year fraction over
        /// the lot's full-life year fraction, capped at 1.
        /// </summary>
        public decimal WeightThrough(DayCountConvention convention, DateOnly periodDate)
        {
            var through = periodDate < Maturity ? periodDate : Maturity;
            if (through <= AcquiredDate)
                return 0m;

            var weight = DayCountConventions.Fraction(convention, AcquiredDate, through) / Life;
            return weight >= 1m ? 1m : weight;
        }
    }

    private static IReadOnlyList<AmortizingLot> BuildAmortizingLots(
        IReadOnlyList<LedgerTaxLot>? openLots,
        Guid securityId,
        DayCountConvention convention,
        DateOnly maturity)
    {
        if (openLots is null || openLots.Count == 0)
            return Array.Empty<AmortizingLot>();

        var lots = new List<AmortizingLot>();
        foreach (var lot in openLots)
        {
            // Only lots explicitly linked to the posted security amortize; unlinked lots carry no
            // reference-data linkage (mirroring the cost-basis adjustment service's lot filter).
            if (lot.SecurityId != securityId || lot.AcquiredDate >= maturity)
                continue;

            // Bond lots record UnitCost as a price per 100 of par, so quantity × (unit cost − par)
            // is the lot's signed premium (positive) or discount (negative) in dollars.
            var total = lot.Quantity * (lot.UnitCost - ParPrice);
            if (total == 0m)
                continue;

            var life = DayCountConventions.Fraction(convention, lot.AcquiredDate, maturity);
            if (life <= 0m)
                continue;

            lots.Add(new AmortizingLot(total, lot.AcquiredDate, maturity, life));
        }

        return lots;
    }

    private static DateTimeOffset ToUtcStartOfDay(DateOnly date)
        => new(date.Year, date.Month, date.Day, 0, 0, 0, TimeSpan.Zero);

    private static decimal RoundCash(decimal amount)
        => decimal.Round(amount, 2, MidpointRounding.AwayFromZero);

    private static Guid DeriveJournalId(Guid securityId, DateOnly effectiveDate, string suffix)
        => new(MD5.HashData(Encoding.UTF8.GetBytes($"{securityId:D}:{effectiveDate:yyyy-MM-dd}:{suffix}")));
}
