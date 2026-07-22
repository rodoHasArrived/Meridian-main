using Meridian.ProviderSdk;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Shared workstation projection seam for provider discovery, market-data, P&amp;L, and tick-rule
/// read models. UI hosts depend only on ProviderSdk records, never on a vendor adapter.
/// </summary>
public sealed class ProviderDataReadModelService
{
    private readonly IReadOnlyList<IProviderDataReadService> _providers;

    public ProviderDataReadModelService(IEnumerable<IProviderDataReadService> providers)
    {
        _providers = providers?.ToArray() ?? throw new ArgumentNullException(nameof(providers));
    }

    public IReadOnlyList<ProviderDataRequestReadModel> GetRequests()
        => _providers.SelectMany(static provider => provider.GetRequests())
            .OrderByDescending(static request => request.UpdatedAt)
            .ThenBy(static request => request.RequestId)
            .ToArray();
}
