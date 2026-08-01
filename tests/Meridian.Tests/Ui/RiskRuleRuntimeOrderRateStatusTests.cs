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
