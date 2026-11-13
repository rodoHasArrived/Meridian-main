using System.Text.Json;
using Meridian.Application.FundStructure;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Services;
using Meridian.Storage.FundStructure;
using Xunit;

namespace Meridian.FundStructure.Tests;

public sealed partial class IntercompanyAgreementServiceTests
{
    private static readonly DateOnly January = new(2026, 1, 1);

    [Fact]
    public void EffectiveChronologyPreservesApprovedAprilBoundaryAfterLaterFebruaryApproval()
    {
        var f = new Fixture();
        var revisions = new[] { Revision(f, 1, January), Revision(f, 2, new(2026, 4, 1)), Revision(f, 3, new(2026, 2, 1)) };
        Assert.Equal(3, IntercompanyAgreementTimeline.Resolve(revisions, new(2026, 3, 31)).Revision!.AgreementVersion);
        Assert.Equal(2, IntercompanyAgreementTimeline.Resolve(revisions, new(2026, 4, 1)).Revision!.AgreementVersion);
        Assert.Equal(2, IntercompanyAgreementTimeline.SemanticBase(revisions, f.Draft with { Action = IntercompanyAgreementActionDto.Amend, EffectiveFrom = new(2026, 5, 1) }));
    }

    [Fact]
    public void ExpiredSelectedRevisionDoesNotFallBackAndTerminationMasksFutureRevision()
    {
        var f = new Fixture();
        var first = Revision(f, 1, January);
        var second = Revision(f, 2, new(2026, 4, 1)) with { Terms = f.Draft with { EffectiveFrom = new(2026, 4, 1), EffectiveTo = new(2026, 5, 1) } };
        Assert.Equal(IntercompanyAgreementStatusDto.Expired, IntercompanyAgreementTimeline.Resolve([first, second], new(2026, 5, 1)).Status);
        var terminate = Revision(f, 3, January) with
        {
            Action = IntercompanyAgreementActionDto.Terminate,
            Terms = f.Draft with { Action = IntercompanyAgreementActionDto.Terminate, TerminationEffectiveDate = new(2026, 3, 1) }
        };
        Assert.Equal(IntercompanyAgreementStatusDto.Terminated, IntercompanyAgreementTimeline.Resolve([first, second, terminate], new(2026, 4, 1)).Status);
    }

    [Fact]
    public void HistoricalKnowledgeExcludesLaterRetrospectiveApproval()
    {
        var f = new Fixture();
        var first = Revision(f, 1, January) with { ApprovedAtUtc = new(2026, 1, 2, 0, 0, 0, TimeSpan.Zero) };
        var correction = Revision(f, 2, January) with { ApprovedAtUtc = new(2026, 3, 1, 0, 0, 0, TimeSpan.Zero) };
        Assert.Equal(1, IntercompanyAgreementTimeline.Resolve([first, correction], new(2026, 2, 1), new(2026, 2, 2, 0, 0, 0, TimeSpan.Zero)).Revision!.AgreementVersion);
        Assert.Equal(2, IntercompanyAgreementTimeline.Resolve([first, correction], new(2026, 2, 1)).Revision!.AgreementVersion);
    }

    [Fact]
    public async Task IncompleteDraftIsRetainedButCannotSubmit()
    {
        var f = new Fixture();
        var command = f.Command(f.Draft with { Terms = null });
        var created = await f.Service.CreateAsync(f.Author, command);
        await Assert.ThrowsAsync<IntercompanyAgreementValidationException>(() => f.Service.SubmitAsync(f.Author, f.Command(proposalId: command.ProposalId)));
        Assert.Equal(1, f.Store.State!.AggregateVersion);
        Assert.Single(f.Store.State.History);
        Assert.True(created.Detail.Capabilities.CanEdit);
    }

    [Fact]
    public async Task EveryAuthorAndSubmitterIsExcludedFromApproval()
    {
        var f = new Fixture();
        var command = f.Command(f.Draft);
        await f.Service.CreateAsync(f.Author, command);
        var editor = f.Author with { Actor = "editor" };
        await f.Service.SaveDraftAsync(editor, f.Command(f.Draft with { Name = "Reviewed loan" }, command.ProposalId));
        var submitted = await f.Service.SubmitAsync(f.Author, f.Command(proposalId: command.ProposalId));
        await Assert.ThrowsAsync<IntercompanyAgreementForbiddenException>(() => f.Service.ApproveAsync(editor,
            f.Command(proposalId: command.ProposalId) with { SubmittedHash = submitted.Detail.Proposals[^1].SubmittedHash }));
        Assert.Equal(3, f.Store.State!.AggregateVersion);
        Assert.Empty(f.Store.State.Revisions);
    }

    [Fact]
    public async Task ChangedEvidenceBlocksApprovalWithoutChangingState()
    {
        var f = new Fixture();
        var command = f.Command(f.Draft);
        await f.Service.CreateAsync(f.Author, command);
        var submitted = await f.Service.SubmitAsync(f.Author, f.Command(proposalId: command.ProposalId));
        f.Evidence.Hash = "changed";
        await Assert.ThrowsAsync<IntercompanyAgreementConflictException>(() => f.Service.ApproveAsync(f.Reviewer,
            f.Command(proposalId: command.ProposalId) with { SubmittedHash = submitted.Detail.Proposals[^1].SubmittedHash }));
        Assert.Equal(2, f.Store.State!.AggregateVersion);
        Assert.Empty(f.Store.State.Revisions);
    }

    [Fact]
    public async Task ExactApprovalRetryAfterAmendmentReturnsOriginalReceipt()
    {
        var f = new Fixture();
        var approval = await f.ApproveAsync(f.Draft);
        await f.ApproveAsync(f.Draft with { Action = IntercompanyAgreementActionDto.Amend, EffectiveFrom = new(2026, 4, 1), Terms = new(FixedAnnualRate: 0.07m) });
        var replay = await f.Service.ApproveAsync(f.Reviewer, approval);
        Assert.True(replay.Replayed);
        Assert.Equal(3, replay.Detail.AggregateVersion);
        Assert.Equal(6, f.Store.State!.AggregateVersion);
        await Assert.ThrowsAsync<IntercompanyAgreementConflictException>(() => f.Service.ApproveAsync(f.Reviewer, approval with { Rationale = "different" }));
    }

    [Fact]
    public async Task ConcurrentCommandsAgainstSameVersionHaveOneWinner()
    {
        var f = new Fixture();
        var create = f.Command(f.Draft);
        await f.Service.CreateAsync(f.Author, create);
        var left = f.Command(f.Draft with { Name = "left" }, create.ProposalId);
        var right = f.Command(f.Draft with { Name = "right" }, create.ProposalId);
        var outcomes = await Task.WhenAll(Run(left), Run(right));
        Assert.Equal(1, outcomes.Count(success => success));
        Assert.Equal(2, f.Store.State!.AggregateVersion);
        async Task<bool> Run(IntercompanyAgreementCommandDto command)
        {
            try
            { await f.Service.SaveDraftAsync(f.Author, command); return true; }
            catch (IntercompanyAgreementConflictException) { return false; }
        }
    }

    [Fact]
    public async Task TerminationCanCloseOpenEndedAgreementAfterPartyIntervalIsShortened()
    {
        var f = new Fixture();
        await f.ApproveAsync(f.Draft);
        var cutoff = new DateOnly(2026, 10, 1);
        foreach (var key in f.Store.Entities.Keys.ToArray())
            f.Store.Entities[key] = f.Store.Entities[key] with { EffectiveTo = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero), LifecycleStatus = LegalEntityLifecycleStatusDto.Dissolved };
        await f.ApproveAsync(f.Draft with { Action = IntercompanyAgreementActionDto.Terminate, TerminationEffectiveDate = cutoff });
        var detail = await f.Service.GetAsync(f.Author, f.AgreementId, cutoff);
        Assert.Equal(IntercompanyAgreementStatusDto.Terminated, detail!.EffectiveStatus);
        Assert.Null(f.Store.State!.Revisions[0].Terms.EffectiveTo);
        Assert.False(detail.Capabilities.CanTerminate);
    }

    [Fact]
    public async Task OnePartyAccessDoesNotDiscloseListDetailOrReplay()
    {
        var f = new Fixture();
        var create = f.Command(f.Draft);
        await f.Service.CreateAsync(f.Author, create);
        f.Access.Allowed = false;
        Assert.Null(await f.Service.GetAsync(f.Author, f.AgreementId));
        Assert.Empty((await f.Service.ListAsync(f.Author, new(EntityId: f.Draft.PartyOneId))).Items);
        await Assert.ThrowsAsync<IntercompanyAgreementNotFoundException>(() => f.Service.CreateAsync(f.Author, create));
    }

    [Fact]
    public async Task RejectedActivationCanBeReplacedWithoutErasingHistory()
    {
        var f = new Fixture();
        var create = f.Command(f.Draft);
        await f.Service.CreateAsync(f.Author, create);
        await f.Service.SubmitAsync(f.Author, f.Command(proposalId: create.ProposalId));
        await f.Service.RejectAsync(f.Reviewer, f.Command(proposalId: create.ProposalId));
        await f.Service.CreateProposalAsync(f.Author, f.Command(f.Draft));
        Assert.Equal(2, f.Store.State!.Proposals.Count);
        Assert.Equal(IntercompanyAgreementProposalStateDto.Rejected, f.Store.State.Proposals[0].State);
        Assert.Equal(4, f.Store.State.History.Count);
    }

    [Fact]
    public async Task DraftPartyChangesDoNotDiscloseHistoricalPartiesToCurrentPairOnlyReader()
    {
        var f = new Fixture();
        var create = f.Command(f.Draft);
        await f.Service.CreateAsync(f.Author, create);
        var third = Guid.NewGuid();
        var fourth = Guid.NewGuid();
        f.Store.Entities[third] = f.Store.Entities[f.Draft.PartyOneId] with { EntityId = third };
        f.Store.Entities[fourth] = f.Store.Entities[f.Draft.PartyTwoId] with { EntityId = fourth };
        var save = f.Command(f.Draft with { PartyOneId = third, PartyTwoId = fourth }, create.ProposalId);
        await f.Service.SaveDraftAsync(f.Author, save);
        f.Access.AllowedEntities = [third, fourth];
        Assert.Null(await f.Service.GetAsync(f.Author, f.AgreementId));
        Assert.Empty((await f.Service.ListAsync(f.Author, new(EntityId: third))).Items);
        await Assert.ThrowsAsync<IntercompanyAgreementNotFoundException>(() => f.Service.SaveDraftAsync(f.Author, save));
        Assert.Equal(2, f.Store.State!.AggregateVersion);
    }

    [Fact]
    public async Task KnownAtBeforeCreationDoesNotInventAnAgreementOrFutureIdentity()
    {
        var f = new Fixture();
        await f.Service.CreateAsync(f.Author, f.Command(f.Draft));
        var before = new DateTimeOffset(2026, 11, 11, 0, 0, 0, TimeSpan.Zero);
        Assert.Null(await f.Service.GetAsync(f.Author, f.AgreementId, knownAtUtc: before));
        Assert.Empty((await f.Service.ListAsync(f.Author, new(KnownAtUtc: before))).Items);
    }

    [Fact]
    public void GeneratedJsonPreservesRolesVersionsAndEvidenceWithoutActorOrTenantInCommands()
    {
        var f = new Fixture();
        var payload = f.Command(f.Draft);
        var json = JsonSerializer.Serialize(payload, FundStructureContractsJsonContext.Default.IntercompanyAgreementCommandDto);
        var roundTrip = JsonSerializer.Deserialize(json, FundStructureContractsJsonContext.Default.IntercompanyAgreementCommandDto);
        Assert.Equal(payload.Draft!.PartyOneId, roundTrip!.Draft!.PartyOneId);
        Assert.Contains("\"agreementType\":\"Loan\"", json);
        Assert.DoesNotContain("tenantId", json);
        Assert.DoesNotContain("actor", json);
    }

    private static IntercompanyAgreementRevisionDto Revision(Fixture f, int version, DateOnly start) => new(
        f.AgreementId, version, version == 1 ? null : version - 1, version == 1 ? null : 1,
        version == 1 ? IntercompanyAgreementActionDto.Activate : IntercompanyAgreementActionDto.Amend,
        f.Draft with { EffectiveFrom = start }, Guid.NewGuid(), $"hash-{version}", ["author"], "author", "reviewer",
        new(2026, 11, version, 0, 0, 0, TimeSpan.Zero), "reviewed");

    private sealed class Fixture
    {
        public Guid AgreementId { get; } = Guid.NewGuid();
        public IntercompanyAgreementCallContext Author { get; } = new("tenant-a", "author");
        public IntercompanyAgreementCallContext Reviewer { get; } = new("tenant-a", "reviewer");
        public MemoryStore Store { get; } = new();
        public AccessAuthority Access { get; } = new();
        public EvidenceAuthority Evidence { get; } = new();
        public IntercompanyAgreementDraftDto Draft { get; }
        public IntercompanyAgreementService Service { get; }
        public Fixture(IEntityLifecycleStore? lifecycle = null)
        {
            var first = Guid.NewGuid();
            var second = Guid.NewGuid();
            Draft = new("IC-001", "Synthetic loan", first, "Lender", second, "Borrower", IntercompanyAgreementTypeDto.Loan,
                "USD", January, Terms: new(FixedAnnualRate: 0.05m), Evidence: [new("vault:agreement")]);
            Store.Entities[first] = Entity(first);
            Store.Entities[second] = Entity(second);
            Service = new(Store, Access, Evidence, new Clock(), lifecycleStore: lifecycle);
        }
        public IntercompanyAgreementCommandDto Command(IntercompanyAgreementDraftDto? draft = null, Guid? proposalId = null) =>
            new(Guid.NewGuid(), AgreementId, Store.State?.AggregateVersion ?? 0, proposalId ?? Guid.NewGuid(), draft, "Reviewed synthetic rationale");
        public async Task<IntercompanyAgreementCommandDto> ApproveAsync(IntercompanyAgreementDraftDto draft)
        {
            var create = Command(draft);
            if (Store.State is null)
                await Service.CreateAsync(Author, create);
            else
                await Service.CreateProposalAsync(Author, create);
            var submitted = await Service.SubmitAsync(Author, Command(proposalId: create.ProposalId));
            var approve = Command(proposalId: create.ProposalId) with { SubmittedHash = submitted.Detail.Proposals[^1].SubmittedHash };
            await Service.ApproveAsync(Reviewer, approve);
            return approve;
        }
        private static LegalEntitySummaryDto Entity(Guid id) => new(id, LegalEntityTypeDto.Other, id.ToString("N"), "Synthetic entity", "US", "USD", true,
            new(2025, 1, 1, 0, 0, 0, TimeSpan.Zero), null);
    }

    private sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 11, 12, 0, 0, 0, TimeSpan.Zero);
    }
    private sealed class AccessAuthority : IIntercompanyAgreementAccessAuthority
    {
        public bool Allowed { get; set; } = true;
        public IReadOnlyList<Guid>? AllowedEntities { get; set; }
        public Task<bool> CanAccessEntitiesAsync(IntercompanyAgreementCallContext context, IReadOnlyList<Guid> ids, CancellationToken ct = default) =>
            Task.FromResult(Allowed && (AllowedEntities is null || ids.All(AllowedEntities.Contains)));
    }
    private sealed class EvidenceAuthority : IIntercompanyAgreementEvidenceAuthority
    {
        public string Hash { get; set; } = "sha256-verified";
        public Task<IReadOnlyList<IntercompanyAgreementEvidenceDto>> ResolveAsync(IntercompanyAgreementCallContext context, Guid agreementId, Guid proposalId,
            IReadOnlyList<IntercompanyAgreementEvidenceDto> refs, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<IntercompanyAgreementEvidenceDto>>(refs.Select(r => new IntercompanyAgreementEvidenceDto(r.Reference, "artifact-1", Hash)).ToArray());
    }
    private sealed class MemoryStore : IIntercompanyAgreementStore
    {
        private readonly SemaphoreSlim _gate = new(1);
        public IntercompanyAgreementStoreState? State { get; private set; }
        public Dictionary<Guid, LegalEntitySummaryDto> Entities { get; } = [];
        public Task<IntercompanyAgreementStoreState?> GetAsync(string tenantId, Guid agreementId, CancellationToken ct = default) => Task.FromResult(State);
        public Task<IReadOnlyList<IntercompanyAgreementStoreState>> ListAsync(string tenantId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<IntercompanyAgreementStoreState>>(State is null ? [] : [State]);
        public Task<IIntercompanyAgreementPostingLease> AcquirePostingLeaseAsync(string tenantId, IReadOnlyList<Guid> ids, CancellationToken ct = default) => throw new NotSupportedException();
        public async Task<T> ExecuteAsync<T>(string tenantId, Guid id, Func<IIntercompanyAgreementStoreTransaction, CancellationToken, Task<T>> operation, CancellationToken ct = default)
        {
            await _gate.WaitAsync(ct);
            try
            {
                var tx = new Transaction(State, Entities);
                var result = await operation(tx, ct);
                State = tx.Pending ?? State;
                return result;
            }
            finally { _gate.Release(); }
        }
        private sealed class Transaction(IntercompanyAgreementStoreState? state, Dictionary<Guid, LegalEntitySummaryDto> entities) : IIntercompanyAgreementStoreTransaction
        {
            public IntercompanyAgreementStoreState? State => state;
            public IntercompanyAgreementStoreState? Pending { get; private set; }
            public Task<LegalEntitySummaryDto?> GetLegalEntityAsync(Guid id, CancellationToken ct = default) => Task.FromResult(entities.GetValueOrDefault(id));
            public void Save(IntercompanyAgreementStoreState value) => Pending = value;
        }
    }
}
