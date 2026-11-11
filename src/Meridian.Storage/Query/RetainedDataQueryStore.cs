using System.Security.Cryptography;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Storage.Archival;

namespace Meridian.Storage.Query;

public sealed record DataQueryOwner(string TenantId, string CompanyId, string OperatorId);
public sealed record DataQueryRetentionReceipt(string ExecutionId, long RowCount, DateTimeOffset ExpiresAt);
public interface IDataQueryResultSink : IAsyncDisposable
{
    Task BeginAsync(string sql, string[] columns, string[] columnTypes, CancellationToken ct);
    Task WriteRowAsync(string?[] row, CancellationToken ct);
    Task<DataQueryRetentionReceipt> CompleteAsync(long rowCount, CancellationToken ct);
}

public sealed class RetainedDataQueryException(string code, string message) : InvalidOperationException(message)
{
    public string Code { get; } = code;
}

public sealed record RetainedDataQueryManifest(int Version, string ExecutionId, string OwnerHash, string Sql,
    string SqlSha256, string[] Columns, string[] ColumnTypes, long RowCount, long ByteCount, string RowsSha256,
    DateTimeOffset ExecutedAt, DateTimeOffset ExpiresAt);
public sealed record RetainedDataQuerySnapshot(RetainedDataQueryManifest Manifest, IReadOnlyList<string?[]> Rows);
internal sealed record DataQueryReservation(long Bytes, DateTimeOffset ExpiresAt);

/// <summary>
/// Immutable, owner-bound executions, outside the DuckDB-readable root. Atomic publication and
/// cross-process reservation accounting prevent a partial or over-quota spool becoming an export.
/// </summary>
public sealed class RetainedDataQueryStore
{
    private readonly DataQueryOptions _options;
    private readonly TimeProvider _clock;
    private readonly string _sourceHash;
    public string RootPath { get; }

    public RetainedDataQueryStore(DataQueryOptions options, string queryableRoot, TimeProvider? clock = null)
    {
        _options = options;
        _clock = clock ?? TimeProvider.System;
        if (options.MaxRetainedResultBytes <= 0 || options.MaxRetainedTotalBytes < options.MaxRetainedResultBytes ||
            options.MaxRetainedRows <= 0 || options.RetainedResultMinutes is < 1 or > 1440)
            throw new RetainedDataQueryException("Unavailable", "Retained query limits are invalid.");
        var sourceRoot = ResolveDirectory(Path.GetFullPath(queryableRoot));
        _sourceHash = Digest(Encoding.UTF8.GetBytes(OperatingSystem.IsWindows() ? sourceRoot.ToUpperInvariant() : sourceRoot));
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(appData))
            appData = Path.GetTempPath();
        RootPath = Path.GetFullPath(options.RetainedResultsRoot ?? Path.Combine(appData, "Meridian", "private-query-results", _sourceHash[..24]));
        if (Inside(ResolveDirectory(Path.GetFullPath(queryableRoot)), ResolveDirectory(RootPath)))
            throw new RetainedDataQueryException("Unavailable", "Retained query results must be outside the SQL-readable storage root.");
        if (OperatingSystem.IsWindows())
            Directory.CreateDirectory(RootPath);
        else
        {
            Directory.CreateDirectory(RootPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            File.SetUnixFileMode(RootPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        var resolvedRoot = ResolveDirectory(RootPath);
        var resolvedQueryable = ResolveDirectory(Path.GetFullPath(queryableRoot));
        if (Inside(resolvedQueryable, resolvedRoot))
            throw new RetainedDataQueryException("Unavailable", "The retained result directory resolves inside the SQL-readable storage root.");
    }

    public async Task<IDataQueryResultSink> CreateAsync(DataQueryOwner owner, CancellationToken ct = default)
    {
        var ownerHash = OwnerHash(owner);
        await using var gate = await AcquireAsync(ct).ConfigureAwait(false);
        CleanupExpired();
        var committedBytes = Directory.EnumerateFiles(RootPath, "*.rows").Sum(path => new FileInfo(path).Length);
        long reservedBytes = 0;
        foreach (var path in Directory.EnumerateFiles(RootPath, "*.reservation"))
        {
            var reservation = JsonSerializer.Deserialize(File.ReadAllText(path), RetainedDataQueryJsonContext.Default.DataQueryReservation)
                ?? throw new RetainedDataQueryException("Unavailable", "Query retention reservation is invalid.");
            reservedBytes = checked(reservedBytes + reservation.Bytes);
        }
        if (committedBytes + reservedBytes > _options.MaxRetainedTotalBytes - _options.MaxRetainedResultBytes)
            throw new RetainedDataQueryException("Quota", "Query retention quota is full. Wait for retained executions to expire or narrow the result.");
        var id = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        var reservationPath = PathFor(id, ".reservation");
        await AtomicFileWriter.WriteAsync(reservationPath, JsonSerializer.Serialize(new DataQueryReservation(
            _options.MaxRetainedResultBytes, _clock.GetUtcNow().AddSeconds(Math.Max(1, _options.TimeoutSeconds) + 60)),
            RetainedDataQueryJsonContext.Default.DataQueryReservation), ct).ConfigureAwait(false);
        try
        { return new Sink(this, ownerHash, id); }
        catch { File.Delete(reservationPath); throw; }
    }

    public async Task<RetainedDataQuerySnapshot> ReadAsync(DataQueryOwner owner, string executionId, CancellationToken ct = default)
    {
        ValidateId(executionId);
        var manifestPath = PathFor(executionId, ".manifest.json");
        if (!File.Exists(manifestPath))
            throw new RetainedDataQueryException("NotFound", "The complete retained execution is unavailable. Run the query again.");
        RetainedDataQueryManifest manifest;
        try
        {
            await using var source = File.OpenRead(manifestPath);
            manifest = await JsonSerializer.DeserializeAsync(source, RetainedDataQueryJsonContext.Default.RetainedDataQueryManifest, ct).ConfigureAwait(false)
                ?? throw new JsonException("Empty execution manifest.");
        }
        catch (JsonException) { throw new RetainedDataQueryException("Invalid", "The retained execution manifest is invalid; no export was produced."); }
        if (manifest.OwnerHash != OwnerHash(owner))
            throw new RetainedDataQueryException("NotFound", "The retained execution is unavailable in this operator scope.");
        if (manifest.ExpiresAt <= _clock.GetUtcNow())
            throw new RetainedDataQueryException("Expired", "The retained execution expired. Run the query again.");
        if (manifest.Version != 1 || manifest.ExecutionId != executionId || manifest.Columns is null || manifest.ColumnTypes is null || manifest.Sql is null ||
            manifest.Columns.Length != manifest.ColumnTypes.Length ||
            manifest.RowCount < 0 || manifest.RowCount > _options.MaxRetainedRows || manifest.ByteCount < 0 || manifest.ByteCount > _options.MaxRetainedResultBytes ||
            manifest.SqlSha256 != Digest(Encoding.UTF8.GetBytes(manifest.Sql)))
            throw new RetainedDataQueryException("Invalid", "The retained execution binding is invalid; no export was produced.");
        var rowsPath = PathFor(executionId, ".rows");
        if (!File.Exists(rowsPath))
            throw new RetainedDataQueryException("Invalid", "The complete retained row population is missing; no export was produced.");
        await using var stream = new FileStream(rowsPath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length != manifest.ByteCount || Convert.ToHexString(await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false)) != manifest.RowsSha256)
            throw new RetainedDataQueryException("Invalid", "Retained rows failed integrity verification; no partial export was produced.");
        stream.Position = 0;
        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
        var rows = new List<string?[]>();
        while (await reader.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
        {
            string?[]? row;
            try
            { row = JsonSerializer.Deserialize(line, RetainedDataQueryJsonContext.Default.StringArray); }
            catch (JsonException) { throw new RetainedDataQueryException("Invalid", "A retained row is invalid; no partial export was produced."); }
            if (row is null || row.Length != manifest.Columns.Length || rows.Count >= manifest.RowCount)
                throw new RetainedDataQueryException("Invalid", "The retained result population is incomplete or inconsistent.");
            rows.Add(row);
        }
        if (rows.Count != manifest.RowCount)
            throw new RetainedDataQueryException("Invalid", "The retained result population is incomplete.");
        return new(manifest, rows);
    }

    private static bool Inside(string parent, string child)
    {
        var relative = Path.GetRelativePath(parent, child);
        return relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) && !Path.IsPathRooted(relative);
    }
    private static string ResolveDirectory(string path)
    {
        // Resolve every existing ancestor: a non-link leaf can still sit below a linked parent.
        var root = Path.GetPathRoot(path)!;
        var cursor = root;
        foreach (var component in path[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            cursor = Path.Combine(cursor, component);
            if (Directory.Exists(cursor))
                cursor = new DirectoryInfo(cursor).ResolveLinkTarget(true)?.FullName ?? cursor;
        }
        return Path.GetFullPath(cursor);
    }
    private string OwnerHash(DataQueryOwner owner)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner.TenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(owner.CompanyId);
        ArgumentException.ThrowIfNullOrWhiteSpace(owner.OperatorId);
        return Digest(JsonSerializer.SerializeToUtf8Bytes(new[] { _sourceHash, owner.TenantId, owner.CompanyId, owner.OperatorId }, RetainedDataQueryJsonContext.Default.StringArray));
    }
    private static string Digest(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static void ValidateId(string id)
    {
        if (id.Length != 48 || id.Any(character => !char.IsAsciiHexDigitLower(character)))
            throw new RetainedDataQueryException("NotFound", "The execution token is invalid.");
    }
    private string PathFor(string id, string suffix) => Path.Combine(RootPath, id + suffix);
    private async Task<FileStream> AcquireAsync(CancellationToken ct)
    {
        var timer = Stopwatch.StartNew();
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try
            { return new FileStream(Path.Combine(RootPath, ".quota.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (timer.Elapsed < TimeSpan.FromSeconds(5)) { await Task.Delay(25, ct).ConfigureAwait(false); }
        }
    }
    private void CleanupExpired()
    {
        foreach (var path in Directory.EnumerateFiles(RootPath, "*.manifest.json"))
        {
            var manifest = JsonSerializer.Deserialize(File.ReadAllText(path), RetainedDataQueryJsonContext.Default.RetainedDataQueryManifest);
            if (manifest is not null && manifest.ExpiresAt <= _clock.GetUtcNow())
                DeleteFiles(manifest.ExecutionId);
        }
        foreach (var path in Directory.EnumerateFiles(RootPath, "*.reservation"))
        {
            var reservation = JsonSerializer.Deserialize(File.ReadAllText(path), RetainedDataQueryJsonContext.Default.DataQueryReservation);
            if (reservation is not null && reservation.ExpiresAt <= _clock.GetUtcNow())
                DeleteFiles(Path.GetFileName(path)[..^12]);
        }
    }
    private void DeleteFiles(string id)
    {
        ValidateId(id);
        foreach (var suffix in new[] { ".reservation", ".part", ".rows", ".manifest.json" })
            File.Delete(PathFor(id, suffix));
    }

    private sealed class Sink : IDataQueryResultSink
    {
        private readonly RetainedDataQueryStore _store;
        private readonly string _ownerHash;
        private readonly string _id;
        private readonly FileStream _stream;
        private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        private string? _sql;
        private string[] _columns = [];
        private string[] _types = [];
        private long _rows;
        private long _bytes;
        private bool _complete;
        private DateTimeOffset _executedAt;
        public Sink(RetainedDataQueryStore store, string ownerHash, string id)
        {
            _store = store;
            _ownerHash = ownerHash;
            _id = id;
            _stream = new FileStream(store.PathFor(id, ".part"), FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous);
        }
        public Task BeginAsync(string sql, string[] columns, string[] columnTypes, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            _sql = sql;
            _columns = columns;
            _types = columnTypes;
            _executedAt = _store._clock.GetUtcNow();
            return Task.CompletedTask;
        }
        public async Task WriteRowAsync(string?[] row, CancellationToken ct)
        {
            if (_sql is null || row.Length != _columns.Length)
                throw new InvalidOperationException("The query result schema changed during retention.");
            var bytes = JsonSerializer.SerializeToUtf8Bytes(row, RetainedDataQueryJsonContext.Default.StringArray);
            if (_rows >= _store._options.MaxRetainedRows || _bytes + bytes.Length + 1 > _store._options.MaxRetainedResultBytes)
                throw new RetainedDataQueryException("Quota", "The result exceeds the retained export quota. No complete execution was retained; narrow the query or use an explicit SQL LIMIT.");
            await _stream.WriteAsync(bytes, ct).ConfigureAwait(false);
            await _stream.WriteAsync(new byte[] { (byte)'\n' }, ct).ConfigureAwait(false);
            _hash.AppendData(bytes);
            _hash.AppendData(new byte[] { (byte)'\n' });
            _rows++;
            _bytes += bytes.Length + 1;
        }
        public async Task<DataQueryRetentionReceipt> CompleteAsync(long rowCount, CancellationToken ct)
        {
            if (_sql is null || rowCount != _rows)
                throw new InvalidOperationException("The query result is incomplete.");
            await _stream.FlushAsync(ct).ConfigureAwait(false);
            _stream.Flush(flushToDisk: true);
            await _stream.DisposeAsync().ConfigureAwait(false);
            var expiry = _store._clock.GetUtcNow().AddMinutes(_store._options.RetainedResultMinutes);
            var manifest = new RetainedDataQueryManifest(1, _id, _ownerHash, _sql, Digest(Encoding.UTF8.GetBytes(_sql)),
                _columns, _types, _rows, _bytes, Convert.ToHexString(_hash.GetHashAndReset()), _executedAt, expiry);
            await using var gate = await _store.AcquireAsync(ct).ConfigureAwait(false);
            File.Move(_store.PathFor(_id, ".part"), _store.PathFor(_id, ".rows"));
            await AtomicFileWriter.WriteAsync(_store.PathFor(_id, ".manifest.json"), JsonSerializer.Serialize(manifest,
                RetainedDataQueryJsonContext.Default.RetainedDataQueryManifest), ct).ConfigureAwait(false);
            File.Delete(_store.PathFor(_id, ".reservation"));
            _complete = true;
            return new(_id, _rows, expiry);
        }
        public async ValueTask DisposeAsync()
        {
            await _stream.DisposeAsync().ConfigureAwait(false);
            _hash.Dispose();
            if (_complete)
                return;
            try
            {
                await using var gate = await _store.AcquireAsync(CancellationToken.None).ConfigureAwait(false);
                _store.DeleteFiles(_id);
            }
            catch (IOException) { /* Expiring reservation remains charged until cleanup. No manifest is published. */ }
        }
    }
}

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(RetainedDataQueryManifest))]
[JsonSerializable(typeof(DataQueryReservation))]
[JsonSerializable(typeof(string[]))]
internal sealed partial class RetainedDataQueryJsonContext : JsonSerializerContext;
