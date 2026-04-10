using System;
using System.Windows;
using Meridian.Ui.Services.Contracts;
using Meridian.Ui.Services.Services;

namespace Meridian.Wpf.Services;

/// <summary>
/// WPF-specific theme service that extends <see cref="ThemeServiceBase"/> with
/// ResourceDictionary management and window chrome updates.
/// Implements singleton pattern for application-wide theme management.
/// Phase 6C.2: Shared base class extracts state management and toggle logic.
/// </summary>
public sealed class ThemeService : ThemeServiceBase
{
    private static readonly Lazy<ThemeService> _instance = new(() => new ThemeService());

    private Window? _mainWindow;

    private const string LightThemeUri = "pack://application:,,,/Themes/LightTheme.xaml";
    private const string DarkThemeUri = "pack://application:,,,/Themes/DarkTheme.xaml";

    /// <summary>
    /// Gets the singleton instance of the ThemeService.
    /// </summary>
    public static ThemeService Instance => _instance.Value;

    /// <summary>
    /// Occurs when the theme is changed. Provides detailed event args with previous/new theme.
    /// </summary>
    public event EventHandler<ThemeChangedEventArgs>? ThemeChanged;

    private ThemeService() : base(AppTheme.Light)
    {
    }

    /// <summary>
    /// Initializes the theme service with the main application window.
    /// </summary>
    /// <param name="window">The main application window.</param>
    /// <exception cref="ArgumentNullException">Thrown when window is null.</exception>
    public void Initialize(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        _mainWindow = window;
        ApplyThemeCore(CurrentTheme);
    }

    /// <inheritdoc />
    protected override void ApplyThemeCore(AppTheme theme)
    {
        if (_mainWindow is null)
        {
            return;
        }

        var themeUri = theme == AppTheme.Dark ? DarkThemeUri : LightThemeUri;

        try
        {
            // Remove existing theme dictionaries
            var toRemove = new System.Collections.Generic.List<ResourceDictionary>();
            foreach (var dict in System.Windows.Application.Current.Resources.MergedDictionaries)
            {
                if (dict.Source?.OriginalString.Contains("Theme", StringComparison.OrdinalIgnoreCase) is true)
                {
                    toRemove.Add(dict);
                }
            }

            foreach (var dict in toRemove)
            {
                System.Windows.Application.Current.Resources.MergedDictionaries.Remove(dict);
            }

            // Add new theme dictionary
            var newThemeDict = new ResourceDictionary
            {
                Source = new Uri(themeUri, UriKind.Absolute)
            };
            System.Windows.Application.Current.Resources.MergedDictionaries.Add(newThemeDict);

            // Update system colors for window chrome
            UpdateWindowChrome(theme);
        }
        catch (Exception)
        {
            // Fall back to programmatic theming if resource dictionaries aren't available
            ApplyProgrammaticTheme(theme);
        }
    }

    /// <inheritdoc />
    protected override void OnThemeChanged(AppTheme previousTheme, AppTheme newTheme)
    {
        ThemeChanged?.Invoke(this, new ThemeChangedEventArgs(previousTheme, newTheme));
    }

    private void ApplyProgrammaticTheme(AppTheme theme)
    {
        if (_mainWindow is null)
        {
            return;
        }

        var isDark = theme == AppTheme.Dark;

        System.Windows.Application.Current.Resources["WindowBackgroundBrush"] = isDark
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(30, 30, 30))
            : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.White);

        System.Windows.Application.Current.Resources["TextBrush"] = isDark
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.White)
            : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Black);

        System.Windows.Application.Current.Resources["AccentBrush"] = isDark
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 120, 215))
            : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 99, 177));
    }

    private void UpdateWindowChrome(AppTheme theme)
    {
        if (_mainWindow is null)
        {
            return;
        }

        var isDark = theme == AppTheme.Dark;
        _mainWindow.Background = isDark
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(30, 30, 30))
            : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.White);
    }
}

/// <summary>
/// Event arguments for theme change events.
/// </summary>
public sealed class ThemeChangedEventArgs : EventArgs
{
    /// <summary>Gets the previous theme.</summary>
    public AppTheme PreviousTheme { get; }

    /// <summary>Gets the new theme.</summary>
    public AppTheme NewTheme { get; }

    /// <summary>
    /// Initializes a new instance of the ThemeChangedEventArgs class.
    /// </summary>
    public ThemeChangedEventArgs(AppTheme previousTheme, AppTheme newTheme)
    {
        PreviousTheme = previousTheme;
        NewTheme = newTheme;
    }
}
