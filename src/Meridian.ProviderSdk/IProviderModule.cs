using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Infrastructure.Adapters.Core;

/// <summary>
/// Defines a provider module that can register provider services and data sources.
/// </summary>
public interface IProviderModule
{
    /// <summary>
    /// Register provider services into the DI container.
    /// </summary>
    void Register(IServiceCollection services, Meridian.Infrastructure.DataSources.DataSourceRegistry registry);
}
