using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using Meridian.Application.Config;
using Meridian.Application.Logging;

namespace Meridian.Application.Monitoring;

/// <summary>
/// Periodically writes a small status snapshot JSON file for dashboards.
/// </summary>
public sealed class StatusWriter : IAsyncDisposable
{
    private readonly string _path;
    private readonly Func<AppConfig> _configProvider;
    private readonly IEventMetrics _metrics;
    private readonly CancellationTokenSource _cts = new();
    private readonly Serilog.ILogger _log = LoggingSetup.ForContext<StatusWriter>();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
    private Task? _loop;

    public StatusWriter(string path, Func<AppConfig> configProvider, IEventMetrics? metrics = null)
    {
        _path = path;
        _configProvider = configProvider;
        _metrics = metrics ?? new DefaultEventMetrics();
    }

    public void Start(TimeSpan interval)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        _loop = WriteLoopAsync(interval);
    }

    private async Task WriteLoopAsync(TimeSpan interval, CancellationToken ct = default)
    {
        while (!_cts.IsCancellationRequested)
        {
            await WriteOnceAsync();
            try
            { await Task.Delay(interval, _cts.Token); }
            catch (TaskCanceledException) { }
        }
    }

    public async Task WriteOnceAsync(CancellationToken ct = default)
    {
        var cfg = _configProvider();
        var payload = new
        {
            timestampUtc = DateTimeOffset.UtcNow.ToString("O"),
            metrics = new
            {
                published = _metrics.Published,
                dropped = _metrics.Dropped,
                integrity = _metrics.Integrity,
                historicalBars = _metrics.HistoricalBars
            },
            symbols = cfg.Symbols ?? Array.Empty<SymbolConfig>()
        };

        var json = JsonSerializer.Serialize(payload, JsonOptions);

        await File.WriteAllTextAsync(_path, json, ct);
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        if (_loop is not null)
        {
            try
            {
                await _loop.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "Error during StatusWriter monitoring loop disposal");
            }
        }
        _cts.Dispose();
    }
}
