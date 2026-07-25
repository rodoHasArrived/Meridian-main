using FluentAssertions;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Instruments.AssetOperations;
using Meridian.Storage.AssetOperations;
using Meridian.TestSupport;
using Npgsql;

namespace Meridian.Tests.AssetOperations;

public sealed class InMemoryInstrumentPositionProjectionStoreTests
{
    [Fact]
    public async Task UpsertAsync_ShouldPreserveTypedCollectionsAcrossLegacyWritesAndKeepStateHistoryAppendOnly()
    {
        var store = new InMemoryAssetOperationsProjectionStore();
        var projection = InstrumentPositionProjectionFixture.Create();

        await store.UpsertAsync(projection, InstrumentPositionProjectionFixture.Approval);
        await store.UpsertAsync(projection with
        {
            InstrumentRoles = [],
            BookPositions = [],
            PositionEconomicStates = [],
            ProjectionLineages = []
        }, InstrumentPositionProjectionFixture.Approval);
        var advanced = InstrumentPositionProjectionFixture.Advance(projection);
        var lineageOnlyState = advanced.PositionEconomicStates.Single() with { SourceEvent = null };
        var lineageOnlyPosition = advanced.BookPositions.Single() with
        {
            CurrentEconomicState = lineageOnlyState,
            ProjectionLineage = lineageOnlyState.ProjectionLineage
        };
        advanced = advanced with
        {
            BookPositions = [lineageOnlyPosition],
            PositionEconomicStates = [lineageOnlyState]
        };
        await store.UpsertAsync(advanced, InstrumentPositionProjectionFixture.Approval);

        var persisted = await store.GetAsync(InstrumentPositionProjectionFixture.SecurityId);
        persisted.Should().NotBeNull();
        persisted!.InstrumentRoles.Should().ContainSingle();
        persisted.BookPositions.Should().ContainSingle()
            .Which.Version.Should().Be(5);
        persisted.PositionEconomicStates.Should().HaveCount(2);
        persisted.ProjectionLineages.Should().HaveCount(2);
        persisted.PositionEconomicStates.Should().OnlyContain(state => state.ProjectionLineage != null);

        var conflicting = projection.PositionEconomicStates.Single() with { CurrentFactor = 0.95m };
        var act = () => store.UpsertAsync(projection with
        {
            InstrumentRoles = [],
            BookPositions = [],
            PositionEconomicStates = [conflicting],
            ProjectionLineages = []
        }, InstrumentPositionProjectionFixture.Approval);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*append-only*");
    }
}

[Trait("Category", "Integration")]
public sealed class PostgresInstrumentPositionProjectionStoreTests : IAsyncLifetime
{
    private const string ConnectionStringVariable = "MERIDIAN_ASSET_OPERATIONS_CONNECTION_STRING";
    private PostgresTestServer? _server;
    private AssetOperationsOptions _options = new();

    public async Task InitializeAsync()
    {
        _server = await PostgresTestServer.CreateAsync(ConnectionStringVariable).ConfigureAwait(false);
        _options = new AssetOperationsOptions
        {
            ConnectionString = _server.ConnectionString,
            Schema = PostgresTestSchema.NewSchemaName("asset_position")
        };
    }

    public async Task DisposeAsync()
    {
        if (_server is not null)
        {
            await _server.DisposeAsync().ConfigureAwait(false);
        }
    }

    [AssetOperationsDatabaseFact]
    public async Task UpsertAsync_ShouldRoundTripTypedRolePositionStateAndLineageWithoutLegacyDeletion()
    {
        await new AssetOperationsMigrationRunner(_options).EnsureMigratedAsync();
        var store = new PostgresAssetOperationsProjectionStore(_options);
        var projection = InstrumentPositionProjectionFixture.Create();

        await store.UpsertAsync(projection, InstrumentPositionProjectionFixture.Approval);
        await store.UpsertAsync(projection with
        {
            InstrumentRoles = [],
            BookPositions = [],
            PositionEconomicStates = [],
            ProjectionLineages = []
        }, InstrumentPositionProjectionFixture.Approval);
        var advanced = InstrumentPositionProjectionFixture.Advance(projection);
        var lineageOnlyState = advanced.PositionEconomicStates.Single() with { SourceEvent = null };
        var lineageOnlyPosition = advanced.BookPositions.Single() with
        {
            CurrentEconomicState = lineageOnlyState,
            ProjectionLineage = lineageOnlyState.ProjectionLineage
        };
        advanced = advanced with
        {
            BookPositions = [lineageOnlyPosition],
            PositionEconomicStates = [lineageOnlyState]
        };
        await store.UpsertAsync(advanced, InstrumentPositionProjectionFixture.Approval);

        var persisted = await store.GetAsync(InstrumentPositionProjectionFixture.SecurityId);
        persisted.Should().NotBeNull();
        persisted!.InstrumentRoles.Should().BeEquivalentTo(projection.InstrumentRoles);
        persisted.BookPositions.Should().BeEquivalentTo(advanced.BookPositions);
        persisted.PositionEconomicStates.Should().HaveCount(2);
        persisted.ProjectionLineages.Should().HaveCount(2);
        persisted.PositionEconomicStates.Should().OnlyContain(state => state.ProjectionLineage != null);

        await using var connection = new NpgsqlConnection(_options.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"select source_event_id from \"{_options.Schema}\".\"position_economic_state_projections\" where economic_state_id = @state_id;";
        command.Parameters.AddWithValue("state_id", lineageOnlyState.EconomicStateId);
        (await command.ExecuteScalarAsync()).Should().Be(lineageOnlyState.ProjectionLineage!.TriggerEvent.EventId);
    }

    [AssetOperationsDatabaseFact]
    public async Task UpsertAsync_ShouldRejectPositionOwnershipChangesPreserveReplayApprovalAndAllowSparseImports()
    {
        await new AssetOperationsMigrationRunner(_options).EnsureMigratedAsync();
        var store = new PostgresAssetOperationsProjectionStore(_options);
        var projection = InstrumentPositionProjectionFixture.Create();
        await store.UpsertAsync(projection, InstrumentPositionProjectionFixture.Approval);

        var replayApproval = InstrumentPositionProjectionFixture.Approval with
        {
            Actor = "different-controller@meridian.local",
            ApprovalReference = "different-approval",
            ApprovedAt = InstrumentPositionProjectionFixture.Approval.ApprovedAt.AddHours(1)
        };
        await store.UpsertAsync(projection, replayApproval);

        await using (var connection = new NpgsqlConnection(_options.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"select approval_actor from \"{_options.Schema}\".\"book_position_projections\" where position_id = @position_id;";
            command.Parameters.AddWithValue("position_id", InstrumentPositionProjectionFixture.PositionId);
            (await command.ExecuteScalarAsync()).Should().Be(InstrumentPositionProjectionFixture.Approval.Actor);
        }

        var originalPosition = projection.BookPositions.Single();
        var conflicting = projection with
        {
            BookPositions =
            [
                originalPosition with
                {
                    Version = originalPosition.Version + 1,
                    BookContext = originalPosition.BookContext with { LedgerBookId = Guid.NewGuid() }
                }
            ],
            PositionEconomicStates = [],
            ProjectionLineages = []
        };

        var act = () => store.UpsertAsync(conflicting, InstrumentPositionProjectionFixture.Approval);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*immutable*");

        var jumpedPosition = originalPosition with
        {
            Version = originalPosition.Version + 2,
            CurrentEconomicState = null,
            ProjectionLineage = null
        };
        var jumped = projection with
        {
            InstrumentRoles = [projection.InstrumentRoles.Single() with { Version = 4 }],
            BookPositions = [jumpedPosition],
            PositionEconomicStates = [],
            ProjectionLineages = []
        };
        await store.UpsertAsync(jumped, InstrumentPositionProjectionFixture.Approval);
        var sparse = await store.GetBookPositionAsync(jumpedPosition.PositionId);
        sparse!.Version.Should().Be(jumpedPosition.Version);
        sparse.CurrentEconomicState!.EconomicStateId.Should()
            .Be(projection.PositionEconomicStates.Single().EconomicStateId);
        (await store.GetSecurityAsync(projection.Subject.SecurityId)).InstrumentRoles.Single().Version
            .Should().Be(4);
    }

    [AssetOperationsDatabaseFact]
    public async Task DedicatedStore_ShouldApplyExpectedVersionAndPreserveIdempotentReplay()
    {
        await new AssetOperationsMigrationRunner(_options).EnsureMigratedAsync();
        var store = new PostgresAssetOperationsProjectionStore(_options);
        var initial = InstrumentPositionProjectionFixture.CreateStrict();

        var created = await store.UpsertAsync(
            initial.Role,
            initial.Position,
            initial.State,
            expectedVersion: 0,
            InstrumentPositionProjectionFixture.Approval);
        var replayed = await store.UpsertAsync(
            initial.Role,
            initial.Position,
            initial.State,
            expectedVersion: 0,
            InstrumentPositionProjectionFixture.Approval with
            {
                Actor = "replay-controller@meridian.local",
                ApprovalReference = "replay-approval"
            });

        await using (var replayConnection = new NpgsqlConnection(_options.ConnectionString))
        {
            await replayConnection.OpenAsync();
            await using var replayApproval = replayConnection.CreateCommand();
            replayApproval.CommandText =
                $"""
                select approval_actor = @actor
                   and approval_reference = @reference
                   and approval_rationale = @rationale
                   and approved_at = @approved_at
                from "{_options.Schema}"."book_position_projections"
                where position_id = @position_id;
                """;
            replayApproval.Parameters.AddWithValue("actor", InstrumentPositionProjectionFixture.Approval.Actor);
            replayApproval.Parameters.AddWithValue("reference", InstrumentPositionProjectionFixture.Approval.ApprovalReference);
            replayApproval.Parameters.AddWithValue("rationale", InstrumentPositionProjectionFixture.Approval.Rationale);
            replayApproval.Parameters.AddWithValue("approved_at", InstrumentPositionProjectionFixture.Approval.ApprovedAt);
            replayApproval.Parameters.AddWithValue("position_id", InstrumentPositionProjectionFixture.PositionId);
            (await replayApproval.ExecuteScalarAsync()).Should().Be(true);
        }

        var advanced = InstrumentPositionProjectionFixture.AdvanceStrict(initial);
        var updated = await store.UpsertAsync(
            advanced.Role,
            advanced.Position,
            advanced.State,
            expectedVersion: 1,
            InstrumentPositionProjectionFixture.Approval);

        created.Version.Should().Be(1);
        replayed.Should().BeEquivalentTo(created);
        updated.Version.Should().Be(2);
        updated.CurrentEconomicState.Should().BeEquivalentTo(advanced.State);

        var replayWithStaleExpectedVersion = () => store.UpsertAsync(
            advanced.Role,
            advanced.Position,
            advanced.State,
            expectedVersion: 0,
            InstrumentPositionProjectionFixture.Approval);
        await replayWithStaleExpectedVersion.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*stale*");

        var stale = () => store.UpsertAsync(
            advanced.Role,
            advanced.Position with { Version = 3 },
            advanced.State with { Version = 3 },
            expectedVersion: 1,
            InstrumentPositionProjectionFixture.Approval);
        await stale.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*stale*");

        await using var connection = new NpgsqlConnection(_options.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"select approval_actor from \"{_options.Schema}\".\"book_position_projections\" where position_id = @position_id;";
        command.Parameters.AddWithValue("position_id", InstrumentPositionProjectionFixture.PositionId);
        (await command.ExecuteScalarAsync()).Should().Be(InstrumentPositionProjectionFixture.Approval.Actor);
    }

    [AssetOperationsDatabaseFact]
    public async Task DedicatedStore_ShouldRejectProjectionRunReuseWithConflictingRetainedLineage()
    {
        await new AssetOperationsMigrationRunner(_options).EnsureMigratedAsync();
        var store = new PostgresAssetOperationsProjectionStore(_options);
        var initial = InstrumentPositionProjectionFixture.CreateStrict();
        await store.UpsertAsync(
            initial.Role,
            initial.Position,
            initial.State,
            expectedVersion: 0,
            InstrumentPositionProjectionFixture.Approval);
        var advanced = InstrumentPositionProjectionFixture.AdvanceStrict(initial);
        var conflictingLineage = advanced.State.ProjectionLineage! with
        {
            ProjectionRunId = initial.State.ProjectionLineage!.ProjectionRunId
        };
        var conflictingState = advanced.State with { ProjectionLineage = conflictingLineage };
        var conflictingPosition = advanced.Position with
        {
            CurrentEconomicState = conflictingState,
            ProjectionLineage = conflictingLineage
        };

        var act = () => store.UpsertAsync(
            advanced.Role,
            conflictingPosition,
            conflictingState,
            expectedVersion: 1,
            InstrumentPositionProjectionFixture.Approval);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*cannot be reused with conflicting lineage*");
        var persisted = await store.GetBookPositionAsync(initial.Position.PositionId);
        persisted!.Version.Should().Be(1);
        (await store.GetSecurityAsync(initial.Position.SecurityId))
            .PositionEconomicStates.Should().ContainSingle();
    }

    [AssetOperationsDatabaseFact]
    public async Task DedicatedStore_ShouldQueryEffectiveBookStateAfterRestartAndRejectOverlap()
    {
        await new AssetOperationsMigrationRunner(_options).EnsureMigratedAsync();
        var writer = new PostgresAssetOperationsProjectionStore(_options);
        var initial = InstrumentPositionProjectionFixture.CreateStrict();
        await writer.UpsertAsync(
            initial.Role,
            initial.Position,
            initial.State,
            expectedVersion: 0,
            InstrumentPositionProjectionFixture.Approval);
        var advanced = InstrumentPositionProjectionFixture.AdvanceStrict(initial);
        await writer.UpsertAsync(
            advanced.Role,
            advanced.Position,
            advanced.State,
            expectedVersion: 1,
            InstrumentPositionProjectionFixture.Approval);

        var restarted = new PostgresAssetOperationsProjectionStore(_options);
        var asOf = await restarted.GetAsOfAsync(
            InstrumentPositionProjectionFixture.SecurityId,
            InstrumentPositionProjectionFixture.LedgerBookId,
            new DateOnly(2026, 5, 31));
        var beforeFirstProjection = await restarted.GetAsOfAsync(
            InstrumentPositionProjectionFixture.SecurityId,
            InstrumentPositionProjectionFixture.LedgerBookId,
            new DateOnly(2026, 5, 1));
        var byId = await restarted.GetBookPositionAsync(InstrumentPositionProjectionFixture.PositionId);
        var history = await restarted.GetSecurityAsync(InstrumentPositionProjectionFixture.SecurityId);

        asOf.BookPositions.Should().ContainSingle();
        asOf.PositionEconomicStates.Should().ContainSingle()
            .Which.EconomicStateId.Should().Be(initial.State.EconomicStateId);
        asOf.BookPositions.Single().CurrentEconomicState.Should().BeEquivalentTo(initial.State);
        beforeFirstProjection.BookPositions.Should().ContainSingle();
        beforeFirstProjection.BookPositions.Single().CurrentEconomicState.Should().BeNull();
        beforeFirstProjection.BookPositions.Single().ProjectionLineage.Should().BeNull();
        beforeFirstProjection.PositionEconomicStates.Should().BeEmpty();
        beforeFirstProjection.ProjectionLineages.Should().BeEmpty();
        byId.Should().NotBeNull();
        byId!.CurrentEconomicState.Should().BeEquivalentTo(advanced.State);
        history.PositionEconomicStates.Should().HaveCount(2);
        history.ProjectionLineages.Should().HaveCount(2);

        var overlapping = initial.Position with
        {
            PositionId = Guid.Parse("a3000000-aaaa-4000-8000-000000000099"),
            Version = 1,
            PositionSide = $"{initial.Position.PositionSide} ",
            CurrentEconomicState = null,
            OriginEvent = initial.Position.OriginEvent! with
            {
                EventId = Guid.NewGuid(),
                BookPositionId = Guid.Parse("a3000000-aaaa-4000-8000-000000000099")
            },
            ProjectionLineage = null,
            BookContext = initial.Position.BookContext with
            {
                Dimensions = initial.Position.BookContext.Dimensions! with
                {
                    PositionId = Guid.Parse("a3000000-aaaa-4000-8000-000000000099")
                }
            }
        };
        var overlapWrite = () => restarted.UpsertAsync(
            initial.Role,
            overlapping,
            null,
            expectedVersion: 0,
            InstrumentPositionProjectionFixture.Approval);
        await overlapWrite.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*overlaps active position*");

        var closedPosition = overlapping with
        {
            PositionId = Guid.Parse("a3000000-aaaa-4000-8000-000000000098"),
            Status = "Closed",
            OriginEvent = overlapping.OriginEvent! with
            {
                EventId = Guid.NewGuid(),
                BookPositionId = Guid.Parse("a3000000-aaaa-4000-8000-000000000098")
            },
            BookContext = overlapping.BookContext with
            {
                Dimensions = overlapping.BookContext.Dimensions! with
                {
                    PositionId = Guid.Parse("a3000000-aaaa-4000-8000-000000000098")
                }
            }
        };
        await restarted.UpsertAsync(
            initial.Role,
            closedPosition,
            null,
            expectedVersion: 0,
            InstrumentPositionProjectionFixture.Approval);
        (await restarted.GetSecurityAsync(InstrumentPositionProjectionFixture.SecurityId))
            .BookPositions.Should().HaveCount(2);
    }

    [AssetOperationsDatabaseFact]
    public async Task GenericStore_DuplicateEconomicStateVersion_ShouldRollBackPositionAndApprovalChanges()
    {
        await new AssetOperationsMigrationRunner(_options).EnsureMigratedAsync();
        var store = new PostgresAssetOperationsProjectionStore(_options);
        var projection = InstrumentPositionProjectionFixture.Create();
        await store.UpsertAsync(projection, InstrumentPositionProjectionFixture.Approval);
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
        var conflictingApproval = InstrumentPositionProjectionFixture.Approval with
        {
            Actor = "other-controller@meridian.local",
            ApprovalReference = "approval-that-must-roll-back"
        };

        var act = () => store.UpsertAsync(
            projection with
            {
                BookPositions = [conflictingPosition],
                PositionEconomicStates = [conflictingState]
            },
            conflictingApproval);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*already has economic state version*");
        var persisted = await store.GetBookPositionAsync(persistedPosition.PositionId);
        persisted!.Version.Should().Be(persistedPosition.Version);
        persisted.CurrentEconomicState!.EconomicStateId.Should().Be(persistedState.EconomicStateId);
        var snapshot = await store.GetSecurityAsync(projection.Subject.SecurityId);
        snapshot.PositionEconomicStates.Should().ContainSingle();

        await using var connection = new NpgsqlConnection(_options.ConnectionString);
        await connection.OpenAsync();
        await using var approval = connection.CreateCommand();
        approval.CommandText =
            $"select approval_reference from \"{_options.Schema}\".\"book_position_projections\" where position_id = @position_id;";
        approval.Parameters.AddWithValue("position_id", persistedPosition.PositionId);
        (await approval.ExecuteScalarAsync()).Should().Be(InstrumentPositionProjectionFixture.Approval.ApprovalReference);
    }

    [AssetOperationsDatabaseFact]
    public async Task DedicatedStore_ConcurrentOverlappingCreates_CommitAtMostOnePosition()
    {
        await new AssetOperationsMigrationRunner(_options).EnsureMigratedAsync();
        var left = InstrumentPositionProjectionFixture.CreateStrict();
        var right = InstrumentPositionProjectionFixture.ReidentifyStrict(left, Guid.NewGuid());
        var leftStore = new PostgresAssetOperationsProjectionStore(_options);
        var rightStore = new PostgresAssetOperationsProjectionStore(_options);

        var writes = await Task.WhenAll(
            Record.ExceptionAsync(() => leftStore.UpsertAsync(
                left.Role,
                left.Position,
                left.State,
                expectedVersion: 0,
                InstrumentPositionProjectionFixture.Approval)),
            Record.ExceptionAsync(() => rightStore.UpsertAsync(
                right.Role,
                right.Position,
                right.State,
                expectedVersion: 0,
                InstrumentPositionProjectionFixture.Approval)));

        writes.Count(error => error is null).Should().Be(1);
        writes.Count(error => error is not null).Should().Be(1);
        var snapshot = await leftStore.GetSecurityAsync(InstrumentPositionProjectionFixture.SecurityId);
        snapshot.BookPositions.Should().ContainSingle();
        snapshot.PositionEconomicStates.Should().ContainSingle();
    }

    [AssetOperationsDatabaseFact]
    public async Task DedicatedStore_ConcurrentSamePositionUpdates_ShouldCommitExactlyOneVersion()
    {
        await new AssetOperationsMigrationRunner(_options).EnsureMigratedAsync();
        var initial = InstrumentPositionProjectionFixture.CreateStrict();
        var seedStore = new PostgresAssetOperationsProjectionStore(_options);
        await seedStore.UpsertAsync(
            initial.Role,
            initial.Position,
            initial.State,
            expectedVersion: 0,
            InstrumentPositionProjectionFixture.Approval);
        var left = InstrumentPositionProjectionFixture.AdvanceStrict(initial);
        var rightSource = left.State.SourceEvent! with
        {
            EventId = Guid.NewGuid(),
            SourceEntityId = "factor-row-concurrent-right",
            SourceContentHash = "sha256:factor-row-concurrent-right"
        };
        var rightLineage = left.State.ProjectionLineage! with
        {
            ProjectionRunId = Guid.NewGuid(),
            ProjectionEventId = Guid.NewGuid(),
            TriggerEvent = rightSource,
            SourceEntityId = rightSource.SourceEntityId,
            TermsHash = rightSource.SourceContentHash
        };
        var rightState = left.State with
        {
            EconomicStateId = Guid.NewGuid(),
            CurrentFactor = left.State.CurrentFactor - 0.001m,
            SourceEvent = rightSource,
            ProjectionLineage = rightLineage
        };
        var right = left with
        {
            Position = left.Position with
            {
                CurrentEconomicState = rightState,
                ProjectionLineage = rightLineage
            },
            State = rightState
        };
        var leftStore = new PostgresAssetOperationsProjectionStore(_options);
        var rightStore = new PostgresAssetOperationsProjectionStore(_options);

        var writes = await Task.WhenAll(
            Record.ExceptionAsync(() => leftStore.UpsertAsync(
                left.Role,
                left.Position,
                left.State,
                expectedVersion: 1,
                InstrumentPositionProjectionFixture.Approval)),
            Record.ExceptionAsync(() => rightStore.UpsertAsync(
                right.Role,
                right.Position,
                right.State,
                expectedVersion: 1,
                InstrumentPositionProjectionFixture.Approval)));

        writes.Count(error => error is null).Should().Be(1);
        writes.Count(error => error is not null).Should().Be(1);
        var restarted = new PostgresAssetOperationsProjectionStore(_options);
        var persisted = await restarted.GetBookPositionAsync(initial.Position.PositionId);
        persisted!.Version.Should().Be(2);
        new[] { left.State.EconomicStateId, right.State.EconomicStateId }
            .Should().Contain(persisted.CurrentEconomicState!.EconomicStateId);
        (await restarted.GetSecurityAsync(initial.Position.SecurityId))
            .PositionEconomicStates.Should().HaveCount(2);
    }
}

internal static class InstrumentPositionProjectionFixture
{
    internal static readonly Guid SecurityId = Guid.Parse("a3000000-aaaa-4000-8000-000000000001");
    private static readonly Guid RoleId = Guid.Parse("a3000000-aaaa-4000-8000-000000000002");
    internal static readonly Guid PositionId = Guid.Parse("a3000000-aaaa-4000-8000-000000000003");
    internal static readonly Guid LedgerBookId = Guid.Parse("a3000000-aaaa-4000-8000-000000000004");

    internal static AssetOperationsWriteApprovalDto Approval { get; } = new(
        "controller@meridian.local",
        "approval-factor-position-1",
        "Approved retained MBS role and position projection.",
        DateTimeOffset.Parse("2026-05-26T12:00:00Z"));

    internal static AssetOperationsProjectionDto Create()
    {
        var factor = new FactorPaydownProjectionService().Project(new FactorPaydownProjectionRequest(
            SecurityId,
            PositionId,
            4,
            4,
            100_000m,
            0.9800m,
            0.9625m,
            "USD",
            new DateOnly(2026, 5, 25),
            DateTimeOffset.Parse("2026-05-25T00:00:00Z"),
            "SecurityMaster",
            "factor-row-2026-05",
            "sha256:factor-row-2026-05",
            ["evidence://factor/2026-05"]));
        var dimensions = new LedgerDimensionSetDto("fund-alpha", "entity-alpha", InstrumentId: SecurityId, BookId: LedgerBookId.ToString("D"))
        {
            PositionId = PositionId
        };
        var bookContext = new AccountingBookContextDto(
            LedgerBookId,
            "fund-alpha",
            Guid.Parse("a3000000-aaaa-4000-8000-000000000005"),
            FundStructureNodeKindDto.Fund,
            "Fund Alpha GAAP",
            "USD",
            AccountingBasisKindDto.Gaap,
            "gaap-mbs-v1",
            "v1",
            Dimensions: dimensions);
        var role = new InstrumentRoleDto(
            RoleId,
            SecurityId,
            "fund-alpha",
            "Fund",
            InstrumentRoleKinds.Holder,
            InstrumentAccountingSides.Debit,
            InstrumentEconomicSides.Asset,
            new DateOnly(2026, 1, 1),
            Version: 2,
            OriginEvent: factor.EconomicState!.SourceEvent,
            EvidenceLinks: ["evidence://position/holder"]);
        var position = new BookPositionDto(
            PositionId,
            SecurityId,
            RoleId,
            bookContext,
            BookPositionSides.Long,
            "Active",
            new DateOnly(2026, 1, 1),
            Version: 4,
            PrimaryAccountId: "Securities",
            CurrentEconomicState: factor.EconomicState,
            OriginEvent: factor.EconomicState!.SourceEvent,
            ProjectionLineage: factor.Lineage,
            EvidenceLinks: ["evidence://position/holder"]);
        var subject = new AssetOperationSubjectDto(
            SecurityId,
            "MortgageBackedSecurity",
            "Agency MBS Pool",
            "FNPOOL1",
            ["FactorProcessing"]);
        var readiness = new AssetOperationsReadinessDto(
            SecurityId,
            "Ready",
            [],
            [],
            [],
            [],
            DateTimeOffset.Parse("2026-05-26T12:00:00Z"),
            "AssetOperations",
            PositionId.ToString("D"));

        return new AssetOperationsProjectionDto(subject, [], [], [], [], [], [], [], [], readiness, [])
        {
            InstrumentRoles = [role],
            BookPositions = [position],
            PositionEconomicStates = [factor.EconomicState!],
            ProjectionLineages = [factor.Lineage!]
        };
    }

    internal static AssetOperationsProjectionDto Advance(AssetOperationsProjectionDto projection)
    {
        var factor = new FactorPaydownProjectionService().Project(new FactorPaydownProjectionRequest(
            SecurityId,
            PositionId,
            5,
            5,
            100_000m,
            0.9625m,
            0.9500m,
            "USD",
            new DateOnly(2026, 6, 25),
            DateTimeOffset.Parse("2026-06-25T00:00:00Z"),
            "SecurityMaster",
            "factor-row-2026-06",
            "sha256:factor-row-2026-06",
            ["evidence://factor/2026-06"]));
        var position = projection.BookPositions.Single() with
        {
            Version = 5,
            CurrentEconomicState = factor.EconomicState,
            ProjectionLineage = factor.Lineage
        };

        return projection with
        {
            BookPositions = [position],
            PositionEconomicStates = [factor.EconomicState!],
            ProjectionLineages = [factor.Lineage!]
        };
    }

    internal static StrictProjection CreateStrict()
    {
        var projection = Create();
        var state = projection.PositionEconomicStates.Single() with { Version = 1 };
        var role = projection.InstrumentRoles.Single() with { Version = 1 };
        var position = projection.BookPositions.Single() with
        {
            Version = 1,
            CurrentEconomicState = state,
            ProjectionLineage = state.ProjectionLineage
        };
        return new StrictProjection(role, position, state);
    }

    internal static StrictProjection AdvanceStrict(StrictProjection current)
    {
        var projection = Create() with
        {
            InstrumentRoles = [current.Role],
            BookPositions = [current.Position],
            PositionEconomicStates = [current.State],
            ProjectionLineages = current.State.ProjectionLineage is null ? [] : [current.State.ProjectionLineage!]
        };
        var advanced = Advance(projection);
        var state = advanced.PositionEconomicStates.Single() with { Version = 2 };
        var position = advanced.BookPositions.Single() with
        {
            Version = 2,
            CurrentEconomicState = state,
            ProjectionLineage = state.ProjectionLineage
        };
        return new StrictProjection(current.Role, position, state);
    }

    internal static StrictProjection ReidentifyStrict(StrictProjection source, Guid positionId)
    {
        var sourceEvent = source.State.SourceEvent! with
        {
            EventId = Guid.NewGuid(),
            BookPositionId = positionId
        };
        var lineage = source.State.ProjectionLineage! with
        {
            ProjectionRunId = Guid.NewGuid(),
            ProjectionEventId = Guid.NewGuid(),
            TriggerEvent = sourceEvent,
            BookPositionId = positionId
        };
        var state = source.State with
        {
            EconomicStateId = Guid.NewGuid(),
            PositionId = positionId,
            SourceEvent = sourceEvent,
            ProjectionLineage = lineage
        };
        var position = source.Position with
        {
            PositionId = positionId,
            BookContext = source.Position.BookContext with
            {
                Dimensions = source.Position.BookContext.Dimensions! with { PositionId = positionId }
            },
            CurrentEconomicState = state,
            OriginEvent = sourceEvent,
            ProjectionLineage = lineage
        };
        return new StrictProjection(source.Role, position, state);
    }

    internal sealed record StrictProjection(
        InstrumentRoleDto Role,
        BookPositionDto Position,
        PositionEconomicStateDto State);
}
