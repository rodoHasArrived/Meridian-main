using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Workflows;

public static class WorkflowServiceCollectionExtensions
{
    public static IServiceCollection AddWorkflowLibrary(this IServiceCollection services)
    {
        services.AddSingleton<IWorkflowDefinitionProvider, BuiltInWorkflowDefinitionProvider>();
        services.AddSingleton<WorkflowRegistry>();
        services.AddSingleton<IWorkflowActionCatalog>(sp => sp.GetRequiredService<WorkflowRegistry>());
        services.AddSingleton<WorkflowLibraryService>();
        return services;
    }
}
