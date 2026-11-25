using System.Text.Json;
using Meridian.Core.Serialization;
using Meridian.Domain.Events;

namespace Meridian.Backtesting.Engine;

/// <summary>Validates raw-price evidence completely before any strategy callback or accounting.</summary>
internal sealed class CanonicalReplayCapture : IAsyncDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"meridian-canonical-replay-{Guid.NewGuid():N}.jsonl");

    public async Task CaptureAsync(
        IReadOnlyList<IAsyncEnumerable<MarketEvent>> streams,
        IReadOnlyDictionary<string, int> symbolOrder,
        CancellationToken ct)
    {
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            Options = FileOptions.Asynchronous | FileOptions.SequentialScan
        };
        if (!OperatingSystem.IsWindows())
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        await using var writer = new StreamWriter(new FileStream(_path, options));
        var hasBars = false;
        await foreach (var item in MultiSymbolMergeEnumerator.MergeAsync(streams, ct, symbolOrder).ConfigureAwait(false))
        {
            if (item.Payload is not HistoricalBar { IsAdjusted: false })
                throw new InvalidOperationException("RawPricesWithCanonicalEvents requires explicitly unadjusted historical bars; adjusted, unknown-basis, and non-bar events are unsupported.");
            hasBars = true;
            await writer.WriteLineAsync(JsonSerializer.Serialize(item, MarketDataJsonContext.HighPerformanceOptions).AsMemory(), ct).ConfigureAwait(false);
        }
        if (!hasBars)
            throw new InvalidOperationException("Canonical raw-price replay requires at least one explicitly unadjusted historical bar.");
    }

    public IAsyncEnumerable<MarketEvent> ReadAsync(CancellationToken ct) => BacktestEngine.ReadCapturedSnapshotAsync(_path, ct);

    public ValueTask DisposeAsync()
    {
        if (File.Exists(_path))
            File.Delete(_path);
        return ValueTask.CompletedTask;
    }
}
