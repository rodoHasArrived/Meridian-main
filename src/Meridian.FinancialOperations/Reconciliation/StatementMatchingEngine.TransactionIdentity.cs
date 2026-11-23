namespace Meridian.FinancialOperations.Reconciliation;

public sealed partial class StatementMatchingEngine
{
    private const string AmbiguousTransactionBreakCode = "TRANSACTION_AMBIGUOUS";
    private const string OutsideWindowBreakCode = "TRANSACTION_OUTSIDE_STATEMENT_WINDOW";

    private static void RecordTransactionsOutsideStatementWindow(
        StatementMatchingRequest request,
        HashSet<string> matchedStatements,
        HashSet<string> matchedInternal,
        List<StatementMatchResult> results)
    {
        foreach (var statement in request.StatementTransactions.Where(trade => IsOutside(trade.TradeDate))
                     .OrderBy(static trade => trade.TransactionId, StringComparer.Ordinal))
        {
            matchedStatements.Add(statement.TransactionId);
            results.Add(OutsideWindow(statement, null));
        }

        foreach (var transaction in request.InternalLedgerTransactions.Where(trade => IsOutside(trade.TradeDate))
                     .OrderBy(static trade => trade.TransactionId, StringComparer.Ordinal))
        {
            matchedInternal.Add(transaction.TransactionId);
            results.Add(OutsideWindow(null, transaction));
        }

        bool IsOutside(DateOnly date) =>
            (request.StatementPeriodStart is { } start && date < start)
            || (request.StatementPeriodEnd is { } end && date > end);

        StatementMatchResult OutsideWindow(NormalizedStatementTransaction? statement, InternalLedgerTransaction? transaction) => new(
            StatementMatchKind.Transaction, StatementMatchTier.Unmatched, 0m, [TransactionBreakRuleId],
            statement?.EvidenceReference, transaction?.EvidenceReference, new StatementMatchVariance(), TransactionTolerance(request.ToleranceProfile),
            "The transaction trade date lies outside the authorized statement window.")
        {
            BreakCode = OutsideWindowBreakCode,
            BrokerRecords = statement is null ? [] : [statement],
            InternalRecords = transaction is null ? [] : [transaction]
        };
    }

    private static void MatchExternalTransactionIds(
        StatementMatchingRequest request,
        HashSet<string> matchedStatements,
        HashSet<string> matchedInternal,
        List<StatementMatchResult> results)
    {
        var statements = request.StatementTransactions
            .Where(trade => !matchedStatements.Contains(trade.TransactionId)
                && !string.IsNullOrWhiteSpace(trade.ExternalTransactionId))
            .ToLookup(static trade => Identity(trade.Account, trade.ExternalTransactionId));
        var internals = request.InternalLedgerTransactions
            .Where(trade => !matchedInternal.Contains(trade.TransactionId)
                && !string.IsNullOrWhiteSpace(trade.ExternalTransactionId))
            .ToLookup(static trade => Identity(trade.Account, trade.ExternalTransactionId));

        foreach (var identity in statements.Select(static group => group.Key)
                     .Concat(internals.Select(static group => group.Key))
                     .Distinct().OrderBy(static key => key.Account, StringComparer.Ordinal)
                     .ThenBy(static key => key.Id, StringComparer.Ordinal))
        {
            var external = statements[identity].ToArray();
            var retained = internals[identity].ToArray();
            if (external.Length > 1 || retained.Length > 1)
            {
                matchedStatements.UnionWith(external.Select(static row => row.TransactionId));
                matchedInternal.UnionWith(retained.Select(static row => row.TransactionId));
                results.Add(AmbiguousTransaction(external, retained,
                    TransactionExternalIdRuleId, "An external transaction ID occurs more than once on one side of this account."));
            }
            else if (external.Length == 1 && retained.Length == 1)
            {
                var statement = external[0];
                var transaction = retained[0];
                matchedStatements.Add(statement.TransactionId);
                matchedInternal.Add(transaction.TransactionId);
                results.Add(CompareExternalTransaction(statement, transaction, request.ToleranceProfile));
            }
        }

        static (string Account, string Id) Identity(string account, string? id) =>
            (IdentityPart(account), IdentityPart(id));
    }

    private static StatementMatchResult CompareExternalTransaction(
        NormalizedStatementTransaction statement,
        InternalLedgerTransaction transaction,
        StatementMatchingToleranceProfile tolerance)
    {
        var sameCurrency = SameNullableText(statement.Currency, transaction.Currency);
        var sameTrade = SameNullableText(statement.SecurityId, transaction.SecurityId)
            && SameText(statement.TransactionType, transaction.TransactionType);
        var sameDates = statement.TradeDate == transaction.TradeDate
            && statement.SettlementDate == transaction.SettlementDate;
        var variance = new StatementMatchVariance(
            Quantity: sameTrade ? statement.Quantity - transaction.Quantity : null,
            Amount: sameCurrency ? statement.NetAmount - transaction.NetAmount : null);
        var sameEconomics = variance.Quantity == 0m && variance.Amount == 0m
            && (tolerance.Rules is null || OptionalPriceEquals(statement.Price, transaction.Price));

        if (sameCurrency && sameTrade && (!sameDates || !sameEconomics))
        {
            if (tolerance.Rules is { } rules && statement.TradeDate == transaction.TradeDate && variance.Quantity == 0m)
            {
                var rule = rules.TransactionRules.FirstOrDefault(candidate => AllowsTransaction(candidate,
                    transaction.NetAmount, statement.NetAmount, DateDelta(statement.SettlementDate, transaction.SettlementDate),
                    transaction.Price, statement.Price));
                if (rule is not null)
                    return CreateConfiguredTransactionResult(statement, transaction, rule);
            }
            else if (tolerance.Rules is null && sameDates
                && Abs(statement.Quantity - transaction.Quantity) <= tolerance.TransactionQuantity
                && Abs(statement.NetAmount - transaction.NetAmount) <= tolerance.TransactionNetAmount)
            {
                return CreateTransactionResult(statement, transaction, StatementMatchTier.Tolerance,
                    ConfidenceFromVariance(variance.LargestAbsoluteAmount, Math.Max(tolerance.TransactionQuantity, tolerance.TransactionNetAmount)),
                    [TransactionToleranceRuleId], variance, TransactionTolerance(tolerance),
                    "Transaction matched by external ID inside configured quantity and net-amount tolerances.");
            }
        }

        var (code, explanation) = (sameCurrency && sameTrade, sameDates, sameEconomics) switch
        {
            (false, _, _) => ("TRANSACTION_IDENTITY_CONFLICT", "The same external ID names different instruments, currencies, or transaction types."),
            (true, false, false) => ("TRANSACTION_AMOUNT_AND_TIMING_DIFFERENCE", "The same external ID has different economics and trade or settlement dates."),
            (true, false, true) => ("TRANSACTION_TIMING_DIFFERENCE", "The same external ID has different trade or settlement dates."),
            (true, true, false) => ("TRANSACTION_AMOUNT_DIFFERENCE", "The same external ID has different quantity, amount, or execution price."),
            _ => ((string?)null, "Exact transaction match on external transaction ID and transaction details.")
        };

        return CreateTransactionResult(statement, transaction,
            code is null ? StatementMatchTier.Exact : StatementMatchTier.Candidate,
            code is null ? 1.00m : 0.50m, [TransactionExternalIdRuleId], variance,
            TransactionTolerance(tolerance), explanation) with
        { BreakCode = code };
    }

    // Group eligible pairs before assigning any of them. A tie is evidence for an operator, and
    // claiming every member of a connected ambiguity keeps later rules from hiding that break.
    private static void MatchUniqueTransactionPairs(
        StatementMatchingRequest request,
        HashSet<string> matchedStatements,
        HashSet<string> matchedInternal,
        Func<NormalizedStatementTransaction, InternalLedgerTransaction, bool> qualifies,
        Func<NormalizedStatementTransaction, InternalLedgerTransaction, StatementMatchResult> createResult,
        List<StatementMatchResult> results)
    {
        var internalByIdentity = request.InternalLedgerTransactions
            .Where(trade => !matchedInternal.Contains(trade.TransactionId))
            .ToLookup(static trade => FallbackIdentity(trade.Account, trade.SecurityId, trade.Currency, trade.TransactionType));
        var candidates = request.StatementTransactions
            .Where(trade => !matchedStatements.Contains(trade.TransactionId))
            .OrderBy(static trade => trade.TransactionId, StringComparer.Ordinal)
            .SelectMany(statement => internalByIdentity[FallbackIdentity(statement.Account, statement.SecurityId, statement.Currency, statement.TransactionType)]
                .Where(internalTrade => CanUseTransactionFallback(statement, internalTrade) && qualifies(statement, internalTrade))
                .Select(internalTrade => (Statement: statement, Internal: internalTrade)))
            .ToArray();
        var byStatement = candidates.ToLookup(static pair => pair.Statement.TransactionId, StringComparer.OrdinalIgnoreCase);
        var byInternal = candidates.ToLookup(static pair => pair.Internal.TransactionId, StringComparer.OrdinalIgnoreCase);

        foreach (var pair in candidates)
        {
            if (matchedStatements.Contains(pair.Statement.TransactionId))
                continue;

            if (byStatement[pair.Statement.TransactionId].Count() == 1 && byInternal[pair.Internal.TransactionId].Count() == 1)
            {
                matchedStatements.Add(pair.Statement.TransactionId);
                matchedInternal.Add(pair.Internal.TransactionId);
                results.Add(createResult(pair.Statement, pair.Internal));
                continue;
            }

            var pending = new Queue<NormalizedStatementTransaction>();
            var statementGroup = new List<NormalizedStatementTransaction>();
            var internalGroup = new List<InternalLedgerTransaction>();
            AddStatement(pair.Statement);
            while (pending.TryDequeue(out var statement))
            {
                foreach (var candidate in byStatement[statement.TransactionId])
                {
                    if (!matchedInternal.Add(candidate.Internal.TransactionId))
                        continue;

                    internalGroup.Add(candidate.Internal);
                    foreach (var neighbour in byInternal[candidate.Internal.TransactionId])
                        AddStatement(neighbour.Statement);
                }
            }

            results.Add(AmbiguousTransaction(statementGroup, internalGroup,
                TransactionCandidateRuleId, "Several transactions satisfy the same fallback rule. Review the retained candidates."));

            void AddStatement(NormalizedStatementTransaction statement)
            {
                if (matchedStatements.Add(statement.TransactionId))
                {
                    statementGroup.Add(statement);
                    pending.Enqueue(statement);
                }
            }
        }
    }

    private static StatementMatchResult AmbiguousTransaction(
        IReadOnlyList<NormalizedStatementTransaction> statements,
        IReadOnlyList<InternalLedgerTransaction> internals,
        string ruleId,
        string explanation)
    {
        var brokerEvidence = statements.Select(static trade => trade.EvidenceReference)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var internalEvidence = internals.Select(static trade => trade.EvidenceReference)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        return new StatementMatchResult(StatementMatchKind.Transaction, StatementMatchTier.Candidate,
            0m, [ruleId], brokerEvidence.FirstOrDefault(), internalEvidence.FirstOrDefault(),
            new StatementMatchVariance(), new StatementMatchTolerance(), explanation)
        {
            BreakCode = AmbiguousTransactionBreakCode,
            BrokerEvidenceReferences = brokerEvidence,
            InternalEvidenceReferences = internalEvidence,
            BrokerRecords = statements,
            InternalRecords = internals
        };
    }

    private sealed record TransactionSplit(
        IReadOnlyList<NormalizedStatementTransaction> Statements,
        IReadOnlyList<InternalLedgerTransaction> Internals,
        StatementMatchResult? Result);

    private static void AssignTransactionSplits(
        List<TransactionSplit> candidates,
        HashSet<string> matchedStatements,
        HashSet<string> matchedInternal,
        List<StatementMatchResult> results)
    {
        var byStatement = candidates.SelectMany((split, index) => split.Statements.Select(row => (row.TransactionId, Index: index)))
            .ToLookup(static member => member.TransactionId, static member => member.Index, StringComparer.OrdinalIgnoreCase);
        var byInternal = candidates.SelectMany((split, index) => split.Internals.Select(row => (row.TransactionId, Index: index)))
            .ToLookup(static member => member.TransactionId, static member => member.Index, StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<int>();
        for (var index = 0; index < candidates.Count; index++)
        {
            if (!visited.Add(index))
                continue;

            var pending = new Queue<int>();
            var statements = new List<NormalizedStatementTransaction>();
            var internals = new List<InternalLedgerTransaction>();
            var count = 0;
            pending.Enqueue(index);
            while (pending.TryDequeue(out var candidateIndex))
            {
                count++;
                var candidate = candidates[candidateIndex];
                foreach (var row in candidate.Statements)
                {
                    if (matchedStatements.Add(row.TransactionId))
                    {
                        statements.Add(row);
                        EnqueueNeighbours(byStatement[row.TransactionId]);
                    }
                }

                foreach (var row in candidate.Internals)
                {
                    if (matchedInternal.Add(row.TransactionId))
                    {
                        internals.Add(row);
                        EnqueueNeighbours(byInternal[row.TransactionId]);
                    }
                }
            }

            results.Add(count == 1 && candidates[index].Result is { } result
                ? result
                : AmbiguousTransaction(statements, internals,
                    TransactionSplitRuleId, "The bounded split search cannot establish a unique settlement group."));

            void EnqueueNeighbours(IEnumerable<int> indexes)
            {
                foreach (var neighbour in indexes)
                {
                    if (visited.Add(neighbour))
                        pending.Enqueue(neighbour);
                }
            }
        }
    }

    private static (string Account, string Security, string Currency, string Type) FallbackIdentity(
        string account, string? security, string? currency, string type) =>
        (IdentityPart(account), IdentityPart(security), IdentityPart(currency), IdentityPart(type));

    private static string IdentityPart(string? value) => value?.Trim().ToUpperInvariant() ?? string.Empty;

    private static bool CanUseTransactionFallback(NormalizedStatementTransaction statement, InternalLedgerTransaction transaction) =>
        string.IsNullOrWhiteSpace(statement.ExternalTransactionId) || string.IsNullOrWhiteSpace(transaction.ExternalTransactionId);
}
