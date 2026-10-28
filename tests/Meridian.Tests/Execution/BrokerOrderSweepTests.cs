using Meridian.Execution.OrderManagement;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Meridian.Tests.TestHelpers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using OrderStatus = Meridian.Execution.Sdk.OrderStatus;

namespace Meridian.Tests.Execution;

public sealed class BrokerOrderSweepTests
{
    [Fact]
    public async Task SnapshotResidualOrders_UsesCurrentBrokerIdentityAfterEnumerationCompletes()
    {
        var gateway = Substitute.For<IBrokerageGateway>();
        var listing = new TaskCompletionSource<IReadOnlyList<BrokerOrder>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        gateway.GetOpenOrdersAsync(Arg.Any<CancellationToken>()).Returns(listing.Task);
        var identities = new OrderBrokerIdentityIndex();
        identities.Confirm("local", "before-replacement");
        var sweep = CreateSweep(gateway, identities);

        var snapshot = sweep.SnapshotResidualOrdersAsync([Tracked("local")], CancellationToken.None);
        Assert.False(snapshot.IsCompleted);
        identities.Confirm("local", "after-replacement");
        listing.SetResult([Broker("after-replacement")]);

        var result = await snapshot.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Empty(result.ResidualOrders);
        Assert.Equal("after-replacement", result.BrokerCancellationIds["local"]);
        Assert.Null(result.EnumerationError);
    }

    [Fact]
    public async Task SnapshotResidualOrders_KeepsAmbiguousAndPriorAliasesResidualWithoutConfusingClientIds()
    {
        var gateway = Substitute.For<IBrokerageGateway>();
        var identities = new OrderBrokerIdentityIndex();
        identities.Confirm("mapped", "old-broker");
        identities.Confirm("mapped", "current-broker");
        identities.Confirm("ambiguous-a", "shared-broker");
        identities.Confirm("ambiguous-b", "shared-broker");
        IReadOnlyList<BrokerOrder> rows =
        [
            Broker("client-shaped-broker", "external"),
            Broker("actual-broker", "client-shaped-broker"),
            Broker("current-broker"),
            Broker("old-broker"),
            Broker("shared-broker"),
            Broker("old-broker"),
            Broker("terminal") with { Status = OrderStatus.Cancelled }
        ];
        gateway.GetOpenOrdersAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(rows));
        var sweep = CreateSweep(gateway, identities);

        var result = await sweep.SnapshotResidualOrdersAsync(
            [Tracked("client-shaped-broker"), Tracked("mapped"), Tracked("ambiguous-a"), Tracked("ambiguous-b")],
            CancellationToken.None);

        Assert.Equal(new[] { "client-shaped-broker", "old-broker", "shared-broker" },
            result.ResidualOrders.Select(order => order.OrderId));
        Assert.Equal(2, result.BrokerCancellationIds.Count);
        Assert.Equal("actual-broker", result.BrokerCancellationIds["client-shaped-broker"]);
        Assert.Equal("current-broker", result.BrokerCancellationIds["mapped"]);
        Assert.Null(result.EnumerationError);
    }

    [Fact]
    public async Task SnapshotResidualOrders_PrefersProvenClientMatchAndPreservesOrdinalIds()
    {
        var gateway = Substitute.For<IBrokerageGateway>();
        var identities = new OrderBrokerIdentityIndex();
        identities.Confirm("active-owner", "broker-a");
        IReadOnlyList<BrokerOrder> rows =
        [
            Broker("broker-a", "client-owner"),
            Broker("case-sensitive", "CLIENT-OWNER"),
            Broker("CASE-SENSITIVE", "external")
        ];
        gateway.GetOpenOrdersAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(rows));

        var result = await CreateSweep(gateway, identities).SnapshotResidualOrdersAsync(
            [Tracked("active-owner"), Tracked("client-owner")], CancellationToken.None);

        Assert.Equal("broker-a", Assert.Single(result.BrokerCancellationIds).Value);
        Assert.True(result.BrokerCancellationIds.ContainsKey("client-owner"));
        Assert.Equal(new[] { "case-sensitive", "CASE-SENSITIVE" },
            result.ResidualOrders.Select(order => order.OrderId));
    }

    [Fact]
    public async Task Snapshots_KeepInitialFailureSeparateFromFreshConvergenceEvidence()
    {
        var gateway = Substitute.For<IBrokerageGateway>();
        IReadOnlyList<BrokerOrder> finalRows =
        [
            Broker("pending") with { Status = OrderStatus.PendingCancel },
            Broker("pending") with { Status = OrderStatus.PendingCancel },
            Broker("partial") with { Status = OrderStatus.PartiallyFilled },
            Broker("new") with { Status = OrderStatus.PendingNew },
            Broker("filled") with { Status = OrderStatus.Filled },
            Broker("rejected") with { Status = OrderStatus.Rejected }
        ];
        gateway.GetOpenOrdersAsync(Arg.Any<CancellationToken>()).Returns(
            Task.FromException<IReadOnlyList<BrokerOrder>>(new IOException("Initial broker view unavailable.")),
            Task.FromResult(finalRows),
            Task.FromException<IReadOnlyList<BrokerOrder>>(new IOException("Final broker view unavailable.")));
        var sweep = CreateSweep(gateway);

        var initial = await sweep.SnapshotResidualOrdersAsync([Tracked("local")], CancellationToken.None);
        var verified = await sweep.SnapshotWorkingOrdersAsync(CancellationToken.None);
        var unavailable = await sweep.SnapshotWorkingOrdersAsync(CancellationToken.None);

        Assert.Equal("Initial broker view unavailable.", initial.EnumerationError);
        Assert.Empty(initial.ResidualOrders);
        Assert.Empty(initial.BrokerCancellationIds);
        Assert.Null(verified.EnumerationError);
        Assert.Equal(new[] { "pending", "partial", "new" }, verified.WorkingOrders.Select(order => order.OrderId));
        Assert.Empty(unavailable.WorkingOrders);
        Assert.Equal("Final broker view unavailable.", unavailable.EnumerationError);
    }

    [Fact]
    public async Task CancelResidualOrder_UsesExplicitBrokerNamespaceAndRetainsOutcomeEvidence()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-broker-sweep", Guid.NewGuid().ToString("N"));
        try
        {
            var gateway = Substitute.For<IBrokerageGateway, IExplicitOrderCancellationGateway>();
            gateway.GatewayId.Returns("broker-sweep-test");
            var explicitGateway = (IExplicitOrderCancellationGateway)gateway;
            var order = Broker("broker-id", "client-id");
            explicitGateway.CancelOrderAsync(Arg.Any<OrderCancellationIdentifier>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(Cancelled(order.OrderId)));
            await using var audit = new ExecutionAuditTrailService(root, NullLogger<ExecutionAuditTrailService>.Instance);
            var sweep = CreateSweep(gateway, auditTrail: audit);

            var failure = await sweep.CancelResidualOrderAsync(order, CancellationToken.None);

            Assert.Null(failure);
            await explicitGateway.Received(1).CancelOrderAsync(
                new OrderCancellationIdentifier("broker-id", OrderCancellationIdentifierKind.BrokerOrderId),
                CancellationToken.None);
            await gateway.DidNotReceive().CancelOrderAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
            var evidence = Assert.Single(await audit.GetAllAsync());
            Assert.Equal("OrderCancelled", evidence.Action);
            Assert.Equal("Cancelled", evidence.Outcome);
            Assert.Equal("broker-id", evidence.OrderId);
            Assert.Equal("broker-sweep-test", evidence.BrokerName);
            Assert.Equal("Cancel-all cancelled a broker-side order the in-memory book did not track.", evidence.Message);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task CancelResidualOrder_LegacyGatewayReceivesBrokerValueAndPreservesRefusalOrException()
    {
        var gateway = Substitute.For<IBrokerageGateway>();
        gateway.CancelOrderAsync("broker-id", Arg.Any<CancellationToken>()).Returns(
            Task.FromResult(Cancelled("broker-id") with
            {
                OrderStatus = OrderStatus.PendingCancel,
                ReportType = ExecutionReportType.CancelRejected,
                RejectReason = "Cancellation is still pending."
            }),
            Task.FromException<ExecutionReport>(new IOException("Cancellation transport failed.")));
        var sweep = CreateSweep(gateway);
        var order = Broker("broker-id", "client-id");

        var refused = await sweep.CancelResidualOrderAsync(order, CancellationToken.None);
        var failed = await sweep.CancelResidualOrderAsync(order, CancellationToken.None);

        Assert.Equal(new KillSwitchSweepFailure("broker-id", "AAPL", "Cancellation is still pending."), refused);
        Assert.Equal(new KillSwitchSweepFailure("broker-id", "AAPL", "Cancellation transport failed."), failed);
        await gateway.Received(2).CancelOrderAsync("broker-id", CancellationToken.None);
        await gateway.DidNotReceive().CancelOrderAsync("client-id", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CancelResidualOrder_AuditFailureCannotChangeConfirmedBrokerOutcome()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-broker-sweep", Guid.NewGuid().ToString("N"));
        try
        {
            var gateway = Substitute.For<IExecutionGateway>();
            gateway.CancelOrderAsync("broker-id", Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(Cancelled("broker-id")));
            await using var audit = new ExecutionAuditTrailService(root, NullLogger<ExecutionAuditTrailService>.Instance);
            var auditFailure = new IOException("Audit evidence could not be prepared.");
            var logger = new RecordingLogger<BrokerOrderSweep>();
            var auditWriter = new OrderExecutionAudit(audit, () => throw auditFailure, logger);
            var sweep = new BrokerOrderSweep(gateway, new OrderBrokerIdentityIndex(), auditWriter, logger);

            var failure = await sweep.CancelResidualOrderAsync(Broker("broker-id"), CancellationToken.None);

            Assert.Null(failure);
            var logged = Assert.Single(logger.Entries);
            Assert.Equal(LogLevel.Error, logged.LogLevel);
            Assert.Same(auditFailure, logged.Exception);
            Assert.Contains("could not audit the broker-side cancellation outcome", logged.Message);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static BrokerOrderSweep CreateSweep(
        IExecutionGateway gateway,
        OrderBrokerIdentityIndex? identities = null,
        ExecutionAuditTrailService? auditTrail = null) => new(
            gateway,
            identities ?? new OrderBrokerIdentityIndex(),
            new OrderExecutionAudit(auditTrail, () => gateway.GatewayId, NullLogger.Instance),
            NullLogger.Instance);

    private static OrderState Tracked(string orderId) => new()
    {
        OrderId = orderId,
        Symbol = "AAPL",
        Side = OrderSide.Buy,
        Type = OrderType.Limit,
        Quantity = 10m,
        Status = OrderStatus.Accepted
    };

    private static BrokerOrder Broker(string orderId, string? clientOrderId = null) => new()
    {
        OrderId = orderId,
        ClientOrderId = clientOrderId,
        Symbol = "AAPL",
        Side = OrderSide.Buy,
        Type = OrderType.Limit,
        Quantity = 10m,
        Status = OrderStatus.Accepted
    };

    private static ExecutionReport Cancelled(string orderId) => new()
    {
        OrderId = orderId,
        Symbol = "AAPL",
        Side = OrderSide.Buy,
        OrderStatus = OrderStatus.Cancelled,
        ReportType = ExecutionReportType.Cancelled,
        Timestamp = DateTimeOffset.UtcNow
    };
}
