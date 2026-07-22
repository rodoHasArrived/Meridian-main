using Meridian.Contracts.Ledger;
using static Meridian.Contracts.Ledger.LedgerCurrencyRounding;

namespace Meridian.Ledger;

/// <summary>One investor's unit holding in a share class.</summary>
public sealed record ShareClassUnitHolding(
    string InvestorId,
    decimal Units,
    decimal SubscribedAmount,
    decimal RedeemedAmount,
    decimal EqualisationCredit,
    decimal ContingentRedemption)
{
    /// <summary>Current value of the holding at the class NAV per unit.</summary>
    public decimal Value(decimal navPerUnit) => RoundCurrency(Units * navPerUnit);
}

/// <summary>One processed unit-register transaction with its resulting units and equalization.</summary>
public sealed record ShareClassUnitMovement(
    string TransactionId,
    string InvestorId,
    DateOnly DealingDate,
    UnitTransactionType Type,
    decimal NavPerUnit,
    decimal Amount,
    decimal Units,
    decimal EqualisationCredit,
    decimal ContingentRedemption);

/// <summary>Unitized state of a share class after folding its transactions to a valuation date.</summary>
public sealed record ShareClassUnitRegister(
    ShareClass ShareClass,
    DateOnly AsOf,
    decimal UnitsOutstanding,
    decimal ClassNav,
    decimal NavPerUnit,
    decimal HighWaterNavPerUnit,
    IReadOnlyList<ShareClassUnitHolding> Holdings,
    IReadOnlyList<ShareClassUnitMovement> Movements,
    IReadOnlyList<AccountingConfigurationValidationIssueDto> ValidationIssues)
{
    /// <summary>Aggregate value of all holdings at the class NAV per unit; ties to class NAV.</summary>
    public decimal AggregateHoldingValue => Holdings.Sum(holding => holding.Value(NavPerUnit));
}

/// <summary>
/// Folds a share class's subscription/redemption transactions into a unit register: units
/// outstanding, per-investor holdings, NAV per unit, running high-water mark, and — for equalising
/// classes — the equalisation credit / contingent redemption on each subscription. Pure and
/// deterministic. NAV per unit at the valuation date uses the supplied class NAV when known,
/// otherwise the last dealing price.
/// </summary>
public static class ShareClassUnitRegisterProjector
{
    public const string RedemptionExceedsHoldingIssueCode = "fund.share-class.redemption-exceeds-holding";

    public static ShareClassUnitRegister Project(
        ShareClass shareClass,
        IReadOnlyList<UnitTransaction> transactions,
        DateOnly asOf,
        decimal? classNav = null)
    {
        ArgumentNullException.ThrowIfNull(shareClass);
        ArgumentNullException.ThrowIfNull(transactions);

        var issues = new List<AccountingConfigurationValidationIssueDto>();
        var holdings = new Dictionary<string, HoldingState>(StringComparer.OrdinalIgnoreCase);
        var movements = new List<ShareClassUnitMovement>();

        var unitsOutstanding = 0m;
        var highWater = shareClass.InceptionNavPerUnit;
        var lastPrice = shareClass.InceptionNavPerUnit;

        foreach (var transaction in transactions
            .Where(item => string.Equals(item.ShareClassId, shareClass.ShareClassId, StringComparison.OrdinalIgnoreCase)
                && item.DealingDate <= asOf)
            .OrderBy(static item => item.DealingDate)
            .ThenBy(static item => item.TransactionId, StringComparer.Ordinal))
        {
            lastPrice = transaction.NavPerUnit;
            var holding = holdings.TryGetValue(transaction.InvestorId, out var existing) ? existing : new HoldingState();

            if (transaction.Type == UnitTransactionType.Subscription)
            {
                var units = NavPerUnitCalculator.UnitsForSubscription(transaction.Amount, transaction.NavPerUnit);
                var adjustment = shareClass.EqualizationMethod == EqualizationMethod.Equalisation
                    ? EqualizationCalculator.Compute(transaction.NavPerUnit, highWater, units, shareClass.PerformanceFeeRate)
                    : new EqualizationAdjustment(0m, 0m);

                holding.Units = NavPerUnitCalculator.RoundUnits(holding.Units + units);
                holding.Subscribed = RoundCurrency(holding.Subscribed + transaction.Amount);
                holding.EqualisationCredit = RoundCurrency(holding.EqualisationCredit + adjustment.EqualisationCredit);
                holding.ContingentRedemption = RoundCurrency(holding.ContingentRedemption + adjustment.ContingentRedemption);
                unitsOutstanding = NavPerUnitCalculator.RoundUnits(unitsOutstanding + units);

                movements.Add(new ShareClassUnitMovement(
                    transaction.TransactionId,
                    transaction.InvestorId,
                    transaction.DealingDate,
                    transaction.Type,
                    transaction.NavPerUnit,
                    transaction.Amount,
                    units,
                    adjustment.EqualisationCredit,
                    adjustment.ContingentRedemption));
            }
            else
            {
                var units = NavPerUnitCalculator.RoundUnits(transaction.Units);
                if (units > holding.Units + LedgerToleranceConstants.Balance)
                {
                    issues.Add(new AccountingConfigurationValidationIssueDto(
                        RedemptionExceedsHoldingIssueCode,
                        AccountingConfigurationValidationSeverityDto.Critical,
                        $"Redemption of {units} units by '{transaction.InvestorId}' exceeds the held {holding.Units} units in class '{shareClass.ShareClassId}'.",
                        transaction.TransactionId,
                        "Reduce the redemption to at most the investor's held units."));
                    units = holding.Units;
                }

                var cash = NavPerUnitCalculator.CashForRedemption(units, transaction.NavPerUnit);
                holding.Units = NavPerUnitCalculator.RoundUnits(holding.Units - units);
                holding.Redeemed = RoundCurrency(holding.Redeemed + cash);
                unitsOutstanding = NavPerUnitCalculator.RoundUnits(unitsOutstanding - units);

                movements.Add(new ShareClassUnitMovement(
                    transaction.TransactionId,
                    transaction.InvestorId,
                    transaction.DealingDate,
                    transaction.Type,
                    transaction.NavPerUnit,
                    cash,
                    units,
                    0m,
                    0m));
            }

            holdings[transaction.InvestorId] = holding;
            highWater = Math.Max(highWater, transaction.NavPerUnit);
        }

        var navPerUnit = classNav is { } nav
            ? NavPerUnitCalculator.ComputeNavPerUnit(nav, unitsOutstanding, shareClass.InceptionNavPerUnit)
            : NavPerUnitCalculator.RoundPrice(lastPrice);
        highWater = Math.Max(highWater, navPerUnit);
        var resolvedClassNav = classNav ?? RoundCurrency(unitsOutstanding * navPerUnit);

        var holdingList = holdings
            .Where(static pair => pair.Value.Units > 0m
                || pair.Value.Subscribed > 0m
                || pair.Value.Redeemed > 0m)
            .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
            .Select(static pair => new ShareClassUnitHolding(
                pair.Key,
                pair.Value.Units,
                pair.Value.Subscribed,
                pair.Value.Redeemed,
                pair.Value.EqualisationCredit,
                pair.Value.ContingentRedemption))
            .ToArray();

        return new ShareClassUnitRegister(
            shareClass,
            asOf,
            unitsOutstanding,
            resolvedClassNav,
            navPerUnit,
            highWater,
            holdingList,
            movements,
            issues);
    }

    private sealed class HoldingState
    {
        public decimal Units;
        public decimal Subscribed;
        public decimal Redeemed;
        public decimal EqualisationCredit;
        public decimal ContingentRedemption;
    }
}
