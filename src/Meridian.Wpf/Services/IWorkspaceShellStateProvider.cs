using Meridian.Wpf.Models;

namespace Meridian.Wpf.Services;

/// <summary>
/// Supplies the i workspace definition and shell state.
/// </summary>
public interface IWorkspaceShellStateProvider
{
    WorkspaceShellDefinition Definition { get; }

    Task<WorkspaceShellState> GetStateAsync(CancellationToken ct = default);
}
