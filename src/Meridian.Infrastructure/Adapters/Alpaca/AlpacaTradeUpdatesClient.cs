using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Meridian.Core.Config;
using Meridian.Execution.Sdk;
using Microsoft.Extensions.Logging;

namespace Meridian.Infrastructure.Adapters.Alpaca;

/// <summary>Authenticated Alpaca <c>trade_updates</c> stream with durable event-id deduplication.</summary>
/// <remarks>The trading stream is an execution source of record only after REST reconciliation succeeds.</remarks>
public sealed class AlpacaTradeUpdatesClient : IAsyncDisposable
{
    private readonly AlpacaCredentialSnapshot _credentials;
    private readonly ILogger<AlpacaTradeUpdatesClient> _logger;
    private readonly Channel<ExecutionReport> _reports = Channel.CreateUnbounded<ExecutionReport>();
    private readonly HashSet<string> _seenEventIds = new(StringComparer.Ordinal);
    private readonly Queue<string> _seenOrder = new();
    private Func<CancellationToken, Task<IReadOnlyList<ExecutionReport>>> _reconcile;
    private readonly TimeProvider _clock;
    private readonly IAlpacaTradeUpdateCursorStore _cursorStore;
    private ClientWebSocket? _socket;
    private CancellationTokenSource? _runCts;
    private Task? _runTask;
    private DateTimeOffset? _watermark;
    private DateTimeOffset? _lastUpdateAt;
    private DateTimeOffset? _lastSubscriptionConfirmedAt;
    private string? _failure;
    private readonly TaskCompletionSource _initialSubscription = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public AlpacaTradeUpdatesClient(AlpacaOptions options, ILogger<AlpacaTradeUpdatesClient> logger,
        Func<CancellationToken, Task<IReadOnlyList<ExecutionReport>>>? reconcile = null,
        TimeProvider? clock = null, TimeSpan? staleAfter = null, IAlpacaTradeUpdateCursorStore? cursorStore = null,
        AlpacaCredentialSnapshot? credentials = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _credentials = credentials ?? AlpacaCredentialEnvironment.Resolve(options);
        _logger = logger;
        _reconcile = reconcile ?? (_ => Task.FromResult<IReadOnlyList<ExecutionReport>>([]));
        _clock = clock ?? TimeProvider.System;
        _ = staleAfter; // Kept for constructor compatibility; subscription health is not event-age based.
        _cursorStore = cursorStore ?? new FileAlpacaTradeUpdateCursorStore();
    }

    // A quiet account is healthy after Alpaca confirms the trade_updates subscription.
    // Order-event freshness is diagnostic only; trade events are naturally intermittent.
    public bool IsHealthy => _socket?.State == WebSocketState.Open && _lastSubscriptionConfirmedAt is not null && _failure is null;
    public DateTimeOffset? Watermark => _watermark;
    public DateTimeOffset? LastUpdateAt => _lastUpdateAt;
    internal Uri StreamEndpoint => new(_credentials.UseSandbox ? "wss://paper-api.alpaca.markets/stream" : "wss://api.alpaca.markets/stream");
    public string? UnhealthyReason => IsHealthy ? null : _failure ?? "Trade-update stream is disconnected or has not confirmed its subscription.";
    public IAsyncEnumerable<ExecutionReport> Reports => _reports.Reader.ReadAllAsync();

    public async Task StartAsync(CancellationToken ct = default)
    {
        if (_runTask is null)
        {
            _watermark = _cursorStore.Load();
            foreach (var eventId in _cursorStore.LoadRecentEventIds()) Remember(eventId);
            _runCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _runTask = Task.Run(() => RunAsync(_runCts.Token), CancellationToken.None);
        }

        await _initialSubscription.Task.WaitAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Sets the REST snapshot reconciliation invoked after every authenticated reconnect.</summary>
    public void ConfigureReconciliation(Func<CancellationToken, Task<IReadOnlyList<ExecutionReport>>> reconcile) =>
        _reconcile = reconcile ?? throw new ArgumentNullException(nameof(reconcile));

    internal async Task ProcessMessageAsync(string message, CancellationToken ct = default)
    {
        using var doc = JsonDocument.Parse(message);
        var root = doc.RootElement;
        if (!root.TryGetProperty("stream", out var stream) || !root.TryGetProperty("data", out var data)) return;

        if (stream.GetString() == "authorization" &&
            string.Equals(ReadString(data, "status"), "authorized", StringComparison.OrdinalIgnoreCase))
        {
            _failure = null;
            return;
        }

        if (stream.GetString() == "listening" && data.TryGetProperty("streams", out var streams) &&
            streams.EnumerateArray().Any(static value => string.Equals(value.GetString(), "trade_updates", StringComparison.Ordinal)))
        {
            _lastSubscriptionConfirmedAt = _clock.GetUtcNow();
            _failure = null;
            _initialSubscription.TrySetResult();
            return;
        }

        if (stream.GetString() != "trade_updates") return;
        _lastUpdateAt = _clock.GetUtcNow();
        _failure = null;

        if (!data.TryGetProperty("order", out var order))
            throw new JsonException("Alpaca trade update lacks order.");

        var timestamp = ReadTime(data, "timestamp") ?? ReadTime(order, "updated_at") ?? _clock.GetUtcNow();
        var eventId = CreateEventIdentity(data, order, timestamp);
        if (!Remember(eventId)) return;

        _watermark = _watermark is null || timestamp > _watermark ? timestamp : _watermark;
        _cursorStore.Save(_watermark.Value, _seenOrder);
        var status = order.TryGetProperty("status", out var statusElement) ? statusElement.GetString() : null;
        var mapped = Map(status);
        var report = new ExecutionReport {
            OrderId = ReadString(order, "id") ?? throw new JsonException("Alpaca trade update lacks order.id."),
            GatewayOrderId = ReadString(order, "id"), ClientOrderId = ReadString(order, "client_order_id"),
            Symbol = ReadString(order, "symbol") ?? string.Empty,
            Side = string.Equals(ReadString(order, "side"), "sell", StringComparison.OrdinalIgnoreCase) ? OrderSide.Sell : OrderSide.Buy,
            OrderStatus = mapped.status, ReportType = mapped.type,
            OrderQuantity = ReadDecimal(order, "qty"), FilledQuantity = ReadDecimal(order, "filled_qty"),
            FillPrice = ReadNullableDecimal(data, "price") ?? ReadNullableDecimal(order, "filled_avg_price"),
            Timestamp = timestamp,
            RejectReason = ReadString(data, "reason"),
            Diagnostics = new ExecutionDiagnostics { BrokerStatus = status, Category = "alpaca-trade-update" }
        };
        await _reports.Writer.WriteAsync(report, ct).ConfigureAwait(false);
    }

    private async Task RunAsync(CancellationToken ct)
    {
        var delay = TimeSpan.FromSeconds(1);
        while (!ct.IsCancellationRequested)
        {
            try {
                _socket = new ClientWebSocket();
                await _socket.ConnectAsync(StreamEndpoint, ct).ConfigureAwait(false);
                await SendAsync(new { action = "auth", key = _credentials.KeyId, secret = _credentials.SecretKey }, ct).ConfigureAwait(false);
                await SendAsync(new { action = "listen", data = new { streams = new[] { "trade_updates" } } }, ct).ConfigureAwait(false);
                foreach (var report in await _reconcile(ct).ConfigureAwait(false)) await _reports.Writer.WriteAsync(report, ct).ConfigureAwait(false);
                delay = TimeSpan.FromSeconds(1);
                var buffer = new byte[64 * 1024];
                while (_socket.State == WebSocketState.Open && !ct.IsCancellationRequested) {
                    var result = await _socket.ReceiveAsync(buffer, ct).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close) break;
                    await ProcessMessageAsync(Encoding.UTF8.GetString(buffer, 0, result.Count), ct).ConfigureAwait(false);
                }
                _failure = "Alpaca trade-update socket closed.";
            } catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex) { _failure = ex.Message; _logger.LogWarning(ex, "Alpaca trade-update stream failed; reconnecting"); }
            finally { _socket?.Dispose(); _socket = null; }
            await Task.Delay(delay, ct).ConfigureAwait(false);
            delay = TimeSpan.FromSeconds(Math.Min(30, delay.TotalSeconds * 2));
        }
    }

    private async Task SendAsync<T>(T value, CancellationToken ct) => await _socket!.SendAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value)), WebSocketMessageType.Text, true, ct).ConfigureAwait(false);
    private static string CreateEventIdentity(JsonElement data, JsonElement order, DateTimeOffset timestamp)
    {
        // Alpaca identifies trade updates by event, with execution_id present for executions.
        // Non-execution lifecycle events are stable within an order at their broker timestamp.
        var executionId = ReadString(data, "execution_id");
        if (!string.IsNullOrWhiteSpace(executionId)) return $"execution:{executionId}";

        var orderId = ReadString(order, "id") ?? throw new JsonException("Alpaca trade update lacks order.id.");
        var eventName = ReadString(data, "event") ?? "unknown";
        return $"event:{eventName}:{orderId}:{timestamp.UtcDateTime.Ticks}";
    }

    private bool Remember(string id) { if (!_seenEventIds.Add(id)) return false; _seenOrder.Enqueue(id); if (_seenOrder.Count > 8192) _seenEventIds.Remove(_seenOrder.Dequeue()); return true; }
    private static string? ReadString(JsonElement e, string name) => e.TryGetProperty(name, out var v) ? v.GetString() : null;
    private static decimal ReadDecimal(JsonElement e, string name) => ReadNullableDecimal(e, name) ?? 0m;
    private static decimal? ReadNullableDecimal(JsonElement e, string name) => e.TryGetProperty(name, out var v) && decimal.TryParse(v.GetString(), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : null;
    private static DateTimeOffset? ReadTime(JsonElement e, string name) => e.TryGetProperty(name, out var v) && DateTimeOffset.TryParse(v.GetString(), out var t) ? t : null;
    private static (OrderStatus status, ExecutionReportType type) Map(string? s) => s?.ToLowerInvariant() switch { "filled" => (OrderStatus.Filled, ExecutionReportType.Fill), "partially_filled" => (OrderStatus.PartiallyFilled, ExecutionReportType.PartialFill), "canceled" => (OrderStatus.Cancelled, ExecutionReportType.Cancelled), "rejected" => (OrderStatus.Rejected, ExecutionReportType.Rejected), "expired" => (OrderStatus.Expired, ExecutionReportType.Expired), _ => (OrderStatus.Accepted, ExecutionReportType.New) };
    public async ValueTask DisposeAsync() { _initialSubscription.TrySetCanceled(); if (_runCts is not null) { await _runCts.CancelAsync().ConfigureAwait(false); if (_runTask is not null) await _runTask.ConfigureAwait(false); _runCts.Dispose(); } _reports.Writer.TryComplete(); }
}

/// <summary>Durable execution watermark seam; implementations must not persist credentials or payloads.</summary>
public interface IAlpacaTradeUpdateCursorStore
{
    DateTimeOffset? Load();
    IReadOnlyList<string> LoadRecentEventIds() => [];
    void Save(DateTimeOffset watermark, IReadOnlyCollection<string> recentEventIds);
}

/// <summary>Atomic, local cursor store used by the default execution stream client.</summary>
public sealed class FileAlpacaTradeUpdateCursorStore : IAlpacaTradeUpdateCursorStore
{
    private readonly string _path;
    public FileAlpacaTradeUpdateCursorStore(string? path = null) => _path = path ?? Path.Combine(AppContext.BaseDirectory, "state", "alpaca-trade-updates.cursor");
    public DateTimeOffset? Load() => Read().watermark;
    public IReadOnlyList<string> LoadRecentEventIds() => Read().ids;
    public void Save(DateTimeOffset watermark, IReadOnlyCollection<string> recentEventIds)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporary = _path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(new PersistedState(watermark, recentEventIds.TakeLast(8192).ToArray())));
        File.Move(temporary, _path, overwrite: true);
    }
    private (DateTimeOffset? watermark, IReadOnlyList<string> ids) Read()
    {
        try { var state = File.Exists(_path) ? JsonSerializer.Deserialize<PersistedState>(File.ReadAllText(_path)) : null; return (state?.Watermark, state?.EventIds ?? []); }
        catch (JsonException) { return (null, []); }
    }
    private sealed record PersistedState(DateTimeOffset Watermark, IReadOnlyList<string> EventIds);
}
