using System.Windows;
using Meridian.Wpf.Models;

namespace Meridian.Wpf.Shell.Services;

/// <summary>
/// Creates page content for a page tag, navigation parameter, and chrome presentation mode.
/// </summary>
public interface IPageContentFactory
{
    FrameworkElement CreateContent(
        string pageTag,
        object? parameter = null,
        WorkspaceChromePresentationMode presentationMode = WorkspaceChromePresentationMode.Docked);
}
