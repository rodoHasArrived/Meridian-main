using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Meridian.Ui.Shared.Extensibility;

/// <summary>
/// Registers extension catalogs and their configuration services.
/// </summary>
public static class ExtensibilityServiceCollectionExtensions
{
    public static IServiceCollection AddExtensibilityCatalog(this IServiceCollection services)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IExtensibilityCatalogProvider, WorkflowExtensibilityCatalogProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IExtensibilityCatalogProvider, ReportingTemplateExtensibilityCatalogProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IExtensibilityCatalogProvider, PermissionExtensibilityCatalogProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IExtensibilityCatalogProvider, OperationalConfigurationExtensibilityCatalogProvider>());
        services.TryAddSingleton<ExtensibilityCatalogService>();
        services.TryAddSingleton<IExtensibilityConfigurationStore, InMemoryExtensibilityConfigurationStore>();
        services.TryAddSingleton<ExtensibilityConfigurationService>();
        return services;
    }
}
