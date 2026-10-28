namespace Meridian.Wpf.Workstation.ViewModels.Base;

/// <summary>
/// Adds selected-item and inspector visibility state to workspace view models.
/// </summary>
public abstract class DetailWorkspaceViewModelBase<TItem> : WorkspaceViewModelBase
{
    private TItem? _selectedItem;
    private bool _isInspectorVisible;

    public TItem? SelectedItem
    {
        get => _selectedItem;
        set => SetProperty(ref _selectedItem, value);
    }

    public bool IsInspectorVisible
    {
        get => _isInspectorVisible;
        set => SetProperty(ref _isInspectorVisible, value);
    }
}
