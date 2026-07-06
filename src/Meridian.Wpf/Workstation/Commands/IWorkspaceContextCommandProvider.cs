using System.Collections.ObjectModel;

namespace Meridian.Wpf.Workstation.Commands;

public interface IWorkspaceContextCommandProvider
{
    ReadOnlyObservableCollection<ContextCommandDescriptor> ContextCommands { get; }
}
