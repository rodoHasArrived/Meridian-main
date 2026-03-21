using Meridian.Ui.Services.Contracts;

namespace Meridian.Ui.Services.Services;

/// <summary>
/// Abstract base class for theme management shared across desktop applications.
/// Provides shared theme state management, toggle logic, and delegates platform-specific
/// theme application and event raising to derived classes.
/// Part of Phase 6C.2 service deduplication (ROADMAP item 6C.2).
/// </summary>
public abstract class ThemeServiceBase
{
    private AppTheme _currentTheme;

    /// <summary>Gets the current application theme.</summary>
    public AppTheme CurrentTheme => _currentTheme;

    /// <summary>
    /// Initializes a new instance with the specified initial theme.
    /// </summary>
    protected ThemeServiceBase(AppTheme initialTheme = AppTheme.Light)
    {
        _currentTheme = initialTheme;
    }

    /// <summary>
    /// Sets the application theme to the specified value.
    /// No-ops if the theme is already set to the requested value.
    /// </summary>
    public void SetTheme(AppTheme theme)
    {
        if (_currentTheme == theme)
        {
            return;
        }

        var previousTheme = _currentTheme;
        _currentTheme = theme;
        ApplyThemeCore(theme);
        OnThemeChanged(previousTheme, theme);
    }

    /// <summary>
    /// Toggles between light and dark themes.
    /// </summary>
    public void ToggleTheme()
    {
        var newTheme = _currentTheme == AppTheme.Light
            ? AppTheme.Dark
            : AppTheme.Light;
        SetTheme(newTheme);
    }

    /// <summary>
    /// Gets the system theme preference.
    /// </summary>
    public virtual AppTheme GetSystemTheme() => AppTheme.Light;

    /// <summary>
    /// When overridden, applies the theme to the platform-specific UI framework
    /// (e.g., WPF ResourceDictionary theme tokens).
    /// </summary>
    protected abstract void ApplyThemeCore(AppTheme theme);

    /// <summary>
    /// When overridden, raises platform-specific theme changed events.
    /// Called after the theme state has been updated and ApplyThemeCore has been called.
    /// </summary>
    protected abstract void OnThemeChanged(AppTheme previousTheme, AppTheme newTheme);
}
