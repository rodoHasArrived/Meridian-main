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
        cashSummary.TotalDebits.Should().Be(130m);
        cashSummary.TotalCredits.Should().Be(0m);
        cashSummary.EntryCount.Should().Be(2);

        revenueSummary.Balance.Should().Be(130m);
        revenueSummary.TotalDebits.Should().Be(0m);
        revenueSummary.TotalCredits.Should().Be(130m);
        revenueSummary.EntryCount.Should().Be(2);
    }

    [Fact]
    public void ProjectLedgerBook_KeyIdentity_IsCaseInsensitive()
    {
        var projectLedgers = new ProjectLedgerBook("project-alpha");
        var lowerKey = new LedgerBookKey("project-alpha", "core", LedgerViewKind.Actual, "baseline");
        var upperKey = new LedgerBookKey("PROJECT-ALPHA", "CORE", LedgerViewKind.Actual, "BASELINE");
        var cash = new LedgerAccount("Cash", LedgerAccountType.Asset);
        var revenue = new LedgerAccount("Revenue", LedgerAccountType.Revenue);

        var lower = projectLedgers.GetOrCreate(lowerKey);
        var upper = projectLedgers.GetOrCreate(upperKey);

        upper.Should().BeSameAs(lower);
        projectLedgers.LedgerKeys.Should().HaveCount(1);
        projectLedgers.TryGetLedger(upperKey, out var resolved).Should().BeTrue();
        resolved.Should().BeSameAs(lower);

        lower.PostLines(DateTimeOffset.UtcNow, "sale", new[] { (cash, 100m, 0m), (revenue, 0m, 100m) });

        var balances = projectLedgers.ConsolidatedTrialBalance(ledgerBook: "CORE", scenarioId: "BASELINE");

        balances[cash].Should().Be(100m);
        balances[revenue].Should().Be(100m);
    }

    [Fact]
    public void GetJournalEntries_WithAccountTypeFilter_ReturnsOnlyEntriesTouchingThatType()
    {
        var ledger = new Meridian.Ledger.Ledger();
        var timestamp = DateTimeOffset.UtcNow;
        var cash = new LedgerAccount("Cash", LedgerAccountType.Asset);
        var revenue = new LedgerAccount("Revenue", LedgerAccountType.Revenue);
        var expense = new LedgerAccount("Commission", LedgerAccountType.Expense);

        // Entry 1: Asset + Revenue
        ledger.PostLines(timestamp, "sale", new[]
        {
            (cash, 100m, 0m),
            (revenue, 0m, 100m),
        });

        // Entry 2: Expense + Asset
        ledger.PostLines(timestamp, "commission", new[]
        {
            (expense, 5m, 0m),
            (cash, 0m, 5m),
        });

        // Filter to Revenue-touching entries only
        var revenueEntries = ledger.GetJournalEntries(new LedgerQuery(AccountType: LedgerAccountType.Revenue));
        revenueEntries.Should().HaveCount(1);
        revenueEntries[0].Description.Should().Be("sale");

        // Filter to Expense-touching entries only
        var expenseEntries = ledger.GetJournalEntries(new LedgerQuery(AccountType: LedgerAccountType.Expense));
        expenseEntries.Should().HaveCount(1);
        expenseEntries[0].Description.Should().Be("commission");

        // No filter — both returned
        var all = ledger.GetJournalEntries(new LedgerQuery());
        all.Should().HaveCount(2);
    }

    [Fact]
    public void LedgerAccounts_DividendReceivable_IsNormalizedAndScopedPerSymbol()
    {
        var aapl = LedgerAccounts.DividendReceivable("aapl");
        var msft = LedgerAccounts.DividendReceivable("MSFT");
        var aaplScoped = LedgerAccounts.DividendReceivable("aapl", "broker-1");

        aapl.Symbol.Should().Be("AAPL");
        aapl.AccountType.Should().Be(LedgerAccountType.Asset);
        msft.Symbol.Should().Be("MSFT");
        aapl.Should().NotBe(msft);
        aaplScoped.FinancialAccountId.Should().Be("broker-1");
        aapl.Should().NotBe(aaplScoped);
    }

    [Fact]
    public void LedgerAccounts_AccruedInterestReceivable_IsAssetAccount()
    {
        var account = LedgerAccounts.AccruedInterestReceivable("USTBILL");
        account.AccountType.Should().Be(LedgerAccountType.Asset);
        account.Symbol.Should().Be("USTBILL");
    }

    [Fact]
    public void LedgerAccounts_CorpActionDistribution_IsRevenueAccount()
    {
        var account = LedgerAccounts.CorpActionDistribution("AAPL");
        account.AccountType.Should().Be(LedgerAccountType.Revenue);
        account.Symbol.Should().Be("AAPL");
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

    [Fact]
    public void LedgerAccounts_UnrealizedGain_IsRevenueAccount()
    {
        LedgerAccounts.UnrealizedGain.AccountType.Should().Be(LedgerAccountType.Revenue);
        LedgerAccounts.UnrealizedGain.Name.Should().Be("Unrealized Gain");
    }

    [Fact]
    public void LedgerAccounts_UnrealizedLoss_IsExpenseAccount()
    {
        LedgerAccounts.UnrealizedLoss.AccountType.Should().Be(LedgerAccountType.Expense);
        LedgerAccounts.UnrealizedLoss.Name.Should().Be("Unrealized Loss");
    }

    [Fact]
    public void LedgerAccounts_RetainedEarnings_IsEquityAccount()
    {
        LedgerAccounts.RetainedEarnings.AccountType.Should().Be(LedgerAccountType.Equity);
        LedgerAccounts.RetainedEarnings.Name.Should().Be("Retained Earnings");
    }

    [Fact]
    public void LedgerAccounts_ScopedVariants_IncludeFinancialAccountId()
    {
        var unrealizedGain = LedgerAccounts.UnrealizedGainFor("broker-1");
        var unrealizedLoss = LedgerAccounts.UnrealizedLossFor("broker-1");
        var retained = LedgerAccounts.RetainedEarningsFor("broker-1");

        unrealizedGain.FinancialAccountId.Should().Be("broker-1");
        unrealizedLoss.FinancialAccountId.Should().Be("broker-1");
        retained.FinancialAccountId.Should().Be("broker-1");

        unrealizedGain.Should().NotBe(LedgerAccounts.UnrealizedGain);
        retained.Should().NotBe(LedgerAccounts.RetainedEarnings);
    }

    [Fact]
    public void Ledger_JournalEntryCount_ReflectsPostedEntries()
    {
        var ledger = new Meridian.Ledger.Ledger();
        var cash = new LedgerAccount("Cash", LedgerAccountType.Asset);
        var revenue = new LedgerAccount("Revenue", LedgerAccountType.Revenue);
        var ts = DateTimeOffset.UtcNow;

        ledger.JournalEntryCount.Should().Be(0);
        ledger.TotalLedgerEntryCount.Should().Be(0);

        ledger.PostLines(ts, "sale-1", new[] { (cash, 100m, 0m), (revenue, 0m, 100m) });

        ledger.JournalEntryCount.Should().Be(1);
        ledger.TotalLedgerEntryCount.Should().Be(2);

        ledger.PostLines(ts, "sale-2", new[] { (cash, 50m, 0m), (revenue, 0m, 50m) });

        ledger.JournalEntryCount.Should().Be(2);
        ledger.TotalLedgerEntryCount.Should().Be(4);
    }

    [Fact]
    public void Ledger_Journal_IsExternallyReadOnly()
    {
        var ledger = new Meridian.Ledger.Ledger();
        var cash = new LedgerAccount("Cash", LedgerAccountType.Asset);
        var revenue = new LedgerAccount("Revenue", LedgerAccountType.Revenue);

        ledger.PostLines(DateTimeOffset.UtcNow, "sale", new[] { (cash, 100m, 0m), (revenue, 0m, 100m) });

        var act = () => ((IList<JournalEntry>)ledger.Journal).Clear();

        act.Should().Throw<NotSupportedException>();
        ledger.Journal.Should().HaveCount(1);
        ledger.GetBalance(cash).Should().Be(100m);
    }

    [Fact]
    public void JournalEntry_Lines_AreDefensivelyCopiedAndReadOnly()
    {
        var journalId = Guid.NewGuid();
        var ts = DateTimeOffset.UtcNow;
        var cash = new LedgerAccount("Cash", LedgerAccountType.Asset);
        var revenue = new LedgerAccount("Revenue", LedgerAccountType.Revenue);
        var originalLines = new List<LedgerEntry>
        {
            new(Guid.NewGuid(), journalId, ts, cash, 100m, 0m, "sale"),
            new(Guid.NewGuid(), journalId, ts, revenue, 0m, 100m, "sale"),
        };

        var entry = new JournalEntry(journalId, ts, "sale", originalLines);

        originalLines.Add(new LedgerEntry(Guid.NewGuid(), journalId, ts, cash, 1m, 0m, "sale"));

        entry.Lines.Should().HaveCount(2);
        entry.IsBalanced.Should().BeTrue();

        var act = () => ((IList<LedgerEntry>)entry.Lines).Clear();

        act.Should().Throw<NotSupportedException>();
        entry.Lines.Should().HaveCount(2);
    }

    [Fact]
    public void Ledger_GetRunningBalance_ReturnsChronologicalCheckpoints()
    {
        var ledger = new Meridian.Ledger.Ledger();
        var cash = new LedgerAccount("Cash", LedgerAccountType.Asset);
        var revenue = new LedgerAccount("Revenue", LedgerAccountType.Revenue);
        var t1 = new DateTimeOffset(2025, 1, 1, 1, 0, 0, TimeSpan.Zero);
        var t2 = new DateTimeOffset(2025, 1, 1, 2, 0, 0, TimeSpan.Zero);

        ledger.PostLines(t1, "sale", new[] { (cash, 100m, 0m), (revenue, 0m, 100m) });
        ledger.PostLines(t2, "commission", new[] { (revenue, 10m, 0m), (cash, 0m, 10m) });

        var running = ledger.GetRunningBalance(cash);

        running.Should().HaveCount(2);
        running[0].Balance.Should().Be(100m);
        running[0].Debit.Should().Be(100m);
        running[0].Credit.Should().Be(0m);
        running[1].Balance.Should().Be(90m);
        running[1].Debit.Should().Be(0m);
        running[1].Credit.Should().Be(10m);
    }

    [Fact]
    public void Ledger_BackdatedPosts_AppearInChronologicalJournalViews()
    {
        var ledger = new Meridian.Ledger.Ledger();
        var cash = new LedgerAccount("Cash", LedgerAccountType.Asset);
        var revenue = new LedgerAccount("Revenue", LedgerAccountType.Revenue);
        var t1 = new DateTimeOffset(2025, 1, 1, 1, 0, 0, TimeSpan.Zero);
        var t2 = new DateTimeOffset(2025, 1, 1, 2, 0, 0, TimeSpan.Zero);

        ledger.PostLines(t2, "later", new[] { (cash, 100m, 0m), (revenue, 0m, 100m) });
        ledger.PostLines(t1, "earlier", new[] { (cash, 50m, 0m), (revenue, 0m, 50m) });

        ledger.Journal.Select(entry => entry.Description).Should().ContainInOrder("earlier", "later");
        ledger.GetJournalEntries().Select(entry => entry.Description).Should().ContainInOrder("earlier", "later");

        var running = ledger.GetRunningBalance(cash);

        running.Select(point => point.Description).Should().ContainInOrder("earlier", "later");
        running[0].Balance.Should().Be(50m);
        running[1].Balance.Should().Be(150m);
    }

    [Fact]
    public void Ledger_GetRunningBalance_WithTimeRange_StartsFromOpeningBalance()
    {
        var ledger = new Meridian.Ledger.Ledger();
        var cash = new LedgerAccount("Cash", LedgerAccountType.Asset);
        var revenue = new LedgerAccount("Revenue", LedgerAccountType.Revenue);
        var t1 = new DateTimeOffset(2025, 1, 1, 1, 0, 0, TimeSpan.Zero);
        var t2 = new DateTimeOffset(2025, 1, 1, 2, 0, 0, TimeSpan.Zero);

        ledger.PostLines(t1, "first-sale", new[] { (cash, 100m, 0m), (revenue, 0m, 100m) });
        ledger.PostLines(t2, "second-sale", new[] { (cash, 50m, 0m), (revenue, 0m, 50m) });

        // Range starts at t2; opening balance from t1 must be carried forward
        var running = ledger.GetRunningBalance(cash, from: t2, to: t2);

        running.Should().HaveCount(1);
        running[0].Balance.Should().Be(150m);  // 100 carried + 50
    }

    [Fact]
    public void Ledger_SnapshotAsOf_ReturnsPointInTimeState()
    {
        var ledger = new Meridian.Ledger.Ledger();
        var cash = new LedgerAccount("Cash", LedgerAccountType.Asset);
        var revenue = new LedgerAccount("Revenue", LedgerAccountType.Revenue);
        var t1 = new DateTimeOffset(2025, 1, 1, 10, 0, 0, TimeSpan.Zero);
        var t2 = t1.AddHours(1);

        ledger.PostLines(t1, "sale", new[] { (cash, 200m, 0m), (revenue, 0m, 200m) });
        ledger.PostLines(t2, "refund", new[] { (cash, 0m, 50m), (revenue, 50m, 0m) });

        var snapAtT1 = ledger.SnapshotAsOf(t1);
        var snapAtT2 = ledger.SnapshotAsOf(t2);

        snapAtT1.Balances[cash].Should().Be(200m);
        snapAtT1.JournalEntryCount.Should().Be(1);
        snapAtT1.LedgerEntryCount.Should().Be(2);

        snapAtT2.Balances[cash].Should().Be(150m);
        snapAtT2.JournalEntryCount.Should().Be(2);
        snapAtT2.LedgerEntryCount.Should().Be(4);
    }

    [Fact]
    public void Ledger_SnapshotAsOf_ReturnsReadOnlyBalances()
    {
        var ledger = new Meridian.Ledger.Ledger();
        var cash = new LedgerAccount("Cash", LedgerAccountType.Asset);
        var revenue = new LedgerAccount("Revenue", LedgerAccountType.Revenue);
        var t1 = new DateTimeOffset(2025, 1, 1, 10, 0, 0, TimeSpan.Zero);

        ledger.PostLines(t1, "sale", new[] { (cash, 200m, 0m), (revenue, 0m, 200m) });

        var snapshot = ledger.SnapshotAsOf(t1);
        var act = () => ((IDictionary<LedgerAccount, decimal>)snapshot.Balances).Clear();

        act.Should().Throw<NotSupportedException>();
        snapshot.Balances[cash].Should().Be(200m);
    }

    [Fact]
    public void LedgerEntry_BothZero_ThrowsWithDistinctMessage()
    {
        var journalId = Guid.NewGuid();
        var ts = DateTimeOffset.UtcNow;
        var account = new LedgerAccount("Cash", LedgerAccountType.Asset);

        var act = () => new LedgerEntry(Guid.NewGuid(), journalId, ts, account, 0m, 0m, "test");

        act.Should()
            .Throw<LedgerValidationException>()
            .WithMessage("*both Debit and Credit are zero*");
    }

    [Fact]
    public void LedgerEntry_BothNonZero_ThrowsWithDistinctMessage()
    {
        var journalId = Guid.NewGuid();
        var ts = DateTimeOffset.UtcNow;
        var account = new LedgerAccount("Cash", LedgerAccountType.Asset);

        var act = () => new LedgerEntry(Guid.NewGuid(), journalId, ts, account, 10m, 5m, "test");

        act.Should()
            .Throw<LedgerValidationException>()
            .WithMessage("*Exactly one*");
    }

    [Fact]
    public void JournalEntry_IsBalanced_TrueForBalancedEntry()
    {
        var journalId = Guid.NewGuid();
        var ts = DateTimeOffset.UtcNow;
        var cash = new LedgerAccount("Cash", LedgerAccountType.Asset);
        var revenue = new LedgerAccount("Revenue", LedgerAccountType.Revenue);

        var entry = new JournalEntry(
            journalId,
            ts,
            "sale",
            new[]
            {
                new LedgerEntry(Guid.NewGuid(), journalId, ts, cash, 100m, 0m, "sale"),
                new LedgerEntry(Guid.NewGuid(), journalId, ts, revenue, 0m, 100m, "sale"),
            });

        entry.IsBalanced.Should().BeTrue();
    }

    [Fact]
    public void FundLedgerBook_EntitySleeveVehicle_GetIndependentLedgers()
    {
        var fund = new FundLedgerBook("fund-xyz");
        var ts = DateTimeOffset.UtcNow;
        var cash = new LedgerAccount("Cash", LedgerAccountType.Asset);
        var revenue = new LedgerAccount("Revenue", LedgerAccountType.Revenue);

        fund.EntityLedger("entity-1").PostLines(ts, "entity-1-sale", new[] { (cash, 100m, 0m), (revenue, 0m, 100m) });
        fund.SleeveLedger("sleeve-a").PostLines(ts, "sleeve-a-sale", new[] { (cash, 40m, 0m), (revenue, 0m, 40m) });
        fund.VehicleLedger("vehicle-x").PostLines(ts, "vehicle-x-sale", new[] { (cash, 20m, 0m), (revenue, 0m, 20m) });

        fund.EntityLedger("entity-1").GetBalance(cash).Should().Be(100m);
        fund.SleeveLedger("sleeve-a").GetBalance(cash).Should().Be(40m);
        fund.VehicleLedger("vehicle-x").GetBalance(cash).Should().Be(20m);
    }

    [Fact]
    public void FundLedgerBook_ConsolidatedTrialBalance_AggregatesAllSubLedgers()
    {
        var fund = new FundLedgerBook("fund-xyz");
        var ts = DateTimeOffset.UtcNow;
        var cash = new LedgerAccount("Cash", LedgerAccountType.Asset);
        var revenue = new LedgerAccount("Revenue", LedgerAccountType.Revenue);

        fund.FundLedger.PostLines(ts, "fund-level", new[] { (cash, 50m, 0m), (revenue, 0m, 50m) });
        fund.EntityLedger("e1").PostLines(ts, "entity-1", new[] { (cash, 30m, 0m), (revenue, 0m, 30m) });

        var consolidated = fund.ConsolidatedTrialBalance();

        consolidated[cash].Should().Be(80m);
        consolidated[revenue].Should().Be(80m);
    }

    [Fact]
    public void FundLedgerBook_EntitySnapshotsAsOf_KeyedByEntityId()
    {
        var fund = new FundLedgerBook("fund-abc");
        var ts = new DateTimeOffset(2025, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var cash = new LedgerAccount("Cash", LedgerAccountType.Asset);
        var revenue = new LedgerAccount("Revenue", LedgerAccountType.Revenue);

        fund.EntityLedger("alpha").PostLines(ts, "sale", new[] { (cash, 75m, 0m), (revenue, 0m, 75m) });
        fund.EntityLedger("beta").PostLines(ts, "sale", new[] { (cash, 25m, 0m), (revenue, 0m, 25m) });

        var snapshots = fund.EntitySnapshotsAsOf(ts);

        snapshots.Should().ContainKey("alpha");
        snapshots.Should().ContainKey("beta");
        snapshots["alpha"].Balances[cash].Should().Be(75m);
        snapshots["beta"].Balances[cash].Should().Be(25m);
    }

    [Fact]
    public void FundLedgerBook_ReconciliationSnapshot_ContainsConsolidatedAndDimensionBreakdowns()
    {
        var fund = new FundLedgerBook("fund-recon");
        var ts = new DateTimeOffset(2025, 9, 1, 12, 0, 0, TimeSpan.Zero);
        var cash = new LedgerAccount("Cash", LedgerAccountType.Asset);
        var revenue = new LedgerAccount("Revenue", LedgerAccountType.Revenue);

        fund.EntityLedger("e1").PostLines(ts, "sale", new[] { (cash, 60m, 0m), (revenue, 0m, 60m) });
        fund.SleeveLedger("s1").PostLines(ts, "sale", new[] { (cash, 40m, 0m), (revenue, 0m, 40m) });

        var snap = fund.ReconciliationSnapshot(ts);

        snap.FundId.Should().Be("fund-recon");
        snap.AsOf.Should().Be(ts);
        snap.Consolidated.Balances[cash].Should().Be(100m);
        snap.Entities.Should().ContainKey("e1");
        snap.Sleeves.Should().ContainKey("s1");
        snap.Vehicles.Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FundLedgerBook_DimensionAccessors_RejectBlankIdentifiers(string? invalidId)
    {
        var fund = new FundLedgerBook("fund-xyz");

        var entityAct = () => fund.EntityLedger(invalidId!);
        var sleeveAct = () => fund.SleeveLedger(invalidId!);
        var vehicleAct = () => fund.VehicleLedger(invalidId!);

        entityAct.Should().Throw<ArgumentException>();
        sleeveAct.Should().Throw<ArgumentException>();
        vehicleAct.Should().Throw<ArgumentException>();
    }
}
