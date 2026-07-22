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
        service.IsPeriodLocked(Book, "2026-Q2").Should().BeFalse();
    }

    [Fact]
    public void DueRecurringJournals_SkipsLockedOccurrences_AndRecordsRunsOnPost()
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

        var due = service.DueRecurringJournals("sched-1", new DateOnly(2026, 3, 31));

        due.Should().HaveCount(2, "the February occurrence is blocked by the lock");
        due.Should().OnlyContain(occurrence => !occurrence.BlockedByLock);

        // Querying is read-only; the run event is recorded only when a post is confirmed.
        service.EventLog.EventsOfKind(FundAdministrationEventKind.RecurringJournalRun).Should().BeEmpty();
        foreach (var occurrence in due)
            service.RecordRecurringJournalPosted("sched-1", occurrence.EffectiveDate, "controller");
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
    public void ProjectYearEndClose_PreviewsWithoutRecording_ThenRecordsOnClose()
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

        var projection = service.ProjectYearEndClose(input);

        projection.IsReady.Should().BeTrue();
        projection.NetIncome.Should().Be(600m);
        // The projection is a preview only — nothing is recorded until the close actually posts.
        service.EventLog.EventsOfKind(FundAdministrationEventKind.YearEndClosed).Should().BeEmpty();

        service.RecordYearEndClosed("FY2026", projection.NetIncome, input.FiscalYearEndUtc, "controller");
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
        // The change event captures the fields that drive resolution (priority/effective window/level),
        // so a replacement that changes only those is still a reconstructable audit event.
        var pricingEvent = service.EventLog.EventsOfKind(FundAdministrationEventKind.PricingRuleChanged)
            .Should().ContainSingle().Which;
        pricingEvent.Attributes["priority"].Should().Be("10");
        pricingEvent.Attributes["fairValueLevel"].Should().Be("Unclassified");
    }

    [Fact]
    public void RegisterJournalTemplate_RecordsFullDefinition()
    {
        var service = new FundAdministrationControlService();
        service.RegisterJournalTemplate(FeeTemplate(), "controller");

        // The full template definition (accounts and sides) is captured so a same-id replacement with
        // different lines produces a distinct, tamper-evident registration event.
        var registered = service.EventLog.EventsOfKind(FundAdministrationEventKind.JournalTemplateRegistered)
            .Should().ContainSingle().Which;
        registered.Attributes["definition"].Should().Contain("Debit").And.Contain("Credit");
    }

    [Fact]
    public void RegisterJournalTemplate_MalformedReplacement_PreservesApprovedTemplateAndAuditTrail()
    {
        var service = new FundAdministrationControlService();
        var approvedTemplate = FeeTemplate();
        service.RegisterJournalTemplate(approvedTemplate, "controller");
        var malformedReplacement = new JournalTemplate(
            "fee",
            "Malformed Fee Accrual",
            "An invalid replacement.",
            [new JournalTemplateLine(null!, JournalTemplateSide.Debit, "fee")]);

        var act = () => service.RegisterJournalTemplate(malformedReplacement, "controller");

        act.Should().Throw<ArgumentNullException>();
        service.RegisteredJournalTemplates.Should().ContainSingle().Which.Should().BeSameAs(approvedTemplate);
        service.EventLog.EventsOfKind(FundAdministrationEventKind.JournalTemplateRegistered).Should().ContainSingle();
        service.EventLog.VerifyIntegrity().Should().BeTrue();
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
            At), "admin");

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
    public void RegisterOnboardingTemplate_RecordsContentIdentifyingEvent()
    {
        var service = new FundAdministrationControlService();
        service.RegisterOnboardingTemplate(new OnboardingTemplate(
            "std-fund",
            "Standard Fund",
            "Standard fund skeleton.",
            [
                new OnboardingTemplateNode(OnboardingTemplateNode.Types.Organization, "org", "{orgCode}", "{orgName}"),
                new OnboardingTemplateNode(OnboardingTemplateNode.Types.Portfolio, "port", "{fundCode}-MAIN", "{fundName} Main", ParentKey: "org", BaseCurrency: "USD"),
            ],
            "admin",
            At), "admin");

        // Registration is audited with a content-identifying event (not just id + count), so the exact
        // approved hierarchy/codes/parents — including base currency and node attributes — are recorded
        // tamper-evidently.
        var registered = service.EventLog.EventsOfKind(FundAdministrationEventKind.OnboardingTemplateRegistered)
            .Should().ContainSingle().Which;
        registered.Attributes["nodeCount"].Should().Be("2");
        registered.Attributes["nodes"].Should().Contain("port|Portfolio|{fundCode}-MAIN|{fundName} Main|org");
        registered.Attributes["nodes"].Should().Contain("ccy=USD", "the base currency is part of the approved definition");
        service.EventLog.VerifyIntegrity().Should().BeTrue();
    }

    [Fact]
    public void OnboardingTemplate_SelfParentedNode_Throws()
    {
        var act = () => new OnboardingTemplate(
            "std-fund",
            "Standard Fund",
            "Standard fund skeleton.",
            [new OnboardingTemplateNode(OnboardingTemplateNode.Types.Portfolio, "port", "P", "P", ParentKey: "port")],
            "admin",
            At);

        act.Should().Throw<ArgumentException>("a node cannot be its own parent");
    }

    [Fact]
    public void ApplyOnboardingTemplate_OrdersNodesParentFirst()
    {
        var service = new FundAdministrationControlService();
        service.RegisterOnboardingTemplate(new OnboardingTemplate(
            "std-fund",
            "Standard Fund",
            "Standard fund skeleton.",
            [
                // A child declared before its parent must still be emitted parent-first in the plan.
                new OnboardingTemplateNode(OnboardingTemplateNode.Types.Book, "book", "GL", "GL", ParentKey: "org"),
                new OnboardingTemplateNode(OnboardingTemplateNode.Types.Organization, "org", "ORG", "Org"),
            ],
            "admin",
            At), "admin");

        var plan = service.ApplyOnboardingTemplate("std-fund", new Dictionary<string, string>(), "admin");

        var nodes = plan.Nodes.ToList();
        var orgIndex = nodes.FindIndex(node => node.Key == "org");
        var bookIndex = nodes.FindIndex(node => node.Key == "book");
        orgIndex.Should().BeLessThan(bookIndex, "a parent must be created before its child");
    }

    [Fact]
    public void OnboardingTemplate_ParentCycle_Throws()
    {
        var act = () => new OnboardingTemplate(
            "std-fund",
            "Standard Fund",
            "Standard fund skeleton.",
            [
                new OnboardingTemplateNode(OnboardingTemplateNode.Types.Entity, "a", "A", "A", ParentKey: "b"),
                new OnboardingTemplateNode(OnboardingTemplateNode.Types.Portfolio, "b", "B", "B", ParentKey: "a"),
            ],
            "admin",
            At);

        act.Should().Throw<ArgumentException>("a parent cycle is unbuildable parent-first");
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
    public void ProjectYearEndClose_NotReady_RecordsNothing()
    {
        var service = new FundAdministrationControlService();
        var input = new YearEndCloseInput(
            "FY2026",
            new DateTimeOffset(2026, 12, 31, 23, 59, 59, TimeSpan.Zero),
            new Dictionary<LedgerAccount, decimal> { [LedgerAccounts.RealizedGain] = 1_000m },
            "controller",
            requiredPeriodIds: ["Q1", "Q2"],
            closedPeriodIds: ["Q1"]);

        var projection = service.ProjectYearEndClose(input);

        projection.IsReady.Should().BeFalse();
        service.EventLog.EventsOfKind(FundAdministrationEventKind.YearEndClosed)
            .Should().BeEmpty("a projection records nothing, and an unclosed fiscal year is never recorded as closed");
    }

    [Fact]
    public void DueRecurringJournals_AreRetrySafeUntilPosted_ThenExcludedOnce()
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

        var due = service.DueRecurringJournals("sched-1", new DateOnly(2026, 3, 31));
        due.Should().HaveCount(3);

        // A failed/absent post leaves occurrences due, so a retry sees the same set (no silent drop).
        service.DueRecurringJournals("sched-1", new DateOnly(2026, 3, 31)).Should().HaveCount(3);

        // Confirming each post records exactly one run event and removes it; re-confirm is a no-op.
        foreach (var occurrence in due)
            service.RecordRecurringJournalPosted("sched-1", occurrence.EffectiveDate, "controller");
        service.RecordRecurringJournalPosted("sched-1", due[0].EffectiveDate, "controller").Should().BeNull();

        service.DueRecurringJournals("sched-1", new DateOnly(2026, 3, 31)).Should().BeEmpty("posted occurrences are no longer due");
        service.EventLog.EventsOfKind(FundAdministrationEventKind.RecurringJournalRun)
            .Should().HaveCount(3, "each occurrence records exactly one run event, and never double-posts");
    }
}
