using FluentAssertions;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Tenancy;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Meridian.Ui.Shared.Services;

namespace Meridian.Tests.Storage;

/// <summary>
/// The fiscal-year close plan over real retained hard-close snapshots: periods are closed through
/// the store's atomic hard close, and the plan is rebuilt after a store restart.
/// </summary>
public sealed class FiscalYearClosePlanPostgresTests
{
    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task FiscalClose_RetainsExactConcurrentReceipt_AndLateCorrectionAppendsSuccessor()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var ct = timeout.Token;
        await using var database = await LedgerPostgresTestDatabase.CreateAsync(ct);
        const string fund = "synthetic-fiscal-close";
        const string tenant = "synthetic-close-tenant";
        const string company = "synthetic-close-company";
        const string controller = "synthetic-controller";
        var owners = new PostgresFundProfileTenancyRegistry(database.Options);
        await owners.BindAsync(fund, tenant, company, ct);
        PostgresLedgerJournalStore OpenStore(string ownerTenant = tenant) => new(database.Options, new FixedTenant(ownerTenant),
            postingAuthorities: LedgerPostgresTestDatabase.SyntheticFinancialPostingAuthorities);
        var store = OpenStore();
        var bookId = Guid.NewGuid();
        var created = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        await store.SaveLedgerBookAsync(new LedgerBookRecord(bookId, fund, Guid.NewGuid(),
            FundStructureNodeKindDto.Fund, "Synthetic year-end book", "USD", created, created), ct);
        var prior = await OpenAsync(store, bookId, 2025, 1, new(2025, 1, 1), new(2025, 12, 31), ct);
        await PostAsync(store, bookId, prior, "Synthetic capital", LedgerPostingKindDto.Originating, ct,
            (LedgerAccounts.Cash, 1_000m, 0m), (LedgerAccounts.CapitalAccount, 0m, 1_000m));
        await HardCloseAsync(store, prior, ct);
        var year = await OpenAsync(store, bookId, 2026, 1, new(2026, 1, 1), new(2026, 12, 31), ct);
        await PostAsync(store, bookId, year, "Synthetic operating income", LedgerPostingKindDto.Originating, ct,
            (LedgerAccounts.Cash, 100m, 0m), (LedgerAccounts.DividendIncome, 0m, 100m));
        await HardCloseAsync(store, year, ct,
            (LedgerAccounts.DividendIncome, 100m, 0m), (LedgerAccounts.RetainedEarnings, 0m, 100m));
        var originalPlan = await store.GetFiscalYearClosePlanAsync(bookId, 2026, ct);
        var request = new FinalizeFiscalYearCloseRequestDto(Guid.NewGuid(), originalPlan.PlanHashSha256!, "Approved close");
        var concurrent = await Task.WhenAll(Finalize(store, request), Finalize(OpenStore(), request));
        var original = concurrent[0];
        concurrent[1].Should().BeEquivalentTo(original);
        original.ClosingJournalEntryIds.Should().ContainSingle();
        original.Plan.ReportedNetIncome.Should().Be(100m);
        original.Plan.PostClosingEquity.Should().Be(1_100m);
        var conflictingRetry = () => Finalize(store, request with { Reason = "Different terms" });
        await conflictingRetry.Should().ThrowAsync<InvalidOperationException>().WithMessage("*different authorized inputs*");
        var duplicateReview = () => Finalize(store, request with
        { CommandId = Guid.NewGuid(), SupersedesReceiptId = original.ReceiptId });
        await duplicateReview.Should().ThrowAsync<InvalidOperationException>().WithMessage("*already has a receipt*");
        var foreign = OpenStore("synthetic-foreign");
        (await foreign.GetFiscalYearCloseReceiptAsync(bookId, 2026, ct: ct)).Should().BeNull();
        var foreignWrite = () => foreign.FinalizeFiscalYearCloseAsync(bookId, 2026, request,
            controller, "Controller", "synthetic-foreign", company, ct);
        await foreignWrite.Should().ThrowAsync<UnauthorizedAccessException>();

        var closed = (await store.GetPeriodAsync(year.PeriodId, ct))!;
        var reopened = await store.SavePeriodAsync(closed with { Status = "Open", ClosedAt = null }, closed.Version,
            new PeriodCloseEventRecord(Guid.NewGuid(), closed.PeriodId, "HardClosed", "Open",
                controller, "Approved synthetic late adjustment", DateTimeOffset.UtcNow), ct);
        var staleRequest = request with { CommandId = Guid.NewGuid(), SupersedesReceiptId = original.ReceiptId };
        var stale = () => Finalize(store, staleRequest with { ExpectedPlanHashSha256 = new string('a', 64) });
        await stale.Should().ThrowAsync<InvalidOperationException>().WithMessage("*inputs changed or have blockers*");
        // The reopened period records an approved late adjustment through the standard journal path.
        var id = Guid.NewGuid();
        var at = new DateTimeOffset(year.EndDate.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);
        const string note = "Approved synthetic late income adjustment";
        var correction = new JournalEntry(id, at, note,
            [new LedgerEntry(Guid.NewGuid(), id, at, LedgerAccounts.Cash, 5m, 0m, note,
                currency: new LedgerEntryCurrency("USD", "USD", 5m, 0m, 1m)),
             new LedgerEntry(Guid.NewGuid(), id, at, LedgerAccounts.DividendIncome, 0m, 5m, note,
                currency: new LedgerEntryCurrency("USD", "USD", 0m, 5m, 1m))],
            new JournalEntryMetadata(EffectiveDate: year.EndDate));
        await store.AppendAsync(new LedgerJournalEntryWrite(correction, Guid.NewGuid(), reopened.PeriodId,
            PostingKind: LedgerPostingKindDto.Adjustment,
            AdjustmentApproval: new("adjustment-5", LedgerAdjustmentApprovalStatusDto.Approved, controller,
                at, "late-adjustment", GovernanceCaseId: "synthetic-close-case"),
            LedgerBookId: bookId), ct);
        await HardCloseAsync(store, reopened, ct,
            (LedgerAccounts.DividendIncome, 5m, 0m), (LedgerAccounts.RetainedEarnings, 0m, 5m));
        var newPlan = await store.GetFiscalYearClosePlanAsync(bookId, 2026, ct);
        var successor = await Finalize(OpenStore(), staleRequest with { ExpectedPlanHashSha256 = newPlan.PlanHashSha256! });
        successor.SupersedesReceiptId.Should().Be(original.ReceiptId);
        successor.Plan.ReportedNetIncome.Should().Be(105m);
        successor.Plan.PostClosingEquity.Should().Be(1_105m);
        (await OpenStore().GetFiscalYearCloseReceiptAsync(bookId, 2026, original.ReceiptId, ct)).Should().BeEquivalentTo(original);
        (await OpenStore().GetFiscalYearCloseReceiptAsync(bookId, 2026, ct: ct)).Should().BeEquivalentTo(successor);
        (await Finalize(OpenStore(), request)).Should().BeEquivalentTo(original, "replay recovers historical approval even after re-close");
        (await store.QueryAsync(new LedgerJournalEntryQuery(LedgerBookId: bookId), ct)).Should().HaveCount(5);

        Task<FiscalYearCloseReceiptDto> Finalize(PostgresLedgerJournalStore target, FinalizeFiscalYearCloseRequestDto command)
            => target.FinalizeFiscalYearCloseAsync(bookId, 2026, command, controller, "Controller", tenant, company, ct);
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task HardClosedYear_BuildsGoldenPlanFromRetainedSnapshots_AndReplaysAfterRestart()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var ct = timeout.Token;
        await using var database = await LedgerPostgresTestDatabase.CreateAsync(ct);
        var store = database.JournalStore;
        var bookId = Guid.NewGuid();
        var created = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        await store.SaveLedgerBookAsync(new LedgerBookRecord(bookId, "synthetic-year-end", Guid.NewGuid(),
            FundStructureNodeKindDto.Fund, "Synthetic year-end book", "USD", created, created), ct);

        var prior = await OpenAsync(store, bookId, 2025, 1, new(2025, 1, 1), new(2025, 12, 31), ct);
        await PostAsync(store, bookId, prior, "Synthetic capital", LedgerPostingKindDto.Originating, ct,
            (LedgerAccounts.Cash, 1_000m, 0m), (LedgerAccounts.CapitalAccount, 0m, 1_000m));
        await HardCloseAsync(store, prior, ct);

        var first = await OpenAsync(store, bookId, 2026, 1, new(2026, 1, 1), new(2026, 6, 30), ct);
        await PostAsync(store, bookId, first, "Synthetic dividend", LedgerPostingKindDto.Originating, ct,
            (LedgerAccounts.Cash, 130m, 0m), (LedgerAccounts.DividendIncome, 0m, 130m));
        await HardCloseAsync(store, first, ct, (LedgerAccounts.DividendIncome, 130m, 0m), (LedgerAccounts.RetainedEarnings, 0m, 130m));

        var second = await OpenAsync(store, bookId, 2026, 2, new(2026, 7, 1), new(2026, 12, 31), ct);
        await PostAsync(store, bookId, second, "Synthetic commission", LedgerPostingKindDto.Originating, ct,
            (LedgerAccounts.CommissionExpense, 30m, 0m), (LedgerAccounts.Cash, 0m, 30m));
        var beforeFinalClose = await FiscalYearClosePlanService.BuildAsync(store, bookId, 2026, ct);
        beforeFinalClose.IsReady.Should().BeFalse();
        beforeFinalClose.Blockers.Should().ContainSingle(b => b.Code == FiscalYearCloseBlockerCodes.PeriodNotHardClosed && b.PeriodId == second.PeriodId);
        await HardCloseAsync(store, second, ct, (LedgerAccounts.RetainedEarnings, 30m, 0m), (LedgerAccounts.CommissionExpense, 0m, 30m));

        var plan = await FiscalYearClosePlanService.BuildAsync(store, bookId, 2026, ct);

        plan.IsReady.Should().BeTrue(string.Join("; ", plan.Blockers.Select(b => b.Message)));
        plan.ReportedNetIncome.Should().Be(100m);
        plan.OpeningEquity.Should().Be(1_000m);
        plan.PostClosingEquity.Should().Be(1_100m);
        plan.ProposedClosingTransfers.Should().BeEmpty();
        plan.NextYearOpeningBalances.Where(r => r.AccountType == "Equity").Sum(r => r.Balance).Should().Be(1_100m);
        plan.Periods.Should().HaveCount(2).And.OnlyContain(p => p.Status == "HardClosed" && p.SnapshotHashSha256 != null);

        var replay = await FiscalYearClosePlanService.BuildAsync(database.RestartJournalStore(), bookId, 2026, ct);
        replay.PlanHashSha256.Should().Be(plan.PlanHashSha256, "reading a retained plan is reproducible and never reruns a close");
        (await store.ListPeriodsAsync(bookId, ct: ct)).Should().OnlyContain(p => p.Status == "HardClosed");
    }

    private static Task<LedgerAccountingPeriod> OpenAsync(ILedgerJournalStore store, Guid bookId, int fiscalYear, int number,
        DateOnly start, DateOnly end, CancellationToken ct)
        => store.SavePeriodAsync(new LedgerAccountingPeriod(Guid.NewGuid(), bookId, fiscalYear, number,
            $"FY{fiscalYear} H{number}", start, end, "Open", new(start.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero), null, 0), 0, ct: ct);

    private static async Task PostAsync(ILedgerJournalStore store, Guid bookId, LedgerAccountingPeriod period, string description,
        LedgerPostingKindDto kind, CancellationToken ct, params (LedgerAccount Account, decimal Debit, decimal Credit)[] lines)
    {
        var id = Guid.NewGuid();
        var at = new DateTimeOffset(period.EndDate.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);
        var entry = new JournalEntry(id, at, description,
            lines.Select(l => new LedgerEntry(Guid.NewGuid(), id, at, l.Account, l.Debit, l.Credit, description,
                currency: new LedgerEntryCurrency("USD", "USD", l.Debit, l.Credit, 1m))).ToArray(),
            new JournalEntryMetadata(EffectiveDate: period.EndDate));
        await store.AppendAsync(new LedgerJournalEntryWrite(entry, Guid.NewGuid(), period.PeriodId, PostingKind: kind, LedgerBookId: bookId), ct);
    }

    private static async Task HardCloseAsync(ILedgerJournalStore store, LedgerAccountingPeriod period, CancellationToken ct,
        params (LedgerAccount Account, decimal Debit, decimal Credit)[] closingLines)
    {
        var soft = await store.SavePeriodAsync(period with { Status = "SoftClosed" }, period.Version, ct: ct);
        if (closingLines.Length > 0)
            await PostAsync(store, period.LedgerBookId!.Value, soft, "Synthetic close", LedgerPostingKindDto.ClosingEntry, ct, closingLines);
        var closedAt = new DateTimeOffset(period.EndDate.ToDateTime(new TimeOnly(23, 0)), TimeSpan.Zero).AddDays(3);
        await ((IAtomicLedgerPeriodCloseStore)store).SaveHardClosedPeriodAsync(soft with { Status = "HardClosed", ClosedAt = closedAt },
            soft.Version, new PeriodCloseEventRecord(Guid.NewGuid(), period.PeriodId, "SoftClosed", "HardClosed",
                "synthetic-controller", "Synthetic hard close", closedAt), ct);
    }

    private sealed class FixedTenant(string tenant) : IFundScopeTenantAccessor
    {
        public string ResolveCallerTenant() => tenant;
    }
}
