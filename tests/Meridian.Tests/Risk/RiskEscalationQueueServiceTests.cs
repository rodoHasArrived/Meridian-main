using FluentAssertions;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meridian.Tests.Risk;

/// <summary>
/// Covers the governed-approval queue: parked orders, operator resolution, and the
/// one-shot fingerprint-matched approval consumption that releases an order.
/// </summary>
public sealed class RiskEscalationQueueServiceTests
{
    private static RiskEscalationQueueOptions CreateOptions() => new(
        Path.Combine(Path.GetTempPath(), "Meridian.Tests", $"escalations-{Guid.NewGuid():N}", "escalations.json"));

    private static RiskEscalationQueueService CreateQueue(RiskEscalationQueueOptions? options = null) =>
        new(NullLogger<RiskEscalationQueueService>.Instance, options: options ?? CreateOptions());

    private static OrderRequest CreateOrder(decimal quantity = 100m, decimal? limitPrice = 250m) => new()
    {
        Symbol = "AAPL",
        Side = OrderSide.Buy,
        Type = OrderType.Limit,
        Quantity = quantity,
        LimitPrice = limitPrice,
    };

    private static OrderRequest WithApprovalToken(OrderRequest request, string escalationId) =>
        request with
        {
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [RiskEscalationQueueService.ApprovalMetadataKey] = escalationId
            }
        };

    [Fact]
    public void Park_AddsPendingEntry()
    {
        var queue = CreateQueue();

        var entry = queue.Park(CreateOrder(), "Notional above governed band.", ruleName: "OrderNotional");

        entry.Status.Should().Be(RiskEscalationStatus.PendingApproval);
        queue.GetPending().Should().ContainSingle(pending => pending.EscalationId == entry.EscalationId);
    }

    [Fact]
    public void ApproveThenConsume_ReleasesExactlyOnce()
    {
        var queue = CreateQueue();
        var entry = queue.Park(CreateOrder(), "escalated");

        var approved = queue.Approve(entry.EscalationId, actor: "risk-desk", reason: "cleared with PM");
        approved.Should().NotBeNull();
        approved!.Status.Should().Be(RiskEscalationStatus.Approved);
        approved.ResolvedBy.Should().Be("risk-desk");

        var resubmission = WithApprovalToken(CreateOrder(), entry.EscalationId);
        queue.TryConsumeApproval(resubmission).Should().NotBeNull();
        queue.TryGet(entry.EscalationId)!.Status.Should().Be(RiskEscalationStatus.Released);

        // One-shot: a second resubmission with the same token must not pass.
        queue.TryConsumeApproval(resubmission).Should().BeNull();
    }

    [Fact]
    public void Consume_WithMismatchedFingerprint_RefusesRelease()
    {
        var queue = CreateQueue();
        var entry = queue.Park(CreateOrder(quantity: 100m), "escalated");
        queue.Approve(entry.EscalationId, actor: "risk-desk");

        // Same token, different quantity: the approval must not transfer.
        var tampered = WithApprovalToken(CreateOrder(quantity: 5_000m), entry.EscalationId);

        queue.TryConsumeApproval(tampered).Should().BeNull();
        queue.TryGet(entry.EscalationId)!.Status.Should().Be(RiskEscalationStatus.Approved);
    }

    [Fact]
    public void Consume_PendingOrDeniedEntry_RefusesRelease()
    {
        var queue = CreateQueue();
        var pending = queue.Park(CreateOrder(), "escalated");
        queue.TryConsumeApproval(WithApprovalToken(CreateOrder(), pending.EscalationId))
            .Should().BeNull("a pending entry has no operator approval yet");

        var denied = queue.Park(CreateOrder(), "escalated");
        queue.Deny(denied.EscalationId, actor: "risk-desk", reason: "too large for today");
        queue.TryConsumeApproval(WithApprovalToken(CreateOrder(), denied.EscalationId))
            .Should().BeNull("a denied entry must never release");
        queue.TryGet(denied.EscalationId)!.Status.Should().Be(RiskEscalationStatus.Denied);
    }

    [Fact]
    public void Resolve_UnknownOrAlreadyResolvedEntry_ReturnsNull()
    {
        var queue = CreateQueue();
        queue.Approve("does-not-exist", actor: "risk-desk").Should().BeNull();

        var entry = queue.Park(CreateOrder(), "escalated");
        queue.Deny(entry.EscalationId, actor: "risk-desk");
        queue.Approve(entry.EscalationId, actor: "risk-desk")
            .Should().BeNull("a denied entry cannot be approved afterwards");
    }

    [Fact]
    public void Consume_WithoutToken_ReturnsFalse()
    {
        var queue = CreateQueue();
        queue.Park(CreateOrder(), "escalated");

        queue.TryConsumeApproval(CreateOrder()).Should().BeNull();
    }

    [Theory]
    [InlineData("stopPrice")]
    [InlineData("timeInForce")]
    [InlineData("fundAccountId")]
    [InlineData("strategyId")]
    public void Consume_WithVariedRoutingOrPayoffField_RefusesRelease(string variation)
    {
        var queue = CreateQueue();
        var entry = queue.Park(CreateOrder(), "escalated");
        queue.Approve(entry.EscalationId, actor: "risk-desk");

        var tampered = variation switch
        {
            "stopPrice" => CreateOrder() with { StopPrice = 240m },
            "timeInForce" => CreateOrder() with { TimeInForce = TimeInForce.GoodTilCancelled },
            "fundAccountId" => CreateOrder() with { FundAccountId = Guid.NewGuid() },
            _ => CreateOrder() with { StrategyId = "different-strategy" }
        };

        queue.TryConsumeApproval(WithApprovalToken(tampered, entry.EscalationId))
            .Should().BeNull("the approval binds to the entire executable order, not a partial fingerprint");
        queue.TryGet(entry.EscalationId)!.Status.Should().Be(RiskEscalationStatus.Approved);
    }

    [Fact]
    public void Trim_DropsOnlyResolvedHistory_NeverPendingOrArmedApprovals()
    {
        var options = new RiskEscalationQueueOptions(
            Path.Combine(Path.GetTempPath(), "Meridian.Tests", $"escalations-{Guid.NewGuid():N}", "escalations.json"),
            MaxRetainedEntries: 3);
        var queue = CreateQueue(options);

        // Oldest first: two entries that become terminal history, then live entries.
        var denied1 = queue.Park(CreateOrder(quantity: 1m), "history");
        var denied2 = queue.Park(CreateOrder(quantity: 2m), "history");
        queue.Deny(denied1.EscalationId, actor: "risk-desk");
        queue.Deny(denied2.EscalationId, actor: "risk-desk");
        var pending = queue.Park(CreateOrder(quantity: 3m), "awaiting decision");

        // Exceeding retention trims the terminal history…
        var armed = queue.Park(CreateOrder(quantity: 4m), "approved and armed");
        queue.Approve(armed.EscalationId, actor: "risk-desk");
        queue.Park(CreateOrder(quantity: 5m), "newest");

        queue.TryGet(denied1.EscalationId).Should().BeNull("terminal history is trimmable");
        queue.TryGet(denied2.EscalationId).Should().BeNull("terminal history is trimmable");

        // …but retention pressure never evicts an unresolved escalation or an armed
        // one-shot approval, even though the queue stays above its retention target.
        queue.Park(CreateOrder(quantity: 6m), "over retention");
        queue.TryGet(pending.EscalationId)!.Status.Should().Be(RiskEscalationStatus.PendingApproval);
        queue.TryGet(armed.EscalationId)!.Status.Should().Be(RiskEscalationStatus.Approved);
    }

    [Fact]
    public void Queue_PersistsAcrossRestart_IncludingArmedApprovals()
    {
        var options = CreateOptions();
        var first = CreateQueue(options);
        var pending = first.Park(CreateOrder(quantity: 10m), "pending across restart", ruleName: "OrderNotional");
        var approved = first.Park(CreateOrder(quantity: 20m), "approved across restart", ruleName: "OrderNotional");
        first.Approve(approved.EscalationId, actor: "risk-desk", reason: "cleared");

        // A new instance over the same snapshot path simulates a process restart.
        var restarted = CreateQueue(options);

        restarted.GetPending().Should().ContainSingle(entry => entry.EscalationId == pending.EscalationId);
        restarted.TryGet(approved.EscalationId)!.Status.Should().Be(RiskEscalationStatus.Approved);

        // The armed approval survives and still releases exactly once.
        var resubmission = WithApprovalToken(CreateOrder(quantity: 20m), approved.EscalationId);
        restarted.TryConsumeApproval(resubmission).Should().NotBeNull();
        restarted.TryConsumeApproval(resubmission).Should().BeNull();

        // The release is durable too.
        var reloaded = CreateQueue(options);
        reloaded.TryGet(approved.EscalationId)!.Status.Should().Be(RiskEscalationStatus.Released);
    }
}
