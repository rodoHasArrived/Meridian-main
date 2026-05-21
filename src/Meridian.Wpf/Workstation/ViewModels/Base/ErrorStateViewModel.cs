namespace Meridian.Wpf.Workstation.ViewModels.Base;

public sealed class ErrorStateViewModel : Meridian.Wpf.ViewModels.BindableBase
{
    private bool _hasError;
    private string _title = string.Empty;
    private string _message = string.Empty;

    public bool HasError
    {
        get => _hasError;
        set => SetProperty(ref _hasError, value);
    }

    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }

    public string Message
    {
        get => _message;
        set => SetProperty(ref _message, value);
    }
}
