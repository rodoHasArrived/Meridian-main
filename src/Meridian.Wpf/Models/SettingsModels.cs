using System.Windows;
using System.Windows.Media;

namespace Meridian.Wpf.Models;

/// <summary>
/// Credential display information for the settings page credential list.
/// </summary>
public sealed class CredentialDisplayInfo
{
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Resource { get; set; } = string.Empty;
    public SolidColorBrush TestStatusColor { get; set; } = new(Color.FromRgb(139, 148, 158));
}

/// <summary>
/// Activity item for the recent activity list on the settings page.
/// </summary>
public sealed class SettingsActivityItem
{
    public string Icon { get; set; } = string.Empty;
    public SolidColorBrush IconColor { get; set; } = new(Color.FromRgb(139, 148, 158));
    public string Message { get; set; } = string.Empty;
    public string Time { get; set; } = string.Empty;
}

/// <summary>
/// View model for credential vault list items showing per-provider credential status.
/// </summary>
public sealed class CredentialVaultItem
{
    public string ProviderId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string StatusMessage { get; set; } = string.Empty;
    public SolidColorBrush StatusBrush { get; set; } = new(Color.FromRgb(139, 148, 158));
    public Visibility ConfigureVisibility { get; set; } = Visibility.Collapsed;
}
