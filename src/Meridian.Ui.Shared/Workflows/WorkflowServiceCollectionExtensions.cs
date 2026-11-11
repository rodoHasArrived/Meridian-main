using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Meridian.Ui.Shared.Workflows;

/// <summary>
/// Registers workflow definitions, the workflow registry, and file-backed workflow presets.
/// </summary>
public static class WorkflowServiceCollectionExtensions
{
    public static IServiceCollection AddWorkflowLibrary(this IServiceCollection services)
    {
        services.AddSingleton<IWorkflowDefinitionProvider, BuiltInWorkflowDefinitionProvider>();
        services.AddSingleton<WorkflowRegistry>();
        services.AddSingleton<IWorkflowActionCatalog>(sp => sp.GetRequiredService<WorkflowRegistry>());
        services.AddSingleton<WorkflowLibraryService>();
        services.AddSingleton<IWorkflowPresetStore>(sp =>
        {
            var dataRoot = ResolveDataRoot(sp);
            return new FileWorkflowPresetStore(
                dataRoot,
                sp.GetRequiredService<ILogger<FileWorkflowPresetStore>>());
        });
        services.AddSingleton<WorkflowPresetService>();
        // Per-operator default operating scope (C1), stored beside the workflow presets.
        services.AddSingleton<IOperatorScopePreferenceStore>(sp => new FileOperatorScopePreferenceStore(
            ResolveDataRoot(sp),
            sp.GetRequiredService<ILogger<FileOperatorScopePreferenceStore>>()));
        // Per-operator last visit to each surface (C3), for "since your last visit" markers.
        services.AddSingleton<IOperatorSurfaceVisitStore>(sp => new FileOperatorSurfaceVisitStore(
            ResolveDataRoot(sp),
            sp.GetRequiredService<ILogger<FileOperatorSurfaceVisitStore>>()));
        return services;
    }

    private static string ResolveDataRoot(IServiceProvider services)
    {
        var applicationConfig = services.GetService<Meridian.Application.UI.ConfigStore>();
        if (applicationConfig is not null)
        {
            return applicationConfig.GetDataRoot();
        }

        var sharedConfig = services.GetService<Meridian.Ui.Shared.Services.ConfigStore>();
        if (sharedConfig is not null)
        {
            return sharedConfig.GetDataRoot();
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Meridian",
            "workstation");
    }
}
