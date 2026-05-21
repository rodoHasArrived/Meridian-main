namespace Meridian.Wpf.Workstation.ViewModels.Base;

public sealed class LoadingStateViewModel : Meridian.Wpf.ViewModels.BindableBase
{
    private bool _isLoading;
    private string _message = string.Empty;

    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    public string Message
    {
        get => _message;
        set => SetProperty(ref _message, value);
    }
}
