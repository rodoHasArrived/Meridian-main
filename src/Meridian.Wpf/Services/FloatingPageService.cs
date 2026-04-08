using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using Meridian.Wpf.Views;

namespace Meridian.Wpf.Services;

/// <summary>
/// Manages generic floating page windows. Pages can be popped out to a floating window
/// via the split-pane "Float Window" drop zone. Window positions are persisted across sessions.
/// </summary>
public sealed class FloatingPageService
{
    private static readonly Lazy<FloatingPageService> _instance =
        new(() => new FloatingPageService());

    public static FloatingPageService Instance => _instance.Value;

    private static readonly string PositionsFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Meridian", "floating-page-positions.json");

    private readonly Dictionary<string, FloatingPageWindow> _openWindows =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, double[]> _savedPositions =
        new(StringComparer.OrdinalIgnoreCase);

    private FloatingPageService()
    {
        LoadPositions();
    }

    /// <summary>
    /// Opens a floating page window for <paramref name="pageTag"/>, or focuses it if already open.
    /// Must be called on the UI thread.
    /// </summary>
    /// <param name="pageTag">The navigation tag identifying the page.</param>
    /// <param name="pageCreator">Factory that creates the page FrameworkElement from the tag.</param>
    /// <param name="pageTitle">Human-readable window title; auto-derived from the tag if null.</param>
    public void OpenPage(
        string pageTag,
        Func<string, FrameworkElement> pageCreator,
        string? pageTitle = null)
    {
        if (string.IsNullOrWhiteSpace(pageTag)) return;

        if (_openWindows.TryGetValue(pageTag, out var existing))
        {
            existing.Activate();
            existing.Focus();
            return;
        }

        var content = pageCreator(pageTag);
        var window = new FloatingPageWindow(pageTitle ?? FormatPageTitle(pageTag), content);
        PositionWindow(pageTag, window);

        window.Closed += (_, _) =>
        {
            _savedPositions[pageTag] = [window.Left, window.Top];
            _openWindows.Remove(pageTag);
            SavePositions();
        };

        _openWindows[pageTag] = window;
        window.Show();
    }

    /// <summary>
    /// Closes all open floating page windows and persists their last positions.
    /// Call this on application exit.
    /// </summary>
    public void CloseAll()
    {
        var snapshot = _openWindows.ToList();
        foreach (var (pageTag, window) in snapshot)
            _savedPositions[pageTag] = [window.Left, window.Top];
        foreach (var (_, window) in snapshot)
            window.Close();
        SavePositions();
    }

    private void PositionWindow(string pageTag, Window window)
    {
        if (_savedPositions.TryGetValue(pageTag, out var pos) && pos.Length >= 2)
        {
            window.Left = pos[0];
            window.Top = pos[1];
            return;
        }

        // Cascade new windows slightly offset from the main window
        var mainWindow = System.Windows.Application.Current.MainWindow;
        var offset = _openWindows.Count * 30;

        if (mainWindow is not null)
        {
            double windowWidth = double.IsNaN(window.Width) ? 1024 : window.Width;
            double windowHeight = double.IsNaN(window.Height) ? 700 : window.Height;

            double desiredLeft = mainWindow.Left + 80 + offset;
            double desiredTop = mainWindow.Top + 60 + offset;

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

    private static string FormatPageTitle(string pageTag)
    {
        // Insert spaces before capital letters: "DataBrowser" → "Data Browser"
        return Regex.Replace(pageTag, @"(?<=[a-z])([A-Z])", " $1").Trim();
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
            Debug.WriteLine($"[FloatingPageService] Failed to load positions from {PositionsFilePath}: {ex.Message}");
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
            Debug.WriteLine($"[FloatingPageService] Failed to save positions to {PositionsFilePath}: {ex.Message}");
        }
    }
}
