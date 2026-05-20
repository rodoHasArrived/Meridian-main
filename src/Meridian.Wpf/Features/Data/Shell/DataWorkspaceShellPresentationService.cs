using Meridian.Wpf.Services;

namespace Meridian.Wpf.Features.Data.Shell;

public interface IDataWorkspaceShellPresentationService
{
    DataOperationsWorkspacePresentation Build(DataOperationsWorkspaceData data);
}

public sealed class DataWorkspaceShellPresentationService : IDataWorkspaceShellPresentationService
{
    public DataOperationsWorkspacePresentation Build(DataOperationsWorkspaceData data)
        => DataOperationsWorkspacePresentationBuilder.Build(data);
}
