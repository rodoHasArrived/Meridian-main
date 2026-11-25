using System.Collections.Concurrent;
using FluentAssertions;
using Meridian.Execution.Events;

namespace Meridian.Tests.Integration;

public sealed partial class FreshHostAccountingRehearsalTests
{
    /// <summary>
    /// Observes the real WAL acknowledgement without replacing storage, accounting or authority.
    /// PublishAsync guarantees acceptance only; this fence waits for the consumer's durable result.
    /// </summary>
    private sealed class ObservedPostingStore(ITradeFillPostingStore inner) : ITradeFillPostingStore
    {
        private readonly ConcurrentDictionary<Guid, PostingObservation> _outcomes = new();

        public string PostingScope => inner.PostingScope;
        public TradeFillPostingScopeIdentity ScopeIdentity => inner.ScopeIdentity;

        public async Task<TradeFillPostingAcceptance> AcceptAsync(TradeExecutedEvent fill, CancellationToken ct = default)
        {
            var acceptance = await inner.AcceptAsync(fill, ct);
            if (acceptance.WasAlreadyPosted)
            {
                var completed = new PostingObservation(null);
                completed.Outcome.TrySetResult(null);
                _outcomes[fill.FillId] = completed;
            }
            else if (acceptance.Posting is { } posting)
            {
                Observe(posting);
            }
            return acceptance;
        }

        public async Task<IReadOnlyList<PendingTradeFillPosting>> LoadPendingAsync(CancellationToken ct = default)
        {
            var pending = await inner.LoadPendingAsync(ct);
            foreach (var posting in pending)
                Observe(posting);
            return pending;
        }

        public async Task MarkPostedAsync(Guid fillId, CancellationToken ct = default)
        {
            var observation = _outcomes[fillId];
            await inner.MarkPostedAsync(fillId, ct);
            observation.Outcome.TrySetResult(null);
        }

        public async Task RecordFailureAsync(Guid fillId, string failure, CancellationToken ct = default)
        {
            var observation = _outcomes[fillId];
            await inner.RecordFailureAsync(fillId, failure, ct);
            observation.Outcome.TrySetResult(failure);
        }

        public async Task WaitForPostedAsync(Guid fillId, CancellationToken ct)
        {
            var observation = _outcomes[fillId];
            var receipt = observation.StoreSequence is { } sequence ? $"sequence {sequence}" : "retained posted receipt";
            string? failure;
            try
            {
                failure = await observation.Outcome.Task.WaitAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw new TimeoutException($"No durable posting acknowledgement was observed for fill '{fillId:D}' ({receipt}) within the rehearsal budget.");
            }
            failure.Should().BeNull($"fill '{fillId:D}' ({receipt}) must receive the real consumer's durable posted acknowledgement");
        }

        public ValueTask DisposeAsync() => inner.DisposeAsync();

        private void Observe(PendingTradeFillPosting posting)
            => _outcomes.AddOrUpdate(posting.TradeEvent.FillId,
                _ => new PostingObservation(posting.StoreSequence),
                (_, previous) => previous.StoreSequence == posting.StoreSequence
                    ? previous : new PostingObservation(posting.StoreSequence));

        // A new accepted sequence creates a new fence. Completion of startup replay must never
        // satisfy the wait for a later reaccepted duplicate with the same fill identity.
        private sealed record PostingObservation(long? StoreSequence)
        {
            public TaskCompletionSource<string?> Outcome { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }
}
