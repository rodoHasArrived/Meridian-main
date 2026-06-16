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
            StrategyId: "strategy-1",
            RunId: "run-override-approved"));

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
                ["manualOverrideId"] = manualOverride.OverrideId,
                ["runId"] = "run-override-approved"
            }
        });

        result.Success.Should().BeTrue();
        result.OrderState.Should().NotBeNull();
        result.OrderState!.Status.Should().Be(Meridian.Execution.Sdk.OrderStatus.Filled);

        var entries = await auditTrail.GetRecentAsync(10);
        entries.Should().Contain(entry =>
            entry.Action == "OrderSubmitted" &&
            entry.OrderId == result.OrderId &&
            entry.CorrelationId == "act-002" &&
            entry.RunId == "run-override-approved" &&
            entry.Reason == "ManualOverrideApplied" &&
            entry.Scope == "run:run-override-approved/strategy:strategy-1/symbol:AAPL" &&
            entry.Metadata != null &&
            entry.Metadata["manualOverrideId"] == manualOverride.OverrideId);
    }

    [Fact]
    public async Task PlaceOrderAsync_WithBypassOverrideForDifferentRun_RejectsOrderWhileCircuitBreakerIsOpen()
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
            Reason: "Run-specific closeout",
            CreatedBy: "ops",
            Symbol: "AAPL",
            StrategyId: "strategy-1",
            RunId: "run-allowed"));

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
                ["correlationId"] = "act-003",
                ["manualOverrideId"] = manualOverride.OverrideId,
                ["runId"] = "run-blocked"
            }
        });

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Operator halt");

        var entries = await auditTrail.GetRecentAsync(10);
        entries.Should().Contain(entry =>
            entry.Action == "OrderRejected" &&
            entry.Outcome == "Rejected" &&
            entry.RunId == "run-blocked" &&
            entry.CorrelationId == "act-003" &&
            entry.Symbol == "AAPL");
    }

    [Fact]
    public async Task PlaceOrderAsync_WhenPositionLimitExceeded_RejectsOrderWithControlAuditScope()
    {
        var tempRoot = CreateTempRoot();

        await using var auditTrail = new ExecutionAuditTrailService(
            new ExecutionAuditTrailOptions(Path.Combine(tempRoot, "audit")),
            NullLogger<ExecutionAuditTrailService>.Instance);

        var controls = new ExecutionOperatorControlService(
            new ExecutionOperatorControlOptions(Path.Combine(tempRoot, "controls")),
            NullLogger<ExecutionOperatorControlService>.Instance,
            auditTrail);

        await controls.SetSymbolPositionLimitAsync(
            "AAPL",
            maxPositionSize: 10m,
            changedBy: "risk",
            reason: "Event-risk limit");

        using var oms = new OrderManagementSystem(
            new ExecutionGateway(NullLogger<ExecutionGateway>.Instance),
            NullLogger<OrderManagementSystem>.Instance,
            operatorControls: controls,
            auditTrail: auditTrail,
            portfolioState: new StaticPortfolioState(new TestPosition("AAPL", 8)));

        var result = await oms.PlaceOrderAsync(new OrderRequest
        {
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = OrderType.Market,
            Quantity = 5m,
            StrategyId = "strategy-1",
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["actor"] = "risk",
                ["correlationId"] = "act-004",
                ["runId"] = "run-limit"
            }
        });

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("exceeds limit 10");

        var entries = await auditTrail.GetRecentAsync(10);
        entries.Should().Contain(entry =>
            entry.Action == "OrderRejected" &&
            entry.Outcome == "Rejected" &&
            entry.Reason == "POSITION_LIMIT_EXCEEDED" &&
            entry.Scope == "run:run-limit/strategy:strategy-1/symbol:AAPL" &&
            entry.Metadata != null &&
            entry.Metadata["controlDecision"] == "rejected-by-operator-controls" &&
            entry.Metadata["rejectCode"] == "POSITION_LIMIT_EXCEEDED");
    }

    [Fact]
    public async Task PlaceOrderAsync_WhenForceBlockOverrideMatchesRun_RejectsOrderWithManualControlAudit()
    {
        var tempRoot = CreateTempRoot();

        await using var auditTrail = new ExecutionAuditTrailService(
            new ExecutionAuditTrailOptions(Path.Combine(tempRoot, "audit")),
            NullLogger<ExecutionAuditTrailService>.Instance);

        var controls = new ExecutionOperatorControlService(
            new ExecutionOperatorControlOptions(Path.Combine(tempRoot, "controls")),
            NullLogger<ExecutionOperatorControlService>.Instance,
            auditTrail);

        var manualBlock = await controls.CreateManualOverrideAsync(new ManualOverrideRequest(
            Kind: ExecutionManualOverrideKinds.ForceBlockOrders,
            Reason: "Manual halt for broker incident",
            CreatedBy: "ops",
            Symbol: "AAPL",
            StrategyId: "strategy-1",
            RunId: "run-force-block"));

        using var oms = new OrderManagementSystem(
            new ExecutionGateway(NullLogger<ExecutionGateway>.Instance),
            NullLogger<OrderManagementSystem>.Instance,
            operatorControls: controls,
            auditTrail: auditTrail,
            portfolioState: new StaticPortfolioState());

        var result = await oms.PlaceOrderAsync(new OrderRequest
        {
            Symbol = "AAPL",
            Side = OrderSide.Sell,
            Type = OrderType.Market,
            Quantity = 2m,
            StrategyId = "strategy-1",
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["actor"] = "ops",
                ["correlationId"] = "act-005",
                ["runId"] = "run-force-block"
            }
        });

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain(manualBlock.OverrideId);

        var entries = await auditTrail.GetRecentAsync(10);
        entries.Should().Contain(entry =>
            entry.Action == "OrderRejected" &&
            entry.Outcome == "Rejected" &&
            entry.Reason == "MANUAL_FORCE_BLOCK" &&
            entry.Scope == "run:run-force-block/strategy:strategy-1/symbol:AAPL" &&
            entry.Message != null &&
            entry.Message.Contains("Manual halt for broker incident", StringComparison.OrdinalIgnoreCase) &&
            entry.Metadata != null &&
            entry.Metadata["rejectCode"] == "MANUAL_FORCE_BLOCK");
    }

    private static string CreateTempRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "Meridian.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class StaticPortfolioState : IPortfolioState
    {
        public StaticPortfolioState(params IPosition[] positions)
        {
            Positions = positions.ToDictionary(static position => position.Symbol, StringComparer.OrdinalIgnoreCase);
        }

        public decimal Cash => 100_000m;
        public decimal PortfolioValue => 100_000m;
        public decimal UnrealisedPnl => 0m;
        public decimal RealisedPnl => 0m;
        public IReadOnlyDictionary<string, IPosition> Positions { get; }
    }

    private sealed record TestPosition(
        string Symbol,
        long Quantity,
        decimal AverageCostBasis = 100m,
        decimal UnrealizedPnl = 0m,
        decimal RealizedPnl = 0m) : IPosition;
}
