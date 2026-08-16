using Meridian.DataIntegration.Credentials;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Application.Composition;

/// <summary>
/// Registers the complete built-in provider setup catalog once across composition layers.
/// </summary>
public static class DefaultProviderSetupHandlerRegistration
{
    /// <summary>
    /// Adds every default setup handler in catalog order. A marker makes the operation idempotent
    /// when the application composition root and workstation composition are both applied.
    /// </summary>
    public static IServiceCollection AddDefaultProviderSetupHandlers(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (services.Any(static descriptor =>
                descriptor.ServiceType == typeof(DefaultProviderSetupHandlerRegistrationMarker)))
        {
            return services;
        }

        services.AddSingleton(new DefaultProviderSetupHandlerRegistrationMarker());
        foreach (var handler in DefaultProviderSetupHandlers.Create())
        {
            // Multiple catalog entries intentionally share the generic implementation type.
            // Explicit instances preserve each descriptor; TryAddEnumerable would deduplicate them.
            services.AddSingleton(typeof(IProviderSetupHandler), handler);
        }

        return services;
    }

    private sealed class DefaultProviderSetupHandlerRegistrationMarker;
}
