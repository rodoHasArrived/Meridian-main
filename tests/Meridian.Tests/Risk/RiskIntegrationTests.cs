using FluentAssertions;
using Meridian.Execution;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Meridian.Tests.Risk;

public sealed class RiskIntegrationTests
{
    [Fact]
    public async Task PositionLimit_ControlRejectsOrder_AndAuditEntryIsRecorded()
    {
        var auditRoot = Path.Combine(Path.GetTempPath(), $"meridian-risk-audit-{Guid.NewGuid():N}");
        Directory.CreateDirectory(auditRoot);

        try
        {
            await using var auditTrail = new ExecutionAuditTrailService(
                new ExecutionAuditTrailOptions(auditRoot),
                NullLogger<ExecutionAuditTrailService>.Instance);
            var controls = new ExecutionOperatorControlService(
                ExecutionOperatorControlOptions.Default,
                NullLogger<ExecutionOperatorControlService>.Instance,
                auditTrail);
            await controls.SetDefaultPositionLimitAsync(10m, "test", "limit for integration test");

            var gateway = Substitute.For<IExecutionGateway>();
            gateway.GatewayId.Returns("test-gateway");
            gateway.IsConnected.Returns(true);

            var portfolio = new PaperTradingPortfolio(100_000m);
            using var oms = new OrderManagementSystem(
                gateway,
                NullLogger<OrderManagementSystem>.Instance,
                operatorControls: controls,
                auditTrail: auditTrail,
                portfolioState: portfolio);

            var result = await oms.PlaceOrderAsync(new OrderRequest
            {
                Symbol = "AAPL",
                Side = OrderSide.Buy,
                Type = OrderType.Market,
                Quantity = 25m,
                Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["actor"] = "risk-test"
                }
            });

            result.Success.Should().BeFalse();
            result.ErrorMessage.Should().NotBeNullOrWhiteSpace();
            portfolio.Positions.Should().BeEmpty("rejected orders must not mutate portfolio positions");

            var auditEntries = await auditTrail.GetRecentAsync(20);
            auditEntries.Should().Contain(entry =>
                string.Equals(entry.Action, "OrderRejected", StringComparison.Ordinal) &&
                string.Equals(entry.Symbol, "AAPL", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(auditRoot))
            {
                Directory.Delete(auditRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task CircuitBreaker_ControlRejectsOrder_AndGatewayIsNotInvoked()
    {
        var controlsRoot = Path.Combine(Path.GetTempPath(), $"meridian-risk-controls-{Guid.NewGuid():N}");
        Directory.CreateDirectory(controlsRoot);

        var gateway = Substitute.For<IExecutionGateway>();
        gateway.GatewayId.Returns("test-gateway");
        gateway.IsConnected.Returns(true);

        var controls = new ExecutionOperatorControlService(
            new ExecutionOperatorControlOptions(controlsRoot),
            NullLogger<ExecutionOperatorControlService>.Instance);
        await controls.SetCircuitBreakerAsync(true, "incident lock", "ops");

        try
        {
            using var oms = new OrderManagementSystem(
                gateway,
                NullLogger<OrderManagementSystem>.Instance,
                operatorControls: controls);

            var result = await oms.PlaceOrderAsync(new OrderRequest
            {
                Symbol = "MSFT",
                Side = OrderSide.Buy,
                Type = OrderType.Market,
                Quantity = 1m
            });

            result.Success.Should().BeFalse();
            await gateway.DidNotReceiveWithAnyArgs().SubmitOrderAsync(default!, default);
        }
        finally
        {
            if (Directory.Exists(controlsRoot))
            {
                Directory.Delete(controlsRoot, recursive: true);
            }
        }
    }
}
