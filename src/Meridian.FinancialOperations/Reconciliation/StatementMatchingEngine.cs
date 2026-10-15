namespace Meridian.FinancialOperations.Reconciliation;

/// <summary>
/// Matches normalized broker statement positions, cash balances, and transactions against
/// Meridian's internal portfolio, cash, and ledger views using deterministic staged rules. Pair
/// selection routes through <see cref="ReconciliationMatchKernel.SelectDeterministicAssignment{TPair}"/>
/// with side-qualified member keys, and transactions additionally get bounded one-to-many /
/// many-to-one split matching over the kernel's split search, so identical populations produce
/// identical results regardless of input enumeration order.
/// </summary>
public sealed partial class StatementMatchingEngine
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
    private const string TransactionSplitRuleId = "statement-transaction-split-v1";
    private const string TransactionCandidateRuleId = "statement-transaction-candidate-v1";
    private const string TransactionBreakRuleId = "statement-transaction-break-v1";
    private const int MaxTransactionSplitLegs = 4;

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
                && OptionalMarketValueEquals(statement.MarketValue, internalPosition.MarketValue)
                && OptionalPriceEquals(statement.Price, internalPosition.Price),
            static (_, _) => 0m,
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
                && FindPositionRule(statement, internalPosition, tolerance) is not null,
            static (statement, internalPosition) => PositionVariance(statement, internalPosition).LargestAbsoluteAmount,
            (statement, internalPosition) => CreatePositionResult(
                statement,
                internalPosition,
                StatementMatchTier.Tolerance,
                ConfidenceFromVariance(PositionVariance(statement, internalPosition).LargestAbsoluteAmount,
                    Math.Max(Math.Abs(FindPositionRule(statement, internalPosition, tolerance)!.QuantityTolerance), Math.Abs(FindPositionRule(statement, internalPosition, tolerance)!.MarketValueTolerance))),
                [FindPositionRule(statement, internalPosition, tolerance)!.RuleId],
                PositionVariance(statement, internalPosition),
                PositionRuleTolerance(FindPositionRule(statement, internalPosition, tolerance)!),
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
            static (_, _) => 0m,
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
            (statement, internalCash) => SameCashScope(statement, internalCash)
                && FindCashRule(statement, internalCash, tolerance) is not null,
            static (statement, internalCash) => Abs(statement.EndingBalance - internalCash.Balance),
            (statement, internalCash) => CreateCashResult(
                statement,
                internalCash,
                StatementMatchTier.Tolerance,
                ConfidenceFromVariance(Abs(statement.EndingBalance - internalCash.Balance), CashRuleTolerance(statement, internalCash, FindCashRule(statement, internalCash, tolerance)!).Amount ?? 0m),
                [FindCashRule(statement, internalCash, tolerance)!.RuleId],
                CashVariance(statement, internalCash),
                CashRuleTolerance(statement, internalCash, FindCashRule(statement, internalCash, tolerance)!),
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
                && statement.NetAmount == internalTransaction.NetAmount
                && OptionalPriceEquals(statement.Price, internalTransaction.Price),
            static (_, _) => 0m,
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
                && statement.NetAmount == internalTransaction.NetAmount
                && OptionalPriceEquals(statement.Price, internalTransaction.Price),
            static (_, _) => 0m,
            (statement, internalTransaction) => CreateTransactionResult(
                statement,
                internalTransaction,
                StatementMatchTier.Exact,
                1.00m,
                [TransactionExactRuleId],
                TransactionVariance(statement, internalTransaction),
                TransactionTolerance(tolerance),
                "Exact transaction match on account, security and currency, dates, type, quantity, and net amount."),
            results);

        MatchStage(
            request.StatementTransactions,
            request.InternalLedgerTransactions,
            matchedStatements,
            matchedInternal,
            (statement, internalTransaction) => SameTransactionScope(statement, internalTransaction)
                && Abs(statement.Quantity - internalTransaction.Quantity) <= tolerance.TransactionQuantity
                && FindTransactionRule(statement, internalTransaction, tolerance) is not null,
            static (statement, internalTransaction) => TransactionVariance(statement, internalTransaction).LargestAbsoluteAmount,
            (statement, internalTransaction) => CreateTransactionResult(
                statement,
                internalTransaction,
                StatementMatchTier.Tolerance,
                ConfidenceFromVariance(TransactionVariance(statement, internalTransaction).LargestAbsoluteAmount,
                    Math.Max(tolerance.TransactionQuantity, Math.Abs(FindTransactionRule(statement, internalTransaction, tolerance)!.AbsoluteCashTolerance))),
                [FindTransactionRule(statement, internalTransaction, tolerance)!.RuleId],
                TransactionVariance(statement, internalTransaction),
                new StatementMatchTolerance(tolerance.TransactionQuantity, Math.Abs(FindTransactionRule(statement, internalTransaction, tolerance)!.AbsoluteCashTolerance)),
                "Transaction matched inside configured quantity and net-amount tolerances."),
            results);

        MatchTransactionSplits(request, matchedStatements, matchedInternal, tolerance, results);

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
                "Transaction candidate shares account, security and currency, type, and nearby dates but requires operator review."),
            results);

        AddUnmatchedTransactions(request.StatementTransactions, request.InternalLedgerTransactions, matchedStatements, matchedInternal, tolerance, results);
    }

    /// <summary>
    /// Runs one pair-matching stage: every admissible pair competes in a total deterministic order
    /// (score, then statement id, then internal id) and the kernel selects a non-overlapping
    /// assignment, so selection depends only on the population contents, never on the order either
    /// side happens to be enumerated in.
    /// </summary>
    private static void MatchStage<TStatement, TInternal>(
        IReadOnlyList<TStatement> statements,
        IReadOnlyList<TInternal> internalItems,
        HashSet<string> matchedStatements,
        HashSet<string> matchedInternal,
        Func<TStatement, TInternal, bool> isMatch,
        Func<TStatement, TInternal, decimal> scorePair,
        Func<TStatement, TInternal, StatementMatchResult> createResult,
        List<StatementMatchResult> results)
        where TStatement : class, IStatementMatchItem
        where TInternal : class, IStatementMatchItem
    {
        var pairs = new List<(TStatement Statement, TInternal Internal, decimal Score)>();
        foreach (var statement in statements)
        {
            if (matchedStatements.Contains(statement.MatchId))
                continue;

            foreach (var internalItem in internalItems)
            {
                if (matchedInternal.Contains(internalItem.MatchId) || !isMatch(statement, internalItem))
                    continue;

                pairs.Add((statement, internalItem, scorePair(statement, internalItem)));
            }
        }

        var ordered = pairs
            .OrderBy(static pair => pair.Score)
            .ThenBy(static pair => pair.Statement.MatchId, StringComparer.Ordinal)
            .ThenBy(static pair => pair.Internal.MatchId, StringComparer.Ordinal)
            .ThenBy(static pair => pair.Statement.EvidenceReference, StringComparer.Ordinal)
            .ThenBy(static pair => pair.Internal.EvidenceReference, StringComparer.Ordinal);
        foreach (var pair in ReconciliationMatchKernel.SelectDeterministicAssignment(
            ordered,
            static pair => new[] { StatementMemberKey(pair.Statement), InternalMemberKey(pair.Internal) }))
        {
            matchedStatements.Add(pair.Statement.MatchId);
            matchedInternal.Add(pair.Internal.MatchId);
            results.Add(createResult(pair.Statement, pair.Internal));
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
        => MatchStage(
            statements,
            internalItems,
            matchedStatements,
            matchedInternal,
            isCandidate,
            scoreCandidate,
            createResult,
            results);

    /// <summary>
    /// Deterministic one-to-many / many-to-one transaction matching over the sided kernel. Anchors
    /// are visited in ordinal id order; each anchor's candidate legs are partitioned by the same
    /// identity constraints the pair stages apply (account, security, currency, trade date and type),
    /// followed by each rule's settlement window BEFORE the kernel's bounded search, so the
    /// <see cref="ReconciliationMatchKernel.MaxSplitSearchCandidates"/> cap lands on legs that could
    /// actually match instead of being exhausted by larger cross-identity amounts. The accept
    /// callback then holds the subset's aggregate quantity to the transaction-quantity tolerance —
    /// legs with the right cash but the wrong share count must stay a break, because a silently
    /// absorbed quantity mismatch surfaces nowhere. Splits discover legs by net cash movement, so a
    /// zero-net-amount anchor and zero-amount legs never split (the kernel searches same-sign
    /// non-zero amounts only); such movements stay in the pair, candidate, and break lanes.
    /// </summary>
    private static void MatchTransactionSplits(
        StatementMatchingRequest request,
        HashSet<string> matchedStatements,
        HashSet<string> matchedInternal,
        StatementMatchingToleranceProfile tolerance,
        List<StatementMatchResult> results)
    {
        // Statement-anchored: one statement movement settled internally as several ledger legs.
        foreach (var statement in request.StatementTransactions
            .Where(item => !matchedStatements.Contains(((IStatementMatchItem)item).MatchId))
            .OrderBy(static item => item.TransactionId, StringComparer.Ordinal))
        {
            var legs = CollectSplitPool(
                request.InternalLedgerTransactions,
                matchedInternal,
                internalTransaction => SameTransactionScope(statement, internalTransaction),
                static internalTransaction => internalTransaction.TransactionId);
            if (!TryMatchSplit(
                statement.NetAmount,
                statement.Quantity,
                legs,
                static leg => leg.TransactionId,
                static leg => leg.NetAmount,
                static leg => leg.Quantity,
                (leg, rule) => DateDelta(statement.SettlementDate, leg.SettlementDate).Duration() <= rule.SettlementDateTolerance.Duration(),
                (subset, rule) => rule.Allows(subset.Sum(leg => leg.NetAmount), statement.NetAmount,
                    subset.Max(leg => DateDelta(statement.SettlementDate, leg.SettlementDate).Duration()),
                    WeightedPrice(subset, leg => leg.Quantity, leg => leg.Price) ?? 0m,
                    ComparablePrice(statement.Price, WeightedPrice(subset, leg => leg.Quantity, leg => leg.Price))),
                tolerance,
                out var selectedLegs,
                out var amountVariance,
                out var quantityVariance,
                out var appliedRule))
            {
                continue;
            }

            matchedStatements.Add(((IStatementMatchItem)statement).MatchId);
            foreach (var leg in selectedLegs)
            {
                matchedInternal.Add(((IStatementMatchItem)leg).MatchId);
            }

            results.Add(new StatementMatchResult(
                StatementMatchKind.Transaction,
                appliedRule is null ? StatementMatchTier.Exact : StatementMatchTier.Tolerance,
                ConfidenceFromVariance(Math.Max(Math.Abs(amountVariance), Math.Abs(quantityVariance)),
                    Math.Max(tolerance.TransactionQuantity, appliedRule is null ? 0m : Math.Abs(appliedRule.AbsoluteCashTolerance))),
                appliedRule is null ? [TransactionSplitRuleId] : [appliedRule.RuleId, TransactionSplitRuleId],
                statement.EvidenceReference,
                null,
                new StatementMatchVariance(Quantity: quantityVariance, Amount: amountVariance),
                new StatementMatchTolerance(tolerance.TransactionQuantity, appliedRule is null ? 0m : Math.Abs(appliedRule.AbsoluteCashTolerance)),
                $"Statement transaction settled as {selectedLegs.Count} internal ledger legs sharing account, security and currency, dates, and type within configured tolerances.")
            {
                InternalEvidenceReferences = selectedLegs.Select(static leg => leg.EvidenceReference).ToArray()
            });
        }

        // Internal-anchored mirror: one internal posting the custodian reports as several rows.
        foreach (var internalTransaction in request.InternalLedgerTransactions
            .Where(item => !matchedInternal.Contains(((IStatementMatchItem)item).MatchId))
            .OrderBy(static item => item.TransactionId, StringComparer.Ordinal))
        {
            var legs = CollectSplitPool(
                request.StatementTransactions,
                matchedStatements,
                statement => SameTransactionScope(statement, internalTransaction),
                static statement => statement.TransactionId);
            if (!TryMatchSplit(
                internalTransaction.NetAmount,
                internalTransaction.Quantity,
                legs,
                static leg => leg.TransactionId,
                static leg => leg.NetAmount,
                static leg => leg.Quantity,
                (leg, rule) => DateDelta(leg.SettlementDate, internalTransaction.SettlementDate).Duration() <= rule.SettlementDateTolerance.Duration(),
                (subset, rule) => rule.Allows(internalTransaction.NetAmount, subset.Sum(leg => leg.NetAmount),
                    subset.Max(leg => DateDelta(leg.SettlementDate, internalTransaction.SettlementDate).Duration()),
                    internalTransaction.Price ?? 0m,
                    ComparablePrice(WeightedPrice(subset, leg => leg.Quantity, leg => leg.Price), internalTransaction.Price)),
                tolerance,
                out var selectedLegs,
                out var amountVariance,
                out var quantityVariance,
                out var appliedRule))
            {
                continue;
            }

            matchedInternal.Add(((IStatementMatchItem)internalTransaction).MatchId);
            foreach (var leg in selectedLegs)
            {
                matchedStatements.Add(((IStatementMatchItem)leg).MatchId);
            }

            // Variance keeps the statement-minus-internal convention of the pair stages.
            results.Add(new StatementMatchResult(
                StatementMatchKind.Transaction,
                appliedRule is null ? StatementMatchTier.Exact : StatementMatchTier.Tolerance,
                ConfidenceFromVariance(Math.Max(Math.Abs(amountVariance), Math.Abs(quantityVariance)),
                    Math.Max(tolerance.TransactionQuantity, appliedRule is null ? 0m : Math.Abs(appliedRule.AbsoluteCashTolerance))),
                appliedRule is null ? [TransactionSplitRuleId] : [appliedRule.RuleId, TransactionSplitRuleId],
                null,
                internalTransaction.EvidenceReference,
                new StatementMatchVariance(Quantity: -quantityVariance, Amount: -amountVariance),
                new StatementMatchTolerance(tolerance.TransactionQuantity, appliedRule is null ? 0m : Math.Abs(appliedRule.AbsoluteCashTolerance)),
                $"Internal ledger transaction reported by the custodian as {selectedLegs.Count} statement rows sharing account, security and currency, dates, and type within configured tolerances.")
            {
                BrokerEvidenceReferences = selectedLegs.Select(static leg => leg.EvidenceReference).ToArray()
            });
        }
    }

    /// <summary>
    /// Collects the identity-partitioned, still-unmatched candidate legs for one split anchor in a
    /// deterministic order. A duplicated raw id keeps only its deterministically-first record,
    /// because the kernel maps legs back by id.
    /// </summary>
    private static IReadOnlyList<TLeg> CollectSplitPool<TLeg>(
        IReadOnlyList<TLeg> population,
        HashSet<string> matchedKeys,
        Func<TLeg, bool> sharesIdentity,
        Func<TLeg, string> legId)
        where TLeg : class, IStatementMatchItem
    {
        var pool = new List<TLeg>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var leg in population
            .Where(item => !matchedKeys.Contains(item.MatchId) && sharesIdentity(item))
            .OrderBy(legId, StringComparer.Ordinal)
            .ThenBy(static item => item.EvidenceReference, StringComparer.Ordinal))
        {
            if (seen.Add(legId(leg)))
            {
                pool.Add(leg);
            }
        }

        return pool;
    }

    /// <summary>
    /// Side-qualified, case-normalized member keys for the kernel's ordinal consumed set. The
    /// qualification keeps a raw id that legitimately appears on both sides — a bank reference
    /// propagated into the ledger — from letting one side's consumption block the other side's
    /// match; upper-casing preserves the engine's case-insensitive id semantics.
    /// </summary>
    private static string StatementMemberKey<T>(T item) where T : class, IStatementMatchItem
        => $"statement:{item.MatchId.ToUpperInvariant()}";

    private static string InternalMemberKey<T>(T item) where T : class, IStatementMatchItem
        => $"internal:{item.MatchId.ToUpperInvariant()}";

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

        // Statement rows surface in retained-import order; internal records arrive in provider
        // enumeration order, which is not retained anywhere, so sort them for a permutation-stable
        // result sequence (break ids derive from the enumeration ordinal downstream).
        foreach (var internalItem in internalItems
            .Where(internalItem => !matchedInternal.Contains(((IStatementMatchItem)internalItem).MatchId))
            .OrderBy(static internalItem => internalItem.PositionId, StringComparer.Ordinal))
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

        foreach (var internalItem in internalItems
            .Where(internalItem => !matchedInternal.Contains(((IStatementMatchItem)internalItem).MatchId))
            .OrderBy(static internalItem => internalItem.CashBalanceId, StringComparer.Ordinal))
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

        foreach (var internalItem in internalItems
            .Where(internalItem => !matchedInternal.Contains(((IStatementMatchItem)internalItem).MatchId))
            .OrderBy(static internalItem => internalItem.TransactionId, StringComparer.Ordinal))
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
        SameCashScope(statement, internalCash) && statement.AsOfDate == internalCash.AsOfDate;

    private static bool SameCashScope(NormalizedStatementCashBalance statement, InternalCashBalance internalCash) =>
        statement.IsForStatementPeriodEnd
        && SameText(statement.Account, internalCash.Account)
        && SameText(statement.Currency, internalCash.Currency);

    private static bool SameTransactionIdentity(NormalizedStatementTransaction statement, InternalLedgerTransaction internalTransaction) =>
        SameTransactionScope(statement, internalTransaction)
        && statement.SettlementDate == internalTransaction.SettlementDate;

    private static bool SameTransactionScope(NormalizedStatementTransaction statement, InternalLedgerTransaction internalTransaction) =>
        SameText(statement.Account, internalTransaction.Account)
        && SameTransactionInstrumentOrCurrency(statement, internalTransaction)
        && statement.TradeDate == internalTransaction.TradeDate
        && SameText(statement.TransactionType, internalTransaction.TransactionType);

    private static bool SameTransactionInstrumentOrCurrency(NormalizedStatementTransaction statement, InternalLedgerTransaction internalTransaction)
    {
        // Security identity never substitutes for denomination. FX normalization can establish a
        // common currency upstream; absent conversion, different currencies remain unmatched.
        return SameNullableText(statement.Currency, internalTransaction.Currency)
            && SameNullableText(statement.SecurityId, internalTransaction.SecurityId);
    }

    private static bool HasSameExternalTransactionId(NormalizedStatementTransaction statement, InternalLedgerTransaction internalTransaction) =>
        !string.IsNullOrWhiteSpace(statement.ExternalTransactionId)
        && SameNullableText(statement.ExternalTransactionId, internalTransaction.ExternalTransactionId);

    private static bool OptionalMarketValueEquals(decimal? statementMarketValue, decimal? internalMarketValue) =>
        statementMarketValue is null || internalMarketValue is null || statementMarketValue == internalMarketValue;

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
    int CandidateDateWindowDays = 2)
{
    /// <summary>The retained full profile. List order is precedence: the first admissible rule wins.</summary>
    public StatementToleranceProfile? Rules { get; init; }
}

public sealed record NormalizedStatementPosition(
    string PositionId,
    string Account,
    string SecurityId,
    DateOnly AsOfDate,
    decimal Quantity,
    decimal? MarketValue,
    string EvidenceReference) : IStatementMatchItem
{
    /// <summary>Retained unit price, when supplied by the source; never inferred from net cash.</summary>
    public decimal? Price { get; init; }

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
    /// <summary>Retained execution price, when supplied by the source; never inferred from net cash.</summary>
    public decimal? Price { get; init; }

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
    /// <summary>Retained unit price, when supplied by the source; never inferred from net cash.</summary>
    public decimal? Price { get; init; }

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
    /// <summary>Retained execution price, when supplied by the source; never inferred from net cash.</summary>
    public decimal? Price { get; init; }

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
    string Explanation)
{
    /// <summary>
    /// Every broker-side member of a many-to-one split group. Null for pair and unmatched results,
    /// whose only broker member is <see cref="BrokerEvidenceReference"/>.
    /// </summary>
    public IReadOnlyList<string>? BrokerEvidenceReferences { get; init; }

    /// <summary>
    /// Every internal-side member of a one-to-many split group. Null for pair and unmatched
    /// results, whose only internal member is <see cref="InternalEvidenceReference"/>.
    /// </summary>
    public IReadOnlyList<string>? InternalEvidenceReferences { get; init; }

    /// <summary>All broker-side evidence members, whether the result is a pair or a split group.</summary>
    public IReadOnlyList<string> AllBrokerEvidenceReferences
        => BrokerEvidenceReferences ?? (BrokerEvidenceReference is null ? [] : [BrokerEvidenceReference]);

    /// <summary>All internal-side evidence members, whether the result is a pair or a split group.</summary>
    public IReadOnlyList<string> AllInternalEvidenceReferences
        => InternalEvidenceReferences ?? (InternalEvidenceReference is null ? [] : [InternalEvidenceReference]);
}

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

    /// <summary>
    /// The durable evidence reference the item contributes to a match or break. Also the final
    /// pair-ordering tie-breaker: match ids are expected to be unique per side, but a population
    /// that repeats one must still order totally, or the assignment would fall back to input
    /// enumeration order.
    /// </summary>
    string EvidenceReference { get; }
}
