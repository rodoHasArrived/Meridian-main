using Meridian.Domain.Reconciliation;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

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
        var statementPeriodEnd = import.StatementPeriodEnd == default
            ? import.StatementDate
            : import.StatementPeriodEnd;

        // A statement run reconciles a single custodian account. Before replacing the source-row
        // account with the run-level key, reject any populated source account that names a different
        // custodian account. Otherwise a statement for account B could be normalized to account A and
        // falsely reconcile against A's internal book. Account aliases must be resolved before this
        // boundary; this matcher never silently treats distinct account identifiers as equivalent.
        var canonicalAccount = string.IsNullOrWhiteSpace(import.ExternalAccountId)
            ? import.FundAccountId.Trim()
            : import.ExternalAccountId.Trim();
        ValidateStatementAccounts(rows, canonicalAccount);

        foreach (var row in rows)
        {
            var evidence = $"{import.ImportId}:{row.SourceRowNumber}";
            rowByEvidence[evidence] = row;
            switch (Classify(row.ActivityType))
            {
                case StatementRowKind.Position:
                    statementPositions.Add(MapPosition(row, canonicalAccount, evidence, fxRateProvider, normalizedBase));
                    break;
                case StatementRowKind.CashBalance:
                    statementCash.Add(MapCash(row, canonicalAccount, evidence, statementPeriodEnd, fxRateProvider, normalizedBase));
                    break;
                default:
                    statementTransactions.Add(MapTransaction(row, canonicalAccount, evidence, fxRateProvider, normalizedBase));
                    break;
            }
        }

        var internalCash = populations.CashBalances
            .Select(cash => NormalizeInternalCash(cash, fxRateProvider, normalizedBase, statementPeriodEnd))
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
        var breakOrdinal = 0;
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
            var toleranceBreached = IsToleranceBreached(result);
            var breakCode = BuildBreakCode(result);

            var record = new ReconciliationBreakRecord(
                BreakId: BuildBreakId(import.ImportId, breakOrdinal++, sourceReference, breakCode),
                RunId: import.ImportId,
                ImportId: import.ImportId,
                SourceReference: sourceReference,
                BreakCode: breakCode,
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

    /// <summary>
    /// Derives a break identity from the break's own material instead of minting a random one. The
    /// statement run's recovery design rebuilds the match artifact when a replay resumes an
    /// interrupted run and refuses to adopt an artifact that differs from the retained one, so a
    /// random id would make every replay conflict with the evidence it is trying to recover. The
    /// enumeration ordinal is part of the material because two unmatched results can legitimately
    /// share a source reference; the engine's result order is deterministic for a given input.
    /// </summary>
    private static string BuildBreakId(
        string importId,
        int ordinal,
        string sourceReference,
        string breakCode)
    {
        var material = string.Join(
            '|',
            importId,
            ordinal.ToString(CultureInfo.InvariantCulture),
            sourceReference,
            breakCode);
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        // 32 hex characters keeps the shape callers already store and index on.
        return Convert.ToHexString(digest)[..32].ToLowerInvariant();
    }

    private static void ValidateStatementAccounts(
        IReadOnlyList<CanonicalStatementRow> rows,
        string canonicalAccount)
    {
        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.Account))
            {
                continue;
            }

            var sourceAccount = row.Account.Trim();
            if (!string.Equals(sourceAccount, canonicalAccount, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"Statement row {row.SourceRowNumber} identifies account '{sourceAccount}', " +
                    $"which does not match the requested external account '{canonicalAccount}'.");
            }
        }
    }

    private static NormalizedStatementPosition MapPosition(
        CanonicalStatementRow row,
        string account,
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
            account,
            row.Symbol,
            row.TradeDate,
            row.Quantity,
            marketValue,
            evidence);
    }

    private static NormalizedStatementCashBalance MapCash(
        CanonicalStatementRow row,
        string account,
        string evidence,
        DateOnly statementPeriodEnd,
        IReconciliationFxRateProvider fxRateProvider,
        string baseCurrency)
    {
        var sourceCurrency = NormalizeCurrency(row.Currency, baseCurrency);
        var (_, amount) = ToBaseCurrency(row.CashAmount, sourceCurrency, baseCurrency, row.TradeDate, fxRateProvider);
        // A cash balance is a closing balance only when it is dated at the run's closing period. Keep the
        // source as-of date for evidence and mark an out-of-period row ineligible for matching. This
        // prevents a stale cash row from reconciling if a faulty internal source happens to return the
        // same stale snapshot instead of the requested period-end snapshot.
        return new NormalizedStatementCashBalance(
            evidence,
            account,
            sourceCurrency,
            amount,
            evidence,
            row.TradeDate,
            IsForStatementPeriodEnd: row.TradeDate == statementPeriodEnd);
    }

    private static NormalizedStatementTransaction MapTransaction(
        CanonicalStatementRow row,
        string account,
        string evidence,
        IReconciliationFxRateProvider fxRateProvider,
        string baseCurrency)
    {
        var netAmountSource = row.CashAmount != 0m ? row.CashAmount : row.Price * row.Quantity;
        var (currency, amount) = ToBaseCurrency(netAmountSource, row.Currency, baseCurrency, row.TradeDate, fxRateProvider);
        return new NormalizedStatementTransaction(
            evidence,
            string.IsNullOrWhiteSpace(row.ExternalTransactionId) ? null : row.ExternalTransactionId,
            account,
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
        var sourceCurrency = NormalizeCurrency(cash.Currency, baseCurrency);
        var (_, amount) = ToBaseCurrency(cash.Balance, sourceCurrency, baseCurrency, asOf, fxRateProvider);
        return cash with { Currency = sourceCurrency, Balance = amount };
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

    private static string NormalizeCurrency(string? currency, string baseCurrency) =>
        string.IsNullOrWhiteSpace(currency) ? baseCurrency : currency.Trim().ToUpperInvariant();

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

    private static bool IsToleranceBreached(StatementMatchResult result)
    {
        if (result.Variance.Quantity is { } quantityVariance
            && Math.Abs(quantityVariance) > (result.Tolerance.Quantity ?? 0m))
        {
            return true;
        }

        var amountTolerance = result.Tolerance.Amount ?? 0m;
        return (result.Variance.MarketValue is { } marketValueVariance
                && Math.Abs(marketValueVariance) > amountTolerance)
            || (result.Variance.Amount is { } amountVariance
                && Math.Abs(amountVariance) > amountTolerance);
    }

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
