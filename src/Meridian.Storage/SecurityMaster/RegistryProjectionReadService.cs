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
/// Executes one registry select for one security and returns every row as named JSON columns. The
/// seam between the descriptor-driven read and PostgreSQL, so the read itself is testable without
/// a database.
/// </summary>
internal interface IRegistryProjectionRowSource
{
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
        _rows = rows ?? new PostgresRowSource(_reader);
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

        var parentColumns = SecurityTermsProjectionRegistry.ParentColumnNames(descriptor);
        var parentRows = await _rows.ReadAsync(
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
            var rows = await _rows.ReadAsync(
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
            var rows = await _rows.ReadAsync(
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

    private sealed class PostgresRowSource : IRegistryProjectionRowSource
    {
        private readonly PostgresReferenceProjectionReader _reader;

        public PostgresRowSource(PostgresReferenceProjectionReader reader)
        {
            _reader = reader;
        }

        public Task<IReadOnlyList<IReadOnlyDictionary<string, JsonElement>>> ReadAsync(
            string sql,
            IReadOnlyList<string> columns,
            Guid securityId,
            CancellationToken ct)
            => _reader.QueryAsync(
                sql,
                [new NpgsqlParameter("security_id", securityId)],
                row => row.ToJsonColumns(columns),
                ct);
    }
}
