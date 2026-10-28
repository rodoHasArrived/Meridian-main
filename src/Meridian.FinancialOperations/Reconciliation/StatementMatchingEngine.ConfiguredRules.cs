namespace Meridian.FinancialOperations.Reconciliation;

public sealed partial class StatementMatchingEngine
{
    // Profile list order is policy precedence. A rule completes its deterministic assignment
    // before the next rule runs, and its actual ID is retained with every accepted result.
    private static void MatchConfiguredPositionRules(
        StatementMatchingRequest request, HashSet<string> statements, HashSet<string> internalItems,
        StatementToleranceProfile profile, List<StatementMatchResult> results)
    {
        foreach (var rule in profile.PositionRules)
        {
            MatchStage(request.StatementPositions, request.InternalPositions, statements, internalItems,
                (statement, position) => SamePositionIdentity(statement, position)
                    && rule.Allows(position.Quantity, statement.Quantity,
                        ComparableValue(position.MarketValue, statement.MarketValue),
                        ComparableValue(statement.MarketValue, position.MarketValue),
                        ComparableValue(PositionPrice(position), PositionPrice(statement)),
                        ComparableValue(PositionPrice(statement), PositionPrice(position))),
                (statement, position) => NormalizedVariance(statement.Quantity, position.Quantity, rule.QuantityTolerance)
                    + NormalizedVariance(ComparableValue(statement.MarketValue, position.MarketValue),
                        ComparableValue(position.MarketValue, statement.MarketValue), rule.MarketValueTolerance)
                    + NormalizedPriceVariance(PositionPrice(statement), PositionPrice(position), rule.PriceTolerance),
                (statement, position) => CreatePositionResult(statement, position,
                    StatementMatchTier.Tolerance,
                    ConfidenceFromVariance(PositionVariance(statement, position).LargestAbsoluteAmount,
                        Math.Max(Math.Abs(rule.QuantityTolerance), Math.Abs(rule.MarketValueTolerance))),
                    [rule.RuleId, PositionToleranceRuleId], PositionVariance(statement, position),
                    new StatementMatchTolerance(Math.Abs(rule.QuantityTolerance), Math.Abs(rule.MarketValueTolerance)),
                    "Position matched the configured quantity, market-value, and price rule."),
                results);
        }
    }

    private static void MatchConfiguredCashRules(
        StatementMatchingRequest request, HashSet<string> statements, HashSet<string> internalItems,
        StatementToleranceProfile profile, List<StatementMatchResult> results)
    {
        foreach (var rule in profile.CashRules)
        {
            MatchStage(request.StatementCashBalances, request.InternalCashBalances, statements, internalItems,
                (statement, cash) => statement.IsForStatementPeriodEnd
                    && SameText(statement.Account, cash.Account) && SameText(statement.Currency, cash.Currency)
                    && rule.Allows(cash.Balance, statement.EndingBalance,
                        DateDelta(statement.AsOfDate, cash.AsOfDate), out _),
                (statement, cash) =>
                {
                    rule.Allows(cash.Balance, statement.EndingBalance,
                        DateDelta(statement.AsOfDate, cash.AsOfDate), out var allowed);
                    return NormalizedVariance(statement.EndingBalance, cash.Balance, allowed)
                        + NormalizedVariance(DateDelta(statement.AsOfDate, cash.AsOfDate).Ticks, 0m,
                            rule.SettlementDateTolerance.Ticks);
                },
                (statement, cash) =>
                {
                    rule.Allows(cash.Balance, statement.EndingBalance,
                        DateDelta(statement.AsOfDate, cash.AsOfDate), out var allowed);
                    return CreateCashResult(statement, cash, StatementMatchTier.Tolerance,
                        ConfidenceFromVariance(Abs(statement.EndingBalance - cash.Balance), allowed),
                        [rule.RuleId, CashToleranceRuleId], CashVariance(statement, cash),
                        new StatementMatchTolerance(Amount: allowed),
                        "Cash matched the configured absolute/basis-point amount and settlement-window rule.");
                }, results);
        }
    }

    private static void MatchConfiguredTransactionRule(
        StatementMatchingRequest request, HashSet<string> statements, HashSet<string> internalItems,
        TransactionToleranceRule rule, List<StatementMatchResult> results)
    {
        MatchStage(request.StatementTransactions, request.InternalLedgerTransactions, statements, internalItems,
            (statement, transaction) => SameSplitIdentity(statement, transaction, rule)
                && statement.Quantity == transaction.Quantity
                && AllowsTransaction(rule, transaction.NetAmount, statement.NetAmount,
                    DateDelta(statement.SettlementDate, transaction.SettlementDate),
                    TransactionPrice(transaction), TransactionPrice(statement)),
            (statement, transaction) => NormalizedVariance(statement.NetAmount, transaction.NetAmount, rule.AbsoluteCashTolerance)
                + NormalizedPriceVariance(TransactionPrice(statement), TransactionPrice(transaction), rule.PriceTolerance)
                + NormalizedVariance(DateDelta(statement.SettlementDate, transaction.SettlementDate).Ticks, 0m,
                    rule.SettlementDateTolerance.Ticks),
            (statement, transaction) => CreateTransactionResult(statement, transaction,
                StatementMatchTier.Tolerance,
                ConfidenceFromVariance(Abs(statement.NetAmount - transaction.NetAmount), Math.Abs(rule.AbsoluteCashTolerance)),
                [rule.RuleId, TransactionToleranceRuleId], TransactionVariance(statement, transaction),
                new StatementMatchTolerance(0m, Math.Abs(rule.AbsoluteCashTolerance)),
                "Transaction matched the configured amount, settlement-window, and price rule."),
            results);
    }

    private static bool SameSplitIdentity(NormalizedStatementTransaction statement,
        InternalLedgerTransaction transaction, TransactionToleranceRule? rule) =>
        rule is null ? SameTransactionIdentity(statement, transaction)
            : SameText(statement.Account, transaction.Account)
                && SameTransactionInstrumentAndCurrency(statement, transaction)
                && statement.TradeDate == transaction.TradeDate
                && SameText(statement.TransactionType, transaction.TransactionType)
                && DateDelta(statement.SettlementDate, transaction.SettlementDate).Duration()
                    <= rule.SettlementDateTolerance.Duration();

    private static TimeSpan DateDelta(DateOnly left, DateOnly right) => TimeSpan.FromDays(left.DayNumber - right.DayNumber);

    // Each eligible dimension contributes a fraction of its own allowed tolerance, so
    // dollar, quantity, price and date units cannot silently dominate one another.
    private static decimal NormalizedVariance(decimal actual, decimal expected, decimal tolerance) =>
        tolerance == 0m ? 0m : Math.Abs(actual - expected) / Math.Abs(tolerance);

    private static decimal NormalizedPriceVariance(decimal? actual, decimal? expected, decimal tolerance) =>
        actual is { } left && expected is { } right ? NormalizedVariance(left, right, tolerance) : 0m;

    private static decimal? PositionPrice(NormalizedStatementPosition position) =>
        position.Price ?? UnitPrice(position.MarketValue, position.Quantity);

    private static decimal? PositionPrice(InternalPortfolioPosition position) =>
        position.Price ?? UnitPrice(position.MarketValue, position.Quantity);

    // Net cash includes fees and cannot stand in for the execution price.
    private static decimal? TransactionPrice(NormalizedStatementTransaction transaction) => transaction.Price;

    private static decimal? TransactionPrice(InternalLedgerTransaction transaction) => transaction.Price;

    private static decimal? UnitPrice(decimal? marketValue, decimal quantity) =>
        marketValue is { } amount && quantity != 0m ? Math.Abs(amount / quantity) : null;

    private static bool OptionalPriceEquals(decimal? left, decimal? right) =>
        left is null || right is null || left == right;

    // Quantity-only snapshots and journals without execution prices retain their existing
    // comparison scope. Only prices observed on both sides can constrain a match.
    private static decimal ComparableValue(decimal? value, decimal? other) => value ?? other ?? 0m;

    private static bool AllowsTransaction(TransactionToleranceRule rule, decimal expectedAmount,
        decimal actualAmount, TimeSpan settlementDelta, decimal? expectedPrice, decimal? actualPrice) =>
        rule.Allows(expectedAmount, actualAmount, settlementDelta,
            ComparableValue(expectedPrice, actualPrice), ComparableValue(actualPrice, expectedPrice));

    private static decimal? SplitPrice<T>(IReadOnlyList<T> legs, Func<T, decimal> quantity, Func<T, decimal?> price)
    {
        if (legs.Any(leg => price(leg) is null))
            return null;
        var totalQuantity = legs.Sum(leg => Math.Abs(quantity(leg)));
        return totalQuantity == 0m ? null : legs.Sum(leg => Math.Abs(quantity(leg)) * price(leg)!.Value) / totalQuantity;
    }
}
