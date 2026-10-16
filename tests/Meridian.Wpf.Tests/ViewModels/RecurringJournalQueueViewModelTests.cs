using Meridian.Contracts.Workstation;
using Meridian.Wpf.ViewModels.Accounting;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed class RecurringJournalQueueViewModelTests
{
    private static readonly Guid BookId = Guid.NewGuid();
    private static RecurringJournalOccurrenceDto Occurrence => new(
        "occurrence-1", "rent", 3, "rent-template", 7, new DateOnly(2026, 10, 1),
        "fund-a", BookId, "entity-a", "2026-10", "Drafted", Guid.NewGuid(), "Draft",
        [], ["evidence://rent/2026-10"]);

    [Fact]
    public async Task Queue_ExposesRetainedVersionsDraftEvidenceAndHumanApproval()
    {
        var occurrence = Occurrence;
        var view = new RecurringJournalQueueViewModel(new Source((_, _, _, _) => Task.FromResult(
            new RecurringJournalQueueDto("fund-a", BookId, "entity-a", [occurrence]))));
        await view.LoadAsync("fund-a", BookId, "entity-a");
        var row = view.Rows.Should().ContainSingle().Which;
        row.Definition.Should().Contain("rent · v3").And.Contain("rent-template · v7");
        row.Draft.Should().Be(occurrence.JournalEntryId!.Value.ToString("D"));
        row.Evidence.Should().Be("evidence://rent/2026-10");
        row.State.Should().Contain("Approval: Draft");
        view.StatusText.Should().Contain("require human approval");
    }

    [Fact]
    public async Task Queue_ShowsBlockedOccurrenceAndGovernedLockRemediation()
    {
        var occurrence = Occurrence with
        {
            State = "Blocked",
            JournalEntryId = null,
            ApprovalStatus = null,
            Blockers = ["Period is locked", "Missing evidence"],
            PeriodLockOwner = "controller-a",
            GovernedReopenPath = "Accounting > Close > Request governed reopen"
        };
        var view = new RecurringJournalQueueViewModel(new Source((_, _, _, _) => Task.FromResult(
            new RecurringJournalQueueDto("fund-a", BookId, "entity-a", [occurrence]))));
        await view.LoadAsync("fund-a", BookId, "entity-a");
        var row = view.Rows.Single();
        row.Blockers.Should().Contain("Period is locked").And.Contain("Missing evidence");
        row.PeriodLock.Should().Contain("Owner: controller-a").And.Contain("Request governed reopen");
        row.Draft.Should().Be("No retained draft");
    }

    [Fact]
    public async Task Queue_FailedReadClearsPriorRowsAndRetryRecoversSameDraft()
    {
        var occurrence = Occurrence;
        var calls = 0;
        var view = new RecurringJournalQueueViewModel(new Source((_, _, _, _) => ++calls == 2
            ? Task.FromException<RecurringJournalQueueDto>(new IOException("Durable state unavailable"))
            : Task.FromResult(new RecurringJournalQueueDto("fund-a", BookId, "entity-a", [occurrence]))));
        await view.LoadAsync("fund-a", BookId, "entity-a");
        await view.LoadAsync("fund-a", BookId, "entity-a");
        view.Rows.Should().BeEmpty();
        view.StatusText.Should().Contain("unavailable");
        await view.LoadAsync("fund-a", BookId, "entity-a");
        view.Rows.Single().Occurrence.JournalEntryId.Should().Be(occurrence.JournalEntryId);
    }

    [Fact]
    public async Task Queue_IgnoresLateResponseForPreviousScope()
    {
        var pending = new TaskCompletionSource<RecurringJournalQueueDto>();
        var view = new RecurringJournalQueueViewModel(new Source((_, _, entity, _) => entity == "entity-a"
            ? pending.Task : Task.FromResult(new RecurringJournalQueueDto("fund-a", BookId, entity, []))));
        var first = view.LoadAsync("fund-a", BookId, "entity-a");
        await view.LoadAsync("fund-a", BookId, "entity-b");
        pending.SetResult(new RecurringJournalQueueDto("fund-a", BookId, "entity-a", [Occurrence]));
        await first;
        view.Rows.Should().BeEmpty();
        view.StatusText.Should().Be("No retained recurring occurrences for this scope.");
    }

    [Fact]
    public async Task Queue_RefusesMismatchedScope()
    {
        var view = new RecurringJournalQueueViewModel(new Source((_, _, _, _) => Task.FromResult(
            new RecurringJournalQueueDto("fund-a", BookId, "entity-b", [Occurrence]))));
        await view.LoadAsync("fund-a", BookId, "entity-a");
        view.Rows.Should().BeEmpty();
        view.StatusText.Should().Contain("does not match");
    }

    private sealed class Source(Func<string, Guid, string, CancellationToken, Task<RecurringJournalQueueDto>> read) : IRecurringJournalQueueSource
    {
        public Task<RecurringJournalQueueDto> GetQueueAsync(string fundProfileId, Guid ledgerBookId, string entityId,
            CancellationToken ct = default, string? tenantId = null, string? companyId = null) => read(fundProfileId, ledgerBookId, entityId, ct);
    }
}
