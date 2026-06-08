using System.ComponentModel;
using System.Windows;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Views;

public partial class StartupWindow : Window
{
    private readonly StartupWindowViewModel _viewModel;

    public StartupWindow(StartupWindowViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        DataContext = _viewModel;

        _viewModel.StartupCompleted += OnStartupCompleted;
        _viewModel.StartupCancelled += OnStartupCancelled;
        _viewModel.PasswordResetRequested += OnPasswordResetRequested;
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        if (UsernameBox.IsVisible)
        {
            UsernameBox.Focus();
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        _viewModel.StartupCompleted -= OnStartupCompleted;
        _viewModel.StartupCancelled -= OnStartupCancelled;
        _viewModel.PasswordResetRequested -= OnPasswordResetRequested;
        base.OnClosing(e);
    }

    private void OnPasswordResetRequested(object? sender, EventArgs e)
    {
        StartupPasswordInput.ClearSecret();
    }

    private void OnStartupCompleted(object? sender, EventArgs e)
    {
        CloseWithResult(true);
    }

    private void OnStartupCancelled(object? sender, EventArgs e)
    {
        CloseWithResult(false);
    }

    private void CloseWithResult(bool result)
    {
        try
        {
            DialogResult = result;
        }
        catch (InvalidOperationException)
        {
            Close();
        }
    }
}
