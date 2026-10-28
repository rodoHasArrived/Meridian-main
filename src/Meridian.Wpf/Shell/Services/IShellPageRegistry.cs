using Meridian.Wpf.Models;

namespace Meridian.Wpf.Shell.Services;

/// <summary>
/// Exposes the registered desktop pages, workspace capabilities, and shell definitions.
/// </summary>
public interface IShellPageRegistry
{
    IReadOnlyList<ShellPageDescriptor> Pages { get; }

    IReadOnlyList<WorkspaceCapabilityDescriptor> WorkspaceCapabilities { get; }

    IReadOnlyList<WorkspaceShellDefinition> WorkspaceShells { get; }
}
