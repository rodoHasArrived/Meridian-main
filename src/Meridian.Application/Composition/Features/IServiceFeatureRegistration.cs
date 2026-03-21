using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Application.Composition.Features;

/// <summary>
/// Contract for a feature-oriented service registration module.
/// Each implementation registers a cohesive set of services into the DI container,
/// gated by the corresponding <see cref="CompositionOptions"/> flags.
/// </summary>
/// <remarks>
/// Implementations are consumed by <see cref="ServiceCompositionRoot.AddMarketDataServices"/>
/// which delegates to each feature module in dependency order.
/// </remarks>
public interface IServiceFeatureRegistration
{
    /// <summary>
    /// Registers services for this feature into the service collection.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="options">Composition options controlling which services to register.</param>
    /// <returns>The configured service collection for chaining.</returns>
    IServiceCollection Register(IServiceCollection services, CompositionOptions options);
}
