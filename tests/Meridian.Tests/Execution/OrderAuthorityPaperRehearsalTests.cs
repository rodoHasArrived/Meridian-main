using FluentAssertions;
using Meridian.Execution;
using Meridian.Execution.OrderManagement;
using Meridian.Execution.Sdk;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

using ExecutionGateway = Meridian.Execution.PaperTradingGateway;

namespace Meridian.Tests.Execution;

/// <summary>
/// Paper rehearsal of the governed order command boundary (Phase 4): one paper OMS takes a
/// strategy-run order, an operator ticket, a ticket for an account the operator was not granted,
/// a kill-switch submit and a legacy untyped order, and the working-order book ends up showing
/// who originated each order that routed. Synthetic evidence only; no broker or credentials.
/// </summary>
public sealed class OrderAuthorityPaperRehearsalTests : IDisposable
{
    private static readonly Guid FundAccount = Guid.Parse("53bf0251-17f6-4fb7-8dbe-6fb4966e2749");
    private static readonly Guid OtherAccount = Guid.Parse("11111111-2222-3333-4444-555555555555");

    private readonly OrderManagementSystem _oms;

    public OrderAuthorityPaperRehearsalTests()
    {
        _oms = new OrderManagementSystem(new ExecutionGateway(NullLogger<ExecutionGateway>.Instance), NullLogger<OrderManagementSystem>.Instance);
        _oms.StartAsync().GetAwaiter().GetResult();
    }

    public void Dispose() => _oms.Dispose();

    [Fact]
    public async Task PaperRehearsal_EveryOriginRoutesOrIsRefusedAsDesigned()
    {
        var now = DateTimeOffset.UtcNow;

        // 1. A strategy run's order routes under the run's authority.
        var strategyGrant = new ExecutionAccountGrant(FundAccount, "live-trading-engine", ExecutionAccountGrantSources.StrategyRun, now);
        var strategy = await _oms.SubmitAsync(new OrderSubmitCommand(
            Order("rehearsal-strategy"), OrderSubmissionAuthority.ForStrategyRun("live-trading-engine", strategyGrant, "run-paper-1")));
        strategy.Success.Should().BeTrue(strategy.ErrorMessage);
        strategy.AuthorityDecision!.Origin.Should().Be(OrderCommandOrigin.StrategyRun);

        // 2. An operator ticket for the granted account routes in paper.
        var ticketGrant = new ExecutionAccountGrant(FundAccount, "ops", ExecutionAccountGrantSources.HttpPrincipal, now);
        var ticket = await _oms.SubmitAsync(new OrderSubmitCommand(
            Order("rehearsal-ticket"), OrderSubmissionAuthority.ForOperatorTicket("ops", ticketGrant, "corr-ticket")));
        ticket.Success.Should().BeTrue(ticket.ErrorMessage);

        // 3. A ticket for an account the operator was not granted is refused before risk.
        var wrongAccount = await _oms.SubmitAsync(new OrderSubmitCommand(
            Order("rehearsal-wrong-account") with { FundAccountId = OtherAccount },
            OrderSubmissionAuthority.ForOperatorTicket("ops", ticketGrant, "corr-wrong")));
        wrongAccount.Success.Should().BeFalse();
        wrongAccount.AuthorityDecision!.ReasonCode.Should().Be(OrderAuthorityReasonCodes.AccountGrantMismatch);
        wrongAccount.AuthorityDecision.Checkpoint.Should().Be("preflight");

        // 4. Kill-switch authority can cancel but never submit.
        var killSwitch = await _oms.SubmitAsync(new OrderSubmitCommand(
            Order("rehearsal-kill-switch"), OrderSubmissionAuthority.ForKillSwitch("ops", "sweep-1")));
        killSwitch.Success.Should().BeFalse();
        killSwitch.AuthorityDecision!.ReasonCode.Should().Be(OrderAuthorityReasonCodes.KillSwitchSubmitNotAllowed);

        // 5. A legacy untyped order still routes in paper.
        var legacy = await _oms.PlaceOrderAsync(Order("rehearsal-legacy"));
        legacy.Success.Should().BeTrue(legacy.ErrorMessage);

        // The working-order book records who originated each order that routed.
        _oms.GetOpenOrders()
            .Select(order => (order.OrderId, order.Origin))
            .Should().BeEquivalentTo(new[]
            {
                ("rehearsal-strategy", (OrderCommandOrigin?)OrderCommandOrigin.StrategyRun),
                ("rehearsal-ticket", (OrderCommandOrigin?)OrderCommandOrigin.OperatorTicket),
                ("rehearsal-legacy", (OrderCommandOrigin?)OrderCommandOrigin.Unattributed)
            });
        _oms.GetCompletedOrders().Select(order => order.Origin)
            .Should().Contain(new OrderCommandOrigin?[] { OrderCommandOrigin.OperatorTicket, OrderCommandOrigin.KillSwitch },
                "refused orders keep their origin in the completed feed");
    }

    // Limit orders far below the market rest, so routed orders stay on the working-order book.
    private static OrderRequest Order(string clientOrderId) => new()
    {
        ClientOrderId = clientOrderId,
        Symbol = "AAPL",
        Side = OrderSide.Buy,
        Type = OrderType.Limit,
        Quantity = 1m,
        LimitPrice = 1m,
        FundAccountId = FundAccount
    };
}
