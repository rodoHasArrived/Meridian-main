using System.Data.Common;
using System.Globalization;
using System.Text.Json;
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
    /// Builds the select over a registry satellite table (its leading spine, its columns, its
    /// trailing spine), located by the owning <paramref name="assetClass"/> and the satellite's
    /// <paramref name="satelliteTableName"/>.
    /// </summary>
    public string SelectSatelliteSql(string assetClass, string satelliteTableName, string predicateSql)
    {
        var descriptor = SecurityTermsProjectionRegistry.Require(assetClass);
        var satellite = descriptor.Satellites.FirstOrDefault(table =>
            string.Equals(table.TableName, satelliteTableName, StringComparison.Ordinal))
            ?? throw new ArgumentException(
                $"'{assetClass}' has no satellite projection table '{satelliteTableName}'.",
                nameof(satelliteTableName));
        return BuildSelect(SecurityTermsProjectionRegistry.SatelliteColumnNames(satellite), satellite.TableName, predicateSql);
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

    /// <summary>
    /// Every named column as a JSON value, keyed by column name in <paramref name="columns"/>
    /// order. The generic registry read uses this for classes that have no typed row: the column
    /// list is the descriptor's, so the result carries exactly what the table holds. The SQL type
    /// of each column (<see cref="DbDataReader.GetDataTypeName"/>) decides how a value is written,
    /// so a <c>date</c> column reads as an ISO date rather than a midnight timestamp.
    /// </summary>
    public IReadOnlyDictionary<string, JsonElement> ToJsonColumns(IReadOnlyList<string> columns)
    {
        ArgumentNullException.ThrowIfNull(columns);
        var values = new Dictionary<string, JsonElement>(columns.Count, StringComparer.Ordinal);
        foreach (var column in columns)
        {
            var ordinal = Ordinal(column);
            var value = _reader.IsDBNull(ordinal) ? null : _reader.GetValue(ordinal);
            values[column] = ProjectionJsonValue.From(value, _reader.GetDataTypeName(ordinal));
        }

        return values;
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

/// <summary>
/// Converts one projection column value to a <see cref="JsonElement"/> for the generic registry
/// read. Numbers keep their exact decimal text, identifiers and text are strings, a SQL
/// <c>date</c> is <c>yyyy-MM-dd</c>, a timestamp is round-trip ISO-8601 (a
/// <c>timestamp without time zone</c> read as UTC, as <see cref="ProjectionRow.UtcTimestamp"/>
/// does), and SQL NULL is JSON null. Anything else is written as its invariant text.
/// </summary>
internal static class ProjectionJsonValue
{
    private static readonly JsonElement Null = Parse(static writer => writer.WriteNullValue());

    public static JsonElement From(object? value, string? dataTypeName)
    {
        if (value is null or DBNull)
        {
            return Null;
        }

        return Parse(writer =>
        {
            switch (value)
            {
                case string text:
                    writer.WriteStringValue(text);
                    break;
                case Guid guid:
                    writer.WriteStringValue(guid.ToString("D"));
                    break;
                case bool flag:
                    writer.WriteBooleanValue(flag);
                    break;
                case decimal number:
                    writer.WriteNumberValue(number);
                    break;
                case long number:
                    writer.WriteNumberValue(number);
                    break;
                case int number:
                    writer.WriteNumberValue(number);
                    break;
                case short number:
                    writer.WriteNumberValue(number);
                    break;
                case double number:
                    writer.WriteNumberValue(number);
                    break;
                case float number:
                    writer.WriteNumberValue(number);
                    break;
                case DateOnly date:
                    writer.WriteStringValue(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                    break;
                case DateTime dateTime when string.Equals(dataTypeName, "date", StringComparison.OrdinalIgnoreCase):
                    writer.WriteStringValue(DateOnly.FromDateTime(dateTime).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                    break;
                case DateTime dateTime:
                    writer.WriteStringValue(new DateTimeOffset(DateTime.SpecifyKind(dateTime, DateTimeKind.Utc), TimeSpan.Zero)
                        .ToString("O", CultureInfo.InvariantCulture));
                    break;
                case DateTimeOffset timestamp:
                    writer.WriteStringValue(timestamp.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
                    break;
                default:
                    writer.WriteStringValue(Convert.ToString(value, CultureInfo.InvariantCulture));
                    break;
            }
        });
    }

    private static JsonElement Parse(Action<Utf8JsonWriter> write)
    {
        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            write(writer);
        }

        using var document = JsonDocument.Parse(buffer.WrittenMemory);
        return document.RootElement.Clone();
    }
}
