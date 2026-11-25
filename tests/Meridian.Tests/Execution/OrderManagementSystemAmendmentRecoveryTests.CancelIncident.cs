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
    public async Task SessionHalt_TerminalPredecessorWithUnconfirmedReplacement_RetainsSuccessorExposureInIncident()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var directory = Path.Combine(Path.GetTempPath(), $"cancel-pending-successor-{Guid.NewGuid():N}");
        try
        {
            await using var audit = new ExecutionAuditTrailService(Path.Combine(directory, "audit"),
                NullLogger<ExecutionAuditTrailService>.Instance);
            await using var gateway = new RecoverableAmendmentGateway { HoldModification = true };
            await using var oms = new OrderManagementSystem(gateway, NullLogger<OrderManagementSystem>.Instance,
                auditTrail: audit, portfolioState: new PaperTradingPortfolio(100_000m),
                recoveryStore: new FileBrokerageOrderRecoveryStore(Path.Combine(directory, "orders.json"), "alpaca"));
            await oms.StartAsync(timeout.Token);
            (await oms.PlaceOrderAsync(RecoveryRequest(), timeout.Token)).Success.Should().BeTrue();
            var modification = oms.ModifyOrderAsync("local", new OrderModification { NewQuantity = 20m }, timeout.Token);
            await gateway.ModificationEntered.Task.WaitAsync(timeout.Token);
            await using (var changes = oms.SubscribeOrderStateChanges())
            {
                await gateway.Reports.Writer.WriteAsync(RecoverableAmendmentGateway.Report(
                    ExecutionReportType.Cancelled, OrderStatus.Cancelled, 2m, 10m, 100m), timeout.Token);
                await foreach (var state in changes.ReadAllAsync(timeout.Token))
                    if (state.OrderId == "local" && state.Status == OrderStatus.Cancelled)
                        break;
            }
            gateway.ReleaseModification.TrySetResult();
            (await modification).Success.Should().BeFalse();
            oms.GetOpenOrders().Should().BeEmpty("the original order is terminal");
            oms.GetExposureReservingOrders().Should().ContainSingle().Which.Quantity.Should().Be(20m);

            var sweep = await oms.CancelAllAsync(timeout.Token).WaitAsync(timeout.Token);
            var incident = (await audit.GetCancelAllIncidentAsync(sweep.IncidentId!, RecoveryAccountId, timeout.Token))!;
            var order = incident.Orders.Should().ContainSingle().Which;

            order.Disposition.Should().Be(CancelAllOrderDisposition.Unknown);
            order.HasUnconfirmedReplacement.Should().BeTrue();
            order.CandidateBrokerOrderId.Should().Be("candidate-broker");
            order.CandidateClientOrderId.Should().Be("attempted-ref");
            order.RemainingQuantity.Should().Be(18m, "the possible twenty-share successor has two retained fills");
            order.ResidualNotional.Should().BeNull("a pending successor has no settled valuation basis");
            order.RequiresReconciliation.Should().BeTrue();
            sweep.Requested.Should().Be(1, "a terminal predecessor cannot remove its pending successor from the sweep");
            sweep.Cancelled.Should().Be(0);
            sweep.RequiresOperatorAction.Should().BeTrue();
            oms.GetPendingAmendmentForRecovery("local").Should().NotBeNull();
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }
}
