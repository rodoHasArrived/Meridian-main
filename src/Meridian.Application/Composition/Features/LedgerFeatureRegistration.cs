using Meridian.Ledger;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Meridian.Application.Composition.Features;

/// <summary>
/// Registers core ledger services into the DI container.
/// </summary>
/// <remarks>
/// <para><b>What is registered:</b></para>
/// <list type="bullet">
/// <item><description><see cref="ProjectLedgerBook"/> — a singleton keyed ledger store that manages
/// independent in-memory <see cref="Ledger.Ledger"/> instances per strategy run or project.
/// Consumers resolve it to obtain or create a <see cref="Ledger.Ledger"/> by <see cref="LedgerBookKey"/>
/// without having to manage ledger lifetime themselves.</description></item>
/// </list>
/// <para><b>What is NOT registered here:</b></para>
/// <list type="bullet">
/// <item><description><c>LedgerReadService</c> — lives in <c>Meridian.Strategies</c>, which is not
/// referenced by <c>Meridian.Application</c>. It is registered by UI host startup code.</description></item>
/// <item><description><see cref="Ledger.Ledger"/> itself — created per-run by the backtesting engine and
/// strategy execution layer; it is a domain object, not an injectable singleton.</description></item>
/// </list>
/// </remarks>
internal sealed class LedgerFeatureRegistration : IServiceFeatureRegistration
{
    public IServiceCollection Register(IServiceCollection services, CompositionOptions options)
    {
        // ProjectLedgerBook manages a keyed collection of independent in-memory ledgers.
        // Registering as a singleton means all components within a host process share one
        // namespace, which is the correct model for an in-process trading workstation.
        services.TryAddSingleton<ProjectLedgerBook>();

        return services;
    }
}
