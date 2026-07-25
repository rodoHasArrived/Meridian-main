using System.Text.Json;
using System.Threading.Channels;
using Meridian.ProviderSdk;

namespace Meridian.Infrastructure.Adapters.InteractiveBrokers;

/// <summary>
/// Composition-boundary reader that durably materializes IB updates before exposing them to
/// operator readers. Register this, rather than the transport-facing service, as the read seam.
/// </summary>
public sealed class IBDataResultMaterializer : IProviderDataReadService
{
    private readonly IProviderDataReadService _source;
    private readonly IIBDataResultStore _store;
    private readonly Channel<ProviderDataRequestReadModel> _published = Channel.CreateUnbounded<ProviderDataRequestReadModel>();

    public IBDataResultMaterializer(IProviderDataReadService source, IIBDataResultStore store)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public IReadOnlyList<ProviderDataRequestReadModel> GetRequests() => _source.GetRequests();
    public IAsyncEnumerable<ProviderDataRequestReadModel> WatchAsync(CancellationToken cancellationToken = default) => _published.Reader.ReadAllAsync(cancellationToken);

    public async Task MaterializeAsync(CancellationToken cancellationToken = default)
    {
        await foreach (var update in _source.WatchAsync(cancellationToken).ConfigureAwait(false))
        {
            var lineage = update.Lineage ?? throw new InvalidOperationException($"IB update {update.RequestId} was missing lineage.");
            var capturedAt = update.UpdatedAt;
            var requestIdentity = lineage.RequestId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var result = new IBDataResult(
                $"interactive-brokers:{requestIdentity}:{update.Capability}", update.ProviderFamily,
                update.Capability, requestIdentity, lineage.Subscription, lineage.Symbol, update.AccountId,
                capturedAt, update.Status, JsonSerializer.Serialize(update), lineage);
            await _store.UpsertAsync(result, cancellationToken).ConfigureAwait(false);
            await _published.Writer.WriteAsync(update, cancellationToken).ConfigureAwait(false);
        }
    }
}
