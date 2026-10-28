using FluentAssertions;
using Meridian.Execution.OrderManagement;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Xunit;

namespace Meridian.Tests.Execution;

public sealed class OrderBrokerIdentityIndexTests
{
    [Fact]
    public void Restore_AdditionalAliasConflictsWithRetiredIdentity_RefusesAmbiguousOwnership()
    {
        var identities = new OrderBrokerIdentityIndex();
        identities.Restore(new BrokerageOrderTombstone("retired", OrderStatus.Filled, 10m, 10m)
        { BrokerOrderAliases = ["retired-broker"] });
        var retained = new RetainedBrokerageOrder(State("working"), "working-broker", true)
        { BrokerOrderAliases = ["working-broker", "retired-broker"] };

        var restore = () => identities.Restore(retained);

        restore.Should().Throw<InvalidDataException>()
            .WithMessage("Retained broker aliases conflict with another order identity.");
        identities.TryResolveAlias("retired-broker", out var owner).Should().BeTrue();
        owner.Should().Be("retired");
    }

    [Fact]
    public void Restore_ActiveAliasCollision_PreservesExistingRestorationPrecedence()
    {
        var identities = new OrderBrokerIdentityIndex();
        identities.Restore(new BrokerageOrderTombstone("retired", OrderStatus.Filled, 10m, 10m)
        { BrokerOrderAliases = ["shared-broker"] });

        // Existing snapshots give the retained active identity precedence over an earlier
        // alias; changing malformed-snapshot validation is separate from this extraction.
        identities.Restore(new RetainedBrokerageOrder(State("working"), "shared-broker", true)
        { BrokerOrderAliases = ["shared-broker"] });

        identities.TryResolveAlias("shared-broker", out var owner).Should().BeTrue();
        owner.Should().Be("working");
        identities.TryGetActiveId("working", out var active).Should().BeTrue();
        active.Should().Be("shared-broker");
    }

    [Fact]
    public void Restore_RetiredAliasAppearsTwice_RefusesDuplicateEvidence()
    {
        var identities = new OrderBrokerIdentityIndex();
        var tombstone = new BrokerageOrderTombstone("retired", OrderStatus.Filled, 10m, 10m)
        { BrokerOrderAliases = ["broker", "broker"] };

        var restore = () => identities.Restore(tombstone);

        restore.Should().Throw<InvalidDataException>()
            .WithMessage("Retired broker aliases conflict with another order identity.");
    }

    [Fact]
    public void ForgetActiveId_PreservesEveryReplacementAliasWithOrdinalIdentity()
    {
        var identities = new OrderBrokerIdentityIndex();
        identities.Confirm("client", "broker-A");
        identities.Remember("client", Report("broker-B", ExecutionReportType.Modified, OrderStatus.Accepted) with
        { ReplacesOrderId = "broker-A" });
        identities.Confirm("other-client", "broker-a");

        identities.ForgetActiveId("client");

        identities.TryGetActiveId("client", out _).Should().BeFalse();
        identities.GetAliases("client").Should().Equal("broker-A", "broker-B");
        identities.TryResolveAlias("broker-A", out var owner).Should().BeTrue();
        owner.Should().Be("client");
        identities.TryResolveAlias("broker-a", out var otherOwner).Should().BeTrue();
        otherOwner.Should().Be("other-client");
        identities.ContainsAlias("BROKER-A").Should().BeFalse();
    }

    [Theory]
    [InlineData(0, OrderStatus.Accepted)]
    [InlineData(3, OrderStatus.PartiallyFilled)]
    [InlineData(10, OrderStatus.Filled)]
    public void Normalize_DelayedPredecessorCancellation_PreservesReplacementAndAdvancesOnlyExecution(
        int filledQuantity, OrderStatus expectedStatus)
    {
        var identities = new OrderBrokerIdentityIndex();
        identities.Confirm("client", "broker-A");
        identities.Remember("client", Report("broker-B", ExecutionReportType.Modified, OrderStatus.Accepted) with
        { ReplacesOrderId = "broker-A" });
        var current = State("client");
        var delayed = Report("broker-A", ExecutionReportType.Cancelled, OrderStatus.Cancelled) with
        {
            ClientOrderId = "stale-client",
            OrderQuantity = 6m,
            FilledQuantity = filledQuantity
        };

        var normalized = identities.Normalize(delayed, id => id == current.OrderId ? current : null);
        identities.Remember("client", normalized);

        normalized.ClientOrderId.Should().Be("client");
        normalized.OrderQuantity.Should().Be(10m);
        normalized.OrderStatus.Should().Be(expectedStatus);
        normalized.FilledQuantity.Should().Be(filledQuantity);
        identities.TryGetActiveId("client", out var active).Should().BeTrue();
        active.Should().Be("broker-B");
    }

    [Fact]
    public void Normalize_RejectedSuccessor_KeepsPredecessorWorkingAndReservesRejectedAlias()
    {
        var identities = new OrderBrokerIdentityIndex();
        identities.Confirm("client", "working-broker");
        var current = State("client") with { FilledQuantity = 2m, Status = OrderStatus.PartiallyFilled };
        var rejected = Report("rejected-successor", ExecutionReportType.Rejected, OrderStatus.Rejected) with
        {
            ReplacesOrderId = "working-broker",
            OrderQuantity = 20m,
            FilledQuantity = 2m
        };

        var normalized = identities.Normalize(rejected, _ => current);
        identities.Remember("client", normalized);

        normalized.ClientOrderId.Should().Be("client");
        normalized.ReportType.Should().Be(ExecutionReportType.ModificationRejected);
        normalized.OrderStatus.Should().Be(OrderStatus.PartiallyFilled);
        normalized.OrderQuantity.Should().Be(10m);
        identities.TryGetActiveId("client", out var active).Should().BeTrue();
        active.Should().Be("working-broker");
        identities.TryResolveAlias("rejected-successor", out var owner).Should().BeTrue();
        owner.Should().Be("client");
    }

    [Fact]
    public void Remember_ProvisionalReplacement_DoesNotClaimCandidateIdentity()
    {
        var identities = new OrderBrokerIdentityIndex();
        identities.Confirm("client", "working-broker");

        identities.Remember("client", Report("candidate", ExecutionReportType.ModificationPending, OrderStatus.Accepted) with
        { ReplacesOrderId = "working-broker", CandidateGatewayOrderId = "candidate" });

        identities.TryGetActiveId("client", out var active).Should().BeTrue();
        active.Should().Be("working-broker");
        identities.ContainsAlias("candidate").Should().BeFalse();
    }

    private static OrderState State(string orderId) => new()
    {
        OrderId = orderId,
        Symbol = "AAPL",
        Side = OrderSide.Buy,
        Type = OrderType.Limit,
        Quantity = 10m,
        LimitPrice = 100m,
        Status = OrderStatus.Accepted
    };

    private static ExecutionReport Report(string brokerOrderId, ExecutionReportType type, OrderStatus status) => new()
    {
        OrderId = brokerOrderId,
        GatewayOrderId = brokerOrderId,
        Symbol = "AAPL",
        Side = OrderSide.Buy,
        ReportType = type,
        OrderStatus = status,
        OrderQuantity = 10m
    };
}
