using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Meridian.ProviderSdk;

namespace Meridian.Infrastructure.Adapters.InteractiveBrokers;

/// <summary>
/// Broadcasts each IB read-model update to every matching subscriber. A dedicated bounded channel
/// per subscriber avoids the competing-reader behavior of a shared channel and keeps tenant/company
/// filtering at the publication boundary.
/// </summary>
internal sealed class TenantScopedProviderDataUpdateHub
{
    private readonly object _gate = new();
    private readonly Dictionary<long, Subscription> _subscriptions = [];
    private long _nextSubscriptionId;
    private bool _completed;

    public IAsyncEnumerable<ProviderDataRequestReadModel> WatchAllAsync(CancellationToken cancellationToken)
        => WatchCoreAsync(scope: null, requireUnowned: false, cancellationToken);

    public IAsyncEnumerable<ProviderDataRequestReadModel> WatchUnownedAsync(
        CancellationToken cancellationToken)
        => WatchCoreAsync(scope: null, requireUnowned: true, cancellationToken);

    public IAsyncEnumerable<ProviderDataRequestReadModel> WatchAsync(
        IBDataRequestOwnership ownership,
        CancellationToken cancellationToken)
        => WatchCoreAsync(
            IBDataRequestOwnership.Require(ownership),
            requireUnowned: false,
            cancellationToken);

    public void Publish(IBDataRequestOwnership? ownership, ProviderDataRequestReadModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        Subscription[] subscriptions;
        lock (_gate)
        {
            if (_completed)
            {
                return;
            }

            subscriptions = [.. _subscriptions.Values];
        }

        foreach (var subscription in subscriptions)
        {
            if (Matches(subscription.Scope, subscription.RequireUnowned, ownership))
            {
                subscription.Channel.Writer.TryWrite(model);
            }
        }
    }

    public void Complete()
    {
        Subscription[] subscriptions;
        lock (_gate)
        {
            if (_completed)
            {
                return;
            }

            _completed = true;
            subscriptions = [.. _subscriptions.Values];
            _subscriptions.Clear();
        }

        foreach (var subscription in subscriptions)
        {
            subscription.Channel.Writer.TryComplete();
        }
    }

    private async IAsyncEnumerable<ProviderDataRequestReadModel> WatchCoreAsync(
        IBDataRequestOwnership? scope,
        bool requireUnowned,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var channel = Channel.CreateBounded<ProviderDataRequestReadModel>(
            new BoundedChannelOptions(256)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.DropOldest
            });
        var subscriptionId = Interlocked.Increment(ref _nextSubscriptionId);
        lock (_gate)
        {
            if (_completed)
            {
                channel.Writer.TryComplete();
            }
            else
            {
                _subscriptions.Add(
                    subscriptionId,
                    new Subscription(scope, requireUnowned, channel));
            }
        }

        try
        {
            await foreach (var update in channel.Reader
                               .ReadAllAsync(cancellationToken)
                               .ConfigureAwait(false))
            {
                yield return update;
            }
        }
        finally
        {
            lock (_gate)
            {
                _subscriptions.Remove(subscriptionId);
            }

            channel.Writer.TryComplete();
        }
    }

    private static bool Matches(
        IBDataRequestOwnership? requestedScope,
        bool requireUnowned,
        IBDataRequestOwnership? updateScope)
        => requireUnowned
            ? updateScope is null
            : requestedScope is null ||
           (updateScope is not null &&
            string.Equals(requestedScope.TenantId, updateScope.TenantId, StringComparison.Ordinal) &&
            string.Equals(requestedScope.CompanyId, updateScope.CompanyId, StringComparison.Ordinal));

    private sealed record Subscription(
        IBDataRequestOwnership? Scope,
        bool RequireUnowned,
        Channel<ProviderDataRequestReadModel> Channel);
}
