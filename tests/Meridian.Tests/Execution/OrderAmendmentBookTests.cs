using Meridian.Execution.OrderManagement;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Xunit;

namespace Meridian.Tests.Execution;

public sealed class OrderAmendmentBookTests
{
    [Theory]
    [InlineData("replacement-client", "replacement-client")]
    [InlineData(" \t ", null)]
    public void Begin_PreservesPreassignedRecoveryEvidenceBeforeAnyBrokerReport(
        string replacementReference, string? expectedReference)
    {
        var original = State();
        var proposed = original with { Quantity = 20m };
        var book = new OrderAmendmentBook(Orders(original));
        var pending = book.Begin(original.OrderId, original, proposed, "original-broker", 2m,
            replacementReference, ["older-broker", "prior-broker"]);

        Assert.Same(pending, book.GetPending(original.OrderId));
        Assert.Equal(expectedReference, pending.CandidateClientOrderId);
        Assert.Equal(new[] { "older-broker", "prior-broker" }, pending.PriorChainBrokerOrderIds);
        Assert.Equal(2m, pending.ProposedTrail);

        book.PrepareReport(original.OrderId, Report(ExecutionReportType.ModificationPending) with
        { CandidateGatewayOrderId = "successor-broker" }, null);
        var enriched = Assert.IsType<PendingOrderAmendment>(book.GetPending(original.OrderId));
        Assert.Equal(expectedReference, enriched.CandidateClientOrderId);
        Assert.Same(pending.PriorChainBrokerOrderIds, enriched.PriorChainBrokerOrderIds);
        Assert.Same(original, enriched.Original);
        Assert.Same(proposed, enriched.Proposed);
    }

    [Fact]
    public void CandidateEnrichment_PreservesAttemptIdentityButRequiresTheCurrentEntryForRemoval()
    {
        var original = State();
        var proposed = original with { Quantity = 20m };
        var orders = Orders(original);
        var book = new OrderAmendmentBook(orders);
        var attempt = book.Begin(original.OrderId, original, proposed, "original-broker", null,
            candidateClientOrderId: "preassigned-reference", priorChainBrokerOrderIds: ["earlier-broker"]);
        Assert.Equal("preassigned-reference", attempt.CandidateClientOrderId);
        Assert.Equal(["earlier-broker"], Assert.IsType<string[]>(attempt.PriorChainBrokerOrderIds));

        var transition = book.PrepareReport(original.OrderId, Report(ExecutionReportType.ModificationPending) with
        {
            CandidateGatewayOrderId = "candidate-broker",
            CandidateClientOrderId = "candidate-client"
        }, null);

        var enriched = Assert.IsType<PendingOrderAmendment>(book.GetPending(original.OrderId));
        Assert.NotSame(attempt, enriched);
        Assert.Same(original, enriched.Original);
        Assert.Same(proposed, enriched.Proposed);
        Assert.True(book.IsSameAttempt(original.OrderId, attempt));
        Assert.False(book.IsSameAttempt(original.OrderId, attempt with { Original = original with { } }));
        Assert.False(book.IsSameAttempt(original.OrderId, attempt with { Proposed = proposed with { } }));
        Assert.Equal("candidate-broker", enriched.CandidateBrokerOrderId);
        Assert.Equal("candidate-client", enriched.CandidateClientOrderId);
        Assert.Equal(["earlier-broker"], Assert.IsType<string[]>(enriched.PriorChainBrokerOrderIds));
        Assert.Null(transition.UpdatedState);
        Assert.Null(transition.ResolvedAmendment);
        Assert.False(book.RemoveExpected(original.OrderId, attempt));
        Assert.Same(enriched, book.GetPending(original.OrderId));
        Assert.True(book.RemoveExpected(original.OrderId, enriched));
    }

    [Fact]
    public void TrailOnlyOutcome_UsesSnapshotIdentityAcrossCopiesAndDoesNotLeakToEqualValuedAttempts()
    {
        var original = State();
        var proposed = original with { };
        var book = new OrderAmendmentBook(Orders(original));
        var refused = book.Begin(original.OrderId, original, proposed, "original-broker", 5m);
        book.RecordOutcome(refused with { CandidateClientOrderId = "candidate" }, accepted: false);
        Assert.True(book.Remove(original.OrderId));

        var later = book.Begin(original.OrderId, original with { }, proposed with { }, "original-broker", 5m);

        Assert.Equal(refused, later);
        Assert.False(book.IsSameAttempt(original.OrderId, refused));
        Assert.False(book.ResolveOutcome(original.OrderId, refused));
        Assert.True(book.ResolveOutcome(original.OrderId, later));
        // Expected-entry removal intentionally retains record equality, a separate contract
        // from the reference comparisons used to track an attempt and its broker outcome.
        Assert.True(book.RemoveExpected(original.OrderId, refused));
    }

    [Fact]
    public void LateCompletion_CannotRemoveANewerDifferentAttempt()
    {
        var original = State();
        var book = new OrderAmendmentBook(Orders(original));
        var earlier = book.Begin(original.OrderId, original, original with { Quantity = 20m }, "original-broker", null);
        Assert.True(book.Remove(original.OrderId));
        var later = book.Begin(original.OrderId, original, original with { Quantity = 30m }, "original-broker", null);

        Assert.False(book.RemoveExpected(original.OrderId, earlier));
        Assert.Same(later, book.GetPending(original.OrderId));
        Assert.False(book.IsSameAttempt(original.OrderId, earlier));
    }

    [Fact]
    public void RestoredProvenance_FollowsTheOriginalSnapshotAcrossCandidateEnrichment()
    {
        var original = State();
        var pending = new PendingOrderAmendment(original, original with { Quantity = 20m }, "original-broker");
        var book = new OrderAmendmentBook(Orders(original));
        book.Restore(new RetainedBrokerageOrder(original, "original-broker", true)
        { PendingAmendment = pending, RequiresAmendmentRecovery = true });
        book.PrepareReport(original.OrderId, Report(ExecutionReportType.ModificationPending) with
        { CandidateClientOrderId = "restored-candidate" }, null);

        var enriched = Assert.IsType<PendingOrderAmendment>(book.GetPending(original.OrderId));
        Assert.True(book.IsRestoredAttempt(original.OrderId, enriched));
        Assert.True(book.IsRestoredAttempt(original.OrderId, enriched with { Proposed = enriched.Proposed with { } }));
        Assert.False(book.IsRestoredAttempt(original.OrderId, enriched with { Original = original with { } }));
        Assert.True(book.Remove(original.OrderId));
        var later = book.Begin(original.OrderId, original with { }, original with { Quantity = 30m }, "original-broker", null);
        Assert.False(book.IsRestoredAttempt(original.OrderId, later));
    }

    [Fact]
    public void LegacyRecoveryPlaceholder_DoesNotClaimRestoredCandidateProvenance()
    {
        var original = State();
        var book = new OrderAmendmentBook(Orders(original));
        book.Restore(new RetainedBrokerageOrder(original, "original-broker", true)
        { RequiresAmendmentRecovery = true });

        var pending = Assert.IsType<PendingOrderAmendment>(book.GetPending(original.OrderId));
        Assert.Same(original, pending.Original);
        Assert.Same(original, pending.Proposed);
        Assert.False(book.IsRestoredAttempt(original.OrderId, pending));
    }

    [Fact]
    public void ConfirmedReport_ReturnsATransitionWithoutWritingTheLiveOrderOrRemovingPendingEvidence()
    {
        var original = State();
        var proposed = original with { Quantity = 20m };
        var orders = Orders(original);
        var book = new OrderAmendmentBook(orders);
        var attempt = book.Begin(original.OrderId, original, proposed, "original-broker", null);

        var transition = book.PrepareReport(original.OrderId, Report(ExecutionReportType.Modified) with
        { GatewayOrderId = "candidate-broker", OrderQuantity = 20m }, locallyAuthorizedQuantity: 20m);

        Assert.True(transition.Accepted);
        Assert.Equal(20m, transition.UpdatedState!.Quantity);
        Assert.Same(original, orders[original.OrderId]);
        Assert.Same(proposed, transition.ResolvedAmendment!.Proposed);
        Assert.True(book.IsSameAttempt(original.OrderId, attempt));
        Assert.True(book.Contains(original.OrderId));
        // The facade applies the live-order write, records evidence, processes the fill, then
        // removes this exact candidate-enriched attempt. Preparing alone cannot clear it.
        orders[original.OrderId] = transition.UpdatedState;
        book.RecordOutcome(transition.ResolvedAmendment, transition.Accepted);
        Assert.True(book.ResolveOutcome(original.OrderId, attempt));
        Assert.True(book.RemoveExpected(original.OrderId, transition.ResolvedAmendment));
    }

    [Fact]
    public void RejectedSuccessor_WithProposedTerms_SelectsTheOriginalSnapshot()
    {
        var original = State();
        var proposed = original with { Quantity = 20m, LimitPrice = 105m };
        var pending = new PendingOrderAmendment(original, proposed, "original-broker")
        { CandidateBrokerOrderId = "candidate-broker" };
        var report = Report(ExecutionReportType.ModificationRejected) with
        {
            GatewayOrderId = "candidate-broker",
            ReplacesOrderId = "original-broker",
            OrderQuantity = 20m,
            HasOrderTerms = true,
            OrderLimitPrice = 105m
        };

        Assert.Same(original, OrderAmendmentBook.SelectAmendmentTerms(report, pending));
    }

    [Fact]
    public void LateWorkingAcknowledgement_ReadsTheCurrentTerminalStateAndKeepsSeparateExposure()
    {
        var original = State();
        var orders = Orders(original);
        var book = new OrderAmendmentBook(orders);
        book.Begin(original.OrderId, original, original with { Quantity = 20m }, "original-broker", null);
        var cancelled = original with { Status = OrderStatus.Cancelled, FilledQuantity = 3m };
        orders[original.OrderId] = cancelled;

        var transition = book.PrepareReport(original.OrderId, Report(ExecutionReportType.Modified) with
        { GatewayOrderId = "candidate-broker", OrderQuantity = 20m }, 20m);

        Assert.Equal(ExecutionReportType.ModificationPending, transition.Report.ReportType);
        Assert.Equal(OrderStatus.Cancelled, transition.Report.OrderStatus);
        Assert.Equal(10m, transition.Report.OrderQuantity);
        Assert.Null(transition.UpdatedState);
        Assert.Null(transition.ResolvedAmendment);
        Assert.Same(cancelled, orders[original.OrderId]);
        var reservation = Assert.Single(book.GetExposureReservations());
        Assert.Equal(OrderStatus.Accepted, reservation.Status);
        Assert.Equal(20m, reservation.Quantity);
        Assert.Equal(3m, reservation.FilledQuantity);
    }

    [Fact]
    public void PendingFill_UsesBothPossibleQuantityCapsWithoutAuthorizingTheReportedTerms()
    {
        var original = State();
        var book = new OrderAmendmentBook(Orders(original));
        book.Begin(original.OrderId, original, original with { Quantity = 20m }, "original-broker", null);

        var transition = book.PrepareReport(original.OrderId, Report(ExecutionReportType.ModificationPending) with
        { FilledQuantity = 12m, FillPrice = 100m, OrderQuantity = 50m, OrderStatus = OrderStatus.Filled }, null);

        Assert.Equal(OrderStatus.PartiallyFilled, transition.Report.OrderStatus);
        Assert.Equal(10m, transition.Report.OrderQuantity);
        Assert.Null(transition.UpdatedState);
        Assert.True(book.Contains(original.OrderId));
    }

    [Theory]
    [InlineData(10, 100, 20, 40, 0, 10, 100)]
    [InlineData(10, 100, 5, 300, 0, 5, 300)]
    [InlineData(100, 100, 200, 40, 90, 200, 40)]
    public void Exposure_SelectsOneCorrelatedRemainingOutcomeAndRetainsOwnership(
        int originalQuantity, int originalPrice, int proposedQuantity, int proposedPrice,
        int filled, int expectedQuantity, int expectedPrice)
    {
        var original = State() with { Quantity = originalQuantity, LimitPrice = originalPrice };
        var current = original with { FilledQuantity = filled, ContractMultiplier = 100m };
        var orders = Orders(current);
        var book = new OrderAmendmentBook(orders);
        book.Begin(original.OrderId, original, original with
        { Quantity = proposedQuantity, LimitPrice = proposedPrice }, "original-broker", null);

        var reservation = Assert.Single(book.GetExposureReservations());

        Assert.Equal((decimal)expectedQuantity, reservation.Quantity);
        Assert.Equal((decimal)expectedPrice, reservation.LimitPrice);
        Assert.Equal(original.FundAccountId, reservation.FundAccountId);
        Assert.Equal(original.StrategyId, reservation.StrategyId);
        Assert.Equal(original.RunId, reservation.RunId);
        Assert.Equal(100m, reservation.ContractMultiplier);
        orders[original.OrderId] = current with { FilledQuantity = Math.Max(originalQuantity, proposedQuantity) };
        Assert.Empty(book.GetExposureReservations());
    }

    [Fact]
    public void Begin_UsesOrdinalIdentityAndPreservesAnAlreadyPendingAttempt()
    {
        var original = State();
        var book = new OrderAmendmentBook(Orders(original));
        var pending = book.Begin("Client", original, original with { Quantity = 20m }, "broker", null);
        book.Begin("client", original, original with { Quantity = 30m }, "other-broker", null);

        Assert.Throws<InvalidOperationException>(() =>
            book.Begin("Client", original, original with { Quantity = 40m }, "broker", null));
        Assert.Same(pending, book.GetPending("Client"));
        Assert.NotSame(pending, book.GetPending("client"));
        Assert.Null(book.GetPending("CLIENT"));
    }

    private static Dictionary<string, OrderState> Orders(OrderState state) => new(StringComparer.Ordinal)
    { [state.OrderId] = state };

    private static OrderState State() => new()
    {
        OrderId = "client",
        Symbol = "AAPL",
        Side = OrderSide.Buy,
        Type = OrderType.Limit,
        Quantity = 10m,
        LimitPrice = 100m,
        Status = OrderStatus.Accepted,
        FundAccountId = Guid.Parse("2b4ef72c-4f61-4b1f-9200-b4062e9f68cf"),
        StrategyId = "strategy",
        RunId = "run"
    };

    private static ExecutionReport Report(ExecutionReportType type) => new()
    {
        OrderId = "original-broker",
        ClientOrderId = "client",
        GatewayOrderId = "original-broker",
        Symbol = "AAPL",
        Side = OrderSide.Buy,
        ReportType = type,
        OrderStatus = OrderStatus.Accepted,
        OrderQuantity = 10m,
        Timestamp = DateTimeOffset.UtcNow
    };
}
