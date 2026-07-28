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
    public void Consume_WithAlteredGatewayMetadata_RefusesRelease()
    {
        var queue = CreateQueue();
        var parkedOrder = CreateOrder() with
        {
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["notional"] = "50000"
            }
        };
        var entry = queue.Park(parkedOrder, "escalated");
        queue.Approve(entry.EscalationId, actor: "risk-desk");

        // Gateways size orders from metadata (notional, bracket legs, extended hours), so
        // an altered bag is a materially different executable order.
        var tampered = CreateOrder() with
        {
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["notional"] = "500000",
                [RiskEscalationQueueService.ApprovalMetadataKey] = entry.EscalationId
            }
        };
        queue.TryConsumeApproval(tampered).Should().BeNull();

        // Adding a key the desk never reviewed is refused too.
        var withExtraKey = CreateOrder() with
        {
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["notional"] = "50000",
                ["extended_hours"] = "true",
                [RiskEscalationQueueService.ApprovalMetadataKey] = entry.EscalationId
            }
        };
        queue.TryConsumeApproval(withExtraKey).Should().BeNull();
        queue.TryGet(entry.EscalationId)!.Status.Should().Be(RiskEscalationStatus.Approved);
    }

    [Fact]
    public void Consume_WithReleaseKeysOnly_StillReleases()
    {
        var queue = CreateQueue();
        var parkedOrder = CreateOrder() with
        {
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["notional"] = "50000",
                ["actor"] = "trade-desk-1"
            }
        };
        var entry = queue.Park(parkedOrder, "escalated");
        queue.Approve(entry.EscalationId, actor: "risk-desk");

        // The approve endpoint stamps the token, the approving actor, and a release
        // correlation id; those keys are excluded from the fingerprint by design.
        var release = CreateOrder() with
        {
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["notional"] = "50000",
                ["actor"] = "risk-desk-supervisor",
                ["correlationId"] = $"risk-escalation-{entry.EscalationId}",
                [RiskEscalationQueueService.ApprovalMetadataKey] = entry.EscalationId
            }
        };

        queue.TryConsumeApproval(release).Should().NotBeNull();
    }

    [Fact]
    public void Trim_ContinuesPastProtectedEntries()
    {
        var options = new RiskEscalationQueueOptions(
            Path.Combine(Path.GetTempPath(), "Meridian.Tests", $"escalations-{Guid.NewGuid():N}", "escalations.json"),
            MaxRetainedEntries: 3);
        var queue = CreateQueue(options);

        // The oldest entry stays unresolved while terminal history piles up behind it.
        var pending = queue.Park(CreateOrder(quantity: 1m), "long-lived pending");
        var denied1 = queue.Park(CreateOrder(quantity: 2m), "history");
        var denied2 = queue.Park(CreateOrder(quantity: 3m), "history");
        queue.Deny(denied1.EscalationId, actor: "risk-desk");
        queue.Deny(denied2.EscalationId, actor: "risk-desk");

        // Retention pressure must trim terminal history even though the queue head is
        // protected — a single unresolved entry cannot shield the rest from the bound.
        queue.Park(CreateOrder(quantity: 4m), "newest");

        queue.TryGet(pending.EscalationId)!.Status.Should().Be(RiskEscalationStatus.PendingApproval);
        queue.TryGet(denied1.EscalationId).Should().BeNull("terminal history behind a protected head must still trim");
    }

    [Fact]
    public void GetRecent_AlwaysIncludesUnresolvedEntries()
    {
        var queue = CreateQueue();
        var pending = queue.Park(CreateOrder(quantity: 1m), "old pending");
        for (var i = 0; i < 3; i++)
        {
            var denied = queue.Park(CreateOrder(quantity: 10m + i), "history");
            queue.Deny(denied.EscalationId, actor: "risk-desk");
        }

        // The window is smaller than the newer terminal history, but the still-actionable
        // pending entry must remain discoverable through the only queue listing.
        var recent = queue.GetRecent(take: 2);

        recent.Should().Contain(entry => entry.EscalationId == pending.EscalationId);
    }

    [Fact]
    public void Deny_WhenSnapshotCannotPersist_RollsBackAndThrows()
    {
        // A file squatting on the snapshot directory makes every persist attempt fail.
        var root = Path.Combine(Path.GetTempPath(), "Meridian.Tests", $"escalations-blocked-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.GetDirectoryName(root)!);
        File.WriteAllText(root, "blocks the snapshot directory");
        try
        {
            var queue = CreateQueue(new RiskEscalationQueueOptions(Path.Combine(root, "escalations.json")));
            var entry = queue.Park(CreateOrder(), "parked while storage is down");

            var deny = () => queue.Deny(entry.EscalationId, actor: "risk-desk", reason: "refused");

            deny.Should().Throw<InvalidOperationException>(
                "an unpersisted denial would reload as pending after a restart and become approvable");
            queue.TryGet(entry.EscalationId)!.Status.Should().Be(RiskEscalationStatus.PendingApproval);

            // Once storage recovers the denial commits normally.
            File.Delete(root);
            queue.Deny(entry.EscalationId, actor: "risk-desk", reason: "refused")!
                .Status.Should().Be(RiskEscalationStatus.Denied);
        }
        finally
        {
            if (File.Exists(root))
            {
                File.Delete(root);
            }
        }
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
