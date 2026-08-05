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
    /// <summary>Sanity bound for one accumulated trade-update message; far above any real payload.</summary>
    private const int MaxTradeUpdateMessageBytes = 4 * 1024 * 1024;

    private readonly AlpacaOptions _options;
    private readonly ILogger<AlpacaTradeUpdatesClient> _logger;
    private readonly Channel<ExecutionReport> _reports = Channel.CreateUnbounded<ExecutionReport>();
    private readonly HashSet<string> _seenEventIds = new(StringComparer.Ordinal);
    private readonly Queue<string> _seenOrder = new();
    private Func<CancellationToken, Task<IReadOnlyList<ExecutionReport>>> _reconcile;
    private readonly TimeProvider _clock;
    private readonly TimeSpan _staleAfter;
    private readonly IAlpacaTradeUpdateCursorStore _cursorStore;
    private ClientWebSocket? _socket;
    private CancellationTokenSource? _runCts;
    private Task? _runTask;
    private DateTimeOffset? _watermark;
    private DateTimeOffset? _lastUpdateAt;
    private string? _failure;

    public AlpacaTradeUpdatesClient(AlpacaOptions options, ILogger<AlpacaTradeUpdatesClient> logger,
        Func<CancellationToken, Task<IReadOnlyList<ExecutionReport>>>? reconcile = null,
        TimeProvider? clock = null, TimeSpan? staleAfter = null, IAlpacaTradeUpdateCursorStore? cursorStore = null)
    {
        _options = options;
        _logger = logger;
        _reconcile = reconcile ?? (_ => Task.FromResult<IReadOnlyList<ExecutionReport>>([]));
        _clock = clock ?? TimeProvider.System;
        _staleAfter = staleAfter ?? TimeSpan.FromSeconds(30);
        _cursorStore = cursorStore ?? new FileAlpacaTradeUpdateCursorStore();
    }

    public bool IsHealthy => _socket?.State == WebSocketState.Open && _lastUpdateAt is { } at && _clock.GetUtcNow() - at <= _staleAfter && _failure is null;
    public DateTimeOffset? Watermark => _watermark;
    public string? UnhealthyReason => IsHealthy ? null : _failure ?? "Trade-update stream is delayed, disconnected, or has not produced an update.";
    public IAsyncEnumerable<ExecutionReport> Reports => _reports.Reader.ReadAllAsync();

    public Task StartAsync(CancellationToken ct = default)
    {
        if (_runTask is not null)
            return Task.CompletedTask;
        _watermark = _cursorStore.Load();
        foreach (var eventId in _cursorStore.LoadRecentEventIds())
            Remember(eventId);
        _runCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _runTask = Task.Run(() => RunAsync(_runCts.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    /// <summary>Sets the REST snapshot reconciliation invoked after every authenticated reconnect.</summary>
    public void ConfigureReconciliation(Func<CancellationToken, Task<IReadOnlyList<ExecutionReport>>> reconcile) =>
        _reconcile = reconcile ?? throw new ArgumentNullException(nameof(reconcile));

    internal async Task ProcessMessageAsync(string message, CancellationToken ct = default)
    {
        using var doc = JsonDocument.Parse(message);
        var root = doc.RootElement;
        if (root.TryGetProperty("stream", out var stream) && stream.GetString() != "trade_updates")
            return;
        if (!root.TryGetProperty("data", out var data))
            return;
        var eventId = data.TryGetProperty("event_id", out var id) ? id.GetString() : null;
        if (string.IsNullOrWhiteSpace(eventId))
            eventId = data.TryGetProperty("id", out id) ? id.GetString() : null;
        if (string.IsNullOrWhiteSpace(eventId) || !Remember(eventId))
            return;

        var order = data.GetProperty("order");
        var timestamp = ReadTime(data, "timestamp") ?? ReadTime(order, "updated_at") ?? _clock.GetUtcNow();
        _watermark = _watermark is null || timestamp > _watermark ? timestamp : _watermark;
        _cursorStore.Save(_watermark.Value, _seenOrder);
        _lastUpdateAt = _clock.GetUtcNow();
        _failure = null;
        var status = order.TryGetProperty("status", out var statusElement) ? statusElement.GetString() : null;
        var mapped = Map(status);
        var report = new ExecutionReport
        {
            OrderId = ReadString(order, "id") ?? throw new JsonException("Alpaca trade update lacks order.id."),
            GatewayOrderId = ReadString(order, "id"),
            ClientOrderId = ReadString(order, "client_order_id"),
            Symbol = ReadString(order, "symbol") ?? string.Empty,
            Side = string.Equals(ReadString(order, "side"), "sell", StringComparison.OrdinalIgnoreCase)
                ? Meridian.Execution.Sdk.OrderSide.Sell
                : Meridian.Execution.Sdk.OrderSide.Buy,
            OrderStatus = mapped.status,
            ReportType = mapped.type,
            OrderQuantity = ReadDecimal(order, "qty"),
            FilledQuantity = ReadDecimal(order, "filled_qty"),
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
            try
            {
                _socket = new ClientWebSocket();
                await _socket.ConnectAsync(new Uri(_options.UseSandbox ? "wss://paper-api.alpaca.markets/stream" : "wss://api.alpaca.markets/stream"), ct).ConfigureAwait(false);
                await SendAsync(new { action = "auth", key = _options.KeyId, secret = _options.SecretKey }, ct).ConfigureAwait(false);
                await SendAsync(new { action = "listen", data = new { streams = new[] { "trade_updates" } } }, ct).ConfigureAwait(false);
                foreach (var report in await _reconcile(ct).ConfigureAwait(false))
                    await _reports.Writer.WriteAsync(report, ct).ConfigureAwait(false);
                delay = TimeSpan.FromSeconds(1);
                var buffer = new byte[64 * 1024];
                using var messageBuffer = new MemoryStream();
                while (_socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
                {
                    var result = await _socket.ReceiveAsync(buffer, ct).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close)
                        break;
                    // Frames can be fragmented or exceed the receive buffer; accumulate until the
                    // final fragment and decode once so multi-byte UTF-8 sequences and large
                    // trade-update payloads are never truncated mid-message. The cap bounds memory
                    // against an endpoint that never sets EndOfMessage; tripping it reconnects.
                    if (messageBuffer.Length + result.Count > MaxTradeUpdateMessageBytes)
                        throw new InvalidOperationException(
                            $"Alpaca trade-update message exceeded {MaxTradeUpdateMessageBytes} bytes without completing.");
                    messageBuffer.Write(buffer, 0, result.Count);
                    if (!result.EndOfMessage)
                        continue;
                    var message = Encoding.UTF8.GetString(messageBuffer.GetBuffer(), 0, (int)messageBuffer.Length);
                    messageBuffer.SetLength(0);
                    await ProcessMessageAsync(message, ct).ConfigureAwait(false);
                }
                _failure = "Alpaca trade-update socket closed.";
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex) { _failure = ex.Message; _logger.LogWarning(ex, "Alpaca trade-update stream failed; reconnecting"); }
            finally { _socket?.Dispose(); _socket = null; }
            await Task.Delay(delay, ct).ConfigureAwait(false);
            delay = TimeSpan.FromSeconds(Math.Min(30, delay.TotalSeconds * 2));
        }
    }

    private async Task SendAsync<T>(T value, CancellationToken ct) => await _socket!.SendAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value)), WebSocketMessageType.Text, true, ct).ConfigureAwait(false);
    private bool Remember(string id) { if (!_seenEventIds.Add(id)) return false; _seenOrder.Enqueue(id); if (_seenOrder.Count > 8192) _seenEventIds.Remove(_seenOrder.Dequeue()); return true; }
    private static string? ReadString(JsonElement e, string name) => e.TryGetProperty(name, out var v) ? v.GetString() : null;
    private static decimal ReadDecimal(JsonElement e, string name) => ReadNullableDecimal(e, name) ?? 0m;
    private static decimal? ReadNullableDecimal(JsonElement e, string name) => e.TryGetProperty(name, out var v) && decimal.TryParse(v.GetString(), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : null;
    private static DateTimeOffset? ReadTime(JsonElement e, string name) => e.TryGetProperty(name, out var v) && DateTimeOffset.TryParse(v.GetString(), out var t) ? t : null;
    private static (OrderStatus status, ExecutionReportType type) Map(string? s) => s?.ToLowerInvariant() switch { "filled" => (OrderStatus.Filled, ExecutionReportType.Fill), "partially_filled" => (OrderStatus.PartiallyFilled, ExecutionReportType.PartialFill), "canceled" => (OrderStatus.Cancelled, ExecutionReportType.Cancelled), "rejected" => (OrderStatus.Rejected, ExecutionReportType.Rejected), "expired" => (OrderStatus.Expired, ExecutionReportType.Expired), _ => (OrderStatus.Accepted, ExecutionReportType.New) };
    public async ValueTask DisposeAsync() { if (_runCts is not null) { await _runCts.CancelAsync().ConfigureAwait(false); if (_runTask is not null) await _runTask.ConfigureAwait(false); _runCts.Dispose(); } _reports.Writer.TryComplete(); }
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
        try
        { var state = File.Exists(_path) ? JsonSerializer.Deserialize<PersistedState>(File.ReadAllText(_path)) : null; return (state?.Watermark, state?.EventIds ?? []); }
        catch (JsonException) { return (null, []); }
    }
    private sealed record PersistedState(DateTimeOffset Watermark, IReadOnlyList<string> EventIds);
}
