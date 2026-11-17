using System.Reflection;
using FluentAssertions;
using Meridian.Execution.Sdk;
using Xunit;

namespace Meridian.Tests.Execution;

/// <summary>
/// Guards the typed order-authority contract (governed order command boundary, Phase 1). The
/// failure mode under guard: authority assembled from incomplete or mismatched parts, or produced
/// by something other than a server-side factory.
/// </summary>
public sealed class OrderSubmissionAuthorityTests
{
    private static readonly Guid AccountId = Guid.Parse("53bf0251-17f6-4fb7-8dbe-6fb4966e2749");
    private static readonly DateTimeOffset ResolvedAt = new(2026, 11, 17, 14, 30, 0, TimeSpan.Zero);

    [Fact]
    public void Authority_HasNoPublicConstructor()
    {
        typeof(OrderSubmissionAuthority)
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .Should().BeEmpty("only the server-side factories may create authority");
    }

    [Fact]
    public void ForOperatorTicket_CarriesGrantAndNoRunIdentity()
    {
        var authority = OrderSubmissionAuthority.ForOperatorTicket(" ops ", Grant("ops"), "corr-1", " session-7 ");

        authority.Origin.Should().Be(OrderCommandOrigin.OperatorTicket);
        authority.Actor.Should().Be("ops");
        authority.AccountGrant!.FundAccountId.Should().Be(AccountId);
        authority.RunId.Should().BeNull("a manual ticket never carries strategy-run authority");
        authority.StrategyArtifactId.Should().BeNull();
        authority.CorrelationId.Should().Be("corr-1");
        authority.SessionId.Should().Be("session-7");
        authority.ReleasedFrom.Should().BeNull();
    }

    [Fact]
    public void ForOperatorTicket_GrantResolvedForAnotherActor_Throws()
    {
        var create = () => OrderSubmissionAuthority.ForOperatorTicket("ops", Grant("someone-else"), "corr-1");

        create.Should().Throw<ArgumentException>().WithMessage("*someone-else*");
    }

    [Fact]
    public void ForOperatorTicket_MissingGrant_Throws()
    {
        var create = () => OrderSubmissionAuthority.ForOperatorTicket("ops", null!, "corr-1");

        create.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ForOperatorTicket_BlankActorOrCorrelation_Throws(string? blank)
    {
        var blankActor = () => OrderSubmissionAuthority.ForOperatorTicket(blank!, Grant("ops"), "corr-1");
        var blankCorrelation = () => OrderSubmissionAuthority.ForOperatorTicket("ops", Grant("ops"), blank!);

        blankActor.Should().Throw<ArgumentException>();
        blankCorrelation.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ForPositionAction_RequiresMatchingGrant()
    {
        var authority = OrderSubmissionAuthority.ForPositionAction("ops", Grant("ops"), "close-1");
        var mismatched = () => OrderSubmissionAuthority.ForPositionAction("ops", Grant("other"), "close-1");

        authority.Origin.Should().Be(OrderCommandOrigin.PositionAction);
        authority.RunId.Should().BeNull();
        mismatched.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ForStrategyRun_UsesRunIdAsCorrelation()
    {
        var grant = new ExecutionAccountGrant(AccountId, "run-runtime", ExecutionAccountGrantSources.StrategyRun, ResolvedAt);

        var authority = OrderSubmissionAuthority.ForStrategyRun("strategy-runtime", grant, " run-live-001 ", " sa1-abc ");

        authority.Origin.Should().Be(OrderCommandOrigin.StrategyRun);
        authority.RunId.Should().Be("run-live-001");
        authority.CorrelationId.Should().Be("run-live-001", "LiveStrategyRunSession correlates its orders by run id");
        authority.StrategyArtifactId.Should().Be("sa1-abc");
    }

    [Fact]
    public void ForStrategyRun_BlankRunId_Throws()
    {
        var create = () => OrderSubmissionAuthority.ForStrategyRun("strategy-runtime", Grant("ops"), " ");

        create.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ForEscalationRelease_CarriesParkedAuthorityAndRecordsApprover()
    {
        var parked = OrderSubmissionAuthority.ForStrategyRun("strategy-runtime", Grant("ops"), "run-live-001", "sa1-abc");

        var release = OrderSubmissionAuthority.ForEscalationRelease(parked, "risk-approver");

        release.Origin.Should().Be(OrderCommandOrigin.EscalationRelease);
        release.Actor.Should().Be("risk-approver");
        release.ReleasedFrom.Should().BeSameAs(parked);
        release.AccountGrant.Should().BeSameAs(parked.AccountGrant, "the release rechecks the original grant, not the approver's");
        release.RunId.Should().Be("run-live-001");
        release.StrategyArtifactId.Should().Be("sa1-abc");
        release.CorrelationId.Should().Be(parked.CorrelationId);
    }

    [Fact]
    public void ForEscalationRelease_OfKillSwitch_Throws()
    {
        var killSwitch = OrderSubmissionAuthority.ForKillSwitch("ops", "sweep-1");

        var release = () => OrderSubmissionAuthority.ForEscalationRelease(killSwitch, "risk-approver");

        release.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ForKillSwitch_HasNoAccountGrant()
    {
        var authority = OrderSubmissionAuthority.ForKillSwitch("ops", "sweep-1");

        authority.Origin.Should().Be(OrderCommandOrigin.KillSwitch);
        authority.AccountGrant.Should().BeNull();
    }

    [Fact]
    public void Unattributed_ReadsLegacyMetadataCaseInsensitively()
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ACTOR"] = "ops",
            ["RunId"] = " run-1 ",
            ["sessionid"] = "session-1"
        };

        var authority = OrderSubmissionAuthority.Unattributed(metadata);

        authority.Origin.Should().Be(OrderCommandOrigin.Unattributed);
        authority.Actor.Should().Be("ops");
        authority.RunId.Should().Be("run-1");
        authority.CorrelationId.Should().Be("run-1", "without a correlation id the OMS falls back to the run id");
        authority.SessionId.Should().Be("session-1");
        authority.AccountGrant.Should().BeNull("legacy metadata never establishes account authority");
    }

    [Fact]
    public void Unattributed_NullMetadata_IsStillUnattributed()
    {
        var authority = OrderSubmissionAuthority.Unattributed(null);

        authority.Origin.Should().Be(OrderCommandOrigin.Unattributed);
        authority.Actor.Should().Be("unattributed");
        authority.RunId.Should().BeNull();
    }

    [Fact]
    public void ExecutionAccountGrant_EmptyAccount_Throws()
    {
        var create = () => new ExecutionAccountGrant(Guid.Empty, "ops", ExecutionAccountGrantSources.HttpPrincipal, ResolvedAt);

        create.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Commands_RequireOrderAndAuthority()
    {
        var authority = OrderSubmissionAuthority.ForKillSwitch("ops", "sweep-1");
        var order = new OrderRequest { Symbol = "SPY", Side = OrderSide.Buy, Type = OrderType.Market, Quantity = 1m };

        ((Action)(() => _ = new OrderSubmitCommand(null!, authority))).Should().Throw<ArgumentNullException>();
        ((Action)(() => _ = new OrderSubmitCommand(order, null!))).Should().Throw<ArgumentNullException>();
        ((Action)(() => _ = new OrderCancelCommand(" ", authority))).Should().Throw<ArgumentException>();
        ((Action)(() => _ = new OrderReplaceCommand("order-1", null!, authority))).Should().Throw<ArgumentNullException>();
        new OrderSubmitCommand(order, authority).Authority.Should().BeSameAs(authority);
    }

    private static ExecutionAccountGrant Grant(string grantedTo) =>
        new(AccountId, grantedTo, ExecutionAccountGrantSources.HttpPrincipal, ResolvedAt);
}
