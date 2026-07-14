using System.Diagnostics;

namespace Meridian.Ui.Shared.Services;

public sealed class DesktopWorkstationLaunchService
{
    private static readonly HashSet<string> AllowedPages = new(StringComparer.OrdinalIgnoreCase)
    {
        "Trading", "Portfolio", "Accounting", "Reporting", "Strategy", "Data", "Settings"
    };

    public bool TryLaunch(string? requestedPage, out string message)
    {
        var page = string.IsNullOrWhiteSpace(requestedPage) ? "Portfolio" : requestedPage.Trim();
        if (!AllowedPages.Contains(page))
        {
            message = "Choose a supported Meridian workspace.";
            return false;
        }

        var installRoot = Environment.GetEnvironmentVariable("MERIDIAN_INSTALL_ROOT") ?? AppContext.BaseDirectory;
        var executable = Path.Combine(installRoot, "desktop", "Meridian.Desktop.exe");
        if (!File.Exists(executable))
        {
            message = "The desktop workstation is not installed. Repair Meridian to restore it.";
            return false;
        }

        Process.Start(new ProcessStartInfo(executable, $"--page={page}") { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(executable)! });
        message = "Desktop workstation opened.";
        return true;
    }
}
