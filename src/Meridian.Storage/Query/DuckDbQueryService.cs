using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;
using DuckDB.NET.Data;
using DuckDB.NET.Native;
using Meridian.Storage.Interfaces;
using Microsoft.Extensions.Logging;

namespace Meridian.Storage.Query;

/// <summary>
/// Read-only SQL analytics over the local market-data store, backed by an in-memory DuckDB
/// session per query. The catalog is exposed as a <c>meridian_files</c> view (symbol, event
/// type, date, format, absolute path, size, event count) so operators can discover files and
/// feed paths into <c>read_parquet</c>/<c>read_json_auto</c> without leaving SQL. Statements
/// are guarded: single SELECT-family statement, keyword blocklist, row cap, and timeout —
/// a runaway query can never touch the ingestion hot path or mutate the store. File access
/// is sandboxed to the storage root via DuckDB's <c>allowed_directories</c> with external
/// access otherwise disabled and the configuration locked, so reader functions cannot reach
/// arbitrary host files.
/// </summary>
public sealed partial class DuckDbQueryService(
    IStorageCatalogService catalogService,
    StorageOptions storageOptions,
    ILogger<DuckDbQueryService>? logger = null)
{
    public string QueryableRoot => storageOptions.RootPath;
    /// <summary>
    /// Executes <paramref name="sql"/> and returns a bounded, string-projected result set.
    /// Guard violations and execution errors are returned as a failed result, not thrown,
    /// so the endpoint can map them to a 400 with the message intact.
    /// </summary>
    public async Task<DataQueryResult> ExecuteAsync(
        string? sql,
        DataQueryOptions options,
        CancellationToken ct = default)
        => await ExecuteCoreAsync(sql, options, null, ct).ConfigureAwait(false);

    /// <summary>Executes once, retaining every result row while MaxRows remains the browser preview limit.</summary>
    public async Task<DataQueryResult> ExecuteRetainedAsync(string? sql, DataQueryOptions options,
        IDataQueryResultSink sink, CancellationToken ct = default)
        => await ExecuteCoreAsync(sql, options, sink, ct).ConfigureAwait(false);

    private async Task<DataQueryResult> ExecuteCoreAsync(string? sql, DataQueryOptions options,
        IDataQueryResultSink? sink, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(options);

        var guardError = SqlStatementGuard.Validate(sql, options.MaxSqlLength);
        if (guardError is not null)
            return DataQueryResult.Failed(guardError);

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutSource.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));

        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var connection = new DuckDBConnection("DataSource=:memory:");
            await connection.OpenAsync(timeoutSource.Token).ConfigureAwait(false);

            await ApplySessionLimitsAsync(connection, options, timeoutSource.Token).ConfigureAwait(false);
            await CreateCatalogViewAsync(connection, timeoutSource.Token).ConfigureAwait(false);

            using var command = connection.CreateCommand();
            // Executing operator-supplied SQL is this workbench's purpose, so parameterization
            // does not apply (CodeQL cs/sql-injection fires here by design). Containment instead
            // of sanitization: SqlStatementGuard admits a single SELECT-family statement with
            // write/config verbs blocked; the session is in-memory, capped (memory/threads/rows/
            // timeout), filesystem-sandboxed to the storage root via allowed_directories with
            // external access otherwise disabled, and its configuration is locked; the endpoint
            // is rate-limited.
            command.CommandText = sql!;

            using var reader = await command.ExecuteReaderAsync(timeoutSource.Token).ConfigureAwait(false);

            var columns = new string[reader.FieldCount];
            var columnTypes = new string[reader.FieldCount];
            for (var i = 0; i < reader.FieldCount; i++)
            {
                columns[i] = reader.GetName(i);
                columnTypes[i] = reader.GetDataTypeName(i);
            }
            var unsupported = columnTypes.FirstOrDefault(type => type.ToUpperInvariant() is "LIST" or "ARRAY" or "STRUCT" or "MAP" or "UNION" or "INTERVAL" or "TIMESTAMPNS" or "TIMENS" or "TIMETZ");
            if (unsupported is not null)
                return DataQueryResult.Failed($"Result type {unsupported} cannot be retained without changing its meaning. Cast that expression explicitly to VARCHAR or JSON in the SQL query.");

            var rows = new List<string?[]>();
            var truncated = false;
            long totalRows = 0;
            if (sink is not null)
                await sink.BeginAsync(sql!, columns, columnTypes, timeoutSource.Token).ConfigureAwait(false);
            while (await reader.ReadAsync(timeoutSource.Token).ConfigureAwait(false))
            {
                if (sink is null && rows.Count >= options.MaxRows)
                {
                    truncated = true;
                    break;
                }

                var row = new string?[reader.FieldCount];
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    row[i] = reader.IsDBNull(i)
                        ? null
                        : columnTypes[i].StartsWith("Decimal", StringComparison.OrdinalIgnoreCase)
                            ? ExactDecimalText(reader.GetFieldValue<DuckDBDecimal>(i))
                            : ExactScalarText(reader.GetValue(i));
                }
                if (sink is not null)
                    await sink.WriteRowAsync(row, timeoutSource.Token).ConfigureAwait(false);
                totalRows++;
                if (rows.Count < options.MaxRows)
                    rows.Add(row);
                else
                    truncated = true;
            }

            var receipt = sink is null ? null : await sink.CompleteAsync(totalRows, timeoutSource.Token).ConfigureAwait(false);
            stopwatch.Stop();
            return new DataQueryResult(
                Success: true,
                Error: null,
                Columns: columns,
                ColumnTypes: columnTypes,
                Rows: rows,
                RowCount: rows.Count,
                Truncated: truncated,
                ElapsedMs: stopwatch.ElapsedMilliseconds)
            { ExecutionId = receipt?.ExecutionId, RetainedRowCount = receipt?.RowCount, ExecutionExpiresAt = receipt?.ExpiresAt };
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return DataQueryResult.Failed(
                $"Query exceeded the {options.TimeoutSeconds}s time limit and was cancelled.");
        }
        catch (Exception ex) when (ex is DuckDBException or InvalidOperationException or FormatException or IOException)
        {
            logger?.LogDebug(ex, "Data query failed");
            return DataQueryResult.Failed(ex is IOException
                ? "The complete query execution could not be retained. No complete result was published."
                : ex.Message);
        }
    }

    // System.Decimal cannot represent every DuckDB DECIMAL(38,s). Read the unscaled source
    // integer directly so both retained exports and their preview preserve every source digit.
    private static string ExactDecimalText(DuckDBDecimal value)
    {
        var unscaled = (new BigInteger(value.Value.Upper) << 64) + value.Value.Lower;
        var digits = BigInteger.Abs(unscaled).ToString(CultureInfo.InvariantCulture);
        if (value.Scale > 0)
        {
            digits = digits.PadLeft(value.Scale + 1, '0');
            digits = digits.Insert(digits.Length - value.Scale, ".");
        }
        return unscaled.Sign < 0 ? "-" + digits : digits;
    }

    private static string? ExactScalarText(object value) => value switch
    {
        string text => text,
        DateTime timestamp => timestamp.ToString("O", CultureInfo.InvariantCulture),
        DateTimeOffset timestamp => timestamp.ToString("O", CultureInfo.InvariantCulture),
        DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        TimeOnly time => time.ToString("O", CultureInfo.InvariantCulture),
        TimeSpan time => time.ToString("c", CultureInfo.InvariantCulture),
        byte[] bytes => "0x" + Convert.ToHexString(bytes),
        bool or byte or sbyte or short or ushort or int or uint or long or ulong or Int128 or UInt128 or BigInteger or decimal or double or float or Guid
            => Convert.ToString(value, CultureInfo.InvariantCulture),
        _ => throw new InvalidOperationException("This result type cannot be retained faithfully. Cast the expression explicitly to VARCHAR or JSON in the SQL query.")
    };

    private async Task ApplySessionLimitsAsync(
        DuckDBConnection connection,
        DataQueryOptions options,
        CancellationToken ct)
    {
        using var command = connection.CreateCommand();
        // Session-scoped resource limits and the filesystem sandbox; these SET statements are
        // issued by the service itself before the (blocklist-guarded) user statement runs.
        // Reader functions (read_parquet/read_csv_auto/...) may only touch the storage root:
        // allowed_directories confines file access, enable_external_access=false blocks
        // everything else (other paths, remote endpoints, extension loading), and
        // lock_configuration=true makes the sandbox immutable for the rest of the session.
        var storageRoot = Path.GetFullPath(storageOptions.RootPath).Replace("'", "''", StringComparison.Ordinal);
        command.CommandText =
            $"SET memory_limit='{options.MemoryLimitMb}MB'; "
            + $"SET threads={options.MaxThreads}; "
            + $"SET allowed_directories=['{storageRoot}']; "
            + "SET enable_external_access=false; "
            + "SET lock_configuration=true;";
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private const int CatalogInsertBatchSize = 500;

    private async Task CreateCatalogViewAsync(DuckDBConnection connection, CancellationToken ct)
    {
        using var create = connection.CreateCommand();
        create.CommandText =
            "CREATE TABLE meridian_files (symbol VARCHAR, event_type VARCHAR, source VARCHAR, "
            + "date DATE, format VARCHAR, compressed BOOLEAN, path VARCHAR, size_bytes BIGINT, event_count BIGINT)";
        await create.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

        var batch = new List<string>(CatalogInsertBatchSize);
        foreach (var file in catalogService.SearchFiles(new CatalogSearchCriteria()))
        {
            var absolutePath = Path.GetFullPath(Path.Combine(storageOptions.RootPath, file.RelativePath));
            batch.Add(
                $"({Quote(file.Symbol)}, {Quote(file.EventType)}, {Quote(file.Source)}, "
                + $"{Quote(file.Date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))}, "
                + $"{Quote(file.Format)}, {(file.IsCompressed ? "true" : "false")}, {Quote(absolutePath)}, "
                + $"{file.SizeBytes.ToString(CultureInfo.InvariantCulture)}, {file.EventCount.ToString(CultureInfo.InvariantCulture)})");

            if (batch.Count >= CatalogInsertBatchSize)
            {
                await InsertBatchAsync(connection, batch, ct).ConfigureAwait(false);
                batch.Clear();
            }
        }

        if (batch.Count > 0)
            await InsertBatchAsync(connection, batch, ct).ConfigureAwait(false);
    }

    private static async Task InsertBatchAsync(
        DuckDBConnection connection,
        IReadOnlyList<string> valueTuples,
        CancellationToken ct)
    {
        using var insert = connection.CreateCommand();
        insert.CommandText = "INSERT INTO meridian_files VALUES " + string.Join(", ", valueTuples);
        await insert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static string Quote(string? value) =>
        value is null ? "NULL" : "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
}

/// <summary>
/// Validates that a statement is a single, read-only SELECT-family query. The first token
/// must be a query verb and no write/config verb may appear anywhere — DuckDB's in-memory
/// session cannot mutate the store through queries, but COPY/EXPORT/ATTACH/INSTALL can touch
/// the filesystem or network, so they are rejected outright.
/// </summary>
public static partial class SqlStatementGuard
{
    private static readonly string[] AllowedFirstTokens =
        ["SELECT", "WITH", "FROM", "DESCRIBE", "SHOW", "EXPLAIN", "SUMMARIZE", "VALUES"];

    [GeneratedRegex(
        @"\b(COPY|EXPORT|IMPORT|INSTALL|LOAD|ATTACH|DETACH|CREATE|INSERT|UPDATE|DELETE|DROP|ALTER|TRUNCATE|PRAGMA|SET|RESET|CALL|BEGIN|COMMIT|ROLLBACK|CHECKPOINT|VACUUM|GRANT|USE)\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex BlockedKeywords();

    [GeneratedRegex(@"^(\s*(--[^\n]*\n|/\*.*?\*/))*\s*", RegexOptions.Singleline)]
    private static partial Regex LeadingCommentsAndWhitespace();

    /// <summary>Returns an error message, or <see langword="null"/> when the statement is allowed.</summary>
    public static string? Validate(string? sql, int maxSqlLength)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return "A SQL statement is required.";

        if (sql.Length > maxSqlLength)
            return $"Statement exceeds the {maxSqlLength}-character limit.";

        var body = LeadingCommentsAndWhitespace().Replace(sql, string.Empty, 1).TrimEnd();
        if (body.Length == 0)
            return "A SQL statement is required.";

        // Single statement only: a trailing semicolon is fine, embedded ones are not.
        var withoutTrailing = body.TrimEnd(';', ' ', '\t', '\r', '\n');
        if (withoutTrailing.Contains(';', StringComparison.Ordinal))
            return "Only a single statement is allowed.";

        var firstToken = FirstToken(withoutTrailing);
        if (!AllowedFirstTokens.Contains(firstToken, StringComparer.OrdinalIgnoreCase))
            return $"Only read-only queries are allowed ({string.Join(", ", AllowedFirstTokens)}); got '{firstToken}'.";

        var blocked = BlockedKeywords().Match(withoutTrailing);
        if (blocked.Success)
            return $"Statement contains the blocked keyword '{blocked.Value.ToUpperInvariant()}'.";

        return null;
    }

    private static string FirstToken(string body)
    {
        var end = 0;
        while (end < body.Length && (char.IsLetter(body[end]) || body[end] == '_'))
            end++;
        return end == 0 ? body[..Math.Min(body.Length, 12)] : body[..end];
    }
}

/// <summary>
/// Limits for the data query workbench, bound from the <c>DataQuery</c> configuration section.
/// </summary>
public sealed class DataQueryOptions
{
    public const string SectionName = "DataQuery";

    public bool Enabled { get; set; } = true;

    /// <summary>Maximum rows returned to the client; additional rows set <c>Truncated</c>.</summary>
    public int MaxRows { get; set; } = 10_000;

    /// <summary>Wall-clock limit for a single query.</summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>DuckDB session memory limit.</summary>
    public int MemoryLimitMb { get; set; } = 512;

    /// <summary>DuckDB session thread cap, keeping analytics off the ingestion cores.</summary>
    public int MaxThreads { get; set; } = 2;

    public int MaxSqlLength { get; set; } = 20_000;

    /// <summary>Private retention must remain outside RootPath, which SQL readers may access.</summary>
    public string? RetainedResultsRoot { get; set; }
    public long MaxRetainedResultBytes { get; set; } = 32L * 1024 * 1024;
    public long MaxRetainedTotalBytes { get; set; } = 128L * 1024 * 1024;
    public int MaxRetainedRows { get; set; } = 250_000;
    public int RetainedResultMinutes { get; set; } = 30;
}

/// <summary>Request body for the data query endpoint.</summary>
public sealed record DataQueryRequest(string? Sql);

/// <summary>Bounded, string-projected query result.</summary>
public sealed record DataQueryResult(
    bool Success,
    string? Error,
    IReadOnlyList<string> Columns,
    IReadOnlyList<string> ColumnTypes,
    IReadOnlyList<string?[]> Rows,
    int RowCount,
    bool Truncated,
    long ElapsedMs)
{
    public string? ExecutionId { get; init; }
    public long? RetainedRowCount { get; init; }
    public DateTimeOffset? ExecutionExpiresAt { get; init; }
    public static DataQueryResult Failed(string error) => new(
        false, error, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string?[]>(), 0, false, 0);
}
