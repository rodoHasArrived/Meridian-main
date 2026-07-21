using Meridian.Domain.Reconciliation;

namespace Meridian.FinancialOperations.Reconciliation;

/// <summary>
/// Bridges an imported statement to <see cref="StatementMatchingEngine"/>: it maps canonical
/// statement rows into the engine's position/cash/transaction shapes (FX-normalized to the run's
/// base currency), runs the engine against the supplied internal book, and turns every non-matched
/// engine result into a <see cref="ReconciliationBreakRecord"/>. This is what wires the real
/// two-sided matcher into the live statement-run workflow, replacing the previous self-referential
/// row matcher that could never compare a statement against Meridian's own records.
/// </summary>
internal static class StatementRunMatcher
{
    public const string DefaultBaseCurrency = "USD";

    public static StatementRunMatchResult Match(
        CanonicalStatementImport import,
        IReadOnlyList<CanonicalStatementRow> rows,
        InternalReconciliationPopulations populations,
        StatementToleranceProfile toleranceProfile,
        IReconciliationFxRateProvider fxRateProvider,
        string baseCurrency,
        DateTimeOffset createdAtUtc)
    {
        ArgumentNullException.ThrowIfNull(import);
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(populations);
        ArgumentNullException.ThrowIfNull(toleranceProfile);
        ArgumentNullException.ThrowIfNull(fxRateProvider);

        var normalizedBase = string.IsNullOrWhiteSpace(baseCurrency)
            ? DefaultBaseCurrency
            : baseCurrency.Trim().ToUpperInvariant();

        var statementPositions = new List<NormalizedStatementPosition>();
        var statementCash = new List<NormalizedStatementCashBalance>();
        var statementTransactions = new List<NormalizedStatementTransaction>();
        var rowByEvidence = new Dictionary<string, CanonicalStatementRow>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in rows)
        {
            var evidence = $"{import.ImportId}:{row.SourceRowNumber}";
            rowByEvidence[evidence] = row;
            switch (Classify(row.ActivityType))
            {
                case StatementRowKind.Position:
                    statementPositions.Add(MapPosition(row, evidence, fxRateProvider, normalizedBase));
                    break;
                case StatementRowKind.CashBalance:
                    statementCash.Add(MapCash(row, evidence, fxRateProvider, normalizedBase));
                    break;
                default:
                    statementTransactions.Add(MapTransaction(row, evidence, fxRateProvider, normalizedBase));
                    break;
            }
        }

        var asOf = import.StatementPeriodEnd == default ? import.StatementDate : import.StatementPeriodEnd;
        var internalCash = populations.CashBalances
            .Select(cash => NormalizeInternalCash(cash, fxRateProvider, normalizedBase, asOf))
            .ToArray();
        var internalTransactions = populations.LedgerTransactions
            .Select(transaction => NormalizeInternalTransaction(transaction, fxRateProvider, normalizedBase))
            .ToArray();

        var engineResult = new StatementMatchingEngine().Run(new StatementMatchingRequest(
            statementPositions,
            statementCash,
            statementTransactions,
            populations.Positions,
            internalCash,
            internalTransactions,
            ToEngineTolerance(toleranceProfile)));

        var breaks = new List<StatementRunBreak>();
        var matchCount = 0;
        foreach (var result in engineResult.Results)
        {
            if (result.MatchTier is StatementMatchTier.Exact or StatementMatchTier.Tolerance)
            {
                matchCount++;
                continue;
            }

            CanonicalStatementRow? statementRow = null;
            if (result.BrokerEvidenceReference is { } brokerEvidence)
            {
                rowByEvidence.TryGetValue(brokerEvidence, out statementRow);
            }

            var sourceReference = result.BrokerEvidenceReference
                ?? result.InternalEvidenceReference
                ?? $"{import.ImportId}:unmatched";
            var toleranceBreached = result.MatchTier == StatementMatchTier.Unmatched;

            var record = new ReconciliationBreakRecord(
                BreakId: Guid.NewGuid().ToString("N"),
                RunId: import.ImportId,
                ImportId: import.ImportId,
                SourceReference: sourceReference,
                BreakCode: BuildBreakCode(result),
                Category: statementRow?.ActivityType ?? result.Kind.ToString().ToLowerInvariant(),
                Delta: result.Variance.LargestAbsoluteAmount,
                Tolerance: ResolveToleranceAmount(result.Tolerance),
                ToleranceBreached: toleranceBreached,
                CreatedAtUtc: createdAtUtc,
                Status: "Open");

            breaks.Add(new StatementRunBreak(record, result, statementRow));
        }

        return new StatementRunMatchResult(breaks, matchCount);
    }

    private static NormalizedStatementPosition MapPosition(
        CanonicalStatementRow row,
        string evidence,
        IReconciliationFxRateProvider fxRateProvider,
        string baseCurrency)
    {
        var marketValueSource = row.Price * row.Quantity;
        if (marketValueSource == 0m && row.CashAmount != 0m)
        {
            marketValueSource = row.CashAmount;
        }

        // Market value can only be compared in the base currency. When the row is foreign and no FX
        // rate is available, drop the market value to null so the engine matches on quantity alone
        // rather than comparing incomparable-currency amounts.
        decimal? marketValue = fxRateProvider.TryConvert(marketValueSource, row.Currency, baseCurrency, row.TradeDate, out var convertedMarketValue)
            ? convertedMarketValue
            : null;

        return new NormalizedStatementPosition(
            evidence,
            row.Account,
            row.Symbol,
            row.TradeDate,
            row.Quantity,
            marketValue,
            evidence);
    }

    private static NormalizedStatementCashBalance MapCash(
        CanonicalStatementRow row,
        string evidence,
        IReconciliationFxRateProvider fxRateProvider,
        string baseCurrency)
    {
        var (currency, amount) = ToBaseCurrency(row.CashAmount, row.Currency, baseCurrency, row.TradeDate, fxRateProvider);
        return new NormalizedStatementCashBalance(evidence, row.Account, currency, amount, evidence);
    }

    private static NormalizedStatementTransaction MapTransaction(
        CanonicalStatementRow row,
        string evidence,
        IReconciliationFxRateProvider fxRateProvider,
        string baseCurrency)
    {
        var netAmountSource = row.CashAmount != 0m ? row.CashAmount : row.Price * row.Quantity;
        var (currency, amount) = ToBaseCurrency(netAmountSource, row.Currency, baseCurrency, row.TradeDate, fxRateProvider);
        return new NormalizedStatementTransaction(
            evidence,
            string.IsNullOrWhiteSpace(row.ExternalTransactionId) ? null : row.ExternalTransactionId,
            row.Account,
            string.IsNullOrWhiteSpace(row.Symbol) ? null : row.Symbol,
            currency,
            row.TradeDate,
            row.SettlementDate ?? row.TradeDate,
            row.ActivityType,
            row.Quantity,
            amount,
            evidence);
    }

    private static InternalCashBalance NormalizeInternalCash(
        InternalCashBalance cash,
        IReconciliationFxRateProvider fxRateProvider,
        string baseCurrency,
        DateOnly asOf)
    {
        var (currency, amount) = ToBaseCurrency(cash.Balance, cash.Currency, baseCurrency, asOf, fxRateProvider);
        return cash with { Currency = currency, Balance = amount };
    }

    private static InternalLedgerTransaction NormalizeInternalTransaction(
        InternalLedgerTransaction transaction,
        IReconciliationFxRateProvider fxRateProvider,
        string baseCurrency)
    {
        var (currency, amount) = ToBaseCurrency(transaction.NetAmount, transaction.Currency, baseCurrency, transaction.TradeDate, fxRateProvider);
        return transaction with { Currency = currency, NetAmount = amount };
    }

    /// <summary>
    /// Converts <paramref name="amount"/> to <paramref name="baseCurrency"/>. On success the line is
    /// re-denominated in the base currency; when no rate is available it stays in its original
    /// currency so the engine's currency-identity check surfaces it as a break instead of matching
    /// across incompatible currencies.
    /// </summary>
    private static (string Currency, decimal Amount) ToBaseCurrency(
        decimal amount,
        string? currency,
        string baseCurrency,
        DateOnly asOf,
        IReconciliationFxRateProvider fxRateProvider)
    {
        var from = string.IsNullOrWhiteSpace(currency) ? baseCurrency : currency.Trim().ToUpperInvariant();
        return fxRateProvider.TryConvert(amount, from, baseCurrency, asOf, out var converted)
            ? (baseCurrency, converted)
            : (from, amount);
    }

    private static StatementMatchingToleranceProfile ToEngineTolerance(StatementToleranceProfile profile)
    {
        var position = profile.PositionRules.Count > 0 ? profile.PositionRules[0] : null;
        var cash = profile.CashRules.Count > 0 ? profile.CashRules[0] : null;
        var transaction = profile.TransactionRules.Count > 0 ? profile.TransactionRules[0] : null;
        return new StatementMatchingToleranceProfile(
            PositionQuantity: position?.QuantityTolerance ?? 0m,
            PositionMarketValue: position?.MarketValueTolerance ?? 0m,
            CashBalance: cash?.AbsoluteCashTolerance ?? 0m,
            TransactionQuantity: 0m,
            TransactionNetAmount: transaction?.AbsoluteCashTolerance ?? 0m);
    }

    private static decimal ResolveToleranceAmount(StatementMatchTolerance tolerance) =>
        Math.Max(tolerance.Quantity ?? 0m, tolerance.Amount ?? 0m);

    private static string BuildBreakCode(StatementMatchResult result)
    {
        var tier = result.MatchTier == StatementMatchTier.Candidate ? "CANDIDATE" : "UNMATCHED";
        return $"{result.Kind.ToString().ToUpperInvariant()}_{tier}";
    }

    private static StatementRowKind Classify(string activityType)
    {
        if (activityType.Equals("position", StringComparison.OrdinalIgnoreCase))
        {
            return StatementRowKind.Position;
        }

        if (activityType.Equals("cash", StringComparison.OrdinalIgnoreCase)
            || activityType.Equals("cashbalance", StringComparison.OrdinalIgnoreCase))
        {
            return StatementRowKind.CashBalance;
        }

        // Trades, fees, dividends, and any other activity reconcile against ledger transactions.
        return StatementRowKind.Transaction;
    }
}

/// <summary>The outcome of a statement run's match pass: the breaks to persist and the match count.</summary>
internal sealed record StatementRunMatchResult(IReadOnlyList<StatementRunBreak> Breaks, int MatchCount);

/// <summary>
/// A single break produced by the match pass, carrying the persisted record, the originating engine
/// result (confidence, tier, rule ids, explanation), and the statement row when the break is on the
/// broker side (null for internal-only breaks).
/// </summary>
internal sealed record StatementRunBreak(
    ReconciliationBreakRecord Record,
    StatementMatchResult EngineResult,
    CanonicalStatementRow? StatementRow);
