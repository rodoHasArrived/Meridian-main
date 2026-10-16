using System.Net.WebSockets;

namespace Meridian.Infrastructure.Resilience;

/// <summary>
/// Serializes writes for one transport generation. Queued writes retain that generation's
/// lifetime, so they cannot migrate to a replacement socket after reconnect.
/// </summary>
internal sealed class WebSocketSendSession(WebSocket socket, CancellationToken lifetimeToken)
{
    // Do not dispose the semaphore while cancellation-ignoring transport operations can
    // still complete and release it. It owns no wait handle and is collected with this session.
    private readonly SemaphoreSlim _gate = new(1, 1);

    internal async ValueTask SendAsync(
        ReadOnlyMemory<byte> payload,
        WebSocketMessageType messageType,
        CancellationToken ct)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, lifetimeToken);
        var token = linkedCts.Token;
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            token.ThrowIfCancellationRequested();
            if (socket.State != WebSocketState.Open)
                throw new WebSocketException("Cannot send because the WebSocket transport is not open.");

            await socket.SendAsync(payload, messageType, endOfMessage: true, token).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    internal async Task CloseAsync(CancellationToken ct)
    {
        // Lifetime cancellation rejects queued application writes, while the shutdown
        // token bounds waiting for the active send before writing the close frame.
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Disconnecting", ct)
                    .ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }
}
