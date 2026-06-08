using System.Windows.Media;
using Meridian.Ui.Services.Services;
using Meridian.Wpf.Services;
using Microsoft.Extensions.Hosting;

namespace Meridian.Wpf.ViewModels;

public sealed class StartupWindowViewModel : BindableBase
{
    private static readonly Brush InfoBrush = CreateBrush(0x2F, 0x6F, 0x8F);
    private static readonly Brush SuccessBrush = CreateBrush(0x16, 0x88, 0x5F);
    private static readonly Brush ErrorBrush = CreateBrush(0xBA, 0x3F, 0x55);

    private readonly DesktopAuthenticationSession _authenticationSession;

    private string _username = string.Empty;
    private string _password = string.Empty;
    private string _statusText = string.Empty;
    private Brush _statusBrush = InfoBrush;
    private Visibility _statusVisibility = Visibility.Collapsed;
    private bool _isBusy;

    public StartupWindowViewModel(
        DesktopAuthenticationSession authenticationSession,
        FixtureModeDetector fixtureModeDetector,
        IHostEnvironment hostEnvironment)
    {
        _authenticationSession = authenticationSession ?? throw new ArgumentNullException(nameof(authenticationSession));
        ArgumentNullException.ThrowIfNull(fixtureModeDetector);
        ArgumentNullException.ThrowIfNull(hostEnvironment);

        EnvironmentText = string.IsNullOrWhiteSpace(hostEnvironment.EnvironmentName)
            ? "Environment: default"
            : $"Environment: {hostEnvironment.EnvironmentName}";
        OperatingModeText = fixtureModeDetector.IsNonLiveMode
            ? fixtureModeDetector.ModeLabel
            : "Operating mode: live-capable workstation";
        AuthenticationModeText = ResolveAuthenticationModeText(authenticationSession);
        AuthenticationDetailText = ResolveAuthenticationDetailText(authenticationSession);
        CredentialSourceText = authenticationSession.IsConfigured
            ? "Credential source: MDC_USERS passwordHash or MDC_USERNAME/MDC_PASSWORD_HASH"
            : "Credential source: no desktop user accounts configured";
        ContinueWithoutCredentialsVisibility = authenticationSession.CanContinueWithoutCredentials
            ? Visibility.Visible
            : Visibility.Collapsed;
        LoginFormVisibility = authenticationSession.IsConfigured
            ? Visibility.Visible
            : Visibility.Collapsed;
        MissingConfigurationVisibility = authenticationSession.IsConfigured
            ? Visibility.Collapsed
            : Visibility.Visible;

        SignInCommand = new AsyncRelayCommand(SignInAsync, CanSignIn);
        ContinueWithoutCredentialsCommand = new RelayCommand(ContinueWithoutCredentials, () => authenticationSession.CanContinueWithoutCredentials && !IsBusy);
        CancelCommand = new RelayCommand(() => StartupCancelled?.Invoke(this, EventArgs.Empty), () => !IsBusy);
    }

    public event EventHandler? StartupCompleted;

    public event EventHandler? StartupCancelled;

    public event EventHandler? PasswordResetRequested;

    public string EnvironmentText { get; }

    public string OperatingModeText { get; }

    public string AuthenticationModeText { get; }

    public string AuthenticationDetailText { get; }

    public string CredentialSourceText { get; }

    public Visibility ContinueWithoutCredentialsVisibility { get; }

    public Visibility LoginFormVisibility { get; }

    public Visibility MissingConfigurationVisibility { get; }

    public IAsyncRelayCommand SignInCommand { get; }

    public IRelayCommand ContinueWithoutCredentialsCommand { get; }

    public IRelayCommand CancelCommand { get; }

    public string Username
    {
        get => _username;
        set
        {
            if (SetProperty(ref _username, value ?? string.Empty))
            {
                SignInCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string Password
    {
        get => _password;
        set
        {
            if (SetProperty(ref _password, value ?? string.Empty))
            {
                SignInCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public Brush StatusBrush
    {
        get => _statusBrush;
        private set => SetProperty(ref _statusBrush, value);
    }

    public Visibility StatusVisibility
    {
        get => _statusVisibility;
        private set => SetProperty(ref _statusVisibility, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                SignInCommand.NotifyCanExecuteChanged();
                ContinueWithoutCredentialsCommand.NotifyCanExecuteChanged();
                CancelCommand.NotifyCanExecuteChanged();
            }
        }
    }

    private bool CanSignIn()
        => !IsBusy &&
           _authenticationSession.IsConfigured &&
           !string.IsNullOrWhiteSpace(Username) &&
           !string.IsNullOrWhiteSpace(Password);

    private async Task SignInAsync()
    {
        if (!CanSignIn())
        {
            SetStatus("Enter a Meridian username and password.", ErrorBrush);
            return;
        }

        IsBusy = true;
        try
        {
            await Task.Yield();
            var result = _authenticationSession.SignIn(Username, Password);
            Password = string.Empty;
            PasswordResetRequested?.Invoke(this, EventArgs.Empty);
            if (!result.Succeeded)
            {
                SetStatus(result.Message, ErrorBrush);
                return;
            }

            SetStatus(result.Message, SuccessBrush);
            StartupCompleted?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ContinueWithoutCredentials()
    {
        var result = _authenticationSession.ContinueWithoutCredentials();
        if (!result.Succeeded)
        {
            SetStatus(result.Message, ErrorBrush);
            return;
        }

        SetStatus(result.Message, SuccessBrush);
        StartupCompleted?.Invoke(this, EventArgs.Empty);
    }

    private void SetStatus(string message, Brush brush)
    {
        StatusText = message;
        StatusBrush = brush;
        StatusVisibility = Visibility.Visible;
    }

    private static string ResolveAuthenticationModeText(DesktopAuthenticationSession session)
    {
        if (session.IsConfigured)
        {
            return "Authentication: configured users";
        }

        return session.CanContinueWithoutCredentials
            ? "Authentication: optional local development"
            : "Authentication: required configuration missing";
    }

    private static string ResolveAuthenticationDetailText(DesktopAuthenticationSession session)
    {
        if (session.IsConfigured)
        {
            return "Sign in with a configured Meridian operator account. The desktop stores only an in-memory session token for this process.";
        }

        return session.CanContinueWithoutCredentials
            ? "No credentials are configured. This development environment can continue without an operator account."
            : "No credentials are configured, and this environment requires authentication before the workstation shell can open.";
    }

    private static Brush CreateBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
