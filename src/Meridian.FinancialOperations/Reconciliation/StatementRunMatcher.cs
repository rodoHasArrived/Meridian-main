using Meridian.Domain.Reconciliation;
using System.Globalization;
using Meridian.Contracts.Integrity;

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
            ToEngineTolerance(toleranceProfile))
        {
            StatementPeriodStart = import.StatementPeriodStart == default ? null : import.StatementPeriodStart,
            StatementPeriodEnd = statementPeriodEnd == default ? null : statementPeriodEnd
        });

        var breaks = new List<StatementRunBreak>();
        var matchGroups = new List<StatementRunMatchGroupRecord>();
        var matchCount = 0;
        var breakOrdinal = 0;
        // With no retained internal ledger transactions, every statement movement is structurally
        // unmatched — the breaks are real evidence but carry no comparison. Classify them so the
        // governed queue publishes them as informational instead of close blockers.
        var internalTransactionsUnavailable = populations.LedgerTransactions.Count == 0;
        var internalPositionsByEvidence = populations.Positions
            .GroupBy(static position => position.EvidenceReference, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.OrdinalIgnoreCase);
        var accruedTolerance = Math.Max(
            ToEngineTolerance(toleranceProfile).PositionMarketValue,
            StatementAccruedInterestReconciliation.MinimumTolerance);
        foreach (var result in engineResult.Results)
        {
            if (result.MatchTier is StatementMatchTier.Exact or StatementMatchTier.Tolerance)
            {
                matchCount++;
                matchGroups.Add(BuildMatchGroup(import.ImportId, result));
                // A holding can agree on quantity and value yet disagree on accrued interest (a bond
                // bought between coupons). That is a distinct break on the matched position.
                if (result.Kind == StatementMatchKind.Position
                    && TryBuildAccruedInterestBreak(import.ImportId, result, rowByEvidence, internalPositionsByEvidence,
                        accruedTolerance, fxRateProvider, normalizedBase, createdAtUtc, ref breakOrdinal) is { } accruedBreak)
                {
                    breaks.Add(accruedBreak);
                }

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
                Status: "Open")
            {
                Classification = internalTransactionsUnavailable && result.Kind == StatementMatchKind.Transaction
                    ? ReconciliationBreakClassifications.InternalTransactionPopulationUnavailable
                    : null
            };

            breaks.Add(new StatementRunBreak(record, result, statementRow));
        }

        return new StatementRunMatchResult(breaks, matchCount, matchGroups);
    }

    /// <summary>
    /// Retains one matched engine result as a durable group record. Member references are sorted so
    /// the group — and the deterministic id derived from its full membership — depends only on which
    /// records reconciled together, never on enumeration order.
    /// </summary>
    private static StatementRunMatchGroupRecord BuildMatchGroup(string importId, StatementMatchResult result)
    {
        var statementReferences = result.AllBrokerEvidenceReferences
            .OrderBy(static reference => reference, StringComparer.Ordinal)
            .ToArray();
        var internalReferences = result.AllInternalEvidenceReferences
            .OrderBy(static reference => reference, StringComparer.Ordinal)
            .ToArray();
        var groupId = ReconciliationMatchKernel.CreateDeterministicId(
            "match-group",
            [
                importId,
                result.Kind.ToString(),
                result.MatchTier.ToString(),
                .. result.RuleIds,
                "statement",
                .. statementReferences,
                "internal",
                .. internalReferences
            ]);
        return new StatementRunMatchGroupRecord(
            groupId,
            result.Kind.ToString(),
            result.MatchTier.ToString(),
            result.RuleIds.ToArray(),
            statementReferences,
            internalReferences,
            result.Confidence)
        {
            QuantityVariance = result.Variance.Quantity,
            MarketValueVariance = result.Variance.MarketValue,
            AmountVariance = result.Variance.Amount
        };
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
        // 32 hex characters keeps the shape callers already store and index on.
        return Sha256Digest.ComputeUtf8(material)[..32];
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

    private static StatementRunBreak? TryBuildAccruedInterestBreak(
        string importId,
        StatementMatchResult result,
        IReadOnlyDictionary<string, CanonicalStatementRow> rowByEvidence,
        IReadOnlyDictionary<string, InternalPortfolioPosition> internalPositionsByEvidence,
        decimal tolerance,
        IReconciliationFxRateProvider fxRateProvider,
        string baseCurrency,
        DateTimeOffset createdAtUtc,
        ref int breakOrdinal)
    {
        if (result.BrokerEvidenceReference is not { } brokerEvidence
            || result.InternalEvidenceReference is not { } internalEvidence
            || !rowByEvidence.TryGetValue(brokerEvidence, out var row)
            || row.AccruedInterest is not { } statedAccrued
            || !internalPositionsByEvidence.TryGetValue(internalEvidence, out var internalPosition))
        {
            return null;
        }

        string breakCode;
        decimal delta;
        string explanation;
        if (!fxRateProvider.TryConvert(statedAccrued, NormalizeCurrency(row.Currency, baseCurrency), baseCurrency,
                row.TradeDate, out var custodianAccrued))
        {
            breakCode = StatementAccruedInterestReconciliation.UnavailableBreakCode;
            delta = Math.Abs(statedAccrued);
            explanation = $"Custodian accrued interest in {row.Currency} cannot be converted to {baseCurrency}; it is not compared.";
        }
        else if (internalPosition.AccruedInterest is not { } bookedAccrued)
        {
            breakCode = StatementAccruedInterestReconciliation.UnavailableBreakCode;
            delta = Math.Abs(custodianAccrued);
            explanation = "Custodian states accrued interest on this holding, but the internal book carries none.";
        }
        else
        {
            delta = custodianAccrued - bookedAccrued;
            if (Math.Abs(delta) <= tolerance)
                return null;
            breakCode = StatementAccruedInterestReconciliation.VarianceBreakCode;
            explanation = $"Custodian accrued interest {custodianAccrued} differs from booked accrued interest {bookedAccrued} by {delta}.";
        }

        var engineResult = new StatementMatchResult(
            StatementMatchKind.Position,
            StatementMatchTier.Candidate,
            result.Confidence,
            [StatementAccruedInterestReconciliation.RuleId],
            brokerEvidence,
            internalEvidence,
            new StatementMatchVariance(Amount: delta),
            new StatementMatchTolerance(Amount: tolerance),
            explanation);
        var record = new ReconciliationBreakRecord(
            BreakId: BuildBreakId(importId, breakOrdinal++, brokerEvidence, breakCode),
            RunId: importId,
            ImportId: importId,
            SourceReference: brokerEvidence,
            BreakCode: breakCode,
            Category: row.ActivityType,
            Delta: Math.Abs(delta),
            Tolerance: tolerance,
            ToleranceBreached: true,
            CreatedAtUtc: createdAtUtc,
            Status: "Open");
        return new StatementRunBreak(record, engineResult, row);
    }

    private static NormalizedStatementPosition MapPosition(
        CanonicalStatementRow row,
        string account,
        string evidence,
        IReconciliationFxRateProvider fxRateProvider,
        string baseCurrency)
    {
        // A stated market value is authoritative: a bond's quantity is face and its price is per
        // 100 face, so quantity x price would overstate value a hundredfold.
        var marketValueSource = row.MarketValue ?? row.Price * row.Quantity;
        if (row.MarketValue is null && marketValueSource == 0m && row.CashAmount != 0m)
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
            evidence)
        {
            // With a stated market value the quoted price may be per 100 face rather than per unit,
            // so the engine derives the comparable unit price from value and quantity instead.
            Price = marketValue is null || row.MarketValue is not null
                ? null
                : NormalizePrice(row.Price, row.Currency, baseCurrency, row.TradeDate, fxRateProvider)
        };
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
            evidence)
        {
            Price = NormalizePrice(row.Price, row.Currency, baseCurrency, row.TradeDate, fxRateProvider)
        };
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
        return transaction with
        {
            Currency = currency,
            NetAmount = amount,
            Price = transaction.Price is { } price
                ? NormalizePrice(price, transaction.Currency, baseCurrency, transaction.TradeDate, fxRateProvider)
                : null
        };
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

    private static decimal? NormalizePrice(decimal price, string? currency, string baseCurrency,
        DateOnly asOf, IReconciliationFxRateProvider fxRateProvider)
        => price == 0m ? null : ToBaseCurrency(price, currency, baseCurrency, asOf, fxRateProvider).Amount;

    private static string NormalizeCurrency(string? currency, string baseCurrency) =>
        string.IsNullOrWhiteSpace(currency) ? baseCurrency : currency.Trim().ToUpperInvariant();

    private static StatementMatchingToleranceProfile ToEngineTolerance(StatementToleranceProfile profile)
        => StatementMatchingToleranceProfile.FromProfile(profile);

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
        if (result.BreakCode is { } code)
        {
            return code;
        }

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

/// <summary>
/// The outcome of a statement run's match pass: the breaks to persist, the match count, and one
/// group record per matched result carrying the evidence membership that formed the match.
/// <see cref="MatchCount"/> counts matched results (equal to <see cref="MatchGroups"/>.Count), not
/// statement rows: a split group covering several rows counts once, so row totals must be derived
/// from group membership rather than from this count.
/// </summary>
internal sealed record StatementRunMatchResult(
    IReadOnlyList<StatementRunBreak> Breaks,
    int MatchCount,
    IReadOnlyList<StatementRunMatchGroupRecord> MatchGroups);

/// <summary>
/// A single break produced by the match pass, carrying the persisted record, the originating engine
/// result (confidence, tier, rule ids, explanation), and the statement row when the break is on the
/// broker side (null for internal-only breaks).
/// </summary>
internal sealed record StatementRunBreak(
    ReconciliationBreakRecord Record,
    StatementMatchResult EngineResult,
    CanonicalStatementRow? StatementRow);

/// <summary>
/// Typed break codes and policy for reconciling custodian-stated accrued interest on a matched
/// position against Meridian's booked accrued interest receivable.
/// </summary>
public static class StatementAccruedInterestReconciliation
{
    /// <summary>Booked and custodian accrued interest differ beyond tolerance.</summary>
    public const string VarianceBreakCode = "POSITION_ACCRUED_INTEREST_VARIANCE";

    /// <summary>The custodian states accrued interest but no comparable internal figure exists.</summary>
    public const string UnavailableBreakCode = "POSITION_ACCRUED_INTEREST_UNAVAILABLE";

    public const string RuleId = "statement-position-accrued-interest-v1";

    /// <summary>One minor unit: accrued interest is a rounded cash amount, so sub-cent drift is noise.</summary>
    public const decimal MinimumTolerance = 0.01m;
}
