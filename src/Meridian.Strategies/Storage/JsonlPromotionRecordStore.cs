using System.Text;
using System.Text.Json;
using Meridian.Storage.Archival;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Promotions;
using Meridian.Strategies.Serialization;
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
}

/// <summary>
/// Append-only JSONL promotion-history store.
/// </summary>
public sealed class JsonlPromotionRecordStore : IPromotionRecordStore
{
    private readonly PromotionRecordStoreOptions _options;
    private readonly ILogger<JsonlPromotionRecordStore> _logger;
    private readonly SemaphoreSlim _appendLock = new(1, 1);

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
                    records.Add(record);
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
        ArgumentNullException.ThrowIfNull(record);

        Directory.CreateDirectory(_options.RootDirectory);
        var json = JsonSerializer.Serialize(record, PromotionRecordJsonContext.Default.StrategyPromotionRecord);

        await _appendLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await AtomicFileWriter.AppendLinesAsync(_options.HistoryPath, [json], ct).ConfigureAwait(false);
        }
        finally
        {
            _appendLock.Release();
        }
    }
}
