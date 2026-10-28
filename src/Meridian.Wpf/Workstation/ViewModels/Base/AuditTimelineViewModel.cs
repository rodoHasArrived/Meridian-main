using System.Collections.ObjectModel;

namespace Meridian.Wpf.Workstation.ViewModels.Base;

/// <summary>
/// Carries an audit event's timestamp, actor, and message.
/// </summary>
public sealed class AuditTimelineEntry
{
    public DateTimeOffset Timestamp { get; init; }
    public string Actor { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}

/// <summary>
/// Exposes an observable collection of timestamped audit entries for desktop binding.
/// </summary>
public sealed class AuditTimelineViewModel : Meridian.Wpf.ViewModels.BindableBase
{
    public ObservableCollection<AuditTimelineEntry> Entries { get; } = [];
}
