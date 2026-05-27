using Meridian.Wpf.Models;

namespace Meridian.Wpf.Shell.Services;

public interface IShellPageRegistry
{
    IReadOnlyList<ShellPageDescriptor> Pages { get; }

    IReadOnlyList<WorkspaceCapabilityDescriptor> WorkspaceCapabilities { get; }

    IReadOnlyList<WorkspaceShellDefinition> WorkspaceShells { get; }
}
