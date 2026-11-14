using Meridian.Contracts.Workstation;
using Meridian.Storage.Workstation;
using Meridian.Ui.Shared.Services;

namespace Meridian.Tests.Ui;

public sealed class OperatorTriagePolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AcknowledgementIsPersonalAndCannotResolveSourceWork()
    {
        var state = Apply(OperatorTriageState.Empty, "alice", new("ack", OperatorTriageAction.Acknowledge, 0, "Reviewed evidence"));
        Assert.Equal(1, state.Version);
        Assert.Equal(Now, Assert.Single(state.Personal).AcknowledgedAt);
        Assert.DoesNotContain(state.Personal, item => item.ActorId == "bob");
        Assert.Null(state.AssigneeId);
        Assert.Equal(OperatorTriageAction.Acknowledge, Assert.Single(state.Activity).Action);
    }

    [Fact]
    public void BlockedWorkRefusesSnoozeAndExistingSnoozeCannotHideEscalation()
    {
        var command = new OperatorTriageCommand("snooze", OperatorTriageAction.Snooze, 0, "Awaiting update", SnoozedUntil: Now.AddHours(1));
        Assert.Throws<OperatorTriageException>(() => OperatorTriagePolicy.Apply(OperatorTriageState.Empty, "alice", command,
            OperatorWorkItemToneDto.Critical, Now, false, out _));
        var state = Apply(OperatorTriageState.Empty, "alice", command);
        var personal = Assert.Single(state.Personal);
        Assert.True(OperatorTriagePolicy.IsSnoozed(personal, OperatorWorkItemToneDto.Warning, Now));
        Assert.False(OperatorTriagePolicy.IsSnoozed(personal, OperatorWorkItemToneDto.Critical, Now));
        Assert.False(OperatorTriagePolicy.IsSnoozed(personal, OperatorWorkItemToneDto.Warning, Now.AddHours(2)));
        var escalated = OperatorTriagePolicy.ObserveSeverity(state, OperatorWorkItemToneDto.Critical, Now.AddMinutes(1));
        Assert.Null(Assert.Single(escalated.Personal).SnoozedUntil);
        Assert.False(OperatorTriagePolicy.IsSnoozed(Assert.Single(escalated.Personal), OperatorWorkItemToneDto.Warning, Now.AddMinutes(2)));
        Assert.Equal("system:severity-escalation", escalated.Activity.Last().ActorId);
    }

    [Fact]
    public void ReplayIsIdempotentAndChangedInputsOrStaleVersionRefuse()
    {
        var command = new OperatorTriageCommand("assign", OperatorTriageAction.Assign, 0, "Coverage handoff", "bob");
        var state = Apply(OperatorTriageState.Empty, "alice", command);
        var replay = OperatorTriagePolicy.Apply(state, "alice", command, OperatorWorkItemToneDto.Warning, Now, false, out var replayed);
        Assert.True(replayed);
        Assert.Same(state, replay);
        Assert.Single(replay.Activity);
        Assert.Throws<OperatorTriageException>(() => Apply(state, "alice", command with { AssigneeId = "carol" }));
        Assert.Throws<OperatorTriageException>(() => Apply(state, "alice", command with { CommandId = "other" }));
    }

    [Fact]
    public void SourceOwnedAssignmentRetainsOnlyTheTriageReceipt()
    {
        var state = OperatorTriagePolicy.Apply(OperatorTriageState.Empty, "alice",
            new("assign", OperatorTriageAction.Assign, 0, "Coverage handoff", "bob"),
            OperatorWorkItemToneDto.Warning, Now, true, out _);
        Assert.Null(state.AssigneeId);
        Assert.Single(state.Receipts);
        Assert.Single(state.Activity);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(8)]
    public void SnoozeRequiresBoundedFutureExpiry(int days)
        => Assert.Throws<OperatorTriageException>(() => Apply(OperatorTriageState.Empty, "alice",
            new("snooze", OperatorTriageAction.Snooze, 0, "Waiting", SnoozedUntil: Now.AddDays(days))));

    [Fact]
    public async Task FileStateSurvivesRestartAndSeparatesTenantCompanyAndActor()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-triage-" + Guid.NewGuid().ToString("N"));
        try
        {
            var scope = new OperatorTriageScope("tenant-a", "company-a");
            var store = new FileOperatorTriageStore(root);
            await store.UpdateAsync(scope, "item-1", (current, _) => Task.FromResult(Apply(current, "alice",
                new("ack", OperatorTriageAction.Acknowledge, 0, "Reviewed"))));
            var restarted = new FileOperatorTriageStore(root);
            Assert.Equal(Now, Assert.Single((await restarted.GetAsync(scope, "item-1")).Personal).AcknowledgedAt);
            Assert.Equal(0, (await restarted.GetAsync(new("tenant-b", "company-a"), "item-1")).Version);
            Assert.Equal(0, (await restarted.GetAsync(new("tenant-a", "company-b"), "item-1")).Version);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task IndependentWritersCannotBothAdvanceTheSameVersion()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-triage-" + Guid.NewGuid().ToString("N"));
        try
        {
            var scope = new OperatorTriageScope("tenant", "company");
            var stores = new[] { new FileOperatorTriageStore(root), new FileOperatorTriageStore(root) };
            var outcomes = await Task.WhenAll(stores.Select(async (store, index) =>
            {
                try
                {
                    await store.UpdateAsync(scope, "item", (current, _) => Task.FromResult(Apply(current, "alice",
                        new($"ack-{index}", OperatorTriageAction.Acknowledge, 0, "Reviewed"))));
                    return true;
                }
                catch (OperatorTriageException) { return false; }
            }));
            Assert.Single(outcomes, success => success);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static OperatorTriageState Apply(OperatorTriageState state, string actor, OperatorTriageCommand command)
        => OperatorTriagePolicy.Apply(state, actor, command, OperatorWorkItemToneDto.Warning, Now, false, out _);
}
