using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.Workstation;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// The durable, Postgres-backed revision-lifecycle store is the authority a publish consults to
/// confirm a revision was actually approved through the governed gate. It must enforce the same
/// compare-and-set transitions as the in-memory store and — the reason it is durable — keep that
/// lifecycle state across process instances so publish authority survives restarts.
/// </summary>
[Trait("Category", "Integration")]
[Collection(nameof(SecurityMasterDatabaseCollection))]
public sealed class PostgresSecurityMasterRevisionStoreTests
{
    private readonly SecurityMasterDatabaseFixture _fixture;

    public PostgresSecurityMasterRevisionStoreTests(SecurityMasterDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    private PostgresSecurityMasterRevisionStore NewStore() => new(_fixture.Options);

    [SecurityMasterDatabaseFact]
    public async Task CreateDraft_IssuesDistinctDraftRevisions()
    {
        var store = NewStore();
        var securityId = Guid.NewGuid();

        var first = await store.CreateDraftAsync(securityId, "ops.analyst");
        var second = await store.CreateDraftAsync(securityId, "ops.analyst");

        first.State.Should().Be(SecurityMasterRevisionStateDto.Draft);
        first.SecurityId.Should().Be(securityId);
        first.RevisionId.Should().NotBe(second.RevisionId);
    }

    [SecurityMasterDatabaseFact]
    public async Task Transition_AdvancesLifecycleAndPersistsAcrossInstances()
    {
        var securityId = Guid.NewGuid();
        var draft = await NewStore().CreateDraftAsync(securityId, "ops.analyst");

        await NewStore().TransitionAsync(draft.RevisionId, SecurityMasterRevisionStateDto.Draft, SecurityMasterRevisionStateDto.Submitted, "ops.analyst");
        await NewStore().TransitionAsync(draft.RevisionId, SecurityMasterRevisionStateDto.Submitted, SecurityMasterRevisionStateDto.Approved, "ops.reviewer");
        var published = await NewStore().TransitionAsync(draft.RevisionId, SecurityMasterRevisionStateDto.Approved, SecurityMasterRevisionStateDto.Published, "ops.analyst");

        published.State.Should().Be(SecurityMasterRevisionStateDto.Published);

        // A fresh store instance observes the durable, published lifecycle state.
        (await NewStore().GetAsync(draft.RevisionId))!.State.Should().Be(SecurityMasterRevisionStateDto.Published);
    }

    [SecurityMasterDatabaseFact]
    public async Task Transition_FromWrongExpectedState_ThrowsAndLeavesStateUnchanged()
    {
        var draft = await NewStore().CreateDraftAsync(Guid.NewGuid(), "ops.analyst");

        await NewStore().Invoking(s => s.TransitionAsync(
                draft.RevisionId, SecurityMasterRevisionStateDto.Submitted, SecurityMasterRevisionStateDto.Approved, "ops.reviewer"))
            .Should().ThrowAsync<SecurityMasterRevisionStateException>();

        (await NewStore().GetAsync(draft.RevisionId))!.State.Should().Be(SecurityMasterRevisionStateDto.Draft);
    }

    [SecurityMasterDatabaseFact]
    public async Task Transition_UnknownRevision_Throws()
    {
        await NewStore().Invoking(s => s.TransitionAsync(
                Guid.NewGuid(), SecurityMasterRevisionStateDto.Approved, SecurityMasterRevisionStateDto.Published, "ops.analyst"))
            .Should().ThrowAsync<SecurityMasterRevisionStateException>();
    }

    [SecurityMasterDatabaseFact]
    public async Task Transition_SecondConcurrentCall_LosesCompareAndSet()
    {
        var draft = await NewStore().CreateDraftAsync(Guid.NewGuid(), "ops.analyst");

        await NewStore().TransitionAsync(draft.RevisionId, SecurityMasterRevisionStateDto.Draft, SecurityMasterRevisionStateDto.Submitted, "ops.analyst");

        await NewStore().Invoking(s => s.TransitionAsync(
                draft.RevisionId, SecurityMasterRevisionStateDto.Draft, SecurityMasterRevisionStateDto.Submitted, "ops.analyst"))
            .Should().ThrowAsync<SecurityMasterRevisionStateException>();
    }

    [SecurityMasterDatabaseFact]
    public async Task Get_UnknownRevision_ReturnsNull()
        => (await NewStore().GetAsync(Guid.NewGuid())).Should().BeNull();

    [SecurityMasterDatabaseFact]
    public async Task Transition_DraftToSubmitted_BindsWorkflowId()
    {
        var draft = await NewStore().CreateDraftAsync(Guid.NewGuid(), "ops.analyst");
        var workflowId = Guid.NewGuid();

        await NewStore().TransitionAsync(
            draft.RevisionId, SecurityMasterRevisionStateDto.Draft, SecurityMasterRevisionStateDto.Submitted,
            "ops.analyst", workflowIdForSubmit: workflowId);

        (await NewStore().GetAsync(draft.RevisionId))!.WorkflowId.Should().Be(workflowId);
    }

    [SecurityMasterDatabaseFact]
    public async Task Transition_NonSubmitTransition_DoesNotRebindWorkflow()
    {
        var draft = await NewStore().CreateDraftAsync(Guid.NewGuid(), "ops.analyst");
        var submitWorkflowId = Guid.NewGuid();
        await NewStore().TransitionAsync(
            draft.RevisionId, SecurityMasterRevisionStateDto.Draft, SecurityMasterRevisionStateDto.Submitted,
            "ops.analyst", workflowIdForSubmit: submitWorkflowId);

        await NewStore().TransitionAsync(
            draft.RevisionId, SecurityMasterRevisionStateDto.Submitted, SecurityMasterRevisionStateDto.Approved,
            "ops.reviewer", workflowIdForSubmit: Guid.NewGuid());

        (await NewStore().GetAsync(draft.RevisionId))!.WorkflowId.Should().Be(submitWorkflowId);
    }

    [SecurityMasterDatabaseFact]
    public async Task CreateDraft_WithFieldMetadata_PersistsFieldEdit()
    {
        var effectiveFrom = new DateTimeOffset(2026, 03, 31, 0, 0, 0, TimeSpan.Zero);

        var draft = await NewStore().CreateDraftAsync(
            Guid.NewGuid(), "ops.analyst", "EconomicDefinition.Coupon", effectiveFrom, "Corrected coupon.", "fund-1");

        var reloaded = await NewStore().GetAsync(draft.RevisionId);
        reloaded!.FieldPath.Should().Be("EconomicDefinition.Coupon");
        reloaded.FieldEffectiveFrom.Should().Be(effectiveFrom);
        reloaded.FieldJustification.Should().Be("Corrected coupon.");
        reloaded.FundProfileId.Should().Be("fund-1");
        reloaded.WorkflowId.Should().BeNull();
    }
}
