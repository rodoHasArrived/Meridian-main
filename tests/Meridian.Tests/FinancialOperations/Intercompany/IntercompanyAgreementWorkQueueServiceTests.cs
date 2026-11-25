using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Operations;
using Meridian.Contracts.Services;
using Meridian.Contracts.Tenancy;
using Meridian.FinancialOperations.Intercompany;
using Meridian.Storage.Ledger;
using Meridian.Storage.Operations;
using Moq;

namespace Meridian.Tests.FinancialOperations.Intercompany;

public sealed class IntercompanyAgreementWorkQueueServiceTests
{
    [Fact]
    public async Task ReadShowsUntrackedCandidateWithoutAppendingHistory()
    {
        using var fixture = new IntercompanyAgreementWorkQueueFixture();
        var queue = await fixture.GetAsync();
        var item = Assert.Single(queue.Cases);
        Assert.False(item.IsTracked);
        Assert.Null(item.FirstSeenAtUtc);
        Assert.Null(item.AgeDays);
        Assert.Equal(0, item.Version);
        Assert.Equal(fixture.Book.LedgerBookId, queue.LedgerBookId);
        Assert.Empty(await fixture.History.ReadAsync(new() { CaseType = "intercompany-agreement-work" }));
        fixture.Journals.Verify(store => store.AppendAsync(It.IsAny<LedgerJournalEntryWrite>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RestartAndNewObservationDatePreserveIdentityOwnerAndFirstSeen()
    {
        using var fixture = new IntercompanyAgreementWorkQueueFixture();
        var tracked = await fixture.TrackAsync();
        var assigned = await fixture.UpdateAsync(tracked, IntercompanyAgreementWorkItemActionDto.AssignOwner, owner: "operator-two");
        fixture.Clock.Now = fixture.Clock.Now.AddDays(3);
        fixture.Scope = fixture.Scope with { AsOf = fixture.Scope.AsOf.AddDays(2), ExpiryHorizonDays = 60 };
        fixture.SourceTag = "new-posted-balances";
        var restarted = fixture.CreateService(new FileOperationalCaseHistoryStore(fixture.Root));
        var queue = await restarted.GetAsync(fixture.Context, fixture.Company, fixture.Scope);
        var current = Assert.Single(queue.Cases);
        Assert.Equal(tracked.CaseId, current.CaseId);
        Assert.Equal(tracked.FirstSeenAtUtc, current.FirstSeenAtUtc);
        Assert.Equal("operator-two", current.Owner);
        Assert.Equal(3, current.AgeDays);
        Assert.Equal(assigned.Version, current.Version);
        Assert.Equal(2, current.History.Count);
        Assert.Contains(current.History[0].SourceVersions, source => source.SourceKind == "approved-legal-revision"
            && source.ContentHash == fixture.Detail.ApplicableRevision!.PayloadHash);
    }

    [Fact]
    public async Task ExactCommandRetryReturnsOriginalReceiptAfterRestartAndSourceChange()
    {
        using var fixture = new IntercompanyAgreementWorkQueueFixture();
        var queue = await fixture.GetAsync();
        var request = new IntercompanyAgreementWorkQueueObserveRequestDto(Guid.NewGuid(), queue.Cases[0].CaseId,
            queue.SourceFingerprint, "Track reviewed obligation");
        var first = await fixture.Service.ObserveAsync(fixture.Context, fixture.Company, fixture.Scope, request);
        fixture.Clock.Now = fixture.Clock.Now.AddHours(4);
        fixture.SourceTag = "subsequent-source";
        var retry = await fixture.CreateService(new FileOperationalCaseHistoryStore(fixture.Root))
            .ObserveAsync(fixture.Context, fixture.Company, fixture.Scope, request);
        Assert.Equal(first.Version, retry.Version);
        Assert.Equal(first.RecordHash, retry.RecordHash);
        Assert.Equal(first.FirstSeenAtUtc, retry.FirstSeenAtUtc);
        Assert.Single(retry.History);
        await Assert.ThrowsAsync<IntercompanyAgreementWorkQueueConflictException>(() =>
            fixture.Service.ObserveAsync(fixture.Context, fixture.Company, fixture.Scope,
                request with { Reason = "Different intent under reused command" }));
    }

    [Fact]
    public async Task AssignmentReviewResolutionAndReopeningRetainEvidenceAndActors()
    {
        using var fixture = new IntercompanyAgreementWorkQueueFixture();
        var item = await fixture.TrackAsync();
        item = await fixture.UpdateAsync(item, IntercompanyAgreementWorkItemActionDto.AssignOwner, owner: "case-owner");
        item = await fixture.UpdateAsync(item, IntercompanyAgreementWorkItemActionDto.AddNote, note: "Renewal proposal is being prepared.");
        item = await fixture.UpdateAsync(item, IntercompanyAgreementWorkItemActionDto.AwaitReview);
        Assert.Equal(IntercompanyAgreementWorkItemStateDto.AwaitingReview, item.State);
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.UpdateAsync(item, IntercompanyAgreementWorkItemActionDto.Resolve));
        item = await fixture.UpdateAsync(item, IntercompanyAgreementWorkItemActionDto.Resolve,
            evidence: ["synthetic:reviewed-renewal-evidence"]);
        Assert.Equal(IntercompanyAgreementWorkItemStateDto.Resolved, item.State);
        Assert.Equal("synthetic:reviewed-renewal-evidence", Assert.Single(item.History[^1].EvidenceReferences));
        Assert.Equal(fixture.Context.Actor, item.History[^1].Actor);
        item = await fixture.UpdateAsync(item, IntercompanyAgreementWorkItemActionDto.Reopen,
            note: "Follow-up is still required.");
        Assert.Equal(IntercompanyAgreementWorkItemStateDto.Reopened, item.State);
        Assert.Equal("case-owner", item.Owner);
        Assert.Equal(6, item.History.Count);
        fixture.Journals.Verify(store => store.AppendAsync(It.IsAny<LedgerJournalEntryWrite>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task OwnerCanBeClearedAndEvidenceCanBeAddedWithoutANote()
    {
        using var fixture = new IntercompanyAgreementWorkQueueFixture();
        var item = await fixture.TrackAsync();
        item = await fixture.UpdateAsync(item, IntercompanyAgreementWorkItemActionDto.AssignOwner, owner: "owner");
        item = await fixture.UpdateAsync(item, IntercompanyAgreementWorkItemActionDto.AssignOwner, owner: "   ");
        Assert.Null(item.Owner);
        Assert.Null(item.History[^1].Owner);
        item = await fixture.UpdateAsync(item, IntercompanyAgreementWorkItemActionDto.AddNote, evidence: ["synthetic:support"]);
        Assert.Equal("synthetic:support", Assert.Single(item.History[^1].EvidenceReferences));
        Assert.Null(item.History[^1].Note);
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.UpdateAsync(item, IntercompanyAgreementWorkItemActionDto.AddNote));
    }

    [Fact]
    public async Task AgeCountsUtcCalendarBoundariesRatherThanFullElapsedDays()
    {
        using var fixture = new IntercompanyAgreementWorkQueueFixture();
        fixture.Clock.Now = new(2026, 11, 23, 23, 59, 0, TimeSpan.Zero);
        await fixture.TrackAsync();
        fixture.Clock.Now = new(2026, 11, 24, 0, 1, 0, TimeSpan.Zero);
        Assert.Equal(1, Assert.Single((await fixture.GetAsync()).Cases).AgeDays);
    }

    [Fact]
    public async Task AbsentFindingRemainsOpenUntilExplicitEvidencedResolution()
    {
        using var fixture = new IntercompanyAgreementWorkQueueFixture();
        var item = await fixture.TrackAsync();
        fixture.Findings = [];
        fixture.SourceTag = "finding-not-in-observation";
        var absent = Assert.Single((await fixture.GetAsync()).Cases);
        Assert.False(absent.IsCurrentlyObserved);
        Assert.Equal(IntercompanyAgreementWorkItemStateDto.Open, absent.State);
        Assert.Equal(item.FirstSeenAtUtc, absent.FirstSeenAtUtc);
        var resolved = await fixture.UpdateAsync(absent, IntercompanyAgreementWorkItemActionDto.Resolve,
            evidence: ["synthetic:operator-reviewed-disposition"]);
        Assert.Equal(IntercompanyAgreementWorkItemStateDto.Resolved, resolved.State);
        Assert.False(resolved.IsCurrentlyObserved);
        Assert.Equal(item.LastObservedAtUtc, resolved.LastObservedAtUtc);
    }

    [Fact]
    public async Task RefreshedObservationDoesNotReopenResolvedCase()
    {
        using var fixture = new IntercompanyAgreementWorkQueueFixture();
        var item = await fixture.TrackAsync();
        item = await fixture.UpdateAsync(item, IntercompanyAgreementWorkItemActionDto.Resolve, evidence: ["synthetic:evidence"]);
        var queue = await fixture.GetAsync();
        var observed = await fixture.Service.ObserveAsync(fixture.Context, fixture.Company, fixture.Scope,
            new(Guid.NewGuid(), item.CaseId, queue.SourceFingerprint, "Observed again", item.Version, item.RecordHash));
        Assert.Equal(IntercompanyAgreementWorkItemStateDto.Resolved, observed.State);
        Assert.Equal(item.FirstSeenAtUtc, observed.FirstSeenAtUtc);
    }

    [Fact]
    public async Task DifferentLegalBoundaryCreatesSeparateCaseWithoutResettingOldCase()
    {
        using var fixture = new IntercompanyAgreementWorkQueueFixture();
        var old = await fixture.TrackAsync();
        fixture.Findings = [fixture.Findings[0] with { DueDate = fixture.Findings[0].DueDate!.Value.AddDays(90) }];
        var queue = await fixture.GetAsync();
        Assert.Equal(2, queue.Cases.Count);
        Assert.Contains(queue.Cases, item => item.CaseId == old.CaseId && item.IsTracked && !item.IsCurrentlyObserved);
        Assert.Contains(queue.Cases, item => item.CaseId != old.CaseId && !item.IsTracked && item.IsCurrentlyObserved);
    }

    [Fact]
    public async Task ExpiryBecomingExpiredExposureKeepsSameBoundaryIdentity()
    {
        using var fixture = new IntercompanyAgreementWorkQueueFixture();
        var old = await fixture.TrackAsync();
        fixture.Findings = [fixture.Findings[0] with
        { Code = "intercompany.agreement.expired-exposure", Message = "The loan is expired with retained posted exposure." }];
        fixture.Scope = fixture.Scope with { AsOf = old.DueDate!.Value.AddDays(4) };
        fixture.SourceTag = "expired";
        var item = Assert.Single((await fixture.GetAsync()).Cases);
        Assert.Equal(old.CaseId, item.CaseId);
        Assert.Equal(4, item.DaysOverdue);
        Assert.Equal(old.FirstSeenAtUtc, item.FirstSeenAtUtc);
        Assert.Equal("intercompany.agreement.expired-exposure", item.FindingCode);
    }

    [Theory]
    [InlineData("Stale")]
    [InlineData("Partial")]
    [InlineData("Unavailable")]
    public async Task NoncurrentObservationCannotAppendOrResolve(string status)
    {
        using var fixture = new IntercompanyAgreementWorkQueueFixture();
        var item = await fixture.TrackAsync();
        var queue = await fixture.GetAsync();
        fixture.Status = status;
        fixture.IsComplete = false;
        await Assert.ThrowsAsync<IntercompanyAgreementWorkQueueUnavailableException>(() =>
            fixture.Service.UpdateAsync(fixture.Context, fixture.Company, fixture.Scope,
                new(Guid.NewGuid(), item.CaseId, IntercompanyAgreementWorkItemActionDto.Resolve,
                    item.Version, item.RecordHash!, queue.SourceFingerprint, "Review", EvidenceReferences: ["synthetic:evidence"])));
        Assert.Single(await fixture.History.ReadAsync(new() { CaseType = "intercompany-agreement-work" }));
        if (status == "Unavailable")
            Assert.Empty((await fixture.GetAsync()).Cases);
    }

    [Fact]
    public async Task StaleSourceOrCaseHashCannotAppend()
    {
        using var fixture = new IntercompanyAgreementWorkQueueFixture();
        var item = await fixture.TrackAsync();
        var queue = await fixture.GetAsync();
        var request = new IntercompanyAgreementWorkQueueUpdateRequestDto(Guid.NewGuid(), item.CaseId,
            IntercompanyAgreementWorkItemActionDto.AssignOwner, item.Version, item.RecordHash!, queue.SourceFingerprint,
            "Assign", Owner: "owner");
        fixture.SourceTag = "changed-after-read";
        await Assert.ThrowsAsync<IntercompanyAgreementWorkQueueConflictException>(() =>
            fixture.Service.UpdateAsync(fixture.Context, fixture.Company, fixture.Scope, request));
        queue = await fixture.GetAsync();
        await Assert.ThrowsAsync<IntercompanyAgreementWorkQueueConflictException>(() =>
            fixture.Service.UpdateAsync(fixture.Context, fixture.Company, fixture.Scope,
                request with { ExpectedSourceFingerprint = queue.SourceFingerprint, ExpectedRecordHash = new string('a', 64) }));
    }

    [Fact]
    public async Task ConcurrentWritersAllowOnlyOneCompareAndAppend()
    {
        using var fixture = new IntercompanyAgreementWorkQueueFixture();
        var item = await fixture.TrackAsync();
        var queue = await fixture.GetAsync();
        async Task<bool> Write(string owner)
        {
            try
            {
                await fixture.CreateService(new FileOperationalCaseHistoryStore(fixture.Root)).UpdateAsync(
                    fixture.Context, fixture.Company, fixture.Scope, new(Guid.NewGuid(), item.CaseId,
                        IntercompanyAgreementWorkItemActionDto.AssignOwner, item.Version, item.RecordHash!,
                        queue.SourceFingerprint, "Concurrent assignment", Owner: owner));
                return true;
            }
            catch (IntercompanyAgreementWorkQueueConflictException) { return false; }
        }
        var results = await Task.WhenAll(Write("first-owner"), Write("second-owner"));
        Assert.Equal(1, results.Count(value => value));
        Assert.Equal(2, (await fixture.History.ReadAsync(new() { CaseType = "intercompany-agreement-work" })).Count);
    }

    [Fact]
    public async Task ForeignScopeAndRevokedRetainedPartyAccessDoNotDiscloseCases()
    {
        using var fixture = new IntercompanyAgreementWorkQueueFixture();
        await fixture.TrackAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Service.GetAsync(fixture.Context, "foreign-company", fixture.Scope));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Service.GetAsync(new("foreign-tenant", "operator"), fixture.Company, fixture.Scope));
        fixture.Findings = [];
        fixture.Access.Setup(authority => authority.CanAccessEntitiesAsync(It.IsAny<IntercompanyAgreementCallContext>(),
            It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IntercompanyAgreementCallContext _, IReadOnlyList<Guid> entities, CancellationToken _) => !entities.Contains(fixture.CounterpartyId));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.GetAsync());
    }

    [Fact]
    public async Task CorruptRetainedHistoryIsUnavailableAndNeverAnEmptyQueue()
    {
        using var fixture = new IntercompanyAgreementWorkQueueFixture();
        await fixture.TrackAsync();
        await File.AppendAllTextAsync(fixture.History.HistoryPath, "invalid-history\n");
        var error = await Assert.ThrowsAsync<IntercompanyAgreementWorkQueueUnavailableException>(() => fixture.GetAsync());
        Assert.DoesNotContain(fixture.Root, error.Message);
        fixture.Status = "Unavailable";
        fixture.IsComplete = false;
        var suppressed = await fixture.GetAsync();
        Assert.Empty(suppressed.Cases); // No old details read when current authority cannot be established.
        Assert.Equal("Unavailable", suppressed.Status);
    }

    [Fact]
    public async Task GuaranteeCapIsDisclosedWhileCurrentExposureRemainsUnknown()
    {
        using var fixture = new IntercompanyAgreementWorkQueueFixture();
        var old = fixture.Detail;
        var terms = old.ApplicableRevision!.Terms with
        {
            AgreementType = IntercompanyAgreementTypeDto.Guarantee,
            PartyOneRole = "Guarantor",
            PartyTwoRole = "Obligor",
            Terms = new() { StructuredTerms = new(Guarantee: new("Synthetic beneficiary", 500_000.25m)) }
        };
        var revision = old.ApplicableRevision with { Terms = terms };
        fixture.Detail = old with { AgreementType = IntercompanyAgreementTypeDto.Guarantee, ApplicableRevision = revision, Revisions = [revision] };
        fixture.Guarantees = [fixture.Detail];
        var guarantee = Assert.Single((await fixture.GetAsync()).Guarantees);
        Assert.Equal(500_000.25m, guarantee.LimitAmount);
        Assert.Equal("Guarantor", guarantee.PartyOneRole);
        Assert.Null(guarantee.CurrentExposure);
        Assert.Equal("Unavailable", guarantee.ExposureStatus);
        Assert.Contains("not current exposure", guarantee.ExposureUnavailableReason);
    }

    [Theory]
    [InlineData(IntercompanyAgreementStatusDto.Expired)]
    [InlineData(IntercompanyAgreementStatusDto.Terminated)]
    [InlineData(IntercompanyAgreementStatusDto.Active)]
    public async Task GuaranteeUsesExactAsOfTermsForExpiryTerminationAndReactivation(IntercompanyAgreementStatusDto status)
    {
        using var fixture = new IntercompanyAgreementWorkQueueFixture();
        var first = fixture.Detail.ApplicableRevision!;
        var terms = first.Terms with
        {
            AgreementType = IntercompanyAgreementTypeDto.Guarantee,
            EffectiveTo = fixture.Scope.AsOf.AddDays(-3),
            Terms = new() { StructuredTerms = new(Guarantee: new("Historical beneficiary", 100m)) }
        };
        first = first with { Terms = terms };
        var applicable = first;
        var revisions = new List<IntercompanyAgreementRevisionDto> { first };
        if (status == IntercompanyAgreementStatusDto.Active)
        {
            // A new approved effective window after expiry reactivates coverage; approval order
            // must not make the old cap or expired window current again.
            applicable = first with
            {
                AgreementVersion = 2,
                Action = IntercompanyAgreementActionDto.Amend,
                PayloadHash = IntercompanyAgreementWorkQueueFixture.Hash("renewal"),
                Terms = terms with
                {
                    EffectiveFrom = fixture.Scope.AsOf.AddDays(-1),
                    EffectiveTo = fixture.Scope.AsOf.AddDays(30),
                    Terms = new() { StructuredTerms = new(Guarantee: new("Renewed beneficiary", 250m)) }
                }
            };
            revisions.Insert(0, applicable); // Deliberately not in approval/effective order.
        }
        else if (status == IntercompanyAgreementStatusDto.Terminated)
            revisions.Add(first with
            {
                AgreementVersion = 2,
                Action = IntercompanyAgreementActionDto.Terminate,
                Terms = terms with { TerminationEffectiveDate = fixture.Scope.AsOf.AddDays(-2) }
            });
        fixture.Detail = fixture.Detail with
        {
            AgreementType = IntercompanyAgreementTypeDto.Guarantee,
            EffectiveStatus = status,
            ApplicableRevision = applicable,
            Revisions = revisions
        };
        fixture.Guarantees = [fixture.Detail];
        var guarantee = Assert.Single((await fixture.GetAsync()).Guarantees);
        Assert.Equal(applicable.AgreementVersion, guarantee.AgreementVersion);
        Assert.Equal(applicable.PayloadHash, guarantee.PayloadHash);
        Assert.Equal(applicable.Terms.EffectiveTo, guarantee.EffectiveTo);
        Assert.Equal(status == IntercompanyAgreementStatusDto.Active ? 250m : 100m, guarantee.LimitAmount);
        Assert.Equal(status == IntercompanyAgreementStatusDto.Terminated ? fixture.Scope.AsOf.AddDays(-2) : (DateOnly?)null,
            guarantee.TerminationEffectiveDate);
        Assert.Null(guarantee.CurrentExposure);
    }

    [Fact]
    public async Task ApprovedFutureTerminationDoesNotBecomeAnEffectiveHistoricalTermination()
    {
        using var fixture = new IntercompanyAgreementWorkQueueFixture();
        var original = fixture.Detail.ApplicableRevision!;
        var terms = original.Terms with
        {
            AgreementType = IntercompanyAgreementTypeDto.Guarantee,
            Terms = new() { StructuredTerms = new(Guarantee: new("Synthetic beneficiary", 100m)) }
        };
        var revision = original with { Terms = terms };
        var termination = revision with
        {
            AgreementVersion = 2,
            Action = IntercompanyAgreementActionDto.Terminate,
            Terms = terms with { TerminationEffectiveDate = fixture.Scope.AsOf.AddDays(5) }
        };
        fixture.Detail = fixture.Detail with
        {
            AgreementType = IntercompanyAgreementTypeDto.Guarantee,
            ApplicableRevision = revision,
            Revisions = [revision, termination]
        };
        fixture.Guarantees = [fixture.Detail];
        var guarantee = Assert.Single((await fixture.GetAsync()).Guarantees);
        Assert.Equal(IntercompanyAgreementStatusDto.Active, guarantee.EffectiveStatus);
        Assert.Null(guarantee.TerminationEffectiveDate);
        Assert.Equal(1, guarantee.AgreementVersion);
    }
}

internal sealed class IntercompanyAgreementWorkQueueFixture : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "meridian-ic-work-tests", Guid.NewGuid().ToString("N"));
    public string Fund { get; } = "synthetic-fund";
    public string Company { get; } = "company-a";
    public IntercompanyAgreementCallContext Context { get; } = new("tenant-a", "operator-one");
    public Guid EntityId { get; } = Guid.NewGuid();
    public Guid CounterpartyId { get; } = Guid.NewGuid();
    public Guid AgreementId { get; } = Guid.NewGuid();
    public LedgerBookRecord Book { get; }
    public IntercompanyObligationMonitorRequestDto Scope { get; set; }
    public Dictionary<Guid, IntercompanyAgreementDetailDto> Details { get; } = [];
    public IntercompanyAgreementDetailDto Detail { get => Details[AgreementId]; set => Details[AgreementId] = value; }
    public IReadOnlyList<IntercompanyObligationFindingDto> Findings { get; set; }
    public IReadOnlyList<IntercompanyAgreementDetailDto> Guarantees { get; set; } = [];
    public string Status { get; set; } = "Current";
    public bool IsComplete { get; set; } = true;
    public string SourceTag { get; set; } = "source-v1";
    public Mock<IIntercompanyObligationMonitorService> Monitor { get; } = new();
    public Mock<IIntercompanyAgreementService> Agreements { get; } = new();
    public Mock<ILedgerJournalStore> Journals { get; } = new();
    public Mock<IFundProfileTenancyRegistry> Ownership { get; } = new();
    public Mock<IIntercompanyAgreementAccessAuthority> Access { get; } = new();
    public TestClock Clock { get; } = new();
    public FileOperationalCaseHistoryStore History { get; }
    public IntercompanyAgreementWorkQueueService Service { get; }

    public IntercompanyAgreementWorkQueueFixture()
    {
        Book = new(Guid.NewGuid(), Fund, EntityId, FundStructureNodeKindDto.Entity, "Synthetic source", "USD", Clock.Now, Clock.Now);
        Scope = new(Fund, Book.LedgerBookId, new(2026, 11, 23));
        var terms = new IntercompanyAgreementDraftDto("SYN-LOAN", "Synthetic loan", EntityId, "Lender",
            CounterpartyId, "Borrower", IntercompanyAgreementTypeDto.Loan, "USD", new(2026, 1, 1), Scope.AsOf.AddDays(10));
        var revision = new IntercompanyAgreementRevisionDto(AgreementId, 1, null, null, IntercompanyAgreementActionDto.Activate,
            terms, Guid.NewGuid(), Hash("legal-v1"), ["author"], "maker", "reviewer", Clock.Now.AddDays(-30), "Synthetic approved agreement");
        Detail = new(AgreementId, 4, terms.Code, terms.Name, EntityId, "Lender", CounterpartyId, "Borrower", terms.AgreementType,
            "USD", Scope.AsOf, IntercompanyAgreementStatusDto.Active, 1, revision, [], [revision], [],
            new(false, false, false, false, false, false, false));
        Findings = [new("intercompany.agreement.expiring", "Warning", "Synthetic legal coverage is ending.",
            AgreementId: AgreementId, AgreementVersion: 1) { DueDate = terms.EffectiveTo }];
        Journals.Setup(store => store.GetLedgerBookAsync(Book.LedgerBookId, It.IsAny<CancellationToken>())).ReturnsAsync(Book);
        Ownership.Setup(registry => registry.ResolveAsync(Fund, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FundProfileOwnership(Fund, Context.TenantId, Company));
        Access.Setup(authority => authority.CanAccessEntitiesAsync(It.IsAny<IntercompanyAgreementCallContext>(),
            It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        Agreements.Setup(service => service.GetAsync(It.IsAny<IntercompanyAgreementCallContext>(), It.IsAny<Guid>(),
            It.IsAny<DateOnly?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IntercompanyAgreementCallContext _, Guid id, DateOnly? asOf, DateTimeOffset? _, CancellationToken _) =>
                Details.TryGetValue(id, out var detail) ? detail with { AsOf = asOf ?? Scope.AsOf } : null);
        Monitor.Setup(service => service.EvaluateAsync(It.IsAny<IntercompanyAgreementCallContext>(), It.IsAny<string>(),
            It.IsAny<IntercompanyObligationMonitorRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IntercompanyAgreementCallContext context, string company, IntercompanyObligationMonitorRequestDto scope,
                CancellationToken _) => Observation(context, company, scope));
        History = new(Root);
        Service = CreateService(History);
    }

    public IntercompanyAgreementWorkQueueService CreateService(IOperationalCaseHistoryStore store) =>
        new(Monitor.Object, store, Agreements.Object, Journals.Object, Ownership.Object, Access.Object, Clock);
    public IntercompanyObligationMonitorDto Observation(IntercompanyAgreementCallContext context, string company,
        IntercompanyObligationMonitorRequestDto scope) => new(scope.AsOf, scope.ExpiryHorizonDays, Clock.Now, Status, IsComplete,
            Hash(SourceTag), [], Findings, Status == "Unavailable" ? null : new(context.TenantId, company, Fund,
                Book.LedgerBookId, scope.AsOf, true, [], [], Hash(SourceTag)), Guarantees);
    public Task<IntercompanyAgreementWorkQueueDto> GetAsync() => Service.GetAsync(Context, Company, Scope);
    public async Task<IntercompanyAgreementWorkItemDto> TrackAsync()
    {
        var queue = await GetAsync();
        return await Service.ObserveAsync(Context, Company, Scope, new(Guid.NewGuid(), queue.Cases.First(item => !item.IsTracked).CaseId,
            queue.SourceFingerprint, "Track synthetic obligation"));
    }
    public async Task<IntercompanyAgreementWorkItemDto> UpdateAsync(IntercompanyAgreementWorkItemDto item,
        IntercompanyAgreementWorkItemActionDto action, string? owner = null, string? note = null, IReadOnlyList<string>? evidence = null)
    {
        var queue = await GetAsync();
        return await Service.UpdateAsync(Context, Company, Scope, new(Guid.NewGuid(), item.CaseId, action,
            item.Version, item.RecordHash!, queue.SourceFingerprint, "Synthetic operator action", owner, note, evidence));
    }
    public static string Hash(string value) => Sha256Digest.ComputeUtf8(value);
    public void Dispose() { if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true); }
    internal sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 23, 16, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
