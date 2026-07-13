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
        await store.UpsertAsync(advanced, InstrumentPositionProjectionFixture.Approval);

        var persisted = await store.GetAsync(InstrumentPositionProjectionFixture.SecurityId);
        persisted.Should().NotBeNull();
        persisted!.InstrumentRoles.Should().BeEquivalentTo(projection.InstrumentRoles);
        persisted.BookPositions.Should().BeEquivalentTo(advanced.BookPositions);
        persisted.PositionEconomicStates.Should().HaveCount(2);
        persisted.ProjectionLineages.Should().HaveCount(2);
        persisted.PositionEconomicStates.Should().OnlyContain(state => state.ProjectionLineage != null);
    }

    [AssetOperationsDatabaseFact]
    public async Task UpsertAsync_ShouldRejectPositionOwnershipChangesAndPreserveReplayApproval()
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
            .WithMessage("*stale or conflicts*");
    }
}

internal static class InstrumentPositionProjectionFixture
{
    internal static readonly Guid SecurityId = Guid.Parse("a3000000-aaaa-4000-8000-000000000001");
    private static readonly Guid RoleId = Guid.Parse("a3000000-aaaa-4000-8000-000000000002");
    internal static readonly Guid PositionId = Guid.Parse("a3000000-aaaa-4000-8000-000000000003");
    private static readonly Guid LedgerBookId = Guid.Parse("a3000000-aaaa-4000-8000-000000000004");

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
}
