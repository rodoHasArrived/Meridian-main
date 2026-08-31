using Meridian.Contracts.Api;
using Meridian.Infrastructure.Adapters.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Tests.TestSupport;

/// <summary>
/// Owns the process-wide provider catalog callback pair installed by one test host.
/// Arbitrary predecessor callbacks are never restored because they may close over a disposed host.
/// </summary>
internal sealed class ProviderCatalogTestLease : IDisposable
{
    private readonly Func<IReadOnlyList<ProviderCatalogEntry>> _catalogProvider;
    private readonly Func<string, ProviderCatalogEntry?> _catalogEntryProvider;
    private bool _disposed;

    private ProviderCatalogTestLease(
        Func<IReadOnlyList<ProviderCatalogEntry>> catalogProvider,
        Func<string, ProviderCatalogEntry?> catalogEntryProvider)
    {
        _catalogProvider = catalogProvider;
        _catalogEntryProvider = catalogEntryProvider;
    }

    public static ProviderCatalogTestLease Capture(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // ProviderRegistry installs the callbacks from its singleton factory. Resolve it before
        // host start so the exact pair owned by this host can be captured deterministically.
        _ = services.GetRequiredService<ProviderRegistry>();
        var catalogProvider = ProviderCatalog.RuntimeCatalogProvider
            ?? throw new InvalidOperationException("ProviderRegistry did not install a catalog callback.");
        var catalogEntryProvider = ProviderCatalog.RuntimeCatalogEntryProvider
            ?? throw new InvalidOperationException("ProviderRegistry did not install a catalog-entry callback.");
        return new ProviderCatalogTestLease(catalogProvider, catalogEntryProvider);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (ReferenceEquals(ProviderCatalog.RuntimeCatalogProvider, _catalogProvider) &&
            ReferenceEquals(ProviderCatalog.RuntimeCatalogEntryProvider, _catalogEntryProvider))
        {
            ProviderCatalog.RuntimeCatalogProvider = null;
            ProviderCatalog.RuntimeCatalogEntryProvider = null;
        }
    }
}
