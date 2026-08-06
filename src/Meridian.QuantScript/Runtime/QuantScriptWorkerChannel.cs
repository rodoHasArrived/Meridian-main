namespace Meridian.QuantScript.Runtime;

/// <summary>
/// Serializes request/response exchanges over the two one-way anonymous pipes used by the
/// isolated worker. Serializing complete exchanges keeps concurrent script data calls from
/// interleaving frames.
/// </summary>
internal sealed class QuantScriptWorkerChannel(
    Stream inbound,
    Stream outbound,
    int maxFrameBytes) : IAsyncDisposable
{
    private readonly SemaphoreSlim _exchangeLock = new(1, 1);

    public Task<WorkerEnvelope> ReadAsync(CancellationToken ct)
        => QuantScriptWorkerProtocol.ReadAsync(inbound, maxFrameBytes, ct);

    public Task WriteAsync<T>(string kind, string correlationId, T payload, CancellationToken ct)
        => QuantScriptWorkerProtocol.WriteAsync(
            outbound,
            kind,
            correlationId,
            payload,
            maxFrameBytes,
            ct);

    public async Task<TResponse> ExchangeAsync<TRequest, TResponse>(
        string requestKind,
        string responseKind,
        TRequest request,
        CancellationToken ct)
    {
        await _exchangeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var correlationId = Guid.NewGuid().ToString("N");
            await WriteAsync(requestKind, correlationId, request, ct).ConfigureAwait(false);
            var envelope = await ReadAsync(ct).ConfigureAwait(false);
            if (!string.Equals(envelope.Kind, responseKind, StringComparison.Ordinal) ||
                !string.Equals(envelope.CorrelationId, correlationId, StringComparison.Ordinal))
            {
                throw new WorkerProtocolException(
                    $"Worker response did not match request '{correlationId}' ({requestKind}).");
            }

            return QuantScriptWorkerProtocol.ReadPayload<TResponse>(envelope);
        }
        finally
        {
            _exchangeLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _exchangeLock.Dispose();
        await inbound.DisposeAsync().ConfigureAwait(false);
        await outbound.DisposeAsync().ConfigureAwait(false);
    }
}
