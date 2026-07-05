using System;
using System.Threading.Channels;
using System.Threading.Tasks;
using Meridian.Contracts.Api;

namespace Meridian.Ui.Shared.Streaming;

/// <summary>
/// A live subscription to a quote topic. Read coalesced snapshots from <see cref="Reader"/>; dispose
/// to unsubscribe and release the session's stream slot. Disposal is idempotent.
/// </summary>
public sealed class QuoteStreamSubscription : IAsyncDisposable
{
    private readonly Func<ValueTask> _onDispose;

    internal QuoteStreamSubscription(ChannelReader<QuotesSnapshotResponse> reader, Func<ValueTask> onDispose)
    {
        Reader = reader;
        _onDispose = onDispose;
    }

    /// <summary>Coalesced snapshot feed for this subscription's topic.</summary>
    public ChannelReader<QuotesSnapshotResponse> Reader { get; }

    public ValueTask DisposeAsync() => _onDispose();
}
