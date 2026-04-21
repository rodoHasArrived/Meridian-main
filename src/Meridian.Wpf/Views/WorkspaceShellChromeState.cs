using System.Windows;

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

    public static bool GetIsHostedInWorkspaceShell(DependencyObject element)
        => (bool)element.GetValue(IsHostedInWorkspaceShellProperty);

    public static void SetIsHostedInWorkspaceShell(DependencyObject element, bool value)
        => element.SetValue(IsHostedInWorkspaceShellProperty, value);
}
