using FluentAssertions;
using Meridian.FinancialOperations.FundAdministration;
using Meridian.Ledger;
using Xunit;

namespace Meridian.Tests.FinancialOperations;

public sealed class RecurringJournalStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "meridian-recurring-tests", Guid.NewGuid().ToString("N"));
    private static readonly DateTimeOffset At = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Date = new(2026, 10, 1);
    private static readonly RecurringJournalScope Scope = new("fund-a", Guid.Parse("048804c2-31df-4878-8008-580386a12e80"), "entity-a", "USD", "tenant-a", "company-a", "account-a");

    private FileRecurringJournalStore Store() => new(_directory);

    private static JournalTemplate Template(decimal factor = 1m) => new("fee", "Management fee", "Approved contract fee",
    [
        new JournalTemplateLine(new LedgerAccount("Expenses:ManagementFees", LedgerAccountType.Expense, "FUND", "account-a"),
            JournalTemplateSide.Debit, "fee", Factor: factor,
            Dimensions: new LedgerLineDimensionSet(EntityId: "entity-a", ExternalGlDimensions: new Dictionary<string, string> { ["desk"] = "operations" })),
        new JournalTemplateLine(new LedgerAccount("Liabilities:FeesPayable", LedgerAccountType.Liability, FinancialAccountId: "account-a"),
            JournalTemplateSide.Credit, "fee", Factor: factor)
    ], ledgerBook: "Fund");

    private static RecurringJournalSchedule Schedule(decimal amount = 100m) => new("schedule-a", "fee", new LedgerBookKey("fund-a", "Fund"),
        RecurringJournalCadence.Monthly, Date, "controller", At,
        new Dictionary<string, decimal> { ["fee"] = amount }, description: "Monthly management fee");

    private static JournalEvidenceReference Evidence() => new("contract-1", "vault://contracts/1", "Contract", "DocumentVault", At,
        "controller", "fund-a", new string('a', 64), "Approved fee contract", EvidenceVersion: 3);

    private async Task SeedAsync(IReadOnlyList<JournalEvidenceReference>? evidence = null)
    {
        var store = Store();
        await store.InitializeAsync();
        await using var session = await store.OpenSessionAsync();
        await session.RegisterTemplateAsync(Template(), "controller", 0, At);
        await session.RegisterScheduleAsync(Schedule(), Scope, evidence ?? [Evidence()], "controller", 0, At);
    }

    [Fact]
    public async Task Restart_RetainsExactDefinitionsEvidenceAndDraftIdentity()
    {
        await SeedAsync();
        RecurringOccurrenceRecord original;
        await using (var session = await Store().OpenSessionAsync())
        {
            original = await session.ClaimAsync("schedule-a", Date, 1, 1, At);
            await session.SetOutcomeAsync(original.OccurrenceKey, RecurringOccurrenceState.Drafted, null, "2026-10", null, null, At);
        }

        await using var restarted = await Store().OpenSessionAsync();
        var replay = await restarted.ClaimAsync("SCHEDULE-A", Date, 1, 1, At.AddHours(2));
        replay.DraftId.Should().Be(original.DraftId);
        replay.ScheduleDefinition.Should().BeEquivalentTo(original.ScheduleDefinition);
        replay.TemplateDefinition.Should().BeEquivalentTo(original.TemplateDefinition);
        replay.ScheduleDefinition.Evidence.Should().ContainSingle().Which.Should().BeEquivalentTo(Evidence());
        replay.TemplateDefinition.Template.Lines[0].Account.Symbol.Should().Be("FUND");
        replay.TemplateDefinition.Template.Lines[0].Dimensions!.ExternalGlDimensions["desk"].Should().Be("operations");
        replay.State.Should().Be(RecurringOccurrenceState.Drafted);
        restarted.Occurrences.Should().ContainSingle();
        replay.History.Should().HaveCount(2);
    }

    [Fact]
    public async Task InterruptedAfterClaim_RetryUsesSameDraftAndDoesNotConsumeOccurrence()
    {
        await SeedAsync();
        Guid retained;
        await using (var first = await Store().OpenSessionAsync())
            retained = (await first.ClaimAsync("schedule-a", Date, 1, 1, At)).DraftId;
        await using var second = await Store().OpenSessionAsync();
        var recovered = await second.ClaimAsync("schedule-a", Date, 1, 1, At.AddDays(1));
        recovered.DraftId.Should().Be(retained);
        recovered.State.Should().Be(RecurringOccurrenceState.Claimed);
        recovered.History.Should().ContainSingle();
        await second.SetOutcomeAsync(recovered.OccurrenceKey, RecurringOccurrenceState.Drafted, null, "2026-10", null, null, At.AddDays(1));
        second.Occurrences.Should().ContainSingle();
    }

    [Fact]
    public async Task ConcurrentStoreInstances_SerializeClaimAndIntakeWithoutDuplicateEffect()
    {
        await SeedAsync();
        var effects = 0;
        var ids = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        {
            await using var session = await Store().OpenSessionAsync();
            var claim = await session.ClaimAsync("schedule-a", Date, 1, 1, At);
            if (claim.State != RecurringOccurrenceState.Drafted)
            {
                Interlocked.Increment(ref effects);
                await Task.Yield();
                await session.SetOutcomeAsync(claim.OccurrenceKey, RecurringOccurrenceState.Drafted, null, "2026-10", null, null, At);
            }
            return claim.DraftId;
        }));
        effects.Should().Be(1);
        ids.Distinct().Should().ContainSingle();
        await using var final = await Store().OpenSessionAsync();
        final.Occurrences.Should().ContainSingle();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ChangedDefinition_BlocksOriginalClaimUntilExplicitRestoration(bool changeSchedule)
    {
        await SeedAsync();
        Guid originalId;
        await using (var session = await Store().OpenSessionAsync())
        {
            originalId = (await session.ClaimAsync("schedule-a", Date, 1, 1, At)).DraftId;
            if (changeSchedule)
                await session.RegisterScheduleAsync(Schedule(200), Scope, [Evidence()], "controller", 1, At.AddMinutes(1));
            else
                await session.RegisterTemplateAsync(Template(2), "controller", 1, At.AddMinutes(1));
            Func<Task> retry = async () => await session.ClaimAsync("schedule-a", Date, changeSchedule ? 2 : 1, changeSchedule ? 1 : 2, At.AddMinutes(2));
            await retry.Should().ThrowAsync<RecurringJournalDefinitionChangedException>();
        }
        await using var restarted = await Store().OpenSessionAsync();
        var blocked = restarted.Occurrences.Should().ContainSingle().Which;
        blocked.State.Should().Be(RecurringOccurrenceState.DefinitionChanged);
        blocked.ScheduleDefinition.Version.Should().Be(1);
        blocked.TemplateDefinition.Version.Should().Be(1);
        blocked.DraftId.Should().Be(originalId);
        await restarted.ActivateDefinitionsAsync("schedule-a", 1, 1, "controller", "Restore approved definition for retained occurrence.", At.AddMinutes(3));
        var recovered = await restarted.ClaimAsync("schedule-a", Date, 1, 1, At.AddMinutes(4));
        recovered.DraftId.Should().Be(originalId);
        recovered.State.Should().Be(RecurringOccurrenceState.Claimed);
        recovered.ScheduleDefinition.Schedule.Parameters["fee"].Should().Be(100m);
        restarted.Activations.Last().Actor.Should().Be("controller");
        restarted.Activations.Last().Reason.Should().Contain("Restore approved");
        restarted.Schedules.Count.Should().Be(changeSchedule ? 2 : 1);
        restarted.Templates.Count.Should().Be(changeSchedule ? 1 : 2);
    }

    [Fact]
    public async Task MissingEvidence_IsRetainedForVisibleBlockedOccurrence()
    {
        await SeedAsync([]);
        await using var session = await Store().OpenSessionAsync();
        var claim = await session.ClaimAsync("schedule-a", Date, 1, 1, At);
        claim.ScheduleDefinition.Evidence.Should().BeEmpty();
        var reason = RecurringJournalEvidenceGuard.ValidateSources(claim.ScheduleDefinition.Evidence);
        reason.Should().NotBeNull();
        await session.SetOutcomeAsync(claim.OccurrenceKey, RecurringOccurrenceState.Blocked, reason, "2026-10", null, null, At);
        session.Occurrences.Single().State.Should().Be(RecurringOccurrenceState.Blocked);
    }

    [Fact]
    public async Task LockedPeriodOutcome_SurvivesRestartWithOwnerAndGovernedReopenPath()
    {
        await SeedAsync();
        await using (var session = await Store().OpenSessionAsync())
        {
            var claim = await session.ClaimAsync("schedule-a", Date, 1, 1, At);
            await session.SetOutcomeAsync(claim.OccurrenceKey, RecurringOccurrenceState.Blocked,
                "Accounting period is locked.", "2026-10", "controller-a", "/accounting/close/2026-10/reopen", At);
        }
        await using var restarted = await Store().OpenSessionAsync();
        var blocked = restarted.Occurrences.Single();
        blocked.LockOwner.Should().Be("controller-a");
        blocked.ReopenPath.Should().Be("/accounting/close/2026-10/reopen");
        blocked.History.Last().PeriodId.Should().Be("2026-10");
    }

    [Fact]
    public async Task UnprovisionedState_FailsClosedWithoutCreatingAnEmptyStore()
    {
        Func<Task> read = async () => await Store().OpenSessionAsync();
        await read.Should().ThrowAsync<InvalidOperationException>();
        Directory.Exists(_directory).Should().BeFalse();
    }

    [Fact]
    public async Task DeletedInitializedState_CannotBeReinitializedAsEmpty()
    {
        await SeedAsync();
        File.Delete(Path.Combine(_directory, "recurring-journals.json"));
        Func<Task> read = async () => await Store().OpenSessionAsync();
        Func<Task> initialize = () => Store().InitializeAsync();
        await read.Should().ThrowAsync<InvalidOperationException>();
        await initialize.Should().ThrowAsync<InvalidOperationException>();
        File.Exists(Path.Combine(_directory, "recurring-journals.json")).Should().BeFalse();
    }

    [Fact]
    public async Task CorruptState_FailsClosedAndPreservesOriginalBytes()
    {
        await SeedAsync();
        var path = Path.Combine(_directory, "recurring-journals.json");
        await File.WriteAllTextAsync(path, "{bad-data");
        Func<Task> read = async () => await Store().OpenSessionAsync();
        Func<Task> initialize = () => Store().InitializeAsync();
        await read.Should().ThrowAsync<InvalidOperationException>();
        await initialize.Should().ThrowAsync<InvalidOperationException>();
        (await File.ReadAllTextAsync(path)).Should().Be("{bad-data");
    }

    [Fact]
    public async Task ScheduleOwnershipAndStaleRegistration_CannotOverwriteRetainedDefinition()
    {
        await SeedAsync();
        await using var session = await Store().OpenSessionAsync();
        Func<Task> takeover = async () => await session.RegisterScheduleAsync(Schedule(), Scope with { TenantId = "tenant-b" }, [Evidence()], "other", 1, At);
        Func<Task> stale = async () => await session.RegisterTemplateAsync(Template(2), "controller", 0, At);
        await takeover.Should().ThrowAsync<InvalidOperationException>();
        await stale.Should().ThrowAsync<RecurringJournalDefinitionChangedException>();
        session.CurrentSchedules.Should().ContainSingle().Which.Scope.TenantId.Should().Be("tenant-a");
        session.Templates.Should().ContainSingle();
    }

    [Fact]
    public async Task ScopedTemplate_CannotBeTakenOverByAnotherTenantOrSchedule()
    {
        await Store().InitializeAsync();
        await using var session = await Store().OpenSessionAsync();
        await session.RegisterTemplateAsync(Template(), "controller", 0, At, scope: Scope);
        var foreign = Scope with { TenantId = "tenant-b" };
        Func<Task> overwrite = async () => await session.RegisterTemplateAsync(Template(2), "other", 1, At, scope: foreign);
        Func<Task> borrow = async () => await session.RegisterScheduleAsync(Schedule(), foreign, [Evidence()], "other", 0, At);
        await overwrite.Should().ThrowAsync<InvalidOperationException>();
        await borrow.Should().ThrowAsync<InvalidOperationException>();
        session.Templates.Should().ContainSingle();
        session.Schedules.Should().BeEmpty();
    }

    [Fact]
    public async Task RestorationPreservesMonotonicVersionsAndUsesActiveVersionForConcurrency()
    {
        await Store().InitializeAsync();
        await using var session = await Store().OpenSessionAsync();
        await session.ConfigureAsync(Schedule(), Template(), Scope, [Evidence()], "controller", 0, 0, At);
        await session.ConfigureAsync(Schedule(200), Template(2), Scope, [Evidence()], "controller", 1, 1, At);
        await session.ActivateDefinitionsAsync("schedule-a", 1, 1, "controller", "Restore original", At);
        var next = await session.ConfigureAsync(Schedule(300), Template(3), Scope, [Evidence()], "controller", 1, 1, At);
        next.Version.Should().Be(3);
        session.GetCurrentTemplate("fee").Version.Should().Be(3);
        session.Schedules.Select(item => item.Version).Should().Equal(1, 2, 3);
    }

    [Theory]
    [InlineData(LedgerViewKind.Historical, null)]
    [InlineData(LedgerViewKind.Actual, "simulation-1")]
    public async Task SimulationSchedules_CannotProduceActualApprovalClaims(LedgerViewKind view, string? scenario)
    {
        await SeedAsync();
        await using var session = await Store().OpenSessionAsync();
        var simulated = new RecurringJournalSchedule("simulation", "fee", new LedgerBookKey("fund-a", "Fund", view, scenario),
            RecurringJournalCadence.Monthly, Date, "controller", At, new Dictionary<string, decimal> { ["fee"] = 100m });
        Func<Task> save = async () => await session.RegisterScheduleAsync(simulated, Scope, [Evidence()], "controller", 0, At);
        await save.Should().ThrowAsync<InvalidOperationException>();
        session.Schedules.Should().ContainSingle();
    }

    [Fact]
    public async Task Configure_ValidatesBothDefinitionsBeforeEitherBecomesActive()
    {
        var store = Store();
        await store.InitializeAsync();
        await using (var session = await store.OpenSessionAsync())
        {
            await session.ConfigureAsync(Schedule(), Template(), Scope, [Evidence()], "controller", 0, 0, At);
            Func<Task> stale = async () => await session.ConfigureAsync(Schedule(200), Template(2), Scope, [Evidence()], "controller", 0, 1, At);
            await stale.Should().ThrowAsync<RecurringJournalDefinitionChangedException>();
            session.Templates.Should().ContainSingle();
            session.Schedules.Should().ContainSingle();
            session.GetCurrentTemplate("fee").Template.Lines[0].Factor.Should().Be(1m);
            var unbalanced = new JournalTemplate("fee", "Invalid", "", [new JournalTemplateLine(new LedgerAccount("Expenses:Fees", LedgerAccountType.Expense), JournalTemplateSide.Debit, FixedAmount: 100m)]);
            Func<Task> invalid = async () => await session.ConfigureAsync(Schedule(200), unbalanced, Scope, [Evidence()], "controller", 1, 1, At);
            await invalid.Should().ThrowAsync<LedgerValidationException>();
            session.Templates.Should().ContainSingle();
        }
        await using var restarted = await store.OpenSessionAsync();
        restarted.GetCurrentSchedule("schedule-a").Version.Should().Be(1);
        restarted.GetCurrentTemplate("fee").Version.Should().Be(1);
    }

    [Fact]
    public async Task ChangedAnchor_MarksOldOccurrenceBlockedEvenWhenNoLongerDue()
    {
        await SeedAsync();
        await using var session = await Store().OpenSessionAsync();
        var original = await session.ClaimAsync("schedule-a", Date, 1, 1, At);
        var changed = new RecurringJournalSchedule("schedule-a", "fee", new LedgerBookKey("fund-a", "Fund"),
            RecurringJournalCadence.Monthly, Date.AddMonths(1), "controller", At,
            new Dictionary<string, decimal> { ["fee"] = 100m });
        await session.RegisterScheduleAsync(changed, Scope, [Evidence()], "controller", 1, At);
        session.Occurrences.Single().State.Should().Be(RecurringOccurrenceState.DefinitionChanged);
        session.Occurrences.Single().DraftId.Should().Be(original.DraftId);
    }

    [Fact]
    public async Task OffCadenceDateAndStalePlan_DoNotCreateClaims()
    {
        await SeedAsync();
        await using var session = await Store().OpenSessionAsync();
        Func<Task> offCadence = async () => await session.ClaimAsync("schedule-a", Date.AddDays(1), 1, 1, At);
        Func<Task> stalePlan = async () => await session.ClaimAsync("schedule-a", Date, 2, 1, At);
        await offCadence.Should().ThrowAsync<InvalidOperationException>();
        await stalePlan.Should().ThrowAsync<RecurringJournalDefinitionChangedException>();
        session.Occurrences.Should().BeEmpty();
    }

    [Fact]
    public async Task CallerMutations_CannotChangeRetainedTemplateOrEvidence()
    {
        await SeedAsync();
        await using (var session = await Store().OpenSessionAsync())
        {
            var exposed = session.GetCurrentTemplate("fee");
            ((IList<JournalTemplateLine>)exposed.Template.Lines)[0] = exposed.Template.Lines[0] with { Factor = 999m };
            session.GetCurrentTemplate("fee").Template.Lines[0].Factor.Should().Be(1m);
        }
        await using var restarted = await Store().OpenSessionAsync();
        restarted.GetCurrentTemplate("fee").Template.Lines[0].Factor.Should().Be(1m);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }
}
