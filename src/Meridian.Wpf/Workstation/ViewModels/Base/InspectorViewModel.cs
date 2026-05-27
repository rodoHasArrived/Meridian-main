namespace Meridian.Wpf.Workstation.ViewModels.Base;

public sealed class InspectorViewModel : Meridian.Wpf.ViewModels.BindableBase
{
    private string _title = string.Empty;
    private string _summary = string.Empty;

    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }

    public string Summary
    {
        get => _summary;
        set => SetProperty(ref _summary, value);
    }
}
