using FluentAssertions;
using Meridian.FinancialOperations.FundAdministration;
using Meridian.Ledger;
using Xunit;

namespace Meridian.Tests.FinancialOperations;

/// <summary>
/// Tests the FundStudio-style administration control surface: privileged actions drive the ledger
/// primitives and every action lands in the tamper-evident event log.
/// </summary>
public sealed class FundAdministrationControlServiceTests
{
    private static readonly LedgerBookKey Book = new("FUND-A", "Fund");
    private static readonly DateTimeOffset At = new(2026, 07, 01, 0, 0, 0, TimeSpan.Zero);

    private static JournalTemplate FeeTemplate() => new(
        "fee",
        "Fee Accrual",
        "Accrue a fee.",
        [
            new JournalTemplateLine(LedgerAccounts.ManagementFeeExpenseFor("FUND-A"), JournalTemplateSide.Debit, "fee"),
            new JournalTemplateLine(LedgerAccounts.ManagementFeePayableFor("FUND-A"), JournalTemplateSide.Credit, "fee"),
        ]);

    private static PeriodReopenEvidence Evidence() => new(
        "reopen-1",
        "Restatement",
        "cfo",
        At.AddDays(1),
        [new JournalEvidenceReference("ev-1", "vault://tickets/CH-1", "Ticket", "Governance", At, "cfo")]);

    [Fact]
    public void LockAndReopenPeriod_RecordEvidenceBearingGovernanceEvents()
    {
        var service = new FundAdministrationControlService();

        service.LockPeriod(
            Book,
            "2026-Q2",
            new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 6, 30, 23, 59, 59, TimeSpan.Zero),
            At,
            "controller",
            "Quarter close");
        service.ReopenPeriod(Book, "2026-Q2", At.AddDays(1), "cfo", Evidence());

        service.EventLog.EventsOfKind(FundAdministrationEventKind.PeriodLocked).Should().ContainSingle();
        var reopen = service.EventLog.EventsOfKind(FundAdministrationEventKind.PeriodReopened).Should().ContainSingle().Which;
        reopen.Evidence.Should().ContainSingle("the reopen evidence reference is carried into the log");
        service.EventLog.VerifyIntegrity().Should().BeTrue();
        service.Periods.IsLocked(Book, "2026-Q2").Should().BeFalse();
    }

    [Fact]
    public void MaterializeDueRecurringJournals_SkipsLockedOccurrencesAndLogsRuns()
    {
        var service = new FundAdministrationControlService();
        service.RegisterJournalTemplate(FeeTemplate(), "controller");
        service.ScheduleRecurringJournal(
            new RecurringJournalSchedule(
                "sched-1",
                "fee",
                Book,
                RecurringJournalCadence.Monthly,
                new DateOnly(2026, 1, 15),
                "controller",
                At,
                parameters: new Dictionary<string, decimal> { ["fee"] = 1_000m }),
            "controller");

        service.LockPeriod(
            Book,
            "2026-02",
            new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 2, 28, 23, 59, 59, TimeSpan.Zero),
            At,
            "controller",
            "February lock");

        var postable = service.MaterializeDueRecurringJournals("sched-1", new DateOnly(2026, 3, 31), "controller");

        postable.Should().HaveCount(2, "the February occurrence is blocked by the lock");
        postable.Should().OnlyContain(occurrence => !occurrence.BlockedByLock);
        service.EventLog.EventsOfKind(FundAdministrationEventKind.RecurringJournalRun).Should().HaveCount(2);
        service.EventLog.VerifyIntegrity().Should().BeTrue();
    }

    [Fact]
    public void ScheduleRecurringJournal_UnregisteredTemplate_Throws()
    {
        var service = new FundAdministrationControlService();
        var act = () => service.ScheduleRecurringJournal(
            new RecurringJournalSchedule("s", "missing", Book, RecurringJournalCadence.Monthly, new DateOnly(2026, 1, 1), "controller", At),
            "controller");

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void RunYearEndClose_RecordsProjectionAndReadiness()
    {
        var service = new FundAdministrationControlService();
        var input = new YearEndCloseInput(
            "FY2026",
            new DateTimeOffset(2026, 12, 31, 23, 59, 59, TimeSpan.Zero),
            new Dictionary<LedgerAccount, decimal>
            {
                [LedgerAccounts.RealizedGain] = 1_000m,
                [LedgerAccounts.CommissionExpense] = 400m,
            },
            "controller",
            requiredPeriodIds: ["Q1", "Q2"],
            closedPeriodIds: ["Q1", "Q2"]);

        var projection = service.RunYearEndClose(input, "controller");

        projection.IsReady.Should().BeTrue();
        projection.NetIncome.Should().Be(600m);
        service.EventLog.EventsOfKind(FundAdministrationEventKind.YearEndClosed).Should().ContainSingle();
    }

    [Fact]
    public void PortfolioPricingRule_SetAndResolve_RecordsAndSelects()
    {
        var service = new FundAdministrationControlService();
        service.SetPortfolioPricingRule(
            new PortfolioPricingRule("bond", "PORT-1", "MatrixPricing", "MarkToModel", "cfo", At, priority: 10, instrumentType: "Bond"),
            "cfo");

        service.ResolvePortfolioPricing("PORT-1", "Bond", new DateOnly(2026, 6, 30))!.PriceSource.Should().Be("MatrixPricing");
        service.EventLog.EventsOfKind(FundAdministrationEventKind.PricingRuleChanged).Should().ContainSingle();
    }

    [Fact]
    public void ApplyOnboardingTemplate_ResolvesPlaceholdersAndRecords()
    {
        var service = new FundAdministrationControlService();
        service.RegisterOnboardingTemplate(new OnboardingTemplate(
            "std-fund",
            "Standard Fund",
            "Standard fund skeleton.",
            [
                new OnboardingTemplateNode(OnboardingTemplateNode.Types.Organization, "org", "{orgCode}", "{orgName}"),
                new OnboardingTemplateNode(OnboardingTemplateNode.Types.Portfolio, "port", "{fundCode}-MAIN", "{fundName} Main", ParentKey: "org", BaseCurrency: "USD"),
                new OnboardingTemplateNode(OnboardingTemplateNode.Types.Account, "acct", "{fundCode}-CUST", "{fundName} Custody", ParentKey: "port"),
                new OnboardingTemplateNode(OnboardingTemplateNode.Types.Book, "book", "{fundCode}-GL", "{fundName} GL", ParentKey: "port"),
            ],
            "admin",
            At));

        var plan = service.ApplyOnboardingTemplate(
            "std-fund",
            new Dictionary<string, string>
            {
                ["orgCode"] = "ACME",
                ["orgName"] = "Acme Capital",
                ["fundCode"] = "GRO",
                ["fundName"] = "Growth Fund",
            },
            "admin");

        plan.Nodes.Should().HaveCount(4);
        plan.Nodes.Single(node => node.Key == "port").Code.Should().Be("GRO-MAIN");
        plan.Nodes.Single(node => node.Key == "book").Name.Should().Be("Growth Fund GL");
        service.EventLog.EventsOfKind(FundAdministrationEventKind.OnboardingApplied).Should().ContainSingle();
    }

    [Fact]
    public void RecordJournalPosted_AppendsPostingEvent()
    {
        var service = new FundAdministrationControlService();
        var journalId = Guid.NewGuid();
        var entry = new JournalEntry(
            journalId,
            At,
            "Manual reclass",
            [
                new LedgerEntry(Guid.NewGuid(), journalId, At, LedgerAccounts.Cash, 250m, 0m, "Manual reclass"),
                new LedgerEntry(Guid.NewGuid(), journalId, At, LedgerAccounts.RealizedGain, 0m, 250m, "Manual reclass"),
            ]);

        service.RecordJournalPosted("Fund", entry, "controller");

        var posted = service.EventLog.EventsOfKind(FundAdministrationEventKind.JournalPosted).Should().ContainSingle().Which;
        posted.Attributes["amount"].Should().Be("250");
    }

    [Fact]
    public void RunYearEndClose_NotReady_DoesNotRecordClosedEvent()
    {
        var service = new FundAdministrationControlService();
        var input = new YearEndCloseInput(
            "FY2026",
            new DateTimeOffset(2026, 12, 31, 23, 59, 59, TimeSpan.Zero),
            new Dictionary<LedgerAccount, decimal> { [LedgerAccounts.RealizedGain] = 1_000m },
            "controller",
            requiredPeriodIds: ["Q1", "Q2"],
            closedPeriodIds: ["Q1"]);

        var projection = service.RunYearEndClose(input, "controller");

        projection.IsReady.Should().BeFalse();
        service.EventLog.EventsOfKind(FundAdministrationEventKind.YearEndClosed)
            .Should().BeEmpty("an unclosed fiscal year must not be recorded as closed");
    }

    [Fact]
    public void MaterializeDueRecurringJournals_SecondCall_ReturnsNoDuplicates()
    {
        var service = new FundAdministrationControlService();
        service.RegisterJournalTemplate(FeeTemplate(), "controller");
        service.ScheduleRecurringJournal(
            new RecurringJournalSchedule(
                "sched-1",
                "fee",
                Book,
                RecurringJournalCadence.Monthly,
                new DateOnly(2026, 1, 15),
                "controller",
                At,
                parameters: new Dictionary<string, decimal> { ["fee"] = 1_000m }),
            "controller");

        var first = service.MaterializeDueRecurringJournals("sched-1", new DateOnly(2026, 3, 31), "controller");
        var second = service.MaterializeDueRecurringJournals("sched-1", new DateOnly(2026, 3, 31), "controller");

        first.Should().HaveCount(3);
        second.Should().BeEmpty("already-materialized occurrences must not be re-emitted");
        service.EventLog.EventsOfKind(FundAdministrationEventKind.RecurringJournalRun)
            .Should().HaveCount(3, "no duplicate run events are recorded on re-invocation");
    }
}
