namespace Meridian.FinancialOperations.Reconciliation;

/// <summary>
/// Matches normalized broker statement positions, cash balances, and transactions against
/// Meridian's internal portfolio, cash, and ledger views using deterministic staged rules.
/// </summary>
public sealed class StatementMatchingEngine
{
    private const string PositionExactRuleId = "statement-position-exact-v1";
    private const string PositionToleranceRuleId = "statement-position-tolerance-v1";
    private const string PositionCandidateRuleId = "statement-position-candidate-v1";
    private const string PositionBreakRuleId = "statement-position-break-v1";
    private const string CashExactRuleId = "statement-cash-exact-v1";
    private const string CashToleranceRuleId = "statement-cash-tolerance-v1";
    private const string CashCandidateRuleId = "statement-cash-candidate-v1";
    private const string CashBreakRuleId = "statement-cash-break-v1";
    private const string TransactionExternalIdRuleId = "statement-transaction-external-id-v1";
    private const string TransactionExactRuleId = "statement-transaction-exact-v1";
    private const string TransactionToleranceRuleId = "statement-transaction-tolerance-v1";
    private const string TransactionCandidateRuleId = "statement-transaction-candidate-v1";
    private const string TransactionBreakRuleId = "statement-transaction-break-v1";

    public StatementMatchingResult Run(StatementMatchingRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.ToleranceProfile);

        var results = new List<StatementMatchResult>(
            request.StatementPositions.Count + request.StatementCashBalances.Count + request.StatementTransactions.Count
            + request.InternalPositions.Count + request.InternalCashBalances.Count + request.InternalLedgerTransactions.Count);

        MatchPositions(request, results);
        MatchCash(request, results);
        MatchTransactions(request, results);

        return new StatementMatchingResult(results);
    }

    private static void MatchPositions(StatementMatchingRequest request, List<StatementMatchResult> results)
    {
        var matchedStatements = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var matchedInternal = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var tolerance = request.ToleranceProfile;

        MatchStage(
            request.StatementPositions,
            request.InternalPositions,
            matchedStatements,
            matchedInternal,
            (statement, internalPosition) => SamePositionIdentity(statement, internalPosition)
                && statement.Quantity == internalPosition.Quantity
                && OptionalMarketValueEquals(statement.MarketValue, internalPosition.MarketValue),
            (statement, internalPosition) => CreatePositionResult(
                statement,
                internalPosition,
                StatementMatchTier.Exact,
                1.00m,
                [PositionExactRuleId],
                PositionVariance(statement, internalPosition),
                new StatementMatchTolerance(0m, 0m),
                "Exact position match on account, security, as-of date, quantity, and market value when provided."),
            results);

        MatchStage(
            request.StatementPositions,
            request.InternalPositions,
            matchedStatements,
            matchedInternal,
            (statement, internalPosition) => SamePositionIdentity(statement, internalPosition)
                && Abs(statement.Quantity - internalPosition.Quantity) <= tolerance.PositionQuantity
                && OptionalMarketValueWithinTolerance(statement.MarketValue, internalPosition.MarketValue, tolerance.PositionMarketValue),
            (statement, internalPosition) => CreatePositionResult(
                statement,
                internalPosition,
                StatementMatchTier.Tolerance,
                ConfidenceFromVariance(PositionVariance(statement, internalPosition).LargestAbsoluteAmount, PositionToleranceAmount(statement, internalPosition, tolerance)),
                [PositionToleranceRuleId],
                PositionVariance(statement, internalPosition),
                new StatementMatchTolerance(tolerance.PositionQuantity, StatementMarketValueTolerance(statement, internalPosition, tolerance)),
                "Position matched inside configured quantity and market-value tolerances."),
            results);

        MatchBestCandidate(
            request.StatementPositions,
            request.InternalPositions,
            matchedStatements,
            matchedInternal,
            (statement, internalPosition) => SameText(statement.Account, internalPosition.Account) && SameText(statement.SecurityId, internalPosition.SecurityId),
            (statement, internalPosition) => PositionVariance(statement, internalPosition).LargestAbsoluteAmount + Math.Abs(statement.AsOfDate.DayNumber - internalPosition.AsOfDate.DayNumber),
            (statement, internalPosition) => CreatePositionResult(
                statement,
                internalPosition,
                StatementMatchTier.Candidate,
                0.55m,
                [PositionCandidateRuleId],
                PositionVariance(statement, internalPosition),
                new StatementMatchTolerance(tolerance.PositionQuantity, StatementMarketValueTolerance(statement, internalPosition, tolerance)),
                "Position candidate shares account and security but requires operator review for date or amount variance."),
            results);

        AddUnmatchedPositions(request.StatementPositions, request.InternalPositions, matchedStatements, matchedInternal, tolerance, results);
    }

    private static void MatchCash(StatementMatchingRequest request, List<StatementMatchResult> results)
    {
        var matchedStatements = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var matchedInternal = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var tolerance = request.ToleranceProfile;

        MatchStage(
            request.StatementCashBalances,
            request.InternalCashBalances,
            matchedStatements,
            matchedInternal,
            (statement, internalCash) => SameCashIdentity(statement, internalCash) && statement.EndingBalance == internalCash.Balance,
            (statement, internalCash) => CreateCashResult(
                statement,
                internalCash,
                StatementMatchTier.Exact,
                1.00m,
                [CashExactRuleId],
                CashVariance(statement, internalCash),
                new StatementMatchTolerance(Amount: tolerance.CashBalance),
                "Exact cash match on account, currency, statement ending balance, and internal cash balance."),
            results);

        MatchStage(
            request.StatementCashBalances,
            request.InternalCashBalances,
            matchedStatements,
            matchedInternal,
            (statement, internalCash) => SameCashIdentity(statement, internalCash)
                && Abs(statement.EndingBalance - internalCash.Balance) <= tolerance.CashBalance,
            (statement, internalCash) => CreateCashResult(
                statement,
                internalCash,
                StatementMatchTier.Tolerance,
                ConfidenceFromVariance(Abs(statement.EndingBalance - internalCash.Balance), tolerance.CashBalance),
                [CashToleranceRuleId],
                CashVariance(statement, internalCash),
                new StatementMatchTolerance(Amount: tolerance.CashBalance),
                "Cash balance matched inside configured balance tolerance."),
            results);

        MatchBestCandidate(
            request.StatementCashBalances,
            request.InternalCashBalances,
            matchedStatements,
            matchedInternal,
            SameCashIdentity,
            (statement, internalCash) => Abs(statement.EndingBalance - internalCash.Balance),
            (statement, internalCash) => CreateCashResult(
                statement,
                internalCash,
                StatementMatchTier.Candidate,
                0.60m,
                [CashCandidateRuleId],
                CashVariance(statement, internalCash),
                new StatementMatchTolerance(Amount: tolerance.CashBalance),
                "Cash candidate shares account and currency but exceeds configured balance tolerance."),
            results);

        AddUnmatchedCash(request.StatementCashBalances, request.InternalCashBalances, matchedStatements, matchedInternal, tolerance, results);
    }

    private static void MatchTransactions(StatementMatchingRequest request, List<StatementMatchResult> results)
    {
        var matchedStatements = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var matchedInternal = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var tolerance = request.ToleranceProfile;

        MatchStage(
            request.StatementTransactions,
            request.InternalLedgerTransactions,
            matchedStatements,
            matchedInternal,
            (statement, internalTransaction) => HasSameExternalTransactionId(statement, internalTransaction)
                && SameTransactionIdentity(statement, internalTransaction)
                && statement.Quantity == internalTransaction.Quantity
                && statement.NetAmount == internalTransaction.NetAmount,
            (statement, internalTransaction) => CreateTransactionResult(
                statement,
                internalTransaction,
                StatementMatchTier.Exact,
                1.00m,
                [TransactionExternalIdRuleId],
                TransactionVariance(statement, internalTransaction),
                TransactionTolerance(tolerance),
                "Exact transaction match on external transaction ID and transaction details."),
            results);

        MatchStage(
            request.StatementTransactions,
            request.InternalLedgerTransactions,
            matchedStatements,
            matchedInternal,
            (statement, internalTransaction) => SameTransactionIdentity(statement, internalTransaction)
                && statement.Quantity == internalTransaction.Quantity
                && statement.NetAmount == internalTransaction.NetAmount,
            (statement, internalTransaction) => CreateTransactionResult(
                statement,
                internalTransaction,
                StatementMatchTier.Exact,
                1.00m,
                [TransactionExactRuleId],
                TransactionVariance(statement, internalTransaction),
                TransactionTolerance(tolerance),
                "Exact transaction match on account, instrument or currency, dates, type, quantity, and net amount."),
            results);

        MatchStage(
            request.StatementTransactions,
            request.InternalLedgerTransactions,
            matchedStatements,
            matchedInternal,
            (statement, internalTransaction) => SameTransactionIdentity(statement, internalTransaction)
                && Abs(statement.Quantity - internalTransaction.Quantity) <= tolerance.TransactionQuantity
                && Abs(statement.NetAmount - internalTransaction.NetAmount) <= tolerance.TransactionNetAmount,
            (statement, internalTransaction) => CreateTransactionResult(
                statement,
                internalTransaction,
                StatementMatchTier.Tolerance,
                ConfidenceFromVariance(TransactionVariance(statement, internalTransaction).LargestAbsoluteAmount, Math.Max(tolerance.TransactionQuantity, tolerance.TransactionNetAmount)),
                [TransactionToleranceRuleId],
                TransactionVariance(statement, internalTransaction),
                TransactionTolerance(tolerance),
                "Transaction matched inside configured quantity and net-amount tolerances."),
            results);

        MatchBestCandidate(
            request.StatementTransactions,
            request.InternalLedgerTransactions,
            matchedStatements,
            matchedInternal,
            (statement, internalTransaction) => SameText(statement.Account, internalTransaction.Account)
                && SameText(statement.TransactionType, internalTransaction.TransactionType)
                && SameTransactionInstrumentOrCurrency(statement, internalTransaction)
                && Math.Abs(statement.TradeDate.DayNumber - internalTransaction.TradeDate.DayNumber) <= tolerance.CandidateDateWindowDays,
            (statement, internalTransaction) => TransactionVariance(statement, internalTransaction).LargestAbsoluteAmount
                + Math.Abs(statement.SettlementDate.DayNumber - internalTransaction.SettlementDate.DayNumber),
            (statement, internalTransaction) => CreateTransactionResult(
                statement,
                internalTransaction,
                StatementMatchTier.Candidate,
                0.50m,
                [TransactionCandidateRuleId],
                TransactionVariance(statement, internalTransaction),
                TransactionTolerance(tolerance),
                "Transaction candidate shares account, instrument or currency, type, and nearby dates but requires operator review."),
            results);

        AddUnmatchedTransactions(request.StatementTransactions, request.InternalLedgerTransactions, matchedStatements, matchedInternal, tolerance, results);
    }

    private static void MatchStage<TStatement, TInternal>(
        IReadOnlyList<TStatement> statements,
        IReadOnlyList<TInternal> internalItems,
        HashSet<string> matchedStatements,
        HashSet<string> matchedInternal,
        Func<TStatement, TInternal, bool> isMatch,
        Func<TStatement, TInternal, StatementMatchResult> createResult,
        List<StatementMatchResult> results)
        where TStatement : class, IStatementMatchItem
        where TInternal : class, IStatementMatchItem
    {
        foreach (var statement in statements)
        {
            if (matchedStatements.Contains(statement.MatchId))
                continue;

            foreach (var internalItem in internalItems)
            {
                if (matchedInternal.Contains(internalItem.MatchId) || !isMatch(statement, internalItem))
                    continue;

                matchedStatements.Add(statement.MatchId);
                matchedInternal.Add(internalItem.MatchId);
                results.Add(createResult(statement, internalItem));
                break;
            }
        }
    }

    private static void MatchBestCandidate<TStatement, TInternal>(
        IReadOnlyList<TStatement> statements,
        IReadOnlyList<TInternal> internalItems,
        HashSet<string> matchedStatements,
        HashSet<string> matchedInternal,
        Func<TStatement, TInternal, bool> isCandidate,
        Func<TStatement, TInternal, decimal> scoreCandidate,
        Func<TStatement, TInternal, StatementMatchResult> createResult,
        List<StatementMatchResult> results)
        where TStatement : class, IStatementMatchItem
        where TInternal : class, IStatementMatchItem
    {
        foreach (var statement in statements)
        {
            if (matchedStatements.Contains(statement.MatchId))
                continue;

            TInternal? best = default;
            decimal? bestScore = null;
            foreach (var internalItem in internalItems)
            {
                if (matchedInternal.Contains(internalItem.MatchId) || !isCandidate(statement, internalItem))
                    continue;

                var score = scoreCandidate(statement, internalItem);
                if (bestScore is null || score < bestScore)
                {
                    best = internalItem;
                    bestScore = score;
                }
            }

            if (best is null)
                continue;

            matchedStatements.Add(statement.MatchId);
            matchedInternal.Add(best.MatchId);
            results.Add(createResult(statement, best));
        }
    }

    private static void AddUnmatchedPositions(
        IReadOnlyList<NormalizedStatementPosition> statements,
        IReadOnlyList<InternalPortfolioPosition> internalItems,
        HashSet<string> matchedStatements,
        HashSet<string> matchedInternal,
        StatementMatchingToleranceProfile tolerance,
        List<StatementMatchResult> results)
    {
        foreach (var statement in statements.Where(statement => !matchedStatements.Contains(((IStatementMatchItem)statement).MatchId)))
        {
            results.Add(new StatementMatchResult(
                StatementMatchKind.Position,
                StatementMatchTier.Unmatched,
                0m,
                [PositionBreakRuleId],
                statement.EvidenceReference,
                null,
                new StatementMatchVariance(Quantity: statement.Quantity, MarketValue: statement.MarketValue),
                new StatementMatchTolerance(tolerance.PositionQuantity, tolerance.PositionMarketValue),
                "Broker statement position did not match any internal portfolio position."));
        }

        foreach (var internalItem in internalItems.Where(internalItem => !matchedInternal.Contains(((IStatementMatchItem)internalItem).MatchId)))
        {
            results.Add(new StatementMatchResult(
                StatementMatchKind.Position,
                StatementMatchTier.Unmatched,
                0m,
                [PositionBreakRuleId],
                null,
                internalItem.EvidenceReference,
                new StatementMatchVariance(Quantity: -internalItem.Quantity, MarketValue: internalItem.MarketValue is null ? null : -internalItem.MarketValue),
                new StatementMatchTolerance(tolerance.PositionQuantity, tolerance.PositionMarketValue),
                "Internal portfolio position did not match any broker statement position."));
        }
    }

    private static void AddUnmatchedCash(
        IReadOnlyList<NormalizedStatementCashBalance> statements,
        IReadOnlyList<InternalCashBalance> internalItems,
        HashSet<string> matchedStatements,
        HashSet<string> matchedInternal,
        StatementMatchingToleranceProfile tolerance,
        List<StatementMatchResult> results)
    {
        foreach (var statement in statements.Where(statement => !matchedStatements.Contains(((IStatementMatchItem)statement).MatchId)))
        {
            results.Add(new StatementMatchResult(
                StatementMatchKind.Cash,
                StatementMatchTier.Unmatched,
                0m,
                [CashBreakRuleId],
                statement.EvidenceReference,
                null,
                new StatementMatchVariance(Amount: statement.EndingBalance),
                new StatementMatchTolerance(Amount: tolerance.CashBalance),
                "Broker statement cash balance did not match any internal cash balance."));
        }

        foreach (var internalItem in internalItems.Where(internalItem => !matchedInternal.Contains(((IStatementMatchItem)internalItem).MatchId)))
        {
            results.Add(new StatementMatchResult(
                StatementMatchKind.Cash,
                StatementMatchTier.Unmatched,
                0m,
                [CashBreakRuleId],
                null,
                internalItem.EvidenceReference,
                new StatementMatchVariance(Amount: -internalItem.Balance),
                new StatementMatchTolerance(Amount: tolerance.CashBalance),
                "Internal cash balance did not match any broker statement cash balance."));
        }
    }

    private static void AddUnmatchedTransactions(
        IReadOnlyList<NormalizedStatementTransaction> statements,
        IReadOnlyList<InternalLedgerTransaction> internalItems,
        HashSet<string> matchedStatements,
        HashSet<string> matchedInternal,
        StatementMatchingToleranceProfile tolerance,
        List<StatementMatchResult> results)
    {
        foreach (var statement in statements.Where(statement => !matchedStatements.Contains(((IStatementMatchItem)statement).MatchId)))
        {
            results.Add(new StatementMatchResult(
                StatementMatchKind.Transaction,
                StatementMatchTier.Unmatched,
                0m,
                [TransactionBreakRuleId],
                statement.EvidenceReference,
                null,
                new StatementMatchVariance(Quantity: statement.Quantity, Amount: statement.NetAmount),
                TransactionTolerance(tolerance),
                "Broker statement transaction did not match any internal ledger transaction."));
        }

        foreach (var internalItem in internalItems.Where(internalItem => !matchedInternal.Contains(((IStatementMatchItem)internalItem).MatchId)))
        {
            results.Add(new StatementMatchResult(
                StatementMatchKind.Transaction,
                StatementMatchTier.Unmatched,
                0m,
                [TransactionBreakRuleId],
                null,
                internalItem.EvidenceReference,
                new StatementMatchVariance(Quantity: -internalItem.Quantity, Amount: -internalItem.NetAmount),
                TransactionTolerance(tolerance),
                "Internal ledger transaction did not match any broker statement transaction."));
        }
    }

    private static StatementMatchResult CreatePositionResult(
        NormalizedStatementPosition statement,
        InternalPortfolioPosition internalPosition,
        StatementMatchTier tier,
        decimal confidence,
        IReadOnlyList<string> ruleIds,
        StatementMatchVariance variance,
        StatementMatchTolerance tolerance,
        string explanation) => new(
            StatementMatchKind.Position,
            tier,
            confidence,
            ruleIds,
            statement.EvidenceReference,
            internalPosition.EvidenceReference,
            variance,
            tolerance,
            explanation);

    private static StatementMatchResult CreateCashResult(
        NormalizedStatementCashBalance statement,
        InternalCashBalance internalCash,
        StatementMatchTier tier,
        decimal confidence,
        IReadOnlyList<string> ruleIds,
        StatementMatchVariance variance,
        StatementMatchTolerance tolerance,
        string explanation) => new(
            StatementMatchKind.Cash,
            tier,
            confidence,
            ruleIds,
            statement.EvidenceReference,
            internalCash.EvidenceReference,
            variance,
            tolerance,
            explanation);

    private static StatementMatchResult CreateTransactionResult(
        NormalizedStatementTransaction statement,
        InternalLedgerTransaction internalTransaction,
        StatementMatchTier tier,
        decimal confidence,
        IReadOnlyList<string> ruleIds,
        StatementMatchVariance variance,
        StatementMatchTolerance tolerance,
        string explanation) => new(
            StatementMatchKind.Transaction,
            tier,
            confidence,
            ruleIds,
            statement.EvidenceReference,
            internalTransaction.EvidenceReference,
            variance,
            tolerance,
            explanation);

    private static bool SamePositionIdentity(NormalizedStatementPosition statement, InternalPortfolioPosition internalPosition) =>
        SameText(statement.Account, internalPosition.Account)
        && SameText(statement.SecurityId, internalPosition.SecurityId)
        && statement.AsOfDate == internalPosition.AsOfDate;

    private static bool SameCashIdentity(NormalizedStatementCashBalance statement, InternalCashBalance internalCash) =>
        statement.IsForStatementPeriodEnd
        && SameText(statement.Account, internalCash.Account)
        && SameText(statement.Currency, internalCash.Currency)
        && statement.AsOfDate == internalCash.AsOfDate;

    private static bool SameTransactionIdentity(NormalizedStatementTransaction statement, InternalLedgerTransaction internalTransaction) =>
        SameText(statement.Account, internalTransaction.Account)
        && SameTransactionInstrumentOrCurrency(statement, internalTransaction)
        && statement.TradeDate == internalTransaction.TradeDate
        && statement.SettlementDate == internalTransaction.SettlementDate
        && SameText(statement.TransactionType, internalTransaction.TransactionType);

    private static bool SameTransactionInstrumentOrCurrency(NormalizedStatementTransaction statement, InternalLedgerTransaction internalTransaction)
    {
        if (!string.IsNullOrWhiteSpace(statement.SecurityId) || !string.IsNullOrWhiteSpace(internalTransaction.SecurityId))
            return SameNullableText(statement.SecurityId, internalTransaction.SecurityId);

        return SameNullableText(statement.Currency, internalTransaction.Currency);
    }

    private static bool HasSameExternalTransactionId(NormalizedStatementTransaction statement, InternalLedgerTransaction internalTransaction) =>
        !string.IsNullOrWhiteSpace(statement.ExternalTransactionId)
        && SameNullableText(statement.ExternalTransactionId, internalTransaction.ExternalTransactionId);

    private static bool OptionalMarketValueEquals(decimal? statementMarketValue, decimal? internalMarketValue) =>
        statementMarketValue is null || internalMarketValue is null || statementMarketValue == internalMarketValue;

    private static bool OptionalMarketValueWithinTolerance(decimal? statementMarketValue, decimal? internalMarketValue, decimal tolerance) =>
        statementMarketValue is null || internalMarketValue is null || Abs(statementMarketValue.Value - internalMarketValue.Value) <= tolerance;

    private static StatementMatchVariance PositionVariance(NormalizedStatementPosition statement, InternalPortfolioPosition internalPosition) =>
        new(
            Quantity: statement.Quantity - internalPosition.Quantity,
            MarketValue: statement.MarketValue is null || internalPosition.MarketValue is null ? null : statement.MarketValue - internalPosition.MarketValue);

    private static StatementMatchVariance CashVariance(NormalizedStatementCashBalance statement, InternalCashBalance internalCash) =>
        new(Amount: statement.EndingBalance - internalCash.Balance);

    private static StatementMatchVariance TransactionVariance(NormalizedStatementTransaction statement, InternalLedgerTransaction internalTransaction) =>
        new(
            Quantity: statement.Quantity - internalTransaction.Quantity,
            Amount: statement.NetAmount - internalTransaction.NetAmount);

    private static decimal PositionToleranceAmount(
        NormalizedStatementPosition statement,
        InternalPortfolioPosition internalPosition,
        StatementMatchingToleranceProfile tolerance)
    {
        var quantityTolerance = tolerance.PositionQuantity;
        var marketValueTolerance = StatementMarketValueTolerance(statement, internalPosition, tolerance);
        return Math.Max(quantityTolerance, marketValueTolerance);
    }

    private static decimal StatementMarketValueTolerance(
        NormalizedStatementPosition statement,
        InternalPortfolioPosition internalPosition,
        StatementMatchingToleranceProfile tolerance) =>
        statement.MarketValue is null || internalPosition.MarketValue is null ? 0m : tolerance.PositionMarketValue;

    private static StatementMatchTolerance TransactionTolerance(StatementMatchingToleranceProfile tolerance) =>
        new(tolerance.TransactionQuantity, tolerance.TransactionNetAmount);

    private static decimal ConfidenceFromVariance(decimal variance, decimal tolerance)
    {
        if (tolerance <= 0m)
            return variance == 0m ? 1.00m : 0.75m;

        var normalized = Math.Clamp(1m - (variance / tolerance), 0m, 1m);
        return decimal.Round(0.75m + (normalized * 0.20m), 2);
    }

    private static bool SameText(string left, string right) => string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool SameNullableText(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) && string.IsNullOrWhiteSpace(right))
            return true;

        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            return false;

        return string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static decimal Abs(decimal value) => Math.Abs(value);
}

public sealed record StatementMatchingRequest(
    IReadOnlyList<NormalizedStatementPosition> StatementPositions,
    IReadOnlyList<NormalizedStatementCashBalance> StatementCashBalances,
    IReadOnlyList<NormalizedStatementTransaction> StatementTransactions,
    IReadOnlyList<InternalPortfolioPosition> InternalPositions,
    IReadOnlyList<InternalCashBalance> InternalCashBalances,
    IReadOnlyList<InternalLedgerTransaction> InternalLedgerTransactions,
    StatementMatchingToleranceProfile ToleranceProfile);

public sealed record StatementMatchingToleranceProfile(
    decimal PositionQuantity,
    decimal PositionMarketValue,
    decimal CashBalance,
    decimal TransactionQuantity,
    decimal TransactionNetAmount,
    int CandidateDateWindowDays = 2);

public sealed record NormalizedStatementPosition(
    string PositionId,
    string Account,
    string SecurityId,
    DateOnly AsOfDate,
    decimal Quantity,
    decimal? MarketValue,
    string EvidenceReference) : IStatementMatchItem
{
    string IStatementMatchItem.MatchId => PositionId;
}

public sealed record NormalizedStatementCashBalance(
    string CashBalanceId,
    string Account,
    string Currency,
    decimal EndingBalance,
    string EvidenceReference,
    DateOnly AsOfDate,
    bool IsForStatementPeriodEnd = true) : IStatementMatchItem
{
    string IStatementMatchItem.MatchId => CashBalanceId;
}

public sealed record NormalizedStatementTransaction(
    string TransactionId,
    string? ExternalTransactionId,
    string Account,
    string? SecurityId,
    string? Currency,
    DateOnly TradeDate,
    DateOnly SettlementDate,
    string TransactionType,
    decimal Quantity,
    decimal NetAmount,
    string EvidenceReference) : IStatementMatchItem
{
    string IStatementMatchItem.MatchId => TransactionId;
}

public sealed record InternalPortfolioPosition(
    string PositionId,
    string Account,
    string SecurityId,
    DateOnly AsOfDate,
    decimal Quantity,
    decimal? MarketValue,
    string EvidenceReference) : IStatementMatchItem
{
    string IStatementMatchItem.MatchId => PositionId;
}

public sealed record InternalCashBalance(
    string CashBalanceId,
    string Account,
    string Currency,
    decimal Balance,
    string EvidenceReference,
    DateOnly AsOfDate) : IStatementMatchItem
{
    string IStatementMatchItem.MatchId => CashBalanceId;
}

public sealed record InternalLedgerTransaction(
    string TransactionId,
    string? ExternalTransactionId,
    string Account,
    string? SecurityId,
    string? Currency,
    DateOnly TradeDate,
    DateOnly SettlementDate,
    string TransactionType,
    decimal Quantity,
    decimal NetAmount,
    string EvidenceReference) : IStatementMatchItem
{
    string IStatementMatchItem.MatchId => TransactionId;
}

public sealed record StatementMatchingResult(IReadOnlyList<StatementMatchResult> Results);

public sealed record StatementMatchResult(
    StatementMatchKind Kind,
    StatementMatchTier MatchTier,
    decimal Confidence,
    IReadOnlyList<string> RuleIds,
    string? BrokerEvidenceReference,
    string? InternalEvidenceReference,
    StatementMatchVariance Variance,
    StatementMatchTolerance Tolerance,
    string Explanation);

public sealed record StatementMatchVariance(
    decimal? Quantity = null,
    decimal? MarketValue = null,
    decimal? Amount = null)
{
    public decimal LargestAbsoluteAmount => new[] { Quantity, MarketValue, Amount }
        .Where(static value => value.HasValue)
        .Select(static value => Math.Abs(value!.Value))
        .DefaultIfEmpty(0m)
        .Max();
}

public sealed record StatementMatchTolerance(
    decimal? Quantity = null,
    decimal? Amount = null);

public enum StatementMatchKind
{
    Position,
    Cash,
    Transaction
}

public enum StatementMatchTier
{
    Exact,
    Tolerance,
    Candidate,
    Unmatched
}

public interface IStatementMatchItem
{
    string MatchId { get; }
}
