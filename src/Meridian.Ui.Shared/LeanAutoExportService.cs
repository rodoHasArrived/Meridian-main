using Meridian.Storage;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Meridian.Ui.Shared;

/// <summary>
/// Background service that continuously exports collected market data to a Lean-compatible
/// directory layout.  Runs on a configurable interval and converts JSONL files found under
/// the Meridian data root into the Lean zip-CSV format expected by the QuantConnect Lean Engine.
/// </summary>
/// <remarks>
/// <para>
/// Configuration is driven by two environment variables:
/// <list type="bullet">
///   <item><c>LEAN_DATA_PATH</c> — target directory where Lean data is written.
///         If unset the service starts but does nothing until the path is supplied via
///         <see cref="Configure"/>.</item>
///   <item><c>LEAN_EXPORT_INTERVAL_SECONDS</c> — export polling interval (default 300 s).</item>
/// </list>
/// </para>
/// <para>
/// The service can also be reconfigured at runtime through <see cref="Configure"/> and
/// paused / resumed through <see cref="Enabled"/>.
/// </para>
/// </remarks>
public sealed class LeanAutoExportService : BackgroundService
{
    private readonly StorageOptions _storageOptions;
    private readonly ILogger<LeanAutoExportService> _logger;

    // ---- runtime-mutable state (lock guards writes) ----
    private readonly object _stateLock = new();
    private string? _leanDataPath;
    private bool _enabled;
    private TimeSpan _interval;
    private HashSet<string> _symbols = new(StringComparer.OrdinalIgnoreCase);

    // ---- statistics ----
    private long _totalFilesExported;
    private long _totalBytesExported;
    private DateTimeOffset? _lastExportAt;
    private DateTimeOffset? _lastExportError;
    private string? _lastErrorMessage;

    /// <summary>Gets whether the auto-export is currently enabled.</summary>
    public bool Enabled
    {
        get { lock (_stateLock) return _enabled; }
    }

    /// <summary>Gets the configured target Lean data path, or <c>null</c> when not set.</summary>
    public string? LeanDataPath
    {
        get { lock (_stateLock) return _leanDataPath; }
    }

    /// <summary>Gets the polling interval between export runs.</summary>
    public TimeSpan Interval
    {
        get { lock (_stateLock) return _interval; }
    }

    /// <summary>Gets the timestamp of the last successful export run.</summary>
    public DateTimeOffset? LastExportAt
    {
        get { lock (_stateLock) return _lastExportAt; }
    }

    /// <summary>Gets the timestamp of the last export error, or <c>null</c> if never.</summary>
    public DateTimeOffset? LastExportError
    {
        get { lock (_stateLock) return _lastExportError; }
    }

    /// <summary>Gets the last error message, or <c>null</c> if no error occurred.</summary>
    public string? LastErrorMessage
    {
        get { lock (_stateLock) return _lastErrorMessage; }
    }

    /// <summary>Gets the total number of files exported since service start.</summary>
    public long TotalFilesExported => Interlocked.Read(ref _totalFilesExported);

    /// <summary>Gets the total bytes written since service start.</summary>
    public long TotalBytesExported => Interlocked.Read(ref _totalBytesExported);

    public LeanAutoExportService(StorageOptions storageOptions, ILogger<LeanAutoExportService> logger)
    {
        _storageOptions = storageOptions;
        _logger = logger;

        // Read initial configuration from environment variables
        _leanDataPath = Environment.GetEnvironmentVariable("LEAN_DATA_PATH");
        _enabled = _leanDataPath != null; // auto-enable only when path is provided at start-up

        var intervalEnv = Environment.GetEnvironmentVariable("LEAN_EXPORT_INTERVAL_SECONDS");
        _interval = int.TryParse(intervalEnv, out var secs) && secs > 0
            ? TimeSpan.FromSeconds(secs)
            : TimeSpan.FromMinutes(5);
    }

    /// <summary>
    /// Reconfigures the auto-export service at runtime.
    /// </summary>
    /// <param name="leanDataPath">Target Lean data directory. Pass <c>null</c> to keep existing value.</param>
    /// <param name="enabled">Whether the service should export on its next tick.</param>
    /// <param name="intervalSeconds">Export polling interval in seconds (minimum 10). Pass ≤0 to keep existing value.</param>
    /// <param name="symbols">Symbols to export. An empty collection means "all available symbols".</param>
    public void Configure(
        string? leanDataPath = null,
        bool? enabled = null,
        int intervalSeconds = 0,
        IEnumerable<string>? symbols = null)
    {
        lock (_stateLock)
        {
            if (leanDataPath != null)
                _leanDataPath = leanDataPath;

            if (enabled.HasValue)
                _enabled = enabled.Value;

            if (intervalSeconds > 0)
                _interval = TimeSpan.FromSeconds(Math.Max(10, intervalSeconds));

            if (symbols != null)
                _symbols = new HashSet<string>(symbols, StringComparer.OrdinalIgnoreCase);
        }

        _logger.LogInformation(
            "LeanAutoExportService reconfigured: enabled={Enabled}, dataPath={DataPath}, interval={Interval}",
            Enabled, LeanDataPath, Interval);
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "LeanAutoExportService started. Enabled={Enabled}, DataPath={DataPath}, Interval={Interval}",
            Enabled, LeanDataPath, Interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            string? dataPath;
            bool enabled;
            TimeSpan interval;
            HashSet<string> symbols;

            lock (_stateLock)
            {
                dataPath = _leanDataPath;
                enabled = _enabled;
                interval = _interval;
                symbols = new HashSet<string>(_symbols, StringComparer.OrdinalIgnoreCase);
            }

            if (enabled && !string.IsNullOrEmpty(dataPath))
            {
                await RunExportCycleAsync(dataPath, symbols, stoppingToken).ConfigureAwait(false);
            }

            try
            {
                await Task.Delay(interval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("LeanAutoExportService stopped.");
    }

    // ---------- export logic ----------

    private async Task RunExportCycleAsync(
        string dataPath,
        HashSet<string> symbols,
        CancellationToken ct)
    {
        var sourceRoot = _storageOptions.RootPath ?? "data";
        if (!Directory.Exists(sourceRoot))
        {
            _logger.LogDebug("LeanAutoExportService: data root '{Root}' not found; skipping export cycle.", sourceRoot);
            return;
        }

        _logger.LogDebug("LeanAutoExportService: starting export cycle. Source={Source}, Target={Target}", sourceRoot, dataPath);

        try
        {
            var filesExported = 0;
            var bytesExported = 0L;

            // Enumerate JSONL files under source root
            var searchPattern = "*.jsonl";
            var allFiles = Directory.EnumerateFiles(sourceRoot, searchPattern, SearchOption.AllDirectories)
                .Concat(Directory.EnumerateFiles(sourceRoot, "*.jsonl.gz", SearchOption.AllDirectories));

            foreach (var filePath in allFiles)
            {
                if (ct.IsCancellationRequested)
                    break;

                // Extract symbol from path convention: {root}/{symbol}/...
                var relativePath = Path.GetRelativePath(sourceRoot, filePath);
                var parts = relativePath.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2)
                    continue;

                var symbol = parts[0].ToUpperInvariant();

                // Filter by requested symbols when specified
                if (symbols.Count > 0 && !symbols.Contains(symbol))
                    continue;

                // Determine event type and date from path
                var eventType = parts.Length > 1 ? parts[1] : "trade";
                var fileName = Path.GetFileNameWithoutExtension(filePath.EndsWith(".gz", StringComparison.OrdinalIgnoreCase)
                    ? Path.GetFileNameWithoutExtension(filePath)
                    : filePath);

                // Parse date from filename: expects yyyy-MM-dd format
                if (!DateTime.TryParseExact(fileName, "yyyy-MM-dd", null,
                    System.Globalization.DateTimeStyles.None, out var fileDate))
                {
                    fileDate = DateTime.UtcNow.Date;
                }

                // Build Lean output path
                var leanDir = LeanSymbolMapper.BuildLeanDataDirectory(dataPath, symbol, "tick");
                Directory.CreateDirectory(leanDir);

                var leanFileName = LeanSymbolMapper.BuildLeanFileName(fileDate, eventType);
                var leanZipPath = Path.Combine(leanDir, leanFileName);

                // Skip if the zip already exists and is newer than the source file
                var sourceInfo = new FileInfo(filePath);
                if (File.Exists(leanZipPath))
                {
                    var zipInfo = new FileInfo(leanZipPath);
                    if (zipInfo.LastWriteTimeUtc >= sourceInfo.LastWriteTimeUtc)
                        continue;
                }

                var bytes = await ExportFileToLeanZipAsync(filePath, leanZipPath, fileDate, eventType, ct)
                    .ConfigureAwait(false);
                if (bytes > 0)
                {
                    filesExported++;
                    bytesExported += bytes;
                }
            }

            lock (_stateLock)
            {
                _lastExportAt = DateTimeOffset.UtcNow;
            }

            Interlocked.Add(ref _totalFilesExported, filesExported);
            Interlocked.Add(ref _totalBytesExported, bytesExported);

            if (filesExported > 0)
            {
                _logger.LogInformation(
                    "LeanAutoExportService: export cycle complete — {Files} files, {Bytes:N0} bytes.",
                    filesExported, bytesExported);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            lock (_stateLock)
            {
                _lastExportError = DateTimeOffset.UtcNow;
                _lastErrorMessage = ex.Message;
            }

            _logger.LogError(ex, "LeanAutoExportService: error during export cycle.");
        }
    }

    /// <summary>
    /// Reads a single Meridian JSONL file and writes a Lean-compatible zip containing a CSV entry.
    /// Returns the number of bytes written to the zip file, or 0 on failure.
    /// </summary>
    private static async Task<long> ExportFileToLeanZipAsync(
        string sourcePath,
        string zipOutputPath,
        DateTime date,
        string eventType,
        CancellationToken ct)
    {
        try
        {
            await using var zipStream = File.Create(zipOutputPath);
            using var archive = new System.IO.Compression.ZipArchive(
                zipStream, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: false);

            var csvEntryName = $"{date:yyyyMMdd}_{LeanSymbolMapper.MapEventTypeToLean(eventType)}.csv";
            var entry = archive.CreateEntry(csvEntryName, System.IO.Compression.CompressionLevel.Optimal);

            await using var entryStream = entry.Open();
            await using var writer = new StreamWriter(entryStream, leaveOpen: false);

            // Open source file (supporting .gz)
            await using var fs = File.OpenRead(sourcePath);
            System.IO.Stream srcStream = sourcePath.EndsWith(".gz", StringComparison.OrdinalIgnoreCase)
                ? new System.IO.Compression.GZipStream(fs, System.IO.Compression.CompressionMode.Decompress)
                : fs;
            await using (srcStream)
            using (var reader = new StreamReader(srcStream, leaveOpen: false))
            {
                string? line;
                while ((line = await reader.ReadLineAsync(ct).ConfigureAwait(false)) != null)
                {
                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    // Minimal Lean CSV format: Milliseconds,Price,Volume
                    // We attempt to extract these fields from JSON by looking for known keys.
                    var leanLine = ConvertJsonLineToLeanCsv(line, date);
                    if (leanLine != null)
                        await writer.WriteLineAsync(leanLine.AsMemory(), ct).ConfigureAwait(false);
                }
            }

            // Flush before getting file size
            await writer.FlushAsync(ct).ConfigureAwait(false);
            zipStream.Flush();
            return new FileInfo(zipOutputPath).Length;
        }
        catch (Exception)
        {
            // Clean up partial output
            try
            { File.Delete(zipOutputPath); }
            catch { /* best-effort */ }
            return 0;
        }
    }

    /// <summary>
    /// Converts a JSONL record line to a Lean tick CSV row.
    /// Lean tick format: {millisSinceEpoch},{price},{size}
    /// Returns <c>null</c> when the line cannot be parsed.
    /// </summary>
    private static string? ConvertJsonLineToLeanCsv(string jsonLine, DateTime date)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(jsonLine);
            var root = doc.RootElement;

            // Resolve timestamp field (common Meridian names: Timestamp, timestamp, Time, time)
            long millisSinceMidnight = 0;
            if (TryGetJsonTimestamp(root, date, out var ts))
                millisSinceMidnight = (long)(ts - date).TotalMilliseconds;

            // Resolve price (Price, price, TradePrice, BidPrice → use mid or trade price)
            decimal price = 0;
            if (root.TryGetProperty("Price", out var pElem) || root.TryGetProperty("price", out pElem))
                pElem.TryGetDecimal(out price);
            else if (root.TryGetProperty("TradePrice", out var tpElem))
                tpElem.TryGetDecimal(out price);
            else if (root.TryGetProperty("MidPrice", out var mpElem))
                mpElem.TryGetDecimal(out price);

            // Resolve size / volume
            decimal size = 0;
            if (root.TryGetProperty("Size", out var sElem) || root.TryGetProperty("size", out sElem))
                sElem.TryGetDecimal(out size);
            else if (root.TryGetProperty("TradeSize", out var tsElem))
                tsElem.TryGetDecimal(out size);
            else if (root.TryGetProperty("Volume", out var vElem))
                vElem.TryGetDecimal(out size);

            if (price == 0)
                return null; // skip records without a price

            // Lean uses integer prices (scale to 10000 for equity tick data)
            var scaledPrice = (long)(price * 10000m);
            var scaledSize = (long)(size * 10000m);

            return $"{millisSinceMidnight},{scaledPrice},{scaledSize}";
        }
        catch
        {
            return null;
        }
    }

    private static bool TryGetJsonTimestamp(
        System.Text.Json.JsonElement root,
        DateTime date,
        out DateTime result)
    {
        foreach (var key in new[] { "Timestamp", "timestamp", "Time", "time" })
        {
            if (!root.TryGetProperty(key, out var elem))
                continue;

            if (elem.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                if (DateTimeOffset.TryParse(elem.GetString(), out var dto))
                {
                    result = dto.UtcDateTime;
                    return true;
                }
            }
            else if (elem.ValueKind == System.Text.Json.JsonValueKind.Number)
            {
                if (elem.TryGetInt64(out var epochMs))
                {
                    result = DateTimeOffset.FromUnixTimeMilliseconds(epochMs).UtcDateTime;
                    return true;
                }
            }
        }

        result = date;
        return false;
    }
}
