using System.Text.Json;
using Meridian.Contracts.SecurityMaster;
using Meridian.Core.Serialization;
using Meridian.Storage.Archival;

namespace Meridian.Storage.SecurityMaster;

/// <summary>
/// Local-first file store for EDGAR reference data under the configured storage root.
/// </summary>
public sealed class FileEdgarReferenceDataStore : IEdgarReferenceDataStore
{
    private readonly string _rootPath;
    private readonly SemaphoreSlim _manifestLock = new(1, 1);
    private int? _tickerAssociationCount;
    private int? _filerPartitionCount;
    private int? _factPartitionCount;
    private int? _securityDataPartitionCount;

    public FileEdgarReferenceDataStore(StorageOptions storageOptions)
    {
        _rootPath = Path.Combine(storageOptions.RootPath, "reference-data", "edgar");
    }

    public async Task SaveTickerAssociationsAsync(
        IReadOnlyList<EdgarTickerAssociation> associations,
        CancellationToken ct = default)
    {
        var ordered = associations
            .OrderBy(a => a.Cik, StringComparer.Ordinal)
            .ThenBy(a => a.Ticker, StringComparer.OrdinalIgnoreCase)
            .ThenBy(a => a.SeriesId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(a => a.ClassId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var json = JsonSerializer.Serialize(
            ordered,
            SecurityMasterJsonContext.Default.ListEdgarTickerAssociation);

        await AtomicFileWriter.WriteAsync(TickerAssociationsPath, json, ct).ConfigureAwait(false);
        _tickerAssociationCount = ordered.Count;
        await WriteManifestAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<EdgarTickerAssociation>> LoadTickerAssociationsAsync(CancellationToken ct = default)
    {
        if (!File.Exists(TickerAssociationsPath))
            return Array.Empty<EdgarTickerAssociation>();

        await using var stream = new FileStream(
            TickerAssociationsPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 65536,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        var associations = await JsonSerializer.DeserializeAsync(
                stream,
                SecurityMasterJsonContext.Default.ListEdgarTickerAssociation,
                ct)
            .ConfigureAwait(false);

        return associations is null ? Array.Empty<EdgarTickerAssociation>() : associations;
    }

    public async Task SaveFilerAsync(EdgarFilerRecord record, CancellationToken ct = default)
    {
        var normalized = NormalizeCik(record.Cik);
        if (normalized.Length == 0)
            throw new ArgumentException("EDGAR filer record must include a CIK.", nameof(record));

        var json = JsonSerializer.Serialize(
            record with { Cik = normalized },
            SecurityMasterJsonContext.Default.EdgarFilerRecord);

        var path = FilerPath(normalized);
        var existed = File.Exists(path);
        await AtomicFileWriter.WriteAsync(path, json, ct).ConfigureAwait(false);
        _filerPartitionCount = CountAfterWrite(_filerPartitionCount, Path.Combine(_rootPath, "filers"), existed);
        await WriteManifestAsync(ct).ConfigureAwait(false);
    }

    public async Task<EdgarFilerRecord?> LoadFilerAsync(string cik, CancellationToken ct = default)
    {
        var normalized = NormalizeCik(cik);
        if (normalized.Length == 0)
            return null;

        var path = FilerPath(normalized);
        if (!File.Exists(path))
            return null;

        await using var stream = OpenRead(path);
        return await JsonSerializer.DeserializeAsync(
                stream,
                SecurityMasterJsonContext.Default.EdgarFilerRecord,
                ct)
            .ConfigureAwait(false);
    }

    public async Task SaveFactsAsync(EdgarFactsRecord record, CancellationToken ct = default)
    {
        var normalized = NormalizeCik(record.Cik);
        if (normalized.Length == 0)
            throw new ArgumentException("EDGAR facts record must include a CIK.", nameof(record));

        var json = JsonSerializer.Serialize(
            record with { Cik = normalized },
            SecurityMasterJsonContext.Default.EdgarFactsRecord);

        var path = FactsPath(normalized);
        var existed = File.Exists(path);
        await AtomicFileWriter.WriteAsync(path, json, ct).ConfigureAwait(false);
        _factPartitionCount = CountAfterWrite(_factPartitionCount, Path.Combine(_rootPath, "facts"), existed);
        await WriteManifestAsync(ct).ConfigureAwait(false);
    }

    public async Task<EdgarFactsRecord?> LoadFactsAsync(string cik, CancellationToken ct = default)
    {
        var normalized = NormalizeCik(cik);
        if (normalized.Length == 0)
            return null;

        var path = FactsPath(normalized);
        if (!File.Exists(path))
            return null;

        await using var stream = OpenRead(path);
        return await JsonSerializer.DeserializeAsync(
                stream,
                SecurityMasterJsonContext.Default.EdgarFactsRecord,
                ct)
            .ConfigureAwait(false);
    }

    public async Task SaveSecurityDataAsync(EdgarSecurityDataRecord record, CancellationToken ct = default)
    {
        var normalized = NormalizeCik(record.Cik);
        if (normalized.Length == 0)
            throw new ArgumentException("EDGAR security data record must include a CIK.", nameof(record));

        var json = JsonSerializer.Serialize(
            record with { Cik = normalized },
            SecurityMasterJsonContext.Default.EdgarSecurityDataRecord);

        var path = SecurityDataPath(normalized);
        var existed = File.Exists(path);
        await AtomicFileWriter.WriteAsync(path, json, ct).ConfigureAwait(false);
        _securityDataPartitionCount = CountAfterWrite(
            _securityDataPartitionCount,
            Path.Combine(_rootPath, "security-data"),
            existed);
        await WriteManifestAsync(ct).ConfigureAwait(false);
    }

    public async Task<EdgarSecurityDataRecord?> LoadSecurityDataAsync(string cik, CancellationToken ct = default)
    {
        var normalized = NormalizeCik(cik);
        if (normalized.Length == 0)
            return null;

        var path = SecurityDataPath(normalized);
        if (!File.Exists(path))
            return null;

        await using var stream = OpenRead(path);
        return await JsonSerializer.DeserializeAsync(
                stream,
                SecurityMasterJsonContext.Default.EdgarSecurityDataRecord,
                ct)
            .ConfigureAwait(false);
    }

    private string TickerAssociationsPath => Path.Combine(_rootPath, "ticker-associations.json");

    private string ManifestPath => Path.Combine(_rootPath, "manifest.json");

    private string FilerPath(string normalizedCik)
        => Path.Combine(_rootPath, "filers", $"{normalizedCik}.json");

    private string FactsPath(string normalizedCik)
        => Path.Combine(_rootPath, "facts", $"{normalizedCik}.json");

    private string SecurityDataPath(string normalizedCik)
        => Path.Combine(_rootPath, "security-data", $"{normalizedCik}.json");

    private async Task WriteManifestAsync(CancellationToken ct)
    {
        await _manifestLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var manifest = new EdgarReferenceDataManifest(
                UpdatedAtUtc: DateTimeOffset.UtcNow,
                TickerAssociationCount: _tickerAssociationCount ?? CountTickerAssociations(),
                FilerPartitionCount: _filerPartitionCount ?? CountJsonFiles(Path.Combine(_rootPath, "filers")),
                FactPartitionCount: _factPartitionCount ?? CountJsonFiles(Path.Combine(_rootPath, "facts")),
                SecurityDataPartitionCount: _securityDataPartitionCount ?? CountJsonFiles(Path.Combine(_rootPath, "security-data")));

            var json = JsonSerializer.Serialize(
                manifest,
                SecurityMasterJsonContext.Default.EdgarReferenceDataManifest);

            await AtomicFileWriter.WriteAsync(ManifestPath, json, ct).ConfigureAwait(false);
        }
        finally
        {
            _manifestLock.Release();
        }
    }

    private static FileStream OpenRead(string path)
        => new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 65536,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

    private static int CountJsonFiles(string directory)
        => Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly).Count()
            : 0;

    private int CountTickerAssociations()
    {
        if (!File.Exists(TickerAssociationsPath))
            return 0;

        try
        {
            using var stream = OpenRead(TickerAssociationsPath);
            var associations = JsonSerializer.Deserialize(
                stream,
                SecurityMasterJsonContext.Default.ListEdgarTickerAssociation);
            return associations?.Count ?? 0;
        }
        catch (JsonException)
        {
            return 0;
        }
    }

    private static int CountAfterWrite(int? cachedCount, string directory, bool existed)
    {
        if (cachedCount is int current)
            return existed ? current : current + 1;

        return CountJsonFiles(directory);
    }

    private static string NormalizeCik(string? cik)
    {
        if (string.IsNullOrWhiteSpace(cik))
            return string.Empty;

        var digits = new string(cik.Where(char.IsDigit).ToArray());
        if (digits.Length == 0)
            return string.Empty;

        return digits.Length >= 10
            ? digits[^10..]
            : digits.PadLeft(10, '0');
    }
}
