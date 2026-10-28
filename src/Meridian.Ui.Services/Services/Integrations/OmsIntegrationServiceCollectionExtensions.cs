using Meridian.Ui.Shared.Contracts.Integrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Meridian.Ui.Services.Services.Integrations;

/// <summary>
/// Registers the shared OMS/EMS integration API handler with dependency injection.
/// </summary>
public static class OmsIntegrationServiceCollectionExtensions
{
    public static IServiceCollection AddOmsIntegrationApiHandlers(this IServiceCollection services)
    {
        services.Replace(ServiceDescriptor.Singleton<IOmsIntegrationApiHandler, OmsIntegrationApiHandler>());
        return services;
    }
}
