using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.Workstation;

namespace Meridian.Tests.SecurityMaster.Workbench;

/// <summary>
/// The durable revision lifecycle is the authority a publish consults to confirm a revision was
/// actually approved through the governed gate. Transitions are compare-and-set so out-of-order or
/// concurrent lifecycle calls are rejected rather than silently advancing the revision.
/// </summary>
public sealed class InMemorySecurityMasterRevisionStoreTests
{
    private static readonly Guid SecurityId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task CreateDraft_IssuesDistinctDraftRevisions()
    {
        var store = new InMemorySecurityMasterRevisionStore();

        var first = await store.CreateDraftAsync(SecurityId, "ops.analyst");
        var second = await store.CreateDraftAsync(SecurityId, "ops.analyst");

        first.State.Should().Be(SecurityMasterRevisionStateDto.Draft);
        first.SecurityId.Should().Be(SecurityId);
        first.RevisionId.Should().NotBe(second.RevisionId);
    }

    [Fact]
    public async Task Transition_AdvancesThroughTheGovernedLifecycle()
    {
        var store = new InMemorySecurityMasterRevisionStore();
        var draft = await store.CreateDraftAsync(SecurityId, "ops.analyst");

        await store.TransitionAsync(draft.RevisionId, SecurityMasterRevisionStateDto.Draft, SecurityMasterRevisionStateDto.Submitted, "ops.analyst");
        await store.TransitionAsync(draft.RevisionId, SecurityMasterRevisionStateDto.Submitted, SecurityMasterRevisionStateDto.Approved, "ops.reviewer");
        var published = await store.TransitionAsync(draft.RevisionId, SecurityMasterRevisionStateDto.Approved, SecurityMasterRevisionStateDto.Published, "ops.analyst");

        published.State.Should().Be(SecurityMasterRevisionStateDto.Published);
        (await store.GetAsync(draft.RevisionId))!.State.Should().Be(SecurityMasterRevisionStateDto.Published);
    }

    [Fact]
    public async Task Transition_FromWrongExpectedState_Throws()
    {
        var store = new InMemorySecurityMasterRevisionStore();
        var draft = await store.CreateDraftAsync(SecurityId, "ops.analyst");

        // Attempting to approve a Draft (skipping Submitted) is rejected.
        await store.Invoking(s => s.TransitionAsync(
                draft.RevisionId, SecurityMasterRevisionStateDto.Submitted, SecurityMasterRevisionStateDto.Approved, "ops.reviewer"))
            .Should().ThrowAsync<SecurityMasterRevisionStateException>();

        (await store.GetAsync(draft.RevisionId))!.State.Should().Be(SecurityMasterRevisionStateDto.Draft);
    }

    [Fact]
    public async Task Transition_UnknownRevision_Throws()
    {
        var store = new InMemorySecurityMasterRevisionStore();

        await store.Invoking(s => s.TransitionAsync(
                Guid.NewGuid(), SecurityMasterRevisionStateDto.Approved, SecurityMasterRevisionStateDto.Published, "ops.analyst"))
            .Should().ThrowAsync<SecurityMasterRevisionStateException>();
    }

    [Fact]
    public async Task Transition_SecondConcurrentCall_LosesCompareAndSet()
    {
        var store = new InMemorySecurityMasterRevisionStore();
        var draft = await store.CreateDraftAsync(SecurityId, "ops.analyst");

        // First Draft→Submitted wins; a second Draft→Submitted on the now-Submitted revision is rejected.
        await store.TransitionAsync(draft.RevisionId, SecurityMasterRevisionStateDto.Draft, SecurityMasterRevisionStateDto.Submitted, "ops.analyst");

        await store.Invoking(s => s.TransitionAsync(
                draft.RevisionId, SecurityMasterRevisionStateDto.Draft, SecurityMasterRevisionStateDto.Submitted, "ops.analyst"))
            .Should().ThrowAsync<SecurityMasterRevisionStateException>();
    }

    [Fact]
    public async Task Get_UnknownRevision_ReturnsNull()
    {
        var store = new InMemorySecurityMasterRevisionStore();

        (await store.GetAsync(Guid.NewGuid())).Should().BeNull();
    }
}
