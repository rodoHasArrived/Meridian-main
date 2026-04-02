using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace Meridian.Wpf.ViewModels;

/// <summary>
/// View model for provider settings rows in the backfill provider settings panel.
/// </summary>
public sealed class ProviderSettingsViewModel : INotifyPropertyChanged
{
    private static readonly SolidColorBrush ConfigUserBrush = new(Color.FromArgb(40, 0, 120, 212));
    private static readonly SolidColorBrush ConfigEnvBrush = new(Color.FromArgb(40, 0, 180, 0));
    private static readonly SolidColorBrush ConfigDefaultBrush = new(Color.FromArgb(40, 128, 128, 128));

    private bool _isEnabled;
    private int _priority;
    private string _rateLimitPerMinute = "";
    private string _rateLimitPerHour = "";
    private string? _inlineWarning;

    public string ProviderId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string DataTypes { get; set; } = string.Empty;
    public string ConfigSource { get; set; } = "default";
    public string HealthStatus { get; set; } = "unknown";
    public bool RequiresApiKey { get; set; }
    public bool FreeTier { get; set; }

    public bool IsEnabled
    {
        get => _isEnabled;
        set { _isEnabled = value; OnPropertyChanged(); }
    }

    public int Priority
    {
        get => _priority;
        set { _priority = value; OnPropertyChanged(); }
    }

    public string RateLimitPerMinute
    {
        get => _rateLimitPerMinute;
        set { _rateLimitPerMinute = value; OnPropertyChanged(); }
    }

    public string RateLimitPerHour
    {
        get => _rateLimitPerHour;
        set { _rateLimitPerHour = value; OnPropertyChanged(); }
    }

    public string? InlineWarning
    {
        get => _inlineWarning;
        set { _inlineWarning = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasInlineWarning)); }
    }

    public bool HasInlineWarning => !string.IsNullOrEmpty(_inlineWarning);

    public Brush ConfigSourceBrush => ConfigSource switch
    {
        "user" => ConfigUserBrush,
        "env" => ConfigEnvBrush,
        _ => ConfigDefaultBrush,
    };

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// View model for fallback chain preview items.
/// </summary>
public sealed class FallbackChainViewModel
{
    private static readonly SolidColorBrush HealthyBrush = new(Color.FromRgb(0, 180, 0));
    private static readonly SolidColorBrush DegradedBrush = new(Color.FromRgb(255, 180, 0));
    private static readonly SolidColorBrush UnhealthyBrush = new(Color.FromRgb(255, 60, 60));
    private static readonly SolidColorBrush UnknownHealthBrush = new(Color.FromRgb(128, 128, 128));
    private static readonly SolidColorBrush ConfigUserBrush = new(Color.FromArgb(40, 0, 120, 212));
    private static readonly SolidColorBrush ConfigEnvBrush = new(Color.FromArgb(40, 0, 180, 0));
    private static readonly SolidColorBrush ConfigDefaultBrush = new(Color.FromArgb(40, 128, 128, 128));

    public string Priority { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string DataTypes { get; set; } = string.Empty;
    public string HealthStatus { get; set; } = "unknown";
    public string RateLimitUsage { get; set; } = string.Empty;
    public string ConfigSource { get; set; } = "default";

    public Brush HealthBrush => HealthStatus switch
    {
        "healthy" => HealthyBrush,
        "degraded" => DegradedBrush,
        "unhealthy" => UnhealthyBrush,
        _ => UnknownHealthBrush,
    };

    public Brush ConfigSourceBrush => ConfigSource switch
    {
        "user" => ConfigUserBrush,
        "env" => ConfigEnvBrush,
        _ => ConfigDefaultBrush,
    };
}

/// <summary>
/// View model for dry-run backfill plan result items.
/// </summary>
public sealed class DryRunResultViewModel
{
    public string Symbol { get; set; } = string.Empty;
    public string SelectedProvider { get; set; } = string.Empty;
    public string FallbackSequence { get; set; } = string.Empty;
}

/// <summary>
/// View model for audit trail log items.
/// </summary>
public sealed class AuditLogViewModel
{
    public string Timestamp { get; set; } = string.Empty;
    public string ProviderId { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
}
