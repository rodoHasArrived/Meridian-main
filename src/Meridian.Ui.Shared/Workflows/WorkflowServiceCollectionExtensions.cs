using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Meridian.Ui.Shared.Workflows;

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
