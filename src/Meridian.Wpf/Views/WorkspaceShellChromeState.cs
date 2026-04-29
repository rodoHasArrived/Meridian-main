using System.Windows;
using Meridian.Ui.Services.Services;

namespace Meridian.Wpf.Views;

/// <summary>
/// Exposes inherited shell-host state so legacy pages can compact duplicate chrome
/// when they are rendered inside the shared workspace shell host.
/// </summary>
public static class WorkspaceShellChromeState
{
    public static readonly DependencyProperty IsHostedInWorkspaceShellProperty =
        DependencyProperty.RegisterAttached(
            "IsHostedInWorkspaceShell",
            typeof(bool),
            typeof(WorkspaceShellChromeState),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));

    public static readonly DependencyProperty ShellDensityModeProperty =
        DependencyProperty.RegisterAttached(
            "ShellDensityMode",
            typeof(ShellDensityMode),
            typeof(WorkspaceShellChromeState),
            new FrameworkPropertyMetadata(ShellDensityMode.Standard, FrameworkPropertyMetadataOptions.Inherits, OnShellDensityModeChanged));

    public static readonly DependencyProperty IsCompactDensityProperty =
        DependencyProperty.RegisterAttached(
            "IsCompactDensity",
            typeof(bool),
            typeof(WorkspaceShellChromeState),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));

    public static bool GetIsHostedInWorkspaceShell(DependencyObject element)
        => (bool)element.GetValue(IsHostedInWorkspaceShellProperty);

    public static void SetIsHostedInWorkspaceShell(DependencyObject element, bool value)
        => element.SetValue(IsHostedInWorkspaceShellProperty, value);

    public static ShellDensityMode GetShellDensityMode(DependencyObject element)
        => (ShellDensityMode)element.GetValue(ShellDensityModeProperty);

    public static void SetShellDensityMode(DependencyObject element, ShellDensityMode value)
        => element.SetValue(ShellDensityModeProperty, value);

    public static bool GetIsCompactDensity(DependencyObject element)
        => (bool)element.GetValue(IsCompactDensityProperty);

    public static void SetIsCompactDensity(DependencyObject element, bool value)
        => element.SetValue(IsCompactDensityProperty, value);

    private static void OnShellDensityModeChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        var densityMode = e.NewValue is ShellDensityMode shellDensityMode
            ? shellDensityMode
            : ShellDensityMode.Standard;
        SetIsCompactDensity(dependencyObject, densityMode == ShellDensityMode.Compact);
    }
}
