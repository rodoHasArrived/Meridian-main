using FluentAssertions;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Storage.AssetOperations;

namespace Meridian.Tests.AssetOperations;

public sealed class InMemoryInstrumentPositionProjectionStoreSlice3Tests
{
    private static readonly Guid SecurityId = Guid.Parse("b3000000-aaaa-4000-8000-000000000001");
    private static readonly Guid RoleId = Guid.Parse("b3000000-aaaa-4000-8000-000000000002");
    private static readonly Guid PositionId = Guid.Parse("b3000000-aaaa-4000-8000-000000000003");
    private static readonly Guid LedgerBookId = Guid.Parse("b3000000-aaaa-4000-8000-000000000004");
    private static readonly Guid OtherLedgerBookId = Guid.Parse("b3000000-aaaa-4000-8000-000000000005");
    private static readonly DateOnly EffectiveFrom = new(2026, 1, 1);

    private static AssetOperationsWriteApprovalDto Approval { get; } = new(
        "controller@meridian.local",
        "approval-mbs-position-2026",
        "Approved retained MBS position projection.",
        DateTimeOffset.Parse("2026-01-02T12:00:00Z"));

    [Fact]
    public async Task UpsertAsync_MonthlyMbsFactorSequence_EnforcesExactCasReplayAndHydratesLatestState()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var store = new InMemoryAssetOperationsProjectionStore();
        var role = CreateRole();
        var firstState = CreateState(PositionId, 1, new DateOnly(2026, 1, 25), 0.9800m, 0.9625m);
        var firstPosition = CreatePosition(role, PositionId, LedgerBookId, 1, firstState);

        var invalidCreateState = CreateState(PositionId, 2, new DateOnly(2026, 1, 25), 0.9800m, 0.9625m);
        var invalidCreate = CreatePosition(role, PositionId, LedgerBookId, 2, invalidCreateState);
        var invalidCreateAct = () => store.UpsertAsync(
            role,
            invalidCreate,
            invalidCreateState,
            0,
            Approval,
            timeout.Token);
        await invalidCreateAct.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*ExpectedVersion 0*Version 1*");

        await store.UpsertAsync(role, firstPosition, firstState, 0, Approval, timeout.Token);
        var secondState = CreateState(PositionId, 2, new DateOnly(2026, 2, 25), 0.9625m, 0.9500m);
        var secondPosition = firstPosition with
        {
            Version = 2,
            CurrentEconomicState = secondState,
            ProjectionLineage = secondState.ProjectionLineage
        };

        await store.UpsertAsync(role, secondPosition, secondState, 1, Approval, timeout.Token);
        var replay = await store.UpsertAsync(
            role,
            secondPosition,
            secondState,
            1,
            Approval,
            timeout.Token);

        replay.Version.Should().Be(2);
        replay.CurrentEconomicState.Should().BeEquivalentTo(secondState);
        var staleState = CreateState(PositionId, 3, new DateOnly(2026, 3, 25), 0.9500m, 0.9400m);
        var stalePosition = secondPosition with
        {
            Version = 3,
            CurrentEconomicState = staleState,
            ProjectionLineage = staleState.ProjectionLineage
        };
        var staleAct = () => store.UpsertAsync(
            role,
            stalePosition,
            staleState,
            1,
            Approval,
            timeout.Token);
        await staleAct.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*stale*");

        var persisted = await store.GetBookPositionAsync(PositionId, timeout.Token);
        persisted.Should().NotBeNull();
        persisted!.Version.Should().Be(2);
        persisted.CurrentEconomicState.Should().BeEquivalentTo(secondState);
        persisted.ProjectionLineage.Should().BeEquivalentTo(secondState.ProjectionLineage);
        var snapshot = await store.GetSecurityAsync(SecurityId, timeout.Token);
        snapshot.PositionEconomicStates.Should().HaveCount(2);
        snapshot.ProjectionLineages.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetAsOfAsync_MultiBookFactorHistory_ReturnsOnlyEffectiveBookState()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var store = new InMemoryAssetOperationsProjectionStore();
        var role = CreateRole();
        var firstState = CreateState(PositionId, 1, new DateOnly(2026, 1, 25), 0.9800m, 0.9625m);
        var firstPosition = CreatePosition(
            role,
            PositionId,
            LedgerBookId,
            1,
            firstState,
            effectiveTo: new DateOnly(2026, 12, 31));
        await store.UpsertAsync(role, firstPosition, firstState, 0, Approval, timeout.Token);

        var futureState = CreateState(PositionId, 2, new DateOnly(2026, 10, 25), 0.9625m, 0.9400m);
        var updatedPosition = firstPosition with
        {
            Version = 2,
            CurrentEconomicState = futureState,
            ProjectionLineage = futureState.ProjectionLineage
        };
        await store.UpsertAsync(role, updatedPosition, futureState, 1, Approval, timeout.Token);

        var otherPositionId = Guid.Parse("b3000000-aaaa-4000-8000-000000000006");
        var otherState = CreateState(otherPositionId, 1, new DateOnly(2026, 2, 25), 1.0000m, 0.9900m);
        var otherPosition = CreatePosition(role, otherPositionId, OtherLedgerBookId, 1, otherState);
        await store.UpsertAsync(role, otherPosition, otherState, 0, Approval, timeout.Token);

        var asOfJune = await store.GetAsOfAsync(
            SecurityId,
            LedgerBookId,
            new DateOnly(2026, 6, 30),
            timeout.Token);

        asOfJune.BookPositions.Should().ContainSingle()
            .Which.PositionId.Should().Be(PositionId);
        asOfJune.PositionEconomicStates.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(firstState);
        asOfJune.BookPositions.Single().CurrentEconomicState.Should().BeEquivalentTo(firstState);
        asOfJune.ProjectionLineages.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(firstState.ProjectionLineage);
        asOfJune.LedgerBookId.Should().Be(LedgerBookId);
        asOfJune.AsOfDate.Should().Be(new DateOnly(2026, 6, 30));

        var afterExpiry = await store.GetAsOfAsync(
            SecurityId,
            LedgerBookId,
            new DateOnly(2027, 1, 1),
            timeout.Token);
        afterExpiry.BookPositions.Should().BeEmpty();
        afterExpiry.PositionEconomicStates.Should().BeEmpty();
        afterExpiry.ProjectionLineages.Should().BeEmpty();
    }

    [Fact]
    public async Task UpsertAsync_OverlappingActiveBookPosition_FailsAtomically()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var store = new InMemoryAssetOperationsProjectionStore();
        var role = CreateRole();
        var firstState = CreateState(PositionId, 1, new DateOnly(2026, 1, 25), 0.9800m, 0.9625m);
        var firstPosition = CreatePosition(role, PositionId, LedgerBookId, 1, firstState);
        await store.UpsertAsync(role, firstPosition, firstState, 0, Approval, timeout.Token);

        var overlappingPositionId = Guid.Parse("b3000000-aaaa-4000-8000-000000000007");
        var overlappingState = CreateState(overlappingPositionId, 1, new DateOnly(2026, 2, 25), 0.9625m, 0.9500m);
        var overlappingPosition = CreatePosition(
            role,
            overlappingPositionId,
            LedgerBookId,
            1,
            overlappingState,
            effectiveFrom: new DateOnly(2026, 2, 1));

        var act = () => store.UpsertAsync(
            role,
            overlappingPosition,
            overlappingState,
            0,
            Approval,
            timeout.Token);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*overlaps active position*");
        var snapshot = await store.GetSecurityAsync(SecurityId, timeout.Token);
        snapshot.BookPositions.Should().ContainSingle()
            .Which.PositionId.Should().Be(PositionId);
        snapshot.PositionEconomicStates.Should().ContainSingle();
    }

    [Fact]
    public async Task UpsertAsync_InvalidRoleOwnerDatesDimensionsAndCrossBook_FailsClosed()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var store = new InMemoryAssetOperationsProjectionStore();
        var role = CreateRole();
        var state = CreateState(PositionId, 1, new DateOnly(2026, 1, 25), 0.9800m, 0.9625m);
        var position = CreatePosition(role, PositionId, LedgerBookId, 1, state);

        var missingRolePosition = position with { RoleId = Guid.NewGuid() };
        var missingRoleAct = () => store.UpsertAsync(
            role,
            missingRolePosition,
            state,
            0,
            Approval,
            timeout.Token);
        await missingRoleAct.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*matching security and owner scope*");

        var wrongOwnerRole = role with { OwnerScopeId = "fund-beta" };
        var wrongOwnerAct = () => store.UpsertAsync(
            wrongOwnerRole,
            position,
            state,
            0,
            Approval,
            timeout.Token);
        await wrongOwnerAct.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*matching security and owner scope*");

        var missingProvenanceAct = () => store.UpsertAsync(
            role with { OriginEvent = null },
            position,
            state,
            0,
            Approval,
            timeout.Token);
        await missingProvenanceAct.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*source event and evidence*");

        var missingSideAct = () => store.UpsertAsync(
            role with { AccountingSide = string.Empty },
            position,
            state,
            0,
            Approval,
            timeout.Token);
        await missingSideAct.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*role projections require*");

        var invalidDates = position with { EffectiveTo = EffectiveFrom.AddDays(-1) };
        var invalidDatesAct = () => store.UpsertAsync(
            role,
            invalidDates,
            state,
            0,
            Approval,
            timeout.Token);
        await invalidDatesAct.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*end dates cannot precede start dates*");

        var mismatchedDimensions = position with
        {
            BookContext = position.BookContext with
            {
                Dimensions = position.BookContext.Dimensions! with { InstrumentId = Guid.NewGuid() }
            }
        };
        var dimensionsAct = () => store.UpsertAsync(
            role,
            mismatchedDimensions,
            state,
            0,
            Approval,
            timeout.Token);
        await dimensionsAct.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*dimensions conflict*");

        var mismatchedVersionState = state with { Version = 2 };
        var stateVersionAct = () => store.UpsertAsync(
            role,
            position with { CurrentEconomicState = null },
            mismatchedVersionState,
            0,
            Approval,
            timeout.Token);
        await stateVersionAct.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*same version*");

        var invalidSourceState = state with
        {
            SourceEvent = state.SourceEvent! with { EventId = Guid.Empty }
        };
        var invalidSourceAct = () => store.UpsertAsync(
            role,
            position with { CurrentEconomicState = invalidSourceState },
            invalidSourceState,
            0,
            Approval,
            timeout.Token);
        await invalidSourceAct.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*event or projection lineage crosses*");

        await store.UpsertAsync(role, position, state, 0, Approval, timeout.Token);
        var caseState = CreateState(PositionId, 2, new DateOnly(2026, 2, 25), 0.9625m, 0.9500m);
        var caseRole = role with { OwnerScopeId = "FUND-ALPHA", Version = 2 };
        var casePosition = CreatePosition(caseRole, PositionId, LedgerBookId, 2, caseState);
        var caseChangeAct = () => store.UpsertAsync(
            caseRole,
            casePosition,
            caseState,
            1,
            Approval,
            timeout.Token);
        await caseChangeAct.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*cannot change security, owner scope, or role kind*");

        var otherBookState = CreateState(PositionId, 2, new DateOnly(2026, 2, 25), 0.9625m, 0.9500m);
        var otherBookPosition = CreatePosition(role, PositionId, OtherLedgerBookId, 2, otherBookState);
        var crossBookAct = () => store.UpsertAsync(
            role,
            otherBookPosition,
            otherBookState,
            1,
            Approval,
            timeout.Token);
        await crossBookAct.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*ledger-book boundary*");

        var persisted = await store.GetBookPositionAsync(PositionId, timeout.Token);
        persisted!.BookContext.LedgerBookId.Should().Be(LedgerBookId);
        persisted.Version.Should().Be(1);
    }

    [Fact]
    public async Task LegacyUpsert_DuplicateEconomicStateVersionWithNewIdentity_IsRejectedAtomically()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var store = new InMemoryAssetOperationsProjectionStore();
        var projection = InstrumentPositionProjectionFixture.Create();
        await store.UpsertAsync(projection, InstrumentPositionProjectionFixture.Approval, timeout.Token);
        var persistedPosition = projection.BookPositions.Single();
        var persistedState = projection.PositionEconomicStates.Single();
        var conflictingState = persistedState with
        {
            EconomicStateId = Guid.NewGuid(),
            CurrentFactor = persistedState.CurrentFactor - 0.001m
        };
        var conflictingPosition = persistedPosition with
        {
            Version = persistedPosition.Version + 1,
            CurrentEconomicState = conflictingState,
            ProjectionLineage = conflictingState.ProjectionLineage
        };
        var conflictingProjection = projection with
        {
            BookPositions = [conflictingPosition],
            PositionEconomicStates = [conflictingState]
        };

        var act = () => store.UpsertAsync(
            conflictingProjection,
            InstrumentPositionProjectionFixture.Approval,
            timeout.Token);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*version*already exists*");
        var persisted = await store.GetBookPositionAsync(InstrumentPositionProjectionFixture.PositionId, timeout.Token);
        persisted!.Version.Should().Be(4);
        var snapshot = await store.GetSecurityAsync(InstrumentPositionProjectionFixture.SecurityId, timeout.Token);
        snapshot.PositionEconomicStates.Should().ContainSingle()
            .Which.EconomicStateId.Should().Be(persistedState.EconomicStateId);
    }

    [Fact]
    public async Task LegacyUpsert_ImportedVersionAndEmptyCompatibilityWrite_RemainVisibleToDedicatedQueries()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var store = new InMemoryAssetOperationsProjectionStore();
        var projection = InstrumentPositionProjectionFixture.Create();

        await store.UpsertAsync(projection, InstrumentPositionProjectionFixture.Approval, timeout.Token);
        await store.UpsertAsync(projection with
        {
            InstrumentRoles = [],
            BookPositions = [],
            PositionEconomicStates = [],
            ProjectionLineages = []
        }, InstrumentPositionProjectionFixture.Approval, timeout.Token);
        var advanced = InstrumentPositionProjectionFixture.Advance(projection);
        await store.UpsertAsync(advanced, InstrumentPositionProjectionFixture.Approval, timeout.Token);

        var snapshot = await store.GetSecurityAsync(
            InstrumentPositionProjectionFixture.SecurityId,
            timeout.Token);
        snapshot.InstrumentRoles.Should().ContainSingle();
        snapshot.BookPositions.Should().ContainSingle()
            .Which.Version.Should().Be(5);
        snapshot.BookPositions.Single().CurrentEconomicState!.Version.Should().Be(6);
        snapshot.PositionEconomicStates.Should().HaveCount(2);
        snapshot.ProjectionLineages.Should().HaveCount(2);
        var legacy = await store.GetAsync(InstrumentPositionProjectionFixture.SecurityId, timeout.Token);
        legacy.Should().NotBeNull();
        legacy!.BookPositions.Should().BeEquivalentTo(snapshot.BookPositions);
        legacy.PositionEconomicStates.Should().BeEquivalentTo(snapshot.PositionEconomicStates);
    }

    [Fact]
    public async Task DedicatedUpsert_ConflictingStateAndLineageInputs_FailClosedAndReplayKeepsApproval()
    {
        var store = new InMemoryAssetOperationsProjectionStore();
        var role = CreateRole();
        var state = CreateState(PositionId, 1, new DateOnly(2026, 1, 25), 0.9800m, 0.9625m);
        var position = CreatePosition(role, PositionId, LedgerBookId, 1, state);
        var conflictingState = state with { CurrentFactor = 0.9500m };

        var conflictingWrite = () => store.UpsertAsync(
            role,
            position,
            conflictingState,
            expectedVersion: 0,
            Approval);
        await conflictingWrite.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*embedded and explicit economic states*");

        var crossedLineage = state.ProjectionLineage! with { BookPositionId = Guid.NewGuid() };
        var crossedPosition = position with
        {
            CurrentEconomicState = null,
            ProjectionLineage = crossedLineage
        };
        var crossedWrite = () => store.UpsertAsync(
            role,
            crossedPosition,
            null,
            expectedVersion: 0,
            Approval);
        await crossedWrite.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*crosses the book-position*");

        await store.UpsertAsync(role, position, state, expectedVersion: 0, Approval);
        var replayApproval = Approval with
        {
            Actor = "replay-controller@meridian.local",
            ApprovalReference = "replay-approval"
        };
        await store.UpsertAsync(role, position, state, expectedVersion: 0, replayApproval);

        store.GetRetainedProjectionApproval(PositionId).Should().BeEquivalentTo(Approval);
        store.GetRetainedProjectionApproval(state.EconomicStateId).Should().BeEquivalentTo(Approval);
    }

    [Fact]
    public async Task LegacyUpsert_CrossSecurityIsRejectedWhileSparseImportedVersionsRemainCompatible()
    {
        var store = new InMemoryAssetOperationsProjectionStore();
        var projection = InstrumentPositionProjectionFixture.Create();
        var crossedRole = projection.InstrumentRoles.Single() with { SecurityId = Guid.NewGuid() };
        var crossed = projection with
        {
            InstrumentRoles = [crossedRole],
            BookPositions = [],
            PositionEconomicStates = [],
            ProjectionLineages = []
        };

        var crossedWrite = () => store.UpsertAsync(crossed, InstrumentPositionProjectionFixture.Approval);
        await crossedWrite.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*subject Security Master identity*");
        (await store.GetSecurityAsync(crossedRole.SecurityId)).BookPositions.Should().BeEmpty();

        await store.UpsertAsync(projection, InstrumentPositionProjectionFixture.Approval);
        var persistedPosition = projection.BookPositions.Single();
        var jumpedState = projection.PositionEconomicStates.Single() with
        {
            EconomicStateId = Guid.NewGuid(),
            Version = persistedPosition.Version + 3
        };
        var jumpedPosition = persistedPosition with
        {
            Version = persistedPosition.Version + 2,
            CurrentEconomicState = jumpedState,
            ProjectionLineage = jumpedState.ProjectionLineage
        };
        var jumped = projection with
        {
            InstrumentRoles = [projection.InstrumentRoles.Single() with { Version = 4 }],
            BookPositions = [jumpedPosition],
            PositionEconomicStates = [jumpedState]
        };

        await store.UpsertAsync(jumped, InstrumentPositionProjectionFixture.Approval);
        var sparse = await store.GetBookPositionAsync(persistedPosition.PositionId);
        sparse!.Version.Should().Be(jumpedPosition.Version);
        sparse.CurrentEconomicState!.Version.Should().Be(jumpedState.Version);
        (await store.GetSecurityAsync(projection.Subject.SecurityId)).InstrumentRoles.Single().Version
            .Should().Be(4);
    }

    [Fact]
    public async Task InMemoryStore_ShouldDefensivelyCloneRetainedEvidenceOnWriteAndRead()
    {
        var store = new InMemoryAssetOperationsProjectionStore();
        var roleEvidence = new List<string> { "evidence://role/original" };
        var stateEvidence = new List<string> { "evidence://state/original" };
        var role = CreateRole() with { EvidenceLinks = roleEvidence };
        var state = CreateState(PositionId, 1, new DateOnly(2026, 1, 25), 0.9800m, 0.9625m) with
        {
            EvidenceLinks = stateEvidence
        };
        var position = CreatePosition(role, PositionId, LedgerBookId, 1, state);

        await store.UpsertAsync(role, position, state, expectedVersion: 0, Approval);
        roleEvidence[0] = "evidence://role/tampered-after-write";
        stateEvidence[0] = "evidence://state/tampered-after-write";

        var firstRead = await store.GetSecurityAsync(SecurityId);
        firstRead.InstrumentRoles.Single().EvidenceLinks.Should().Equal("evidence://role/original");
        firstRead.PositionEconomicStates.Single().EvidenceLinks.Should().Equal("evidence://state/original");
        var returnedEvidence = firstRead.InstrumentRoles.Single().EvidenceLinks.Should()
            .BeAssignableTo<IList<string>>().Subject;
        returnedEvidence[0] = "evidence://role/tampered-after-read";

        var secondRead = await store.GetSecurityAsync(SecurityId);
        secondRead.InstrumentRoles.Single().EvidenceLinks.Should().Equal("evidence://role/original");
    }

    [Fact]
    public async Task DedicatedUpsert_UnknownActiveStatusAndWindowShrink_CannotBypassGuards()
    {
        var store = new InMemoryAssetOperationsProjectionStore();
        var role = CreateRole();
        var firstState = CreateState(PositionId, 1, new DateOnly(2026, 1, 25), 0.9800m, 0.9625m);
        var first = CreatePosition(role, PositionId, LedgerBookId, 1, firstState);
        await store.UpsertAsync(role, first, firstState, expectedVersion: 0, Approval);
        var secondState = CreateState(PositionId, 2, new DateOnly(2026, 2, 25), 0.9625m, 0.9500m);
        var second = first with
        {
            Version = 2,
            CurrentEconomicState = secondState,
            ProjectionLineage = secondState.ProjectionLineage
        };
        await store.UpsertAsync(role, second, secondState, expectedVersion: 1, Approval);

        var shrink = second with
        {
            Version = 3,
            EffectiveTo = new DateOnly(2026, 1, 31),
            CurrentEconomicState = null,
            ProjectionLineage = null
        };
        var shrinkWrite = () => store.UpsertAsync(role, shrink, null, expectedVersion: 2, Approval);
        await shrinkWrite.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*inside the book position effective window*");

        var typoPositionId = Guid.NewGuid();
        var typoState = CreateState(typoPositionId, 1, new DateOnly(2026, 3, 25), 0.9500m, 0.9400m);
        var typoPosition = CreatePosition(role, typoPositionId, LedgerBookId, 1, typoState) with
        {
            Status = "Actve"
        };
        var typoWrite = () => store.UpsertAsync(
            role,
            typoPosition,
            typoState,
            expectedVersion: 0,
            Approval);
        await typoWrite.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*overlaps active position*");

        var closedPositionId = Guid.NewGuid();
        var closedState = CreateState(closedPositionId, 1, new DateOnly(2026, 3, 25), 0.9500m, 0.9400m);
        var closedPosition = CreatePosition(role, closedPositionId, LedgerBookId, 1, closedState) with
        {
            Status = "Closed"
        };
        await store.UpsertAsync(role, closedPosition, closedState, expectedVersion: 0, Approval);
        (await store.GetSecurityAsync(SecurityId)).BookPositions.Should().HaveCount(2);
    }

    private static InstrumentRoleDto CreateRole(
        DateOnly? effectiveFrom = null,
        DateOnly? effectiveTo = null)
    {
        var start = effectiveFrom ?? EffectiveFrom;
        var originEvent = new EconomicEventReferenceDto(
            Guid.NewGuid(),
            "PositionRoleEstablished",
            1,
            start,
            new DateTimeOffset(start.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
            "AssetOperations",
            $"role-{RoleId:D}",
            SourceContentHash: $"sha256:role-{RoleId:D}",
            EvidenceLinks: ["evidence://position/holder"])
        {
            SecurityId = SecurityId
        };
        return new InstrumentRoleDto(
            RoleId,
            SecurityId,
            "fund-alpha",
            "Fund",
            InstrumentRoleKinds.Holder,
            InstrumentAccountingSides.Debit,
            InstrumentEconomicSides.Asset,
            start,
            effectiveTo,
            Version: 1,
            OriginEvent: originEvent,
            EvidenceLinks: ["evidence://position/holder"]);
    }

    private static BookPositionDto CreatePosition(
        InstrumentRoleDto role,
        Guid positionId,
        Guid ledgerBookId,
        long version,
        PositionEconomicStateDto state,
        DateOnly? effectiveFrom = null,
        DateOnly? effectiveTo = null)
    {
        var dimensions = new LedgerDimensionSetDto(
            role.OwnerScopeId,
            "entity-alpha",
            InstrumentId: role.SecurityId,
            BookId: ledgerBookId.ToString("D"))
        {
            PositionId = positionId
        };
        var context = new AccountingBookContextDto(
            ledgerBookId,
            role.OwnerScopeId,
            Guid.Parse("b3000000-aaaa-4000-8000-000000000008"),
            FundStructureNodeKindDto.Fund,
            "Fund Alpha GAAP",
            "USD",
            AccountingBasisKindDto.Gaap,
            "gaap-mbs-v1",
            "v1",
            Dimensions: dimensions);

        return new BookPositionDto(
            positionId,
            role.SecurityId,
            role.RoleId,
            context,
            BookPositionSides.Long,
            "Active",
            effectiveFrom ?? EffectiveFrom,
            effectiveTo,
            version,
            "Securities",
            state,
            OriginEvent: state.SourceEvent,
            ProjectionLineage: state.ProjectionLineage,
            EvidenceLinks: ["evidence://position/holder"]);
    }

    private static PositionEconomicStateDto CreateState(
        Guid positionId,
        long version,
        DateOnly asOfDate,
        decimal priorFactor,
        decimal currentFactor)
    {
        var eventId = Guid.NewGuid();
        var economicEvent = new EconomicEventReferenceDto(
            eventId,
            "MbsFactorPaydown",
            version,
            asOfDate,
            new DateTimeOffset(asOfDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
            "SecurityMaster",
            $"factor-row-{asOfDate:yyyy-MM-dd}",
            SourceContentHash: $"sha256:factor-{asOfDate:yyyy-MM-dd}",
            EvidenceLinks: [$"evidence://factor/{asOfDate:yyyy-MM-dd}"])
        {
            SecurityId = SecurityId,
            BookPositionId = positionId
        };
        var lineage = new ProjectionLineageDto(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "mbs-factor-paydown",
            "1.0.0",
            "factor-paydown-projection-v1",
            "Base",
            asOfDate,
            economicEvent.OccurredAtUtc,
            "SecurityMaster",
            economicEvent.SourceEntityId,
            economicEvent,
            TermsHash: economicEvent.SourceContentHash,
            EvidenceLinks: economicEvent.EvidenceLinks)
        {
            BookPositionId = positionId
        };

        return new PositionEconomicStateDto(
            Guid.NewGuid(),
            positionId,
            asOfDate,
            "USD",
            version,
            ParAmount: 100_000m,
            OriginalFaceAmount: 100_000m,
            CurrentFaceAmount: decimal.Round(100_000m * currentFactor, 2),
            PriorFactor: priorFactor,
            CurrentFactor: currentFactor,
            SourceEvent: economicEvent,
            EvidenceLinks: economicEvent.EvidenceLinks)
        {
            ProjectionLineage = lineage
        };
    }
}
