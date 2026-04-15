using System.Windows.Controls;
using Meridian.Wpf.Views;

namespace Meridian.Wpf.Tests.Support;

internal static class NavigationHostInspector
{
    public static object? ResolveInnermostContent(object? content)
    {
        return content switch
        {
            WorkspaceDeepPageHostPage hostPage => ResolveInnermostContent(hostPage.HostedContent ?? hostPage.HostedPage),
            Frame frame => ResolveInnermostContent(frame.Content),
            _ => content
        };
    }

    public static Page? ResolveInnermostPage(object? content)
        => ResolveInnermostContent(content) as Page;
}
