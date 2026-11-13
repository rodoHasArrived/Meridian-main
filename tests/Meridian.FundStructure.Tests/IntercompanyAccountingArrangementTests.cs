using System.Text.Json;
using Meridian.Application.FundStructure;
using Meridian.Contracts.FundStructure;
using Meridian.FinancialOperations.Intercompany;
using Meridian.Storage.FundStructure;
using Xunit;

namespace Meridian.FundStructure.Tests;

public sealed class IntercompanyAccountingArrangementTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Cutoff = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly string LegalHash = new('a', 64);

    [Fact]
    public async Task ProfileApprovalPinsNativeRevision_AndRetryReturnsOriginalDraft()
    {
        var fixture = new ArrangementFixture();
        var request = fixture.Request();
        var approved = await ApprovedAsync(fixture.Service, request);
        Assert.Equal("owner", approved.ApprovedBy);
        Assert.Equal(request.CanonicalAgreementId, approved.CanonicalAgreementId);
        Assert.Equal(request.CanonicalAgreementVersion, approved.CanonicalAgreementVersion);
        Assert.Equal(request.CanonicalPayloadHash, approved.CanonicalPayloadHash);
        var resolved = await fixture.Service.ResolveApprovedRevisionAsync("tenant-a", approved.ArrangementId, approved.RevisionId, Start);
        Assert.Equal("USD", resolved.Currency);
        Assert.Equal("Lender", resolved.PartyOneRole);
        Assert.Equal("Quarterly", resolved.SettlementCadence);
        Assert.Equal(14, resolved.SettlementDueDays);
        Assert.Equal("independent-reviewer", resolved.LegalRevision.Approver);
        Assert.Equal(0, fixture.Native.Commands);

        var retry = await fixture.Service.CreateAsync("tenant-a", "owner", request);
        Assert.Equal(IntercompanyAccountingArrangementStatusDto.Draft, retry.Status);
        Assert.Equal(1, retry.Version);
        Assert.Equal(approved.Version, (await fixture.Service.GetAsync("tenant-a", approved.ArrangementId))!.Version);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.CreateAsync("tenant-a", "owner",
            request with { ArrangementReference = "different-profile" }));
    }

    [Fact]
    public void AuthoringContractsContainConfigurationAndCanonicalPins_WithoutEditableLegalFacts()
    {
        var request = new ArrangementFixture().Request();
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(request,
            IntercompanyAccountingArrangementsJsonContext.Default.CreateIntercompanyAccountingArrangementRequest));
        Assert.True(json.RootElement.TryGetProperty("canonicalAgreementId", out _));
        Assert.True(json.RootElement.TryGetProperty("canonicalAgreementVersion", out _));
        Assert.True(json.RootElement.TryGetProperty("canonicalPayloadHash", out _));
        foreach (var property in new[] { "currency", "partyOneEntityId", "partyTwoEntityId", "partyOneRole",
                     "partyTwoRole", "signedDocument", "settlementCadence", "settlementDueDays", "terms", "evidence" })
            Assert.False(json.RootElement.TryGetProperty(property, out _), property);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(2, false)]
    [InlineData(1, true)]
    public async Task CreateRejectsMissingVersionOrMismatchedCanonicalHash(int version, bool wrongHash)
    {
        var fixture = new ArrangementFixture();
        var request = fixture.Request() with { CanonicalAgreementVersion = version,
            CanonicalPayloadHash = wrongHash ? new string('b', 64) : LegalHash };
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.CreateAsync("tenant-a", "owner", request));
        Assert.Empty(await fixture.Store.ListAsync("tenant-a"));
        Assert.Equal(0, fixture.Native.ActiveLeases);
    }

    [Fact]
    public async Task MappingPartiesTemplateAndWindowMustMatchPinnedNativeRevision()
    {
        var fixture = new ArrangementFixture();
        var request = fixture.Request();
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Service.CreateAsync("tenant-a", "owner",
            request with { EffectiveTo = Start }));
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Service.CreateAsync("tenant-a", "owner",
            request with { EffectiveFrom = Start.AddDays(-1) }));
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Service.CreateAsync("tenant-a", "owner",
            request with { TemplateKind = IntercompanyAccountingTemplateKindDto.ManagementFee }));
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Service.CreateAsync("tenant-a", "owner",
            request with { PartyMappings = [request.PartyMappings[0], request.PartyMappings[1] with { EntityId = Guid.NewGuid() }] }));
        await fixture.Service.CreateAsync("tenant-a", "owner", request);
        await fixture.Service.CreateAsync("tenant-a", "owner", request with { RequestId = Guid.NewGuid(),
            ArrangementId = Guid.NewGuid(), ArrangementReference = "separate-profile" });
        Assert.Equal(2, (await fixture.Service.ListAsync("tenant-a")).Count);
        Assert.Null(await fixture.Service.GetAsync("tenant-b", request.ArrangementId));
        Assert.Empty(await fixture.Service.ListAsync("tenant-b"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeAmendmentOrTerminationCutsOffMappingWindow(bool termination)
    {
        var fixture = new ArrangementFixture();
        await fixture.Native.ReplaceAsync(fixture.Legal, fixture.NextLegal(termination));
        var request = fixture.Request() with { EffectiveTo = Cutoff };
        var approved = await ApprovedAsync(fixture.Service, request);
        await fixture.Service.ResolveApprovedRevisionAsync("tenant-a", approved.ArrangementId, approved.RevisionId, Cutoff.AddTicks(-1));
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.ResolveApprovedRevisionAsync(
            "tenant-a", approved.ArrangementId, approved.RevisionId, Cutoff));
        var unbounded = await fixture.Service.CreateAsync("tenant-a", "owner", request with
            { RequestId = Guid.NewGuid(), ArrangementId = Guid.NewGuid(), EffectiveTo = null });
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.ApplyActionAsync("tenant-a", "owner",
            unbounded.ArrangementId, IntercompanyAccountingArrangementActionDto.Submit, new(Guid.NewGuid(), unbounded.Version)));
        Assert.Contains("cutoff", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ApprovalRechecksNativeCutoffEstablishedAfterSubmission()
    {
        var fixture = new ArrangementFixture();
        var draft = await fixture.Service.CreateAsync("tenant-a", "owner", fixture.Request());
        var submitted = await fixture.Service.ApplyActionAsync("tenant-a", "owner", draft.ArrangementId,
            IntercompanyAccountingArrangementActionDto.Submit, new(Guid.NewGuid(), draft.Version));
        await fixture.Native.ReplaceAsync(fixture.Legal, fixture.NextLegal(termination: true));
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.ApplyActionAsync("tenant-a", "owner",
            draft.ArrangementId, IntercompanyAccountingArrangementActionDto.Approve, new(Guid.NewGuid(), submitted.Version)));
        var retained = (await fixture.Service.GetAsync("tenant-a", draft.ArrangementId))!;
        Assert.Equal(submitted.Version, retained.Version);
        Assert.Equal(IntercompanyAccountingArrangementStatusDto.Submitted, retained.Status);
        Assert.Null(retained.Arrangement.ApprovedBy);
    }

    [Fact]
    public async Task ExactMappingRetirementUsesHalfOpenWindowWithoutTerminatingNativeAgreement()
    {
        var fixture = new ArrangementFixture();
        var approved = await ApprovedAsync(fixture.Service, fixture.Request());
        await fixture.Service.ApplyActionAsync("tenant-a", "owner", approved.ArrangementId,
            IntercompanyAccountingArrangementActionDto.Retire, new(Guid.NewGuid(), approved.Version, "Mapping no longer used", Cutoff));
        var historical = await fixture.Service.ResolveApprovedRevisionAsync("tenant-a", approved.ArrangementId, approved.RevisionId, Cutoff.AddTicks(-1));
        Assert.Equal(approved.RevisionId, historical.RevisionId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.ResolveApprovedRevisionAsync(
            "tenant-a", approved.ArrangementId, approved.RevisionId, Cutoff));
        var exactHistorical = await fixture.Service.GetApprovedRevisionAsync("tenant-a", approved.ArrangementId, approved.RevisionId);
        Assert.Equal(approved.RevisionId, exactHistorical.RevisionId);
        Assert.Equal(approved.CanonicalReference, exactHistorical.Arrangement.CanonicalReference);
        Assert.Equal(fixture.Legal, await fixture.LegalAuthority.GetEffectiveRevisionAsync("tenant-a",
            approved.CanonicalReference, DateOnly.FromDateTime(Cutoff.UtcDateTime)));
        Assert.Equal(0, fixture.Native.Commands);
    }

    [Fact]
    public async Task AmendmentDraftKeepsApprovedPredecessorUsableUntilNewMappingApproval()
    {
        var fixture = new ArrangementFixture();
        var approved = await ApprovedAsync(fixture.Service, fixture.Request());
        var amended = await fixture.Service.ReviseAsync("tenant-a", "owner", approved.ArrangementId,
            Revision(approved, Cutoff, "amended-profile"));
        Assert.NotEqual(approved.RevisionId, amended.RevisionId);
        Assert.Null(amended.ApprovedBy);
        Assert.Equal(approved.RevisionId, amended.SupersedesRevisionId);
        Assert.Equal(approved.RevisionId, (await fixture.Service.ResolveApprovedRevisionAsync("tenant-a",
            approved.ArrangementId, approved.RevisionId, Cutoff)).RevisionId);
        var submitted = await fixture.Service.ApplyActionAsync("tenant-a", "owner", amended.ArrangementId,
            IntercompanyAccountingArrangementActionDto.Submit, new(Guid.NewGuid(), amended.Version));
        await fixture.Service.ApplyActionAsync("tenant-a", "owner", amended.ArrangementId,
            IntercompanyAccountingArrangementActionDto.Approve, new(Guid.NewGuid(), submitted.Version));
        Assert.Equal(approved.RevisionId, (await fixture.Service.ResolveApprovedRevisionAsync("tenant-a",
            approved.ArrangementId, approved.RevisionId, Cutoff.AddTicks(-1))).RevisionId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.ResolveApprovedRevisionAsync(
            "tenant-a", approved.ArrangementId, approved.RevisionId, Cutoff));
        Assert.Equal(amended.RevisionId, (await fixture.Service.ResolveApprovedRevisionAsync("tenant-a",
            approved.ArrangementId, amended.RevisionId, Cutoff)).RevisionId);
    }

    [Fact]
    public async Task PostingLeaseFencesMappingMutation_AndStaleCasRefuses()
    {
        var fixture = new ArrangementFixture();
        var draft = await fixture.Service.CreateAsync("tenant-a", "owner", fixture.Request());
        var lease = await fixture.Store.AcquireAuthorityLeaseAsync();
        var mutation = fixture.Service.ApplyActionAsync("tenant-a", "owner", draft.ArrangementId,
            IntercompanyAccountingArrangementActionDto.Submit, new(Guid.NewGuid(), draft.Version));
        try { Assert.False(mutation.IsCompleted); }
        finally { await lease.DisposeAsync(); }
        await mutation;
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.ApplyActionAsync("tenant-a", "owner",
            draft.ArrangementId, IntercompanyAccountingArrangementActionDto.Approve, new(Guid.NewGuid(), draft.Version)));
    }

    [Fact]
    public async Task TargetedRetirementPreservesPendingAmendmentAndAggregateCas()
    {
        var fixture = new ArrangementFixture();
        var approved = await ApprovedAsync(fixture.Service, fixture.Request());
        var draft = await fixture.Service.ReviseAsync("tenant-a", "owner", approved.ArrangementId,
            Revision(approved, Cutoff.AddMonths(1), "pending-profile"));
        var request = new IntercompanyAccountingArrangementActionRequest(Guid.NewGuid(), draft.Version,
            "Retire predecessor mapping", Cutoff, approved.RevisionId);
        var current = await fixture.Service.ApplyActionAsync("tenant-a", "owner", approved.ArrangementId,
            IntercompanyAccountingArrangementActionDto.Retire, request);
        Assert.Equal(draft.RevisionId, current.RevisionId);
        Assert.Equal(IntercompanyAccountingArrangementStatusDto.Draft, current.Status);
        Assert.Equal(draft.Version + 1, current.Version);
        Assert.Equal("pending-profile", current.ArrangementReference);
        var history = (await fixture.Service.GetHistoryAsync("tenant-a", approved.ArrangementId))!;
        Assert.Equal(approved.RevisionId, history.Events[^1].TargetRevisionId);
        Assert.Equal(IntercompanyAccountingArrangementStatusDto.Retired, history.Events[^1].AffectedRevision.Status);
        Assert.Equal(current.Version, (await fixture.Service.ApplyActionAsync("tenant-a", "owner", approved.ArrangementId,
            IntercompanyAccountingArrangementActionDto.Retire, request)).Version);
        await fixture.Service.ResolveApprovedRevisionAsync("tenant-a", approved.ArrangementId, approved.RevisionId, Cutoff.AddTicks(-1));
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.ResolveApprovedRevisionAsync(
            "tenant-a", approved.ArrangementId, approved.RevisionId, Cutoff));
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.ApplyActionAsync("tenant-a", "owner",
            approved.ArrangementId, IntercompanyAccountingArrangementActionDto.Submit, new(Guid.NewGuid(), draft.Version)));
        var submitted = await fixture.Service.ApplyActionAsync("tenant-a", "owner", approved.ArrangementId,
            IntercompanyAccountingArrangementActionDto.Submit, new(Guid.NewGuid(), current.Version));
        Assert.Equal(draft.RevisionId, submitted.RevisionId);
    }

    [Fact]
    public async Task NativeLeaseIsRetainedWhileMappingApprovalValidatesAndCommits()
    {
        var fixture = new ArrangementFixture();
        var draft = await fixture.Service.CreateAsync("tenant-a", "owner", fixture.Request());
        var submitted = await fixture.Service.ApplyActionAsync("tenant-a", "owner", draft.ArrangementId,
            IntercompanyAccountingArrangementActionDto.Submit, new(Guid.NewGuid(), draft.Version));
        var validated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Scope.DuringValidation = async (_, active) =>
        {
            if (active) { validated.TrySetResult(); await finish.Task; }
        };
        var approval = fixture.Service.ApplyActionAsync("tenant-a", "owner", draft.ArrangementId,
            IntercompanyAccountingArrangementActionDto.Approve, new(Guid.NewGuid(), submitted.Version));
        Task? amendment = null;
        try
        {
            await validated.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1, fixture.Native.ActiveLeases);
            amendment = fixture.Native.ReplaceAsync(fixture.Legal, fixture.NextLegal(termination: true));
            Assert.False(amendment.IsCompleted);
        }
        finally { finish.TrySetResult(); }
        var approved = await approval;
        if (amendment is not null) await amendment;
        Assert.Equal(IntercompanyAccountingArrangementStatusDto.Approved, approved.Status);
        Assert.Equal(0, fixture.Native.ActiveLeases);
    }

    [Fact]
    public async Task FileStoreReopensCanonicalPins_AndTamperingFailsClosed()
    {
        var directory = Path.Combine(Path.GetTempPath(), "meridian-arrangement-" + Guid.NewGuid().ToString("N"));
        try
        {
            var fixture = new ArrangementFixture(new FileIntercompanyAccountingArrangementStore(directory));
            var request = fixture.Request();
            var approved = await ApprovedAsync(fixture.Service, request);
            var reopened = fixture.WithStore(new FileIntercompanyAccountingArrangementStore(directory));
            var retained = (await reopened.GetAsync("tenant-a", approved.ArrangementId))!;
            Assert.Equal(approved.RevisionId, retained.RevisionId);
            Assert.Equal(approved.CanonicalReference, retained.Arrangement.CanonicalReference);
            Assert.Equal(1, (await reopened.CreateAsync("tenant-a", "owner", request)).Version);
            var retainedFile = Directory.GetFiles(directory, "*.json", SearchOption.AllDirectories).Single();
            var json = await File.ReadAllTextAsync(retainedFile);
            await File.WriteAllTextAsync(retainedFile, json.Replace(LegalHash, new string('e', 64), StringComparison.Ordinal));
            await Assert.ThrowsAsync<InvalidDataException>(() => reopened.GetAsync("tenant-a", approved.ArrangementId));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task ConcurrentFileWritersKeepHistory_AndOneStaleMutationRefuses()
    {
        var directory = Path.Combine(Path.GetTempPath(), "meridian-arrangement-" + Guid.NewGuid().ToString("N"));
        try
        {
            var fixture = new ArrangementFixture(new FileIntercompanyAccountingArrangementStore(directory));
            var second = fixture.WithStore(new FileIntercompanyAccountingArrangementStore(directory));
            var draft = await fixture.Service.CreateAsync("tenant-a", "owner", fixture.Request());
            async Task<bool> Submit(IntercompanyAccountingArrangementService service)
            {
                try
                {
                    await service.ApplyActionAsync("tenant-a", "owner", draft.ArrangementId,
                        IntercompanyAccountingArrangementActionDto.Submit, new(Guid.NewGuid(), draft.Version));
                    return true;
                }
                catch (InvalidOperationException) { return false; }
            }
            var results = await Task.WhenAll(Submit(fixture.Service), Submit(second));
            Assert.Single(results.Where(item => item));
            Assert.Equal(2, (await fixture.Service.GetHistoryAsync("tenant-a", draft.ArrangementId))!.Events.Count);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    private static ReviseIntercompanyAccountingArrangementRequest Revision(IntercompanyAccountingArrangementDto approved,
        DateTimeOffset effectiveFrom, string reference) => new(Guid.NewGuid(), approved.Version, reference, approved.TemplateKind,
            approved.CanonicalAgreementVersion, approved.CanonicalPayloadHash, effectiveFrom, null, approved.PartyMappings, "Revise accounting mappings");

    private static async Task<IntercompanyAccountingArrangementDto> ApprovedAsync(IntercompanyAccountingArrangementService service,
        CreateIntercompanyAccountingArrangementRequest request)
    {
        var draft = await service.CreateAsync("tenant-a", "owner", request);
        var submitted = await service.ApplyActionAsync("tenant-a", "owner", draft.ArrangementId,
            IntercompanyAccountingArrangementActionDto.Submit, new(Guid.NewGuid(), draft.Version));
        return await service.ApplyActionAsync("tenant-a", "owner", draft.ArrangementId,
            IntercompanyAccountingArrangementActionDto.Approve, new(Guid.NewGuid(), submitted.Version));
    }

    private sealed class ArrangementFixture
    {
        public IIntercompanyAccountingArrangementStore Store { get; }
        public NativeStore Native { get; }
        public Scope Scope { get; } = new();
        public IntercompanyAgreementRevisionDto Legal { get; }
        public IntercompanyAccountingArrangementLegalAuthority LegalAuthority { get; }
        public IntercompanyAccountingArrangementService Service { get; }

        public ArrangementFixture(IIntercompanyAccountingArrangementStore? store = null)
        {
            Store = store ?? new InMemoryIntercompanyAccountingArrangementStore();
            var terms = new IntercompanyAgreementDraftDto("LEGAL-ONE", "Native legal loan", Guid.NewGuid(), "Lender",
                Guid.NewGuid(), "Borrower", IntercompanyAgreementTypeDto.Loan, "USD", DateOnly.FromDateTime(Start.UtcDateTime),
                Terms: new(ScheduleDescription: "Quarterly", SettlementDays: 14), Evidence: [new("evidence-vault:legal", "artifact", LegalHash)]);
            Legal = new(Guid.NewGuid(), 1, null, null, IntercompanyAgreementActionDto.Activate, terms, Guid.NewGuid(), LegalHash,
                ["legal-author"], "legal-author", "independent-reviewer", Start, "Native legal approval");
            Native = new(Legal);
            LegalAuthority = new(Native);
            Service = WithStore(Store);
        }

        public IntercompanyAccountingArrangementService WithStore(IIntercompanyAccountingArrangementStore store)
            => new(store, Scope, LegalAuthority, new Clock());
        public CreateIntercompanyAccountingArrangementRequest Request() => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "profile-one", IntercompanyAccountingTemplateKindDto.Loan, Legal.AgreementId, Legal.AgreementVersion, Legal.PayloadHash,
            Start, null, [new(Legal.Terms.PartyOneId, Guid.NewGuid(), "ar", "ap", "cash"),
                new(Legal.Terms.PartyTwoId, Guid.NewGuid(), "ar", "ap", "cash")]);
        public IntercompanyAgreementRevisionDto NextLegal(bool termination) => Legal with
        {
            AgreementVersion = 2, PredecessorAgreementVersion = 1, BaseAgreementVersion = 1,
            Action = termination ? IntercompanyAgreementActionDto.Terminate : IntercompanyAgreementActionDto.Amend,
            Terms = Legal.Terms with { EffectiveFrom = termination ? Legal.Terms.EffectiveFrom : DateOnly.FromDateTime(Cutoff.UtcDateTime),
                Action = termination ? IntercompanyAgreementActionDto.Terminate : IntercompanyAgreementActionDto.Amend,
                TerminationEffectiveDate = termination ? DateOnly.FromDateTime(Cutoff.UtcDateTime) : null },
            ProposalId = Guid.NewGuid(), PayloadHash = new string('b', 64), ApprovedAtUtc = Cutoff
        };
    }

    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => Cutoff.AddMonths(2); }

    private sealed class Scope : IIntercompanyAccountingArrangementScopeAuthority
    {
        public Func<ResolvedIntercompanyAccountingArrangementDto, bool, Task>? DuringValidation { get; set; }
        public Task<IIntercompanyAccountingArrangementScopeLease> AcquireLeaseAsync(string tenantId,
            ResolvedIntercompanyAccountingArrangementDto arrangement, CancellationToken ct = default)
            => Task.FromResult<IIntercompanyAccountingArrangementScopeLease>(new ScopeLease());
        public Task<IIntercompanyAccountingArrangementScopeLease> AcquireLeasesAsync(string tenantId,
            IReadOnlyList<ResolvedIntercompanyAccountingArrangementDto> arrangements, CancellationToken ct = default)
            => Task.FromResult<IIntercompanyAccountingArrangementScopeLease>(new ScopeLease());
        public Task ValidateAsync(string tenantId, ResolvedIntercompanyAccountingArrangementDto arrangement,
            bool requireActive, CancellationToken ct = default) => DuringValidation?.Invoke(arrangement, requireActive) ?? Task.CompletedTask;
        public Task<IReadOnlyList<IntercompanyEntityLifecycleReferenceDto>> GetLifecycleEvidenceAsync(string tenantId,
            ResolvedIntercompanyAccountingArrangementDto arrangement, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<IntercompanyEntityLifecycleReferenceDto>>([new(arrangement.PartyOneEntityId, 1, LegalHash),
                new(arrangement.PartyTwoEntityId, 1, new string('b', 64))]);
    }
    private sealed class ScopeLease : IIntercompanyAccountingArrangementScopeLease
    {
        public LegalEntityReadAuthorityProof? StructureAuthorityProof => null;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class NativeStore(IntercompanyAgreementRevisionDto initial) : IIntercompanyAgreementStore
    {
        private readonly SemaphoreSlim _authority = new(1, 1);
        private IReadOnlyList<IntercompanyAgreementRevisionDto> _revisions = [initial];
        private int _activeLeases;
        public int ActiveLeases => Volatile.Read(ref _activeLeases);
        public int Commands { get; private set; }
        private IntercompanyAgreementStoreState State => new(initial.AgreementId, _revisions.Count, [], _revisions, [], []);
        public Task<T> ExecuteAsync<T>(string tenantId, Guid agreementId,
            Func<IIntercompanyAgreementStoreTransaction, CancellationToken, Task<T>> operation, CancellationToken ct = default)
        { Commands++; throw new InvalidOperationException("Mapping configuration cannot mutate canonical legal authority."); }
        public Task<IntercompanyAgreementStoreState?> GetAsync(string tenantId, Guid agreementId, CancellationToken ct = default)
            => Task.FromResult<IntercompanyAgreementStoreState?>(tenantId == "tenant-a" && agreementId == initial.AgreementId ? State : null);
        public Task<IReadOnlyList<IntercompanyAgreementStoreState>> ListAsync(string tenantId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<IntercompanyAgreementStoreState>>(tenantId == "tenant-a" ? [State] : []);
        public async Task<IIntercompanyAgreementPostingLease> AcquirePostingLeaseAsync(string tenantId,
            IReadOnlyList<Guid> agreementIds, CancellationToken ct = default)
        {
            await _authority.WaitAsync(ct);
            Interlocked.Increment(ref _activeLeases);
            return new NativeLease(this, tenantId == "tenant-a" && agreementIds.Contains(initial.AgreementId) ? [State] : []);
        }
        public async Task ReplaceAsync(params IntercompanyAgreementRevisionDto[] revisions)
        {
            await _authority.WaitAsync();
            try { _revisions = revisions; }
            finally { _authority.Release(); }
        }
        private sealed class NativeLease(NativeStore owner, IReadOnlyList<IntercompanyAgreementStoreState> agreements)
            : IIntercompanyAgreementPostingLease
        {
            private int _disposed;
            public IReadOnlyList<IntercompanyAgreementStoreState> Agreements => agreements;
            public Task<LegalEntitySummaryDto?> GetLegalEntityAsync(Guid entityId, CancellationToken ct = default)
                => throw new NotSupportedException("The mapping adapter reads canonical terms only.");
            public ValueTask DisposeAsync()
            {
                if (Interlocked.Exchange(ref _disposed, 1) == 0)
                { Interlocked.Decrement(ref owner._activeLeases); owner._authority.Release(); }
                return ValueTask.CompletedTask;
            }
        }
    }
}
