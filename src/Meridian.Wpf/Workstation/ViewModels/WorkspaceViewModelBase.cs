using System.Collections.ObjectModel;
using Meridian.Wpf.ViewModels;
using Meridian.Wpf.Workstation.Models;

namespace Meridian.Wpf.Workstation.ViewModels;

public abstract class WorkspaceViewModelBase : BindableBase
{
    private WorkstationStateModel _state = WorkstationStateModel.Loading(
        "Workspace loading",
        "The workstation is preparing the current workspace.");

    public WorkstationStateModel State
    {
        get => _state;
        protected set => SetProperty(ref _state, value);
    }
}

public abstract class DetailWorkspaceViewModelBase : WorkspaceViewModelBase
{
    private InspectorPanelModel _inspector = new();

    public InspectorPanelModel Inspector
    {
        get => _inspector;
        protected set => SetProperty(ref _inspector, value);
    }
}

public abstract class CommandHostViewModel : WorkspaceViewModelBase
{
    private WorkstationCommandGroupModel _commandGroup = new();

    public WorkstationCommandGroupModel CommandGroup
    {
        get => _commandGroup;
        protected set => SetProperty(ref _commandGroup, value);
    }
}

public class TableViewModel<TRecord> : WorkspaceViewModelBase
{
    private TRecord? _selectedRecord;

    public ObservableCollection<TRecord> Rows { get; } = new();

    public TRecord? SelectedRecord
    {
        get => _selectedRecord;
        set => SetProperty(ref _selectedRecord, value);
    }
}

public class FilterableTableViewModel<TRecord, TFilter> : TableViewModel<TRecord>
{
    private TFilter? _filter;

    public TFilter? Filter
    {
        get => _filter;
        set => SetProperty(ref _filter, value);
    }
}

public class InspectorViewModel<TItem> : WorkspaceViewModelBase
{
    private TItem? _selectedItem;
    private InspectorPanelModel _panel = new();

    public TItem? SelectedItem
    {
        get => _selectedItem;
        set => SetProperty(ref _selectedItem, value);
    }

    public InspectorPanelModel Panel
    {
        get => _panel;
        protected set => SetProperty(ref _panel, value);
    }
}
