using System.Collections;
using System.Collections.ObjectModel;
using System.Windows.Media;
using Meridian.Wpf.Models;

namespace Meridian.Wpf.Workstation.Models;

public enum WorkstationStateKind
{
    Ready,
    Loading,
    Empty,
    Error,
    Stale,
    Blocked
}

public sealed record WorkstationStateModel(
    WorkstationStateKind Kind,
    string Title,
    string Detail,
    string ActionText = "",
    string TargetText = "",
    string EvidenceText = "",
    string Glyph = "",
    string Tone = WorkspaceTone.Neutral)
{
    public static WorkstationStateModel Ready(
        string title,
        string detail,
        string actionText = "",
        string targetText = "",
        string evidenceText = "",
        string glyph = "\uE73E",
        string tone = WorkspaceTone.Success)
        => new(WorkstationStateKind.Ready, title, detail, actionText, targetText, evidenceText, glyph, tone);

    public static WorkstationStateModel Loading(string title, string detail, string glyph = "\uE72C")
        => new(WorkstationStateKind.Loading, title, detail, Glyph: glyph, Tone: WorkspaceTone.Info);

    public static WorkstationStateModel Empty(
        string title,
        string detail,
        string actionText = "",
        string targetText = "",
        string glyph = "\uE946")
        => new(WorkstationStateKind.Empty, title, detail, actionText, targetText, Glyph: glyph, Tone: WorkspaceTone.Neutral);

    public static WorkstationStateModel Error(
        string title,
        string detail,
        string actionText = "",
        string targetText = "",
        string evidenceText = "",
        string glyph = "\uE783")
        => new(WorkstationStateKind.Error, title, detail, actionText, targetText, evidenceText, glyph, WorkspaceTone.Danger);
}

public sealed record WorkstationCommandModel(
    string Id,
    string Label,
    string Description,
    string Glyph = "",
    string Tone = WorkspaceTone.Secondary,
    bool IsEnabled = true,
    string DisabledReason = "",
    bool IsBusy = false,
    string ConfirmationText = "",
    string ShortcutHint = "");

public sealed class WorkstationCommandGroupModel
{
    public IReadOnlyList<WorkstationCommandModel> PrimaryCommands { get; init; } = Array.Empty<WorkstationCommandModel>();

    public IReadOnlyList<WorkstationCommandModel> SecondaryCommands { get; init; } = Array.Empty<WorkstationCommandModel>();
}

public static class WorkstationCommandMapper
{
    public static WorkstationCommandModel FromWorkspaceCommand(WorkspaceCommandItem command)
    {
        ArgumentNullException.ThrowIfNull(command);

        return new WorkstationCommandModel(
            command.Id,
            command.Label,
            command.Description,
            command.Glyph,
            command.Tone,
            command.IsEnabled,
            command.IsEnabled ? string.Empty : command.Description,
            IsBusy: false,
            ConfirmationText: string.Empty,
            command.ShortcutHint);
    }

    public static WorkstationCommandGroupModel FromWorkspaceCommandGroup(WorkspaceCommandGroup commandGroup)
    {
        ArgumentNullException.ThrowIfNull(commandGroup);

        return new WorkstationCommandGroupModel
        {
            PrimaryCommands = commandGroup.PrimaryCommands.Select(FromWorkspaceCommand).ToArray(),
            SecondaryCommands = commandGroup.SecondaryCommands.Select(FromWorkspaceCommand).ToArray()
        };
    }
}

public sealed record WorkstationBadgeModel(
    string Label,
    string Value,
    string Glyph = "",
    string Tone = WorkspaceTone.Neutral);

public sealed record WorkstationMetricModel(
    string Label,
    string Value,
    string Detail = "",
    string Glyph = "",
    string Tone = WorkspaceTone.Neutral,
    Brush? AccentBrush = null);

public sealed record WorkstationTableColumnModel(
    string Header,
    string BindingPath,
    double Width = 120);

public class WorkstationTableModel
{
    public string Title { get; init; } = string.Empty;

    public string EmptyTitle { get; init; } = "No rows";

    public string EmptyDetail { get; init; } = "No records are available.";

    public IReadOnlyList<WorkstationTableColumnModel> Columns { get; init; } = Array.Empty<WorkstationTableColumnModel>();

    public IEnumerable Rows { get; init; } = Array.Empty<object>();
}

public sealed class WorkstationTableModel<TRecord> : WorkstationTableModel
{
    public new IEnumerable<TRecord> Rows { get; init; } = Array.Empty<TRecord>();

    public WorkstationTableModel()
    {
        base.Rows = Rows;
    }

    public WorkstationTableModel(
        IEnumerable<TRecord> rows,
        IReadOnlyList<WorkstationTableColumnModel> columns,
        string title = "",
        string emptyTitle = "No rows",
        string emptyDetail = "No records are available.")
    {
        Rows = rows ?? throw new ArgumentNullException(nameof(rows));
        base.Rows = Rows;
        Columns = columns ?? throw new ArgumentNullException(nameof(columns));
        Title = title;
        EmptyTitle = emptyTitle;
        EmptyDetail = emptyDetail;
    }
}

public sealed class WorkstationFilterModel
{
    public string SearchText { get; init; } = string.Empty;

    public string ScopeText { get; init; } = string.Empty;

    public IReadOnlyList<string> ActiveFilters { get; init; } = Array.Empty<string>();
}

public sealed class InspectorPanelModel
{
    public string Title { get; init; } = "No selection";

    public string Subtitle { get; init; } = string.Empty;

    public string Detail { get; init; } = string.Empty;

    public WorkstationBadgeModel? Badge { get; init; }

    public IReadOnlyList<KeyValueFactModel> Facts { get; init; } = Array.Empty<KeyValueFactModel>();
}

public sealed record KeyValueFactModel(
    string Label,
    string Value,
    string Detail = "",
    string Tone = WorkspaceTone.Neutral);

public sealed class DiagnosticsChecklistModel
{
    public string Title { get; init; } = "Diagnostics";

    public string Detail { get; init; } = string.Empty;

    public IReadOnlyList<DiagnosticsChecklistItemModel> Items { get; init; } = Array.Empty<DiagnosticsChecklistItemModel>();
}

public sealed record DiagnosticsChecklistItemModel(
    string Label,
    string StatusText,
    string Detail,
    string Tone = WorkspaceTone.Neutral);

public sealed class AuditTimelineModel
{
    public string Title { get; init; } = "Activity";

    public string EmptyText { get; init; } = "No activity recorded.";

    public ObservableCollection<AuditTimelineEntryModel> Entries { get; } = new();
}

public sealed record AuditTimelineEntryModel(
    string Title,
    string Detail,
    string TimeText,
    string Tone = WorkspaceTone.Neutral);

public sealed class DataQualitySummaryModel
{
    public string Title { get; init; } = "Data quality";

    public string Detail { get; init; } = string.Empty;

    public IReadOnlyList<KeyValueFactModel> Facts { get; init; } = Array.Empty<KeyValueFactModel>();
}

public sealed class RoutingMatrixModel
{
    public string Title { get; init; } = "Routing";

    public string Detail { get; init; } = string.Empty;

    public IReadOnlyList<RoutingMatrixRowModel> Rows { get; init; } = Array.Empty<RoutingMatrixRowModel>();
}

public sealed record RoutingMatrixRowModel(
    string Workflow,
    string Route,
    string Status,
    string Detail,
    string Tone = WorkspaceTone.Neutral);
