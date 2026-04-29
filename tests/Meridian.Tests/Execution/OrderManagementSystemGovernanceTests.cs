using FluentAssertions;
using Meridian.Execution;
using Meridian.Execution.Models;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using ExecutionGateway = Meridian.Execution.PaperTradingGateway;

namespace Meridian.Tests.Execution;

public sealed class OrderManagementSystemGovernanceTests
{
    [Fact]
    public async Task PlaceOrderAsync_WhenCircuitBreakerOpen_RejectsOrderAndPersistsAudit()
    {
        var tempRoot = CreateTempRoot();

        await using var auditTrail = new ExecutionAuditTrailService(
            new ExecutionAuditTrailOptions(Path.Combine(tempRoot, "audit")),
            NullLogger<ExecutionAuditTrailService>.Instance);

        var controls = new ExecutionOperatorControlService(
            new ExecutionOperatorControlOptions(Path.Combine(tempRoot, "controls")),
            NullLogger<ExecutionOperatorControlService>.Instance,
            auditTrail);

        await controls.SetCircuitBreakerAsync(
            isOpen: true,
            reason: "Operator halt",
            changedBy: "ops");

        using var oms = new OrderManagementSystem(
            new ExecutionGateway(NullLogger<ExecutionGateway>.Instance),
            NullLogger<OrderManagementSystem>.Instance,
            operatorControls: controls,
            auditTrail: auditTrail,
            portfolioState: new StaticPortfolioState());

        var result = await oms.PlaceOrderAsync(new OrderRequest
        {
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = OrderType.Market,
            Quantity = 10m,
            StrategyId = "strategy-1",
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["actor"] = "ops",
                ["correlationId"] = "act-001",
                ["runId"] = "run-001"
            }
        });

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Operator halt");

        var entries = await auditTrail.GetRecentAsync(10);
        entries.Should().Contain(entry =>
            entry.Action == "OrderRejected" &&
            entry.Outcome == "Rejected" &&
            entry.RunId == "run-001" &&
            entry.CorrelationId == "act-001" &&
            entry.Symbol == "AAPL");
    }

    [Fact]
    public async Task PlaceOrderAsync_WithBypassOverride_AllowsOrderWhileCircuitBreakerIsOpen()
    {
        var tempRoot = CreateTempRoot();

        await using var auditTrail = new ExecutionAuditTrailService(
            new ExecutionAuditTrailOptions(Path.Combine(tempRoot, "audit")),
            NullLogger<ExecutionAuditTrailService>.Instance);

        var controls = new ExecutionOperatorControlService(
            new ExecutionOperatorControlOptions(Path.Combine(tempRoot, "controls")),
            NullLogger<ExecutionOperatorControlService>.Instance,
            auditTrail);

        var manualOverride = await controls.CreateManualOverrideAsync(new ManualOverrideRequest(
            Kind: ExecutionManualOverrideKinds.BypassOrderControls,
            Reason: "Operator approved emergency close",
            CreatedBy: "ops",
            Symbol: "AAPL",
            StrategyId: "strategy-1"));

        await controls.SetCircuitBreakerAsync(
            isOpen: true,
            reason: "Operator halt",
            changedBy: "ops");

        using var oms = new OrderManagementSystem(
            new ExecutionGateway(NullLogger<ExecutionGateway>.Instance),
            NullLogger<OrderManagementSystem>.Instance,
            operatorControls: controls,
            auditTrail: auditTrail,
            portfolioState: new StaticPortfolioState());

        var result = await oms.PlaceOrderAsync(new OrderRequest
        {
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = OrderType.Market,
            Quantity = 1m,
            StrategyId = "strategy-1",
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["actor"] = "ops",
                ["correlationId"] = "act-002",
                ["manualOverrideId"] = manualOverride.OverrideId
            }
        });

        result.Success.Should().BeTrue();
        result.OrderState.Should().NotBeNull();
        result.OrderState!.Status.Should().Be(Meridian.Execution.Sdk.OrderStatus.Filled);

        var entries = await auditTrail.GetRecentAsync(10);
        entries.Should().Contain(entry =>
            entry.Action == "OrderSubmitted" &&
            entry.OrderId == result.OrderId &&
            entry.CorrelationId == "act-002");
    }

    private static string CreateTempRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "Meridian.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class StaticPortfolioState : IPortfolioState
    {
        public decimal Cash => 100_000m;
        public decimal PortfolioValue => 100_000m;
        public decimal UnrealisedPnl => 0m;
        public decimal RealisedPnl => 0m;
        public IReadOnlyDictionary<string, IPosition> Positions { get; } =
            new Dictionary<string, IPosition>(StringComparer.OrdinalIgnoreCase);
    }
}
