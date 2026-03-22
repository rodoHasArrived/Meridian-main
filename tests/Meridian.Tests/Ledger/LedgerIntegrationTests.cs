using FluentAssertions;
using Meridian.Ledger;
using Xunit;

namespace Meridian.Tests.Ledger;

public sealed class LedgerIntegrationTests
{
    [Fact]
    public void Post_WithUnbalancedJournal_ThrowsLedgerValidationException()
    {
        var ledger = new Meridian.Ledger.Ledger();
        var journalId = Guid.NewGuid();
        var timestamp = DateTimeOffset.UtcNow;
        var cash = new LedgerAccount("Cash", LedgerAccountType.Asset);
        var revenue = new LedgerAccount("Revenue", LedgerAccountType.Revenue);

        var entry = new JournalEntry(
            journalId,
            timestamp,
            "bad-entry",
            new[]
            {
                new LedgerEntry(Guid.NewGuid(), journalId, timestamp, cash, 100m, 0m, "bad-entry"),
                new LedgerEntry(Guid.NewGuid(), journalId, timestamp, revenue, 0m, 50m, "bad-entry"),
            });

        var action = () => ledger.Post(entry);

        action.Should().Throw<LedgerValidationException>();
    }

    [Fact]
    public void TrialBalance_UsesDelegatedNetBalanceRules()
    {
        var ledger = new Meridian.Ledger.Ledger();
        var timestamp = DateTimeOffset.UtcNow;
        var cash = new LedgerAccount("Cash", LedgerAccountType.Asset);
        var revenue = new LedgerAccount("Revenue", LedgerAccountType.Revenue);

        ledger.PostLines(timestamp, "sale", new[]
        {
            (cash, 100m, 0m),
            (revenue, 0m, 100m),
        });

        var trialBalance = ledger.TrialBalance();

        trialBalance[cash].Should().Be(100m);
        trialBalance[revenue].Should().Be(100m);
    }

    [Fact]
    public void ProjectLedgerBook_CanTrackParallelLedgersPerProject()
    {
        var projectLedgers = new ProjectLedgerBook("project-alpha");
        var actualKey = new LedgerBookKey("project-alpha", "core", LedgerViewKind.Actual);
        var historicalKey = new LedgerBookKey("project-alpha", "core", LedgerViewKind.Historical);
        var securityMasterKey = new LedgerBookKey("project-alpha", "cashflows", LedgerViewKind.SecurityMaster, "baseline");

        var actual = projectLedgers.GetOrCreate(actualKey);
        var historical = projectLedgers.GetOrCreate(historicalKey);
        var securityMaster = projectLedgers.GetOrCreate(securityMasterKey);

        actual.Should().NotBeSameAs(historical);
        actual.Should().NotBeSameAs(securityMaster);
        projectLedgers.LedgerKeys.Should().HaveCount(3);
        projectLedgers.TryGetLedger(actualKey, out var sameActual).Should().BeTrue();
        sameActual.Should().BeSameAs(actual);
    }

    [Fact]
    public void GetJournalEntries_CanFilterByProjectSecurityAndLedgerViewMetadata()
    {
        var ledger = new Meridian.Ledger.Ledger();
        var timestamp = DateTimeOffset.UtcNow;
        var securityId = Guid.NewGuid();
        var cash = new LedgerAccount("Cash", LedgerAccountType.Asset);
        var revenue = new LedgerAccount("Revenue", LedgerAccountType.Revenue);

        ledger.PostLines(
            timestamp,
            "security-master accrual",
            new[]
            {
                (cash, 25m, 0m),
                (revenue, 0m, 25m),
            },
            new JournalEntryMetadata(
                ActivityType: "Accrual",
                Symbol: "USTBILL",
                SecurityId: securityId,
                ProjectId: "project-alpha",
                LedgerBook: "cashflows",
                LedgerView: LedgerViewKind.SecurityMaster,
                ScenarioId: "baseline"));

        ledger.PostLines(
            timestamp,
            "actual cash",
            new[]
            {
                (cash, 10m, 0m),
                (revenue, 0m, 10m),
            },
            new JournalEntryMetadata(
                ActivityType: "Cash",
                Symbol: "USTBILL",
                SecurityId: securityId,
                ProjectId: "project-alpha",
                LedgerBook: "cashflows",
                LedgerView: LedgerViewKind.Actual));

        var filtered = ledger.GetJournalEntries(new LedgerQuery(
            ProjectId: "project-alpha",
            LedgerBook: "cashflows",
            LedgerView: LedgerViewKind.SecurityMaster,
            SecurityId: securityId,
            ScenarioId: "baseline"));

        filtered.Should().HaveCount(1);
        filtered[0].Metadata.LedgerView.Should().Be(LedgerViewKind.SecurityMaster);
        filtered[0].Metadata.SecurityId.Should().Be(securityId);
    }
}
