using Meridian.Wpf.Models;

namespace Meridian.Wpf.Services;

internal sealed record DesktopLaunchRequest(string? PageTag, bool StartCollector)
{
    public bool HasPageNavigation => !string.IsNullOrWhiteSpace(PageTag);

    public bool HasActions => HasPageNavigation || StartCollector;
}

internal static class DesktopLaunchArguments
{
    public static DesktopLaunchRequest Parse(IReadOnlyList<string>? args)
    {
        if (args is null || args.Count == 0)
        {
            return new DesktopLaunchRequest(PageTag: null, StartCollector: false);
        }

        string? pageTag = null;
        var startCollector = false;

        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (string.IsNullOrWhiteSpace(arg))
            {
                continue;
            }

            if (arg.StartsWith("--page=", StringComparison.OrdinalIgnoreCase))
            {
                pageTag = NormalizePageTag(arg["--page=".Length..]);
                continue;
            }

            if (string.Equals(arg, "--page", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(arg, "--navigate", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Count)
                {
                    pageTag = NormalizePageTag(args[++i]);
                }

                continue;
            }

            if (string.Equals(arg, "--start-collector", StringComparison.OrdinalIgnoreCase))
            {
                startCollector = true;
            }
        }

        return new DesktopLaunchRequest(pageTag, startCollector);
    }

    private static string? NormalizePageTag(string? pageTag)
    {
        if (string.IsNullOrWhiteSpace(pageTag))
        {
            return null;
        }

        return ShellNavigationCatalog.GetCanonicalPageTag(pageTag);
    }
}
