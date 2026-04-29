using System;
using System.Collections.Generic;
using System.IO;
using Meridian.Ui.Services.Services;

namespace Meridian.Wpf.Services;

/// <summary>
/// Service for managing contextual tooltips, onboarding tips, and feature discovery.
/// Tracks which tips have been shown to avoid repetition.
/// Uses file-based storage for dismissed tips persistence.
/// Delegates content to the shared <see cref="TooltipContent"/> in Ui.Services.
/// </summary>
public sealed class TooltipService
{
    private static readonly Lazy<TooltipService> _instance = new(() => new TooltipService());

    private readonly HashSet<string> _shownTips = new();
    private readonly HashSet<string> _dismissedTips = new();
    private const string DismissedTipsFileName = "dismissed-tooltips.txt";

    public static TooltipService Instance => _instance.Value;

    private TooltipService()
    {
        LoadDismissedTips();
    }

    public FeatureHelp GetFeatureHelp(string featureKey) => TooltipContent.GetFeatureHelp(featureKey);

    public bool ShouldShowTip(string tipKey)
    {
        if (_dismissedTips.Contains(tipKey))
            return false;
        if (_shownTips.Contains(tipKey))
            return false;
        _shownTips.Add(tipKey);
        return true;
    }

    public void DismissTip(string tipKey)
    {
        _dismissedTips.Add(tipKey);
        SaveDismissedTips();
    }

    public void ResetAllTips()
    {
        _dismissedTips.Clear();
        _shownTips.Clear();
        SaveDismissedTips();
    }

    public IReadOnlyList<OnboardingTip> GetOnboardingTips(string pageKey) => TooltipContent.GetOnboardingTips(pageKey);

    /// <summary>
    /// Gets formatted tooltip text for a feature (title + description + tips).
    /// Can be used to populate WPF ToolTip content on controls.
    /// </summary>
    public string GetTooltipContent(string featureKey) => TooltipContent.GetTooltipText(featureKey);

    private static string GetSettingsFilePath()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Meridian");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, DismissedTipsFileName);
    }

    private void LoadDismissedTips()
    {
        try
        {
            var filePath = GetSettingsFilePath();
            if (File.Exists(filePath))
            {
                var serialized = File.ReadAllText(filePath);
                foreach (var tip in serialized.Split(',', StringSplitOptions.RemoveEmptyEntries))
                    _dismissedTips.Add(tip);
            }
        }
        catch { /* Ignore settings errors */ }
    }

    private void SaveDismissedTips()
    {
        try
        {
            File.WriteAllText(GetSettingsFilePath(), string.Join(",", _dismissedTips));
        }
        catch { /* Ignore settings errors */ }
    }
}
