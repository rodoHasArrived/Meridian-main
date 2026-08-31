using System.Text.Json;
using FluentAssertions;
using Meridian.Execution.Sdk;
using Meridian.Execution.Serialization;
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
    public void Consume_BySubmitterOfTheParkedOrder_IsRefused()
    {
        var queue = CreateQueue();
        var entry = queue.Park(CreateOrder(), "escalated", actor: "trader-alice");

        // Approved without releasing: the entry stays armed, and Alice already learned this
        // escalation id from her own parked response. Resubmitting the order with the token
        // must not let her route it — that is the approval endpoint's self-submitter check
        // reached by a cheaper path.
        queue.Approve(entry.EscalationId, actor: "risk-desk", reason: "cleared with PM").Should().NotBeNull();

        var selfRelease = WithApprovalToken(CreateOrder(), entry.EscalationId) with
        {
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [RiskEscalationQueueService.ApprovalMetadataKey] = entry.EscalationId,
                ["actor"] = "trader-alice"
            }
        };

        queue.TryConsumeApproval(selfRelease).Should().BeNull("the submitter cannot release their own order");
        queue.TryGet(entry.EscalationId)!.Status.Should().Be(
            RiskEscalationStatus.Approved,
            "a refused release leaves the decision armed for someone authorized to act on it");

        // Another operator carrying the same token still releases it.
        var peerRelease = WithApprovalToken(CreateOrder(), entry.EscalationId) with
        {
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [RiskEscalationQueueService.ApprovalMetadataKey] = entry.EscalationId,
                ["actor"] = "trader-bob"
            }
        };

        queue.TryConsumeApproval(peerRelease).Should().NotBeNull();
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
    public void Park_WhenSnapshotCannotPersist_RollsBackAndThrows()
    {
        var root = Path.Combine(Path.GetTempPath(), "Meridian.Tests", $"escalations-park-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.GetDirectoryName(root)!);
        File.WriteAllText(root, "blocks the snapshot directory");
        try
        {
            var queue = CreateQueue(new RiskEscalationQueueOptions(Path.Combine(root, "escalations.json")));

            var park = () => queue.Park(CreateOrder(), "storage is down");

            park.Should().Throw<InvalidOperationException>(
                "an escalation that cannot survive a restart must not hand out an actionable id");
            queue.GetPending().Should().BeEmpty("the rolled-back park leaves nothing behind");
        }
        finally
        {
            File.Delete(root);
        }
    }

    [Fact]
    public void Park_FreezesTheRetainedRequest()
    {
        var queue = CreateQueue();
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["notional"] = "50000" };
        var order = CreateOrder() with { Metadata = metadata };
        var entry = queue.Park(order, "escalated");
        queue.Approve(entry.EscalationId, actor: "risk-desk");

        // Mutating the caller's dictionary after parking must not change what the desk
        // approved — otherwise the fingerprint would compare the altered order to itself.
        metadata["notional"] = "500000";

        var replay = CreateOrder() with
        {
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["notional"] = "500000",
                [RiskEscalationQueueService.ApprovalMetadataKey] = entry.EscalationId
            }
        };
        queue.TryConsumeApproval(replay).Should().BeNull("the retained order was frozen at parking time");
    }

    [Fact]
    public void Release_RecordsItsOwnActorWithoutOverwritingTheApprover()
    {
        var queue = CreateQueue();
        var entry = queue.Park(CreateOrder(), "escalated");
        queue.Approve(entry.EscalationId, actor: "alice", reason: "cleared");

        var release = CreateOrder() with
        {
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [RiskEscalationQueueService.ApprovalMetadataKey] = entry.EscalationId,
                ["actor"] = "bob"
            }
        };
        queue.TryConsumeApproval(release).Should().NotBeNull();

        var released = queue.TryGet(entry.EscalationId)!;
        released.ResolvedBy.Should().Be("alice", "the approver's identity is evidence and must survive the release");
        released.ReleasedBy.Should().Be("bob");
        released.ReleasedAt.Should().NotBeNull();
    }

    [Fact]
    public void TryBeginRelease_ClaimsAnApprovedEntryExactlyOnce()
    {
        var queue = CreateQueue();
        var entry = queue.Park(CreateOrder(), "escalated");
        queue.Approve(entry.EscalationId, actor: "risk-desk");

        queue.TryBeginRelease(entry.EscalationId).Should().BeTrue();
        queue.TryBeginRelease(entry.EscalationId).Should().BeFalse(
            "a second concurrent release must not submit the same retained order");

        queue.EndRelease(entry.EscalationId);
        queue.TryBeginRelease(entry.EscalationId).Should().BeTrue("a cleared claim is retryable");
    }

    [Fact]
    public void Consume_WithSeveralTokens_ReleasesEveryGrantedApproval()
    {
        var queue = CreateQueue();
        var first = queue.Park(CreateOrder(), "rule A band", ruleName: "OrderNotional");
        var second = queue.Park(CreateOrder(), "rule B review", ruleName: "DeskReview");
        queue.Approve(first.EscalationId, actor: "risk-desk");
        queue.Approve(second.EscalationId, actor: "risk-desk");

        // An order breaching two escalation-capable rules carries one token per decision;
        // both must be honored in a single evaluation or the order can never route.
        var resubmission = CreateOrder() with
        {
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [RiskEscalationQueueService.ApprovalMetadataKey] =
                    RiskEscalationQueueService.JoinTokens([first.EscalationId, second.EscalationId])
            }
        };

        var released = queue.TryConsumeApprovals(resubmission);

        released.Select(entry => entry.EscalationId).Should().BeEquivalentTo([first.EscalationId, second.EscalationId]);
        queue.TryGet(first.EscalationId)!.Status.Should().Be(RiskEscalationStatus.Released);
        queue.TryGet(second.EscalationId)!.Status.Should().Be(RiskEscalationStatus.Released);

        // Still one-shot: replaying the same token set releases nothing.
        queue.TryConsumeApprovals(resubmission).Should().BeEmpty();
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
        var queue = CreateQueue(new RiskEscalationQueueOptions(Path.Combine(root, "escalations.json")));
        // Park while storage is healthy, then block it so only the denial fails.
        var entry = queue.Park(CreateOrder(), "parked before storage failed");
        Directory.Delete(root, recursive: true);
        File.WriteAllText(root, "blocks the snapshot directory");
        try
        {
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

    [Fact]
    public void Restart_FromUnreadableSnapshot_FailsClosed()
    {
        var options = CreateOptions();
        var first = CreateQueue(options);
        first.Park(CreateOrder(), "must not vanish", ruleName: "OrderNotional");

        File.Exists(options.SnapshotPath).Should().BeTrue();
        File.WriteAllText(options.SnapshotPath, "{ this is not valid json");

        // Starting empty would erase every parked order and armed approval this queue has
        // already reported as durable, with no operator able to approve, deny, or audit it.
        var start = () => CreateQueue(options);

        start.Should().Throw<InvalidOperationException>()
            .WithMessage("*could not be read*");
    }

    [Fact]
    public void Restart_ClearsReleaseClaimsHeldByThePreviousProcess()
    {
        var options = CreateOptions();
        var first = CreateQueue(options);
        var approved = first.Park(CreateOrder(quantity: 20m), "claimed then lost", ruleName: "OrderNotional");
        first.Approve(approved.EscalationId, actor: "risk-desk", reason: "cleared");

        first.TryBeginRelease(approved.EscalationId).Should().BeTrue();
        // A concurrent park snapshots every entry, capturing the in-flight claim on disk.
        first.Park(CreateOrder(quantity: 30m), "concurrent park", ruleName: "OrderNotional");

        // The claim belongs to the process that took it. Reloading it would leave the
        // approval permanently unclaimable, so the operator could never release the order.
        var restarted = CreateQueue(options);

        restarted.TryGet(approved.EscalationId)!.Status.Should().Be(RiskEscalationStatus.Approved);
        restarted.TryBeginRelease(approved.EscalationId).Should().BeTrue(
            "a release claim from a dead process must not wedge the approval");
    }

    [Fact]
    public void Park_AtTheUnresolvedCapacityLimit_IsRefused()
    {
        var options = CreateOptions() with { MaxUnresolvedEntries = 2 };
        var queue = CreateQueue(options);

        queue.Park(CreateOrder(quantity: 1m), "first", ruleName: "OrderNotional");
        var second = queue.Park(CreateOrder(quantity: 2m), "second", ruleName: "OrderNotional");

        // Unresolved entries are never trimmed, so without backpressure the queue, its
        // snapshot, and every subsequent park's latency would grow without bound.
        var overflow = () => queue.Park(CreateOrder(quantity: 3m), "third", ruleName: "OrderNotional");
        overflow.Should().Throw<InvalidOperationException>().WithMessage("*unresolved escalation*");

        // Resolving one frees a slot; nothing actionable was dropped to make room.
        queue.Deny(second.EscalationId, actor: "risk-desk", reason: "not today");
        queue.Park(CreateOrder(quantity: 3m), "third", ruleName: "OrderNotional").Should().NotBeNull();
        queue.GetPending().Should().HaveCount(2);
    }

    [Fact]
    public void Restart_FromASnapshotWithAPartialEntry_FailsClosed()
    {
        var options = CreateOptions();
        var first = CreateQueue(options);
        first.Park(CreateOrder(), "must not vanish", ruleName: "OrderNotional");

        // Syntactically valid JSON whose entry lost its fields. Skipping it would delete a
        // governed order — and its client-order-id reservation — from a queue that then
        // reported startup as clean.
        File.WriteAllText(options.SnapshotPath, """{"Entries":[{"Reason":"orphaned"}]}""");

        var start = () => CreateQueue(options);

        start.Should().Throw<InvalidOperationException>().WithMessage("*partial governed-approval queue*");
    }

    [Fact]
    public void Withdraw_ResolvesAnApprovedEntryThatWasNeverReleased()
    {
        var queue = CreateQueue();
        var parked = queue.Park(CreateOrder(), "above band", ruleName: "OrderNotional");
        queue.Approve(parked.EscalationId, actor: "risk-desk", reason: "cleared");

        // An approval that has not been released is only a permission; if the order behind
        // it is gone, the entry must resolve or an operator could still release it later.
        queue.Deny(parked.EscalationId, actor: "oms", reason: "plain denial").Should().BeNull(
            "a plain denial only resolves pending entries");

        var withdrawn = queue.Withdraw(parked.EscalationId, actor: "oms", reason: "the submitter cancelled");

        withdrawn.Should().NotBeNull();
        queue.TryGet(parked.EscalationId)!.Status.Should().Be(RiskEscalationStatus.Denied);
        queue.TryConsumeApproval(WithApprovalToken(CreateOrder(), parked.EscalationId)).Should().BeNull(
            "a withdrawn escalation can never release its order");
    }

    [Fact]
    public void Withdraw_LeavesAReleaseThatIsAlreadyInFlight()
    {
        var queue = CreateQueue();
        var parked = queue.Park(CreateOrder(), "above band", ruleName: "OrderNotional");
        queue.Approve(parked.EscalationId, actor: "risk-desk", reason: "cleared");
        queue.TryBeginRelease(parked.EscalationId).Should().BeTrue();

        // That order is on its way to the broker; only its own outcome may resolve the
        // entry, or the queue would disown an order that is about to exist.
        queue.Withdraw(parked.EscalationId, actor: "oms", reason: "too late").Should().BeNull();
        queue.TryGet(parked.EscalationId)!.Status.Should().Be(RiskEscalationStatus.Approved);
    }

    [Fact]
    public void TryConsumeApproval_WithADifferentClientOrderId_IsRefused()
    {
        var queue = CreateQueue();
        var parked = queue.Park(
            CreateOrder() with { ClientOrderId = "CLIENT-PARKED" },
            "above band",
            ruleName: "OrderNotional");
        queue.Approve(parked.EscalationId, actor: "risk-desk", reason: "cleared");

        // Same reviewed fields, different client identity: routing it would file the
        // execution under an id the parking and approval audit entries never mention, and
        // leave the reserved parked id stranded.
        var renamed = WithApprovalToken(CreateOrder() with { ClientOrderId = "CLIENT-OTHER" }, parked.EscalationId);
        queue.TryConsumeApproval(renamed).Should().BeNull();

        var original = WithApprovalToken(CreateOrder() with { ClientOrderId = "CLIENT-PARKED" }, parked.EscalationId);
        queue.TryConsumeApproval(original).Should().NotBeNull("the release under the parked id still works");
    }

    private static void WriteSnapshot(RiskEscalationQueueOptions options, params RiskEscalationEntry[] entries)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(options.SnapshotPath)!);
        File.WriteAllText(
            options.SnapshotPath,
            JsonSerializer.Serialize(
                new RiskEscalationSnapshot(entries),
                ExecutionJsonContext.Default.RiskEscalationSnapshot));
    }

    // An entry shaped like the pre-retained-submitter release code persisted it: the
    // current Park can no longer produce this shape, so restart tests write it directly.
    private static RiskEscalationEntry LegacyEntry(
        string escalationId,
        OrderRequest request,
        string? actor,
        RiskEscalationStatus status = RiskEscalationStatus.PendingApproval) => new(
        escalationId,
        request,
        "escalated",
        RuleName: null,
        Actor: actor,
        RunId: null,
        CorrelationId: null,
        ParkedAt: DateTimeOffset.UtcNow.AddMinutes(-5),
        Status: status);

    private static OrderRequest LegacyChainedRequest(string originEscalationId) => CreateOrder() with
    {
        Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [RiskEscalationQueueService.ApprovalMetadataKey] = originEscalationId,
            ["actor"] = "risk-officer-bob"
        }
    };

    [Fact]
    public void Restart_RebindsLegacyChainedEntriesToTheOriginalSubmitter()
    {
        var options = CreateOptions();
        // A snapshot written by the pre-retained-submitter release code: the chained
        // re-park carries the origin's token, no riskSubmitter, and the releasing
        // approver recorded as the entry's actor.
        WriteSnapshot(
            options,
            LegacyEntry("origin-1", CreateOrder(), actor: "trader-alice", RiskEscalationStatus.Released),
            LegacyEntry("chained-1", LegacyChainedRequest("origin-1"), actor: "risk-officer-bob"));

        var restarted = CreateQueue(options);

        // The reload recovers the submitter from the chain's fingerprint-verified first
        // linked approval, so the original submitter can no longer approve or release a
        // later stage of their own order — and the repair is persisted into the trusted
        // submitter channel so it survives origin trimming and later restarts.
        var repaired = restarted.TryGet("chained-1")!;
        repaired.Actor.Should().Be("trader-alice");
        repaired.Request.Metadata.Should().Contain(RiskEscalationQueueService.SubmitterMetadataKey, "trader-alice");
    }

    [Fact]
    public void Restart_DeniesLegacyChainedEntriesWhoseOriginalParkIsGone()
    {
        var options = CreateOptions();
        WriteSnapshot(options, LegacyEntry("chained-1", LegacyChainedRequest("trimmed-origin"), actor: "risk-officer-bob"));

        var restarted = CreateQueue(options);

        // With the chain's original park no longer retained, the submitter identity the
        // segregation-of-duties checks must bind to cannot be recovered — fail closed with
        // an audited denial rather than leave an entry the submitter could self-release.
        var reloaded = restarted.TryGet("chained-1")!;
        reloaded.Status.Should().Be(RiskEscalationStatus.Denied);
        reloaded.ResolvedBy.Should().Be("system");
        restarted.GetPending().Should().BeEmpty();
    }

    [Fact]
    public void Restart_DeniesLegacyChainedEntriesWhoseLinkedOriginDoesNotMatchTheOrder()
    {
        var options = CreateOptions();
        // Clients could attach arbitrary riskEscalationId values before the migration, so
        // a legacy order can reference someone else's first-stage escalation. The rebind
        // must verify the link against the order fingerprint, not trust the token.
        WriteSnapshot(
            options,
            LegacyEntry("origin-1", CreateOrder(quantity: 999m), actor: "trader-carol", RiskEscalationStatus.Released),
            LegacyEntry("chained-1", LegacyChainedRequest("origin-1"), actor: "risk-officer-bob"));

        var restarted = CreateQueue(options);

        var reloaded = restarted.TryGet("chained-1")!;
        reloaded.Status.Should().Be(RiskEscalationStatus.Denied,
            "an unverifiable chain link must fail closed instead of rebinding to another submitter's identity");
        reloaded.Actor.Should().Be("risk-officer-bob", "the unproven origin's actor must not be adopted");
    }

    [Fact]
    public void Restart_KeepsSelfConsistentChainedEntriesWhoseOriginWasTrimmed()
    {
        var options = CreateOptions();
        var chained = CreateOrder() with
        {
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [RiskEscalationQueueService.ApprovalMetadataKey] = "trimmed-origin",
                [RiskEscalationQueueService.SubmitterMetadataKey] = "trader-alice",
                ["actor"] = "risk-officer-bob"
            }
        };
        WriteSnapshot(options, LegacyEntry("chained-1", chained, actor: "trader-alice"));

        var restarted = CreateQueue(options);

        // The trusted path always writes Actor and riskSubmitter from the same resolved
        // value; a self-consistent entry survives origin trimming instead of being denied.
        var reloaded = restarted.TryGet("chained-1")!;
        reloaded.Status.Should().Be(RiskEscalationStatus.PendingApproval);
        reloaded.Actor.Should().Be("trader-alice");
    }

    [Fact]
    public void Restart_DeniesLegacyChainedEntriesWhoseStampedSubmitterContradictsTheActor()
    {
        var options = CreateOptions();
        // riskSubmitter was not a reserved key before the migration, so a legacy client
        // could have planted one; a value that does not corroborate the recorded actor is
        // an unverifiable identity claim, not evidence of the trusted release path.
        var chained = CreateOrder() with
        {
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [RiskEscalationQueueService.ApprovalMetadataKey] = "trimmed-origin",
                [RiskEscalationQueueService.SubmitterMetadataKey] = "someone-else",
                ["actor"] = "risk-officer-bob"
            }
        };
        WriteSnapshot(options, LegacyEntry("chained-1", chained, actor: "risk-officer-bob"));

        var restarted = CreateQueue(options);

        restarted.TryGet("chained-1")!.Status.Should().Be(RiskEscalationStatus.Denied);
    }

    [Fact]
    public void Park_TokenCarryingResubmissionWithoutSubmitterMetadata_BindsToTheChainOrigin()
    {
        var queue = CreateQueue();
        var original = queue.Park(CreateOrder(), "first rule", ruleName: "OrderNotional", actor: "trader-alice");
        queue.Approve(original.EscalationId, actor: "risk-officer-bob", reason: "cleared");

        // The approver releases by resubmitting the approved order directly through the
        // submit path, which stamps the approver as the actor and strips any
        // client-supplied riskSubmitter.
        var resubmission = CreateOrder() with
        {
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [RiskEscalationQueueService.ApprovalMetadataKey] = original.EscalationId,
                ["actor"] = "risk-officer-bob"
            }
        };
        queue.TryConsumeApproval(resubmission).Should().NotBeNull("the approver may release by direct resubmission");

        // A later rule escalates the same in-flight submission: the new entry must bind to
        // the original submitter recovered from the consumed approval, not the approver.
        var second = queue.Park(resubmission, "second rule", ruleName: "PortfolioNotional", actor: "risk-officer-bob");

        second.Actor.Should().Be("trader-alice");
        second.Request.Metadata.Should().Contain(RiskEscalationQueueService.SubmitterMetadataKey, "trader-alice");
    }
}
