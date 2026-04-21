using System.Text;
using System.Text.Json;
using Meridian.Storage.Archival;
using Meridian.Strategies.Promotions;
using Microsoft.Extensions.Logging;

namespace Meridian.Strategies.Storage;

/// <summary>
/// JSONL-backed promotion decision store.
/// </summary>
public sealed class JsonlPromotionRecordStore : IPromotionRecordStore
{
    private readonly string _historyPath;
    private readonly ILogger<JsonlPromotionRecordStore> _logger;
    private readonly SemaphoreSlim _appendLock = new(1, 1);

    public JsonlPromotionRecordStore(
        string baseDirectory,
        ILogger<JsonlPromotionRecordStore> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _historyPath = Path.Combine(baseDirectory, "promotion-history.jsonl");
    }

    public Task<IReadOnlyList<StrategyPromotionRecord>> LoadAsync(CancellationToken ct = default) =>
        ReadAllAsync(ct);

    public async Task AppendAsync(StrategyPromotionRecord record, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        var directory = Path.GetDirectoryName(_historyPath)!;
        Directory.CreateDirectory(directory);
        var line = JsonSerializer.Serialize(record);

        await _appendLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await AtomicFileWriter.AppendLinesAsync(_historyPath, [line], ct).ConfigureAwait(false);
        }
        finally
        {
            _appendLock.Release();
        }
    }

    public Task<IReadOnlyList<StrategyPromotionRecord>> GetHistoryAsync(CancellationToken ct = default) =>
        ReadAllAsync(ct);

    private async Task<IReadOnlyList<StrategyPromotionRecord>> ReadAllAsync(CancellationToken ct)
    {
        if (!File.Exists(_historyPath))
        {
            return [];
        }

        var records = new List<StrategyPromotionRecord>();
        await using var stream = File.OpenRead(_historyPath);
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
                var record = JsonSerializer.Deserialize<StrategyPromotionRecord>(line);
                if (record is not null)
                {
                    records.Add(record);
                }
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Skipping corrupt promotion record in {Path}", _historyPath);
            }
        }

        return records;
    }
}
