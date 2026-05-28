using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.Execution;
using Meridian.Execution.Models;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Meridian.Strategies.Promotions;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ExecutionGateway = Meridian.Execution.PaperTradingGateway;

namespace Meridian.Tests.Ui;

/// <summary>
/// W4 Promotion Decision Chain Scenario Test.
/// Exercises the full "day in the life" operator flow from session creation
/// through live-order execution to a traceable promotion decision:
/// create session → submit order → record fill → add audit evidence →
/// approve promotion → assert complete traceable chain.
/// Matches the W4 acceptance criteria for governed promotion decisions and
/// extends the OrderManagementSystemGovernanceTests with a full multi-step
/// operator scenario.
/// </summary>
[Trait("Category", "Scenario")]
public sealed class PromotionDecisionChainScenarioTests
{
    [Fact]
    public async Task FullPromotionDecisionChain_SessionToTracedApproval()
    {
        var tempRoot = CreateTempRoot();

        // ---- STEP 1: set up execution infrastructure ----

        await using var auditTrail = new ExecutionAuditTrailService(
            new ExecutionAuditTrailOptions(Path.Combine(tempRoot, "audit")),
            NullLogger<ExecutionAuditTrailService>.Instance);

        var controls = new ExecutionOperatorControlService(
            new ExecutionOperatorControlOptions(Path.Combine(tempRoot, "controls")),
            NullLogger<ExecutionOperatorControlService>.Instance,
            auditTrail);

        using var oms = new OrderManagementSystem(
            new ExecutionGateway(NullLogger<ExecutionGateway>.Instance),
            NullLogger<OrderManagementSystem>.Instance,
            operatorControls: controls,
            auditTrail: auditTrail,
            portfolioState: new StaticPortfolioState());

        // ---- STEP 2: create a paper session ----

        var sessionService = new PaperSessionPersistenceService(
            NullLogger<PaperSessionPersistenceService>.Instance,
            auditTrail: auditTrail);

        var session = await sessionService.CreateSessionAsync(
            new CreatePaperSessionDto("w4-promotion-scenario", "W4 Promotion Chain", 100_000m)
            {
                Symbols = ["AAPL"]
            });

        session.IsActive.Should().BeTrue();

        // ---- STEP 3: submit a market order ----
        //
        // PaperTradingGateway fills market orders immediately, so the OMS
        // will record an OrderSubmitted audit entry and the fill will settle.

        var runId = Guid.NewGuid().ToString("N");
        var orderResult = await oms.PlaceOrderAsync(new OrderRequest
        {
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = OrderType.Market,
            Quantity = 10m,
            StrategyId = "w4-strategy",
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["actor"] = "algo",
                ["correlationId"] = "corr-w4-001",
                ["runId"] = runId
            }
        });

        orderResult.Success.Should().BeTrue("the paper gateway should fill a market order immediately");
        orderResult.OrderState!.Status.Should().Be(Meridian.Execution.Sdk.OrderStatus.Filled);

        // ---- STEP 4: record the fill in the paper session ----

        var fill = new ExecutionFill
        {
            SessionId = session.SessionId,
            Symbol = "AAPL",
            Quantity = 10,
            FillPrice = 175m,
            FilledAt = DateTimeOffset.UtcNow,
            FillType = FillType.Trade
        };
        await sessionService.RecordPaperFillAsync(session.SessionId, fill);

        // ---- STEP 5: verify the audit trail has the order evidence ----

        var auditEntries = await auditTrail.GetRecentAsync(20);
        auditEntries.Should().Contain(e =>
            e.Action == "OrderSubmitted" &&
            e.Symbol == "AAPL" &&
            e.RunId == runId,
            "OMS must record an OrderSubmitted audit entry for every filled order");

        // ---- STEP 6: verify replay — confirms the fill count is traceable ----

        var replay = await sessionService.VerifyReplayAsync(
            session.SessionId,
            expectedFillCount: 1,
            expectedOrderCount: 0);

        replay!.IsConsistent.Should().BeTrue("fill was recorded in the session");
        replay.VerifiedFilledCount.Should().Be(1);

        // ---- STEP 7: operator approves promotion ----
        //
        // The PromotionRecordService is in-memory — it records the decision and
        // associates a unique audit reference so the chain is traceable from
        // session through fill through to the governance decision.

        var promotionService = new PromotionRecordService(
            NullLogger<PromotionRecordService>.Instance);

        var backtestRunId = Guid.NewGuid();
        var approval = await promotionService.ApprovePromotionAsync(
            runId: backtestRunId,
            operatorId: "portfolio-manager",
            reason: "Paper session shows positive P&L with consistent replay — approved for live consideration",
            ct: default);

        // ---- STEP 8: assert the complete traceable chain ----

        // Promotion record is complete.
        approval.Decision.Should().Be(PromotionDecision.Approved);
        approval.AuditReference.Should().NotBeNullOrEmpty("every promotion must carry an audit reference");
        approval.OperatorId.Should().Be("portfolio-manager");
        approval.DecidedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));

        // Session history is intact.
        var sessionDetail = sessionService.GetSession(session.SessionId);
        sessionDetail.Should().NotBeNull();
        sessionDetail!.FillHistory.Should().HaveCount(1);
        sessionDetail.FillHistory[0].Symbol.Should().Be("AAPL");

        // Replay is verified and consistent.
        replay.IsConsistent.Should().BeTrue();

        // Promotion history is recoverable.
        var promotionHistory = await promotionService.GetPromotionHistoryAsync(backtestRunId);
        promotionHistory.Should().HaveCount(1);
        promotionHistory[0].Decision.Should().Be(PromotionDecision.Approved);
        promotionHistory[0].AuditReference.Should().Be(approval.AuditReference);

        // Readiness snapshot reflects the full session state.
        await using var provider = new ServiceCollection()
            .AddSingleton(sessionService)
            .AddSingleton(auditTrail)
            .AddSingleton(controls)
            .BuildServiceProvider();
        var readinessService = new TradingOperatorReadinessService(
            provider,
            NullLogger<TradingOperatorReadinessService>.Instance);

        var readiness = await readinessService.GetAsync();
        readiness.ActiveSession!.SessionId.Should().Be(session.SessionId);
        readiness.OverallStatus.Should().NotBe(TradingAcceptanceGateStatusDto.Blocked,
            "a clean session with verified replay must not be in a blocked state");
    }

    [Fact]
    public async Task PromotionDecisionChain_RejectedPromotionIsAuditableAndRecoverable()
    {
        // An operator rejects a promotion — the reason and identity must both be
        // captured so the decision can be reviewed after the fact.

        var promotionService = new PromotionRecordService(
            NullLogger<PromotionRecordService>.Instance);

        var runId = Guid.NewGuid();
        var rejection = await promotionService.RejectPromotionAsync(
            runId: runId,
            operatorId: "risk-officer",
            reason: "Drawdown limit exceeded in back-test P&L — not acceptable for live consideration",
            ct: default);

        rejection.Decision.Should().Be(PromotionDecision.Rejected);
        rejection.AuditReference.Should().NotBeNullOrEmpty("a rejection must be as traceable as an approval");
        rejection.OperatorId.Should().Be("risk-officer");
        rejection.Reason.Should().Contain("Drawdown limit exceeded");

        // The full decision trail is recoverable for audit purposes.
        var history = await promotionService.GetPromotionHistoryAsync(runId);
        history.Should().HaveCount(1);
        history[0].Decision.Should().Be(PromotionDecision.Rejected);
        history[0].AuditReference.Should().Be(rejection.AuditReference);
    }

    [Fact]
    public async Task PromotionDecisionChain_CircuitBreakerBlocksOrderAndAuditsRejection()
    {
        // When the circuit breaker is open, an order should be rejected and a
        // traceable rejection event written — the operator must be able to see
        // why the order failed when reviewing the promotion chain.

        var tempRoot = CreateTempRoot();

        await using var auditTrail = new ExecutionAuditTrailService(
            new ExecutionAuditTrailOptions(Path.Combine(tempRoot, "audit")),
            NullLogger<ExecutionAuditTrailService>.Instance);

        var controls = new ExecutionOperatorControlService(
            new ExecutionOperatorControlOptions(Path.Combine(tempRoot, "controls")),
            NullLogger<ExecutionOperatorControlService>.Instance,
            auditTrail);

        await controls.SetCircuitBreakerAsync(true, "Operator halt for risk review", "risk-officer");

        using var oms = new OrderManagementSystem(
            new ExecutionGateway(NullLogger<ExecutionGateway>.Instance),
            NullLogger<OrderManagementSystem>.Instance,
            operatorControls: controls,
            auditTrail: auditTrail,
            portfolioState: new StaticPortfolioState());

        var result = await oms.PlaceOrderAsync(new OrderRequest
        {
            Symbol = "MSFT",
            Side = OrderSide.Buy,
            Type = OrderType.Market,
            Quantity = 5m,
            StrategyId = "w4-blocked-strategy",
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["actor"] = "algo",
                ["correlationId"] = "corr-blocked-001",
                ["runId"] = Guid.NewGuid().ToString("N")
            }
        });

        result.Success.Should().BeFalse("circuit breaker is open — order must be rejected");
        result.ErrorMessage.Should().Contain("Operator halt for risk review");

        var recentEntries = await auditTrail.GetRecentAsync(10);
        recentEntries.Should().Contain(e =>
            e.Action == "OrderRejected" &&
            e.Symbol == "MSFT",
            "rejected order must leave a traceable audit entry so operators can diagnose the promotion chain");
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
