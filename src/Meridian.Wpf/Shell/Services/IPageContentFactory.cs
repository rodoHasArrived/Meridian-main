using System.Windows;
using Meridian.Wpf.Models;

namespace Meridian.Wpf.Shell.Services;

public interface IPageContentFactory
{
    FrameworkElement CreateContent(
        string pageTag,
        object? parameter = null,
        WorkspaceChromePresentationMode presentationMode = WorkspaceChromePresentationMode.Docked);
}
