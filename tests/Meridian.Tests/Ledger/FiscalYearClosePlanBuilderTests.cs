using FluentAssertions;
using Meridian.Contracts.Ledger;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Meridian.Ui.Shared.Services;
using Codes = Meridian.Contracts.Ledger.FiscalYearCloseBlockerCodes;

namespace Meridian.Tests.Ledger;

/// <summary>
/// W12-YEND-001 slice 1: the read-only fiscal-year close plan built from retained hard-close
/// snapshots. Golden case from the design charter (Section 22.6.3): opening equity 1,000 plus
/// profit 100 reports income of 100 after transfers and next-year opening equity of 1,100.
/// </summary>
public sealed class FiscalYearClosePlanBuilderTests
{
    [Fact]
    public void Golden_MonthlyClosedYear_ReportsIncomeAndOpensNextYearExactlyOnce()
    {
        var year = SyntheticYear.Create();

        var plan = year.Build();

        plan.IsReady.Should().BeTrue();
        plan.Blockers.Should().BeEmpty();
        plan.FiscalYearStart.Should().Be(new DateOnly(2026, 1, 1));
        plan.FiscalYearEnd.Should().Be(new DateOnly(2026, 12, 31));
        plan.ReportedNetIncome.Should().Be(100m, "monthly closing transfers must not erase the year's income");
        plan.OpeningEquity.Should().Be(1_000m);
        plan.PreClosingEquity.Should().Be(1_000m);
        plan.PostClosingEquity.Should().Be(1_100m);

        Balance(plan.PreClosingTrialBalance, "Dividend Income").Should().Be(130m);
        Balance(plan.PreClosingTrialBalance, "Commission Expense").Should().Be(30m);
        Balance(plan.PreClosingTrialBalance, LedgerAccounts.RetainedEarnings.Name).Should().Be(0m);
        plan.PostedClosingTransfers.Should().NotBeEmpty();
        Balance(plan.PostedClosingTransfers, LedgerAccounts.RetainedEarnings.Name).Should().Be(100m);
        plan.ProposedClosingTransfers.Should().BeEmpty("every monthly close already rolled income to retained earnings");

        plan.PostClosingTrialBalance.Should().NotContain(r => r.AccountType == "Revenue" || r.AccountType == "Expense");
        plan.NextYearOpeningBalances.Should().HaveCount(3);
        Balance(plan.NextYearOpeningBalances, LedgerAccounts.Cash.Name).Should().Be(1_100m);
        Balance(plan.NextYearOpeningBalances, LedgerAccounts.CapitalAccount.Name).Should().Be(1_000m);
        Balance(plan.NextYearOpeningBalances, LedgerAccounts.RetainedEarnings.Name).Should().Be(100m);

        plan.Periods.Should().HaveCount(4).And.OnlyContain(p => p.SnapshotHashSha256 != null);
        plan.PlanHashSha256.Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Fact]
    public void PlanHash_IsStableAcrossReadOrderAndChangesWithRetainedEvidence()
    {
        var year = SyntheticYear.Create();
        var first = year.Build();
        var reordered = FiscalYearClosePlanBuilder.Build(year.BookId, 2026,
            year.Periods.AsEnumerable().Reverse().ToArray(),
            year.Reads.Reverse().ToDictionary(pair => pair.Key, pair => pair.Value));

        reordered.PlanHashSha256.Should().Be(first.PlanHashSha256);
        SyntheticYear.Create().Build().PlanHashSha256.Should().NotBe(first.PlanHashSha256,
            "a different retained journal population is different evidence");
    }

    [Fact]
    public void UnpostedYearEndTransfers_AreProposed_WithoutChangingReportedIncome()
    {
        var year = SyntheticYear.Create(postFinalClosingEntry: false);

        var plan = year.Build();

        plan.IsReady.Should().BeTrue();
        plan.ReportedNetIncome.Should().Be(100m);
        Balance(plan.ProposedClosingTransfers, LedgerAccounts.RetainedEarnings.Name).Should().Be(40m);
        plan.PostClosingEquity.Should().Be(1_100m);
        plan.PostClosingTrialBalance.Should().NotContain(r => r.AccountType == "Revenue" || r.AccountType == "Expense");
    }

    [Fact]
    public void MissingMiddlePeriod_BlocksAsCoverageGap_NotEmptyPeriod()
    {
        var year = SyntheticYear.Create();
        var q2 = year.Quarter(2);

        var plan = year.Build(periods: year.Periods.Where(p => p.PeriodId != q2.PeriodId).ToArray());

        AssertBlocked(plan, Codes.CoverageGap);
        plan.Blockers.Single(b => b.Code == Codes.CoverageGap).Message.Should().Contain("2026-04-01").And.Contain("2026-06-30");
    }

    [Fact]
    public void MissingFirstPeriod_BlocksAgainstPriorYearEnd()
    {
        var year = SyntheticYear.Create();
        var q1 = year.Quarter(1);

        var plan = year.Build(periods: year.Periods.Where(p => p.PeriodId != q1.PeriodId).ToArray());

        AssertBlocked(plan, Codes.CoverageGap);
    }

    [Fact]
    public void NoPeriods_Blocks()
        => AssertBlocked(SyntheticYear.Create().Build(fiscalYear: 2030), Codes.NoPeriods);

    [Fact]
    public void SoftClosedPeriod_Blocks()
    {
        var year = SyntheticYear.Create();
        var q3 = year.Quarter(3);

        var plan = year.Build(periods: year.Periods.Select(p => p.PeriodId == q3.PeriodId ? p with { Status = "SoftClosed" } : p).ToArray());

        AssertBlocked(plan, Codes.PeriodNotHardClosed);
        plan.Blockers.Single().PeriodId.Should().Be(q3.PeriodId);
    }

    [Fact]
    public void PriorYearStillOpen_BlocksBecauseOpeningHistoryIsNotFinal()
    {
        var year = SyntheticYear.Create();

        var plan = year.Build(periods: year.Periods.Select(p => p.FiscalYear == 2025 ? p with { Status = "Open" } : p).ToArray());

        AssertBlocked(plan, Codes.PriorYearNotClosed);
    }

    [Fact]
    public void TamperedSnapshot_BlocksOnIntegrity()
    {
        var year = SyntheticYear.Create();
        var q4 = year.Quarter(4);
        var reads = new Dictionary<Guid, FiscalYearCloseSnapshotRead>(year.Reads)
        {
            [q4.PeriodId] = new(year.Reads[q4.PeriodId].Snapshot! with
            {
                PayloadJson = year.Reads[q4.PeriodId].Snapshot!.PayloadJson.Replace("Synthetic dividend", "Changed dividend")
            })
        };

        AssertBlocked(year.Build(reads: reads), Codes.SnapshotIntegrity);
    }

    [Fact]
    public void StoreRefusedSnapshot_BlocksOnIntegrity()
    {
        var year = SyntheticYear.Create();
        var reads = new Dictionary<Guid, FiscalYearCloseSnapshotRead>(year.Reads) { [year.Quarter(2).PeriodId] = new(null, IntegrityFailed: true) };

        AssertBlocked(year.Build(reads: reads), Codes.SnapshotIntegrity);
    }

    [Fact]
    public void MissingAndStaleSnapshots_Block()
    {
        var year = SyntheticYear.Create();
        var missing = new Dictionary<Guid, FiscalYearCloseSnapshotRead>(year.Reads);
        missing.Remove(year.Quarter(1).PeriodId);
        AssertBlocked(year.Build(reads: missing), Codes.SnapshotMissing);

        var q2 = year.Quarter(2);
        var reclosed = year.Periods.Select(p => p.PeriodId == q2.PeriodId ? p with { Version = p.Version + 2 } : p).ToArray();
        AssertBlocked(year.Build(periods: reclosed), Codes.SnapshotStale);
    }

    [Fact]
    public void EarlierPeriodReclosedAfterFinalPeriod_Blocks()
    {
        var year = SyntheticYear.Create();
        var q1 = year.Quarter(1);
        var late = year.Recapture(q1, year.Quarter(4).ClosedAt!.Value.AddDays(5));

        AssertBlocked(year.Build(reads: new Dictionary<Guid, FiscalYearCloseSnapshotRead>(year.Reads) { [q1.PeriodId] = new(late) }),
            Codes.FinalSnapshotSuperseded);
    }

    private static void AssertBlocked(FiscalYearClosePlanDto plan, string code)
    {
        plan.IsReady.Should().BeFalse();
        plan.Blockers.Should().Contain(b => b.Code == code);
        plan.PlanHashSha256.Should().BeNull();
        plan.PreClosingTrialBalance.Should().BeEmpty();
        plan.PostClosingTrialBalance.Should().BeEmpty();
        plan.NextYearOpeningBalances.Should().BeEmpty();
        plan.ReportedNetIncome.Should().Be(0m);
    }

    private static decimal Balance(IReadOnlyList<FiscalYearCloseBalanceDto> rows, string accountName)
        => rows.Where(r => r.AccountName == accountName).Sum(r => r.Balance);

    /// <summary>
    /// Prior year 2025 holds a 1,000 capital contribution. Fiscal 2026 has four quarterly hard
    /// closes: Q2 earns 60 of dividends; Q4 earns 70 and pays 30 of commission. Each earning quarter
    /// posts its own closing entry, as monthly closes do, so retained earnings reaches 100.
    /// </summary>
    private sealed class SyntheticYear
    {
        private readonly List<LedgerJournalEntryRecord> _entries = [];
        private long _sequence;

        public Guid BookId { get; } = Guid.NewGuid();
        public List<LedgerAccountingPeriod> Periods { get; } = [];
        public Dictionary<Guid, FiscalYearCloseSnapshotRead> Reads { get; } = [];

        public static SyntheticYear Create(bool postFinalClosingEntry = true)
        {
            var year = new SyntheticYear();
            var prior = year.AddPeriod(2025, 1, new(2025, 1, 1), new(2025, 12, 31));
            year.Post(prior, "Synthetic capital", LedgerPostingKindDto.Originating,
                (LedgerAccounts.Cash, 1_000m, 0m), (LedgerAccounts.CapitalAccount, 0m, 1_000m));
            year.Close(prior);

            var q1 = year.AddPeriod(2026, 1, new(2026, 1, 1), new(2026, 3, 31));
            year.Close(q1);
            var q2 = year.AddPeriod(2026, 2, new(2026, 4, 1), new(2026, 6, 30));
            year.Post(q2, "Synthetic dividend", LedgerPostingKindDto.Originating,
                (LedgerAccounts.Cash, 60m, 0m), (LedgerAccounts.DividendIncome, 0m, 60m));
            year.Post(q2, "Synthetic close", LedgerPostingKindDto.ClosingEntry,
                (LedgerAccounts.DividendIncome, 60m, 0m), (LedgerAccounts.RetainedEarnings, 0m, 60m));
            year.Close(q2);
            var q3 = year.AddPeriod(2026, 3, new(2026, 7, 1), new(2026, 9, 30));
            year.Close(q3);
            var q4 = year.AddPeriod(2026, 4, new(2026, 10, 1), new(2026, 12, 31));
            year.Post(q4, "Synthetic dividend", LedgerPostingKindDto.Originating,
                (LedgerAccounts.Cash, 70m, 0m), (LedgerAccounts.DividendIncome, 0m, 70m));
            year.Post(q4, "Synthetic commission", LedgerPostingKindDto.Originating,
                (LedgerAccounts.CommissionExpense, 30m, 0m), (LedgerAccounts.Cash, 0m, 30m));
            if (postFinalClosingEntry)
                year.Post(q4, "Synthetic close", LedgerPostingKindDto.ClosingEntry,
                    (LedgerAccounts.DividendIncome, 70m, 0m), (LedgerAccounts.CommissionExpense, 0m, 30m),
                    (LedgerAccounts.RetainedEarnings, 0m, 40m));
            year.Close(q4);
            return year;
        }

        public LedgerAccountingPeriod Quarter(int number) => Periods.Single(p => p.FiscalYear == 2026 && p.PeriodNo == number);

        public FiscalYearClosePlanDto Build(
            int fiscalYear = 2026,
            IReadOnlyList<LedgerAccountingPeriod>? periods = null,
            IReadOnlyDictionary<Guid, FiscalYearCloseSnapshotRead>? reads = null)
            => FiscalYearClosePlanBuilder.Build(BookId, fiscalYear, periods ?? Periods, reads ?? Reads);

        public LedgerPeriodCloseSnapshot Recapture(LedgerAccountingPeriod period, DateTimeOffset closedAt)
        {
            var closeEvent = new PeriodCloseEventRecord(Guid.NewGuid(), period.PeriodId, "SoftClosed", "HardClosed",
                "synthetic-controller", "Synthetic re-close", closedAt);
            return LedgerPeriodCloseSnapshot.Capture(period with { ClosedAt = closedAt }, closeEvent,
                _entries.Where(e => e.Entry.Metadata.EffectiveDate <= period.EndDate).ToArray());
        }

        private LedgerAccountingPeriod AddPeriod(int fiscalYear, int number, DateOnly start, DateOnly end)
        {
            var period = new LedgerAccountingPeriod(Guid.NewGuid(), BookId, fiscalYear, number, $"FY{fiscalYear} P{number}",
                start, end, "Open", At(start), null, 0);
            Periods.Add(period);
            return period;
        }

        private void Post(LedgerAccountingPeriod period, string description, LedgerPostingKindDto kind,
            params (LedgerAccount Account, decimal Debit, decimal Credit)[] lines)
        {
            var id = Guid.NewGuid();
            var at = At(period.EndDate);
            var entry = new JournalEntry(id, at, description,
                lines.Select(l => new LedgerEntry(Guid.NewGuid(), id, at, l.Account, l.Debit, l.Credit, description,
                    currency: new LedgerEntryCurrency("USD", "USD", l.Debit, l.Credit, 1m))).ToArray(),
                new JournalEntryMetadata(EffectiveDate: period.EndDate));
            _entries.Add(new(entry, Guid.NewGuid(), period.PeriodId, null, null, ++_sequence, at, PostingKind: kind));
        }

        private void Close(LedgerAccountingPeriod period)
        {
            var closedAt = At(period.EndDate).AddDays(3);
            var closed = period with { Status = "HardClosed", ClosedAt = closedAt, Version = 2 };
            Periods[Periods.FindIndex(p => p.PeriodId == period.PeriodId)] = closed;
            var closeEvent = new PeriodCloseEventRecord(Guid.NewGuid(), period.PeriodId, "SoftClosed", "HardClosed",
                "synthetic-controller", "Synthetic close", closedAt);
            Reads[period.PeriodId] = new(LedgerPeriodCloseSnapshot.Capture(closed, closeEvent,
                _entries.Where(e => e.Entry.Metadata.EffectiveDate <= period.EndDate).ToArray()));
        }

        private static DateTimeOffset At(DateOnly date) => new(date.ToDateTime(new TimeOnly(23, 0)), TimeSpan.Zero);
    }
}
