using Meridian.Wpf.Services;

namespace Meridian.Wpf.Features.Data.Shell;

/// <summary>
/// Projects a loaded data-workspace snapshot into desktop shell presentation.
/// </summary>
public interface IDataWorkspaceShellPresentationService
{
    DataWorkspacePresentation Build(DataWorkspaceData data);
}

/// <summary>
/// Builds data-workspace presentation through the shared desktop presentation builder.
/// </summary>
public sealed class DataWorkspaceShellPresentationService : IDataWorkspaceShellPresentationService, IWorkspaceScopedService
{
    public DataWorkspacePresentation Build(DataWorkspaceData data)
        => DataWorkspacePresentationBuilder.Build(data);
}
