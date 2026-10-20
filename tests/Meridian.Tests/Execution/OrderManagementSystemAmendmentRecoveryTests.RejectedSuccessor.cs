using FluentAssertions;
using Meridian.Execution;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meridian.Tests.Execution;

public sealed partial class OrderManagementSystemAmendmentRecoveryTests
{
    [Fact]
    public async Task Recovery_RejectedSuccessorWithProposedTerms_SelectsTheOriginalOutcome()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"rejected-successor-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var store = new FileBrokerageOrderRecoveryStore(Path.Combine(directory, "orders.json"), "alpaca");
            store.BindScope("paper:account");
            var gateway = new PendingAmendmentGateway();
            await using var oms = new OrderManagementSystem(gateway, NullLogger<OrderManagementSystem>.Instance,
                recoveryStore: store, portfolioState: new PaperTradingPortfolio(100_000m));
            (await oms.PlaceOrderAsync(new OrderRequest
            {
                ClientOrderId = "local",
                Symbol = "AAPL",
                Side = OrderSide.Buy,
                Type = OrderType.Limit,
                Quantity = 10m,
                LimitPrice = 100m
            })).Success.Should().BeTrue();
            (await oms.ModifyOrderAsync("local", new OrderModification { NewQuantity = 20m })).Success.Should().BeFalse();
            oms.GetOrder("local")!.Quantity.Should().Be(10m, "the amendment is still pending");
            oms.GetPendingAmendmentForRecovery("local").Should().NotBeNull()
                .And.Match<PendingOrderAmendment>(p => p.CandidateBrokerOrderId == "replacement-broker");

            // The broker reports the replacement itself as rejected; its echoed terms are the proposal's.
            await oms.ReconcileRecoveryOrderAsync(PendingAmendmentGateway.Report(
                ExecutionReportType.ModificationRejected, OrderStatus.Rejected, 2m) with
            {
                OrderId = "replacement-broker",
                GatewayOrderId = "replacement-broker",
                ReplacesOrderId = "original-broker",
                OrderQuantity = 20m
            });

            oms.GetOrder("local")!.Quantity.Should().Be(10m, "a rejected successor never makes the proposed terms live");
            store.Load().Single().PendingAmendment.Should().BeNull();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
