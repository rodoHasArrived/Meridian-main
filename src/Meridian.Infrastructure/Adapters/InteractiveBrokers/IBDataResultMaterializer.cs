using System.Collections.Concurrent;
using System.Text.Json;
using Meridian.ProviderSdk;

namespace Meridian.Infrastructure.Adapters.InteractiveBrokers;

/// <summary>
/// Composition-boundary reader that durably materializes IB updates before exposing them to
/// operator readers. Register this, rather than the transport-facing service, as the read seam.
/// </summary>
public sealed class IBDataResultMaterializer : ITenantScopedProviderDataReadService, IDisposable
{
    private readonly ITenantScopedProviderDataReadService _source;
    private readonly IIBDataResultStore _store;
    private readonly TenantScopedProviderDataUpdateHub _published = new();
    private readonly ConcurrentDictionary<string, MaterializedUpdate> _materialized = new(StringComparer.Ordinal);

    public IBDataResultMaterializer(ITenantScopedProviderDataReadService source, IIBDataResultStore store)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <summary>
    /// Unscoped operator reads fail closed. Callers must use the tenant/company overload.
    /// </summary>
    public IReadOnlyList<ProviderDataRequestReadModel> GetRequests() => [];

    public IReadOnlyList<ProviderDataRequestReadModel> GetRequests(string tenantId, string companyId)
    {
        var ownership = IBDataRequestOwnership.Require(new IBDataRequestOwnership(tenantId, companyId));
        return _materialized.Values
            .Where(update =>
                string.Equals(update.Ownership.TenantId, ownership.TenantId, StringComparison.Ordinal) &&
                string.Equals(update.Ownership.CompanyId, ownership.CompanyId, StringComparison.Ordinal))
            .Select(static update => update.Request)
            .OrderBy(static request => request.RequestId)
            .ToArray();
    }

    /// <summary>
    /// Unscoped operator streams fail closed. Callers must use the tenant/company overload.
    /// </summary>
    public IAsyncEnumerable<ProviderDataRequestReadModel> WatchAsync(CancellationToken cancellationToken = default)
        => EmptyAsync(cancellationToken);

    public IAsyncEnumerable<ProviderDataRequestReadModel> WatchAsync(
        string tenantId,
        string companyId,
        CancellationToken cancellationToken = default)
        => _published.WatchAsync(
            IBDataRequestOwnership.Require(new IBDataRequestOwnership(tenantId, companyId)),
            cancellationToken);

    public async Task MaterializeAsync(
        string tenantId,
        string companyId,
        CancellationToken cancellationToken = default)
    {
        var ownership = IBDataRequestOwnership.Require(new IBDataRequestOwnership(tenantId, companyId));
        await foreach (var update in _source
                           .WatchAsync(ownership.TenantId, ownership.CompanyId, cancellationToken)
                           .ConfigureAwait(false))
        {
            var lineage = update.Lineage ?? throw new InvalidOperationException($"IB update {update.RequestId} was missing lineage.");
            var capturedAt = update.UpdatedAt;
            var requestIdentity = lineage.RequestId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var result = new IBDataResult(
                ownership.TenantId,
                ownership.CompanyId,
                $"interactive-brokers:{requestIdentity}:{update.Capability}", update.ProviderFamily,
                update.Capability, requestIdentity, lineage.Subscription, lineage.Symbol, update.AccountId,
                capturedAt, update.Status, JsonSerializer.Serialize(update), lineage);
            await _store.UpsertAsync(result, cancellationToken).ConfigureAwait(false);
            _materialized[CreateKey(ownership, update.RequestId)] = new MaterializedUpdate(ownership, update);
            _published.Publish(ownership, update);
        }
    }

    public void Dispose() => _published.Complete();

    private static async IAsyncEnumerable<ProviderDataRequestReadModel> EmptyAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        yield break;
    }

    private static string CreateKey(IBDataRequestOwnership ownership, int requestId)
        => string.Concat(
            ownership.TenantId.Length.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ":",
            ownership.TenantId,
            ownership.CompanyId.Length.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ":",
            ownership.CompanyId,
            requestId.ToString(System.Globalization.CultureInfo.InvariantCulture));

    private sealed record MaterializedUpdate(
        IBDataRequestOwnership Ownership,
        ProviderDataRequestReadModel Request);
}
