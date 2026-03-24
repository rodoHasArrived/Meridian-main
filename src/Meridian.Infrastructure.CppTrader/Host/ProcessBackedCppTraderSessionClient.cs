using Meridian.Infrastructure.CppTrader.Options;
using Meridian.Infrastructure.CppTrader.Protocol;

namespace Meridian.Infrastructure.CppTrader.Host;

internal sealed class ProcessBackedCppTraderSessionClient : ICppTraderSessionClient
{
    private readonly Process _process;
    private readonly LengthPrefixedProtocolStream _protocolStream;
    private readonly Channel<CppTraderEnvelope> _events;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<CppTraderEnvelope>> _pending = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _readerCts = new();
    private readonly Task _readerTask;
    private readonly ILogger _logger;
    private readonly string _hostId;

    public string SessionId { get; }

    public CppTraderSessionKind SessionKind { get; }

    public ProcessBackedCppTraderSessionClient(
        Process process,
        string sessionId,
        CppTraderSessionKind sessionKind,
        ILogger logger)
    {
        _process = process;
        _logger = logger;
        SessionId = sessionId;
        SessionKind = sessionKind;
        _hostId = $"{Environment.MachineName}:{Environment.ProcessId}";
        _events = Channel.CreateUnbounded<CppTraderEnvelope>();

        var input = process.StandardOutput.BaseStream;
        var output = process.StandardInput.BaseStream;
        _protocolStream = new LengthPrefixedProtocolStream(input, output);
        _readerTask = ReaderLoopAsync(_readerCts.Token);
    }

    public Task<RegisterSymbolResponse> RegisterSymbolAsync(RegisterSymbolRequest request, CancellationToken ct = default) =>
        SendRequestAsync(request, CppTraderProtocolNames.RegisterSymbol, CppTraderProtocolNames.RegisterSymbolResponse, CppTraderJsonContext.Default.RegisterSymbolResponse, ct);

    public Task<SubmitOrderResponse> SubmitOrderAsync(SubmitOrderRequest request, CancellationToken ct = default) =>
        SendRequestAsync(request, CppTraderProtocolNames.SubmitOrder, CppTraderProtocolNames.SubmitOrderResponse, CppTraderJsonContext.Default.SubmitOrderResponse, ct);

    public Task<CancelOrderResponse> CancelOrderAsync(CancelOrderRequest request, CancellationToken ct = default) =>
        SendRequestAsync(request, CppTraderProtocolNames.CancelOrder, CppTraderProtocolNames.CancelOrderResponse, CppTraderJsonContext.Default.CancelOrderResponse, ct);

    public Task<GetSnapshotResponse> GetSnapshotAsync(GetSnapshotRequest request, CancellationToken ct = default) =>
        SendRequestAsync(request, CppTraderProtocolNames.GetSnapshot, CppTraderProtocolNames.GetSnapshotResponse, CppTraderJsonContext.Default.GetSnapshotResponse, ct);

    public Task<HeartbeatResponse> HeartbeatAsync(CancellationToken ct = default) =>
        SendRequestAsync(new HeartbeatRequest(_hostId), CppTraderProtocolNames.Heartbeat, CppTraderProtocolNames.HeartbeatResponse, CppTraderJsonContext.Default.HeartbeatResponse, ct);

    public IAsyncEnumerable<CppTraderEnvelope> ReadEventsAsync(CancellationToken ct = default) =>
        _events.Reader.ReadAllAsync(ct);

    private async Task<TResponse> SendRequestAsync<TRequest, TResponse>(
        TRequest request,
        string requestType,
        string responseType,
        JsonTypeInfo<TResponse> responseTypeInfo,
        CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_readerCts.IsCancellationRequested, this);

        var requestId = Guid.NewGuid().ToString("N");
        var tcs = new TaskCompletionSource<CppTraderEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(requestId, tcs))
            throw new InvalidOperationException($"Duplicate request id '{requestId}'.");

        try
        {
            await _protocolStream.WriteAsync(requestType, requestId, SessionId, request, ct).ConfigureAwait(false);
            using var reg = ct.Register(() => tcs.TrySetCanceled(ct));
            var envelope = await tcs.Task.ConfigureAwait(false);
            if (!string.Equals(envelope.MessageType, responseType, StringComparison.Ordinal))
                throw new InvalidDataException($"Expected '{responseType}' but received '{envelope.MessageType}'.");

            return envelope.Payload.Deserialize(responseTypeInfo)
                ?? throw new InvalidDataException($"Response payload for '{responseType}' was empty.");
        }
        finally
        {
            _pending.TryRemove(requestId, out _);
        }
    }

    private async Task ReaderLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var envelope = await _protocolStream.ReadAsync(ct).ConfigureAwait(false);
                if (envelope is null)
                    break;

                if (envelope.RequestId is { Length: > 0 } requestId &&
                    _pending.TryGetValue(requestId, out var waiter))
                {
                    waiter.TrySetResult(envelope);
                    continue;
                }

                await _events.Writer.WriteAsync(envelope, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CppTrader protocol reader terminated unexpectedly for session {SessionId}", SessionId);
            foreach (var (_, waiter) in _pending)
                waiter.TrySetException(ex);
        }
        finally
        {
            _events.Writer.TryComplete();
            foreach (var (_, waiter) in _pending)
                waiter.TrySetException(new ObjectDisposedException(nameof(ProcessBackedCppTraderSessionClient)));
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _readerCts.CancelAsync().ConfigureAwait(false);

        try
        {
            await _readerTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        if (!_process.HasExited)
        {
            _process.Kill(entireProcessTree: true);
            await _process.WaitForExitAsync().ConfigureAwait(false);
        }

        _process.Dispose();
        _readerCts.Dispose();
    }
}
