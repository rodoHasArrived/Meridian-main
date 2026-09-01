using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using Meridian.Contracts.Integrity;
using Meridian.Core.Config;
using Meridian.Core.IO;
using Meridian.Execution.Sdk;
using Meridian.Storage.Archival;
using Microsoft.Extensions.Logging;
using ExecutionOrderSide = Meridian.Execution.Sdk.OrderSide;

namespace Meridian.Infrastructure.Adapters.Alpaca;

/// <summary>Authenticated Alpaca <c>trade_updates</c> stream with durable event-id deduplication.</summary>
/// <remarks>The trading stream is an execution source of record only after REST reconciliation succeeds.</remarks>
public sealed class AlpacaTradeUpdatesClient : IAsyncDisposable
{
    /// <summary>Sanity bound for one accumulated trade-update message; far above any real payload.</summary>
    private const int MaxTradeUpdateMessageBytes = 4 * 1024 * 1024;

    private readonly AlpacaOptions _options;
    private readonly ILogger<AlpacaTradeUpdatesClient> _logger;
    private readonly Channel<TradeUpdateEnvelope> _reports = Channel.CreateUnbounded<TradeUpdateEnvelope>(
        new UnboundedChannelOptions { AllowSynchronousContinuations = false });
    private readonly object _cursorGate = new();
    private readonly HashSet<string> _committedEventIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _committedEventHashes = new(StringComparer.Ordinal);
    private readonly Queue<string> _committedEventOrder = new();
    private readonly Dictionary<string, AlpacaTradeUpdatePendingEnvelope> _pendingByEventId =
        new(StringComparer.Ordinal);
    private readonly HashSet<string> _queuedEventIds = new(StringComparer.Ordinal);
    private Func<DateTimeOffset?, CancellationToken, Task<IReadOnlyList<AlpacaReconciliationReport>>> _reconcile;
    private readonly TimeProvider _clock;
    private readonly TimeSpan _staleAfter;
    private IAlpacaTradeUpdateCursorStore? _cursorStore;
    private readonly bool _usesDefaultCursorStore;
    private ClientWebSocket? _socket;
    private CancellationTokenSource? _runCts;
    private Task? _runTask;
    private DateTimeOffset? _watermark;
    private DateTimeOffset? _lastUpdateAt;
    private string? _failure;
    private string? _transportFailure;
    private volatile bool _streamReady;
    private string? _stateIdentityScope;
    private AlpacaCredentialSnapshot? _scopedCredentials;
    private bool _stateInitialized;
    private int _disposeState;

    public AlpacaTradeUpdatesClient(
        AlpacaOptions options,
        ILogger<AlpacaTradeUpdatesClient> logger,
        Func<CancellationToken, Task<IReadOnlyList<ExecutionReport>>>? reconcile = null,
        TimeProvider? clock = null,
        TimeSpan? staleAfter = null,
        IAlpacaTradeUpdateCursorStore? cursorStore = null)
    {
        _options = options;
        _logger = logger;
        _reconcile = reconcile is null
            ? static (_, _) => Task.FromResult<IReadOnlyList<AlpacaReconciliationReport>>([])
            : async (_, ct) => (await reconcile(ct).ConfigureAwait(false))
                .Select(report => new AlpacaReconciliationReport("rest-reconciliation", null, report))
                .ToArray();
        _clock = clock ?? TimeProvider.System;
        _staleAfter = staleAfter ?? TimeSpan.FromSeconds(30);
        _cursorStore = cursorStore;
        _usesDefaultCursorStore = cursorStore is null;
    }

    /// <summary>
    /// Whether the stream is an execution source of record right now: the socket is open, the
    /// subscription was authenticated and the post-connect REST reconciliation completed, and no
    /// durable-state failure is outstanding.
    /// <para>
    /// Deliberately not a function of how recently a trade update arrived. An idle or order-free
    /// account legitimately produces no updates for hours, and reading that silence as a dead
    /// stream blocked every new submission on such an account once the stale window lapsed.
    /// Transport liveness is instead established by the WebSocket keep-alive: the socket pings
    /// on <see cref="_staleAfter"/>'s cadence and is aborted when the peer stops answering, which
    /// takes <see cref="WebSocketState.Open"/> away from this check.
    /// </para>
    /// </summary>
    public bool IsHealthy =>
        _socket?.State == WebSocketState.Open &&
        _streamReady &&
        _failure is null;

    /// <summary>When any frame -- control acknowledgement or trade update -- last arrived.</summary>
    public DateTimeOffset? LastActivityAt => _lastUpdateAt;

    public DateTimeOffset? Watermark
    {
        get
        {
            lock (_cursorGate)
                return _watermark;
        }
    }

    public string? UnhealthyReason => IsHealthy
        ? null
        : _failure
          ?? _transportFailure
          ?? "Trade-update stream is not connected, or its post-connect reconciliation has not completed.";

    public IAsyncEnumerable<ExecutionReport> Reports => ReadReportsAsync();

    public Task StartAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeState) != 0, this);

        lock (_cursorGate)
        {
            if (_runTask is not null)
                return Task.CompletedTask;

            EnsureStateInitializedLocked();
            _runCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _runTask = Task.Run(() => RunAsync(_runCts.Token), CancellationToken.None);
        }

        // The persisted normalized envelopes, rather than a broker redelivery, drive restart
        // replay. They were durably committed before their original channel admission.
        QueueDurablePendingForDelivery();
        return Task.CompletedTask;
    }

    /// <summary>Sets the REST snapshot reconciliation invoked after every authenticated reconnect.</summary>
    internal void ConfigureReconciliation(
        Func<DateTimeOffset?, CancellationToken, Task<IReadOnlyList<AlpacaReconciliationReport>>> reconcile) =>
        _reconcile = reconcile ?? throw new ArgumentNullException(nameof(reconcile));

    /// <summary>
    /// Binds the default durable inbox to the authenticated provider account and paper/live
    /// environment. A caller-supplied cursor store already owns its scope and is left unchanged.
    /// </summary>
    public void ConfigureDurableStateScope(string providerAccountId, string tradingEnvironment)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerAccountId);
        ArgumentException.ThrowIfNullOrWhiteSpace(tradingEnvironment);
        var identityScope = FileAlpacaTradeUpdateCursorStore.CreateIdentityScope(
            providerAccountId,
            tradingEnvironment);
        var credentials = AlpacaCredentialEnvironment.Resolve(_options);

        lock (_cursorGate)
        {
            if (_stateIdentityScope is not null &&
                !string.Equals(_stateIdentityScope, identityScope, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The Alpaca durable state scope cannot change to a different provider account or environment.");
            }
            _stateIdentityScope = identityScope;
            if (_scopedCredentials is not null && _scopedCredentials != credentials)
            {
                throw new InvalidOperationException(
                    "The Alpaca execution-stream credentials changed after durable state was scoped; create a new client and revalidate its account.");
            }
            _scopedCredentials = credentials;

            if (!_usesDefaultCursorStore)
                return;
            if (_stateInitialized || _runTask is not null)
            {
                throw new InvalidOperationException(
                    "The Alpaca durable state scope cannot change after the execution stream has initialized.");
            }

            _cursorStore = new FileAlpacaTradeUpdateCursorStore(providerAccountId, tradingEnvironment);
        }
    }

    internal async Task ProcessMessageAsync(string message, CancellationToken ct = default)
    {
        using var doc = JsonDocument.Parse(message);
        var root = doc.RootElement;
        if (!root.TryGetProperty("stream", out var stream))
            return;

        if (string.Equals(stream.GetString(), "authorization", StringComparison.Ordinal))
        {
            // A refused subscription keeps the socket open and silent, which is exactly the
            // shape an idle account has. Name it, so health reports the refusal rather than
            // waiting on updates that will never come.
            if (root.TryGetProperty("data", out var authorization)
                && string.Equals(ReadString(authorization, "status"), "unauthorized", StringComparison.OrdinalIgnoreCase))
            {
                _transportFailure = "Alpaca refused the trade-update stream authorization.";
                _streamReady = false;
            }

            return;
        }

        if (!string.Equals(stream.GetString(), "trade_updates", StringComparison.Ordinal))
            return;

        if (!root.TryGetProperty("data", out var data))
            return;

        var eventId = data.TryGetProperty("event_id", out var id) ? id.GetString() : null;
        if (string.IsNullOrWhiteSpace(eventId))
            eventId = data.TryGetProperty("execution_id", out id) ? id.GetString() : null;
        var order = data.GetProperty("order");
        var timestamp = ReadTime(data, "timestamp") ??
            ReadTime(order, "updated_at") ??
            throw new JsonException("Alpaca trade update lacks a stable timestamp.");
        var eventType = ReadString(data, "event");
        var status = order.TryGetProperty("status", out var statusElement) ? statusElement.GetString() : null;
        var mapped = Map(status);
        var report = new ExecutionReport
        {
            OrderId = ReadString(order, "id") ?? throw new JsonException("Alpaca trade update lacks order.id."),
            GatewayOrderId = ReadString(order, "id"),
            ClientOrderId = ReadString(order, "client_order_id"),
            Symbol = ReadString(order, "symbol") ?? string.Empty,
            Side = string.Equals(ReadString(order, "side"), "sell", StringComparison.OrdinalIgnoreCase)
                ? ExecutionOrderSide.Sell
                : ExecutionOrderSide.Buy,
            OrderStatus = mapped.status,
            ReportType = mapped.type,
            OrderQuantity = ReadDecimal(order, "qty"),
            FilledQuantity = ReadDecimal(order, "filled_qty"),
            FillPrice = ReadNullableDecimal(data, "price") ?? ReadNullableDecimal(order, "filled_avg_price"),
            // The event's own executed quantity, distinct from the order's cumulative filled_qty:
            // the OMS needs it to book a fill for an order it no longer tracks after a restart.
            LastFillQuantity = mapped.type is ExecutionReportType.Fill or ExecutionReportType.PartialFill
                ? ReadNullableDecimal(data, "qty")
                : null,
            Timestamp = timestamp,
            RejectReason = ReadString(data, "reason"),
            Diagnostics = new ExecutionDiagnostics
            {
                BrokerStatus = status,
                Category = string.IsNullOrWhiteSpace(eventType)
                    ? "alpaca-trade-update"
                    : $"alpaca-trade-update:{eventType.Trim().ToLowerInvariant()}"
            }
        };

        // Normalize first, then persist the bounded envelope atomically, and only then expose it
        // through the process-local channel. Raw broker JSON and credentials never enter the store.
        var contentHash = AlpacaTradeUpdateStateCodec.ComputeContentHash(report, timestamp);
        eventId = string.IsNullOrWhiteSpace(eventId)
            ? AlpacaTradeUpdateStateCodec.CreateStableEventId(
                RequireStateIdentityScope(),
                "stream",
                contentHash)
            : eventId.Trim();
        await AdmitAndDeliverAsync(eventId, contentHash, report, timestamp, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Reconciles the REST snapshot after an authenticated socket connection and commits every
    /// normalized report to the durable inbox before making it visible to transient consumers.
    /// </summary>
    internal async Task ReconcileAfterConnectAsync(CancellationToken ct = default)
    {
        DateTimeOffset? watermark;
        lock (_cursorGate)
        {
            EnsureStateInitializedLocked();
            watermark = _watermark;
        }

        foreach (var reconciliation in await _reconcile(watermark, ct).ConfigureAwait(false))
        {
            ArgumentNullException.ThrowIfNull(reconciliation);
            var report = reconciliation.Report;
            ArgumentNullException.ThrowIfNull(report);
            var contentHash = AlpacaTradeUpdateStateCodec.ComputeContentHash(report, report.Timestamp);
            var eventId = string.IsNullOrWhiteSpace(reconciliation.ProviderEventId)
                ? AlpacaTradeUpdateStateCodec.CreateStableEventId(
                    RequireStateIdentityScope(),
                    reconciliation.Source,
                    contentHash)
                : AlpacaTradeUpdateStateCodec.CreateProviderEventId(
                    RequireStateIdentityScope(),
                    reconciliation.Source,
                    reconciliation.ProviderEventId);
            await AdmitAndDeliverAsync(eventId, contentHash, report, report.Timestamp, ct).ConfigureAwait(false);
        }
    }

    private async Task AdmitAndDeliverAsync(
        string eventId,
        string contentHash,
        ExecutionReport report,
        DateTimeOffset timestamp,
        CancellationToken ct)
    {
        var candidateEnvelope = new AlpacaTradeUpdatePendingEnvelope(
            eventId,
            contentHash,
            report,
            timestamp);
        var deliveryEnvelope = AdmitDurably(candidateEnvelope);
        if (deliveryEnvelope is null)
            return;

        try
        {
            await _reports.Writer.WriteAsync(ToDeliveryEnvelope(deliveryEnvelope), ct).ConfigureAwait(false);
            _lastUpdateAt = _clock.GetUtcNow();
        }
        catch
        {
            // The durable envelope remains pending. Only its transient channel-admission marker is
            // released, so a later consumer or restarted client can replay it.
            ReleaseDelivery(eventId);
            throw;
        }
    }

    private async IAsyncEnumerable<ExecutionReport> ReadReportsAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // A new consumer is an explicit retry boundary. We intentionally do not requeue inside the
        // same iterator after an acknowledgement failure, which avoids a hot in-process replay loop.
        QueueDurablePendingForDelivery();

        await foreach (var envelope in _reports.Reader.ReadAllAsync(ct).ConfigureAwait(false))
        {
            try
            {
                yield return envelope.Report;

                // Code after yield runs only when downstream asks for the next report. Breaking,
                // disposal, or cancellation before that point leaves the durable pending envelope
                // untouched and therefore replayable.
                ct.ThrowIfCancellationRequested();
                if (envelope.EventId is not null)
                    _ = TryAcknowledge(envelope);
            }
            finally
            {
                if (envelope.EventId is not null)
                    ReleaseDelivery(envelope.EventId);
            }
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        var delay = TimeSpan.FromSeconds(1);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var credentials = _scopedCredentials ?? AlpacaCredentialEnvironment.Resolve(_options);
                _socket = new ClientWebSocket();
                ConfigureTransportLiveness(_socket);
                await _socket.ConnectAsync(
                        new Uri(credentials.UseSandbox
                            ? "wss://paper-api.alpaca.markets/stream"
                            : "wss://api.alpaca.markets/stream"),
                        ct)
                    .ConfigureAwait(false);
                await SendAsync(new { action = "auth", key = credentials.KeyId, secret = credentials.SecretKey }, ct)
                    .ConfigureAwait(false);
                await SendAsync(
                        new { action = "listen", data = new { streams = new[] { "trade_updates" } } },
                        ct)
                    .ConfigureAwait(false);

                await ReconcileAfterConnectAsync(ct).ConfigureAwait(false);

                // Source of record from here: authenticated, subscribed, and reconciled against
                // the REST snapshot. A transport failure from the previous connection is over.
                _transportFailure = null;
                _lastUpdateAt = _clock.GetUtcNow();
                _streamReady = true;

                delay = TimeSpan.FromSeconds(1);
                var buffer = new byte[64 * 1024];
                using var messageBuffer = new MemoryStream();
                while (_socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
                {
                    var result = await _socket.ReceiveAsync(buffer, ct).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close)
                        break;

                    // Frames can be fragmented or exceed the receive buffer; accumulate until the
                    // final fragment and decode once. The cap also bounds a peer that never marks
                    // EndOfMessage.
                    if (messageBuffer.Length + result.Count > MaxTradeUpdateMessageBytes)
                    {
                        throw new InvalidOperationException(
                            $"Alpaca trade-update message exceeded {MaxTradeUpdateMessageBytes} bytes without completing.");
                    }

                    messageBuffer.Write(buffer, 0, result.Count);
                    if (!result.EndOfMessage)
                        continue;

                    var message = Encoding.UTF8.GetString(
                        messageBuffer.GetBuffer(),
                        0,
                        checked((int)messageBuffer.Length));
                    messageBuffer.SetLength(0);
                    _lastUpdateAt = _clock.GetUtcNow();
                    await ProcessMessageAsync(message, ct).ConfigureAwait(false);
                }

                _transportFailure = "Alpaca trade-update socket closed.";
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _transportFailure = ex.Message;
                _logger.LogWarning(ex, "Alpaca trade-update stream failed; reconnecting");
            }
            finally
            {
                _streamReady = false;
                _socket?.Dispose();
                _socket = null;
            }

            try
            {
                await Task.Delay(delay, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }

            delay = TimeSpan.FromSeconds(Math.Min(30, delay.TotalSeconds * 2));
        }
    }

    /// <summary>
    /// Makes the socket's own state a liveness signal. The client pings on half the stale window
    /// and the runtime aborts the connection when a pong does not arrive within the window, so a
    /// transport that died without a close frame stops reporting <see cref="WebSocketState.Open"/>
    /// instead of sitting open and silent -- the same silence an idle account produces, which is
    /// why message recency cannot be the signal.
    /// </summary>
    private void ConfigureTransportLiveness(ClientWebSocket socket)
    {
        var timeout = _staleAfter > TimeSpan.Zero ? _staleAfter : TimeSpan.FromSeconds(30);
        var interval = TimeSpan.FromTicks(Math.Max(TimeSpan.TicksPerSecond, timeout.Ticks / 2));
        socket.Options.KeepAliveInterval = interval;
        socket.Options.KeepAliveTimeout = timeout;
    }

    private async Task SendAsync<T>(T value, CancellationToken ct) =>
        await _socket!.SendAsync(
                Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value)),
                WebSocketMessageType.Text,
                true,
                ct)
            .ConfigureAwait(false);

    private AlpacaTradeUpdatePendingEnvelope? AdmitDurably(AlpacaTradeUpdatePendingEnvelope candidate)
    {
        lock (_cursorGate)
        {
            EnsureStateInitializedLocked();

            try
            {
                var persisted = CursorStoreLocked.UpdateState(current =>
                {
                    var validated = AlpacaTradeUpdateStateCodec.ValidateAndSnapshot(current);
                    var pending = validated.PendingEnvelopes.FirstOrDefault(item =>
                        string.Equals(item.EventId, candidate.EventId, StringComparison.Ordinal));
                    if (pending is not null)
                    {
                        EnsureSameContent(candidate.EventId, pending.ContentHash, candidate.ContentHash);
                        return current;
                    }

                    if (validated.EventIds.Contains(candidate.EventId, StringComparer.Ordinal))
                    {
                        if (validated.EventHashes.TryGetValue(candidate.EventId, out var committedHash))
                            EnsureSameContent(candidate.EventId, committedHash, candidate.ContentHash);
                        return current;
                    }

                    return new AlpacaTradeUpdateCursorState(
                        AlpacaTradeUpdateCursorState.CurrentVersion,
                        validated.Watermark,
                        validated.EventIds,
                        validated.EventHashes,
                        validated.PendingEnvelopes.Append(candidate).ToArray());
                });
                ApplyStateLocked(persisted, preserveQueuedDeliveries: true);
            }
            catch (Exception ex)
            {
                _ = TryReloadStateAfterWriteFailureLocked(out var reloadFailure);
                _failure = $"Alpaca trade-update inbox save failed: {ex.Message}";
                _logger.LogWarning(
                    ex,
                    "Alpaca trade-update {EventId} was not admitted because its durable inbox write failed",
                    candidate.EventId);
                if (reloadFailure is not null)
                {
                    _logger.LogError(
                        reloadFailure,
                        "Alpaca trade-update state could not be reloaded after an ambiguous inbox write failure");
                }
                throw;
            }

            if (_committedEventIds.Contains(candidate.EventId))
                return null;
            if (!_pendingByEventId.TryGetValue(candidate.EventId, out var persistedPending))
                throw new InvalidDataException($"Alpaca event '{candidate.EventId}' disappeared during durable admission.");

            EnsureSameContent(candidate.EventId, persistedPending.ContentHash, candidate.ContentHash);
            return _queuedEventIds.Add(candidate.EventId) ? persistedPending : null;
        }
    }

    private bool TryAcknowledge(TradeUpdateEnvelope envelope)
    {
        var eventId = envelope.EventId!;
        lock (_cursorGate)
        {
            if (!_pendingByEventId.TryGetValue(eventId, out var pending))
                return false;

            EnsureSameContent(eventId, pending.ContentHash, envelope.ContentHash!);

            try
            {
                var persisted = CursorStoreLocked.UpdateState(current =>
                {
                    var validated = AlpacaTradeUpdateStateCodec.ValidateAndSnapshot(current);
                    var persistedPending = validated.PendingEnvelopes.FirstOrDefault(item =>
                        string.Equals(item.EventId, eventId, StringComparison.Ordinal));
                    if (persistedPending is null)
                    {
                        if (validated.EventIds.Contains(eventId, StringComparer.Ordinal) &&
                            validated.EventHashes.TryGetValue(eventId, out var committedHash))
                        {
                            EnsureSameContent(eventId, pending.ContentHash, committedHash);
                        }
                        return current;
                    }

                    EnsureSameContent(eventId, pending.ContentHash, persistedPending.ContentHash);
                    var watermark = validated.Watermark is null || persistedPending.Timestamp > validated.Watermark.Value
                        ? persistedPending.Timestamp
                        : validated.Watermark.Value;
                    var recentEventIds = validated.EventIds
                        .Where(id => !string.Equals(id, eventId, StringComparison.Ordinal))
                        .Append(eventId)
                        .TakeLast(AlpacaTradeUpdateStateCodec.MaxRecentEventIds)
                        .ToArray();
                    var recentHashes = new Dictionary<string, string>(StringComparer.Ordinal);
                    foreach (var recentEventId in recentEventIds)
                    {
                        if (string.Equals(recentEventId, eventId, StringComparison.Ordinal))
                            recentHashes[recentEventId] = persistedPending.ContentHash;
                        else if (validated.EventHashes.TryGetValue(recentEventId, out var hash))
                            recentHashes[recentEventId] = hash;
                    }

                    return new AlpacaTradeUpdateCursorState(
                        AlpacaTradeUpdateCursorState.CurrentVersion,
                        watermark,
                        recentEventIds,
                        recentHashes,
                        validated.PendingEnvelopes
                            .Where(item => !string.Equals(item.EventId, eventId, StringComparison.Ordinal))
                            .ToArray());
                });
                ApplyStateLocked(persisted, preserveQueuedDeliveries: true);
            }
            catch (Exception ex)
            {
                _failure = $"Alpaca trade-update acknowledgement save failed: {ex.Message}";
                _logger.LogWarning(
                    ex,
                    "Alpaca trade-update {EventId} was delivered but its durable acknowledgement failed; the envelope remains pending",
                    eventId);

                // Atomic replacement can report a late durability error after the rename has
                // committed. Reload before deciding whether this event is still pending, so the
                // process-local view never overwrites an uncertain on-disk outcome.
                if (TryReloadStateAfterWriteFailureLocked(out var reloadFailure))
                {
                    if (_committedEventIds.Contains(eventId))
                    {
                        if (_committedEventHashes.TryGetValue(eventId, out var committedHash))
                            EnsureSameContent(eventId, pending.ContentHash, committedHash);
                        _failure = null;
                        return true;
                    }

                    if (_pendingByEventId.TryGetValue(eventId, out var reloadedPending))
                        EnsureSameContent(eventId, pending.ContentHash, reloadedPending.ContentHash);
                }
                else if (reloadFailure is not null)
                {
                    _logger.LogError(
                        reloadFailure,
                        "Alpaca trade-update state could not be reloaded after an ambiguous acknowledgement write failure");
                }
                return false;
            }

            if (!_committedEventIds.Contains(eventId))
                return false;
            if (_committedEventHashes.TryGetValue(eventId, out var persistedHash))
                EnsureSameContent(eventId, pending.ContentHash, persistedHash);
            _failure = null;
            return true;
        }
    }

    private void QueueDurablePendingForDelivery()
    {
        lock (_cursorGate)
        {
            EnsureStateInitializedLocked();
        }

        while (true)
        {
            AlpacaTradeUpdatePendingEnvelope? pending;
            lock (_cursorGate)
            {
                pending = _pendingByEventId.Values.FirstOrDefault(item => !_queuedEventIds.Contains(item.EventId));
                if (pending is null)
                    return;
                _queuedEventIds.Add(pending.EventId);
            }

            if (_reports.Writer.TryWrite(ToDeliveryEnvelope(pending)))
                continue;

            ReleaseDelivery(pending.EventId);
            return;
        }
    }

    private void EnsureStateInitializedLocked()
    {
        if (_stateInitialized)
            return;

        var state = AlpacaTradeUpdateStateCodec.ValidateAndSnapshot(CursorStoreLocked.LoadState());
        ApplyStateLocked(state, preserveQueuedDeliveries: false);
    }

    private void ApplyStateLocked(
        AlpacaTradeUpdateCursorState state,
        bool preserveQueuedDeliveries)
    {
        var previouslyQueued = preserveQueuedDeliveries
            ? _queuedEventIds.ToArray()
            : Array.Empty<string>();
        _watermark = state.Watermark;
        _committedEventIds.Clear();
        _committedEventHashes.Clear();
        _committedEventOrder.Clear();
        _pendingByEventId.Clear();
        _queuedEventIds.Clear();

        foreach (var eventId in state.EventIds)
        {
            state.EventHashes.TryGetValue(eventId, out var hash);
            RememberCommitted(eventId, hash);
        }

        foreach (var pending in state.PendingEnvelopes)
            _pendingByEventId.Add(pending.EventId, pending);
        foreach (var eventId in previouslyQueued)
        {
            if (_pendingByEventId.ContainsKey(eventId))
                _queuedEventIds.Add(eventId);
        }

        _stateInitialized = true;
    }

    private bool TryReloadStateAfterWriteFailureLocked(out Exception? reloadFailure)
    {
        _stateInitialized = false;
        try
        {
            var reloaded = AlpacaTradeUpdateStateCodec.ValidateAndSnapshot(CursorStoreLocked.LoadState());
            ApplyStateLocked(reloaded, preserveQueuedDeliveries: true);
            reloadFailure = null;
            return true;
        }
        catch (Exception ex)
        {
            reloadFailure = ex;
            return false;
        }
    }

    private IAlpacaTradeUpdateCursorStore CursorStoreLocked => _cursorStore ?? throw new InvalidOperationException(
        "The default Alpaca trade-update inbox must be scoped to an authenticated provider account before use.");

    private string RequireStateIdentityScope() => _stateIdentityScope ?? throw new InvalidOperationException(
        "Alpaca content-derived event identity requires an authenticated provider account and trading environment scope.");

    private void ReleaseDelivery(string eventId)
    {
        lock (_cursorGate)
            _queuedEventIds.Remove(eventId);
    }

    private void RememberCommitted(string eventId, string? contentHash)
    {
        if (!_committedEventIds.Add(eventId))
        {
            if (contentHash is not null)
                _committedEventHashes[eventId] = contentHash;
            return;
        }

        _committedEventOrder.Enqueue(eventId);
        if (contentHash is not null)
            _committedEventHashes[eventId] = contentHash;

        if (_committedEventOrder.Count <= AlpacaTradeUpdateStateCodec.MaxRecentEventIds)
            return;

        var evicted = _committedEventOrder.Dequeue();
        _committedEventIds.Remove(evicted);
        _committedEventHashes.Remove(evicted);
    }

    private static void EnsureSameContent(string eventId, string expectedHash, string actualHash)
    {
        if (!string.Equals(expectedHash, actualHash, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Alpaca event ID '{eventId}' was reused with conflicting normalized execution content.");
        }
    }

    private static TradeUpdateEnvelope ToDeliveryEnvelope(AlpacaTradeUpdatePendingEnvelope pending) =>
        new(pending.Report, pending.EventId, pending.ContentHash, pending.Timestamp);

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) ? value.GetString() : null;

    private static decimal ReadDecimal(JsonElement element, string name) =>
        ReadNullableDecimal(element, name) ?? 0m;

    private static decimal? ReadNullableDecimal(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) &&
        decimal.TryParse(
            value.GetString(),
            System.Globalization.NumberStyles.Number,
            System.Globalization.CultureInfo.InvariantCulture,
            out var parsed)
            ? parsed
            : null;

    private static DateTimeOffset? ReadTime(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && DateTimeOffset.TryParse(value.GetString(), out var parsed)
            ? parsed
            : null;

    private static (OrderStatus status, ExecutionReportType type) Map(string? status) =>
        status?.ToLowerInvariant() switch
        {
            "filled" => (OrderStatus.Filled, ExecutionReportType.Fill),
            "partially_filled" => (OrderStatus.PartiallyFilled, ExecutionReportType.PartialFill),
            "canceled" => (OrderStatus.Cancelled, ExecutionReportType.Cancelled),
            "rejected" => (OrderStatus.Rejected, ExecutionReportType.Rejected),
            "expired" => (OrderStatus.Expired, ExecutionReportType.Expired),
            _ => (OrderStatus.Accepted, ExecutionReportType.New)
        };

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
            return;

        try
        {
            if (_runCts is not null)
            {
                await _runCts.CancelAsync().ConfigureAwait(false);
                if (_runTask is not null)
                    await _runTask.ConfigureAwait(false);
                _runCts.Dispose();
            }
        }
        finally
        {
            _reports.Writer.TryComplete();
        }
    }

    private sealed record TradeUpdateEnvelope(
        ExecutionReport Report,
        string? EventId,
        string? ContentHash,
        DateTimeOffset Timestamp);
}

/// <summary>A normalized, credential-free broker event retained until downstream acknowledgement.</summary>
public sealed record AlpacaTradeUpdatePendingEnvelope(
    string EventId,
    string ContentHash,
    ExecutionReport Report,
    DateTimeOffset Timestamp);

/// <summary>Provider-identified REST reconciliation report admitted through the durable inbox.</summary>
internal sealed record AlpacaReconciliationReport(
    string Source,
    string? ProviderEventId,
    ExecutionReport Report);

/// <summary>Versioned durable trade-update inbox, acknowledgement cursor, and deduplication window.</summary>
public sealed record AlpacaTradeUpdateCursorState(
    int Version,
    DateTimeOffset? Watermark,
    IReadOnlyList<string> EventIds,
    IReadOnlyDictionary<string, string> EventHashes,
    IReadOnlyList<AlpacaTradeUpdatePendingEnvelope> PendingEnvelopes)
{
    public const int CurrentVersion = 2;

    public static AlpacaTradeUpdateCursorState Empty => new(
        CurrentVersion,
        null,
        [],
        new Dictionary<string, string>(StringComparer.Ordinal),
        []);
}

/// <summary>
/// Durable execution-state seam. Implementations must never persist credentials or raw broker payloads.
/// </summary>
public interface IAlpacaTradeUpdateCursorStore
{
    DateTimeOffset? Load();

    IReadOnlyList<string> LoadRecentEventIds() => [];

    void Save(DateTimeOffset watermark, IReadOnlyCollection<string> recentEventIds);

    /// <summary>
    /// Loads the versioned inbox state. The default preserves compatibility with cursor-only stores.
    /// </summary>
    AlpacaTradeUpdateCursorState LoadState() => new(
        AlpacaTradeUpdateCursorState.CurrentVersion,
        Load(),
        LoadRecentEventIds(),
        new Dictionary<string, string>(StringComparer.Ordinal),
        []);

    /// <summary>
    /// Atomically replaces inbox and cursor state. Legacy implementations fail closed when the
    /// transition requires normalized pending envelopes or content hashes.
    /// </summary>
    void SaveState(AlpacaTradeUpdateCursorState state)
    {
        var snapshot = AlpacaTradeUpdateStateCodec.ValidateAndSnapshot(state);
        if (snapshot.PendingEnvelopes.Count != 0 || snapshot.EventHashes.Count != 0)
        {
            throw new NotSupportedException(
                "This Alpaca cursor store must implement SaveState to support durable normalized envelopes.");
        }

        if (snapshot.Watermark is { } watermark)
        {
            Save(watermark, snapshot.EventIds);
            return;
        }

        if (snapshot.EventIds.Count != 0)
            throw new InvalidDataException("An Alpaca cursor cannot contain event IDs without a watermark.");
    }

    /// <summary>
    /// Applies a read-modify-write transition. File-backed implementations override this method so
    /// the read and atomic replacement share one process-local and cross-process writer lease.
    /// </summary>
    AlpacaTradeUpdateCursorState UpdateState(
        Func<AlpacaTradeUpdateCursorState, AlpacaTradeUpdateCursorState> transition)
    {
        ArgumentNullException.ThrowIfNull(transition);
        var current = AlpacaTradeUpdateStateCodec.ValidateAndSnapshot(LoadState());
        var candidate = transition(current)
            ?? throw new InvalidDataException("An Alpaca state transition cannot return null.");
        if (ReferenceEquals(candidate, current))
            return current;

        var snapshot = AlpacaTradeUpdateStateCodec.ValidateAndSnapshot(candidate);
        SaveState(snapshot);
        return snapshot;
    }
}

/// <summary>
/// Atomic local store used by the default execution-stream client. Writers sharing a path are
/// serialized in-process and through a lock file before each read-modify-replace transition.
/// </summary>
public sealed class FileAlpacaTradeUpdateCursorStore : IAlpacaTradeUpdateCursorStore
{
    private const string StateFileExtension = ".cursor";
    private static readonly TimeSpan WriterLeaseTimeout = TimeSpan.FromSeconds(30);
    private static readonly ConcurrentDictionary<string, object> PathGates = new(
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    private readonly object _gate;
    private readonly RootedPathGuard _pathGuard;
    private readonly string _path;
    private readonly string _lockPath;

    public FileAlpacaTradeUpdateCursorStore(string? path = null)
    {
        if (path is null)
        {
            throw new ArgumentException(
                "An explicit path is required, or construct the store with provider account and trading environment scope.",
                nameof(path));
        }

        var fullPath = Path.GetFullPath(path);
        var root = Path.GetDirectoryName(fullPath)
            ?? throw new ArgumentException("The Alpaca cursor path must name a file.", nameof(path));
        _pathGuard = new RootedPathGuard(root);
        _path = _pathGuard.ResolvePath(Path.GetFileName(fullPath));

        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!string.Equals(fullPath, _path, comparison))
            throw new InvalidOperationException("The Alpaca cursor path escaped its configured root.");

        _lockPath = _path + ".lock";
        _pathGuard.EnsurePath(_lockPath);
        _gate = PathGates.GetOrAdd(_path, static _ => new object());
    }

    /// <summary>Creates the default credential-free path scoped by paper/live and account hash.</summary>
    public FileAlpacaTradeUpdateCursorStore(
        string providerAccountId,
        string tradingEnvironment,
        string? stateRoot = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerAccountId);
        ArgumentException.ThrowIfNullOrWhiteSpace(tradingEnvironment);

        var environment = NormalizeEnvironment(tradingEnvironment);

        var root = stateRoot ?? Path.Combine(
            AppContext.BaseDirectory,
            "state",
            "alpaca",
            "trade-updates");
        _pathGuard = new RootedPathGuard(root);
        var accountHash = ComputeAccountHash(providerAccountId);
        _path = _pathGuard.ResolvePath(environment, $"account-{accountHash}{StateFileExtension}");
        _lockPath = _path + ".lock";
        _pathGuard.EnsurePath(_lockPath);
        _gate = PathGates.GetOrAdd(_path, static _ => new object());
    }

    internal string StatePath => _path;

    internal static string CreateIdentityScope(string providerAccountId, string tradingEnvironment) =>
        $"{NormalizeEnvironment(tradingEnvironment)}:{ComputeAccountHash(providerAccountId)}";

    public DateTimeOffset? Load() => LoadState().Watermark;

    public IReadOnlyList<string> LoadRecentEventIds() => LoadState().EventIds;

    public AlpacaTradeUpdateCursorState LoadState()
    {
        lock (_gate)
        {
            using var lease = AcquireWriterLeaseLocked();
            return ReadStateLocked();
        }
    }

    public void Save(DateTimeOffset watermark, IReadOnlyCollection<string> recentEventIds)
    {
        ArgumentNullException.ThrowIfNull(recentEventIds);
        _ = UpdateState(current =>
        {
            var ids = recentEventIds
                .TakeLast(AlpacaTradeUpdateStateCodec.MaxRecentEventIds)
                .ToArray();
            var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var eventId in ids)
            {
                if (current.EventHashes.TryGetValue(eventId, out var hash))
                    hashes[eventId] = hash;
            }

            return new AlpacaTradeUpdateCursorState(
                AlpacaTradeUpdateCursorState.CurrentVersion,
                watermark,
                ids,
                hashes,
                current.PendingEnvelopes);
        });
    }

    public void SaveState(AlpacaTradeUpdateCursorState state)
    {
        lock (_gate)
        {
            using var lease = AcquireWriterLeaseLocked();
            WriteStateLocked(state);
        }
    }

    public AlpacaTradeUpdateCursorState UpdateState(
        Func<AlpacaTradeUpdateCursorState, AlpacaTradeUpdateCursorState> transition)
    {
        ArgumentNullException.ThrowIfNull(transition);
        lock (_gate)
        {
            using var lease = AcquireWriterLeaseLocked();
            var current = ReadStateLocked();
            var candidate = transition(current)
                ?? throw new InvalidDataException("An Alpaca state transition cannot return null.");
            if (ReferenceEquals(candidate, current))
                return current;

            var snapshot = AlpacaTradeUpdateStateCodec.ValidateAndSnapshot(candidate);
            WriteStateLocked(snapshot);
            return snapshot;
        }
    }

    private AlpacaTradeUpdateCursorState ReadStateLocked()
    {
        _pathGuard.EnsurePath(_path);
        if (!File.Exists(_path))
            return AlpacaTradeUpdateCursorState.Empty;

        return AlpacaTradeUpdateStateCodec.Deserialize(File.ReadAllText(_path));
    }

    private void WriteStateLocked(AlpacaTradeUpdateCursorState state)
    {
        var serialized = AlpacaTradeUpdateStateCodec.Serialize(state);
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        _pathGuard.EnsurePath(_path);
        AtomicFileWriter.Write(_path, serialized);
    }

    private FileStream AcquireWriterLeaseLocked()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_lockPath)!);
        _pathGuard.EnsurePath(_lockPath);
        var stopwatch = Stopwatch.StartNew();
        IOException? lastFailure = null;
        while (stopwatch.Elapsed < WriterLeaseTimeout)
        {
            try
            {
                return new FileStream(
                    _lockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None);
            }
            catch (IOException ex)
            {
                lastFailure = ex;
                Thread.Sleep(25);
            }
        }

        throw new IOException(
            $"Timed out acquiring the Alpaca trade-update state lease for '{_path}'.",
            lastFailure);
    }

    private static string NormalizeEnvironment(string tradingEnvironment)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tradingEnvironment);
        var environment = tradingEnvironment.Trim().ToLowerInvariant();
        if (environment is AlpacaCredentialEnvironment.PaperEnvironment or
            AlpacaCredentialEnvironment.LiveEnvironment)
        {
            return environment;
        }

        throw new ArgumentException(
            "The Alpaca trading environment must be either 'paper' or 'live'.",
            nameof(tradingEnvironment));
    }

    private static string ComputeAccountHash(string providerAccountId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerAccountId);
        return Sha256Digest.ComputeUtf8(providerAccountId.Trim());
    }
}

internal static class AlpacaTradeUpdateStateCodec
{
    internal const int MaxRecentEventIds = 8192;
    internal const int MaxPendingEnvelopes = 4096;
    internal const int MaxSerializedStateBytes = 16 * 1024 * 1024;
    private const int MaxEventIdCharacters = 512;
    private const int Sha256HexCharacters = 64;

    // Deliberately NOT routed through Sha256Digest (which lowercases): this hash — and the
    // providerIdHash below — is embedded in stable event IDs persisted in the trade-update
    // cursor state; changing the casing would change every event ID and re-deliver
    // previously-seen fills as new events (#2691).
    internal static string ComputeContentHash(ExecutionReport report, DateTimeOffset timestamp)
    {
        ArgumentNullException.ThrowIfNull(report);
        // The per-event quantity is derived from the same broker event as everything else here,
        // so it adds nothing to identity -- and keeping it out means an envelope persisted before
        // the field existed still matches a redelivery of the same event rather than failing
        // closed as conflicting content.
        var content = JsonSerializer.SerializeToUtf8Bytes(
            new AlpacaTradeUpdateHashMaterial(report with { LastFillQuantity = null }, timestamp),
            AlpacaTradeUpdateStateJsonContext.Default.AlpacaTradeUpdateHashMaterial);
        return Convert.ToHexString(SHA256.HashData(content));
    }

    internal static string CreateStableEventId(
        string identityScope,
        string source,
        string contentHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identityScope);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ValidateHash(contentHash, source);
        return $"alpaca:{identityScope}:{source}:{contentHash}";
    }

    internal static string CreateProviderEventId(
        string identityScope,
        string source,
        string providerEventId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerEventId);
        var providerIdHash = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(providerEventId.Trim())));
        return CreateStableEventId(identityScope, source, providerIdHash);
    }

    internal static AlpacaTradeUpdateCursorState ValidateAndSnapshot(AlpacaTradeUpdateCursorState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.Version != AlpacaTradeUpdateCursorState.CurrentVersion)
        {
            throw new InvalidDataException(
                $"Unsupported Alpaca trade-update state version {state.Version}.");
        }

        var eventIds = state.EventIds?.ToArray()
            ?? throw new InvalidDataException("Alpaca trade-update EventIds cannot be null.");
        var eventHashes = state.EventHashes is null
            ? throw new InvalidDataException("Alpaca trade-update EventHashes cannot be null.")
            : state.EventHashes.ToDictionary(
                pair => pair.Key,
                pair => pair.Value,
                StringComparer.Ordinal);
        var pending = state.PendingEnvelopes?.ToArray()
            ?? throw new InvalidDataException("Alpaca trade-update PendingEnvelopes cannot be null.");

        if (eventIds.Length > MaxRecentEventIds)
            throw new InvalidDataException($"Alpaca trade-update state exceeds {MaxRecentEventIds} recent event IDs.");
        if (pending.Length > MaxPendingEnvelopes)
            throw new InvalidDataException($"Alpaca trade-update state exceeds {MaxPendingEnvelopes} pending envelopes.");
        if (eventIds.Length != 0 && state.Watermark is null)
            throw new InvalidDataException("Committed Alpaca event IDs require an acknowledgement watermark.");

        var committedIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var eventId in eventIds)
        {
            ValidateEventId(eventId);
            if (!committedIds.Add(eventId))
                throw new InvalidDataException($"Duplicate committed Alpaca event ID '{eventId}'.");
        }

        foreach (var pair in eventHashes)
        {
            if (!committedIds.Contains(pair.Key))
                throw new InvalidDataException($"Alpaca content hash has no committed event ID '{pair.Key}'.");
            ValidateHash(pair.Value, pair.Key);
        }

        var pendingIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var envelope in pending)
        {
            if (envelope is null)
                throw new InvalidDataException("Alpaca trade-update pending envelopes cannot contain null entries.");
            ValidateEventId(envelope.EventId);
            if (!pendingIds.Add(envelope.EventId))
                throw new InvalidDataException($"Duplicate pending Alpaca event ID '{envelope.EventId}'.");
            if (committedIds.Contains(envelope.EventId))
                throw new InvalidDataException($"Alpaca event ID '{envelope.EventId}' is both pending and committed.");
            if (envelope.Report is null)
                throw new InvalidDataException($"Pending Alpaca event '{envelope.EventId}' has no normalized report.");
            if (string.IsNullOrWhiteSpace(envelope.Report.OrderId) ||
                envelope.Report.OrderId.Length > MaxEventIdCharacters ||
                string.IsNullOrWhiteSpace(envelope.Report.Symbol) ||
                !Enum.IsDefined(envelope.Report.Side) ||
                !Enum.IsDefined(envelope.Report.OrderStatus) ||
                !Enum.IsDefined(envelope.Report.ReportType))
            {
                throw new InvalidDataException(
                    $"Pending Alpaca event '{envelope.EventId}' has an invalid normalized execution report.");
            }
            if (envelope.Report.Timestamp != envelope.Timestamp)
                throw new InvalidDataException($"Pending Alpaca event '{envelope.EventId}' has inconsistent timestamps.");

            ValidateHash(envelope.ContentHash, envelope.EventId);
            var computedHash = ComputeContentHash(envelope.Report, envelope.Timestamp);
            if (!string.Equals(computedHash, envelope.ContentHash, StringComparison.Ordinal))
                throw new InvalidDataException($"Pending Alpaca event '{envelope.EventId}' failed its content-hash check.");
        }

        var orderedHashes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var eventId in eventIds)
        {
            if (eventHashes.TryGetValue(eventId, out var hash))
                orderedHashes[eventId] = hash;
        }

        var snapshot = new AlpacaTradeUpdateCursorState(
            AlpacaTradeUpdateCursorState.CurrentVersion,
            state.Watermark,
            eventIds,
            orderedHashes,
            pending);
        var byteCount = Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(
            snapshot,
            AlpacaTradeUpdateStateJsonContext.Default.AlpacaTradeUpdateCursorState));
        if (byteCount > MaxSerializedStateBytes)
        {
            throw new InvalidDataException(
                $"Alpaca trade-update state exceeds {MaxSerializedStateBytes} serialized bytes.");
        }

        return snapshot;
    }

    internal static string Serialize(AlpacaTradeUpdateCursorState state)
    {
        var snapshot = ValidateAndSnapshot(state);
        var serialized = JsonSerializer.Serialize(
            snapshot,
            AlpacaTradeUpdateStateJsonContext.Default.AlpacaTradeUpdateCursorState);
        if (Encoding.UTF8.GetByteCount(serialized) > MaxSerializedStateBytes)
        {
            throw new InvalidDataException(
                $"Alpaca trade-update state exceeds {MaxSerializedStateBytes} serialized bytes.");
        }
        return serialized;
    }

    internal static AlpacaTradeUpdateCursorState Deserialize(string serialized)
    {
        ArgumentNullException.ThrowIfNull(serialized);
        if (Encoding.UTF8.GetByteCount(serialized) > MaxSerializedStateBytes)
        {
            throw new InvalidDataException(
                $"Alpaca trade-update state exceeds {MaxSerializedStateBytes} serialized bytes.");
        }

        try
        {
            using var document = JsonDocument.Parse(serialized);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("Alpaca trade-update state must be a JSON object.");

            var hasVersion = document.RootElement.EnumerateObject()
                .Any(property => string.Equals(property.Name, "Version", StringComparison.OrdinalIgnoreCase));
            var hasEventIds = document.RootElement.EnumerateObject()
                .Any(property => string.Equals(property.Name, "EventIds", StringComparison.OrdinalIgnoreCase));
            var hasEventHashes = document.RootElement.EnumerateObject()
                .Any(property => string.Equals(property.Name, "EventHashes", StringComparison.OrdinalIgnoreCase));
            var hasPending = document.RootElement.EnumerateObject()
                .Any(property => string.Equals(property.Name, "PendingEnvelopes", StringComparison.OrdinalIgnoreCase));
            if (!hasVersion && (hasEventHashes || hasPending))
                throw new InvalidDataException("Versioned Alpaca state is missing its Version field.");
            if (hasVersion && (!hasEventIds || !hasEventHashes || !hasPending))
                throw new InvalidDataException("Versioned Alpaca state is missing required fields.");

            var persisted = JsonSerializer.Deserialize(
                    serialized,
                    AlpacaTradeUpdateStateJsonContext.Default.AlpacaTradeUpdatePersistedState)
                ?? throw new InvalidDataException("Alpaca trade-update state was empty.");
            var state = new AlpacaTradeUpdateCursorState(
                persisted.Version ?? AlpacaTradeUpdateCursorState.CurrentVersion,
                persisted.Watermark,
                persisted.EventIds ?? [],
                persisted.EventHashes ?? new Dictionary<string, string>(StringComparer.Ordinal),
                persisted.PendingEnvelopes ?? []);
            return ValidateAndSnapshot(state);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Alpaca trade-update state is corrupt JSON.", ex);
        }
        catch (NotSupportedException ex)
        {
            throw new InvalidDataException("Alpaca trade-update state contains unsupported JSON.", ex);
        }
    }

    private static void ValidateEventId(string eventId)
    {
        if (string.IsNullOrWhiteSpace(eventId) ||
            eventId.Length > MaxEventIdCharacters ||
            eventId.Any(char.IsControl) ||
            !string.Equals(eventId, eventId.Trim(), StringComparison.Ordinal))
        {
            throw new InvalidDataException("Alpaca event IDs must be non-empty, bounded, and normalized.");
        }
    }

    private static void ValidateHash(string hash, string eventId)
    {
        if (hash is null ||
            hash.Length != Sha256HexCharacters ||
            hash.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new InvalidDataException($"Alpaca event '{eventId}' has an invalid SHA-256 content hash.");
        }
    }

}

internal sealed record AlpacaTradeUpdateHashMaterial(
    ExecutionReport Report,
    DateTimeOffset Timestamp);

internal sealed class AlpacaTradeUpdatePersistedState
{
    public int? Version { get; init; }
    public DateTimeOffset? Watermark { get; init; }
    public IReadOnlyList<string>? EventIds { get; init; }
    public IReadOnlyDictionary<string, string>? EventHashes { get; init; }
    public IReadOnlyList<AlpacaTradeUpdatePendingEnvelope>? PendingEnvelopes { get; init; }
}

[JsonSerializable(typeof(AlpacaTradeUpdateCursorState))]
[JsonSerializable(typeof(AlpacaTradeUpdatePersistedState))]
[JsonSerializable(typeof(AlpacaTradeUpdateHashMaterial))]
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
internal sealed partial class AlpacaTradeUpdateStateJsonContext : JsonSerializerContext;
