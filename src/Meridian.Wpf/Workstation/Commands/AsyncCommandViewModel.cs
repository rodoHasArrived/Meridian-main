using CommunityToolkit.Mvvm.Input;

namespace Meridian.Wpf.Workstation.Commands;

public sealed class AsyncCommandViewModel : CommandViewModel
{
    private bool _isBusy;
    private string _busyMessage = string.Empty;
    private string _errorMessage = string.Empty;
    private bool _canCancel;
    private IRelayCommand? _cancelCommand;

    public bool IsBusy
    {
        get => _isBusy;
        set => SetProperty(ref _isBusy, value);
    }

    public string BusyMessage
    {
        get => _busyMessage;
        set => SetProperty(ref _busyMessage, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        set => SetProperty(ref _errorMessage, value);
    }

    public bool CanCancel
    {
        get => _canCancel;
        set => SetProperty(ref _canCancel, value);
    }

    public IRelayCommand? CancelCommand
    {
        get => _cancelCommand;
        set => SetProperty(ref _cancelCommand, value);
    }
}
