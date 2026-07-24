using System.ComponentModel;
using System.Windows;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Views;

public partial class StartupWindow : Window
{
    private readonly StartupWindowViewModel _viewModel;
    private readonly CancellationTokenSource _lifecycleReadinessCts = new();

    public StartupWindow(StartupWindowViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        DataContext = _viewModel;

        _viewModel.StartupCompleted += OnStartupCompleted;
        _viewModel.StartupCancelled += OnStartupCancelled;
        _viewModel.PasswordResetRequested += OnPasswordResetRequested;
    }

    protected override async void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        if (UsernameBox.IsVisible)
        {
            UsernameBox.Focus();
        }
        try
        {
            await _viewModel.WaitForLifecycleReadinessAsync(_lifecycleReadinessCts.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (_lifecycleReadinessCts.IsCancellationRequested)
        {
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        _lifecycleReadinessCts.Cancel();
        _viewModel.StartupCompleted -= OnStartupCompleted;
        _viewModel.StartupCancelled -= OnStartupCancelled;
        _viewModel.PasswordResetRequested -= OnPasswordResetRequested;
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _lifecycleReadinessCts.Dispose();
        base.OnClosed(e);
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
