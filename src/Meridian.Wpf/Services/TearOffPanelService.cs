using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using Meridian.Wpf.ViewModels;
using Meridian.Wpf.Views;

namespace Meridian.Wpf.Services;

/// <summary>
/// Manages floating tear-off quote panels. Each panel tracks a single symbol live.
/// Window positions are persisted across sessions via a local JSON file.
/// </summary>
public sealed class TearOffPanelService
{
    private static readonly Lazy<TearOffPanelService> _instance =
        new(() => new TearOffPanelService());

    public static TearOffPanelService Instance => _instance.Value;

    private static readonly string PositionsFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Meridian", "tearoff-positions.json");

    private readonly Dictionary<string, QuoteFloatWindow> _openPanels =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, double[]> _savedPositions =
        new(StringComparer.OrdinalIgnoreCase);

    private TearOffPanelService()
    {
        LoadPositions();
    }

    /// <summary>
    /// Opens a floating panel for <paramref name="symbol"/>, or focuses it if already open.
    /// Must be called on the UI thread.
    /// </summary>
    public void TearOff(string symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol)) return;

        if (_openPanels.TryGetValue(symbol, out var existing))
        {
            existing.Activate();
            existing.Focus();
            return;
        }

        var vm = new QuoteFloatViewModel(symbol);
        var window = new QuoteFloatWindow(vm);
        PositionWindow(symbol, window);

        window.Closed += (_, _) =>
        {
            _savedPositions[symbol] = [window.Left, window.Top];
            _openPanels.Remove(symbol);
            SavePositions();
        };

        _openPanels[symbol] = window;
        window.Show();
    }

    /// <summary>
    /// Closes all open panels and persists their last positions. Called on app exit.
    /// </summary>
    public void CloseAll()
    {
        // Snapshot before closing: each window.Close() fires Closed which modifies _openPanels
        var snapshot = _openPanels.ToList();
        foreach (var (symbol, window) in snapshot)
            _savedPositions[symbol] = [window.Left, window.Top];
        foreach (var (_, window) in snapshot)
            window.Close();
        SavePositions();
    }

    private void PositionWindow(string symbol, Window window)
    {
        if (_savedPositions.TryGetValue(symbol, out var pos) && pos.Length >= 2)
        {
            window.Left = pos[0];
            window.Top = pos[1];
            return;
        }

        // Default: just to the right of the main window
        var mainWindow = System.Windows.Application.Current.MainWindow;
        if (mainWindow is not null)
        {
            double windowWidth = double.IsNaN(window.Width) ? 320 : window.Width;
            double windowHeight = double.IsNaN(window.Height) ? 210 : window.Height;

            double desiredLeft = mainWindow.Left + mainWindow.ActualWidth + 10;
            double desiredTop = mainWindow.Top;

            double minLeft = SystemParameters.VirtualScreenLeft;
            double minTop = SystemParameters.VirtualScreenTop;
            double maxLeft = minLeft + SystemParameters.VirtualScreenWidth - windowWidth;
            double maxTop = minTop + SystemParameters.VirtualScreenHeight - windowHeight;

            window.Left = Math.Max(minLeft, Math.Min(desiredLeft, maxLeft));
            window.Top = Math.Max(minTop, Math.Min(desiredTop, maxTop));
        }
        else
        {
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
    }

    private void LoadPositions()
    {
        try
        {
            if (!File.Exists(PositionsFilePath)) return;
            var json = File.ReadAllText(PositionsFilePath);
            var loaded = JsonSerializer.Deserialize<Dictionary<string, double[]>>(json);
            if (loaded is null) return;
            foreach (var (key, value) in loaded)
                _savedPositions[key] = value;
        }
        catch (Exception ex)
        {
        }
    }

    private void SavePositions()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PositionsFilePath)!);
            var json = JsonSerializer.Serialize(_savedPositions);
            File.WriteAllText(PositionsFilePath, json);
        }
        catch (Exception ex)
        {
        }
    }
}
