using System.Collections.ObjectModel;
using Meridian.Wpf.Models;

namespace Meridian.Wpf.ViewModels;

/// <summary>
/// Interface for ViewModels that provide contextual action bar content.
/// </summary>
public interface IPageActionBarProvider
{
    /// <summary>
    /// The title to display on the left of the action bar.
    /// </summary>
    string PageTitle { get; }

    /// <summary>
    /// Observable collection of actions to display in the action bar.
    /// </summary>
    ObservableCollection<ActionEntry> Actions { get; }
}
