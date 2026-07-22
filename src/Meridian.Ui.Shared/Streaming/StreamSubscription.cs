using System;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Meridian.Ui.Shared.Streaming;

/// <summary>
/// A live subscription to a stream topic produced by <see cref="StreamBroadcaster{TPayload}"/>. Read
/// coalesced payloads from <see cref="Reader"/>; dispose to unsubscribe and release the session's
/// stream slot. Disposal is idempotent.
/// </summary>
public sealed class StreamSubscription<TPayload> : IAsyncDisposable
{
    private readonly Func<ValueTask> _onDispose;

    internal StreamSubscription(ChannelReader<TPayload> reader, Func<ValueTask> onDispose)
    {
        Reader = reader;
        _onDispose = onDispose;
    }

    /// <summary>Coalesced payload feed for this subscription's topic.</summary>
    public ChannelReader<TPayload> Reader { get; }

    public ValueTask DisposeAsync() => _onDispose();
}
