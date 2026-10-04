using FluentAssertions;
using Meridian.Contracts.Ledger;
using Meridian.FinancialOperations.FundAdministration;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Meridian.Ui.Shared.Services;
using Moq;
using Xunit;

namespace Meridian.Tests.Ui;

public sealed class RecurringJournalLifecycleTests : IDisposable
{
    private const string Fund = "fund-recurring-lifecycle";
    private const string Tenant = "tenant-recurring";
    private const string Company = "company-recurring";
    private const string ScheduleId = "monthly-fee";
    private static readonly Guid Book = Guid.Parse("caeeebac-ecb3-4e76-8391-a615d9ab2345");
    private static readonly Guid Period = Guid.Parse("8e867b27-4a59-4c3b-86b3-ec2b7f1b2347");
    private static readonly DateOnly Date = new(2026, 7, 1);
    private static readonly DateTimeOffset Now = new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly RecurringJournalScope Scope = new(Fund, Book, "entity-recurring", "USD", Tenant, Company);
    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-recurring-lifecycle-" + Guid.NewGuid().ToString("N"));
    private string StateDirectory => Path.Combine(_root, "recurring");

    [Theory]
    [InlineData(JournalEntryLifecycleActionDto.Submit, false)]
    [InlineData(JournalEntryLifecycleActionDto.Submit, true)]
    [InlineData(JournalEntryLifecycleActionDto.Approve, false)]
    [InlineData(JournalEntryLifecycleActionDto.Approve, true)]
    [InlineData(JournalEntryLifecycleActionDto.Post, false)]
    [InlineData(JournalEntryLifecycleActionDto.Post, true)]
    public async Task MissingOrCorruptRegistry_BlocksLifecycleWithoutEffects(JournalEntryLifecycleActionDto action, bool corrupt)
    {
        var fixture = await CreateFixtureAsync(action);
        var statePath = Path.Combine(StateDirectory, "recurring-journals.json");
        if (corrupt)
            await File.WriteAllTextAsync(statePath, "{invalid retained snapshot");
        else
            File.Delete(statePath);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Workbench.ApplyLifecycleActionAsync(Request(fixture.Draft, action)));

        failure.Message.Should().Contain("durable recurring journal state");
        await AssertUnchangedAsync(fixture);
    }

    [Theory]
    [InlineData(JournalEntryLifecycleActionDto.Approve, false)]
    [InlineData(JournalEntryLifecycleActionDto.Approve, true)]
    [InlineData(JournalEntryLifecycleActionDto.Post, false)]
    [InlineData(JournalEntryLifecycleActionDto.Post, true)]
    public async Task ChangedDefinition_BlocksApprovalAndPostingWithoutEffects(JournalEntryLifecycleActionDto action, bool changeTemplate)
    {
        var fixture = await CreateFixtureAsync(action);
        await using (var session = await fixture.Registry.OpenSessionAsync())
        {
            if (changeTemplate)
                await session.RegisterTemplateAsync(Template(2m), "controller", 1, Now.AddMinutes(1), scope: Scope);
            else
                await session.RegisterScheduleAsync(Schedule(250m), Scope, [Source()], "controller", 1, Now.AddMinutes(1));
        }

        await Assert.ThrowsAsync<RecurringJournalDefinitionChangedException>(() => fixture.Workbench.ApplyLifecycleActionAsync(Request(fixture.Draft, action)));

        await AssertUnchangedAsync(fixture);
    }

    [Fact]
    public async Task DirectSubmit_RequiresDurableRegistryAndExactCurrentClaim()
    {
        var fixture = await CreateFixtureAsync(JournalEntryLifecycleActionDto.Submit);
        await using (var session = await fixture.Registry.OpenSessionAsync())
            await session.RegisterTemplateAsync(Template(2m), "controller", 1, Now.AddMinutes(1), scope: Scope);

        await Assert.ThrowsAsync<RecurringJournalDefinitionChangedException>(() => fixture.Workbench.SubmitApprovalAsync(Submit(fixture.Draft)));
        await AssertUnchangedAsync(fixture);

        await using (var session = await fixture.Registry.OpenSessionAsync())
            await session.ActivateDefinitionsAsync(ScheduleId, 1, 1, "controller", "Restore reviewed source definition", Now.AddMinutes(2));
        var submitted = await fixture.Workbench.SubmitApprovalAsync(Submit(fixture.Draft));
        submitted.Status.Should().Be(ManualJournalEntryStatusDto.Submitted);
        submitted.RecurringJournalEvidenceJson.Should().Be(fixture.Draft.RecurringJournalEvidenceJson);
        // Even a completed receipt cannot bypass the live registry on an exact retry.
        File.Delete(Path.Combine(StateDirectory, "recurring-journals.json"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Workbench.SubmitApprovalAsync(Submit(fixture.Draft)));
        (await fixture.Drafts.GetAsync(Fund, submitted.JournalEntryId)).Should().BeEquivalentTo(submitted);
        (await fixture.Audit.ListAsync(Fund, Book)).Should().HaveCount(2);
    }

    [Fact]
    public async Task MissingRegistryDependency_BlocksRecurringButOrdinaryDraftStillSubmits()
    {
        var fixture = await CreateFixtureAsync(JournalEntryLifecycleActionDto.Submit, omitRegistry: true);
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Workbench.SubmitApprovalAsync(Submit(fixture.Draft)));
        failure.Message.Should().Contain("durable recurring journal registry is required");
        await AssertUnchangedAsync(fixture);

        var ordinary = fixture.Draft with
        {
            JournalEntryId = Guid.NewGuid(),
            RecurringJournalEvidenceJson = null,
            RecurringJournalEvidenceDigest = null,
            RequiresRecurringJournalEvidence = false,
            TreasuryContext = null
        };
        await fixture.Drafts.SaveAsync(ordinary);
        var submitted = await fixture.Workbench.SubmitApprovalAsync(Submit(ordinary));
        submitted.Status.Should().Be(ManualJournalEntryStatusDto.Submitted);
    }

    [Fact]
    public async Task RegistryLease_IsHeldUntilLifecycleEffectsFinish()
    {
        var fixture = await CreateFixtureAsync(JournalEntryLifecycleActionDto.Submit);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Ledger.Setup(value => value.GetPeriodAsync(Period, It.IsAny<CancellationToken>()))
            .Returns(async () => { entered.TrySetResult(); await release.Task; return OpenPeriod(); });
        var submission = fixture.Workbench.SubmitApprovalAsync(Submit(fixture.Draft));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var competingLease = new FileRecurringJournalStore(StateDirectory).OpenSessionAsync(timeout.Token);
        try
        {
            competingLease.IsCompleted.Should().BeFalse("the lifecycle command still owns the durable registry lease");
        }
        finally { release.TrySetResult(); }
        var submitted = await submission;
        await using var session = await competingLease;
        submitted.Status.Should().Be(ManualJournalEntryStatusDto.Submitted);
        await session.RegisterTemplateAsync(Template(2m), "controller", 1, Now.AddMinutes(1), scope: Scope);
        session.Occurrences.Single().State.Should().Be(RecurringOccurrenceState.DefinitionChanged);
    }

    private async Task<Fixture> CreateFixtureAsync(JournalEntryLifecycleActionDto action, bool omitRegistry = false)
    {
        var registry = new FileRecurringJournalStore(StateDirectory);
        await registry.InitializeAsync();
        await using (var session = await registry.OpenSessionAsync())
            await session.ConfigureAsync(Schedule(), Template(), Scope, [Source()], "controller", 0, 0, Now);
        var configuration = new InMemoryAccountingConfigurationStore();
        await configuration.SaveAsync(new AccountingConfigurationWorkspaceDto(Fund, Book,
            AccountingConfigurationStatusDto.Draft, "test", Now, [],
            [new("expense", "Expenses:Management Fee Expense", "Management Fee Expense", "Expense"),
             new("payable", "Liabilities:Management Fee Payable", "Management Fee Payable", "Liability")],
            [], [], [], [], TenantId: Tenant, CompanyId: Company));
        var audit = new FileAccountingConfigurationStore(Path.Combine(_root, "audit.json"));
        var configurationService = new AccountingConfigurationService(configuration, audit);
        var drafts = new FileManualJournalEntryDraftStore(Path.Combine(_root, "drafts.json"));
        var ledger = new Mock<ILedgerJournalStore>(MockBehavior.Strict);
        ledger.Setup(value => value.GetPeriodAsync(Period, It.IsAny<CancellationToken>())).ReturnsAsync(OpenPeriod());
        var workbench = new ManualJournalEntryWorkbenchService(drafts, configurationService, audit,
            journalStore: ledger.Object, recurringStore: omitRegistry ? null : registry);
        var intake = new AutomatedJournalDraftIntakeService(workbench, drafts, configurationService);
        await new RecurringJournalRunner(registry, intake, drafts, new PeriodAuthority()).RunDueAsync(Now);
        var draft = (await drafts.ListAsync(Fund, Book)).Single();
        draft = draft with
        {
            Status = action switch
            {
                JournalEntryLifecycleActionDto.Approve => ManualJournalEntryStatusDto.Submitted,
                JournalEntryLifecycleActionDto.Post => ManualJournalEntryStatusDto.Approved,
                _ => ManualJournalEntryStatusDto.Draft
            }
        };
        await drafts.SaveAsync(draft);
        return new(registry, drafts, audit, workbench, ledger, draft);
    }

    private static async Task AssertUnchangedAsync(Fixture fixture)
    {
        (await fixture.Drafts.ListAsync(Fund, Book)).Should().ContainSingle().Which.Should().BeEquivalentTo(fixture.Draft);
        (await fixture.Audit.ListAsync(Fund, Book)).Should().ContainSingle().Which.Action.Should().Be("manual-je.save-draft");
        fixture.Ledger.Verify(value => value.AppendAsync(It.IsAny<LedgerJournalEntryWrite>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static JournalEntryLifecycleActionRequestDto Request(ManualJournalEntryDraftDto draft, JournalEntryLifecycleActionDto action)
        => new(draft.JournalEntryId, Fund, action, "independent-reviewer", draft.Version,
            Notes: "Reviewed retained fee terms", EvidenceLinks: [Source().Uri], LedgerBookId: Book, TenantId: Tenant, CompanyId: Company);
    private static SubmitManualJournalEntryApprovalRequest Submit(ManualJournalEntryDraftDto draft)
        => new(draft.JournalEntryId, Fund, "preparer", draft.Version, LedgerBookId: Book, TenantId: Tenant, CompanyId: Company);
    private static LedgerAccountingPeriod OpenPeriod()
        => new(Period, Book, 2026, 7, "July 2026", Date, Date.AddMonths(1).AddDays(-1), "Open", Now, null, 1);
    private static JournalTemplate Template(decimal factor = 1m)
        => new("fee-template", "Management fee", "Monthly retained fee terms",
            [new(LedgerAccounts.ManagementFeeExpenseFor(Fund), JournalTemplateSide.Debit, "amount", Factor: factor),
             new(LedgerAccounts.ManagementFeePayableFor(Fund), JournalTemplateSide.Credit, "amount", Factor: factor)]);
    private static RecurringJournalSchedule Schedule(decimal amount = 125m)
        => new(ScheduleId, "fee-template", new LedgerBookKey(Fund, "Fund"), RecurringJournalCadence.Monthly,
            Date, "controller", Now, new Dictionary<string, decimal> { ["amount"] = amount }, endsOn: Date);
    private static JournalEvidenceReference Source()
        => new("fee-terms-2026", "evidence://fee-terms-2026", "fee-terms", "document-vault", Now.AddDays(-1),
            "records-controller", ContentHash: new string('a', 64), EvidenceVersion: 3);
    private sealed class PeriodAuthority : IRecurringJournalPeriodAuthority
    {
        public Task<RecurringJournalPeriodState> ResolveAsync(RecurringJournalScope scope, DateOnly date, CancellationToken ct)
            => Task.FromResult(new RecurringJournalPeriodState(Period.ToString(), 1, true, null, null, null));
    }
    private sealed record Fixture(FileRecurringJournalStore Registry, FileManualJournalEntryDraftStore Drafts,
        FileAccountingConfigurationStore Audit, ManualJournalEntryWorkbenchService Workbench,
        Mock<ILedgerJournalStore> Ledger, ManualJournalEntryDraftDto Draft);
    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
