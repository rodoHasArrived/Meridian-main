using FluentAssertions;
using Meridian.Execution;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

using ExecutionGateway = Meridian.Execution.PaperTradingGateway;

namespace Meridian.Tests.Execution;

/// <summary>
/// The kill-switch sweep records the authority that invoked it on every cancellation it makes
/// (governed order command boundary): the tracked order's journal and audit evidence, the
/// withdrawn parked escalation, and one record for the sweep itself. Authority never refuses a
/// cancellation; an unattributed sweep keeps its earlier evidence unchanged.
/// </summary>
public sealed class KillSwitchAuthorityRecordingTests : IDisposable
{
    private static readonly Guid AccountId = Guid.Parse("53bf0251-17f6-4fb7-8dbe-6fb4966e2749");
    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-tests", "kill-switch-authority", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task AttributedSweep_RecordsTheKillSwitchAuthorityOnEveryCancellationAndOnTheSweep()
    {
        await using var audit = new ExecutionAuditTrailService(Path.Combine(_root, "audit"), NullLogger<ExecutionAuditTrailService>.Instance);
        var queue = new RiskEscalationQueueService(
            NullLogger<RiskEscalationQueueService>.Instance,
            options: new RiskEscalationQueueOptions(Path.Combine(_root, "escalations.json")));
        using var oms = CreateOms(audit, queue);
        await oms.StartAsync();

        (await oms.PlaceOrderAsync(RestingOrder("resting-1", "AAPL"))).Success.Should().BeTrue();
        (await oms.PlaceOrderAsync(RestingOrder("parked-1", "MSFT"))).RequiresApproval.Should().BeTrue();

        var sweep = await oms.CancelAllAsync(new OrderCancelAllCommand(OrderSubmissionAuthority.ForKillSwitch("ops", "sweep-1")));

        sweep.Outcome.Should().Be(KillSwitchSweepOutcome.Completed);
        var entries = await audit.GetAllAsync();

        var cancellations = entries.Where(entry => entry.Action == "OrderCancelled").ToList();
        cancellations.Select(entry => entry.OrderId).Should().BeEquivalentTo(["resting-1", "parked-1"],
            "the resting order is cancelled and the parked escalation withdrawn");
        cancellations.Should().AllSatisfy(entry =>
        {
            entry.Actor.Should().Be("ops");
            entry.CorrelationId.Should().Be("sweep-1");
            entry.Metadata!["commandOrigin"].Should().Be(nameof(OrderCommandOrigin.KillSwitch));
        });

        var sweepRecord = entries.Should().ContainSingle(entry => entry.Action == "KillSwitchSweep").Subject;
        sweepRecord.Actor.Should().Be("ops");
        sweepRecord.CorrelationId.Should().Be("sweep-1");
        sweepRecord.Outcome.Should().Be(nameof(KillSwitchSweepOutcome.Completed));
        sweepRecord.Metadata!["cancelled"].Should().Be("1",
            "the sweep counts broker cancellations; a withdrawn escalation had nothing working at the broker");
    }

    [Fact]
    public async Task UnattributedSweep_KeepsItsEarlierEvidence()
    {
        await using var audit = new ExecutionAuditTrailService(Path.Combine(_root, "audit"), NullLogger<ExecutionAuditTrailService>.Instance);
        using var oms = CreateOms(audit, queue: null);
        await oms.StartAsync();
        (await oms.PlaceOrderAsync(RestingOrder("resting-1", "AAPL"))).Success.Should().BeTrue();

        (await oms.CancelAllAsync()).Outcome.Should().Be(KillSwitchSweepOutcome.Completed);

        var entries = await audit.GetAllAsync();
        entries.Should().NotContain(entry => entry.Action == "KillSwitchSweep");
        var cancellation = entries.Should().ContainSingle(entry => entry.Action == "OrderCancelled").Subject;
        cancellation.Actor.Should().BeNull();
        cancellation.Metadata?.Keys.Should().NotContain("commandOrigin");
    }

    [Fact]
    public async Task AttributedSweep_RecordsTheAuthorityOnTheCancelJournalEntry()
    {
        await using var journal = new ExecutionJournal(Path.Combine(_root, "journal"))
        {
            OpeningEvidenceSource = (_, _) => new(true, true, DateTimeOffset.UtcNow, "test opening evidence")
        };
        using var oms = new OrderManagementSystem(
            new ExecutionGateway(NullLogger<ExecutionGateway>.Instance),
            NullLogger<OrderManagementSystem>.Instance,
            journal: journal);
        await oms.StartAsync();
        (await oms.PlaceOrderAsync(RestingOrder("journaled-1", "AAPL"))).Success.Should().BeTrue();

        await oms.CancelAllAsync(new OrderCancelAllCommand(OrderSubmissionAuthority.ForKillSwitch("risk-engine", "trip-1")));

        var cancelIntent = (await journal.ReadAsync()).Should()
            .ContainSingle(entry => entry.OrderId == "journaled-1" && entry.Kind == "CancelIntent").Subject;
        cancelIntent.Authority!.Origin.Should().Be(OrderCommandOrigin.KillSwitch);
        cancelIntent.Authority.Actor.Should().Be("risk-engine");
        cancelIntent.Authority.CorrelationId.Should().Be("trip-1");
    }

    [Fact]
    public void CancelAllCommand_AcceptsOnlyKillSwitchAuthority()
    {
        var ticket = OrderSubmissionAuthority.ForOperatorTicket(
            "ops",
            new ExecutionAccountGrant(AccountId, "ops", ExecutionAccountGrantSources.HttpPrincipal, DateTimeOffset.UtcNow),
            "corr-1");

        var build = () => new OrderCancelAllCommand(ticket);

        build.Should().Throw<ArgumentException>("only the kill switch may sweep the whole book");
    }

    // MSFT escalates and is parked; everything else is approved and rests below the market.
    private static OrderManagementSystem CreateOms(ExecutionAuditTrailService audit, RiskEscalationQueueService? queue)
    {
        IRiskValidator? validator = null;
        if (queue is not null)
        {
            validator = RiskValidatorSubstitute.Create();
            validator.ValidateOrderAsync(Arg.Any<OrderRequest>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    var request = call.Arg<OrderRequest>();
                    if (request.Symbol != "MSFT")
                        return RiskValidationResult.Approved();
                    var parked = queue.Park(request, "above band", actor: "ops");
                    return RiskValidationResult.Escalated($"Parked for governed approval ({parked.EscalationId})", parked.EscalationId);
                });
        }

        return new OrderManagementSystem(
            new ExecutionGateway(NullLogger<ExecutionGateway>.Instance),
            NullLogger<OrderManagementSystem>.Instance,
            riskValidator: validator,
            auditTrail: audit,
            escalationQueue: queue);
    }

    private static OrderRequest RestingOrder(string clientOrderId, string symbol) => new()
    {
        ClientOrderId = clientOrderId,
        Symbol = symbol,
        Side = OrderSide.Buy,
        Type = OrderType.Limit,
        Quantity = 1m,
        LimitPrice = 1m,
        FundAccountId = AccountId
    };
}
