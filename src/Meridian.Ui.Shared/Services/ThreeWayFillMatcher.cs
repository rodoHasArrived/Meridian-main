using Meridian.Contracts.Workstation;

namespace Meridian.Ui.Shared.Services;

/// <summary>Fill-level difference types reported by the three-way comparison.</summary>
public static class ThreeWayFillClassification
{
    public const string MissingFillInLedger = "MissingFillInLedger";
    public const string MissingFillAtBroker = "MissingFillAtBroker";
    public const string QuantityMismatch = "QuantityMismatch";
    public const string PriceMismatch = "PriceMismatch";
    public const string FeeMismatch = "FeeMismatch";
    public const string TimingOnly = "TimingOnly";
    public const string FillMatched = "FillMatched";
    public const string FillCoverageUnavailable = "FillCoverageUnavailable";
}

/// <summary>
/// Pairs broker fills with posted Meridian fills and classifies each difference. Pairing never
/// guesses across symbols or sides: an exact broker activity id wins, then the client order id,
/// then an economic match (same symbol, side and quantity, price within tolerance, close in time).
/// </summary>
public static class ThreeWayFillMatcher
{
    /// <summary>Check status for a difference explained only by when each source observed the fill.</summary>
    public const string TimingStatus = "Timing";

    /// <summary>
    /// Check status when fill evidence is unavailable. It is recorded and counted, never read as
    /// clean, but it does not block the balance comparison that existed before fill matching.
    /// </summary>
    public const string NotComparedStatus = "NotCompared";

    /// <summary>Statuses that are recorded on the receipt but never queued as breaks.</summary>
    public static bool IsInformational(string status) => status is TimingStatus or NotComparedStatus;

    public static IReadOnlyList<ThreeWayReconciliationCheckDto> Match(
        ThreeWaySourceSnapshotDto ledger,
        ThreeWaySourceSnapshotDto broker,
        decimal priceTolerance,
        decimal feeTolerance,
        TimeSpan timeWindow)
    {
        // Neither source captured fill evidence (older receipts, providers without activity sync):
        // keep the balance-only comparison exactly as it was, with no fill checks at all.
        if (!ledger.FillCoverageComplete && !broker.FillCoverageComplete)
            return [];

        // Fill evidence from a source that failed coverage (stale, wrong account, unsupported) is not trusted.
        if (!ledger.FillCoverageComplete || !broker.FillCoverageComplete
            || ledger.CoverageFailures.Count > 0 || broker.CoverageFailures.Count > 0)
        {
            return
            [
                new ThreeWayReconciliationCheckDto("fill:coverage", "fill", null, null, "ledger", "broker", null, null, 0m,
                    NotComparedStatus, "Fill-level evidence is incomplete for the ledger or broker, so fills were not compared.")
                {
                    Classification = ThreeWayFillClassification.FillCoverageUnavailable
                }
            ];
        }

        var unmatchedLedger = ledger.Fills.ToList();
        var unmatchedBroker = broker.Fills.ToList();
        var pairs = new List<(ThreeWayFillDto Ledger, ThreeWayFillDto Broker)>();

        // 1. Exact broker activity id (fills recovered over REST carry it into the journal).
        foreach (var ledgerFill in unmatchedLedger.Where(fill => fill.BrokerActivityId is not null).ToArray())
        {
            var brokerFill = unmatchedBroker.FirstOrDefault(fill =>
                string.Equals(fill.BrokerActivityId, ledgerFill.BrokerActivityId, StringComparison.Ordinal));
            if (brokerFill is not null)
                Pair(ledgerFill, brokerFill);
        }

        // 2. Client order id, when the broker side mapped it; partial fills pair by quantity, then time.
        foreach (var ledgerFill in unmatchedLedger.Where(fill => fill.ClientOrderId is not null).ToArray())
        {
            var brokerFill = unmatchedBroker
                .Where(fill => string.Equals(fill.ClientOrderId, ledgerFill.ClientOrderId, StringComparison.Ordinal)
                    && SameInstrument(fill, ledgerFill))
                .OrderBy(fill => fill.Quantity == ledgerFill.Quantity ? 0 : 1)
                .ThenBy(fill => (fill.ExecutedAtUtc - ledgerFill.ExecutedAtUtc).Duration())
                .FirstOrDefault();
            if (brokerFill is not null)
                Pair(ledgerFill, brokerFill);
        }

        // 3. Economic match for streamed fills that carry neither id on the broker side.
        foreach (var ledgerFill in unmatchedLedger.ToArray())
        {
            var brokerFill = unmatchedBroker
                .Where(fill => SameInstrument(fill, ledgerFill) && fill.Quantity == ledgerFill.Quantity
                    && Math.Abs(fill.Price - ledgerFill.Price) <= priceTolerance
                    && (fill.ExecutedAtUtc - ledgerFill.ExecutedAtUtc).Duration() <= timeWindow)
                .OrderBy(fill => (fill.ExecutedAtUtc - ledgerFill.ExecutedAtUtc).Duration())
                .FirstOrDefault();
            if (brokerFill is not null)
                Pair(ledgerFill, brokerFill);
        }

        var checks = new List<ThreeWayReconciliationCheckDto>();
        foreach (var (ledgerFill, brokerFill) in pairs)
            checks.Add(ComparePair(ledgerFill, brokerFill, broker.AsOfUtc, priceTolerance, feeTolerance, timeWindow));
        foreach (var brokerFill in unmatchedBroker)
        {
            checks.Add(Check(brokerFill, null, brokerFill, "Break", ThreeWayFillClassification.MissingFillInLedger,
                brokerFill.Quantity, null, 0m,
                $"Broker fill {brokerFill.BrokerActivityId} ({brokerFill.Side} {brokerFill.Quantity} {brokerFill.Symbol} at {brokerFill.Price}) has no posted Meridian fill."));
        }
        foreach (var ledgerFill in unmatchedLedger)
        {
            // The broker observation is older than the ledger cut; a fill after it is not yet observable.
            var notYetObserved = ledgerFill.ExecutedAtUtc > broker.AsOfUtc - timeWindow;
            checks.Add(Check(ledgerFill, ledgerFill, null, notYetObserved ? TimingStatus : "Break",
                notYetObserved ? ThreeWayFillClassification.TimingOnly : ThreeWayFillClassification.MissingFillAtBroker,
                ledgerFill.Quantity, null, 0m,
                notYetObserved
                    ? $"Posted fill {ledgerFill.FillKey} executed after the retained broker observation; recheck on the next comparison."
                    : $"Posted fill {ledgerFill.FillKey} ({ledgerFill.Side} {ledgerFill.Quantity} {ledgerFill.Symbol} at {ledgerFill.Price}) has no broker fill."));
        }
        return checks;

        void Pair(ThreeWayFillDto ledgerFill, ThreeWayFillDto brokerFill)
        {
            unmatchedLedger.Remove(ledgerFill);
            unmatchedBroker.Remove(brokerFill);
            pairs.Add((ledgerFill, brokerFill));
        }
    }

    private static ThreeWayReconciliationCheckDto ComparePair(ThreeWayFillDto ledgerFill, ThreeWayFillDto brokerFill,
        DateTimeOffset brokerAsOf, decimal priceTolerance, decimal feeTolerance, TimeSpan timeWindow)
    {
        if (ledgerFill.Quantity != brokerFill.Quantity)
        {
            return Check(brokerFill, ledgerFill, brokerFill, "Break", ThreeWayFillClassification.QuantityMismatch,
                ledgerFill.Quantity, brokerFill.Quantity, 0m,
                $"Matched fill quantity differs: ledger {ledgerFill.Quantity}, broker {brokerFill.Quantity}.");
        }
        if (Math.Abs(ledgerFill.Price - brokerFill.Price) > priceTolerance)
        {
            return Check(brokerFill, ledgerFill, brokerFill, "Break", ThreeWayFillClassification.PriceMismatch,
                ledgerFill.Price, brokerFill.Price, priceTolerance,
                $"Matched fill price differs: ledger {ledgerFill.Price}, broker {brokerFill.Price}.");
        }
        // A broker that reports no commission gives no fee evidence; only reported fees are compared.
        if (brokerFill.Commission is { } brokerFee && Math.Abs((ledgerFill.Commission ?? 0m) - brokerFee) > feeTolerance)
        {
            return Check(brokerFill, ledgerFill, brokerFill, "Break", ThreeWayFillClassification.FeeMismatch,
                ledgerFill.Commission ?? 0m, brokerFee, feeTolerance,
                $"Matched fill commission differs: ledger {ledgerFill.Commission ?? 0m}, broker {brokerFee}.");
        }
        if ((ledgerFill.ExecutedAtUtc - brokerFill.ExecutedAtUtc).Duration() > timeWindow
            || ledgerFill.ExecutedAtUtc > brokerAsOf != brokerFill.ExecutedAtUtc > brokerAsOf)
        {
            return Check(brokerFill, ledgerFill, brokerFill, TimingStatus, ThreeWayFillClassification.TimingOnly,
                ledgerFill.Quantity, brokerFill.Quantity, 0m,
                "Matched fill economics agree; only the recorded execution times differ.");
        }
        return Check(brokerFill, ledgerFill, brokerFill, "Matched", ThreeWayFillClassification.FillMatched,
            ledgerFill.Quantity, brokerFill.Quantity, 0m, "Broker and posted fill agree.");
    }

    private static ThreeWayReconciliationCheckDto Check(ThreeWayFillDto subject, ThreeWayFillDto? ledgerFill,
        ThreeWayFillDto? brokerFill, string status, string classification, decimal? ledgerAmount, decimal? brokerAmount,
        decimal tolerance, string explanation)
    {
        // A stable subject per execution: the broker activity id when the broker saw it, else Meridian's fill id.
        var key = brokerFill?.BrokerActivityId ?? ledgerFill?.FillKey ?? subject.FillKey;
        var evidence = new List<string>();
        if (brokerFill?.BrokerActivityId is { } activityId)
            evidence.Add("broker-activity:" + activityId);
        if (ledgerFill is not null)
            evidence.AddRange(ledgerFill.JournalEntryIds.Select(id => "journal-entry:" + id));
        return new ThreeWayReconciliationCheckDto("fill:" + key, "fill", null, subject.Symbol, "ledger", "broker",
            ledgerAmount, brokerAmount, tolerance, status, explanation)
        {
            Classification = classification,
            EvidenceIds = evidence
        };
    }

    private static bool SameInstrument(ThreeWayFillDto left, ThreeWayFillDto right)
        => string.Equals(left.Symbol, right.Symbol, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.Side, right.Side, StringComparison.OrdinalIgnoreCase);
}
