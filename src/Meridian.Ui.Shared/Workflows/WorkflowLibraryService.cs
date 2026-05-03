using Meridian.Contracts.Workstation;

namespace Meridian.Ui.Shared.Workflows;

/// <summary>
/// Builds the shared workflow library payload exposed to hosts and desktop UI.
/// </summary>
public sealed class WorkflowLibraryService
{
    private readonly IWorkflowActionCatalog _catalog;

    public WorkflowLibraryService(IWorkflowActionCatalog catalog)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    }

    public WorkflowLibraryDto GetLibrary()
        => new(
            GeneratedAt: DateTimeOffset.UtcNow,
            Workflows: _catalog.GetWorkflowDefinitions(),
            Actions: _catalog.GetActions());
}
