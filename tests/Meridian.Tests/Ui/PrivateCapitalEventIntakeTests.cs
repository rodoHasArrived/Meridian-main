using FluentAssertions;
using Meridian.Contracts.Ledger;
using Meridian.FinancialOperations.FundAdministration;
using Meridian.Ledger;
using Meridian.Ui.Shared.Services;
using Xunit;

namespace Meridian.Tests.Ui;

/// <summary>
/// Capital-call default interest and distribution waterfall drafts through the real runner, journal
/// intake and workbench: each lands in the approval queue unposted, and neither can book the same
/// economics twice.
/// </summary>
public sealed class PrivateCapitalEventIntakeTests : IDisposable
{
    private const string Fund = "fund-alpha";
    private const string Tenant = "tenant-alpha";
    private const string Company = "company-alpha";
    private const string CallId = "call-1";
    private const string DefaultId = "default:cmt-1:call-1:cmt-1";
    private static readonly Guid BookId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly DateOnly NoticeDate = new(2026, 3, 15);
    private static readonly DateTimeOffset AsOf = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-private-capital-events-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task DefaultInterest_AccruesOnTheUnfundedCall_AndLaterRunsBookOnlyWhatIsNotYetPosted()
    {
        // 1,000,000 called; due 25 March with 5 grace days, so interest runs from 30 March at 10%.
        var issuance = new[] { CapitalCallFundingTestData.PostedIssuanceEvent(Fund, CallId, "cmt-1", "lp-1", 1_000_000m, NoticeDate, AsOf) };
        var issued = CapitalCallFundingTestData.PostedIssuanceImpact(Fund, CallId, "cmt-1", "lp-1", 1_000_000m, NoticeDate);
        var first = await CreateFixtureAsync(issuance, [issued]);

        var april = await first.Runner.RunCapitalCallDefaultInterestIntakeAsync(DefaultRequest(new(2026, 4, 29)));

        april.Readiness.Should().Be(AutomatedJournalIntakeReadiness.Ready);
        var draft = april.Intake.Created.Should().ContainSingle().Subject;
        draft.Status.Should().Be(ManualJournalEntryStatusDto.Draft);
        draft.TotalDebits.Should().Be(8_219.18m, "1,000,000 x 10% x 30/365");
        draft.Lines.Select(line => (line.AccountPath, line.Side)).Should().BeEquivalentTo(new[]
        {
            ("Assets:Capital Call Default Interest Receivable", AccountingTemplateLineSideDto.Debit),
            ("Revenue:Capital Call Default Interest Income", AccountingTemplateLineSideDto.Credit)
        });
        draft.TreasuryContext!.IdempotencyKey.Should().Be($"default-interest:{DefaultId}:20260429");
        draft.TreasuryContext.CapitalAccountId.Should().Be("ca-lp-1");
        draft.EvidenceLinks.Should().Contain("evidence://lpa/default-terms");

        // The same run again admits nothing new.
        var replay = await first.Runner.RunCapitalCallDefaultInterestIntakeAsync(DefaultRequest(new(2026, 4, 29)));
        replay.Intake.Created.Should().BeEmpty();
        replay.Intake.Skipped.Should().ContainSingle().Which.IsReadyDuplicate.Should().BeTrue();

        // While April's draft awaits approval, accruing through May would count April's days twice.
        var early = await first.Runner.RunCapitalCallDefaultInterestIntakeAsync(DefaultRequest(new(2026, 5, 29)));
        early.Readiness.Should().Be(AutomatedJournalIntakeReadiness.Blocked);
        early.ReadinessBlockers.Should().ContainSingle().Which.Should().Contain("post or reject it before accruing further");
        early.Intake.Created.Should().BeEmpty();

        // Once April's interest is posted, May books the 30 further days only.
        await first.Drafts.SaveAsync(draft with { Status = ManualJournalEntryStatusDto.Posted, PostedAtUtc = AsOf, PostedBy = "controller" });
        var second = await CreateFixtureAsync(issuance, [issued, PostedInterest("20260429", 8_219.18m)]);
        var may = await second.Runner.RunCapitalCallDefaultInterestIntakeAsync(DefaultRequest(new(2026, 5, 29)));
        var mayDraft = may.Intake.Created.Should().ContainSingle().Subject;
        mayDraft.TotalDebits.Should().Be(8_219.18m, "16,438.36 cumulative less 8,219.18 posted");

        await second.Drafts.SaveAsync(mayDraft with { Status = ManualJournalEntryStatusDto.Posted, PostedAtUtc = AsOf, PostedBy = "controller" });
        var settled = await CreateFixtureAsync(issuance, [issued, PostedInterest("20260429", 8_219.18m), PostedInterest("20260529", 8_219.18m)]);
        var again = await settled.Runner.RunCapitalCallDefaultInterestIntakeAsync(DefaultRequest(new(2026, 5, 29)));
        again.Intake.Created.Should().BeEmpty();
        again.ProducerSkips.Should().ContainSingle().Which.Reason.Should().Contain("already posted");
    }

    [Fact]
    public async Task DefaultInterest_StopsAtTheCureDate_AndIgnoresACallFundedWithinGrace()
    {
        var issuance = new[]
        {
            CapitalCallFundingTestData.PostedIssuanceEvent(Fund, CallId, "cmt-1", "lp-1", 1_000_000m, NoticeDate, AsOf),
        };
        var issued = CapitalCallFundingTestData.PostedIssuanceImpact(Fund, CallId, "cmt-1", "lp-1", 1_000_000m, NoticeDate);
        // Funded in full on 9 April: 10 days of interest from 30 March, however late the run.
        var cured = await CreateFixtureAsync(issuance,
            [issued, CapitalCallFundingTestData.PostedFundingImpact(Fund, CallId, "cmt-1", "lp-1", 1_000_000m, new(2026, 4, 9))]);
        var late = await cured.Runner.RunCapitalCallDefaultInterestIntakeAsync(DefaultRequest(new(2026, 5, 29)));
        late.Intake.Created.Should().ContainSingle().Which.TotalDebits.Should().Be(2_739.73m);

        var prompt = await CreateFixtureAsync(issuance,
            [issued, CapitalCallFundingTestData.PostedFundingImpact(Fund, CallId, "cmt-1", "lp-1", 1_000_000m, new(2026, 3, 28))]);
        var none = await prompt.Runner.RunCapitalCallDefaultInterestIntakeAsync(DefaultRequest(new(2026, 5, 29)));
        none.Intake.Created.Should().BeEmpty();
        none.Readiness.Should().Be(AutomatedJournalIntakeReadiness.Ready);
        none.ProducerSkips.Should().ContainSingle().Which.Reason.Should().Contain("No default interest has accrued");
    }

    [Fact]
    public async Task DefaultInterest_WithoutAPostedCallOrEvidence_Blocks()
    {
        var fixture = await CreateFixtureAsync([], []);

        var unissued = await fixture.Runner.RunCapitalCallDefaultInterestIntakeAsync(DefaultRequest(new(2026, 4, 29)));
        var unevidenced = await fixture.Runner.RunCapitalCallDefaultInterestIntakeAsync(DefaultRequest(new(2026, 4, 29), evidence: []));

        unissued.Readiness.Should().Be(AutomatedJournalIntakeReadiness.Blocked);
        unissued.ReadinessBlockers.Should().ContainSingle().Which.Should().Contain("no posted issuance");
        unevidenced.ReadinessBlockers.Should().ContainSingle().Which.Should().Contain("no retained evidence");
    }

    [Fact]
    public async Task Distribution_SplitsThroughTheWaterfall_AndTheNextOneContinuesFromThePostedTiers()
    {
        var fixture = await CreateFixtureAsync([], []);

        // 1,000,000 contributed, 80,000 preferred accrued, 20% carry with full catch-up.
        var first = await fixture.Runner.RunDistributionDeclarationIntakeAsync(DistributionRequest("dist-1", new(2026, 4, 30)));

        first.Readiness.Should().Be(AutomatedJournalIntakeReadiness.Ready);
        var firstDraft = first.Intake.Created.Should().ContainSingle().Subject;
        firstDraft.Status.Should().Be(ManualJournalEntryStatusDto.Draft);
        firstDraft.EntryType.Should().Be(ManualJournalEntryTypeDto.Distribution);
        firstDraft.TreasuryContext!.CapitalAccountId.Should().Be("ca-lp-1");
        Lines(firstDraft).Should().BeEquivalentTo(new[]
        {
            ("Equity:Investor Capital", AccountingTemplateLineSideDto.Debit, 600_000m),
            ("Liabilities:Fund Distribution Payable", AccountingTemplateLineSideDto.Credit, 600_000m)
        });
        firstDraft.ValidationIssues.Where(issue => issue.Severity == AccountingConfigurationValidationSeverityDto.Critical).Should().BeEmpty();

        // Until the first is posted its tiers are not final, so the second cannot be split.
        var early = await fixture.Runner.RunDistributionDeclarationIntakeAsync(DistributionRequest("dist-2", new(2026, 5, 31)));
        early.Readiness.Should().Be(AutomatedJournalIntakeReadiness.Blocked);
        early.ReadinessBlockers.Should().ContainSingle().Which.Should().Contain("post or reject it first");

        await fixture.Drafts.SaveAsync(firstDraft with { Status = ManualJournalEntryStatusDto.Posted, PostedAtUtc = AsOf, PostedBy = "controller" });
        var second = await fixture.Runner.RunDistributionDeclarationIntakeAsync(DistributionRequest("dist-2", new(2026, 5, 31)));

        // Remaining capital 400,000, preferred 80,000, GP catch-up 20,000, then 100,000 split 80/20.
        Lines(second.Intake.Created.Should().ContainSingle().Subject).Should().BeEquivalentTo(new[]
        {
            ("Equity:Investor Capital", AccountingTemplateLineSideDto.Debit, 560_000m),
            ("Equity:Carried Interest Allocation", AccountingTemplateLineSideDto.Debit, 40_000m),
            ("Liabilities:Fund Distribution Payable", AccountingTemplateLineSideDto.Credit, 600_000m)
        });

        var replay = await fixture.Runner.RunDistributionDeclarationIntakeAsync(DistributionRequest("dist-2", new(2026, 5, 31)));
        replay.Intake.Created.Should().BeEmpty();
        replay.Intake.Skipped.Should().ContainSingle();
        var retained = await fixture.Register.ListAsync(Tenant, Company, Fund, BookId, "lp-1");
        retained.Select(record => (record.DistributionId, record.ReturnOfCapital, record.PreferredReturn, record.GpCatchUp, record.GpCarry))
            .Should().Equal(("dist-1", 600_000m, 0m, 0m, 0m), ("dist-2", 400_000m, 80_000m, 20_000m, 20_000m));
        retained.Should().OnlyContain(record => record.JournalEntryId != Guid.Empty);

        // The same distribution id cannot be redeclared with a different amount.
        var changed = await fixture.Runner.RunDistributionDeclarationIntakeAsync(DistributionRequest("dist-2", new(2026, 5, 31), amount: 700_000m));
        changed.Readiness.Should().Be(AutomatedJournalIntakeReadiness.Blocked);
        changed.ReadinessBlockers.Should().ContainSingle().Which.Should().Contain("different terms");
    }

    [Fact]
    public async Task Distribution_RejectedEarlierDistribution_DoesNotCountTowardLaterTiers()
    {
        var fixture = await CreateFixtureAsync([], []);
        var first = await fixture.Runner.RunDistributionDeclarationIntakeAsync(DistributionRequest("dist-1", new(2026, 4, 30)));
        await fixture.Drafts.SaveAsync(first.Intake.Created.Single() with { Status = ManualJournalEntryStatusDto.Rejected });

        var second = await fixture.Runner.RunDistributionDeclarationIntakeAsync(DistributionRequest("dist-2", new(2026, 5, 31)));

        // Nothing was returned by the rejected distribution, so this one is all return of capital.
        Lines(second.Intake.Created.Should().ContainSingle().Subject).Should().BeEquivalentTo(new[]
        {
            ("Equity:Investor Capital", AccountingTemplateLineSideDto.Debit, 600_000m),
            ("Liabilities:Fund Distribution Payable", AccountingTemplateLineSideDto.Credit, 600_000m)
        });
    }

    private static IEnumerable<(string, AccountingTemplateLineSideDto, decimal)> Lines(ManualJournalEntryDraftDto draft)
        => draft.Lines.Select(line => (line.AccountPath, line.Side, line.Amount));

    private static RunCapitalCallDefaultInterestDraftIntakeRequest DefaultRequest(DateOnly through, string[]? evidence = null)
        => new(Fund, "USD", "ops-user", CallId, through,
            [new CapitalCallDefaultTermsInput("cmt-1", "ca-lp-1", "lp-1", new DateOnly(2026, 3, 25), 0.10m,
                DefaultInterestConvention.Actual365Fixed, 5, evidence ?? ["evidence://lpa/default-terms"])],
            BookId, "2026-04", "entity-alpha", Tenant, Company, AsOf);

    private static RunDistributionDeclarationDraftIntakeRequest DistributionRequest(string id, DateOnly date, decimal amount = 600_000m)
        => new(Fund, "USD", "ops-user", id, date, 0.20m, 1m,
            [new DistributionInvestorInput("lp-1", "ca-lp-1", 1_000_000m, 80_000m, amount, ["evidence://distribution/notice"])],
            BookId, null, "entity-alpha", Tenant, Company, AsOf);

    private static PrivateCapitalLedgerImpactDto PostedInterest(string through, decimal amount)
    {
        var id = Guid.NewGuid();
        return new($"journal-entry:{id:D}", id, $"fund-event:{Fund}:default-interest:{DefaultId}:{through}", "DefaultInterest",
            "ca-lp-1", "lp-1", ManualJournalEntryStatusDto.Approved, new DateOnly(2026, 4, 29), "USD", amount, amount, 0m, 2, true, true, [],
            [new("line-1", "Capital Call Default Interest Receivable", AccountingTemplateLineSideDto.Debit, amount, "USD", "lp-1", null, null, null),
             new("line-2", "Capital Call Default Interest Income", AccountingTemplateLineSideDto.Credit, amount, "USD", Fund, null, null, null)],
            []);
    }

    private async Task<Fixture> CreateFixtureAsync(PrivateCapitalFundEventDto[] postedFundEvents, PrivateCapitalLedgerImpactDto[] postedLedgerImpacts)
    {
        var workspace = CapitalCallFundingTestData.Workspace(Fund, Tenant, Company);
        var configurationStore = new InMemoryAccountingConfigurationStore();
        await configurationStore.SaveAsync(workspace with
        {
            ChartOfAccounts =
            [
                .. workspace.ChartOfAccounts,
                Node("Assets:Capital Call Default Interest Receivable", "Capital Call Default Interest Receivable", "Asset"),
                Node("Revenue:Capital Call Default Interest Income", "Capital Call Default Interest Income", "Revenue"),
                Node("Equity:Carried Interest Allocation", "Carried Interest Allocation", "Equity"),
                Node("Liabilities:Fund Distribution Payable", "Fund Distribution Payable", "Liability")
            ]
        });
        var configurationService = new AccountingConfigurationService(configurationStore, new InMemoryAccountingActionAuditStore());
        var drafts = new FileManualJournalEntryDraftStore(Path.Combine(_root, "drafts.json"));
        var workbench = new ManualJournalEntryWorkbenchService(drafts, configurationService, new InMemoryAccountingActionAuditStore());
        var register = new FileDistributionRegister(Path.Combine(_root, "registers"));
        var runner = new AutomatedJournalIntakeRunner(new AutomatedJournalDraftIntakeService(workbench, drafts, configurationService),
            new FeeScheduleAccrualEventProducer(),
            manualJournalWorkbench: new PostedActivityManualJournalWorkbench(workbench, postedFundEvents, postedLedgerImpacts),
            distributionRegister: register);
        return new(runner, drafts, register);
    }

    private static ChartOfAccountsNodeDto Node(string path, string name, string type)
        => new(NodeId: path, Path: path, AccountName: name, AccountType: type);

    private sealed record Fixture(AutomatedJournalIntakeRunner Runner, FileManualJournalEntryDraftStore Drafts, FileDistributionRegister Register);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
