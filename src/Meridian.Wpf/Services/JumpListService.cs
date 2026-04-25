using System;
using System.Windows;
using System.Windows.Shell;

namespace Meridian.Wpf.Services;

/// <summary>
/// Registers the Windows taskbar jump list so users can right-click the Meridian
/// taskbar icon and launch directly to common pages or trigger the collector.
/// Call <see cref="Register"/> once from <c>App.OnStartup</c> after the host is built.
/// </summary>
public sealed class JumpListService
{
    private static readonly Lazy<JumpListService> _instance =
        new(() => new JumpListService());

    public static JumpListService Instance => _instance.Value;

    private JumpListService() { }

    /// <summary>
    /// Builds and applies the static tasks jump list to the current application.
    /// </summary>
    public void Register()
    {
        try
        {
            var exePath = GetExePath();

            var jumpList = new JumpList
            {
                ShowFrequentCategory = false,
                ShowRecentCategory = true,
            };

            jumpList.JumpItems.Add(CreateTask("Start Data Collection", "--start-collector", "Start holdings data collection immediately", exePath));
            jumpList.JumpItems.Add(CreateTask("Open Portfolio Operations", "--page=Dashboard", "Open holdings and data-quality oversight", exePath));
            jumpList.JumpItems.Add(CreateTask("Open Data Browser", "--page=DataBrowser", "Browse collected market data", exePath));
            jumpList.JumpItems.Add(CreateTask("Open Symbol Search", "--page=Symbols", "Manage and search symbols", exePath));
            jumpList.JumpItems.Add(CreateTask("Open Backtest", "--page=Backtest", "Run a strategy backtest", exePath));

            JumpList.SetJumpList(System.Windows.Application.Current, jumpList);

        }
        catch (Exception)
        {
            // Jump list is cosmetic; log and continue rather than crashing startup.
        }
    }

    /// <summary>
    /// Records a descriptive label in the jump list recent category.
    /// Callers supply human-readable strings such as <c>"Symbol: AAPL"</c>
    /// or <c>"Backtest: momentum-2024"</c>.
    /// </summary>
    public void AddToRecent(string label)
    {
        try
        {
            JumpList.AddToRecentCategory(label);
        }
        catch (Exception)
        {
        }
    }

    private static JumpTask CreateTask(string title, string arguments, string description, string exePath) =>
        new()
        {
            Title = title,
            Arguments = arguments,
            Description = description,
            ApplicationPath = exePath,
            IconResourcePath = exePath,
        };

    private static string GetExePath()
    {
        try
        {
            return System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
}
