using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using static Meridian.Execution.OrderManagement.OrderRiskSizingPolicy;

namespace Meridian.Execution.OrderManagement;

/// <summary>
/// Owns pending amendment attempts, restored-attempt provenance and broker outcome evidence.
/// The order view stays live and read-only: the facade applies returned transitions under its
/// shared report gate, persists them and removes attempts only after fill processing succeeds.
/// Mutating operations require that same gate (or construction before startup); concurrent
/// readers take no additional lock, including evidence reads under the recovery journal lock.
/// </summary>
internal sealed class OrderAmendmentBook(IReadOnlyDictionary<string, OrderState> orders)
{
    private readonly ConcurrentDictionary<string, PendingOrderAmendment> _pending = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, PendingOrderAmendment> _restored = new(StringComparer.Ordinal);
    // Candidate `with` copies keep the proposed snapshot object; equal-valued attempts must
    // never share the evidence that distinguishes acceptance of a trail-only amendment.
    private readonly ConditionalWeakTable<OrderState, StrongBox<bool>> _outcomes = new();

    internal bool Contains(string orderId) => _pending.ContainsKey(orderId);
    internal PendingOrderAmendment? GetPending(string orderId) => _pending.GetValueOrDefault(orderId);

    internal PendingOrderAmendment Begin(
        string orderId, OrderState original, OrderState proposed, string? brokerId, decimal? proposedTrail)
    {
        var pending = new PendingOrderAmendment(original, proposed, brokerId) { ProposedTrail = proposedTrail };
        if (!_pending.TryAdd(orderId, pending))
            throw new InvalidOperationException("An earlier amendment requires broker reconciliation.");
        return pending;
    }

    internal void Restore(RetainedBrokerageOrder retained)
    {
        var state = retained.State;
        if (retained.PendingAmendment is { } pending)
        {
            _pending[state.OrderId] = pending;
            _restored[state.OrderId] = pending;
        }
        else if (retained.RequiresAmendmentRecovery)
            _pending[state.OrderId] = new PendingOrderAmendment(state, state, retained.BrokerOrderId);
    }

    internal bool IsRestoredAttempt(string orderId, PendingOrderAmendment pending) =>
        _restored.TryGetValue(orderId, out var restored)
        && ReferenceEquals(restored.Original, pending.Original);

    internal bool IsSameAttempt(string orderId, PendingOrderAmendment amendment) =>
        _pending.TryGetValue(orderId, out var current)
        && ReferenceEquals(current.Original, amendment.Original)
        && ReferenceEquals(current.Proposed, amendment.Proposed);

    internal bool Remove(string orderId) => _pending.TryRemove(orderId, out _);

    // Preserve the existing expected-entry record equality, distinct from the snapshot
    // reference comparisons that identify an attempt across candidate enrichment.
    internal bool RemoveExpected(string orderId, PendingOrderAmendment amendment) =>
        _pending.TryRemove(new KeyValuePair<string, PendingOrderAmendment>(orderId, amendment));

    internal void RecordOutcome(PendingOrderAmendment pending, bool accepted) =>
        _outcomes.AddOrUpdate(pending.Proposed, new StrongBox<bool>(accepted));

    internal bool ResolveOutcome(string orderId, PendingOrderAmendment amendment)
    {
        if (_outcomes.TryGetValue(amendment.Proposed, out var outcome))
            return outcome.Value;
        var resolved = orders[orderId];
        return resolved.LimitPrice == amendment.Proposed.LimitPrice
            && resolved.StopPrice == amendment.Proposed.StopPrice
            && resolved.Quantity == Math.Max(amendment.Proposed.Quantity, resolved.FilledQuantity);
    }

    internal static string? DescribeInvalidModification(OrderState state, OrderModification modification) =>
        modification.NewQuantity is <= 0m ? "Amended quantity must be positive."
        : modification.NewQuantity is { } quantity && quantity < state.FilledQuantity
            ? "Amended quantity cannot be below the quantity already filled."
        : modification.NewLimitPrice is <= 0m ? "Amended limit price must be positive."
        : modification.NewStopPrice is <= 0m ? "Amended stop price must be positive."
        : modification.NewTrail is <= 0m ? "Amended trail must be positive."
        : null;

    internal static OrderState ApplyAmendmentTerms(OrderState current, OrderState terms) => current with
    {
        Quantity = Math.Max(terms.Quantity, current.FilledQuantity),
        LimitPrice = terms.LimitPrice,
        StopPrice = terms.StopPrice,
        RoutedNotional = terms.RoutedNotional
    };

    private static bool MatchesAmendmentTerms(ExecutionReport report, OrderState terms) =>
        report.OrderQuantity == terms.Quantity && (!report.HasOrderTerms
            || report.OrderLimitPrice == terms.LimitPrice && report.OrderStopPrice == terms.StopPrice);

    internal static OrderState SelectAmendmentTerms(
        ExecutionReport report, PendingOrderAmendment pending, bool locallyConfirmed = false)
    {
        if (report.ReportType is ExecutionReportType.ModificationPending)
            throw new InvalidDataException("Broker amendment outcome remains pending.");
        var brokerId = report.GatewayOrderId ?? report.OrderId;
        var successor = !string.Equals(brokerId, pending.OriginalBrokerOrderId, StringComparison.Ordinal)
            && (report.ReplacesOrderId == pending.OriginalBrokerOrderId || brokerId == pending.CandidateBrokerOrderId);
        if (brokerId == pending.OriginalBrokerOrderId
            && (pending.CandidateBrokerOrderId is not null || pending.CandidateClientOrderId is not null)
            && IsTerminal(report.OrderStatus)
            && report.ReportType is not ExecutionReportType.ModificationRejected)
            throw new InvalidDataException("A terminal predecessor does not establish the candidate replacement outcome.");
        if (locallyConfirmed && report.ReportType is ExecutionReportType.Modified)
            return pending.Proposed;
        // A rejection settles the amendment as refused whichever broker id carries it: a
        // rejected successor (Robinhood reports the rejected replacement itself) never makes the
        // proposed terms live, even when its echoed terms match them.
        if (report.ReportType is ExecutionReportType.ModificationRejected)
            return pending.Original;
        if (!report.HasOrderTerms
            && (pending.Original.LimitPrice != pending.Proposed.LimitPrice || pending.Original.StopPrice != pending.Proposed.StopPrice))
            throw new InvalidDataException("Broker recovery lacks the prices needed to distinguish amendment outcomes.");
        if (successor && MatchesAmendmentTerms(report, pending.Proposed))
            return pending.Proposed;
        var original = MatchesAmendmentTerms(report, pending.Original);
        var proposed = MatchesAmendmentTerms(report, pending.Proposed);
        if (original && proposed)
            // Indistinguishable terms (a trail-only amendment): the report type is the evidence.
            return report.ReportType is ExecutionReportType.Modified ? pending.Proposed : pending.Original;
        if (original)
            return pending.Original;
        if (proposed)
            return pending.Proposed;
        throw new InvalidDataException("Broker recovery matches neither retained amendment outcome.");
    }

    internal AmendmentReportTransition PrepareReport(
        string orderId, ExecutionReport report, decimal? locallyAuthorizedQuantity)
    {
        if (!_pending.TryGetValue(orderId, out var pending))
            return new(report);
        if (report.CandidateGatewayOrderId is { Length: > 0 } candidate)
        {
            pending = pending with { CandidateBrokerOrderId = candidate };
            _pending[orderId] = pending;
        }
        if (report.CandidateClientOrderId is { Length: > 0 } candidateClient)
        {
            pending = pending with { CandidateClientOrderId = candidateClient };
            _pending[orderId] = pending;
        }
        var current = orders[orderId];
        if (report.ReportType is ExecutionReportType.Modified
            && report.GatewayOrderId is { Length: > 0 } confirmedCandidate
            && confirmedCandidate != pending.OriginalBrokerOrderId)
        {
            pending = pending with { CandidateBrokerOrderId = confirmedCandidate };
            _pending[orderId] = pending;
        }
        if (report.ReportType is ExecutionReportType.ModificationPending)
        {
            var status = current.Status;
            if (!IsTerminal(status) && report.FillPrice is > 0m && report.FilledQuantity > current.FilledQuantity)
                status = report.FilledQuantity >= Math.Max(pending.Original.Quantity, pending.Proposed.Quantity)
                    ? OrderStatus.Filled : OrderStatus.PartiallyFilled;
            report = report with { OrderStatus = status, OrderQuantity = current.Quantity };
            return new(report);
        }
        if (report.ReportType is not (ExecutionReportType.Modified or ExecutionReportType.ModificationRejected))
            return new(report);
        OrderState terms;
        try
        {
            terms = SelectAmendmentTerms(report, pending, locallyAuthorizedQuantity is not null);
        }
        catch (InvalidDataException)
        {
            report = report with
            {
                ReportType = ExecutionReportType.ModificationPending,
                OrderStatus = current.Status,
                OrderQuantity = current.Quantity
            };
            return new(report);
        }
        // A late working acknowledgement cannot reopen or resize a terminal order. Its
        // still-possible exposure remains separately reserved until authoritative recovery.
        if (IsTerminal(current.Status)
            && (!IsTerminal(report.OrderStatus) || terms.Quantity != current.Quantity))
        {
            report = report with
            {
                ReportType = ExecutionReportType.ModificationPending,
                OrderStatus = current.Status,
                OrderQuantity = current.Quantity
            };
            return new(report);
        }
        return new(report, ApplyAmendmentTerms(current, terms), pending,
            ReferenceEquals(terms, pending.Proposed));
    }

    internal IReadOnlyList<OrderState> GetExposureReservations()
    {
        var reservations = new List<OrderState>();
        foreach (var (id, pending) in _pending)
        {
            var current = orders.GetValueOrDefault(id) ?? pending.Original;
            if (Math.Max(pending.Original.Quantity, pending.Proposed.Quantity) <= current.FilledQuantity)
                continue;
            // Reserve one real outcome, never independent maxima: 100 shares at $1 amended to
            // 1 share at $100 is $100 either way, not a synthetic 100 shares at $100. Exposure
            // providers multiply quantity by price, so the terms must stay correlated.
            var outcome = SelectLargerAmendmentOutcome(pending, current);
            if (outcome.Quantity <= current.FilledQuantity)
                outcome = outcome == pending.Original ? pending.Proposed : pending.Original;
            reservations.Add(current with
            {
                Status = OrderStatus.Accepted,
                Quantity = outcome.Quantity,
                LimitPrice = outcome.LimitPrice,
                StopPrice = outcome.StopPrice,
                RoutedNotional = outcome.RoutedNotional
            });
        }
        return reservations;
    }

    private static OrderState SelectLargerAmendmentOutcome(PendingOrderAmendment pending, OrderState current)
    {
        // Compare what each outcome can still execute: both share the fills already booked, so
        // total order value misranks them (90 filled: 100@$100 leaves $1,000, 200@$40 leaves $4,400).
        decimal? Remaining(OrderState outcome)
        {
            var remaining = Math.Max(0m, outcome.Quantity - current.FilledQuantity);
            var routed = outcome.RoutedNotional is { } notional && notional > 0m && outcome.Quantity > 0m
                ? notional * remaining / outcome.Quantity
                : (decimal?)null;
            return MeasureOrderValue(remaining, outcome.LimitPrice, outcome.StopPrice, routed,
                current.ContractMultiplier, current.UsesFaceValuePercentageOfPar);
        }

        var original = Remaining(pending.Original);
        var proposed = Remaining(pending.Proposed);
        // Unpriced outcomes (market orders) are valued at the mark by the exposure provider, so
        // the larger remaining quantity is the larger exposure.
        if (original is null || proposed is null || proposed.Value == original.Value)
            return pending.Proposed.Quantity > pending.Original.Quantity ? pending.Proposed : pending.Original;
        return proposed.Value > original.Value ? pending.Proposed : pending.Original;
    }

    private static bool IsTerminal(OrderStatus status) =>
        status is OrderStatus.Filled or OrderStatus.Cancelled or OrderStatus.Rejected or OrderStatus.Expired;
}

/// <summary>
/// A prepared report and optional live-order transition. The facade writes UpdatedState before
/// recording Accepted, and retains ResolvedAmendment until the report's fill effects complete.
/// </summary>
internal readonly record struct AmendmentReportTransition(
    ExecutionReport Report,
    OrderState? UpdatedState = null,
    PendingOrderAmendment? ResolvedAmendment = null,
    bool Accepted = false);
