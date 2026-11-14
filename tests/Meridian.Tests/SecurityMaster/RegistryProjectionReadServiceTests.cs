using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using NSubstitute;
using Xunit;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// The generic registry read answers for every <see cref="SecurityTermsProjectionRegistry"/>
/// descriptor from the descriptor alone: the parent select, each child table and each satellite,
/// with the column set and order the descriptor declares. The security's current asset class
/// gates the read, so an unknown class, a class without a registry projection, a mismatched class
/// and a security without a projection row answer null without reading a projection table.
/// </summary>
public sealed class RegistryProjectionReadServiceTests
{
    private static readonly SecurityMasterOptions Options = new() { Schema = "sm_test", ConnectionString = "Host=unused" };
    private static readonly Guid SecurityId = Guid.Parse("4c0f2b1e-9d8a-4e7f-b6c5-a4b3c2d1e0f9");

    public static TheoryData<string> EveryDescriptor()
    {
        var data = new TheoryData<string>();
        foreach (var assetClass in SecurityTermsProjectionRegistry.AssetClasses)
        {
            data.Add(assetClass);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(EveryDescriptor))]
    public async Task GetAsync_ReadsEveryTableTheDescriptorDeclares_InDescriptorOrder(string assetClass)
    {
        var descriptor = SecurityTermsProjectionRegistry.Require(assetClass);
        var rows = new FakeRowSource();
        var service = new RegistryProjectionReadService(Options, StoreReturning(assetClass), rows);

        var projection = await service.GetAsync(assetClass, SecurityId);

        projection.Should().NotBeNull();
        projection!.SecurityId.Should().Be(SecurityId);
        projection.AssetClass.Should().Be(assetClass);
        projection.TableName.Should().Be(descriptor.TableName);
        projection.Columns.Keys.Should().Equal(SecurityTermsProjectionRegistry.ParentColumnNames(descriptor));
        projection.Columns.Values.Select(value => value.GetString())
            .Should().Equal(SecurityTermsProjectionRegistry.ParentColumnNames(descriptor).Select(column => $"{descriptor.TableName}.{column}"));

        projection.ChildTables.Select(child => child.TableName).Should().Equal(descriptor.ChildTables.Select(child => child.TableName));
        foreach (var child in descriptor.ChildTables)
        {
            var read = projection.ChildTables.Single(table => table.TableName == child.TableName);
            read.Rows.Should().HaveCount(FakeRowSource.ChildRowCount);
            read.Rows.Should().AllSatisfy(row => row.Keys.Should().Equal(SecurityTermsProjectionRegistry.ChildColumnNames(child)));
        }

        projection.SatelliteTables.Select(satellite => satellite.TableName).Should().Equal(descriptor.Satellites.Select(satellite => satellite.TableName));
        foreach (var satellite in descriptor.Satellites)
        {
            projection.SatelliteTables.Single(table => table.TableName == satellite.TableName).Row!.Keys
                .Should().Equal(SecurityTermsProjectionRegistry.SatelliteColumnNames(satellite));
        }

        var reader = new PostgresReferenceProjectionReader(Options);
        rows.Statements.Should().Equal(
            new[] { reader.SelectSql(assetClass, "where security_id = @security_id") }
                .Concat(descriptor.ChildTables.Select(child =>
                    reader.SelectChildSql(assetClass, child.TableName, "where security_id = @security_id order by ordinal")))
                .Concat(descriptor.Satellites.Select(satellite =>
                    reader.SelectSatelliteSql(assetClass, satellite.TableName, "where security_id = @security_id"))));
        rows.SecurityIds.Should().AllBeEquivalentTo(SecurityId);
    }

    [Fact]
    public void EveryDescriptor_IsACatalogClassTheReadResolves()
    {
        // 30 descriptors: the 12 classes with typed readers and the 18 that have only this read.
        SecurityTermsProjectionRegistry.AssetClasses.Should().HaveCount(30);
        foreach (var assetClass in SecurityTermsProjectionRegistry.AssetClasses)
        {
            RegistryProjectionReadService.TryResolveDescriptor(assetClass, out var descriptor).Should().BeTrue(assetClass);
            descriptor!.AssetClass.Should().Be(assetClass);
        }
    }

    [Theory]
    [InlineData("directloan", "DirectLoan")]
    [InlineData("  CommercialPaper ", "CommercialPaper")]
    public async Task GetAsync_AcceptsACatalogSpellingInAnyCasing(string requested, string canonical)
    {
        var service = new RegistryProjectionReadService(Options, StoreReturning(canonical), new FakeRowSource());

        var projection = await service.GetAsync(requested, SecurityId);

        projection!.AssetClass.Should().Be(canonical);
    }

    [Theory]
    [InlineData("Option")]
    [InlineData("CustomAsset")]
    [InlineData("InfrastructureHolding")]
    [InlineData("")]
    public async Task GetAsync_AClassWithoutARegistryProjection_IsNull_WithoutTouchingTheStore(string assetClass)
    {
        var store = Substitute.For<ISecurityMasterStore>();
        var rows = new FakeRowSource();
        var service = new RegistryProjectionReadService(Options, store, rows);

        (await service.GetAsync(assetClass, SecurityId)).Should().BeNull();

        await store.DidNotReceiveWithAnyArgs().GetProjectionAsync(default, default);
        rows.Statements.Should().BeEmpty();
        rows.SnapshotsOpened.Should().Be(0);
    }

    [Fact]
    public async Task GetAsync_ASecurityOfAnotherClass_OrNoSecurity_IsNull_WithoutReadingAProjectionTable()
    {
        var rows = new FakeRowSource();

        var mismatch = new RegistryProjectionReadService(Options, StoreReturning("Repo"), rows);
        (await mismatch.GetAsync("CommercialPaper", SecurityId)).Should().BeNull();

        var missing = new RegistryProjectionReadService(Options, Substitute.For<ISecurityMasterStore>(), rows);
        (await missing.GetAsync("CommercialPaper", SecurityId)).Should().BeNull();

        rows.Statements.Should().BeEmpty();
        rows.SnapshotsOpened.Should().Be(0);
    }

    [Fact]
    public async Task GetAsync_NoProjectionRow_IsNull_AndAnAbsentSatelliteRowIsNull()
    {
        var gated = new RegistryProjectionReadService(Options, StoreReturning("DirectLoan"), new FakeRowSource { ParentRows = 0 });
        (await gated.GetAsync("DirectLoan", SecurityId)).Should().BeNull();

        var bond = SecurityTermsProjectionRegistry.Require("Bond");
        var issuerless = new FakeRowSource { EmptyTables = { bond.Satellites[^1].TableName } };
        var projection = await new RegistryProjectionReadService(Options, StoreReturning("Bond"), issuerless).GetAsync("Bond", SecurityId);

        projection!.SatelliteTables.Single(table => table.TableName == bond.Satellites[^1].TableName).Row.Should().BeNull();
    }

    [Theory]
    [MemberData(nameof(EveryDescriptor))]
    public async Task GetAsync_ReadsTheGateParentChildrenAndSatellites_InOneSnapshot(string assetClass)
    {
        var descriptor = SecurityTermsProjectionRegistry.Require(assetClass);
        var rows = new FakeRowSource();
        var service = new RegistryProjectionReadService(Options, StoreReturning(assetClass), rows);

        (await service.GetAsync(assetClass, SecurityId)).Should().NotBeNull();

        rows.SnapshotsOpened.Should().Be(1, "the whole composite is read on one connection and transaction");
        rows.SnapshotsDisposed.Should().Be(1, "the snapshot is released when the read completes");
        rows.GateSnapshots.Should().Equal(1);
        rows.StatementSnapshots.Should().HaveCount(1 + descriptor.ChildTables.Count + descriptor.Satellites.Count)
            .And.AllBeEquivalentTo(1);
    }

    [Fact]
    public async Task GetAsync_AReclassificationVisibleInTheSnapshot_IsNull_WithoutReadingAProjectionTable()
    {
        // The store gate saw DirectLoan; by the time the snapshot was taken the security had been
        // reclassified. The snapshot's own gate decides, so no stale DirectLoan row is returned.
        var rows = new FakeRowSource { CurrentAssetClassInSnapshot = "Repo" };
        var service = new RegistryProjectionReadService(Options, StoreReturning("DirectLoan"), rows);

        (await service.GetAsync("DirectLoan", SecurityId)).Should().BeNull();

        rows.GateSnapshots.Should().Equal(1);
        rows.Statements.Should().BeEmpty();
        rows.SnapshotsDisposed.Should().Be(1);
    }

    [Fact]
    public async Task GetAsync_NoProjectionRow_StillReleasesTheSnapshot()
    {
        var rows = new FakeRowSource { ParentRows = 0 };
        var service = new RegistryProjectionReadService(Options, StoreReturning("DirectLoan"), rows);

        (await service.GetAsync("DirectLoan", SecurityId)).Should().BeNull();

        rows.SnapshotsOpened.Should().Be(1);
        rows.SnapshotsDisposed.Should().Be(1);
    }

    [Fact]
    public void Dto_SerializesThroughItsSourceGeneratedContext()
    {
        var dto = new SecurityTermsProjectionReadDto(
            SecurityId,
            "CommercialPaper",
            "commercial_paper_projection",
            new Dictionary<string, JsonElement> { ["discount_rate"] = JsonDocument.Parse("0.0525").RootElement.Clone() },
            [],
            []);

        var json = JsonSerializer.Serialize(dto, SecurityTermsProjectionReadJsonContext.Default.SecurityTermsProjectionReadDto);

        json.Should().Contain("\"discount_rate\":0.0525");
    }

    internal static ISecurityMasterStore StoreReturning(string assetClass)
    {
        var store = Substitute.For<ISecurityMasterStore>();
        store.GetProjectionAsync(SecurityId, Arg.Any<CancellationToken>()).Returns(Record(SecurityId, assetClass));
        return store;
    }

    internal static SecurityProjectionRecord Record(Guid securityId, string assetClass)
    {
        var empty = JsonDocument.Parse("{}").RootElement.Clone();
        return new SecurityProjectionRecord(
            securityId, assetClass, SecurityStatusDto.Active, "Display", "USD", "InternalCode", "PRIMARY",
            empty, empty, empty, 3L, DateTimeOffset.UnixEpoch, null, [], []);
    }

    /// <summary>
    /// Answers each select with rows whose values name their table and column, so a test can see
    /// which table a value came from. Records every statement in the order it was sent, and which
    /// snapshot (one per <see cref="OpenSnapshotAsync"/>) each gate check and statement ran in.
    /// </summary>
    internal sealed class FakeRowSource : IRegistryProjectionRowSource
    {
        public const int ChildRowCount = 2;

        public int ParentRows { get; init; } = 1;

        /// <summary>When set, the snapshot reports this as the security's current class (a reclassification).</summary>
        public string? CurrentAssetClassInSnapshot { get; init; }

        public HashSet<string> EmptyTables { get; } = new(StringComparer.Ordinal);

        public List<string> Statements { get; } = [];

        public List<Guid> SecurityIds { get; } = [];

        /// <summary>The snapshot number each statement in <see cref="Statements"/> ran in.</summary>
        public List<int> StatementSnapshots { get; } = [];

        /// <summary>The snapshot number of each class gate check, in order.</summary>
        public List<int> GateSnapshots { get; } = [];

        public int SnapshotsOpened { get; private set; }

        public int SnapshotsDisposed { get; private set; }

        public Task<IRegistryProjectionRowSnapshot> OpenSnapshotAsync(CancellationToken ct)
        {
            SnapshotsOpened++;
            return Task.FromResult<IRegistryProjectionRowSnapshot>(new Snapshot(this, SnapshotsOpened));
        }

        private sealed class Snapshot : IRegistryProjectionRowSnapshot
        {
            private readonly FakeRowSource _source;
            private readonly int _number;
            private bool _disposed;

            public Snapshot(FakeRowSource source, int number)
            {
                _source = source;
                _number = number;
            }

            public Task<bool> IsCurrentAssetClassAsync(Guid securityId, string assetClass, CancellationToken ct)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                _source.GateSnapshots.Add(_number);
                return Task.FromResult(
                    _source.CurrentAssetClassInSnapshot is null
                    || string.Equals(_source.CurrentAssetClassInSnapshot, assetClass, StringComparison.Ordinal));
            }

            public Task<IReadOnlyList<IReadOnlyDictionary<string, JsonElement>>> ReadAsync(
                string sql,
                IReadOnlyList<string> columns,
                Guid securityId,
                CancellationToken ct)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                _source.StatementSnapshots.Add(_number);
                return _source.ReadAsync(sql, columns, securityId);
            }

            public ValueTask DisposeAsync()
            {
                if (!_disposed)
                {
                    _disposed = true;
                    _source.SnapshotsDisposed++;
                }

                return ValueTask.CompletedTask;
            }
        }

        private Task<IReadOnlyList<IReadOnlyDictionary<string, JsonElement>>> ReadAsync(
            string sql,
            IReadOnlyList<string> columns,
            Guid securityId)
        {
            Statements.Add(sql);
            SecurityIds.Add(securityId);

            var table = TableOf(sql);
            var parent = SecurityTermsProjectionRegistry.Descriptors.Any(descriptor => descriptor.TableName == table);
            var child = SecurityTermsProjectionRegistry.Descriptors.Any(descriptor => descriptor.ChildTables.Any(c => c.TableName == table));
            var count = EmptyTables.Contains(table) ? 0 : parent ? ParentRows : child ? ChildRowCount : 1;

            IReadOnlyList<IReadOnlyDictionary<string, JsonElement>> rows = Enumerable.Range(0, count)
                .Select(_ => (IReadOnlyDictionary<string, JsonElement>)columns.ToDictionary(
                    column => column,
                    column => JsonDocument.Parse(JsonSerializer.Serialize($"{table}.{column}")).RootElement.Clone(),
                    StringComparer.Ordinal))
                .ToArray();
            return Task.FromResult(rows);
        }

        private static string TableOf(string sql)
        {
            const string marker = " from sm_test.";
            var start = sql.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
            var end = sql.IndexOf(' ', start);
            return sql[start..end];
        }
    }
}
