using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Meridian.Ui.Shared.Extensibility;

public static class ExtensibilityServiceCollectionExtensions
{
    public static IServiceCollection AddExtensibilityCatalog(this IServiceCollection services)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IExtensibilityCatalogProvider, WorkflowExtensibilityCatalogProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IExtensibilityCatalogProvider, ReportingTemplateExtensibilityCatalogProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IExtensibilityCatalogProvider, PermissionExtensibilityCatalogProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IExtensibilityCatalogProvider, OperationalConfigurationExtensibilityCatalogProvider>());
        services.TryAddSingleton<ExtensibilityCatalogService>();
        return services;
    }
}
