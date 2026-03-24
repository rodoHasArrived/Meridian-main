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
    public void ProjectLedgerBook_CanBuildConsolidatedTrialBalanceAcrossFilteredLedgers()
    {
        var projectLedgers = new ProjectLedgerBook("project-alpha");
        var actualKey = new LedgerBookKey("project-alpha", "core", LedgerViewKind.Actual);
        var historicalKey = new LedgerBookKey("project-alpha", "core", LedgerViewKind.Historical, "baseline");
        var cash = new LedgerAccount("Cash", LedgerAccountType.Asset);
        var revenue = new LedgerAccount("Revenue", LedgerAccountType.Revenue);

        projectLedgers.GetOrCreate(actualKey).PostLines(
            DateTimeOffset.UtcNow,
            "actual-sale",
            new[]
            {
                (cash, 100m, 0m),
                (revenue, 0m, 100m),
            });

        projectLedgers.GetOrCreate(historicalKey).PostLines(
            DateTimeOffset.UtcNow,
            "historical-sale",
            new[]
            {
                (cash, 20m, 0m),
                (revenue, 0m, 20m),
            });

        var allBalances = projectLedgers.ConsolidatedTrialBalance();
        var actualOnlyBalances = projectLedgers.ConsolidatedTrialBalance(ledgerView: LedgerViewKind.Actual);
        var baselineOnlyBalances = projectLedgers.ConsolidatedTrialBalance(
            ledgerBook: "core",
            scenarioId: "baseline");

        allBalances[cash].Should().Be(120m);
        allBalances[revenue].Should().Be(120m);
        actualOnlyBalances[cash].Should().Be(100m);
        actualOnlyBalances[revenue].Should().Be(100m);
        baselineOnlyBalances[cash].Should().Be(20m);
        baselineOnlyBalances[revenue].Should().Be(20m);
    }

    [Fact]
    public void ProjectLedgerBook_CanBuildConsolidatedSnapshotAsOfTimestamp()
    {
        var projectLedgers = new ProjectLedgerBook("project-alpha");
        var actualKey = new LedgerBookKey("project-alpha", "core", LedgerViewKind.Actual);
        var historicalKey = new LedgerBookKey("project-alpha", "core", LedgerViewKind.Historical);
        var cash = new LedgerAccount("Cash", LedgerAccountType.Asset);
        var revenue = new LedgerAccount("Revenue", LedgerAccountType.Revenue);
        var t0 = new DateTimeOffset(2025, 01, 01, 0, 0, 0, TimeSpan.Zero);
        var t1 = t0.AddHours(1);
        var t2 = t1.AddHours(1);

        projectLedgers.GetOrCreate(actualKey).PostLines(
            t1,
            "actual-sale",
            new[]
            {
                (cash, 100m, 0m),
                (revenue, 0m, 100m),
            });

        projectLedgers.GetOrCreate(historicalKey).PostLines(
            t2,
            "historical-sale",
            new[]
            {
                (cash, 20m, 0m),
                (revenue, 0m, 20m),
            });

        var snapshotAtT1 = projectLedgers.ConsolidatedSnapshotAsOf(t1);
        var snapshotAtT2 = projectLedgers.ConsolidatedSnapshotAsOf(t2);

        snapshotAtT1.Balances[cash].Should().Be(100m);
        snapshotAtT1.Balances[revenue].Should().Be(100m);
        snapshotAtT1.JournalEntryCount.Should().Be(1);
        snapshotAtT1.LedgerEntryCount.Should().Be(2);

        snapshotAtT2.Balances[cash].Should().Be(120m);
        snapshotAtT2.Balances[revenue].Should().Be(120m);
        snapshotAtT2.JournalEntryCount.Should().Be(2);
        snapshotAtT2.LedgerEntryCount.Should().Be(4);
    }

    [Fact]
    public void ProjectLedgerBook_FilteredSnapshot_FiltersByBookViewAndScenario()
    {
        var projectLedgers = new ProjectLedgerBook("project-alpha");
        var actualKey = new LedgerBookKey("project-alpha", "core", LedgerViewKind.Actual);
        var historicalKey = new LedgerBookKey("project-alpha", "core", LedgerViewKind.Historical, "baseline");
        var replayKey = new LedgerBookKey("project-alpha", "cashflows", LedgerViewKind.Historical, "stress");

        projectLedgers.GetOrCreate(actualKey);
        projectLedgers.GetOrCreate(historicalKey);
        projectLedgers.GetOrCreate(replayKey);

        var filtered = projectLedgers.FilteredSnapshot(
            ledgerBook: "core",
            ledgerView: LedgerViewKind.Historical,
            scenarioId: "baseline");

        filtered.Should().HaveCount(1);
        filtered.Keys.Should().ContainSingle(key =>
            key.LedgerBook == "core" &&
            key.LedgerView == LedgerViewKind.Historical &&
            key.ScenarioId == "baseline");
    }

    [Fact]
    public void ProjectLedgerBook_FilteredLedgerKeys_ReturnsSortedFilteredKeys()
    {
        var projectLedgers = new ProjectLedgerBook("project-alpha");
        var keyA = new LedgerBookKey("project-alpha", "core", LedgerViewKind.Historical, "baseline");
        var keyB = new LedgerBookKey("project-alpha", "core", LedgerViewKind.Actual);
        var keyC = new LedgerBookKey("project-alpha", "cashflows", LedgerViewKind.Actual);

        projectLedgers.GetOrCreate(keyA);
        projectLedgers.GetOrCreate(keyB);
        projectLedgers.GetOrCreate(keyC);

        var filtered = projectLedgers.FilteredLedgerKeys(ledgerBook: "core");

        filtered.Should().HaveCount(2);
        filtered[0].Should().Be(keyB.Normalize());
        filtered[1].Should().Be(keyA.Normalize());
    }

    [Fact]
    public void ProjectLedgerBook_CanQueryConsolidatedJournalEntriesAcrossLedgers()
    {
        var projectLedgers = new ProjectLedgerBook("project-alpha");
        var coreActual = projectLedgers.GetOrCreate(new LedgerBookKey("project-alpha", "core", LedgerViewKind.Actual));
        var coreHistorical = projectLedgers.GetOrCreate(new LedgerBookKey("project-alpha", "core", LedgerViewKind.Historical));
        var cash = new LedgerAccount("Cash", LedgerAccountType.Asset);
        var revenue = new LedgerAccount("Revenue", LedgerAccountType.Revenue);
        var timestamp = DateTimeOffset.UtcNow;

        coreActual.PostLines(
            timestamp,
            "core actual trade",
            new[] { (cash, 100m, 0m), (revenue, 0m, 100m) },
            new JournalEntryMetadata(ProjectId: "project-alpha", LedgerBook: "core", LedgerView: LedgerViewKind.Actual, ActivityType: "Trade"));
        coreHistorical.PostLines(
            timestamp.AddMinutes(1),
            "core historical trade",
            new[] { (cash, 20m, 0m), (revenue, 0m, 20m) },
            new JournalEntryMetadata(ProjectId: "project-alpha", LedgerBook: "core", LedgerView: LedgerViewKind.Historical, ActivityType: "Trade"));

        var consolidated = projectLedgers.ConsolidatedJournalEntries(
            new LedgerQuery(ActivityType: "Trade", ProjectId: "project-alpha", LedgerBook: "core"),
            ledgerBook: "core");

        consolidated.Should().HaveCount(2);
        consolidated[0].Description.Should().Be("core actual trade");
        consolidated[1].Description.Should().Be("core historical trade");
    }

    [Fact]
    public void ProjectLedgerBook_CanBuildConsolidatedAccountSummaries()
    {
        var projectLedgers = new ProjectLedgerBook("project-alpha");
        var actual = projectLedgers.GetOrCreate(new LedgerBookKey("project-alpha", "core", LedgerViewKind.Actual));
        var historical = projectLedgers.GetOrCreate(new LedgerBookKey("project-alpha", "core", LedgerViewKind.Historical));
        var cash = new LedgerAccount("Cash", LedgerAccountType.Asset);
        var revenue = new LedgerAccount("Revenue", LedgerAccountType.Revenue);

        actual.PostLines(DateTimeOffset.UtcNow, "actual", new[] { (cash, 100m, 0m), (revenue, 0m, 100m) });
        historical.PostLines(DateTimeOffset.UtcNow, "historical", new[] { (cash, 30m, 0m), (revenue, 0m, 30m) });

        var summaries = projectLedgers.ConsolidatedAccountSummaries();
        var cashSummary = summaries.Single(s => s.Account == cash);
        var revenueSummary = summaries.Single(s => s.Account == revenue);

        cashSummary.Balance.Should().Be(130m);
        cashSummary.Debits.Should().Be(130m);
        cashSummary.Credits.Should().Be(0m);
        cashSummary.EntryCount.Should().Be(2);

        revenueSummary.Balance.Should().Be(130m);
        revenueSummary.Debits.Should().Be(0m);
        revenueSummary.Credits.Should().Be(130m);
        revenueSummary.EntryCount.Should().Be(2);
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
