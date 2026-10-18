using System.Data.Common;
using Meridian.Contracts.SecurityMaster;
using Npgsql;

namespace Meridian.Storage.SecurityMaster;

/// <summary>
/// The shared read side of the Security Master projection tables. Every
/// <c>Postgres*ReferenceProjectionStore</c> composes one of these instead of carrying its own
/// connection, command, reader loop and <c>schema.table</c> qualification, and a store whose table
/// is a <see cref="SecurityTermsProjectionRegistry"/> descriptor selects the descriptor's columns
/// rather than restating them: the registry is the single statement of what a projection table
/// holds, on the write side and the read side alike.
/// </summary>
internal sealed class PostgresReferenceProjectionReader
{
    private readonly SecurityMasterOptions _options;

    public PostgresReferenceProjectionReader(SecurityMasterOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>Qualifies <paramref name="table"/> with the configured Security Master schema.</summary>
    public string Qualified(string table) => $"{_options.Schema}.{table}";

    /// <summary>
    /// Opens a connection to the Security Master database. Stores that write (the option-chain
    /// upsert) use this directly; readers go through <see cref="QueryAsync{TRow}"/>.
    /// </summary>
    public async Task<NpgsqlConnection> OpenConnectionAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.ConnectionString))
        {
            throw new InvalidOperationException("SecurityMasterOptions.ConnectionString is not configured.");
        }

        var connection = new NpgsqlConnection(_options.ConnectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);
        return connection;
    }

    /// <summary>
    /// Builds <c>select &lt;descriptor columns&gt; from &lt;schema&gt;.&lt;descriptor table&gt; &lt;predicate&gt;;</c>
    /// for the registry descriptor of <paramref name="assetClass"/>. The column list is the one the
    /// writer binds (<see cref="SecurityTermsProjectionRegistry.ParentColumnNames"/>), so a row
    /// mapper that reads by column name can only name columns the table actually has.
    /// </summary>
    public string SelectSql(string assetClass, string predicateSql)
    {
        var descriptor = SecurityTermsProjectionRegistry.Require(assetClass);
        return BuildSelect(
            SecurityTermsProjectionRegistry.ParentColumnNames(descriptor),
            descriptor.TableName,
            predicateSql);
    }

    /// <summary>
    /// Builds the select over a registry child table (<c>security_id, ordinal, &lt;element
    /// columns&gt;</c>), located by the owning <paramref name="assetClass"/> and the child's
    /// <paramref name="childTableName"/>.
    /// </summary>
    public string SelectChildSql(string assetClass, string childTableName, string predicateSql)
    {
        var descriptor = SecurityTermsProjectionRegistry.Require(assetClass);
        var child = descriptor.ChildTables.FirstOrDefault(table =>
            string.Equals(table.TableName, childTableName, StringComparison.Ordinal))
            ?? throw new ArgumentException(
                $"'{assetClass}' has no child projection table '{childTableName}'.",
                nameof(childTableName));
        return BuildSelect(SecurityTermsProjectionRegistry.ChildColumnNames(child), child.TableName, predicateSql);
    }

    /// <summary>
    /// Runs <paramref name="sql"/> with <paramref name="parameters"/> and maps every row through
    /// <paramref name="map"/>, in result order.
    /// </summary>
    public async Task<IReadOnlyList<TRow>> QueryAsync<TRow>(
        string sql,
        IReadOnlyList<NpgsqlParameter> parameters,
        Func<ProjectionRow, TRow> map,
        CancellationToken ct)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = CreateCommand(connection, sql, parameters);

        var results = new List<TRow>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var row = new ProjectionRow(reader);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            results.Add(map(row));
        }

        return results;
    }

    /// <summary>
    /// Runs <paramref name="sql"/> and maps the first row, or returns <see langword="null"/> when
    /// the query matches nothing. Only the first row is read.
    /// </summary>
    public async Task<TRow?> QuerySingleAsync<TRow>(
        string sql,
        IReadOnlyList<NpgsqlParameter> parameters,
        Func<ProjectionRow, TRow> map,
        CancellationToken ct)
        where TRow : class
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = CreateCommand(connection, sql, parameters);

        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            return null;
        }

        return map(new ProjectionRow(reader));
    }

    private static NpgsqlCommand CreateCommand(
        NpgsqlConnection connection,
        string sql,
        IReadOnlyList<NpgsqlParameter> parameters)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var parameter in parameters)
        {
            command.Parameters.Add(parameter);
        }

        return command;
    }

    private string BuildSelect(IReadOnlyList<string> columns, string table, string predicateSql)
        => $"select {string.Join(", ", columns)} from {Qualified(table)} {predicateSql};";
}

/// <summary>
/// One result row, read by column name. Reading by name rather than by position is what lets a
/// store select a registry descriptor's column list without restating it: the mapper names the
/// columns it needs and the select carries whatever the descriptor declares, in the descriptor's
/// order. A name the select does not carry fails fast with the column name in the exception.
/// Date columns are <c>date</c> in PostgreSQL and come back as <see cref="DateTime"/>; this maps
/// them to <see cref="DateOnly"/> exactly as the hand-written mappers did.
/// </summary>
internal sealed class ProjectionRow
{
    private readonly DbDataReader _reader;

    public ProjectionRow(DbDataReader reader)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
    }

    public Guid Guid(string column) => _reader.GetGuid(Ordinal(column));

    public Guid? NullableGuid(string column)
    {
        var ordinal = Ordinal(column);
        return _reader.IsDBNull(ordinal) ? null : _reader.GetGuid(ordinal);
    }

    public string String(string column) => _reader.GetString(Ordinal(column));

    public string? NullableString(string column)
    {
        var ordinal = Ordinal(column);
        return _reader.IsDBNull(ordinal) ? null : _reader.GetString(ordinal);
    }

    public decimal Decimal(string column) => _reader.GetDecimal(Ordinal(column));

    public decimal? NullableDecimal(string column)
    {
        var ordinal = Ordinal(column);
        return _reader.IsDBNull(ordinal) ? null : _reader.GetDecimal(ordinal);
    }

    public int Int32(string column) => _reader.GetInt32(Ordinal(column));

    public int? NullableInt32(string column)
    {
        var ordinal = Ordinal(column);
        return _reader.IsDBNull(ordinal) ? null : _reader.GetInt32(ordinal);
    }

    public long Int64(string column) => _reader.GetInt64(Ordinal(column));

    public bool Boolean(string column) => _reader.GetBoolean(Ordinal(column));

    public bool? NullableBoolean(string column)
    {
        var ordinal = Ordinal(column);
        return _reader.IsDBNull(ordinal) ? null : _reader.GetBoolean(ordinal);
    }

    public DateOnly Date(string column) => DateOnly.FromDateTime(_reader.GetDateTime(Ordinal(column)));

    public DateOnly? NullableDate(string column)
    {
        var ordinal = Ordinal(column);
        return _reader.IsDBNull(ordinal) ? null : DateOnly.FromDateTime(_reader.GetDateTime(ordinal));
    }

    /// <summary>A <c>timestamp</c> column read as UTC, as the option-contract mapper always has.</summary>
    public DateTimeOffset UtcTimestamp(string column)
        => new(_reader.GetDateTime(Ordinal(column)), TimeSpan.Zero);

    private int Ordinal(string column)
    {
        try
        {
            return _reader.GetOrdinal(column);
        }
        catch (Exception exception) when (exception is IndexOutOfRangeException or ArgumentException)
        {
            // Npgsql reports an unknown column as IndexOutOfRangeException; DbDataReader implementations
            // used in tests report ArgumentException. Either way the caller gets the column name.
            throw new InvalidOperationException(
                $"The projection select does not carry a column named '{column}'.",
                exception);
        }
    }
}
