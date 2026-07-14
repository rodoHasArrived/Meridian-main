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
    /// accrual/amortization entry per schedule period (coupon accrual plus straight-line
    /// premium/discount amortization when a position is supplied), and a separate principal-paydown
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
/// derived here from the position because the schedule does not carry it.
/// </summary>
/// <param name="PositionFace">Held face/par of the position; drives premium/discount amortization. Zero disables it.</param>
/// <param name="PurchasePricePercentOfPar">Purchase price as a percent of par (e.g. 102 = 2% premium; 98 = 2% discount).</param>
/// <param name="FinancialAccountId">Optional account scope for the ledger accounts.</param>
/// <param name="Scenario">Rate scenario used to request the projection.</param>
/// <param name="MaxPeriods">Optional cap on how many schedule periods to post (null = all).</param>
public sealed record AmortizationLedgerPostingContext(
    decimal PositionFace = 0m,
    decimal PurchasePricePercentOfPar = 100m,
    string? FinancialAccountId = null,
    StructuredCashFlowScenario Scenario = StructuredCashFlowScenario.Base,
    int? MaxPeriods = null);

/// <inheritdoc />
public sealed class SecurityMasterAmortizationLedgerBridge : ISecurityMasterAmortizationLedgerBridge
{
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

        // Straight-line premium/discount over the posted periods; positive = premium (write-down),
        // negative = discount (write-up).
        var premiumDiscountTotal = postingContext.PositionFace > 0m
            ? RoundCash(postingContext.PositionFace * (postingContext.PurchasePricePercentOfPar - 100m) / 100m)
            : 0m;

        var allocatedPremiumDiscount = 0m;
        var posted = 0;

        for (var i = 0; i < schedule.Count; i++)
        {
            var entry = schedule[i];
            var effectiveDate = DateOnly.FromDateTime(entry.PeriodDate.UtcDateTime.Date);
            var timestamp = ToUtcStartOfDay(effectiveDate);

            // Cumulative-target distribution: each period's share is the rounded cumulative total
            // to date minus what has already been allocated. This keeps shares monotonic toward the
            // target so accumulated per-period rounding can never flip the final period's sign.
            var cumulativeTarget = RoundCash(premiumDiscountTotal * (i + 1) / schedule.Count);
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

    private static DateTimeOffset ToUtcStartOfDay(DateOnly date)
        => new(date.Year, date.Month, date.Day, 0, 0, 0, TimeSpan.Zero);

    private static decimal RoundCash(decimal amount)
        => decimal.Round(amount, 2, MidpointRounding.AwayFromZero);

    private static Guid DeriveJournalId(Guid securityId, DateOnly effectiveDate, string suffix)
        => new(MD5.HashData(Encoding.UTF8.GetBytes($"{securityId:D}:{effectiveDate:yyyy-MM-dd}:{suffix}")));
}
