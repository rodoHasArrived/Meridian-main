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
    private static RiskEscalationQueueService CreateQueue() =>
        new(NullLogger<RiskEscalationQueueService>.Instance);

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
        queue.TryConsumeApproval(resubmission).Should().BeTrue();
        queue.TryGet(entry.EscalationId)!.Status.Should().Be(RiskEscalationStatus.Released);

        // One-shot: a second resubmission with the same token must not pass.
        queue.TryConsumeApproval(resubmission).Should().BeFalse();
    }

    [Fact]
    public void Consume_WithMismatchedFingerprint_RefusesRelease()
    {
        var queue = CreateQueue();
        var entry = queue.Park(CreateOrder(quantity: 100m), "escalated");
        queue.Approve(entry.EscalationId, actor: "risk-desk");

        // Same token, different quantity: the approval must not transfer.
        var tampered = WithApprovalToken(CreateOrder(quantity: 5_000m), entry.EscalationId);

        queue.TryConsumeApproval(tampered).Should().BeFalse();
        queue.TryGet(entry.EscalationId)!.Status.Should().Be(RiskEscalationStatus.Approved);
    }

    [Fact]
    public void Consume_PendingOrDeniedEntry_RefusesRelease()
    {
        var queue = CreateQueue();
        var pending = queue.Park(CreateOrder(), "escalated");
        queue.TryConsumeApproval(WithApprovalToken(CreateOrder(), pending.EscalationId))
            .Should().BeFalse("a pending entry has no operator approval yet");

        var denied = queue.Park(CreateOrder(), "escalated");
        queue.Deny(denied.EscalationId, actor: "risk-desk", reason: "too large for today");
        queue.TryConsumeApproval(WithApprovalToken(CreateOrder(), denied.EscalationId))
            .Should().BeFalse("a denied entry must never release");
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

        queue.TryConsumeApproval(CreateOrder()).Should().BeFalse();
    }
}
