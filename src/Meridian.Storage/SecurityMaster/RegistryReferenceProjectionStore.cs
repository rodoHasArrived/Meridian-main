using Meridian.Contracts.SecurityMaster;
using Npgsql;

namespace Meridian.Storage.SecurityMaster;

/// <summary>
/// One named read of a registry projection table: a <c>where … order by …</c> predicate over the
/// descriptor's select. The name identifies the query in diagnostics and tests; the SQL is the
/// predicate text the hand-written store carried, unchanged.
/// </summary>
/// <param name="Name">Unique within its store, e.g. <c>ByNetwork</c>.</param>
/// <param name="PredicateSql">The text appended after <c>from &lt;schema&gt;.&lt;table&gt;</c>.</param>
internal sealed record RegistryProjectionQuery(string Name, string PredicateSql)
{
    /// <summary>The by-security read every parent projection table answers.</summary>
    internal static RegistryProjectionQuery BySecurityId { get; } = new("BySecurityId", "where security_id = @security_id");
}

/// <summary>
/// The generic read side of one <see cref="SecurityTermsProjectionRegistry"/> table — a
/// descriptor's parent table or one of its child tables — for a typed row. A per-class store
/// supplies only what differs between classes: the asset class (and child table), the row mapper,
/// and its named predicate queries. The select list, the <c>schema.table</c> qualification, the
/// connection and the reader loop all come from <see cref="PostgresReferenceProjectionReader"/>,
/// so a class's read path is a declaration over the same descriptor its writer binds.
/// <para>
/// Every query a store runs must be declared up front. The SQL for each one is built once, at
/// construction, from the descriptor; a store naming a class or child table the registry does not
/// carry therefore fails when it is constructed rather than on its first read, and
/// <see cref="Queries"/> lists everything the store can ask of the table.
/// </para>
/// <para>
/// The <c>I*ReferenceProjectionStore</c> interfaces stay as they are: each Postgres store
/// implements its interface by delegating to one of these per table. Bond (joined reads and
/// keyset paging over <c>securities</c>) and Option (contract-keyed tables with a second, quote-path
/// writer) keep their specialized stores.
/// </para>
/// </summary>
/// <typeparam name="TRow">The typed row the mapper produces.</typeparam>
internal sealed class RegistryReferenceProjectionStore<TRow>
    where TRow : class
{
    private readonly PostgresReferenceProjectionReader _reader;
    private readonly Func<ProjectionRow, TRow> _map;
    private readonly Dictionary<string, (RegistryProjectionQuery Query, string Sql)> _queries;

    /// <summary>
    /// Builds the store over <paramref name="assetClass"/>'s parent table, or over its child table
    /// <paramref name="childTableName"/> when one is given. A parent store always answers
    /// <see cref="RegistryProjectionQuery.BySecurityId"/>; a child store answers only what it declares.
    /// </summary>
    public RegistryReferenceProjectionStore(
        SecurityMasterOptions options,
        string assetClass,
        Func<ProjectionRow, TRow> map,
        IReadOnlyList<RegistryProjectionQuery> queries,
        string? childTableName = null)
    {
        ArgumentNullException.ThrowIfNull(queries);
        ArgumentException.ThrowIfNullOrWhiteSpace(assetClass);
        _reader = new PostgresReferenceProjectionReader(options);
        _map = map ?? throw new ArgumentNullException(nameof(map));
        AssetClass = assetClass;
        ChildTableName = childTableName;

        IEnumerable<RegistryProjectionQuery> declared = childTableName is null
            ? queries.Prepend(RegistryProjectionQuery.BySecurityId)
            : queries;

        _queries = new Dictionary<string, (RegistryProjectionQuery, string)>(StringComparer.Ordinal);
        foreach (var query in declared)
        {
            ArgumentNullException.ThrowIfNull(query);
            var sql = childTableName is null
                ? _reader.SelectSql(assetClass, query.PredicateSql)
                : _reader.SelectChildSql(assetClass, childTableName, query.PredicateSql);
            if (!_queries.TryAdd(query.Name, (query, sql)))
            {
                throw new ArgumentException(
                    $"The '{assetClass}' projection store declares the query '{query.Name}' more than once.",
                    nameof(queries));
            }
        }
    }

    /// <summary>The registry asset class whose table this store reads.</summary>
    public string AssetClass { get; }

    /// <summary>The child table read, or <see langword="null"/> for the descriptor's parent table.</summary>
    public string? ChildTableName { get; }

    /// <summary>Every query the store can run, in declaration order (the by-security read first on a parent store).</summary>
    public IReadOnlyList<RegistryProjectionQuery> Queries => _queries.Values.Select(static entry => entry.Query).ToArray();

    /// <summary>The full select statement for <paramref name="query"/>, exactly as it is sent.</summary>
    public string SqlFor(RegistryProjectionQuery query) => Resolve(query);

    /// <summary>The parent row for <paramref name="securityId"/>, or <see langword="null"/>.</summary>
    public Task<TRow?> GetBySecurityIdAsync(Guid securityId, CancellationToken ct)
        => SingleAsync(RegistryProjectionQuery.BySecurityId, [new NpgsqlParameter("security_id", securityId)], ct);

    /// <summary>Every row <paramref name="query"/> matches, in the order its predicate states.</summary>
    public Task<IReadOnlyList<TRow>> ListAsync(
        RegistryProjectionQuery query,
        IReadOnlyList<NpgsqlParameter> parameters,
        CancellationToken ct)
        => _reader.QueryAsync(Resolve(query), parameters, _map, ct);

    /// <summary>The first row <paramref name="query"/> matches, or <see langword="null"/>.</summary>
    public Task<TRow?> SingleAsync(
        RegistryProjectionQuery query,
        IReadOnlyList<NpgsqlParameter> parameters,
        CancellationToken ct)
        => _reader.QuerySingleAsync(Resolve(query), parameters, _map, ct);

    private string Resolve(RegistryProjectionQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (_queries.TryGetValue(query.Name, out var entry) && entry.Query == query)
        {
            return entry.Sql;
        }

        throw new InvalidOperationException(
            $"The '{AssetClass}' projection store{(ChildTableName is null ? string.Empty : $" for '{ChildTableName}'")} " +
            $"does not declare the query '{query.Name}'.");
    }
}
