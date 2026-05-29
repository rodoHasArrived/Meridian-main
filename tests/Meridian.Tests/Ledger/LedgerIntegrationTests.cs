using FluentAssertions;
using FsCheck.Xunit;
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

    [Property(MaxTest = 200)]
    public void Scenario_FundAccountingClose_GeneratedBalancedJournalIsStableUnderLineReordering(
        int lineCountSeed,
        int amountSeed,
        int accountSeed)
    {
        var timestamp = DateTimeOffset.UnixEpoch.AddMinutes(BoundedLong(accountSeed, 0, 525_600));
        var forwardLedger = new Meridian.Ledger.Ledger();
        var reversedLedger = new Meridian.Ledger.Ledger();
        var forwardEntry = BuildGeneratedBalancedJournal(lineCountSeed, amountSeed, accountSeed, timestamp, reverse: false);
        var reversedEntry = BuildGeneratedBalancedJournal(lineCountSeed, amountSeed, accountSeed, timestamp, reverse: true);

        forwardLedger.Post(forwardEntry);
        reversedLedger.Post(reversedEntry);

        forwardEntry.IsBalanced.Should().BeTrue();
        reversedEntry.IsBalanced.Should().BeTrue();
        forwardLedger.TrialBalance().Should().BeEquivalentTo(reversedLedger.TrialBalance());
        forwardLedger.SummarizeAccounts().Should().BeEquivalentTo(reversedLedger.SummarizeAccounts());
        forwardLedger.TotalLedgerEntryCount.Should().Be(reversedLedger.TotalLedgerEntryCount);
    }

    [Property(MaxTest = 200)]
    public void Scenario_FundAccountingClose_GeneratedDuplicateLedgerLineIdsAlwaysReject(
        int lineCountSeed,
        int amountSeed,
        int accountSeed)
    {
        var timestamp = DateTimeOffset.UnixEpoch.AddMinutes(BoundedLong(accountSeed, 0, 525_600));
        var journalId = DeterministicGuid("duplicate-journal", lineCountSeed, amountSeed, accountSeed);
        var duplicateEntryId = DeterministicGuid("duplicate-line", lineCountSeed, amountSeed, accountSeed);
        var lines = BuildGeneratedBalancedLines(journalId, timestamp, lineCountSeed, amountSeed, accountSeed);
        var duplicate = new LedgerEntry(
            duplicateEntryId,
            journalId,
            timestamp,
            lines[0].Account,
            lines[0].Debit,
            lines[0].Credit,
            lines[0].Description);
        var duplicatedLines = new[] { duplicate, duplicate }
            .Concat(lines.Skip(1))
            .ToArray();

        var act = () => new JournalEntry(journalId, timestamp, duplicate.Description, duplicatedLines);

        act.Should()
            .Throw<LedgerValidationException>()
            .WithMessage("*duplicated within the journal entry*");
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
        fund.VehicleLedger("v1").PostLines(ts, "sale", new[] { (cash, 25m, 0m), (revenue, 0m, 25m) });

        var snap = fund.ReconciliationSnapshot(ts);

        snap.FundId.Should().Be("fund-recon");
        snap.AsOf.Should().Be(ts);
        snap.Consolidated.Balances[cash].Should().Be(125m);
        snap.Consolidated.JournalEntryCount.Should().Be(3);
        snap.Consolidated.LedgerEntryCount.Should().Be(6);
        snap.Entities.Should().ContainKey("e1");
        snap.Entities["e1"].Balances[cash].Should().Be(60m);
        snap.Entities["e1"].JournalEntryCount.Should().Be(1);
        snap.Sleeves.Should().ContainKey("s1");
        snap.Sleeves["s1"].LedgerEntryCount.Should().Be(2);
        snap.Vehicles.Should().ContainKey("v1");
        snap.Vehicles["v1"].Balances[cash].Should().Be(25m);
    }

    [Fact]
    public void ChartOfAccounts_Register_CreatesHierarchyForCustomAccountPaths()
    {
        var chart = new ChartOfAccounts();

        var brokerageCash = chart.Register(" Assets : Cash : Brokerage ", LedgerAccountType.Asset, financialAccountId: "broker-1");
        var collateralCash = chart.Register("Assets:Cash:Collateral", LedgerAccountType.Asset);

        brokerageCash.Name.Should().Be("Assets:Cash:Brokerage");
        brokerageCash.FinancialAccountId.Should().Be("broker-1");
        collateralCash.Name.Should().Be("Assets:Cash:Collateral");
        chart.Find("Assets").Should().NotBeNull();
        chart.Find("Assets:Cash").Should().NotBeNull();
        chart.GetChildren("Assets").Should().ContainSingle(node => node.Path == "Assets:Cash");
        chart.GetDescendants("Assets").Select(node => node.Path).Should().Contain(new[]
        {
            "Assets:Cash",
            "Assets:Cash:Brokerage",
            "Assets:Cash:Collateral",
        });
    }

    [Fact]
    public void ChartOfAccounts_Register_RejectsConflictingParentAccountTypes()
    {
        var chart = new ChartOfAccounts();
        chart.Register("Assets:Cash", LedgerAccountType.Asset);

        var act = () => chart.Register("Assets:Fees", LedgerAccountType.Expense);

        act.Should()
            .Throw<ArgumentException>()
            .WithMessage("*parent 'Assets' is registered as Asset, not Expense*");
    }

    [Fact]
    public void ChartOfAccounts_Register_RejectsDuplicatePathWithDifferentScope()
    {
        var chart = new ChartOfAccounts();
        chart.Register("Assets:Cash:Brokerage", LedgerAccountType.Asset, financialAccountId: "broker-1");

        var act = () => chart.Register("Assets:Cash:Brokerage", LedgerAccountType.Asset, financialAccountId: "broker-2");

        act.Should()
            .Throw<ArgumentException>()
            .WithMessage("*already registered with a different account scope*");
    }

    [Fact]
    public void ChartOfAccounts_AggregateBalances_RollsTrialBalanceUpHierarchy()
    {
        var chart = new ChartOfAccounts();
        var brokerageCash = chart.Register("Assets:Cash:Brokerage", LedgerAccountType.Asset);
        var collateralCash = chart.Register("Assets:Cash:Collateral", LedgerAccountType.Asset);
        var investorCapital = chart.Register("Equity:Partners:Capital", LedgerAccountType.Equity);
        var ledger = new Meridian.Ledger.Ledger();
        var ts = new DateTimeOffset(2026, 5, 28, 0, 0, 0, TimeSpan.Zero);

        ledger.PostLines(ts, "capital contribution", new[]
        {
            (brokerageCash, 100m, 0m),
            (collateralCash, 25m, 0m),
            (investorCapital, 0m, 125m),
        });

        var balances = chart.AggregateBalances(ledger.TrialBalance());
        var assets = balances.Single(row => row.Path == "Assets");
        var cash = balances.Single(row => row.Path == "Assets:Cash");
        var brokerage = balances.Single(row => row.Path == "Assets:Cash:Brokerage");
        var equity = balances.Single(row => row.Path == "Equity");

        assets.DirectBalance.Should().Be(0m);
        assets.AggregateBalance.Should().Be(125m);
        cash.AggregateBalance.Should().Be(125m);
        brokerage.DirectBalance.Should().Be(100m);
        brokerage.AggregateBalance.Should().Be(100m);
        equity.AggregateBalance.Should().Be(125m);
    }

    [Fact]
    public void LedgerFinancialStatementBuilder_BuildsIncomeStatementAndBalanceSheetFromChart()
    {
        var chart = new ChartOfAccounts();
        var cash = chart.Register("Assets:Cash:Brokerage", LedgerAccountType.Asset);
        var investorCapital = chart.Register("Equity:Partners:Capital", LedgerAccountType.Equity);
        var feeRevenue = chart.Register("Revenue:Management Fees", LedgerAccountType.Revenue);
        var commissionExpense = chart.Register("Expenses:Commissions", LedgerAccountType.Expense);
        var ledger = new Meridian.Ledger.Ledger();
        var ts = new DateTimeOffset(2026, 5, 28, 0, 0, 0, TimeSpan.Zero);

        ledger.PostLines(ts, "capital contribution", new[]
        {
            (cash, 1_000m, 0m),
            (investorCapital, 0m, 1_000m),
        });
        ledger.PostLines(ts.AddHours(1), "management fee accrual", new[]
        {
            (cash, 100m, 0m),
            (feeRevenue, 0m, 100m),
        });
        ledger.PostLines(ts.AddHours(2), "commission accrual", new[]
        {
            (commissionExpense, 25m, 0m),
            (cash, 0m, 25m),
        });

        var statements = LedgerFinancialStatementBuilder.Build(ledger, chart);

        statements.TotalAssets.Should().Be(1_075m);
        statements.TotalLiabilities.Should().Be(0m);
        statements.TotalEquity.Should().Be(1_000m);
        statements.TotalRevenue.Should().Be(100m);
        statements.TotalExpenses.Should().Be(25m);
        statements.NetIncome.Should().Be(75m);
        statements.EndingEquity.Should().Be(1_075m);
        statements.AccountingEquationVariance.Should().Be(0m);
        statements.IncomeStatementRows.Should().Contain(row => row.Path == "Revenue" && row.AggregateBalance == 100m);
        statements.IncomeStatementRows.Should().Contain(row => row.Path == "Expenses" && row.AggregateBalance == 25m);
        statements.BalanceSheetRows.Should().Contain(row => row.Path == "Assets" && row.AggregateBalance == 1_075m);
    }

    [Fact]
    public void LedgerFinancialStatementBuilder_BuildAsOf_ExcludesLaterPostings()
    {
        var cash = new LedgerAccount("Assets:Cash", LedgerAccountType.Asset);
        var investorCapital = new LedgerAccount("Equity:Capital", LedgerAccountType.Equity);
        var revenue = new LedgerAccount("Revenue:Fees", LedgerAccountType.Revenue);
        var ledger = new Meridian.Ledger.Ledger();
        var t1 = new DateTimeOffset(2026, 5, 28, 9, 0, 0, TimeSpan.Zero);
        var t2 = t1.AddHours(1);

        ledger.PostLines(t1, "capital contribution", new[]
        {
            (cash, 500m, 0m),
            (investorCapital, 0m, 500m),
        });
        ledger.PostLines(t2, "fee accrual", new[]
        {
            (cash, 40m, 0m),
            (revenue, 0m, 40m),
        });

        var beforeFee = LedgerFinancialStatementBuilder.BuildAsOf(ledger, t1);
        var afterFee = LedgerFinancialStatementBuilder.BuildAsOf(ledger, t2);

        beforeFee.AsOf.Should().Be(t1);
        beforeFee.TotalAssets.Should().Be(500m);
        beforeFee.NetIncome.Should().Be(0m);
        beforeFee.AccountingEquationVariance.Should().Be(0m);

        afterFee.TotalAssets.Should().Be(540m);
        afterFee.NetIncome.Should().Be(40m);
        afterFee.AccountingEquationVariance.Should().Be(0m);
    }

    [Fact]
    public void MultiCurrencyLedgerTranslator_TranslatesLocalCurrencyBalancesToBaseCurrency()
    {
        var eurCash = LedgerAccounts.CashInCurrency("eur", "broker-1");
        var eurCapital = new LedgerAccount("Equity:Capital", LedgerAccountType.Equity, Symbol: "EUR", FinancialAccountId: "broker-1");
        var ledger = new Meridian.Ledger.Ledger();
        var ts = new DateTimeOffset(2026, 5, 28, 0, 0, 0, TimeSpan.Zero);

        ledger.PostLines(ts, "EUR subscription in local currency", new[]
        {
            (eurCash, 1_000m, 0m),
            (eurCapital, 0m, 1_000m),
        });

        var translation = MultiCurrencyLedgerTranslator.Translate(
            ledger,
            "USD",
            new Dictionary<string, decimal> { ["EUR"] = 1.1m },
            financialAccountId: "broker-1");

        translation.BaseCurrency.Should().Be("USD");
        translation.TranslatedTrialBalance[eurCash].Should().Be(1_100m);
        translation.Total(LedgerAccountType.Asset).Should().Be(1_100m);
        translation.Total(LedgerAccountType.Equity).Should().Be(1_100m);
        translation.Exposures.Single(exposure => exposure.Account == eurCash).LocalCurrency.Should().Be("EUR");
    }

    [Fact]
    public void MultiCurrencyLedgerTranslator_BuildsBalancedUnrealizedFxRevaluationLines()
    {
        var eurCash = LedgerAccounts.CashInCurrency("EUR", "broker-1");
        var trialBalance = new Dictionary<LedgerAccount, decimal>
        {
            [eurCash] = 1_000m,
        };
        var carryingBaseBalances = new Dictionary<LedgerAccount, decimal>
        {
            [eurCash] = 1_050m,
        };

        var translation = MultiCurrencyLedgerTranslator.Translate(
            trialBalance,
            "USD",
            new Dictionary<string, decimal> { ["EUR"] = 1.1m },
            carryingBaseBalances: carryingBaseBalances);
        var lines = MultiCurrencyLedgerTranslator.BuildUnrealizedFxRevaluationLines(translation, "broker-1");

        lines.Should().HaveCount(2);
        lines.Sum(line => line.debit).Should().Be(50m);
        lines.Sum(line => line.credit).Should().Be(50m);
        lines.Should().Contain(line => line.account == eurCash && line.debit == 50m && line.credit == 0m);
        lines.Should().Contain(line =>
            line.account.Name == "Unrealized FX Gain" &&
            line.account.FinancialAccountId == "broker-1" &&
            line.debit == 0m &&
            line.credit == 50m);
    }

    [Fact]
    public void FixedIncomeAmortizationProjector_ProjectsCouponAndDiscountAccretionLines()
    {
        var carryingAccount = LedgerAccounts.Securities("corp2029", "broker-1");

        var projection = FixedIncomeAmortizationProjector.Project(new FixedIncomeAmortizationInput(
            "corp2029",
            carryingAccount,
            CouponAccrual: 20m,
            DiscountAccretion: 5m,
            PremiumAmortization: 0m,
            FinancialAccountId: "broker-1"));

        projection.Symbol.Should().Be("CORP2029");
        projection.IsBalanced.Should().BeTrue();
        projection.TotalDebits.Should().Be(25m);
        projection.TotalCredits.Should().Be(25m);
        projection.Lines.Should().Contain(line =>
            line.account.Name == "Accrued Interest Receivable" &&
            line.account.Symbol == "CORP2029" &&
            line.debit == 20m);
        projection.Lines.Should().Contain(line => line.account == carryingAccount && line.debit == 5m);
        projection.Lines.Where(line => line.account.Name == "Coupon Income").Sum(line => line.credit).Should().Be(25m);
    }

    [Fact]
    public void FixedIncomeAmortizationProjector_ProjectsPremiumAmortizationAsIncomeReduction()
    {
        var carryingAccount = LedgerAccounts.Securities("muni2031");

        var projection = FixedIncomeAmortizationProjector.Project(new FixedIncomeAmortizationInput(
            "muni2031",
            carryingAccount,
            CouponAccrual: 0m,
            DiscountAccretion: 0m,
            PremiumAmortization: 7.5m));

        projection.IsBalanced.Should().BeTrue();
        projection.Lines.Should().Contain(line => line.account == LedgerAccounts.CouponIncome && line.debit == 7.5m);
        projection.Lines.Should().Contain(line => line.account == carryingAccount && line.credit == 7.5m);
    }

    [Fact]
    public void FixedIncomeAmortizationProjector_RejectsNegativeAmounts()
    {
        var carryingAccount = LedgerAccounts.Securities("UST2030");

        var act = () => FixedIncomeAmortizationProjector.Project(new FixedIncomeAmortizationInput(
            "UST2030",
            carryingAccount,
            CouponAccrual: 0m,
            DiscountAccretion: -1m,
            PremiumAmortization: 0m));

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void LedgerAccountTaxLotPolicyBook_ResolvesAccountSpecificReliefMethod()
    {
        var policyBook = new LedgerAccountTaxLotPolicyBook();
        var account = LedgerAccounts.Securities("AAPL", "broker-1");
        var effectiveDate = new DateOnly(2026, 1, 1);

        var registered = policyBook.Register(
            account,
            LedgerTaxLotReliefMethod.Hifo,
            "policy-hifo-aapl",
            effectiveDate,
            "Minimize realized gains for this sleeve.");

        var resolved = policyBook.Resolve(account, new DateOnly(2026, 5, 28));

        resolved.Should().Be(registered);
        resolved.ReliefMethod.Should().Be(LedgerTaxLotReliefMethod.Hifo);
        resolved.PolicyId.Should().Be("policy-hifo-aapl");
        resolved.Rationale.Should().Be("Minimize realized gains for this sleeve.");
    }

    [Fact]
    public void LedgerAccountTaxLotPolicyBook_UsesDefaultWhenAccountPolicyIsMissing()
    {
        var policyBook = new LedgerAccountTaxLotPolicyBook(LedgerTaxLotReliefMethod.Lifo);
        var account = LedgerAccounts.Securities("MSFT", "broker-2");

        var resolved = policyBook.Resolve(account, new DateOnly(2026, 5, 28));

        resolved.Account.Should().Be(account);
        resolved.ReliefMethod.Should().Be(LedgerTaxLotReliefMethod.Lifo);
        resolved.PolicyId.Should().Be("default");
    }

    [Fact]
    public void LedgerAccountTaxLotPolicyBook_RejectsPoliciesBeforeEffectiveDate()
    {
        var policyBook = new LedgerAccountTaxLotPolicyBook();
        var account = LedgerAccounts.Securities("TSLA");
        policyBook.Register(account, LedgerTaxLotReliefMethod.SpecificId, "policy-specific", new DateOnly(2026, 6, 1));

        var act = () => policyBook.Resolve(account, new DateOnly(2026, 5, 28));

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*not effective until 2026-06-01*");
    }

    [Fact]
    public void AutomatedJournalDraftProjector_ProjectsDividendDeclarationAndReceipt()
    {
        var declared = AutomatedJournalDraftProjector.Project(new AutomatedJournalEvent(
            AutomatedJournalEventKind.DividendDeclared,
            "aapl",
            42m,
            new DateTimeOffset(2026, 5, 28, 0, 0, 0, TimeSpan.Zero),
            FinancialAccountId: "broker-1",
            SourceEventId: "div-001"));
        var received = AutomatedJournalDraftProjector.Project(new AutomatedJournalEvent(
            AutomatedJournalEventKind.DividendReceived,
            "AAPL",
            42m,
            new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
            FinancialAccountId: "broker-1"));

        declared.IsBalanced.Should().BeTrue();
        declared.Metadata.ActivityType.Should().Be(nameof(AutomatedJournalEventKind.DividendDeclared));
        declared.Metadata.Symbol.Should().Be("AAPL");
        declared.Metadata.Tags.Should().ContainKey("sourceEventId");
        declared.Lines.Should().Contain(line => line.account.Name == "Dividend Receivable" && line.account.Symbol == "AAPL" && line.debit == 42m);
        declared.Lines.Should().Contain(line => line.account.Name == "Dividend Income" && line.account.FinancialAccountId == "broker-1" && line.credit == 42m);

        received.IsBalanced.Should().BeTrue();
        received.Lines.Should().Contain(line => line.account.Name == "Cash" && line.account.FinancialAccountId == "broker-1" && line.debit == 42m);
        received.Lines.Should().Contain(line => line.account.Name == "Dividend Receivable" && line.credit == 42m);
    }

    [Fact]
    public void AutomatedJournalDraftProjector_ProjectsCorporateActionExpense()
    {
        var draft = AutomatedJournalDraftProjector.Project(new AutomatedJournalEvent(
            AutomatedJournalEventKind.CorporateActionExpense,
            "msft",
            12.5m,
            DateTimeOffset.UtcNow,
            FinancialAccountId: "broker-2"));

        draft.IsBalanced.Should().BeTrue();
        draft.Lines.Should().Contain(line => line.account.Name == "Corporate Action Expense" && line.account.FinancialAccountId == "broker-2" && line.debit == 12.5m);
        draft.Lines.Should().Contain(line => line.account.Name == "Cash" && line.account.FinancialAccountId == "broker-2" && line.credit == 12.5m);
    }

    [Fact]
    public void AutomatedJournalDraftProjector_RejectsNonPositiveAmounts()
    {
        var act = () => AutomatedJournalDraftProjector.Project(new AutomatedJournalEvent(
            AutomatedJournalEventKind.CashInterestCredited,
            "USD",
            0m,
            DateTimeOffset.UtcNow));

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void LockedAccountingPeriodBook_RejectsPostingInsideLockedPeriod()
    {
        var projectLedgers = new ProjectLedgerBook("fund-alpha");
        var locks = new LockedAccountingPeriodBook();
        var key = new LedgerBookKey("fund-alpha", "Fund", LedgerViewKind.Actual);
        var cash = LedgerAccounts.CashAccount("broker-1");
        var revenue = LedgerAccounts.DividendIncomeFor("broker-1");
        var periodStart = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);
        var periodEnd = new DateTimeOffset(2026, 5, 31, 23, 59, 59, TimeSpan.Zero);

        locks.LockPeriod(
            key,
            "2026-05",
            periodStart,
            periodEnd,
            new DateTimeOffset(2026, 6, 2, 12, 0, 0, TimeSpan.Zero),
            "nav-controller",
            "Published May NAV.");

        var act = () => locks.PostLines(
            projectLedgers,
            key,
            new DateTimeOffset(2026, 5, 15, 12, 0, 0, TimeSpan.Zero),
            "late dividend",
            new[] { (cash, 15m, 0m), (revenue, 0m, 15m) });

        act.Should()
            .Throw<LedgerValidationException>()
            .WithMessage("*2026-05*locked*");
        projectLedgers.GetOrCreate(key).JournalEntryCount.Should().Be(0);
    }

    [Fact]
    public void LockedAccountingPeriodBook_AllowsPostingOutsideLockedPeriodAndScopesByBook()
    {
        var projectLedgers = new ProjectLedgerBook("fund-alpha");
        var locks = new LockedAccountingPeriodBook();
        var actualKey = new LedgerBookKey("fund-alpha", "Fund", LedgerViewKind.Actual);
        var shadowKey = new LedgerBookKey("fund-alpha", "ShadowNAV", LedgerViewKind.Actual);
        var cash = LedgerAccounts.CashAccount("broker-1");
        var revenue = LedgerAccounts.CashInterestIncomeFor("broker-1");

        locks.LockPeriod(
            actualKey,
            "2026-05",
            new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 5, 31, 23, 59, 59, TimeSpan.Zero),
            DateTimeOffset.UtcNow,
            "nav-controller",
            "Published May NAV.");

        locks.PostLines(
            projectLedgers,
            actualKey,
            new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
            "June interest",
            new[] { (cash, 5m, 0m), (revenue, 0m, 5m) });
        locks.PostLines(
            projectLedgers,
            shadowKey,
            new DateTimeOffset(2026, 5, 15, 0, 0, 0, TimeSpan.Zero),
            "shadow nav adjustment",
            new[] { (cash, 7m, 0m), (revenue, 0m, 7m) });

        projectLedgers.GetOrCreate(actualKey).JournalEntryCount.Should().Be(1);
        projectLedgers.GetOrCreate(shadowKey).JournalEntryCount.Should().Be(1);
        locks.TryFindLock(actualKey, new DateTimeOffset(2026, 5, 10, 0, 0, 0, TimeSpan.Zero), out var lockedPeriod).Should().BeTrue();
        lockedPeriod!.LockedBy.Should().Be("nav-controller");
    }

    [Fact]
    public void LockedAccountingPeriodBook_RejectsOverlappingLocksForSameBook()
    {
        var locks = new LockedAccountingPeriodBook();
        var key = new LedgerBookKey("fund-alpha", "Fund");

        locks.LockPeriod(
            key,
            "2026-05",
            new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 5, 31, 23, 59, 59, TimeSpan.Zero),
            DateTimeOffset.UtcNow,
            "nav-controller",
            "Published May NAV.");

        var act = () => locks.LockPeriod(
            key,
            "2026-05-overlap",
            new DateTimeOffset(2026, 5, 15, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 6, 15, 23, 59, 59, TimeSpan.Zero),
            DateTimeOffset.UtcNow,
            "nav-controller",
            "Duplicate close range.");

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*overlaps*");
    }

    private static JournalEntry BuildGeneratedBalancedJournal(
        int lineCountSeed,
        int amountSeed,
        int accountSeed,
        DateTimeOffset timestamp,
        bool reverse)
    {
        var journalId = DeterministicGuid("journal", lineCountSeed, amountSeed, accountSeed);
        var lines = BuildGeneratedBalancedLines(journalId, timestamp, lineCountSeed, amountSeed, accountSeed);
        if (reverse)
            Array.Reverse(lines);

        return new JournalEntry(journalId, timestamp, "generated balanced journal", lines);
    }

    private static LedgerEntry[] BuildGeneratedBalancedLines(
        Guid journalId,
        DateTimeOffset timestamp,
        int lineCountSeed,
        int amountSeed,
        int accountSeed)
    {
        var pairCount = (int)BoundedLong(lineCountSeed, 1, 16);
        var lines = new List<LedgerEntry>(pairCount * 2);

        for (var index = 0; index < pairCount; index++)
        {
            var amount = BoundedLong(HashCode.Combine(amountSeed, index), 1, 1_000_000) / 100m;
            var debitAccount = new LedgerAccount(
                $"Generated Asset {BoundedLong(HashCode.Combine(accountSeed, index), 1, 5)}",
                LedgerAccountType.Asset);
            var creditAccount = new LedgerAccount(
                $"Generated Revenue {BoundedLong(HashCode.Combine(accountSeed, ~index), 1, 5)}",
                LedgerAccountType.Revenue);

            lines.Add(new LedgerEntry(
                DeterministicGuid("debit", lineCountSeed, amountSeed, accountSeed, index),
                journalId,
                timestamp,
                debitAccount,
                amount,
                0m,
                "generated balanced journal"));
            lines.Add(new LedgerEntry(
                DeterministicGuid("credit", lineCountSeed, amountSeed, accountSeed, index),
                journalId,
                timestamp,
                creditAccount,
                0m,
                amount,
                "generated balanced journal"));
        }

        return lines.ToArray();
    }

    private static long BoundedLong(int seed, long min, long max)
    {
        var range = (ulong)(max - min + 1);
        return min + (long)((uint)seed % range);
    }

    private static Guid DeterministicGuid(string scope, params int[] seeds)
    {
        unchecked
        {
            var h0 = (uint)StringComparer.Ordinal.GetHashCode(scope);
            var h1 = 0x9E3779B9u;
            var h2 = 0x85EBCA6Bu;
            var h3 = 0xC2B2AE35u;

            foreach (var seed in seeds)
            {
                var value = (uint)seed;
                h0 = (h0 ^ value) * 16777619u;
                h1 = (h1 + value) * 2246822519u;
                h2 = (h2 ^ (value << 13 | value >> 19)) * 3266489917u;
                h3 = (h3 + (value ^ h0)) * 668265263u;
            }

            var bytes = new byte[16];
            BitConverter.GetBytes(h0).CopyTo(bytes, 0);
            BitConverter.GetBytes(h1).CopyTo(bytes, 4);
            BitConverter.GetBytes(h2).CopyTo(bytes, 8);
            BitConverter.GetBytes(h3).CopyTo(bytes, 12);

            return new Guid(bytes);
        }
    }
}
