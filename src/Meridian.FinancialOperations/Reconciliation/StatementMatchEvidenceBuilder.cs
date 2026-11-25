using System.Globalization;
using Meridian.Contracts.Reconciliation;
using Meridian.Domain.Reconciliation;

namespace Meridian.FinancialOperations.Reconciliation;

/// <summary>Captures the exact normalized records used by the matcher without choosing any additional pairs.</summary>
internal sealed class StatementMatchEvidenceBuilder(string baseCurrency)
{
    public RetainedStatementMatchEvidence Build(StatementMatchResult result)
    {
        var amountMeasure = result.Kind switch
        {
            StatementMatchKind.Position => "Market value",
            StatementMatchKind.Cash => "Cash balance",
            _ => "Net amount"
        };
        return Build(result, amountMeasure, Order(result.BrokerRecords), Order(result.InternalRecords), result.Kind == StatementMatchKind.Position
                ? result.Variance.MarketValue : result.Variance.Amount);
    }

    public static RetainedStatementMatchEvidence AccruedInterest(StatementMatchResult result, CanonicalStatementRow row,
        InternalPortfolioPosition ledger, decimal sourceAmount, string sourceCurrency, string baseCurrency, bool comparable)
        => Build(result, "Accrued interest",
            [new Operand(result.BrokerEvidenceReference!, result.BrokerEvidenceReference!, row.Symbol, row.TradeDate, null, null, sourceAmount, sourceCurrency)],
            [new Operand(ledger.PositionId, ledger.EvidenceReference, ledger.SecurityId, ledger.AsOfDate, null, null, ledger.AccruedInterest, baseCurrency)],
            comparable ? result.Variance.Amount : null);

    private static RetainedStatementMatchEvidence Build(StatementMatchResult result, string amountMeasure,
        Operand[] source, Operand[] ledger, decimal? amountVariance)
    {
        var all = source.Concat(ledger).ToArray();
        var residuals = new List<StatementMatchResidual>(2);
        if (all.Select(static item => item.Instrument).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1)
            AddResidual("Quantity", "units", static item => item.Quantity, result.Variance.Quantity, result.Tolerance.Quantity);
        var currencies = all.Select(static item => item.Currency).Distinct(StringComparer.Ordinal).ToArray();
        if (currencies is [string currency])
            AddResidual(amountMeasure, currency, static item => item.Amount, amountVariance, result.Tolerance.Amount);

        return new RetainedStatementMatchEvidence(result.MatchTier.ToString(), result.Explanation,
            result.MatchTier == StatementMatchTier.Unmatched ? null : Exact(result.Confidence), amountMeasure, result.RuleIds,
            source.Select(static item => item.ToAllocation()).ToArray(),
            ledger.Select(static item => item.ToAllocation()).ToArray(), residuals);

        void AddResidual(string measure, string unit, Func<Operand, decimal?> value, decimal? variance, decimal? tolerance)
        {
            if (variance is null || source.Length == 0 || ledger.Length == 0 || all.Any(item => value(item) is null))
                return;
            var sourceTotal = source.Sum(item => value(item)!.Value);
            var ledgerTotal = ledger.Sum(item => value(item)!.Value);
            if (sourceTotal - ledgerTotal == variance)
                residuals.Add(new StatementMatchResidual(measure, unit, Exact(sourceTotal), Exact(ledgerTotal),
                    Exact(variance.Value), Format(result.MatchTier == StatementMatchTier.Exact ? 0m : tolerance)));
        }
    }

    private Operand[] Order(IReadOnlyList<IStatementMatchItem> records)
        => records.OrderBy(static item => item.EvidenceReference, StringComparer.Ordinal)
            .ThenBy(static item => item.MatchId, StringComparer.Ordinal).Select(ToOperand).ToArray();

    private Operand ToOperand(IStatementMatchItem item) => item switch
    {
        NormalizedStatementPosition row => new(item.MatchId, item.EvidenceReference, row.SecurityId, row.AsOfDate, null,
            row.Quantity, row.MarketValue, baseCurrency, row.Price),
        InternalPortfolioPosition row => new(item.MatchId, item.EvidenceReference, row.SecurityId, row.AsOfDate, null,
            row.Quantity, row.MarketValue, baseCurrency, row.Price),
        NormalizedStatementCashBalance row => new(item.MatchId, item.EvidenceReference, null, row.AsOfDate, null,
            null, row.EndingBalance, row.ComparisonCurrency),
        InternalCashBalance row => new(item.MatchId, item.EvidenceReference, null, row.AsOfDate, null,
            null, row.Balance, row.ComparisonCurrency),
        NormalizedStatementTransaction row => new(item.MatchId, item.EvidenceReference, row.SecurityId, row.TradeDate,
            row.SettlementDate, row.Quantity, row.NetAmount, row.Currency, row.Price, row.TransactionType, row.ExternalTransactionId),
        InternalLedgerTransaction row => new(item.MatchId, item.EvidenceReference, row.SecurityId, row.TradeDate,
            row.SettlementDate, row.Quantity, row.NetAmount, row.Currency, row.Price, row.TransactionType, row.ExternalTransactionId),
        _ => throw new InvalidOperationException($"Unsupported statement match operand '{item.GetType().Name}'.")
    };

    private static string Exact(decimal value) => value.ToString(CultureInfo.InvariantCulture);
    private static string? Format(decimal? value) => value is { } number ? Exact(number) : null;

    private sealed record Operand(string Id, string Reference, string? Instrument, DateOnly Date, DateOnly? Settlement,
        decimal? Quantity, decimal? Amount, string? Currency, decimal? Price = null, string? TransactionType = null, string? ExternalTransactionId = null)
    {
        public StatementMatchAllocation ToAllocation() => new(Id, Reference, Instrument, Date, Settlement,
            Format(Quantity), Format(Amount), Amount is null && Price is null ? null : Currency,
            Format(Price), TransactionType, ExternalTransactionId);
    }
}
