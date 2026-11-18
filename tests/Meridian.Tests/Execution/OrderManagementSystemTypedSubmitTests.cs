using FluentAssertions;
using Meridian.Execution;
using Meridian.Execution.OrderManagement;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Meridian.Tests.TestHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

using ExecutionGateway = Meridian.Execution.PaperTradingGateway;

namespace Meridian.Tests.Execution;

/// <summary>
/// <see cref="OrderManagementSystem.SubmitAsync"/> end to end on a paper OMS: typed authority is
/// enforced, and its identity replaces whatever identity the caller put in metadata.
/// </summary>
public sealed class OrderManagementSystemTypedSubmitTests : IDisposable
{
    private static readonly Guid AccountId = Guid.Parse("53bf0251-17f6-4fb7-8dbe-6fb4966e2749");
    private static readonly Guid OtherAccountId = Guid.Parse("11111111-2222-3333-4444-555555555555");

    private readonly OrderManagementSystem _oms;

    public OrderManagementSystemTypedSubmitTests()
    {
        var gateway = new ExecutionGateway(NullLogger<ExecutionGateway>.Instance);
        _oms = new OrderManagementSystem(gateway, NullLogger<OrderManagementSystem>.Instance);
        _oms.StartAsync().GetAwaiter().GetResult();
    }

    public void Dispose() => _oms.Dispose();

    [Fact]
    public async Task SubmitAsync_PaperOperatorTicket_RoutesUnderTheGrantedAccount()
    {
        var command = new OrderSubmitCommand(LimitOrder() with { FundAccountId = null }, Ticket());

        var result = await _oms.SubmitAsync(command);

        result.Success.Should().BeTrue(result.ErrorMessage);
        _oms.GetOrder(result.OrderId!)!.FundAccountId.Should().Be(AccountId,
            "an order without an account takes the account its submitter was granted");
    }

    [Fact]
    public async Task SubmitAsync_OrderForAnotherAccount_IsRefused()
    {
        var command = new OrderSubmitCommand(LimitOrder(fundAccountId: OtherAccountId), Ticket());

        var result = await _oms.SubmitAsync(command);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("fund account");
    }

    [Fact]
    public async Task SubmitAsync_KillSwitchAuthority_IsRefused()
    {
        var command = new OrderSubmitCommand(LimitOrder(), OrderSubmissionAuthority.ForKillSwitch("ops", "sweep-1"));

        var result = await _oms.SubmitAsync(command);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("cannot submit");
    }

    [Fact]
    public async Task PlaceOrderAsync_LegacyPaperOrder_StillRoutes()
    {
        var result = await _oms.PlaceOrderAsync(LimitOrder());

        result.Success.Should().BeTrue(result.ErrorMessage);
        result.AuthorityDecision.Should().BeNull("an untyped order carries no typed authority decision");
        _oms.GetOrder(result.OrderId)!.Origin.Should().Be(OrderCommandOrigin.Unattributed);
    }

    [Fact]
    public async Task SubmitAsync_Admitted_ReportsAuthorityAndRecordsOrigin()
    {
        var result = await _oms.SubmitAsync(new OrderSubmitCommand(LimitOrder(), Ticket()));

        result.Success.Should().BeTrue(result.ErrorMessage);
        result.AuthorityDecision.Should().BeEquivalentTo(new AuthorityDecisionSummary(
            OrderCommandOrigin.OperatorTicket, true, null, null, "dispatch", null, null));
        _oms.GetOrder(result.OrderId)!.Origin.Should().Be(OrderCommandOrigin.OperatorTicket);
    }

    [Fact]
    public async Task SubmitAsync_AuthorityRefusal_ReportsCheckpointCodeAndOrigin()
    {
        var result = await _oms.SubmitAsync(new OrderSubmitCommand(LimitOrder(fundAccountId: OtherAccountId), Ticket()));

        result.Success.Should().BeFalse();
        result.AuthorityDecision.Should().NotBeNull();
        result.AuthorityDecision!.IsAuthorized.Should().BeFalse();
        result.AuthorityDecision.Checkpoint.Should().Be("preflight");
        result.AuthorityDecision.ReasonCode.Should().Be(OrderAuthorityReasonCodes.AccountGrantMismatch);
        result.AuthorityDecision.Origin.Should().Be(OrderCommandOrigin.OperatorTicket);
        result.OrderState!.Origin.Should().Be(OrderCommandOrigin.OperatorTicket);
    }

    [Fact]
    public async Task SubmitAsync_DispatchRecheckThrows_RearmsTheConsumedApproval()
    {
        var queue = new RiskEscalationQueueService(
            NullLogger<RiskEscalationQueueService>.Instance,
            options: new RiskEscalationQueueOptions(
                Path.Combine(Path.GetTempPath(), "Meridian.Tests", $"escalations-{Guid.NewGuid():N}", "escalations.json")));
        var parked = queue.Park(LimitOrder(), "above band");
        queue.Approve(parked.EscalationId, actor: "risk-desk");
        var releaseRequest = LimitOrder() with
        {
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [RiskEscalationQueueService.ApprovalMetadataKey] = parked.EscalationId
            }
        };
        queue.TryConsumeApproval(releaseRequest).Should().NotBeNull();

        var riskValidator = RiskValidatorSubstitute.Create();
        riskValidator.ValidateOrderAsync(Arg.Any<OrderRequest>(), Arg.Any<CancellationToken>())
            .Returns(RiskValidationResult.Approved() with { ConsumedApprovalId = parked.EscalationId });

        // Security Master answers at preflight, then its lookup fails during the dispatch recheck.
        var securityMaster = Substitute.For<ISecurityMasterGate>();
        securityMaster.CheckAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(
                _ => Task.FromResult(new SecurityMasterGateResult(true)),
                _ => throw new TimeoutException("security master lookup timed out"));

        using var oms = new OrderManagementSystem(
            new ExecutionGateway(NullLogger<ExecutionGateway>.Instance),
            NullLogger<OrderManagementSystem>.Instance,
            riskValidator: riskValidator,
            securityMasterGate: securityMaster,
            escalationQueue: queue);
        await oms.StartAsync();

        var submit = () => oms.SubmitAsync(new OrderSubmitCommand(LimitOrder() with { ClientOrderId = "recheck-fault" }, Ticket()));

        await submit.Should().ThrowAsync<TimeoutException>();
        queue.TryConsumeApproval(releaseRequest).Should().NotBeNull(
            "nothing was dispatched, so a failed authority recheck must not spend the desk's one-shot approval");
    }

    [Fact]
    public async Task SubmitAsync_ParkedAndApprovedBeforeThePostParkStep_StillRetainsItsAuthority()
    {
        var queue = new RiskEscalationQueueService(
            NullLogger<RiskEscalationQueueService>.Instance,
            options: new RiskEscalationQueueOptions(
                Path.Combine(Path.GetTempPath(), "Meridian.Tests", $"escalations-{Guid.NewGuid():N}", "escalations.json")));

        // The validator parks the order, and the desk approves it before the OMS's post-park step
        // runs. Attaching authority afterwards is refused for an entry no longer pending, so only
        // retention at park time keeps the authority.
        var riskValidator = RiskValidatorSubstitute.Create();
        riskValidator.ValidateOrderAsync(Arg.Any<OrderRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var parked = queue.Park(call.Arg<OrderRequest>(), "above band", actor: "ops");
                queue.Approve(parked.EscalationId, "risk-desk");
                return RiskValidationResult.Escalated($"Parked for governed approval ({parked.EscalationId})", parked.EscalationId);
            });

        using var oms = new OrderManagementSystem(
            new ExecutionGateway(NullLogger<ExecutionGateway>.Instance),
            NullLogger<OrderManagementSystem>.Instance,
            riskValidator: riskValidator,
            escalationQueue: queue);
        await oms.StartAsync();

        var result = await oms.SubmitAsync(new OrderSubmitCommand(LimitOrder() with { ClientOrderId = "park-race" }, Ticket()));

        result.RequiresApproval.Should().BeTrue();
        var entry = queue.TryGet(result.EscalationId!)!;
        entry.Status.Should().Be(RiskEscalationStatus.Approved);
        entry.Authority.Should().NotBeNull("the park retained the order's typed authority in its own write");
        entry.Authority!.Origin.Should().Be(OrderCommandOrigin.OperatorTicket);
    }

    [Fact]
    public void OrderOrigin_SerializesByName()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new AuthorityDecisionSummary(
            OrderCommandOrigin.StrategyRun, false, "RUN_NOT_ACTIVE", "ended", "dispatch", null, null));

        json.Should().Contain("\"StrategyRun\"", "the browser renders the origin by name");
    }

    [Fact]
    public void Bind_ReplacesCallerIdentityMetadataWithAuthority()
    {
        var order = LimitOrder() with
        {
            Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["RUNID"] = "spoofed-run",
                ["actor"] = "someone-else",
                ["session_id"] = "spoofed-session",
                ["desk"] = "options-income"
            }
        };

        var bound = OrderAuthorityMetadata.Bind(order, Ticket());

        bound.Metadata.Should().NotContainKeys("RUNID", "runId", "session_id", "sessionId");
        bound.Metadata!["actor"].Should().Be("ops");
        bound.Metadata["correlationId"].Should().Be("corr-1");
        bound.Metadata["desk"].Should().Be("options-income", "non-identity metadata is kept");
    }

    [Fact]
    public void Bind_StrategyRun_StampsRunId()
    {
        var grant = new ExecutionAccountGrant(AccountId, "strategy-runtime", ExecutionAccountGrantSources.StrategyRun, DateTimeOffset.UtcNow);
        var authority = OrderSubmissionAuthority.ForStrategyRun("strategy-runtime", grant, "run-live");

        var bound = OrderAuthorityMetadata.Bind(LimitOrder(), authority);

        bound.Metadata!["runId"].Should().Be("run-live");
        bound.Metadata["correlationId"].Should().Be("run-live");
    }

    [Fact]
    public void Bind_Release_NamesApproverAsActorAndRetainsSubmitter()
    {
        var release = OrderSubmissionAuthority.ForEscalationRelease(Ticket(), "risk-approver", "risk-escalation-esc-1");
        var order = LimitOrder() with
        {
            Metadata = new Dictionary<string, string> { [RiskEscalationQueueService.SubmitterMetadataKey] = "spoofed" }
        };

        var bound = OrderAuthorityMetadata.Bind(order, release);

        // The queue records "actor" as ReleasedBy and refuses a release by the order's own
        // submitter, so the release names the approver; the submitter travels separately so a
        // rule that escalates again re-parks under the submitter.
        bound.Metadata!["actor"].Should().Be("risk-approver");
        bound.Metadata[RiskEscalationQueueService.SubmitterMetadataKey].Should().Be("ops");
        bound.Metadata["correlationId"].Should().Be("risk-escalation-esc-1");
    }

    private static OrderSubmissionAuthority Ticket() =>
        OrderSubmissionAuthority.ForOperatorTicket(
            "ops",
            new ExecutionAccountGrant(AccountId, "ops", ExecutionAccountGrantSources.HttpPrincipal, DateTimeOffset.UtcNow),
            "corr-1");

    private static OrderRequest LimitOrder(Guid? fundAccountId = null) => new()
    {
        Symbol = "AAPL",
        Side = OrderSide.Buy,
        Type = OrderType.Limit,
        Quantity = 10m,
        LimitPrice = 150m,
        FundAccountId = fundAccountId ?? AccountId
    };
}
