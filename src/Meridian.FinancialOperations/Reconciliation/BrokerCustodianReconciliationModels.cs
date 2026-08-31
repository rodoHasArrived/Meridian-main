namespace Meridian.FinancialOperations.Reconciliation;

public enum CanonicalReconciliationSourceKind
{
    Broker,
    Custodian,
    InternalBook
}

public enum CanonicalReconciliationRecordKind
{
    Position,
    Cash,
    Transaction
}

public enum CanonicalReconciliationDecisionStatus
{
    AutoMatched,
    Proposed,
    UnresolvedBreak,
    ManuallyResolved,
    SignedOff
}

public sealed record BrokerCustodianFeedDescriptor(
    string FeedId,
    CanonicalReconciliationSourceKind SourceKind,
    string SourceSystem,
    string AccountId,
    string ExternalAccountId,
    DateOnly BusinessDate,
    string BaseCurrency,
    DateTimeOffset CapturedAtUtc,
    string ContentHash,
    string AdapterId,
    string AdapterVersion);

public sealed record CanonicalReconciliationRecord(
    string RecordId,
    BrokerCustodianFeedDescriptor Feed,
    CanonicalReconciliationRecordKind RecordKind,
    string InstrumentId,
    string? Symbol,
    decimal Quantity,
    decimal Price,
    decimal MarketValue,
    decimal CashAmount,
    string Currency,
    DateOnly TradeDate,
    DateOnly? SettlementDate,
    string ActivityType,
    string ExternalReference,
    IReadOnlyDictionary<string, string> Evidence);

public sealed record BrokerCustodianFeedBatch(
    BrokerCustodianFeedDescriptor Descriptor,
    IReadOnlyList<CanonicalReconciliationRecord> Records);

public interface IBrokerCustodianReconciliationFeedAdapter
{
    string AdapterId { get; }
    string AdapterVersion { get; }
    CanonicalReconciliationSourceKind SourceKind { get; }

    Task<BrokerCustodianFeedBatch> ReadAsync(BrokerCustodianFeedDescriptor descriptor, Stream source, CancellationToken ct = default);
}

public sealed record BrokerCustodianMatchRule(
    string RuleId,
    ReconciliationMatchTier Tier,
    decimal QuantityTolerance,
    decimal PriceTolerance,
    decimal MarketValueTolerance,
    decimal CashTolerance,
    TimeSpan SettlementTolerance,
    decimal MinimumConfidence);

public sealed record BrokerCustodianMatchDecision(
    string DecisionId,
    string RunId,
    string LeftRecordId,
    string? RightRecordId,
    CanonicalReconciliationDecisionStatus Status,
    ReconciliationMatchTier Tier,
    decimal Confidence,
    string RuleId,
    decimal QuantityVariance,
    decimal PriceVariance,
    decimal MarketValueVariance,
    decimal CashVariance,
    string Explanation,
    DateTimeOffset DecidedAtUtc,
    string DecidedBy,
    IReadOnlyList<string> EvidenceReferences)
{
    public bool IsTerminal => Status is CanonicalReconciliationDecisionStatus.AutoMatched
        or CanonicalReconciliationDecisionStatus.ManuallyResolved
        or CanonicalReconciliationDecisionStatus.SignedOff;
}

public sealed record ReconciliationResolutionHistoryEntry(
    string HistoryId,
    string DecisionId,
    string FromStatus,
    string ToStatus,
    string Actor,
    string Reason,
    DateTimeOffset OccurredAtUtc,
    IReadOnlyList<string> EvidenceReferences);

public interface IReconciliationDecisionJournal
{
    Task AppendDecisionAsync(BrokerCustodianMatchDecision decision, CancellationToken ct = default);
    Task AppendHistoryAsync(ReconciliationResolutionHistoryEntry history, CancellationToken ct = default);
    Task<IReadOnlyList<BrokerCustodianMatchDecision>> ListDecisionsAsync(string runId, CancellationToken ct = default);
    Task<IReadOnlyList<ReconciliationResolutionHistoryEntry>> ListHistoryAsync(string decisionId, CancellationToken ct = default);
}

public sealed class BrokerCustodianMatchingPipeline
{
    public static readonly IReadOnlyList<BrokerCustodianMatchRule> DefaultRules =
    [
        new("exact-position-cash-transaction-v1", ReconciliationMatchTier.Exact, 0m, 0m, 0m, 0m, TimeSpan.Zero, 1.00m),
        new("tolerance-position-cash-transaction-v1", ReconciliationMatchTier.Tolerance, 0.0001m, 0.01m, 0.01m, 0.01m, TimeSpan.FromDays(1), 0.90m),
        new("heuristic-reference-symbol-date-v1", ReconciliationMatchTier.Heuristic, 0.01m, 0.05m, 1.00m, 1.00m, TimeSpan.FromDays(3), 0.70m)
    ];

    public IReadOnlyList<BrokerCustodianMatchDecision> Match(
        string runId,
        IReadOnlyList<CanonicalReconciliationRecord> brokerRecords,
        IReadOnlyList<CanonicalReconciliationRecord> custodianRecords,
        DateTimeOffset decidedAtUtc,
        string decidedBy,
        IReadOnlyList<BrokerCustodianMatchRule>? rules = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentNullException.ThrowIfNull(brokerRecords);
        ArgumentNullException.ThrowIfNull(custodianRecords);
        var effectiveRules = rules is { Count: > 0 } ? rules : DefaultRules;
        var unmatchedCustodian = new HashSet<string>(custodianRecords.Select(static item => item.RecordId), StringComparer.Ordinal);
        var decisions = new List<BrokerCustodianMatchDecision>(brokerRecords.Count);

        foreach (var broker in brokerRecords.OrderBy(static item => item.RecordId, StringComparer.Ordinal))
        {
            var candidates = custodianRecords
                .Where(candidate => unmatchedCustodian.Contains(candidate.RecordId))
                .Where(candidate => candidate.RecordKind == broker.RecordKind)
                .Where(candidate => string.Equals(candidate.Feed.AccountId, broker.Feed.AccountId, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(candidate.Feed.ExternalAccountId, broker.Feed.ExternalAccountId, StringComparison.OrdinalIgnoreCase))
                .SelectMany(candidate => effectiveRules.Select(rule => ScoreCandidate(broker, candidate, rule)))
                .Where(scored => scored.Confidence >= scored.Rule.MinimumConfidence)
                .OrderByDescending(static scored => scored.Confidence)
                .ThenBy(static scored => scored.Rule.Tier)
                .ThenBy(static scored => scored.Candidate.RecordId, StringComparer.Ordinal)
                .ToArray();

            if (candidates.Length == 0)
            {
                decisions.Add(BuildBreakDecision(runId, broker, decidedAtUtc, decidedBy));
                continue;
            }

            var selected = candidates[0];
            unmatchedCustodian.Remove(selected.Candidate.RecordId);
            decisions.Add(BuildMatchedDecision(runId, broker, selected.Candidate, selected.Rule, selected.Confidence, decidedAtUtc, decidedBy));
        }

        foreach (var custodian in custodianRecords.Where(item => unmatchedCustodian.Contains(item.RecordId)).OrderBy(static item => item.RecordId, StringComparer.Ordinal))
        {
            decisions.Add(BuildBreakDecision(runId, custodian, decidedAtUtc, decidedBy));
        }

        return decisions;
    }

    private static (CanonicalReconciliationRecord Candidate, BrokerCustodianMatchRule Rule, decimal Confidence) ScoreCandidate(
        CanonicalReconciliationRecord left,
        CanonicalReconciliationRecord right,
        BrokerCustodianMatchRule rule)
    {
        var quantityVariance = Math.Abs(left.Quantity - right.Quantity);
        var priceVariance = Math.Abs(left.Price - right.Price);
        var marketValueVariance = Math.Abs(left.MarketValue - right.MarketValue);
        var cashVariance = Math.Abs(left.CashAmount - right.CashAmount);
        var settlementVariance = left.SettlementDate.HasValue && right.SettlementDate.HasValue
            ? Math.Abs(left.SettlementDate.Value.DayNumber - right.SettlementDate.Value.DayNumber)
            : 0;
        var symbolMatches = string.Equals(left.Symbol, right.Symbol, StringComparison.OrdinalIgnoreCase)
            || string.Equals(left.InstrumentId, right.InstrumentId, StringComparison.OrdinalIgnoreCase);
        var referenceMatches = !string.IsNullOrWhiteSpace(left.ExternalReference)
            && string.Equals(left.ExternalReference, right.ExternalReference, StringComparison.OrdinalIgnoreCase);
        var currencyMatches = string.Equals(left.Currency, right.Currency, StringComparison.OrdinalIgnoreCase);

        var withinNumericTolerance = quantityVariance <= rule.QuantityTolerance
            && priceVariance <= rule.PriceTolerance
            && marketValueVariance <= rule.MarketValueTolerance
            && cashVariance <= rule.CashTolerance
            && settlementVariance <= Math.Max(0, rule.SettlementTolerance.TotalDays);

        var confidence = rule.Tier switch
        {
            ReconciliationMatchTier.Exact when currencyMatches && withinNumericTolerance && referenceMatches => 1.00m,
            ReconciliationMatchTier.Exact when currencyMatches && withinNumericTolerance && symbolMatches => 0.99m,
            ReconciliationMatchTier.Tolerance when currencyMatches && withinNumericTolerance && (symbolMatches || referenceMatches) => 0.93m,
            ReconciliationMatchTier.Heuristic when currencyMatches && symbolMatches && left.TradeDate == right.TradeDate => 0.78m,
            ReconciliationMatchTier.Heuristic when currencyMatches && referenceMatches => 0.74m,
            _ => 0m
        };

        return (right, rule, confidence);
    }

    private static BrokerCustodianMatchDecision BuildMatchedDecision(
        string runId,
        CanonicalReconciliationRecord left,
        CanonicalReconciliationRecord right,
        BrokerCustodianMatchRule rule,
        decimal confidence,
        DateTimeOffset decidedAtUtc,
        string decidedBy)
        => new(
            DecisionId: StableDecisionId(runId, left.RecordId, right.RecordId),
            RunId: runId,
            LeftRecordId: left.RecordId,
            RightRecordId: right.RecordId,
            Status: rule.Tier is ReconciliationMatchTier.Heuristic
                ? CanonicalReconciliationDecisionStatus.Proposed
                : CanonicalReconciliationDecisionStatus.AutoMatched,
            Tier: rule.Tier,
            Confidence: confidence,
            RuleId: rule.RuleId,
            QuantityVariance: Math.Abs(left.Quantity - right.Quantity),
            PriceVariance: Math.Abs(left.Price - right.Price),
            MarketValueVariance: Math.Abs(left.MarketValue - right.MarketValue),
            CashVariance: Math.Abs(left.CashAmount - right.CashAmount),
            Explanation: $"{rule.Tier} reconciliation match using {rule.RuleId} at {confidence:P0} confidence.",
            DecidedAtUtc: decidedAtUtc,
            DecidedBy: string.IsNullOrWhiteSpace(decidedBy) ? "system" : decidedBy.Trim(),
            EvidenceReferences: [$"record:{left.RecordId}", $"record:{right.RecordId}"]);

    private static BrokerCustodianMatchDecision BuildBreakDecision(
        string runId,
        CanonicalReconciliationRecord record,
        DateTimeOffset decidedAtUtc,
        string decidedBy)
        => new(
            DecisionId: StableDecisionId(runId, record.RecordId, "unresolved"),
            RunId: runId,
            LeftRecordId: record.RecordId,
            RightRecordId: null,
            Status: CanonicalReconciliationDecisionStatus.UnresolvedBreak,
            Tier: ReconciliationMatchTier.Unmatched,
            Confidence: 0m,
            RuleId: "unmatched-break-v1",
            QuantityVariance: Math.Abs(record.Quantity),
            PriceVariance: Math.Abs(record.Price),
            MarketValueVariance: Math.Abs(record.MarketValue),
            CashVariance: Math.Abs(record.CashAmount),
            Explanation: "No broker/custodian candidate satisfied exact, tolerance, or heuristic matching rules.",
            DecidedAtUtc: decidedAtUtc,
            DecidedBy: string.IsNullOrWhiteSpace(decidedBy) ? "system" : decidedBy.Trim(),
            EvidenceReferences: [$"record:{record.RecordId}"]);

    private static string StableDecisionId(string runId, string left, string right)
    {
        var bytes = Meridian.Contracts.Integrity.Sha256Digest.ComputeBytesUtf8($"{runId}|{left}|{right}");
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
