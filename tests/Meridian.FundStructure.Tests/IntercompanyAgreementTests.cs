using Meridian.Contracts.FundStructure;
using Meridian.Entities.FundStructure;
using Meridian.FinancialOperations.Intercompany;
using Meridian.Storage.FundStructure;
using Xunit;

namespace Meridian.FundStructure.Tests;

public sealed class IntercompanyAgreementTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Cutoff = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task OwnerCanApproveExactRevision_AndRetryReturnsOriginalResultAfterLaterAction()
    {
        var evidence = new Evidence();
        var service = Service(new InMemoryIntercompanyAgreementStore(), evidence);
        var request = Request() with { SettlementDueDays = 14 };
        var draft = await service.CreateAsync("tenant-a", "owner", request);
        var submitted = await service.ApplyActionAsync("tenant-a", "owner", draft.AgreementId,
            IntercompanyAgreementActionDto.Submit, new(Guid.NewGuid(), draft.Version));
        var approved = await service.ApplyActionAsync("tenant-a", "owner", draft.AgreementId,
            IntercompanyAgreementActionDto.Approve, new(Guid.NewGuid(), submitted.Version));
        Assert.Equal("owner", approved.ApprovedBy);
        Assert.Equal(draft.RevisionId, approved.RevisionId);
        Assert.Equal(14, approved.SettlementDueDays);
        Assert.Equal(1, evidence.Calls);
        var retry = await service.CreateAsync("tenant-a", "owner", request);
        Assert.Equal(IntercompanyAgreementStatusDto.Draft, retry.Status);
        Assert.Equal(1, retry.Version);
        Assert.Equal(approved.Version, (await service.GetAsync("tenant-a", draft.AgreementId))!.Version);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync("tenant-a", "owner", request with { Currency = "GBP" }));
    }

    [Fact]
    public async Task ApprovalRequiresVerifiedSignedDocument_AndTenantReadIsIsolated()
    {
        var service = Service(new InMemoryIntercompanyAgreementStore());
        var draft = await service.CreateAsync("tenant-a", "owner", Request() with { SignedDocument = null });
        var submitted = await service.ApplyActionAsync("tenant-a", "owner", draft.AgreementId,
            IntercompanyAgreementActionDto.Submit, new(Guid.NewGuid(), draft.Version));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ApplyActionAsync("tenant-a", "owner", draft.AgreementId,
            IntercompanyAgreementActionDto.Approve, new(Guid.NewGuid(), submitted.Version)));
        Assert.Null(await service.GetAsync("tenant-b", draft.AgreementId));
        Assert.Empty(await service.ListAsync("tenant-b"));
    }

    [Fact]
    public async Task MultipleFacilitiesForOnePairAreAllowed_ButSelfAgreementAndEmptyWindowRefuse()
    {
        var service = Service(new InMemoryIntercompanyAgreementStore());
        var request = Request();
        await service.CreateAsync("tenant-a", "owner", request);
        await service.CreateAsync("tenant-a", "owner", request with { RequestId = Guid.NewGuid(), AgreementId = Guid.NewGuid(), FacilityReference = "facility-two" });
        Assert.Equal(2, (await service.ListAsync("tenant-a")).Count);
        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync("tenant-a", "owner", request with
            { RequestId = Guid.NewGuid(), AgreementId = Guid.NewGuid(), PartyTwoEntityId = request.PartyOneEntityId }));
        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync("tenant-a", "owner", request with
            { RequestId = Guid.NewGuid(), AgreementId = Guid.NewGuid(), EffectiveTo = Start }));
        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync("tenant-a", "owner", request with
            { RequestId = Guid.NewGuid(), AgreementId = Guid.NewGuid(), SettlementDueDays = -1 }));
    }

    [Fact]
    public async Task ExactHistoricalRevisionResolvesBeforeTermination_AndRefusesAtHalfOpenCutoff()
    {
        var service = Service(new InMemoryIntercompanyAgreementStore());
        var approved = await ApprovedAsync(service, Request());
        await service.ApplyActionAsync("tenant-a", "owner", approved.AgreementId, IntercompanyAgreementActionDto.Terminate,
            new(Guid.NewGuid(), approved.Version, "Facility settled", Cutoff));
        var retained = await service.ResolveApprovedRevisionAsync("tenant-a", approved.AgreementId, approved.RevisionId, Cutoff.AddTicks(-1));
        Assert.Equal(approved.RevisionId, retained.RevisionId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ResolveApprovedRevisionAsync("tenant-a", approved.AgreementId, approved.RevisionId, Cutoff));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ResolveApprovedRevisionAsync("tenant-a", approved.AgreementId, Guid.NewGuid(), Start));
    }

    [Fact]
    public async Task AmendmentDraftPreservesLivePredecessor_ApprovalRetainsExplicitSupersessionCutoff()
    {
        var service = Service(new InMemoryIntercompanyAgreementStore());
        var approved = await ApprovedAsync(service, Request());
        var amended = await service.ReviseAsync("tenant-a", "owner", approved.AgreementId, new(Guid.NewGuid(), approved.Version,
            "facility-one", "Lender", "Borrower", "USD", "Monthly", Cutoff, null, approved.PartyMappings, approved.SignedDocument, "New terms"));
        Assert.NotEqual(approved.RevisionId, amended.RevisionId);
        Assert.Null(amended.ApprovedBy);
        Assert.Equal(approved.RevisionId, amended.SupersedesRevisionId);
        Assert.Equal(approved.RevisionId, (await service.ResolveApprovedRevisionAsync("tenant-a", approved.AgreementId, approved.RevisionId, Cutoff)).RevisionId);
        var submitted = await service.ApplyActionAsync("tenant-a", "owner", amended.AgreementId,
            IntercompanyAgreementActionDto.Submit, new(Guid.NewGuid(), amended.Version));
        await service.ApplyActionAsync("tenant-a", "owner", amended.AgreementId,
            IntercompanyAgreementActionDto.Approve, new(Guid.NewGuid(), submitted.Version));
        var old = await service.ResolveApprovedRevisionAsync("tenant-a", approved.AgreementId, approved.RevisionId, Cutoff.AddTicks(-1));
        Assert.Equal("facility-one", old.FacilityReference);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ResolveApprovedRevisionAsync("tenant-a", approved.AgreementId, approved.RevisionId, Cutoff));
        Assert.Equal(amended.RevisionId, (await service.ResolveApprovedRevisionAsync("tenant-a", approved.AgreementId, amended.RevisionId, Cutoff)).RevisionId);
    }

    [Fact]
    public async Task StaleVersionsRefuse_AndPostingLeaseFencesAgreementMutation()
    {
        var store = new InMemoryIntercompanyAgreementStore();
        var service = Service(store);
        var draft = await service.CreateAsync("tenant-a", "owner", Request());
        var lease = await store.AcquireAuthorityLeaseAsync();
        var mutation = service.ApplyActionAsync("tenant-a", "owner", draft.AgreementId,
            IntercompanyAgreementActionDto.Submit, new(Guid.NewGuid(), draft.Version));
        Assert.False(mutation.IsCompleted);
        await lease.DisposeAsync();
        await mutation;
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ApplyActionAsync("tenant-a", "owner", draft.AgreementId,
            IntercompanyAgreementActionDto.Approve, new(Guid.NewGuid(), draft.Version)));
    }

    [Fact]
    public async Task TargetedPredecessorTerminationPreservesPendingAmendmentAndAggregateCas()
    {
        var service = Service(new InMemoryIntercompanyAgreementStore());
        var approved = await ApprovedAsync(service, Request());
        var draft = await service.ReviseAsync("tenant-a", "owner", approved.AgreementId,
            new(Guid.NewGuid(), approved.Version, "pending amendment", "Lender", "Borrower", "USD", "Monthly",
                Cutoff.AddMonths(1), null, approved.PartyMappings, approved.SignedDocument, "Prepare next terms"));
        var request = new IntercompanyAgreementActionRequest(Guid.NewGuid(), draft.Version, "Settle predecessor", Cutoff,
            RevisionId: approved.RevisionId);
        var current = await service.ApplyActionAsync("tenant-a", "owner", approved.AgreementId, IntercompanyAgreementActionDto.Terminate, request);
        Assert.Equal(draft.RevisionId, current.RevisionId);
        Assert.Equal(IntercompanyAgreementStatusDto.Draft, current.Status);
        Assert.Equal(draft.Version + 1, current.Version);
        Assert.Equal("pending amendment", current.FacilityReference);
        var history = (await service.GetHistoryAsync("tenant-a", approved.AgreementId))!;
        Assert.Equal(approved.RevisionId, history.Events[^1].TargetRevisionId);
        Assert.Equal(IntercompanyAgreementStatusDto.Terminated, history.Events[^1].AffectedRevision.Status);
        Assert.Equal(current.Version, (await service.ApplyActionAsync("tenant-a", "owner", approved.AgreementId,
            IntercompanyAgreementActionDto.Terminate, request)).Version);
        await service.ResolveApprovedRevisionAsync("tenant-a", approved.AgreementId, approved.RevisionId, Cutoff.AddTicks(-1));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ResolveApprovedRevisionAsync("tenant-a", approved.AgreementId, approved.RevisionId, Cutoff));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ApplyActionAsync("tenant-a", "owner", approved.AgreementId,
            IntercompanyAgreementActionDto.Submit, new(Guid.NewGuid(), draft.Version)));
        var submitted = await service.ApplyActionAsync("tenant-a", "owner", approved.AgreementId,
            IntercompanyAgreementActionDto.Submit, new(Guid.NewGuid(), current.Version));
        Assert.Equal(draft.RevisionId, submitted.RevisionId);
    }

    [Fact]
    public async Task NovationRefusesSuccessorChangedBeforeMutationLease_AndLeasesEveryParty()
    {
        var store = new InMemoryIntercompanyAgreementStore();
        var scope = new Scope();
        var service = new IntercompanyAgreementService(store, scope, new Evidence());
        var successorService = Service(store);
        var source = await ApprovedAsync(service, Request());
        var successor = await ApprovedAsync(successorService, Request() with { OrganizationId = source.OrganizationId, EffectiveFrom = Cutoff });
        scope.BeforeLease = () => successorService.ApplyActionAsync("tenant-a", "owner", successor.AgreementId,
            IntercompanyAgreementActionDto.Terminate, new(Guid.NewGuid(), successor.Version, "Successor closed", Cutoff.AddDays(1)));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.ApplyActionAsync("tenant-a", "owner", source.AgreementId,
            IntercompanyAgreementActionDto.Novate, new(Guid.NewGuid(), source.Version, "Novation", Cutoff, successor.AgreementId,
                source.RevisionId, successor.RevisionId, successor.Version)));
        Assert.Contains("successor changed", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(4, scope.LastLeasedEntityIds.Count);
        Assert.Equal(source.Version, (await service.GetAsync("tenant-a", source.AgreementId))!.Version);
    }

    [Fact]
    public async Task TargetedNovationPinsExactPeer_AndPeerMutationWaitsUntilCommit()
    {
        var store = new InMemoryIntercompanyAgreementStore();
        var scope = new Scope();
        var service = new IntercompanyAgreementService(store, scope, new Evidence());
        var peerService = Service(store);
        var source = await ApprovedAsync(service, Request());
        var successor = await ApprovedAsync(peerService, Request() with { OrganizationId = source.OrganizationId, EffectiveFrom = Cutoff });
        var draft = await service.ReviseAsync("tenant-a", "owner", source.AgreementId,
            new(Guid.NewGuid(), source.Version, "unapproved amendment", "Lender", "Borrower", "USD", "Monthly",
                Cutoff.AddMonths(1), null, source.PartyMappings, source.SignedDocument, "Prepare future terms"));
        var validated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        scope.DuringValidation = async (agreement, active) =>
        {
            if (active && agreement.AgreementId == successor.AgreementId)
            { validated.TrySetResult(); await finish.Task; }
        };
        var novation = service.ApplyActionAsync("tenant-a", "owner", source.AgreementId, IntercompanyAgreementActionDto.Novate,
            new(Guid.NewGuid(), draft.Version, "Reviewed novation", Cutoff, successor.AgreementId,
                source.RevisionId, successor.RevisionId, successor.Version));
        await validated.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var peerTermination = peerService.ApplyActionAsync("tenant-a", "owner", successor.AgreementId,
            IntercompanyAgreementActionDto.Terminate, new(Guid.NewGuid(), successor.Version, "Later peer close", Cutoff.AddDays(1)));
        Assert.False(peerTermination.IsCompleted);
        finish.TrySetResult();
        var projection = await novation;
        await peerTermination;
        Assert.Equal(draft.RevisionId, projection.RevisionId);
        Assert.Equal(IntercompanyAgreementStatusDto.Draft, projection.Status);
        var item = (await service.GetHistoryAsync("tenant-a", source.AgreementId))!.Events[^1];
        Assert.Equal(IntercompanyAgreementStatusDto.Novated, item.AffectedRevision.Status);
        Assert.Equal(successor.RevisionId, item.AffectedRevision.NovationSuccessorRevisionId);
        Assert.Equal(successor.Version, item.AffectedRevision.NovationSuccessorVersion);
        Assert.Equal(2, item.AffectedRevision.NovationSuccessorLifecycleEvidence!.Count);
        Assert.Equal(4, scope.LastLeasedEntityIds.Count);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ApplyActionAsync("tenant-a", "owner", source.AgreementId,
            IntercompanyAgreementActionDto.Submit, new(Guid.NewGuid(), projection.Version)));
    }

    [Fact]
    public async Task FileStoreReopensHistory_AndCorruptHistoryFailsClosed()
    {
        var directory = Path.Combine(Path.GetTempPath(), "meridian-intercompany-" + Guid.NewGuid().ToString("N"));
        try
        {
            var service = Service(new FileIntercompanyAgreementStore(directory));
            var request = Request();
            var approved = await ApprovedAsync(service, request);
            var reopened = Service(new FileIntercompanyAgreementStore(directory));
            Assert.Equal(approved.RevisionId, (await reopened.GetAsync("tenant-a", approved.AgreementId))!.RevisionId);
            Assert.Equal(1, (await reopened.CreateAsync("tenant-a", "owner", request)).Version);
            var retainedFile = Directory.GetFiles(directory, "*.json", SearchOption.AllDirectories).Single();
            var json = await File.ReadAllTextAsync(retainedFile);
            await File.WriteAllTextAsync(retainedFile, json.Replace("facility-one", "tampered-terms", StringComparison.Ordinal));
            await Assert.ThrowsAsync<InvalidDataException>(() => reopened.GetAsync("tenant-a", approved.AgreementId));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task ConcurrentFileWritersDoNotLoseHistory_OneStaleMutationRefuses()
    {
        var directory = Path.Combine(Path.GetTempPath(), "meridian-intercompany-" + Guid.NewGuid().ToString("N"));
        try
        {
            var first = Service(new FileIntercompanyAgreementStore(directory));
            var second = Service(new FileIntercompanyAgreementStore(directory));
            var draft = await first.CreateAsync("tenant-a", "owner", Request());
            async Task<bool> Submit(IntercompanyAgreementService service)
            {
                try
                {
                    await service.ApplyActionAsync("tenant-a", "owner", draft.AgreementId, IntercompanyAgreementActionDto.Submit,
                        new(Guid.NewGuid(), draft.Version));
                    return true;
                }
                catch (InvalidOperationException) { return false; }
            }
            var results = await Task.WhenAll(Submit(first), Submit(second));
            Assert.Single(results.Where(x => x));
            Assert.Equal(2, (await first.GetHistoryAsync("tenant-a", draft.AgreementId))!.Events.Count);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task ProductionScopeRefusesForeignEntityBeforeReadingOrganizationOrBooks()
    {
        var request = Request();
        var store = new FakeFundStructureStore(isTenantPartitioned: true);
        await store.StampNodeTenantAsync(request.OrganizationId, "tenant-a");
        await store.StampNodeTenantAsync(request.PartyOneEntityId, "tenant-a");
        await store.StampNodeTenantAsync(request.PartyTwoEntityId, "tenant-b");
        var agreement = await Service(new InMemoryIntercompanyAgreementStore()).CreateAsync("tenant-a", "owner", request);
        var authority = new FundStructureIntercompanyAgreementScopeAuthority(null!, store, null!, null!, new InMemoryEntityLifecycleStore(), null!);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => authority.ValidateAsync("tenant-a", agreement, false));
    }

    [Fact]
    public void LifecyclePolicyRefusesInactiveParties()
    {
        var request = Request();
        var entity = new LegalEntitySummaryDto(request.PartyOneEntityId, LegalEntityTypeDto.Fund, "A", "Entity A", "US", "USD",
            true, Start, null, LifecycleStatus: LegalEntityLifecycleStatusDto.Dissolved);
        var agreement = new IntercompanyAgreementDto(request.AgreementId, request.OrganizationId, request.FacilityReference, request.Kind,
            request.PartyOneEntityId, request.PartyTwoEntityId, "Lender", "Borrower", "USD", "Monthly", Start, null,
            Guid.NewGuid(), 1, 1, IntercompanyAgreementStatusDto.Draft, request.PartyMappings, request.SignedDocument, "owner", Start, "owner", Start);
        Assert.Throws<InvalidOperationException>(() => IntercompanyAgreementPolicy.ValidateEntity(agreement, entity));
    }

    private static CreateIntercompanyAgreementRequest Request()
    {
        var first = Guid.NewGuid(); var second = Guid.NewGuid();
        return new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "facility-one", IntercompanyAgreementKindDto.Loan,
            first, second, "Lender", "Borrower", "USD", "Monthly", Start, null,
            [new(first, Guid.NewGuid(), "ar", "ap", "cash"), new(second, Guid.NewGuid(), "ar", "ap", "cash")],
            new("vault", "document", "artifact", new string('a', 64), "company"));
    }

    private static IntercompanyAgreementService Service(IIntercompanyAgreementStore store, Evidence? evidence = null)
        => new(store, new Scope(), evidence ?? new Evidence());

    private static async Task<IntercompanyAgreementDto> ApprovedAsync(IntercompanyAgreementService service, CreateIntercompanyAgreementRequest request)
    {
        var draft = await service.CreateAsync("tenant-a", "owner", request);
        var submitted = await service.ApplyActionAsync("tenant-a", "owner", draft.AgreementId, IntercompanyAgreementActionDto.Submit, new(Guid.NewGuid(), draft.Version));
        return await service.ApplyActionAsync("tenant-a", "owner", draft.AgreementId, IntercompanyAgreementActionDto.Approve, new(Guid.NewGuid(), submitted.Version));
    }

    private sealed class Scope : IIntercompanyAgreementScopeAuthority
    {
        public Func<Task>? BeforeLease { get; set; }
        public Func<IntercompanyAgreementDto, bool, Task>? DuringValidation { get; set; }
        public IReadOnlyList<Guid> LastLeasedEntityIds { get; private set; } = [];
        public Task<IAsyncDisposable> AcquireLeaseAsync(string tenantId, IntercompanyAgreementDto agreement, CancellationToken ct = default)
            => AcquireLeasesAsync(tenantId, [agreement], ct);
        public async Task<IAsyncDisposable> AcquireLeasesAsync(string tenantId, IReadOnlyList<IntercompanyAgreementDto> agreements, CancellationToken ct = default)
        {
            LastLeasedEntityIds = agreements.SelectMany(x => new[] { x.PartyOneEntityId, x.PartyTwoEntityId }).Distinct().Order().ToArray();
            if (BeforeLease is { } callback) { BeforeLease = null; await callback(); }
            return new Lease();
        }
        public Task ValidateAsync(string tenantId, IntercompanyAgreementDto agreement, bool requireActive, CancellationToken ct = default)
            => DuringValidation?.Invoke(agreement, requireActive) ?? Task.CompletedTask;
        public Task<IReadOnlyList<IntercompanyEntityLifecycleReferenceDto>> GetLifecycleEvidenceAsync(string tenantId, IntercompanyAgreementDto agreement, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<IntercompanyEntityLifecycleReferenceDto>>([new(agreement.PartyOneEntityId, 1, new string('a', 64)),
                new(agreement.PartyTwoEntityId, 1, new string('b', 64))]);
    }
    private sealed class Lease : IAsyncDisposable { public ValueTask DisposeAsync() => ValueTask.CompletedTask; }
    private sealed class Evidence : IIntercompanyAgreementEvidenceAuthority
    {
        public int Calls { get; private set; }
        public Task VerifyAsync(string tenantId, Guid agreementId, IntercompanySignedDocumentDto document, CancellationToken ct = default)
        { Calls++; return Task.CompletedTask; }
    }
}
