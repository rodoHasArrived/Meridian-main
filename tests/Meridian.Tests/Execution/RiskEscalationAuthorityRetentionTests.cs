using FluentAssertions;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meridian.Tests.Execution;

/// <summary>
/// A parked order's typed authority is retained with its escalation so the release is judged by
/// the authority the order was submitted under (governed order command boundary, Phase 3).
/// </summary>
public sealed class RiskEscalationAuthorityRetentionTests : IDisposable
{
    private static readonly Guid AccountId = Guid.Parse("53bf0251-17f6-4fb7-8dbe-6fb4966e2749");
    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-tests", "escalation-authority", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void TryAttachAuthority_PendingEntry_IsRetainedAcrossReload()
    {
        var authority = Authority();
        var queue = Queue();
        var entry = queue.Park(Order(), "notional above limit", actor: "ops");

        queue.TryAttachAuthority(entry.EscalationId, authority).Should().BeTrue();

        var reloaded = Queue().TryGet(entry.EscalationId);
        reloaded!.Authority.Should().BeEquivalentTo(authority, "the release after a restart must see the same authority");
    }

    [Fact]
    public void TryAttachAuthority_IsOneShot()
    {
        var queue = Queue();
        var entry = queue.Park(Order(), "notional above limit", actor: "ops");
        queue.TryAttachAuthority(entry.EscalationId, Authority()).Should().BeTrue();

        var replacement = Authority() with { Actor = "someone-else", GrantedTo = "someone-else" };

        queue.TryAttachAuthority(entry.EscalationId, replacement).Should().BeFalse();
        queue.TryGet(entry.EscalationId)!.Authority!.Actor.Should().Be("ops");
    }

    [Fact]
    public void TryAttachAuthority_ResolvedEntry_IsRefused()
    {
        var queue = Queue();
        var entry = queue.Park(Order(), "notional above limit", actor: "ops");
        queue.Approve(entry.EscalationId, "approver", "ok");

        queue.TryAttachAuthority(entry.EscalationId, Authority()).Should().BeFalse(
            "authority can only be attached while the order is still awaiting a decision");
    }

    [Fact]
    public void TryAttachAuthority_UnknownEntry_IsRefused()
    {
        Queue().TryAttachAuthority("missing", Authority()).Should().BeFalse();
    }

    private RiskEscalationQueueService Queue() => new(
        NullLogger<RiskEscalationQueueService>.Instance,
        options: new RiskEscalationQueueOptions(Path.Combine(_root, "escalations.json")));

    private static OrderSubmissionAuthorityRecord Authority() =>
        OrderSubmissionAuthority.ForOperatorTicket(
            "ops",
            new ExecutionAccountGrant(AccountId, "ops", ExecutionAccountGrantSources.HttpPrincipal, DateTimeOffset.UtcNow),
            "corr-1").ToRecord();

    private static OrderRequest Order() => new()
    {
        Symbol = "AAPL",
        Side = OrderSide.Buy,
        Type = OrderType.Limit,
        Quantity = 10m,
        LimitPrice = 100m,
        FundAccountId = AccountId
    };
}
