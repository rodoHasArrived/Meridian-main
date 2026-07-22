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
public sealed record AmortizationLedgerPostingContext(
    IReadOnlyList<LedgerTaxLot>? OpenLots = null,
    string? FinancialAccountId = null,
    StructuredCashFlowScenario Scenario = StructuredCashFlowScenario.Base,
    int? MaxPeriods = null);

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
        var projection = await _cashFlowService.GetProjectionAsync(securityId, postingContext.Scenario, ct).ConfigureAwait(false);
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

        var allocatedPremiumDiscount = 0m;
        var posted = 0;

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

            var couponAccrual = Math.Max(0m, RoundCash(entry.InterestAmount));
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
