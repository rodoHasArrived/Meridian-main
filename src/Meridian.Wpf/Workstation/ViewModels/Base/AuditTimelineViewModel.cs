using System.Collections.ObjectModel;

namespace Meridian.Wpf.Workstation.ViewModels.Base;

public sealed class AuditTimelineEntry
{
    public DateTimeOffset Timestamp { get; init; }
    public string Actor { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}

public sealed class AuditTimelineViewModel : Meridian.Wpf.ViewModels.BindableBase
{
    public ObservableCollection<AuditTimelineEntry> Entries { get; } = [];
}
