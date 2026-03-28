using System.Windows;
using Meridian.Wpf.ViewModels;
using Meridian.Wpf.Views;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Wpf.Services;

/// <summary>
/// Manages the single always-on-top <see cref="TickerStripWindow"/> instance.
/// Call <see cref="Toggle"/> to open/close the strip from any part of the shell.
/// </summary>
public static class TickerStripService
{
    private static TickerStripWindow? _window;

    /// <summary>Opens the strip if it is closed; closes it if it is open.</summary>
    public static void Toggle()
    {
        if (_window is null || !_window.IsLoaded)
            Open();
        else
            Close();
    }

    /// <summary>Opens and shows the ticker strip (no-op if already visible).</summary>
    public static void Open()
    {
        if (_window is { IsLoaded: true })
        {
            _window.Activate();
            return;
        }

        var viewModel = App.Services!.GetRequiredService<TickerStripViewModel>();
        _window = new TickerStripWindow(viewModel);
        _window.Closed += (_, _) => _window = null;
        _window.Show();
    }

    /// <summary>Closes the ticker strip (no-op if already closed).</summary>
    public static void Close()
    {
        _window?.Close();
        _window = null;
    }

    /// <summary>Returns <c>true</c> while the strip window is open.</summary>
    public static bool IsOpen => _window is { IsLoaded: true };
}
