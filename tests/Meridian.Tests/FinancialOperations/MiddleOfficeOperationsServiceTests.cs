using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.Domain.Reconciliation;
using Meridian.FinancialOperations.MiddleOffice;
using Meridian.Ledger;
using Xunit;

namespace Meridian.Tests.FinancialOperations;

/// <summary>
/// Tests the middle-office managed-service workflow primitive: T+0 booking with a T+1 reconciliation
/// due date, true-break escalation against SLA timers, and normalized file distribution with an
/// archived, tamper-evident delivery log.
/// </summary>
public sealed class MiddleOfficeOperationsServiceTests
{
    private static readonly DateTimeOffset At = new(2026, 06, 30, 12, 0, 0, TimeSpan.Zero);

    private static MiddleOfficeOperationsService NewService(out FundAdministrationEventLog log)
    {
        log = new FundAdministrationEventLog();
        return new MiddleOfficeOperationsService(log);
    }

    [Fact]
    public void BookTrade_DerivesSettlementAndT1ReconciliationDate()
    {
        var service = NewService(out _);

        var booking = service.BookTrade(new TradeBookingRequest(
            "ACC-1",
            "aapl",
            ReconciliationDimension.Trade,
            Quantity: 100m,
            Amount: 19_000m,
            Currency: "usd",
            TradeDate: new DateOnly(2026, 6, 30),
            SettlementCycleDays: 1,
            BookedBy: "ops"));

        booking.Symbol.Should().Be("AAPL");
        booking.SettlementDate.Should().Be(new DateOnly(2026, 7, 1), "T+1 settlement");
        booking.ReconciliationDueDate.Should().Be(new DateOnly(2026, 7, 1), "reconciliation is due T+1");
        service.BookingsDueForReconciliation(new DateOnly(2026, 7, 1)).Should().ContainSingle();
        service.BookingsDueForReconciliation(new DateOnly(2026, 6, 30)).Should().BeEmpty();
    }

    [Fact]
    public void RaiseTrueBreak_MatchedRow_IsRejected()
    {
        var service = NewService(out _);
        var act = () => service.RaiseTrueBreak(new TrueBreakEscalationRequest(
            "brk-1", BreakClassification.Matched, ReconciliationBreakSeverity.Low, "n/a", "ops"));

        act.Should().Throw<ArgumentException>("matched rows are not exceptions");
    }

    [Fact]
    public void RaiseTrueBreak_CreatesOpenEscalationAndLogsIt()
    {
        var service = NewService(out var log);

        var escalation = service.RaiseTrueBreak(new TrueBreakEscalationRequest(
            "brk-1",
            BreakClassification.TrueBreak,
            ReconciliationBreakSeverity.High,
            "Position quantity variance",
            "recon-analyst",
            RaisedAtUtc: At));

        escalation.Level.Should().Be(0);
        escalation.Status.Should().Be(TrueBreakEscalationStatus.Open);
        escalation.Timer!.DueAtUtc.Should().Be(At.AddHours(8), "High severity defaults to an 8-hour SLA");
        service.OpenEscalations.Should().ContainSingle();
        log.EventsOfKind(FundAdministrationEventKind.ReconciliationBreakEscalated).Should().ContainSingle();
    }

    [Fact]
    public void EscalateOverdue_BreachedTimer_AdvancesLevelAndLogsSlaBreach()
    {
        var service = NewService(out var log);
        service.RaiseTrueBreak(new TrueBreakEscalationRequest(
            "brk-1",
            BreakClassification.TrueBreak,
            ReconciliationBreakSeverity.Critical,
            "Cash out of balance",
            "recon-analyst",
            RaisedAtUtc: At));

        // Critical SLA is 4h; evaluate 5h later so the timer has breached.
        var advanced = service.EscalateOverdue(At.AddHours(5));

        advanced.Should().ContainSingle();
        advanced[0].Level.Should().Be(1);
        advanced[0].Status.Should().Be(TrueBreakEscalationStatus.Escalated);
        log.EventsOfKind(FundAdministrationEventKind.SlaBreached).Should().ContainSingle();
        log.VerifyIntegrity().Should().BeTrue();
    }

    [Fact]
    public void EscalateOverdue_TimerOnTrack_DoesNothing()
    {
        var service = NewService(out _);
        service.RaiseTrueBreak(new TrueBreakEscalationRequest(
            "brk-1", BreakClassification.TrueBreak, ReconciliationBreakSeverity.Medium, "reason", "ops", RaisedAtUtc: At));

        // Medium SLA is 24h; one hour in, nothing is overdue.
        service.EscalateOverdue(At.AddHours(1)).Should().BeEmpty();
    }

    [Fact]
    public void ResolveBreak_StopsTimerAndClosesEscalation()
    {
        var service = NewService(out _);
        var escalation = service.RaiseTrueBreak(new TrueBreakEscalationRequest(
            "brk-1", BreakClassification.TrueBreak, ReconciliationBreakSeverity.High, "reason", "ops", RaisedAtUtc: At));

        var resolved = service.ResolveBreak(escalation.EscalationId, "supervisor", "Confirmed custodian error corrected", At.AddHours(2));

        resolved.Status.Should().Be(TrueBreakEscalationStatus.Resolved);
        resolved.Timer!.StateAt(At.AddHours(10)).Should().Be(WorkflowSlaState.Stopped);
        service.OpenEscalations.Should().BeEmpty();
    }

    [Fact]
    public void Distribute_FansOutToNormalizedRecipientsAndArchivesDeliveryLog()
    {
        var service = NewService(out var log);

        var records = service.Distribute(new FileDistributionRequest(
            "nav-pack.zip",
            "application/zip",
            ContentSha256: "abc123",
            ContentLength: 2048,
            Recipients:
            [
                new DistributionRecipient(DistributionRecipientKind.Administrator, "Acme Admin", "SecurePortal", "portal://acme"),
                new DistributionRecipient(DistributionRecipientKind.Custodian, "BigBank Custody", "SFTP", "sftp://bigbank"),
                new DistributionRecipient(DistributionRecipientKind.Counterparty, "Prime Broker", "Email", "ops@prime"),
            ],
            DistributedBy: "fund-ops",
            DistributedAtUtc: At));

        records.Should().HaveCount(3);
        records.Select(r => r.Recipient.Kind).Distinct().Should().HaveCount(3, "each normalized recipient kind is delivered to");
        records.Should().OnlyContain(r => r.Status == FileDeliveryStatus.Delivered);
        records.Select(r => r.DistributionId).Distinct().Should().ContainSingle("all deliveries share one distribution id");

        service.DeliveryLog.Should().HaveCount(3);
        log.EventsOfKind(FundAdministrationEventKind.FileDelivered).Should().HaveCount(3);
        log.VerifyIntegrity().Should().BeTrue();
    }

    [Fact]
    public void WorkflowSlaTimer_TransitionsThroughOnTrackWarningBreached()
    {
        var timer = new WorkflowSlaTimer("t1", "brk-1", new WorkflowSlaPolicy("p", TimeSpan.FromHours(4)), At);

        timer.StateAt(At.AddHours(1)).Should().Be(WorkflowSlaState.OnTrack);
        timer.StateAt(At.AddHours(3)).Should().Be(WorkflowSlaState.Warning, "warning at 50% of a 4h window is 2h");
        timer.StateAt(At.AddHours(5)).Should().Be(WorkflowSlaState.Breached);
        timer.Stop(At.AddHours(1)).StateAt(At.AddHours(5)).Should().Be(WorkflowSlaState.Stopped);
    }

    [Fact]
    public void BookTrade_FridayTradeWithT1_SettlesAndReconcilesNextBusinessDay()
    {
        var service = NewService(out _);

        // 2026-07-03 is a Friday; T+1 in business days settles the following Monday, not Saturday.
        var booking = service.BookTrade(new TradeBookingRequest(
            "ACC-1",
            "AAPL",
            ReconciliationDimension.Trade,
            Quantity: 100m,
            Amount: 19_000m,
            Currency: "USD",
            TradeDate: new DateOnly(2026, 7, 3),
            SettlementCycleDays: 1,
            BookedBy: "ops"));

        booking.SettlementDate.Should().Be(new DateOnly(2026, 7, 6), "Friday + 1 business day is Monday");
        booking.ReconciliationDueDate.Should().Be(new DateOnly(2026, 7, 6));
    }

    [Fact]
    public void RaiseTrueBreak_WithSubject_CorrelatesEventToSubjectAndRetainsBreakId()
    {
        var service = NewService(out var log);

        var escalation = service.RaiseTrueBreak(new TrueBreakEscalationRequest(
            "brk-1",
            BreakClassification.TrueBreak,
            ReconciliationBreakSeverity.High,
            "Position variance",
            "ops",
            SubjectId: "FUND-A",
            RaisedAtUtc: At));

        escalation.SubjectId.Should().Be("FUND-A");
        log.EventsFor("FUND-A").Should().ContainSingle("the event is keyed by the supplied subject");
        log.EventsFor("brk-1").Should().BeEmpty("the break id is not the subject when a subject is supplied");
        var evt = log.EventsOfKind(FundAdministrationEventKind.ReconciliationBreakEscalated).Should().ContainSingle().Which;
        evt.Attributes["breakId"].Should().Be("brk-1");
    }

    [Fact]
    public void Distribute_FailedTransport_RecordsFailedWithoutDeliveredEvent()
    {
        var log = new FundAdministrationEventLog();
        var service = new MiddleOfficeOperationsService(log, new FailingTransport());

        var records = service.Distribute(new FileDistributionRequest(
            "nav-pack.zip",
            "application/zip",
            ContentSha256: "abc123",
            ContentLength: 2048,
            Recipients: [new DistributionRecipient(DistributionRecipientKind.Custodian, "BigBank Custody", "SFTP", "sftp://bigbank")],
            DistributedBy: "fund-ops",
            DistributedAtUtc: At));

        records.Should().ContainSingle();
        records[0].Status.Should().Be(FileDeliveryStatus.Failed);
        records[0].FailureReason.Should().Be("unreachable host");
        service.DeliveryLog.Should().ContainSingle("failed attempts are still archived in the delivery log");
        log.EventsOfKind(FundAdministrationEventKind.FileDelivered)
            .Should().BeEmpty("a failed delivery must not be recorded as delivery evidence");
    }

    [Fact]
    public void BookTrade_ConflictingId_Throws_ButExactRetryIsIdempotent()
    {
        var service = NewService(out _);
        var request = new TradeBookingRequest(
            "ACC-1", "AAPL", ReconciliationDimension.Trade, 100m, 19_000m, "USD",
            new DateOnly(2026, 6, 30), SettlementCycleDays: 1, BookedBy: "ops", BookingId: "bk-1");

        var first = service.BookTrade(request);
        var retry = service.BookTrade(request);

        retry.BookingId.Should().Be(first.BookingId);
        service.Bookings.Should().ContainSingle("an exact retry is idempotent");

        // Re-using the id with different economics must be rejected rather than silently overwriting.
        var act = () => service.BookTrade(request with { Amount = 25_000m });
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void RaiseTrueBreak_DuplicateActiveBreak_ReturnsExistingCaseWithoutDuplicating()
    {
        var service = NewService(out var log);
        var request = new TrueBreakEscalationRequest(
            "brk-1", BreakClassification.TrueBreak, ReconciliationBreakSeverity.High, "Position variance", "ops", RaisedAtUtc: At);

        var first = service.RaiseTrueBreak(request);
        var retry = service.RaiseTrueBreak(request);

        retry.EscalationId.Should().Be(first.EscalationId, "an already-open break is not escalated twice");
        service.OpenEscalations.Should().ContainSingle("a single break carries one active operator case");
        log.EventsOfKind(FundAdministrationEventKind.ReconciliationBreakEscalated)
            .Should().ContainSingle("the duplicate raise records no second escalation event");
    }

    [Fact]
    public void RaiseTrueBreak_ResolvedBreak_CanBeRaisedAgain()
    {
        var service = NewService(out _);
        var first = service.RaiseTrueBreak(new TrueBreakEscalationRequest(
            "brk-1", BreakClassification.TrueBreak, ReconciliationBreakSeverity.High, "variance", "ops", RaisedAtUtc: At));
        service.ResolveBreak(first.EscalationId, "supervisor", "custodian error corrected", At.AddHours(1));

        var second = service.RaiseTrueBreak(new TrueBreakEscalationRequest(
            "brk-1", BreakClassification.TrueBreak, ReconciliationBreakSeverity.High, "recurred", "ops", RaisedAtUtc: At.AddHours(2)));

        second.EscalationId.Should().NotBe(first.EscalationId, "a resolved break may recur as a fresh escalation");
        service.OpenEscalations.Should().ContainSingle();
    }

    private sealed class FailingTransport : IFileDistributionTransport
    {
        public FileDeliveryOutcome Deliver(DistributionRecipient recipient, FileDistributionRequest request)
            => new(Delivered: false, FailureReason: "unreachable host");
    }
}
