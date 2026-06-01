using Meridian.Wpf.Services;

namespace Meridian.Wpf.Features.Data.Shell;

public interface IDataWorkspaceShellPresentationService
{
    DataWorkspacePresentation Build(DataWorkspaceData data);
}

public sealed class DataWorkspaceShellPresentationService : IDataWorkspaceShellPresentationService, IWorkspaceScopedService
{
    public DataWorkspacePresentation Build(DataWorkspaceData data)
        => DataWorkspacePresentationBuilder.Build(data);
}
