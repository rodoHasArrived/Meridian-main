using FluentAssertions;
using Meridian.Execution;
using Meridian.Execution.OrderManagement;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Microsoft.Extensions.Logging.Abstractions;
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
