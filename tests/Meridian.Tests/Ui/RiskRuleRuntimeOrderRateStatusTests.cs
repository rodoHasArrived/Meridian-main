using FluentAssertions;
using Meridian.Execution;
using Meridian.Execution.Services;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.Ui;

/// <summary>
/// The dashboard's order-rate count has to agree with the throttle it reports on. The OMS rolls a
/// rate slot back when the gateway returns a rejected report, so counting that submission here
/// would show the window as constrained while the rule has full capacity.
/// </summary>
public sealed class RiskRuleRuntimeOrderRateStatusTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "meridian-risk-rate-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task OrderRateStatus_CountsRoutedSubmissionsOnly()
    {
        await using var audit = new ExecutionAuditTrailService(
            Path.Combine(_root, "audit"),
            NullLogger<ExecutionAuditTrailService>.Instance);

        await audit.RecordAsync(SubmittedEntry(outcome: "Accepted"));
        await audit.RecordAsync(SubmittedEntry(outcome: "Rejected"));
        await audit.RecordAsync(SubmittedEntry(outcome: "Rejected"));

        var status = await BuildService(audit).GetStatusAsync("OrderRateThrottle");

        status.Should().NotBeNull();
        status!.CurrentValue.Should().Be(
            "1 orders/minute",
            "only the accepted submission actually consumed a slot");
    }

    /// <summary>
    /// A submission that threw after dispatch is recorded as rejected, but the OMS commits its
    /// reservation because the order may still have reached the venue. Skipping it here would show
    /// room the throttle does not have — the opposite error to counting a clean rejection.
    /// </summary>
    [Fact]
    public async Task OrderRateStatus_CountsAmbiguousSubmissionsThatKeptTheirSlot()
    {
        await using var audit = new ExecutionAuditTrailService(
            Path.Combine(_root, "audit"),
            NullLogger<ExecutionAuditTrailService>.Instance);

        await audit.RecordAsync(RejectedEntry(reason: OrderManagementSystem.AmbiguousSubmissionReason));
        await audit.RecordAsync(RejectedEntry(reason: OrderManagementSystem.AmbiguousSubmissionReason));
        // An ordinary rejection released its slot and must stay uncounted.
        await audit.RecordAsync(RejectedEntry(reason: null));

        var status = await BuildService(audit).GetStatusAsync("OrderRateThrottle");

        status!.CurrentValue.Should().Be("2 orders/minute");
    }

    [Fact]
    public async Task OrderRateStatus_WithOnlyRejectedSubmissions_IsHealthy()
    {
        await using var audit = new ExecutionAuditTrailService(
            Path.Combine(_root, "audit"),
            NullLogger<ExecutionAuditTrailService>.Instance);

        for (var i = 0; i < 5; i++)
        {
            await audit.RecordAsync(SubmittedEntry(outcome: "Rejected"));
        }

        var status = await BuildService(audit).GetStatusAsync("OrderRateThrottle");

        status!.CurrentValue.Should().Be("0 orders/minute");
        status.IsBreached.Should().BeFalse();
    }

    /// <summary>
    /// When a throttle is composed, the status reports what it holds rather than a reconstruction.
    /// The audit history here says one slot; the live rule says three, because two submissions are
    /// in flight and have no audit record yet. The gate's number wins.
    /// </summary>
    [Fact]
    public async Task OrderRateStatus_PrefersTheLiveThrottleOverAuditHistory()
    {
        await using var audit = new ExecutionAuditTrailService(
            Path.Combine(_root, "audit"),
            NullLogger<ExecutionAuditTrailService>.Instance);

        await audit.RecordAsync(SubmittedEntry(outcome: "Accepted"));

        var service = BuildService(audit);
        service.OrderRateUsageProbe = () => 3;

        var status = await service.GetStatusAsync("OrderRateThrottle");

        status!.CurrentValue.Should().Be("3 orders/minute");
    }

    /// <summary>
    /// Evaluate-all means one rejection can carry several findings, and only the highest-severity
    /// one reaches the entry's top-level message. A rule whose breach is recorded only in the
    /// per-violation metadata must still show as breached, or the panel reports "healthy" for a rule
    /// that did in fact block.
    /// </summary>
    [Fact]
    public async Task RuleStatus_SurfacesABreachRecordedOnlyInViolationMetadata()
    {
        await using var audit = new ExecutionAuditTrailService(
            Path.Combine(_root, "audit"),
            NullLogger<ExecutionAuditTrailService>.Instance);

        // Headline is the drawdown breach; the position-limit finding survives only in metadata.
        await audit.RecordAsync(new ExecutionAuditEntry(
            AuditId: Guid.NewGuid().ToString("N"),
            Category: "Order",
            Action: "OrderRejected",
            Outcome: "Rejected",
            OccurredAt: DateTimeOffset.UtcNow,
            Symbol: "AAPL",
            Reason: "DRAWDOWN_CIRCUIT_BREAKER_TRIPPED",
            Metadata: new Dictionary<string, string>
            {
                ["decisionSource"] = "risk",
                ["violation.count"] = "2",
                ["violation.0.rule"] = "DrawdownCircuitBreaker",
                ["violation.0.code"] = "DRAWDOWN_CIRCUIT_BREAKER_TRIPPED",
                ["violation.0.message"] = "Drawdown 12% exceeds 10% threshold.",
                ["violation.1.rule"] = "PositionLimit",
                ["violation.1.code"] = "POSITION_LIMIT_EXCEEDED",
                ["violation.1.message"] = "Position would reach 1,240 against a 1,000 limit."
            },
            Message: "Drawdown 12% exceeds 10% threshold."));

        var status = await BuildService(audit).GetStatusAsync("PositionLimit");

        status.Should().NotBeNull();
        status!.RecentViolations.Should().ContainSingle()
            .Which.Should().Contain("1,240", "the panel shows the position finding, not the headline");
    }

    private RiskRuleRuntimeService BuildService(ExecutionAuditTrailService audit)
    {
        var services = new ServiceCollection()
            .AddSingleton(audit)
            .BuildServiceProvider();

        // Point the snapshot at the temp root so the test never reads or writes the operator's
        // real risk-rule settings.
        return new RiskRuleRuntimeService(
            services,
            NullLogger<RiskRuleRuntimeService>.Instance,
            new RiskRuleRuntimeOptions(Path.Combine(_root, "risk-rules.json")));
    }

    private static ExecutionAuditEntry SubmittedEntry(string outcome) => new(
        AuditId: Guid.NewGuid().ToString("N"),
        Category: "Order",
        Action: "OrderSubmitted",
        Outcome: outcome,
        OccurredAt: DateTimeOffset.UtcNow,
        Symbol: "AAPL");

    private static ExecutionAuditEntry RejectedEntry(string? reason) => new(
        AuditId: Guid.NewGuid().ToString("N"),
        Category: "Order",
        Action: "OrderRejected",
        Outcome: "Rejected",
        OccurredAt: DateTimeOffset.UtcNow,
        Symbol: "AAPL",
        Reason: reason);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // A leftover temp directory must not fail the test run.
        }
    }
}
