namespace Meridian.Wpf.Workstation.ViewModels.Base;

public sealed class DiagnosticsViewModel : Meridian.Wpf.ViewModels.BindableBase
{
    private string _statusTitle = string.Empty;
    private string _statusDetail = string.Empty;

    public string StatusTitle
    {
        get => _statusTitle;
        set => SetProperty(ref _statusTitle, value);
    }

    public string StatusDetail
    {
        get => _statusDetail;
        set => SetProperty(ref _statusDetail, value);
    }
}
