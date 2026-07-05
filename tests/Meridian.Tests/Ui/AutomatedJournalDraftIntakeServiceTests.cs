using FluentAssertions;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.Ledger;
using Meridian.Ui.Shared.Services;
using Xunit;

namespace Meridian.Tests.Ui;

/// <summary>
/// Tests the automated journal intake: dividend/fee/withholding events projected through
/// AutomatedJournalDraftProjector land in the manual journal workbench queue (the close
/// cockpit's approval source) as validated drafts awaiting human submit/approve.
/// </summary>
public sealed class AutomatedJournalDraftIntakeServiceTests
{
    private const string FundProfileId = "fund-alpha";
    private static readonly Guid BookId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly DateTimeOffset AsOf = new(2026, 07, 01, 16, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task IntakeAsync_DividendAndFeeEvents_LandInWorkbenchQueueAsDrafts()
    {
        var fixture = await CreateFixtureAsync(seedChart: true);

        var result = await fixture.Intake.IntakeAsync(BuildRequest(
            DividendDeclaredEvent(),
            ManagementFeeAccruedEvent()));

        result.Skipped.Should().BeEmpty();
        result.Created.Should().HaveCount(2);
        result.NeedsFixCount.Should().Be(0);
        result.Created.Should().OnlyContain(
            draft => draft.Status == ManualJournalEntryStatusDto.Draft,
            "chart-resolvable automated drafts must be ready for human submission");
        result.Created.Should().OnlyContain(draft => draft.TotalDebits == draft.TotalCredits && draft.TotalDebits > 0m);
        result.Created.Should().OnlyContain(draft => draft.PreparedBy == "automated-journal");

        var dividendDraft = result.Created.Single(draft => draft.EntryType == ManualJournalEntryTypeDto.AccruedBalance);
        dividendDraft.Lines.Should().Contain(line =>
            line.Side == AccountingTemplateLineSideDto.Debit && line.AccountPath == "Assets:Dividend Receivable");
        dividendDraft.Lines.Should().Contain(line =>
            line.Side == AccountingTemplateLineSideDto.Credit && line.AccountPath == "Income:Dividend Income");
        dividendDraft.EvidenceLinks.Should().Contain("/api/evidence/corporate-actions/ca-aapl-2026-07");

        var feeDraft = result.Created.Single(draft => draft.EntryType == ManualJournalEntryTypeDto.AccruedExpense);
        feeDraft.Lines.Should().Contain(line =>
            line.Side == AccountingTemplateLineSideDto.Debit && line.AccountPath == "Expenses:Management Fee Expense");
        feeDraft.Lines.Should().Contain(line =>
            line.Side == AccountingTemplateLineSideDto.Credit && line.AccountPath == "Liabilities:Management Fee Payable");

        // The drafts are visible in the workbench feed the close cockpit reads from.
        var workbench = await fixture.Workbench.GetWorkbenchAsync(FundProfileId, BookId);
        workbench.Drafts.Should().HaveCount(2);
    }

    [Fact]
    public async Task IntakeAsync_SameEventTwice_SkipsDuplicateInsteadOfOverwriting()
    {
        var fixture = await CreateFixtureAsync(seedChart: true);

        var first = await fixture.Intake.IntakeAsync(BuildRequest(DividendDeclaredEvent()));
        var second = await fixture.Intake.IntakeAsync(BuildRequest(DividendDeclaredEvent()));

        first.Created.Should().ContainSingle();
        second.Created.Should().BeEmpty();
        var skip = second.Skipped.Should().ContainSingle().Subject;
        skip.JournalEntryId.Should().Be(first.Created[0].JournalEntryId);
        skip.Reason.Should().Contain("already exists");
    }

    [Fact]
    public async Task IntakeAsync_UnmappedAccounts_SurfaceAsNeedsFixNotSilence()
    {
        var fixture = await CreateFixtureAsync(seedChart: false);

        var result = await fixture.Intake.IntakeAsync(BuildRequest(WithholdingTaxAccruedEvent()));

        var draft = result.Created.Should().ContainSingle().Subject;
        draft.Status.Should().Be(ManualJournalEntryStatusDto.NeedsFix,
            "an unmappable account must be surfaced for fixing, never posted to a wrong account");
        draft.ValidationIssues.Should().Contain(issue => issue.Code == "manual-je.account-missing");
        result.NeedsFixCount.Should().Be(1);
    }

    [Fact]
    public async Task IntakeAsync_InvalidEvent_SkippedWithReasonAndBatchContinues()
    {
        var fixture = await CreateFixtureAsync(seedChart: true);
        var invalidEvent = new AutomatedJournalEvent(
            AutomatedJournalEventKind.CashInterestCredited,
            "CASH",
            Amount: 0m,
            AsOf);

        var result = await fixture.Intake.IntakeAsync(BuildRequest(invalidEvent, DividendDeclaredEvent()));

        result.Skipped.Should().ContainSingle().Which.Reason.Should().Contain("Projection failed");
        result.Created.Should().ContainSingle("one bad event must not block the rest of the batch");
    }

    [Fact]
    public async Task IntakeAsync_CreatedDraft_CanBeSubmittedByHumanThroughExistingWorkflow()
    {
        var fixture = await CreateFixtureAsync(seedChart: true);
        var intake = await fixture.Intake.IntakeAsync(BuildRequest(DividendDeclaredEvent()));
        var draft = intake.Created.Single();

        var validated = await fixture.Workbench.ValidateDraftAsync(
            new ValidateManualJournalEntryDraftRequest(draft, Actor: "controller"));
        validated.ValidationIssues.Should().BeEmpty("diagnostic: submission-blocking issues");

        var submitted = await fixture.Workbench.SubmitApprovalAsync(new SubmitManualJournalEntryApprovalRequest(
            draft.JournalEntryId,
            FundProfileId,
            Actor: "controller",
            Version: draft.Version,
            EvidenceLinks: ["/api/evidence/corporate-actions/ca-aapl-2026-07"],
            ActionOrigin: OperationsActionOriginDto.HumanOperator,
            LedgerBookId: BookId));

        submitted.Status.Should().Be(ManualJournalEntryStatusDto.Submitted,
            "automated drafts must flow through the existing human approval lifecycle");
        submitted.ApprovalId.Should().NotBeNullOrWhiteSpace();
    }

    // -------------------------------------------------------------------------
    // Fixture
    // -------------------------------------------------------------------------

    private sealed record Fixture(
        AutomatedJournalDraftIntakeService Intake,
        IManualJournalEntryWorkbenchService Workbench,
        InMemoryManualJournalEntryDraftStore DraftStore);

    private static async Task<Fixture> CreateFixtureAsync(bool seedChart)
    {
        var configurationStore = new InMemoryAccountingConfigurationStore();
        if (seedChart)
        {
            await configurationStore.SaveAsync(new AccountingConfigurationWorkspaceDto(
                FundProfileId,
                LedgerBookId: null,
                AccountingConfigurationStatusDto.Draft,
                "test",
                DateTimeOffset.UtcNow,
                LedgerBooks: [],
                ChartOfAccounts: BuildChart(),
                JournalTemplates: [],
                PostingRules: [],
                ValidationIssues: [],
                AuditTrail: []));
        }

        var configurationService = new AccountingConfigurationService(
            configurationStore,
            new InMemoryAccountingActionAuditStore());
        var draftStore = new InMemoryManualJournalEntryDraftStore();
        var workbench = new ManualJournalEntryWorkbenchService(
            draftStore,
            configurationService,
            new InMemoryAccountingActionAuditStore());
        var intake = new AutomatedJournalDraftIntakeService(workbench, draftStore, configurationService);
        return new Fixture(intake, workbench, draftStore);
    }

    private static IReadOnlyList<ChartOfAccountsNodeDto> BuildChart() =>
    [
        Node("Assets:Cash", "Cash", "Asset"),
        Node("Assets:Dividend Receivable", "Dividend Receivable", "Asset"),
        Node("Income:Dividend Income", "Dividend Income", "Revenue"),
        Node("Income:Cash Interest Income", "Cash Interest Income", "Revenue"),
        Node("Expenses:Management Fee Expense", "Management Fee Expense", "Expense"),
        Node("Liabilities:Management Fee Payable", "Management Fee Payable", "Liability"),
        Node("Expenses:Withholding Tax Expense", "Withholding Tax Expense", "Expense"),
        Node("Liabilities:Withholding Tax Payable", "Withholding Tax Payable", "Liability")
    ];

    private static ChartOfAccountsNodeDto Node(string path, string name, string type)
        => new(NodeId: path, Path: path, AccountName: name, AccountType: type);

    private static AutomatedJournalDraftIntakeRequest BuildRequest(params AutomatedJournalEvent[] events)
        => new(
            FundProfileId,
            Currency: "USD",
            Events: events,
            Actor: "automated-journal",
            LedgerBookId: BookId,
            PeriodId: "2026-07",
            EntityId: "entity-alpha");

    private static AutomatedJournalEvent DividendDeclaredEvent() => new(
        AutomatedJournalEventKind.DividendDeclared,
        "AAPL",
        Amount: 500m,
        AsOf,
        Description: "AAPL dividend declared",
        SourceEventId: "ca-aapl-2026-07",
        EffectiveDate: new DateOnly(2026, 07, 01),
        IdempotencyKey: "dividend-declared|AAPL|2026-07-01",
        EvidenceReferences:
        [
            new JournalEvidenceReference(
                "ev-ca-aapl-2026-07",
                "/api/evidence/corporate-actions/ca-aapl-2026-07",
                "corporate-action",
                "security-master",
                AsOf,
                "automation")
        ]);

    private static AutomatedJournalEvent ManagementFeeAccruedEvent() => new(
        AutomatedJournalEventKind.ManagementFeeAccrued,
        "FUND-ALPHA",
        Amount: 1_200m,
        AsOf,
        EffectiveDate: new DateOnly(2026, 07, 01),
        IdempotencyKey: "mgmt-fee|FUND-ALPHA|2026-07");

    private static AutomatedJournalEvent WithholdingTaxAccruedEvent() => new(
        AutomatedJournalEventKind.WithholdingTaxAccrued,
        "AAPL",
        Amount: 75m,
        AsOf,
        EffectiveDate: new DateOnly(2026, 07, 01),
        IdempotencyKey: "wht|AAPL|2026-07-01");
}
