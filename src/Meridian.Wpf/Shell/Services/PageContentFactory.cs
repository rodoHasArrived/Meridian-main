using System.Windows;
using Meridian.Wpf.Models;
using Meridian.Wpf.Services;

namespace Meridian.Wpf.Shell.Services;

public sealed class PageContentFactory : IPageContentFactory
{
    private readonly NavigationService _navigationService;

    public PageContentFactory(NavigationService navigationService)
    {
        _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
    }

    public FrameworkElement CreateContent(
        string pageTag,
        object? parameter = null,
        WorkspaceChromePresentationMode presentationMode = WorkspaceChromePresentationMode.Docked)
        => _navigationService.CreatePageContent(pageTag, parameter, presentationMode);
}
