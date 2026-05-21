using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Workstation.State;

public enum WorkstationRegionStatus
{
    Loading,
    Empty,
    Error,
    Success,
    Warning
}

public sealed class WorkstationRegionState : BindableBase
{
    private WorkstationRegionStatus _status;
    private string _title = string.Empty;
    private string _detail = string.Empty;
    private string _actionId = string.Empty;
    private string _actionLabel = string.Empty;

    public WorkstationRegionStatus Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }

    public string Detail
    {
        get => _detail;
        set => SetProperty(ref _detail, value);
    }

    public string ActionId
    {
        get => _actionId;
        set => SetProperty(ref _actionId, value);
    }

    public string ActionLabel
    {
        get => _actionLabel;
        set => SetProperty(ref _actionLabel, value);
    }

    public static WorkstationRegionState Loading(string title, string detail)
        => new()
        {
            Status = WorkstationRegionStatus.Loading,
            Title = title,
            Detail = detail
        };

    public static WorkstationRegionState Empty(string title, string detail)
        => new()
        {
            Status = WorkstationRegionStatus.Empty,
            Title = title,
            Detail = detail
        };

    public static WorkstationRegionState Error(string title, string detail, string actionId = "", string actionLabel = "")
        => new()
        {
            Status = WorkstationRegionStatus.Error,
            Title = title,
            Detail = detail,
            ActionId = actionId,
            ActionLabel = actionLabel
        };

    public static WorkstationRegionState Success(string title, string detail)
        => new()
        {
            Status = WorkstationRegionStatus.Success,
            Title = title,
            Detail = detail
        };

    public static WorkstationRegionState Warning(string title, string detail)
        => new()
        {
            Status = WorkstationRegionStatus.Warning,
            Title = title,
            Detail = detail
        };
}
