using Meridian.Wpf.Models;

namespace Meridian.Wpf.Services;

public interface IWorkspaceShellStateProvider
{
    WorkspaceShellDefinition Definition { get; }

    Task<WorkspaceShellState> GetStateAsync(CancellationToken ct = default);
}
