using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Meridian.Storage.Archival;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Promotions;
using Meridian.Strategies.Serialization;
using Meridian.Strategies.Services;
using Microsoft.Extensions.Logging;

namespace Meridian.Strategies.Storage;

/// <summary>
/// Configuration for durable promotion-history storage.
/// </summary>
public sealed record PromotionRecordStoreOptions(string RootDirectory)
{
    public static PromotionRecordStoreOptions Default { get; } = new(
        Path.Combine(AppContext.BaseDirectory, "data", "strategies", "promotions"));

    public string HistoryPath => Path.Combine(RootDirectory, "promotion-history.jsonl");

    public string AuthorityLockPath => Path.Combine(RootDirectory, "promotion-history.lock");
}

/// <summary>
/// Append-only JSONL promotion-history store.
/// </summary>
public sealed class JsonlPromotionRecordStore : IPromotionRecordStore
{
    private static readonly TimeSpan AuthorityLockRetryDelay = TimeSpan.FromMilliseconds(20);
    private static readonly TimeSpan AuthorityLockTimeout = TimeSpan.FromSeconds(30);

    private readonly PromotionRecordStoreOptions _options;
    private readonly ILogger<JsonlPromotionRecordStore> _logger;

    public JsonlPromotionRecordStore(
        PromotionRecordStoreOptions? options,
        ILogger<JsonlPromotionRecordStore> logger)
    {
        _options = options ?? PromotionRecordStoreOptions.Default;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public JsonlPromotionRecordStore(
        string baseDirectory,
        ILogger<JsonlPromotionRecordStore> logger)
        : this(new PromotionRecordStoreOptions(baseDirectory), logger)
    {
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<StrategyPromotionRecord>> LoadAllAsync(CancellationToken ct = default)
    {
        await using var authorityLock = await AcquireAuthorityLockAsync(ct).ConfigureAwait(false);
        return await LoadAllCoreAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<PromotionDecisionReservation> ReserveFirstDecisionAsync(
        StrategyPromotionRecord record,
        CancellationToken ct = default)
    {
        ValidateRecord(record);

        var authorityLock = await AcquireAuthorityLockAsync(ct).ConfigureAwait(false);
        try
        {
            var records = await LoadAllCoreAsync(ct).ConfigureAwait(false);
            var existing = records.FirstOrDefault(candidate => HasSameDecisionKey(candidate, record));
            if (existing is not null)
            {
                return new PromotionDecisionReservation(
                    existing,
                    wasAppended: false,
                    authorityLock.DisposeAsync);
            }

            await AppendCoreAsync(record, ct).ConfigureAwait(false);
            return new PromotionDecisionReservation(
                record,
                wasAppended: true,
                authorityLock.DisposeAsync);
        }
        catch
        {
            await authorityLock.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private async Task<IReadOnlyList<StrategyPromotionRecord>> LoadAllCoreAsync(CancellationToken ct)
    {
        if (!File.Exists(_options.HistoryPath))
        {
            return [];
        }

        var records = new List<StrategyPromotionRecord>();
        await using var stream = File.OpenRead(_options.HistoryPath);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        while (!reader.EndOfStream)
        {
            ct.ThrowIfCancellationRequested();

            var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                var record = JsonSerializer.Deserialize(
                    line,
                    PromotionRecordJsonContext.Default.StrategyPromotionRecord);
                if (record is not null)
                {
                    if (PromotionService.TryValidatePromotionRecord(record, out var validationError))
                    {
                        records.Add(record);
                    }
                    else
                    {
                        _logger.LogWarning(
                            "Skipping invalid promotion record in {Path}: {ValidationError}",
                            _options.HistoryPath,
                            validationError);
                    }
                }
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Skipping corrupt promotion record in {Path}", _options.HistoryPath);
            }
        }

        return records;
    }

    /// <inheritdoc />
    public async Task AppendAsync(StrategyPromotionRecord record, CancellationToken ct = default)
    {
        ValidateRecord(record);
        await using var authorityLock = await AcquireAuthorityLockAsync(ct).ConfigureAwait(false);
        await AppendCoreAsync(record, ct).ConfigureAwait(false);
    }

    private async Task AppendCoreAsync(StrategyPromotionRecord record, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(record, PromotionRecordJsonContext.Default.StrategyPromotionRecord);
        await AtomicFileWriter.AppendLinesAsync(_options.HistoryPath, [json], ct).ConfigureAwait(false);
    }

    private async Task<FileStream> AcquireAuthorityLockAsync(CancellationToken ct)
    {
        Directory.CreateDirectory(_options.RootDirectory);
        var startedAt = Stopwatch.GetTimestamp();

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(
                    _options.AuthorityLockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    bufferSize: 1,
                    FileOptions.Asynchronous);
            }
            catch (IOException) when (Stopwatch.GetElapsedTime(startedAt) < AuthorityLockTimeout)
            {
                await Task.Delay(AuthorityLockRetryDelay, ct).ConfigureAwait(false);
            }
        }
    }

    private static bool HasSameDecisionKey(
        StrategyPromotionRecord left,
        StrategyPromotionRecord right) =>
        string.Equals(left.SourceRunId, right.SourceRunId, StringComparison.Ordinal) &&
        left.SourceRunType == right.SourceRunType &&
        left.TargetRunType == right.TargetRunType;

    private static void ValidateRecord(StrategyPromotionRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (!PromotionService.TryValidatePromotionRecord(record, out var validationError))
        {
            throw new InvalidOperationException(validationError ?? "Promotion record is invalid.");
        }
    }
}
