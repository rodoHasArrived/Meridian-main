using System.Globalization;
using Meridian.Domain.Reconciliation;

namespace Meridian.FinancialOperations.Reconciliation;

/// <summary>
/// Two-sided matcher for the live statement-run workflow. Normalizes imported broker/custodian
/// statement rows, runs them through the shared staged <see cref="StatementMatchingEngine"/>
/// against Meridian's internal book, and projects the engine's exact/tolerance/candidate/unmatched
/// results into reconciliation break records and per-row match outcomes.
/// </summary>
/// <remarks>
/// This replaces the retired single-sided self-check: a row is only "matched" when the engine can
/// pair it with an internal position, cash balance, or ledger transaction inside the configured
/// tolerances. Rows with no internal counterpart — and internal records with no statement
/// counterpart — become genuine breaks. <c>ToleranceBreached</c> is computed from the actual
/// variance rather than hardcoded.
/// </remarks>
public static class StatementRunMatchingService
{
    public static StatementRunMatchResult Match(
        CanonicalStatementImport import,
        IReadOnlyList<CanonicalStatementRow> rows,
        InternalReconciliationBook internalBook,
        StatementToleranceProfile toleranceProfile)
    {
        ArgumentNullException.ThrowIfNull(import);
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(internalBook);
        ArgumentNullException.ThrowIfNull(toleranceProfile);

        var importId = import.ImportId;
        var statementPositions = new List<NormalizedStatementPosition>();
        var statementCash = new List<NormalizedStatementCashBalance>();
        var statementTransactions = new List<NormalizedStatementTransaction>();
        var rowByReference = new Dictionary<string, CanonicalStatementRow>(StringComparer.Ordinal);

        foreach (var row in rows)
        {
            var reference = BuildRowReference(importId, row.SourceRowNumber);
            rowByReference[reference] = row;
            switch (ClassifyRow(row.ActivityType))
            {
                case StatementItemKind.Position:
                    statementPositions.Add(new NormalizedStatementPosition(
                        reference,
                        row.Account,
                        row.Symbol,
                        row.TradeDate,
                        row.Quantity,
                        PositionMarketValue(row),
                        reference));
                    break;
                case StatementItemKind.Cash:
                    statementCash.Add(new NormalizedStatementCashBalance(
                        reference,
                        row.Account,
                        UnknownCurrency,
                        row.CashAmount,
                        reference));
                    break;
                default:
                    statementTransactions.Add(new NormalizedStatementTransaction(
                        reference,
                        ExternalTransactionId: null,
                        row.Account,
                        NormalizeSecurityId(row.Symbol),
                        Currency: null,
                        row.TradeDate,
                        row.TradeDate,
                        row.ActivityType,
                        row.Quantity,
                        row.CashAmount,
                        reference));
                    break;
            }
        }

        var engineTolerance = ToEngineTolerance(toleranceProfile);
        var result = new StatementMatchingEngine().Run(new StatementMatchingRequest(
            statementPositions,
            statementCash,
            statementTransactions,
            internalBook.Positions,
            internalBook.CashBalances,
            internalBook.Transactions,
            engineTolerance));

        var resultsByBrokerReference = new Dictionary<string, StatementMatchResult>(StringComparer.Ordinal);
        var internalOnlyResults = new List<StatementMatchResult>();
        foreach (var match in result.Results)
        {
            if (match.BrokerEvidenceReference is { Length: > 0 } brokerReference)
            {
                resultsByBrokerReference[brokerReference] = match;
            }
            else if (match.InternalEvidenceReference is { Length: > 0 })
            {
                internalOnlyResults.Add(match);
            }
        }

        var now = DateTimeOffset.UtcNow;
        var breaks = new List<ReconciliationBreakRecord>();
        var outcomes = new List<MatchOutcome>(rows.Count);

        foreach (var row in rows)
        {
            var reference = BuildRowReference(importId, row.SourceRowNumber);
            if (!resultsByBrokerReference.TryGetValue(reference, out var match))
            {
                // Defensive: every normalized statement row yields exactly one engine result, so a
                // missing entry means the row could not be classified. Treat it as an open break.
                outcomes.Add(new MatchOutcome(row.RawChecksum, "STATEMENT_ROW_UNCLASSIFIED", string.Empty, 0m,
                    "Statement row could not be normalized for matching and requires operator review.")
                {
                    ToleranceProfileId = toleranceProfile.ProfileId,
                    ToleranceProfileVersion = toleranceProfile.Version,
                });
                breaks.Add(BuildBreak(importId, reference, "STATEMENT_ROW_UNCLASSIFIED", row.ActivityType, 0m, 0m, now));
                continue;
            }

            var matched = match.MatchTier is StatementMatchTier.Exact or StatementMatchTier.Tolerance;
            if (matched)
            {
                outcomes.Add(new MatchOutcome(
                    row.RawChecksum,
                    "matched",
                    match.InternalEvidenceReference ?? string.Empty,
                    match.Confidence,
                    match.Explanation)
                {
                    ToleranceProfileId = toleranceProfile.ProfileId,
                    ToleranceProfileVersion = toleranceProfile.Version,
                    ToleranceRuleId = match.RuleIds.FirstOrDefault(),
                });
                continue;
            }

            var breakCode = StatementBreakCode(match.Kind, match.MatchTier);
            var toleranceAmount = ToleranceForKind(match.Kind, engineTolerance);
            var delta = match.Variance.LargestAbsoluteAmount;
            outcomes.Add(new MatchOutcome(
                row.RawChecksum,
                breakCode,
                string.Empty,
                match.Confidence,
                match.Explanation)
            {
                ToleranceProfileId = toleranceProfile.ProfileId,
                ToleranceProfileVersion = toleranceProfile.Version,
            });
            breaks.Add(BuildBreak(importId, reference, breakCode, KindCategory(match.Kind), delta, toleranceAmount, now));
        }

        foreach (var match in internalOnlyResults)
        {
            var breakCode = InternalMissingBreakCode(match.Kind);
            var toleranceAmount = ToleranceForKind(match.Kind, engineTolerance);
            breaks.Add(BuildBreak(
                importId,
                match.InternalEvidenceReference!,
                breakCode,
                KindCategory(match.Kind),
                match.Variance.LargestAbsoluteAmount,
                toleranceAmount,
                now));
        }

        return new StatementRunMatchResult(breaks, outcomes);
    }

    private const string UnknownCurrency = "";

    private static string BuildRowReference(string importId, int sourceRowNumber)
        => $"{importId}:{sourceRowNumber.ToString(CultureInfo.InvariantCulture)}";

    private static StatementItemKind ClassifyRow(string activityType)
    {
        if (activityType.Equals("position", StringComparison.OrdinalIgnoreCase))
        {
            return StatementItemKind.Position;
        }

        if (activityType.Equals("cash", StringComparison.OrdinalIgnoreCase)
            || activityType.Equals("cashbalance", StringComparison.OrdinalIgnoreCase))
        {
            return StatementItemKind.Cash;
        }

        return StatementItemKind.Transaction;
    }

    private static decimal PositionMarketValue(CanonicalStatementRow row)
        => row.Price != 0m ? row.Quantity * row.Price : row.CashAmount;

    private static string? NormalizeSecurityId(string symbol)
        => string.IsNullOrWhiteSpace(symbol) ? null : symbol.Trim();

    private static StatementMatchingToleranceProfile ToEngineTolerance(StatementToleranceProfile profile)
    {
        var positionRule = profile.PositionRules.FirstOrDefault();
        var cashRule = profile.CashRules.FirstOrDefault();
        var transactionRule = profile.TransactionRules.FirstOrDefault();
        return new StatementMatchingToleranceProfile(
            PositionQuantity: positionRule?.QuantityTolerance ?? 0m,
            PositionMarketValue: positionRule?.MarketValueTolerance ?? 0m,
            CashBalance: cashRule?.AbsoluteCashTolerance ?? 0m,
            TransactionQuantity: 0m,
            TransactionNetAmount: transactionRule?.AbsoluteCashTolerance ?? 0m);
    }

    private static decimal ToleranceForKind(StatementMatchKind kind, StatementMatchingToleranceProfile tolerance)
        => kind switch
        {
            StatementMatchKind.Position => Math.Max(tolerance.PositionQuantity, tolerance.PositionMarketValue),
            StatementMatchKind.Cash => tolerance.CashBalance,
            _ => Math.Max(tolerance.TransactionQuantity, tolerance.TransactionNetAmount),
        };

    private static ReconciliationBreakRecord BuildBreak(
        string importId,
        string sourceReference,
        string breakCode,
        string category,
        decimal delta,
        decimal tolerance,
        DateTimeOffset createdAt)
        => new(
            BreakId: Guid.NewGuid().ToString("N"),
            RunId: importId,
            ImportId: importId,
            SourceReference: sourceReference,
            BreakCode: breakCode,
            Category: category,
            Delta: Math.Abs(delta),
            Tolerance: tolerance,
            ToleranceBreached: Math.Abs(delta) > tolerance,
            CreatedAtUtc: createdAt,
            Status: "Open");

    private static string StatementBreakCode(StatementMatchKind kind, StatementMatchTier tier)
    {
        var kindToken = KindToken(kind);
        return tier == StatementMatchTier.Candidate
            ? $"{kindToken}_CANDIDATE_REVIEW"
            : $"{kindToken}_UNMATCHED";
    }

    private static string InternalMissingBreakCode(StatementMatchKind kind)
        => $"{KindToken(kind)}_MISSING_ON_STATEMENT";

    private static string KindCategory(StatementMatchKind kind) => kind.ToString();

    private static string KindToken(StatementMatchKind kind) => kind switch
    {
        StatementMatchKind.Position => "POSITION",
        StatementMatchKind.Cash => "CASH",
        _ => "TRANSACTION",
    };

    private enum StatementItemKind
    {
        Position,
        Cash,
        Transaction,
    }
}

/// <summary>
/// Output of <see cref="StatementRunMatchingService.Match"/>: reconciliation breaks (one per
/// unmatched or candidate statement row and per internal record missing from the statement) plus
/// a per-statement-row match outcome carrying confidence and rationale for casework.
/// </summary>
public sealed record StatementRunMatchResult(
    IReadOnlyList<ReconciliationBreakRecord> Breaks,
    IReadOnlyList<MatchOutcome> Outcomes);
