using FluentAssertions;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meridian.Tests.Execution;

/// <summary>
/// Synthetic host interruption and audit replay during a session halt. Partial checkpoints remain
/// inspectable, duplicate durable delivery is idempotent, and contradictory history is refused.
/// </summary>
public sealed class CancelAllIncidentReplayTests : IDisposable
{
    private static readonly Guid AccountId = Guid.Parse("19ac8cd2-fdf8-4e29-a8ad-263ec272fd36");
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Meridian.Tests", "cancel-replay", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SessionHalt_HostStopsBeforeCompletion_ReopensCheckpointWithExplicitlyIncompleteCoverage()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var header = Header();
        var row = Row();
        await using (var audit = CreateAudit())
        {
            await AppendAsync(audit, new(header.IncidentId, 1, Snapshot: header), timeout.Token);
            await AppendAsync(audit, new(header.IncidentId, 2, Snapshot: header, Order: row), timeout.Token);
            // The process stopped after this order's outcome but before it completed the sweep.
        }

        await using var reopened = CreateAudit();
        var incident = (await reopened.GetCancelAllIncidentAsync(header.IncidentId, AccountId, timeout.Token))!;

        incident.CompletedAt.Should().BeNull();
        incident.EvidenceStatus.Should().Be("InProgress");
        incident.EvidenceError.Should().Contain("no retained completion");
        incident.BrokerViewUnavailable.Should().BeTrue();
        incident.Orders.Should().ContainSingle().Which.Should().BeEquivalentTo(row);
    }

    [Fact]
    public async Task SessionHalt_ReplayedIdenticalDelta_DoesNotDuplicateOrdersOrChangeAccount()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var header = Header();
        var row = Row();
        await using (var audit = CreateAudit())
        {
            await AppendAsync(audit, new(header.IncidentId, 1, Snapshot: header), timeout.Token);
            var delta = new CancelAllIncidentUpdate(header.IncidentId, 2, Snapshot: header, Order: row);
            await AppendAsync(audit, delta, timeout.Token);
            await AppendAsync(audit, delta, timeout.Token);
            await AppendAsync(audit, new(header.IncidentId, 3, Snapshot: header with
            {
                CompletedAt = row.ObservedAt, EvidenceStatus = "Complete", BrokerViewUnavailable = false,
                BrokerViewError = null, Orders = [row]
            }), timeout.Token);
        }

        await using var reopened = CreateAudit();
        var incident = (await reopened.GetCancelAllIncidentAsync(header.IncidentId, AccountId, timeout.Token))!;
        incident.Orders.Should().ContainSingle().Which.OrderId.Should().Be("resting");
        incident.EvidenceStatus.Should().Be("Complete");
        (await reopened.GetCancelAllIncidentAsync(header.IncidentId, Guid.NewGuid(), timeout.Token)).Should().BeNull();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SessionHalt_ContradictoryDurableDelivery_RefusesToPresentACompletedIncident(bool conflictingSequence)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var header = Header();
        await using (var audit = CreateAudit())
        {
            await AppendAsync(audit, new(header.IncidentId, 1, Snapshot: header, Order: Row()), timeout.Token);
            var conflict = conflictingSequence
                ? new CancelAllIncidentUpdate(header.IncidentId, 1, Snapshot: header, Order: Row() with { RemainingQuantity = 20m })
                : new CancelAllIncidentUpdate(header.IncidentId, 2, Snapshot: header with { FundAccountId = Guid.NewGuid() });
            await AppendAsync(audit, conflict, timeout.Token);
        }

        await using var reopened = CreateAudit();
        var read = () => reopened.GetCancelAllIncidentAsync(header.IncidentId, AccountId, timeout.Token);
        await read.Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task SessionHalt_CorruptedAuditSegment_RefusesCompleteEvidenceReadback()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var header = Header();
        await using (var audit = CreateAudit())
            await AppendAsync(audit, new(header.IncidentId, 1, Snapshot: header with
            { CompletedAt = header.StartedAt, EvidenceStatus = "Complete", Orders = [Row()] }), timeout.Token);
        var segment = Directory.GetFiles(Path.Combine(_root, "wal"), "*.wal").Single();
        await File.AppendAllTextAsync(segment, "invalid-audit-record\n", timeout.Token);

        await using var reopened = CreateAudit();
        var read = () => reopened.GetCancelAllIncidentAsync(header.IncidentId, AccountId, timeout.Token);
        await read.Should().ThrowAsync<InvalidDataException>();
    }

    [Theory]
    [InlineData("account", false)]
    [InlineData("account", true)]
    [InlineData("order", false)]
    [InlineData("order", true)]
    [InlineData("drop", true)]
    public async Task SessionHalt_ReplayChangesObservedOrderOwnershipOrDropsIt_RefusesContradictoryEvidence(string change, bool final)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var header = Header();
        var row = change == "account" ? Row() with { FundAccountId = Guid.NewGuid() }
            : change == "order" ? Row() with { OrderId = "different-order" } : Row();
        await using (var audit = CreateAudit())
        {
            await AppendAsync(audit, new(header.IncidentId, 1, Snapshot: header, Order: Row()), timeout.Token);
            await AppendAsync(audit, final
                ? new(header.IncidentId, 2, Snapshot: header with
                {
                    CompletedAt = row.ObservedAt, EvidenceStatus = "Complete", Orders = change == "drop" ? [] : [row]
                })
                : new(header.IncidentId, 2, Snapshot: header, Order: row), timeout.Token);
        }

        await using var reopened = CreateAudit();
        var read = () => reopened.GetCancelAllIncidentAsync(header.IncidentId, AccountId, timeout.Token);
        await read.Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task SessionHalt_InitiallyUnattributedSubmissionGetsVerifiedAccount_ReplayAllowsOneWayAttribution()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var header = Header();
        await using (var audit = CreateAudit())
        {
            await AppendAsync(audit, new(header.IncidentId, 1, Snapshot: header, Order: Row() with { FundAccountId = null }), timeout.Token);
            await AppendAsync(audit, new(header.IncidentId, 2, Snapshot: header, Order: Row()), timeout.Token);
            await AppendAsync(audit, new(header.IncidentId, 3, Snapshot: header with
            { CompletedAt = Row().ObservedAt, EvidenceStatus = "Complete", Orders = [Row()] }), timeout.Token);
        }

        await using var reopened = CreateAudit();
        var incident = (await reopened.GetCancelAllIncidentAsync(header.IncidentId, AccountId, timeout.Token))!;
        incident.Orders.Should().ContainSingle().Which.FundAccountId.Should().Be(AccountId);
        incident.EvidenceStatus.Should().Be("Complete");
    }

    private static CancelAllIncident Header() => new()
    {
        IncidentId = "cancel-retained-session-halt", StartedAt = new(2026, 11, 2, 14, 30, 0, TimeSpan.Zero),
        GatewayId = "synthetic-broker", ExecutionMode = ExecutionMode.Paper, FundAccountId = AccountId,
        BrokerScopeIdentity = "paper:account", Actor = "solo-operator", CorrelationId = "incident-replay"
    };

    private static CancelAllOrderOutcome Row() => new()
    {
        ObservationKey = "local:resting", OrderId = "resting", BrokerOrderId = "broker-resting",
        FundAccountId = AccountId, Symbol = "AAPL", Side = OrderSide.Buy,
        InitialStatus = OrderStatus.Accepted, Status = OrderStatus.Cancelled,
        InitialQuantity = 10m, InitialFilledQuantity = 0m, Quantity = 10m, FilledQuantity = 0m, RemainingQuantity = 0m,
        Disposition = CancelAllOrderDisposition.ConfirmedCancelled, CancellationAttempted = true,
        ObservedAt = new(2026, 11, 2, 14, 30, 1, TimeSpan.Zero)
    };

    private static Task AppendAsync(ExecutionAuditTrailService audit, CancelAllIncidentUpdate update, CancellationToken ct) =>
        audit.RecordAsync(new ExecutionAuditEntry($"test-{Guid.NewGuid():N}", "Order", "CancelAllIncident", "Observed",
            update.Snapshot?.StartedAt ?? update.Order!.ObservedAt, CancelAllEvidence: update), ct);

    private ExecutionAuditTrailService CreateAudit() => new(_root, NullLogger<ExecutionAuditTrailService>.Instance);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
