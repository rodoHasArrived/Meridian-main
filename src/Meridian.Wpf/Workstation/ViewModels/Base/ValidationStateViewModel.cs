namespace Meridian.Wpf.Workstation.ViewModels.Base;

public sealed class ValidationStateViewModel : Meridian.Wpf.ViewModels.BindableBase
{
    private bool _isValid = true;
    private string _message = string.Empty;

    public bool IsValid
    {
        get => _isValid;
        set => SetProperty(ref _isValid, value);
    }

    public string Message
    {
        get => _message;
        set => SetProperty(ref _message, value);
    }
}
