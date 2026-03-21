namespace Meridian.Ui.Services.Contracts;

/// <summary>
/// Application theme options.
/// Shared between WPF desktop applications.
/// </summary>
public enum AppTheme : byte
{
    Light,
    Dark,
    System
}

/// <summary>
/// Interface for managing application themes.
/// Shared between WPF desktop applications.
/// </summary>
public interface IThemeService
{
    AppTheme CurrentTheme { get; }

    event EventHandler<AppTheme>? ThemeChanged;

    void SetTheme(AppTheme theme);
    AppTheme GetSystemTheme();
}
