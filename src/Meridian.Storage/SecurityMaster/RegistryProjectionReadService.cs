using System.Text.Json;
using Meridian.Contracts.SecurityMaster;
using Npgsql;

namespace Meridian.Storage.SecurityMaster;

/// <summary>
/// Reads any <see cref="SecurityTermsProjectionRegistry"/> projection as column→value data, driven
/// by the class's descriptor rather than a typed row. This is the read path for the registry classes
/// that never had a typed reader (CommercialPaper through CommitmentGuarantee, the OTC derivatives,
/// the rate options and linked notes); the typed <c>I*ReferenceProjectionStore</c> readers remain the
/// path for the classes that have one.
/// </summary>
public interface IRegistryProjectionReadService
{
    /// <summary>
    /// The projection of <paramref name="securityId"/> read as <paramref name="assetClass"/>, or
    /// <see langword="null"/> when the class has no registry descriptor, the security does not
    /// exist, its current asset class is a different class, or it has no projection row (a
    /// gating term is absent). <paramref name="assetClass"/> accepts a catalog name or alias in
    /// any casing.
    /// </summary>
    Task<SecurityTermsProjectionReadDto?> GetAsync(string assetClass, Guid securityId, CancellationToken ct = default);
}

/// <summary>
/// Opens the single-snapshot scope one registry read runs in. The seam between the
/// descriptor-driven read and PostgreSQL, so the read itself is testable without a database.
/// </summary>
internal interface IRegistryProjectionRowSource
{
    /// <summary>
    /// Opens one snapshot: every read through it sees the same committed database state, so the
    /// class gate, parent row, child tables and satellites of one composite read agree.
    /// </summary>
    Task<IRegistryProjectionRowSnapshot> OpenSnapshotAsync(CancellationToken ct);
}

/// <summary>One consistent view of the Security Master tables for one composite read.</summary>
internal interface IRegistryProjectionRowSnapshot : IAsyncDisposable
{
    /// <summary>
    /// <see langword="true"/> when the security exists in this snapshot and its current asset
    /// class (canonicalized) is <paramref name="assetClass"/>.
    /// </summary>
    Task<bool> IsCurrentAssetClassAsync(Guid securityId, string assetClass, CancellationToken ct);

    /// <summary>Executes one registry select for one security and returns every row as named JSON columns.</summary>
    Task<IReadOnlyList<IReadOnlyDictionary<string, JsonElement>>> ReadAsync(
        string sql,
        IReadOnlyList<string> columns,
        Guid securityId,
        CancellationToken ct);
}

/// <summary>
/// The PostgreSQL implementation of <see cref="IRegistryProjectionReadService"/>. The security's
/// current asset class gates the read: it is taken from <see cref="ISecurityMasterStore.GetProjectionAsync"/>
/// and must be the requested class, so a request can never read another class's table for a
/// security, nor a stale row a reclassified security left behind.
/// <para>
/// The composite itself is read from one database snapshot: the class is confirmed again, and the
/// parent row, every child table and every satellite are read, on one connection inside one
/// read-only <c>REPEATABLE READ</c> transaction. The writer commits all of a security's tables
/// together, so an amend that commits while the read is under way is either wholly visible or
/// wholly invisible; the result is never an old parent with new child rows.
/// </para>
/// </summary>
public sealed class RegistryProjectionReadService : IRegistryProjectionReadService
{
    private const string BySecurityId = "where security_id = @security_id";
    private const string ChildrenBySecurityId = "where security_id = @security_id order by ordinal";

    private readonly ISecurityMasterStore _store;
    private readonly PostgresReferenceProjectionReader _reader;
    private readonly IRegistryProjectionRowSource _rows;

    public RegistryProjectionReadService(SecurityMasterOptions options, ISecurityMasterStore store)
        : this(options, store, rows: null)
    {
    }

    internal RegistryProjectionReadService(
        SecurityMasterOptions options,
        ISecurityMasterStore store,
        IRegistryProjectionRowSource? rows)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _reader = new PostgresReferenceProjectionReader(options);
        _rows = rows ?? new PostgresRegistryProjectionRowSource(_reader);
    }

    /// <summary>
    /// The registry descriptor <paramref name="assetClass"/> names, resolving catalog aliases and
    /// casing first; <see langword="false"/> for a class without a registry projection (Option,
    /// CustomAsset, or no catalog class at all).
    /// </summary>
    internal static bool TryResolveDescriptor(string? assetClass, out SecurityTermsProjectionDescriptor? descriptor)
    {
        descriptor = null;
        if (!SecurityAssetClassCatalog.TryGet(assetClass, out var catalogClass))
        {
            return false;
        }

        return SecurityTermsProjectionRegistry.TryGet(catalogClass.AssetClass, out descriptor);
    }

    public async Task<SecurityTermsProjectionReadDto?> GetAsync(string assetClass, Guid securityId, CancellationToken ct = default)
    {
        if (!TryResolveDescriptor(assetClass, out var descriptor) || descriptor is null)
        {
            return null;
        }

        var record = await _store.GetProjectionAsync(securityId, ct).ConfigureAwait(false);
        if (record is null
            || !string.Equals(
                SecurityAssetClassCatalog.Canonicalize(record.AssetClass),
                descriptor.AssetClass,
                StringComparison.Ordinal))
        {
            return null;
        }

        await using var snapshot = await _rows.OpenSnapshotAsync(ct).ConfigureAwait(false);

        // The store gate above answers the request; this confirms it inside the snapshot the rows
        // are read from, so a reclassification committed in between cannot pair with stale rows.
        if (!await snapshot.IsCurrentAssetClassAsync(securityId, descriptor.AssetClass, ct).ConfigureAwait(false))
        {
            return null;
        }

        var parentColumns = SecurityTermsProjectionRegistry.ParentColumnNames(descriptor);
        var parentRows = await snapshot.ReadAsync(
                _reader.SelectSql(descriptor.AssetClass, BySecurityId),
                parentColumns,
                securityId,
                ct)
            .ConfigureAwait(false);
        if (parentRows.Count == 0)
        {
            return null;
        }

        var children = new List<SecurityTermsProjectionChildRowsDto>(descriptor.ChildTables.Count);
        foreach (var child in descriptor.ChildTables)
        {
            var rows = await snapshot.ReadAsync(
                    _reader.SelectChildSql(descriptor.AssetClass, child.TableName, ChildrenBySecurityId),
                    SecurityTermsProjectionRegistry.ChildColumnNames(child),
                    securityId,
                    ct)
                .ConfigureAwait(false);
            children.Add(new SecurityTermsProjectionChildRowsDto(child.TableName, rows));
        }

        var satellites = new List<SecurityTermsProjectionSatelliteRowDto>(descriptor.Satellites.Count);
        foreach (var satellite in descriptor.Satellites)
        {
            var rows = await snapshot.ReadAsync(
                    _reader.SelectSatelliteSql(descriptor.AssetClass, satellite.TableName, BySecurityId),
                    SecurityTermsProjectionRegistry.SatelliteColumnNames(satellite),
                    securityId,
                    ct)
                .ConfigureAwait(false);
            satellites.Add(new SecurityTermsProjectionSatelliteRowDto(satellite.TableName, rows.Count == 0 ? null : rows[0]));
        }

        return new SecurityTermsProjectionReadDto(
            securityId,
            descriptor.AssetClass,
            descriptor.TableName,
            parentRows[0],
            children,
            satellites);
    }

}

/// <summary>
/// The PostgreSQL <see cref="IRegistryProjectionRowSource"/>: each snapshot is one connection with
/// one read-only <c>REPEATABLE READ</c> transaction from
/// <see cref="PostgresReferenceProjectionReader.BeginSnapshotAsync"/>.
/// </summary>
internal sealed class PostgresRegistryProjectionRowSource : IRegistryProjectionRowSource
{
    private readonly PostgresReferenceProjectionReader _reader;

    public PostgresRegistryProjectionRowSource(SecurityMasterOptions options)
        : this(new PostgresReferenceProjectionReader(options))
    {
    }

    internal PostgresRegistryProjectionRowSource(PostgresReferenceProjectionReader reader)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
    }

    public async Task<IRegistryProjectionRowSnapshot> OpenSnapshotAsync(CancellationToken ct)
        => new Snapshot(_reader, await _reader.BeginSnapshotAsync(ct).ConfigureAwait(false));

    private sealed class Snapshot : IRegistryProjectionRowSnapshot
    {
        private readonly PostgresReferenceProjectionReader _reader;
        private readonly ProjectionReadSnapshot _snapshot;

        public Snapshot(PostgresReferenceProjectionReader reader, ProjectionReadSnapshot snapshot)
        {
            _reader = reader;
            _snapshot = snapshot;
        }

        public async Task<bool> IsCurrentAssetClassAsync(Guid securityId, string assetClass, CancellationToken ct)
        {
            var current = await _snapshot.QuerySingleAsync(
                    $"select asset_class from {_reader.Qualified("securities")} where security_id = @security_id;",
                    [new NpgsqlParameter("security_id", securityId)],
                    static row => row.String("asset_class"),
                    ct)
                .ConfigureAwait(false);
            return current is not null
                && string.Equals(SecurityAssetClassCatalog.Canonicalize(current), assetClass, StringComparison.Ordinal);
        }

        public Task<IReadOnlyList<IReadOnlyDictionary<string, JsonElement>>> ReadAsync(
            string sql,
            IReadOnlyList<string> columns,
            Guid securityId,
            CancellationToken ct)
            => _snapshot.QueryAsync(
                sql,
                [new NpgsqlParameter("security_id", securityId)],
                row => row.ToJsonColumns(columns),
                ct);

        public ValueTask DisposeAsync() => _snapshot.DisposeAsync();
    }
}
