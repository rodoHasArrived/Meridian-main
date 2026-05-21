using System.Text;
using System.Text.Json;
using Meridian.Contracts.Workstation;
using Meridian.Storage.Archival;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Serialization;
using Meridian.Strategies.Services;
using Microsoft.Extensions.Logging;

namespace Meridian.Strategies.Storage;

/// <summary>
/// Configuration for durable Strategy Builder draft storage.
/// </summary>
public sealed record StrategyDesignStoreOptions(string RootDirectory)
{
    public static StrategyDesignStoreOptions Default { get; } = new(
        Path.Combine(AppContext.BaseDirectory, "data", "strategies", "designer"));

    public string DraftHistoryPath => Path.Combine(RootDirectory, "strategy-design-drafts.jsonl");
}

/// <summary>
/// Append-only JSONL repository for Strategy Builder design drafts.
/// </summary>
public sealed class JsonlStrategyDesignRepository : IStrategyDesignRepository
{
    private readonly StrategyDesignStoreOptions _options;
    private readonly ILogger<JsonlStrategyDesignRepository> _logger;
    private readonly SemaphoreSlim _appendLock = new(1, 1);

    public JsonlStrategyDesignRepository(
        StrategyDesignStoreOptions? options,
        ILogger<JsonlStrategyDesignRepository> logger)
    {
        _options = options ?? StrategyDesignStoreOptions.Default;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public JsonlStrategyDesignRepository(
        string baseDirectory,
        ILogger<JsonlStrategyDesignRepository> logger)
        : this(new StrategyDesignStoreOptions(baseDirectory), logger)
    {
    }

    /// <inheritdoc />
    public async Task SaveAsync(StrategyDesignDocument document, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        Directory.CreateDirectory(_options.RootDirectory);
        var json = JsonSerializer.Serialize(document, StrategyDesignJsonContext.Default.StrategyDesignDocument);

        await _appendLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await AtomicFileWriter.AppendLinesAsync(_options.DraftHistoryPath, [json], ct).ConfigureAwait(false);
        }
        finally
        {
            _appendLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<StrategyDesignDocument?> GetAsync(string documentId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);

        var latest = await LoadLatestByIdAsync(ct).ConfigureAwait(false);
        latest.TryGetValue(documentId, out var document);
        return document;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<StrategyDesignDraftSummary>> ListDraftsAsync(CancellationToken ct = default)
    {
        var latest = await LoadLatestByIdAsync(ct).ConfigureAwait(false);
        return latest.Values
            .OrderByDescending(static document => document.UpdatedAt)
            .ThenBy(static document => document.Name, StringComparer.OrdinalIgnoreCase)
            .Select(StrategyDesignService.CreateDraftSummary)
            .ToArray();
    }

    private async Task<Dictionary<string, StrategyDesignDocument>> LoadLatestByIdAsync(CancellationToken ct)
    {
        var latest = new Dictionary<string, StrategyDesignDocument>(StringComparer.Ordinal);
        if (!File.Exists(_options.DraftHistoryPath))
        {
            return latest;
        }

        await using var stream = File.OpenRead(_options.DraftHistoryPath);
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
                var document = JsonSerializer.Deserialize(
                    line,
                    StrategyDesignJsonContext.Default.StrategyDesignDocument);
                if (document is null || string.IsNullOrWhiteSpace(document.DocumentId))
                {
                    continue;
                }

                if (!latest.TryGetValue(document.DocumentId, out var existing) ||
                    document.UpdatedAt >= existing.UpdatedAt)
                {
                    latest[document.DocumentId] = document;
                }
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Skipping corrupt strategy design draft in {Path}", _options.DraftHistoryPath);
            }
        }

        return latest;
    }
}
