using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using Meridian.Core.Config;
using Meridian.Core.Logging;
using Meridian.Storage.Archival;
using Meridian.Contracts.Monitoring;
using Meridian.Platform.Tracing;

namespace Meridian.Application.Monitoring;

/// <summary>
/// Periodically writes a small status snapshot JSON file for dashboards.
/// </summary>
public sealed class StatusWriter : IAsyncDisposable
{
    private readonly string _path;
    private readonly Func<AppConfig> _configProvider;
    private readonly IEventMetrics _metrics;
    private readonly TimeProvider _timeProvider;
    private readonly CancellationTokenSource _cts = new();
    private readonly Serilog.ILogger _log = LoggingSetup.ForContext<StatusWriter>();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
    private Task? _loop;

    public StatusWriter(string path, Func<AppConfig> configProvider, IEventMetrics? metrics = null, TimeProvider? timeProvider = null)
    {
        _path = path;
        _configProvider = configProvider;
        _metrics = metrics ?? new DefaultEventMetrics();
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public void Start(TimeSpan interval)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(interval, TimeSpan.Zero, nameof(interval));
        _loop ??= WriteLoopAsync(interval);
    }

    private async Task WriteLoopAsync(TimeSpan interval)
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                await WriteOnceAsync(_cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_cts.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _log.Warning(ex, "Status snapshot write failed; retrying at the next interval");
            }

            try
            {
                await Task.Delay(interval, _timeProvider, _cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_cts.IsCancellationRequested)
            {
                break;
            }
        }
    }

    public async Task WriteOnceAsync(CancellationToken ct = default)
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

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

        await AtomicFileWriter.WriteAsync(_path, json, ct).ConfigureAwait(false);
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
