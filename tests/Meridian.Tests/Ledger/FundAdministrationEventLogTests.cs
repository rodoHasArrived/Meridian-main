using FluentAssertions;
using Meridian.Ledger;
using Xunit;

namespace Meridian.Tests.Ledger;

/// <summary>
/// Tests the tamper-evident fund-administration event log: append-only ordering, hash chaining,
/// integrity verification, and subject/kind querying.
/// </summary>
public sealed class FundAdministrationEventLogTests
{
    private static readonly DateTimeOffset At = new(2026, 06, 30, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Append_AssignsMonotonicSequenceAndChainsHashes()
    {
        var log = new FundAdministrationEventLog();

        var first = log.Append(FundAdministrationEventKind.PeriodLocked, "controller", "FUND:2026-Q2", "Locked Q2", occurredAtUtc: At);
        var second = log.Append(FundAdministrationEventKind.ReportExported, "ops", "report-1", "Exported report", occurredAtUtc: At.AddMinutes(1));

        first.Sequence.Should().Be(1);
        second.Sequence.Should().Be(2);
        first.PreviousHash.Should().BeNull("the first event has no predecessor");
        first.Hash.Should().NotBeNullOrEmpty();
        second.PreviousHash.Should().Be(first.Hash, "each event chains to its predecessor's hash");
    }

    [Fact]
    public void VerifyIntegrity_UntamperedChain_ReturnsTrue()
    {
        var log = new FundAdministrationEventLog();
        for (var i = 0; i < 10; i++)
        {
            log.Append(FundAdministrationEventKind.JournalPosted, "poster", $"book:{i}", $"Posted {i}", occurredAtUtc: At.AddMinutes(i));
        }

        log.VerifyIntegrity().Should().BeTrue();
        log.Events.Should().HaveCount(10);
    }

    [Fact]
    public void Append_IdenticalContentAcrossLogs_ProducesIdenticalDeterministicChain()
    {
        // The content hash excludes the random EventId, so two logs fed identical governance
        // content at identical timestamps must produce identical hash chains — the property that
        // lets an auditor recompute and confirm the chain.
        var left = new FundAdministrationEventLog();
        var right = new FundAdministrationEventLog();

        var leftFirst = left.Append(FundAdministrationEventKind.PeriodLocked, "controller", "FUND:2026-Q2", "Locked Q2", occurredAtUtc: At);
        var rightFirst = right.Append(FundAdministrationEventKind.PeriodLocked, "controller", "FUND:2026-Q2", "Locked Q2", occurredAtUtc: At);

        leftFirst.Hash.Should().Be(rightFirst.Hash);
    }

    [Fact]
    public void EventsForAndEventsOfKind_FilterCorrectly()
    {
        var log = new FundAdministrationEventLog();
        log.Append(FundAdministrationEventKind.PeriodLocked, "controller", "FUND:2026-Q2", "Locked", occurredAtUtc: At);
        log.Append(FundAdministrationEventKind.PeriodReopened, "controller", "FUND:2026-Q2", "Reopened", occurredAtUtc: At.AddHours(1));
        log.Append(FundAdministrationEventKind.PeriodLocked, "controller", "FUND:2026-Q3", "Locked", occurredAtUtc: At.AddHours(2));

        log.EventsFor("FUND:2026-Q2").Should().HaveCount(2);
        log.EventsOfKind(FundAdministrationEventKind.PeriodLocked).Should().HaveCount(2);
        log.EventsOfKind(FundAdministrationEventKind.PeriodReopened).Should().ContainSingle();
    }

    [Fact]
    public void Append_BlankActor_Throws()
    {
        var log = new FundAdministrationEventLog();
        var act = () => log.Append(FundAdministrationEventKind.PeriodLocked, "  ", "subject", "summary");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Append_CarriesEvidenceReferences()
    {
        var log = new FundAdministrationEventLog();
        var evidence = new JournalEvidenceReference(
            "ev-1", "vault://approval/1", "Approval", "Governance", At, "approver");

        var evt = log.Append(
            FundAdministrationEventKind.PeriodReopened,
            "approver",
            "FUND:2026-Q2",
            "Reopened with approval",
            evidence: [evidence],
            occurredAtUtc: At);

        evt.Evidence.Should().ContainSingle();
        evt.Evidence[0].EvidenceId.Should().Be("ev-1");
        log.VerifyIntegrity().Should().BeTrue();
    }
}
