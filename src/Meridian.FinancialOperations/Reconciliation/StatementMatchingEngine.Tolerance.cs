namespace Meridian.FinancialOperations.Reconciliation;

public sealed partial class StatementMatchingEngine
{
    // Profile order is rule precedence, matching ReconciliationMatchingEngine. The legacy scalar
    // API remains available to direct engine callers; live statement runs always supply Rules.
    private static IEnumerable<PositionToleranceRule> PositionRules(StatementMatchingToleranceProfile tolerance) =>
        tolerance.Rules?.PositionRules ?? [new(PositionToleranceRuleId, tolerance.PositionQuantity, tolerance.PositionMarketValue, 0m)];

    private static IEnumerable<CashToleranceRule> CashRules(StatementMatchingToleranceProfile tolerance) =>
        tolerance.Rules?.CashRules ?? [new(CashToleranceRuleId, tolerance.CashBalance, null, TimeSpan.Zero)];

    private static IEnumerable<TransactionToleranceRule> TransactionRules(StatementMatchingToleranceProfile tolerance) =>
        tolerance.Rules?.TransactionRules ?? [new(TransactionToleranceRuleId, tolerance.TransactionNetAmount, TimeSpan.Zero, 0m)];

    private static PositionToleranceRule? FindPositionRule(NormalizedStatementPosition statement,
        InternalPortfolioPosition internalPosition, StatementMatchingToleranceProfile tolerance) =>
        PositionRules(tolerance).FirstOrDefault(rule => rule.Allows(
            internalPosition.Quantity, statement.Quantity,
            internalPosition.MarketValue ?? 0m,
            statement.MarketValue is not null && internalPosition.MarketValue is not null ? statement.MarketValue.Value : internalPosition.MarketValue ?? 0m,
            internalPosition.Price ?? 0m,
            ComparablePrice(statement.Price, internalPosition.Price)));

    private static CashToleranceRule? FindCashRule(NormalizedStatementCashBalance statement,
        InternalCashBalance internalCash, StatementMatchingToleranceProfile tolerance) =>
        CashRules(tolerance).FirstOrDefault(rule => rule.Allows(internalCash.Balance, statement.EndingBalance,
            DateDelta(statement.AsOfDate, internalCash.AsOfDate), out _));

    private static TransactionToleranceRule? FindTransactionRule(NormalizedStatementTransaction statement,
        InternalLedgerTransaction internalTransaction, StatementMatchingToleranceProfile tolerance) =>
        TransactionRules(tolerance).FirstOrDefault(rule => rule.Allows(internalTransaction.NetAmount, statement.NetAmount,
            DateDelta(statement.SettlementDate, internalTransaction.SettlementDate), internalTransaction.Price ?? 0m,
            ComparablePrice(statement.Price, internalTransaction.Price)));

    // Price is optional evidence in the population contracts, like position market value. Compare
    // retained prices only when both sides supply them; net cash includes fees and is not a price.
    private static decimal ComparablePrice(decimal? actual, decimal? expected) =>
        actual.HasValue && expected.HasValue ? actual.Value : expected ?? 0m;

    private static bool OptionalPriceEquals(decimal? actual, decimal? expected) =>
        !actual.HasValue || !expected.HasValue || actual == expected;

    private static TimeSpan DateDelta(DateOnly actual, DateOnly expected) =>
        TimeSpan.FromDays(actual.DayNumber - expected.DayNumber);

    private static StatementMatchTolerance PositionRuleTolerance(PositionToleranceRule rule) =>
        new(Math.Abs(rule.QuantityTolerance), Math.Abs(rule.MarketValueTolerance));

    private static StatementMatchTolerance CashRuleTolerance(NormalizedStatementCashBalance statement,
        InternalCashBalance internalCash, CashToleranceRule rule)
    {
        rule.Allows(internalCash.Balance, statement.EndingBalance, DateDelta(statement.AsOfDate, internalCash.AsOfDate), out var allowed);
        return new StatementMatchTolerance(Amount: allowed);
    }

    private static decimal? WeightedPrice<T>(IReadOnlyList<T> legs, Func<T, decimal> quantity, Func<T, decimal?> price)
    {
        if (legs.Any(leg => price(leg) is null))
        {
            return null;
        }

        var weight = legs.Sum(leg => Math.Abs(quantity(leg)));
        return weight == 0m ? null : legs.Sum(leg => price(leg)!.Value * Math.Abs(quantity(leg))) / weight;
    }

    private static bool TryMatchSplit<TLeg>(
        decimal anchorNetAmount,
        decimal anchorQuantity,
        IReadOnlyList<TLeg> legs,
        Func<TLeg, string> legId,
        Func<TLeg, decimal> legNetAmount,
        Func<TLeg, decimal> legQuantity,
        Func<TLeg, TransactionToleranceRule, bool> allowsLeg,
        Func<IReadOnlyList<TLeg>, TransactionToleranceRule, bool> allowsRule,
        StatementMatchingToleranceProfile tolerance,
        out IReadOnlyList<TLeg> selectedLegs,
        out decimal amountVariance,
        out decimal quantityVariance,
        out TransactionToleranceRule? appliedRule)
        where TLeg : class
    {
        selectedLegs = [];
        amountVariance = 0m;
        quantityVariance = 0m;
        appliedRule = null;
        if (legs.Count < 2)
        {
            return false;
        }

        var legById = legs.ToDictionary(legId, StringComparer.Ordinal);
        // Exact splits precede configured tolerance rules, just as exact pairs do. Every leg must
        // meet the selected settlement window and the weighted retained price must satisfy that
        // same rule; independent maximum thresholds must never assemble a synthetic policy.
        var exact = new TransactionToleranceRule(TransactionSplitRuleId, 0m, TimeSpan.Zero, 0m);
        foreach (var rule in new[] { exact }.Concat(TransactionRules(tolerance)))
        {
            var quantityTolerance = ReferenceEquals(rule, exact) ? 0m : tolerance.TransactionQuantity;
            if (!ReconciliationMatchKernel.TryFindSplit(
                anchorNetAmount,
                legs.Where(leg => allowsLeg(leg, rule)).Select(leg => new ReconciliationMatchKernel.SplitCandidate(legId(leg), legNetAmount(leg))).ToArray(),
                Math.Abs(rule.AbsoluteCashTolerance),
                MaxTransactionSplitLegs,
                accept: subset => Abs(anchorQuantity - subset.Sum(leg => legQuantity(legById[leg.Id]))) <= quantityTolerance
                    && allowsRule(subset.Select(leg => legById[leg.Id]).ToArray(), rule),
                out var kernelLegs,
                out var residual))
            {
                continue;
            }

            selectedLegs = kernelLegs
                .Select(leg => legById[leg.Id])
                .OrderBy(legId, StringComparer.Ordinal)
                .ToArray();
            amountVariance = residual;
            quantityVariance = anchorQuantity - selectedLegs.Sum(legQuantity);
            appliedRule = ReferenceEquals(rule, exact) ? null : rule;
            return true;
        }

        return false;
    }
}
