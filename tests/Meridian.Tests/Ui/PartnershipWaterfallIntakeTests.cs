using FluentAssertions;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Ledger;
using Meridian.Ui.Shared.Services;
using NSubstitute;
using Xunit;

namespace Meridian.Tests.Ui;

/// <summary>
/// The partnership waterfall posting path: a soft-closed period's distributable profit is allocated through
/// ordered tiers from retained earnings to partners' capital, capped at the retained earnings the close
/// posted, and queued as a governed adjusting draft. The partners' capital statement then reports each
/// partner's credit as allocated result.
/// </summary>
public sealed class PartnershipWaterfallIntakeTests
{
    private const string FundProfileId = "fund-alpha";
    private static readonly Guid BookId = Guid.Parse("7e000000-0000-0000-0000-0000000000b1");
    private static readonly Guid PeriodId = Guid.Parse("7e000000-0000-0000-0000-0000000000c1");
    private static readonly DateTimeOffset Now = new(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Projection_TakesTheAllocationFromTheNamedRetainedEarnings_TierByTier()
    {
        var projection = PartnershipWaterfallProjector.Project(Input(100m) with
        {
            RetainedEarningsAccount = LedgerAccounts.RetainedEarnings
        });

        projection.Lines.Should().HaveCount(4);
        projection.Lines[0].Should().Be((LedgerAccounts.RetainedEarnings, 100m, 0m));
        projection.Lines.Should().Contain((LedgerAccounts.InvestorCapitalFor("gp"), 0m, 30m));
        projection.Lines.Should().Contain((LedgerAccounts.InvestorCapitalFor("lp-1"), 0m, 42m));
        projection.Lines.Should().Contain((LedgerAccounts.InvestorCapitalFor("lp-2"), 0m, 28m));
    }

    [Fact]
    public void Draft_IsIdempotentForTheSameTerms_AndRefusesAnEmptyAllocation()
    {
        var first = PartnershipWaterfallDraftBuilder.BuildDraft(PartnershipWaterfallProjector.Project(Input(100m)),
            new DateOnly(2026, 10, 31), Now, "ops-user");
        var replay = PartnershipWaterfallDraftBuilder.BuildDraft(PartnershipWaterfallProjector.Project(Input(100m)),
            new DateOnly(2026, 10, 31), Now.AddHours(1), "ops-user");
        var changed = PartnershipWaterfallDraftBuilder.BuildDraft(PartnershipWaterfallProjector.Project(Input(90m)),
            new DateOnly(2026, 10, 31), Now, "ops-user");

        first.Event.Kind.Should().Be(AutomatedJournalEventKind.PartnershipProfitAllocated);
        replay.Metadata.IdempotencyKey.Should().Be(first.Metadata.IdempotencyKey);
        changed.Metadata.IdempotencyKey.Should().NotBe(first.Metadata.IdempotencyKey);
        var empty = () => PartnershipWaterfallDraftBuilder.BuildDraft(PartnershipWaterfallProjector.Project(Input(0m)),
            new DateOnly(2026, 10, 31), Now, "ops-user");
        empty.Should().Throw<ArgumentException>().WithMessage("*allocated no profit*");
    }

    [Fact]
    public void PostedAllocation_IsEachPartnersAllocatedResult_OnThePartnersCapitalStatement()
    {
        var ledger = new Meridian.Ledger.Ledger();
        ledger.PostLines(new DateTimeOffset(2026, 1, 5, 0, 0, 0, TimeSpan.Zero), "contributions",
            [(LedgerAccounts.Cash, 1_000m, 0m), (LedgerAccounts.InvestorCapitalFor("lp-1"), 0m, 600m),
             (LedgerAccounts.InvestorCapitalFor("lp-2"), 0m, 400m)]);
        ledger.PostLines(new DateTimeOffset(2026, 6, 30, 0, 0, 0, TimeSpan.Zero), "interest income",
            [(LedgerAccounts.Cash, 100m, 0m), (LedgerAccounts.CashInterestIncome, 0m, 100m)]);
        ledger.PostLines(new DateTimeOffset(2026, 10, 31, 0, 0, 0, TimeSpan.Zero), "period close",
            [(LedgerAccounts.CashInterestIncome, 100m, 0m), (LedgerAccounts.RetainedEarnings, 0m, 100m)]);
        var projection = PartnershipWaterfallProjector.Project(Input(100m) with { RetainedEarningsAccount = LedgerAccounts.RetainedEarnings });
        ledger.PostLines(new DateTimeOffset(2026, 10, 31, 1, 0, 0, TimeSpan.Zero), "waterfall allocation", projection.Lines);

        var statements = LedgerFinancialStatementBuilder.BuildForPeriod(
            ledger, new DateTimeOffset(2025, 12, 31, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 12, 31, 0, 0, 0, TimeSpan.Zero));
        var capital = statements.PartnersCapital!;

        Row(capital, "lp-1").AllocatedResult.Should().Be(42m);
        Row(capital, "lp-2").AllocatedResult.Should().Be(28m);
        Row(capital, "gp").AllocatedResult.Should().Be(30m);
        capital.EndingCapital.Should().Be(statements.EndingEquity);
    }

    [Fact]
    public async Task Intake_QueuesTheAllocationAsAnAdjustingDraft_WithoutPosting()
    {
        var fixture = await CreateFixtureAsync(retainedEarningsCredit: 150m);

        var result = await fixture.Runner.RunPartnershipWaterfallIntakeAsync(Request(100m));

        var draft = result.Intake.Created.Should().ContainSingle().Subject;
        draft.Status.Should().Be(ManualJournalEntryStatusDto.Draft);
        draft.IsAdjustingEntry.Should().BeTrue("the allocation posts into the soft-closed period as an approved adjustment");
        draft.TotalDebits.Should().Be(100m);
        draft.Lines.Should().Contain(line => line.AccountPath == "Equity:Retained Earnings" && line.Side == AccountingTemplateLineSideDto.Debit && line.Amount == 100m);
        draft.Lines.Should().Contain(line => line.Side == AccountingTemplateLineSideDto.Credit && line.Amount == 42m);
        draft.ValidationIssues.Where(issue => issue.Severity == AccountingConfigurationValidationSeverityDto.Critical)
            .Select(issue => $"{issue.Code}: {issue.Message}").Should().BeEmpty();
        draft.TreasuryContext!.IdempotencyKey.Should().StartWith(PartnershipWaterfallDraftBuilder.IdempotencyPrefix);
    }

    [Fact]
    public async Task Intake_RefusesAnAllocationLargerThanPostedRetainedEarnings()
    {
        var fixture = await CreateFixtureAsync(retainedEarningsCredit: 80m);

        var run = () => fixture.Runner.RunPartnershipWaterfallIntakeAsync(Request(100m));

        await run.Should().ThrowAsync<InvalidOperationException>().WithMessage("*exceeds the 80 of retained earnings*");
        (await fixture.Workbench.GetWorkbenchAsync(FundProfileId, BookId)).Drafts.Should().BeEmpty();
    }

    [Fact]
    public async Task Intake_RefusesAPeriodThatIsNotSoftClosed()
    {
        var fixture = await CreateFixtureAsync(retainedEarningsCredit: 150m, status: LedgerPeriodStatusDto.Open);

        var run = () => fixture.Runner.RunPartnershipWaterfallIntakeAsync(Request(100m));

        await run.Should().ThrowAsync<InvalidOperationException>().WithMessage("*must be soft-closed*");
    }

    [Fact]
    public async Task Intake_RefusesABookOwnedByAnotherFund()
    {
        var fixture = await CreateFixtureAsync(retainedEarningsCredit: 150m, bookFundProfileId: "fund-other");

        var run = () => fixture.Runner.RunPartnershipWaterfallIntakeAsync(Request(100m));

        await run.Should().ThrowAsync<InvalidOperationException>().WithMessage("*does not belong to the requested fund profile*");
    }

    [Fact]
    public async Task Intake_ChecksTheRetainedEarningsScopeItDrawsOn()
    {
        // The close left 150 in the unscoped retained earnings; a scoped allocation finds nothing there.
        var fixture = await CreateFixtureAsync(retainedEarningsCredit: 150m);

        var run = () => fixture.Runner.RunPartnershipWaterfallIntakeAsync(Request(100m) with { RetainedEarningsFinancialAccountId = "acct-9" });

        await run.Should().ThrowAsync<InvalidOperationException>().WithMessage("*exceeds the 0 of retained earnings*");
    }

    private static LedgerPartnersCapitalRollForward Row(LedgerPartnersCapitalStatement statement, string investorId)
        => statement.Accounts.Single(row => row.CapitalAccount == LedgerAccounts.InvestorCapitalFor(investorId));

    private static PartnershipWaterfallAllocationInput Input(decimal profit) => new(
        FundProfileId, PeriodId.ToString("D"), Now, profit,
        [new PartnershipInvestor("gp", "General Partner", 0.02m), new PartnershipInvestor("lp-1", "LP One", 0.59m),
         new PartnershipInvestor("lp-2", "LP Two", 0.39m)],
        [new PartnershipWaterfallTier("hurdle", "GP catch-up", [new PartnershipWaterfallAllocationRule("gp", 1m)], capAmount: 30m),
         new PartnershipWaterfallTier("residual", "Residual pro rata",
             [new PartnershipWaterfallAllocationRule("lp-1", 0.6m), new PartnershipWaterfallAllocationRule("lp-2", 0.4m)])]);

    private static RunPartnershipWaterfallDraftIntakeRequest Request(decimal profit) => new(
        FundProfileId, PeriodId, "ops-user", profit,
        [new("gp", "General Partner", 0.02m), new("lp-1", "LP One", 0.59m), new("lp-2", "LP Two", 0.39m)],
        [new("hurdle", "GP catch-up", [new("gp", 1m)], 30m),
         new("residual", "Residual pro rata", [new("lp-1", 0.6m), new("lp-2", 0.4m)])],
        EntityId: "entity-alpha", TenantId: "tenant-alpha", CompanyId: "company-alpha");

    private static async Task<Fixture> CreateFixtureAsync(
        decimal retainedEarningsCredit,
        LedgerPeriodStatusDto status = LedgerPeriodStatusDto.SoftClosed,
        string bookFundProfileId = FundProfileId)
    {
        var configurationStore = new InMemoryAccountingConfigurationStore();
        await configurationStore.SaveAsync(new AccountingConfigurationWorkspaceDto(
            FundProfileId,
            LedgerBookId: null,
            AccountingConfigurationStatusDto.Draft,
            "test",
            DateTimeOffset.UtcNow,
            LedgerBooks: [],
            ChartOfAccounts:
            [
                new ChartOfAccountsNodeDto("Equity:Retained Earnings", "Equity:Retained Earnings", "Retained Earnings", "Equity"),
                new ChartOfAccountsNodeDto("Equity:Investor Capital", "Equity:Investor Capital", "Investor Capital", "Equity")
            ],
            JournalTemplates: [],
            PostingRules: [],
            ValidationIssues: [],
            AuditTrail: [], TenantId: "tenant-alpha", CompanyId: "company-alpha"));
        var configurationService = new AccountingConfigurationService(configurationStore, new InMemoryAccountingActionAuditStore());
        var draftStore = new InMemoryManualJournalEntryDraftStore();
        var workbench = new ManualJournalEntryWorkbenchService(draftStore, configurationService, new InMemoryAccountingActionAuditStore());
        var intake = new AutomatedJournalDraftIntakeService(workbench, draftStore, configurationService);

        var books = Substitute.For<ILedgerBookService>();
        books.GetPeriodSummaryAsync(PeriodId, Arg.Any<CancellationToken>()).Returns(new LedgerPeriodSummaryDto(
            PeriodId, BookId, 2026, 10, "2026-10",
            [new LedgerPeriodTrialBalanceLineDto("Retained Earnings", "Equity", null, null, 0m, retainedEarningsCredit, retainedEarningsCredit, 1)],
            0m, retainedEarningsCredit, 0m, null, 0, default, Now));
        books.GetBookAsync(BookId, Arg.Any<CancellationToken>()).Returns(new LedgerBookDto(
            BookId, bookFundProfileId, Guid.NewGuid(), FundStructureNodeKindDto.Fund, "Primary book", "USD", Now, Now));
        books.ListPeriodsAsync(Arg.Any<LedgerPeriodQuery>(), Arg.Any<CancellationToken>()).Returns(new[]
        {
            new LedgerPeriodDto(PeriodId, BookId, 2026, 10, "2026-10", new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31),
                status, Now, null, 1)
        });

        var runner = new AutomatedJournalIntakeRunner(
            intake,
            new FeeScheduleAccrualEventProducer(),
            ledgerBookService: books,
            timeProvider: new FixedTimeProvider(Now));
        return new Fixture(runner, workbench);
    }

    private sealed record Fixture(AutomatedJournalIntakeRunner Runner, ManualJournalEntryWorkbenchService Workbench);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
