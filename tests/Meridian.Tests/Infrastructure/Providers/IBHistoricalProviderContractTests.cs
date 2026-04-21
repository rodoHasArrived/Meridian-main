using Meridian.Infrastructure.Adapters.InteractiveBrokers;

namespace Meridian.Tests.Infrastructure.Providers;

/// <summary>
/// Applies the shared <see cref="HistoricalDataProviderContractTests{TProvider}"/> suite to
/// <see cref="IBHistoricalDataProvider"/>.
/// <para>
/// In non-IBAPI builds the provider compiles to a no-arg stub that still satisfies the full
/// identity, metadata, capability, and disposal contracts.  This ensures IB's provider metadata
/// is always verifiable even when the optional IBAPI package is absent.
/// </para>
/// </summary>
public sealed class IBHistoricalProviderContractTests : HistoricalDataProviderContractTests<IBHistoricalDataProvider>
{
    protected override IBHistoricalDataProvider CreateProvider()
        => new();
}
