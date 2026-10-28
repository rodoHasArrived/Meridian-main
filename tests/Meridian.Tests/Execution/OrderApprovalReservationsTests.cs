using FluentAssertions;
using Meridian.Execution.OrderManagement;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using OrderType = Meridian.Execution.Sdk.OrderType;

namespace Meridian.Tests.Execution;

public sealed class OrderApprovalReservationsTests
{
    [Fact]
    public async Task ApprovalToken_OnlyPassesAfterApproval_AndDoesNotReleaseTheIdentityEarly()
    {
        await using var fixture = new QueueFixture();
        var entry = fixture.Queue.Park(Request(), "risk band");
        var reservations = fixture.CreateReservations();
        reservations.RehydrateParkedOrderReservations();
        var release = WithTokens(Request(), entry.EscalationId);

        reservations.IsReservedByLiveEscalation("CLIENT-1", release).Should().BeTrue();
        fixture.Queue.Approve(entry.EscalationId, "risk-desk").Should().NotBeNull();
        reservations.IsReservedByLiveEscalation("CLIENT-1", release).Should().BeFalse();
        reservations.Contains("CLIENT-1").Should().BeTrue("later pre-trade gates can still refuse the release");
        reservations.IsReservedByLiveEscalation("client-1", Request()).Should().BeTrue();

        reservations.Release("CLIENT-1");
        reservations.Contains("client-1").Should().BeFalse();
    }

    [Fact]
    public async Task Rehydrate_ReopenedQueue_ReservesOnlyUnresolvedClientIdentities()
    {
        await using var fixture = new QueueFixture();
        fixture.Queue.Park(Request("pending"), "risk band");
        var approved = fixture.Queue.Park(Request("approved"), "risk band");
        fixture.Queue.Approve(approved.EscalationId, "risk-desk");
        var denied = fixture.Queue.Park(Request("denied"), "risk band");
        fixture.Queue.Deny(denied.EscalationId, "risk-desk");
        var released = fixture.Queue.Park(Request("released"), "risk band");
        fixture.Queue.Approve(released.EscalationId, "risk-desk");
        fixture.Queue.TryConsumeApproval(WithTokens(Request("released"), released.EscalationId)).Should().NotBeNull();
        fixture.Queue.Park(Request(null), "no client identity");
        fixture.Queue.Park(Request(""), "empty client identity");
        await using var reopened = fixture.Reopen();
        var reservations = new OrderApprovalReservations(reopened, NullLogger.Instance);

        reservations.RehydrateParkedOrderReservations();

        reservations.SnapshotOrderIds().Should().BeEquivalentTo(new[] { "pending", "approved" });
        reservations.Contains("PENDING").Should().BeTrue();
    }

    [Fact]
    public async Task Withdrawal_ReleaseInFlight_RetainsReservationUntilQueueAcceptsWithdrawal()
    {
        await using var fixture = new QueueFixture();
        var entry = fixture.Queue.Park(Request(), "risk band");
        fixture.Queue.Approve(entry.EscalationId, "risk-desk");
        var reservations = fixture.CreateReservations();
        reservations.Reserve("client-1", entry.EscalationId);
        fixture.Queue.TryBeginRelease(entry.EscalationId).Should().BeTrue();

        reservations.TryWithdrawEscalation("CLIENT-1").Should().BeNull();
        reservations.Contains("client-1").Should().BeTrue();
        fixture.Queue.TryGet(entry.EscalationId)!.Status.Should().Be(RiskEscalationStatus.Approved);

        fixture.Queue.EndRelease(entry.EscalationId);
        reservations.TryWithdrawEscalation("CLIENT-1").Should().Be(entry.EscalationId);
        reservations.Contains("client-1").Should().BeFalse();
        await using var reopened = fixture.Reopen();
        reopened.TryGet(entry.EscalationId)!.Status.Should().Be(RiskEscalationStatus.Denied);
    }

    [Fact]
    public async Task DeclinedApproval_ClearsOnlyTheDeadReservation()
    {
        await using var fixture = new QueueFixture();
        var denied = fixture.Queue.Park(Request(), "risk band");
        var live = fixture.Queue.Park(Request("live"), "risk band");
        var reservations = fixture.CreateReservations();
        reservations.RehydrateParkedOrderReservations();
        reservations.WasRiskApprovalDeclined("client-1").Should().BeFalse();
        fixture.Queue.Deny(denied.EscalationId, "risk-desk");

        reservations.WasRiskApprovalDeclined("CLIENT-1").Should().BeTrue();
        reservations.WasRiskApprovalDeclined("client-1").Should().BeFalse();
        reservations.Contains("live").Should().BeTrue();
        fixture.Queue.TryGet(live.EscalationId)!.Status.Should().Be(RiskEscalationStatus.PendingApproval);
    }

    [Fact]
    public async Task RestoreConsumedApprovals_RearmsEachTokenAndRestoresCancellationOwnership()
    {
        await using var fixture = new QueueFixture();
        var first = fixture.Queue.Park(Request(), "first rule", ruleName: "first");
        var second = fixture.Queue.Park(Request(), "second rule", ruleName: "second");
        fixture.Queue.Approve(first.EscalationId, "risk-desk");
        fixture.Queue.Approve(second.EscalationId, "risk-desk");
        var tokens = RiskEscalationQueueService.JoinTokens([first.EscalationId, second.EscalationId]);
        fixture.Queue.TryConsumeApprovals(WithTokens(Request(), tokens)).Should().HaveCount(2);
        var reservations = fixture.CreateReservations();

        reservations.RestoreConsumedApprovals(tokens + RiskEscalationQueueService.TokenSeparator + "missing-token", "broker refusal", "client-1");

        fixture.Queue.TryGet(first.EscalationId)!.Status.Should().Be(RiskEscalationStatus.Approved);
        fixture.Queue.TryGet(second.EscalationId)!.Status.Should().Be(RiskEscalationStatus.Approved);
        reservations.Contains("CLIENT-1").Should().BeTrue();
        reservations.TryWithdrawEscalation("client-1").Should().Be(second.EscalationId);
    }

    [Fact]
    public async Task AmendmentApprovalRestore_DoesNotInterceptOrdinaryOrderCancellation()
    {
        await using var fixture = new QueueFixture();
        var entry = fixture.Queue.Park(Request(), "risk band");
        fixture.Queue.Approve(entry.EscalationId, "risk-desk");
        fixture.Queue.TryConsumeApproval(WithTokens(Request(), entry.EscalationId)).Should().NotBeNull();
        var reservations = fixture.CreateReservations();

        reservations.RestoreConsumedApprovals(entry.EscalationId, "amendment refused");

        fixture.Queue.TryGet(entry.EscalationId)!.Status.Should().Be(RiskEscalationStatus.Approved);
        reservations.SnapshotOrderIds().Should().BeEmpty();
    }

    [Fact]
    public void MissingQueue_OnlyClearsAReservationWhenItsLivenessIsExamined()
    {
        var reservations = new OrderApprovalReservations(null, NullLogger.Instance);
        reservations.Reserve("client-1", "orphaned");
        reservations.Contains("CLIENT-1").Should().BeTrue();
        reservations.TryWithdrawEscalation("client-1").Should().BeNull();
        reservations.Contains("client-1").Should().BeTrue();
        reservations.IsReservedByLiveEscalation("client-1", Request()).Should().BeFalse();
        reservations.Contains("client-1").Should().BeFalse();
    }

    private static OrderRequest Request(string? clientOrderId = "client-1") => new()
    {
        ClientOrderId = clientOrderId,
        Symbol = "SPY",
        Side = OrderSide.Buy,
        Type = OrderType.Limit,
        Quantity = 10m,
        LimitPrice = 100m
    };

    private static OrderRequest WithTokens(OrderRequest request, string tokens) => request with
    {
        Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        { [RiskEscalationQueueService.ApprovalMetadataKey] = tokens }
    };

    private sealed class QueueFixture : IAsyncDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "Meridian.Tests", $"approval-owner-{Guid.NewGuid():N}");
        private readonly RiskEscalationQueueOptions _options;
        internal RiskEscalationQueueService Queue { get; }

        internal QueueFixture()
        {
            _options = new RiskEscalationQueueOptions(Path.Combine(_directory, "escalations.json"));
            Queue = Reopen();
        }

        internal RiskEscalationQueueService Reopen() => new(NullLogger<RiskEscalationQueueService>.Instance, options: _options);
        internal OrderApprovalReservations CreateReservations() => new(Queue, NullLogger.Instance);

        public async ValueTask DisposeAsync()
        {
            await Queue.DisposeAsync();
            if (Directory.Exists(_directory))
                Directory.Delete(_directory, recursive: true);
        }
    }
}
