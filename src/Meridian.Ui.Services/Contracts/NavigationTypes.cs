namespace Meridian.Ui.Services.Contracts;

/// <summary>
/// Represents a navigation history entry.
/// Shared between WPF desktop applications.
/// Part of Phase 6C.2 service deduplication.
/// </summary>
public sealed class NavigationEntry
{
    public string PageTag { get; init; } = string.Empty;
    public object? Parameter { get; init; }
    public DateTime Timestamp { get; init; }
}

/// <summary>
/// Navigation event arguments.
/// </summary>
public sealed class NavigationEventArgs : EventArgs
{
    public string PageTag { get; init; } = string.Empty;
    public object? Parameter { get; init; }
}
