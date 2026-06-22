using Meridian.Wpf.Models;

namespace Meridian.Wpf.Services;

internal sealed record DesktopLaunchRequest(string? PageTag, bool StartCollector, string? ScreenshotPath)
{
    public bool HasPageNavigation => !string.IsNullOrWhiteSpace(PageTag);

    public bool HasScreenshotRequest => !string.IsNullOrWhiteSpace(ScreenshotPath);

    public bool RequiresShell => HasPageNavigation || HasScreenshotRequest;

    public bool HasActions => HasPageNavigation || StartCollector || HasScreenshotRequest;
}

internal static class DesktopLaunchArguments
{
    public static DesktopLaunchRequest Parse(IReadOnlyList<string>? args)
    {
        if (args is null || args.Count == 0)
        {
            return new DesktopLaunchRequest(PageTag: null, StartCollector: false, ScreenshotPath: null);
        }

        string? pageTag = null;
        var startCollector = false;
        string? screenshotPath = null;

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

            if (arg.StartsWith("--screenshot=", StringComparison.OrdinalIgnoreCase))
            {
                screenshotPath = NormalizeScreenshotPath(arg["--screenshot=".Length..]);
                continue;
            }

            if (string.Equals(arg, "--screenshot", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(arg, "--capture-screenshot", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Count)
                {
                    screenshotPath = NormalizeScreenshotPath(args[++i]);
                }

                continue;
            }

            if (string.Equals(arg, "--start-collector", StringComparison.OrdinalIgnoreCase))
            {
                startCollector = true;
            }
        }

        return new DesktopLaunchRequest(pageTag, startCollector, screenshotPath);
    }

    private static string? NormalizePageTag(string? pageTag)
    {
        if (string.IsNullOrWhiteSpace(pageTag))
        {
            return null;
        }

        return ShellNavigationCatalog.GetCanonicalPageTag(pageTag);
    }

    private static string? NormalizeScreenshotPath(string? path)
    {
        return string.IsNullOrWhiteSpace(path) ? null : path.Trim();
    }
}
