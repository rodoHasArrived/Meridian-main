using Meridian.Wpf.Services;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Views;

public partial class LifecycleControlPage : Page
{
    private readonly CancellationTokenSource _pageLifetime = new();
    private readonly LifecycleControlViewModel _viewModel;

    public LifecycleControlPage(LifecycleControlViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        DataContext = _viewModel;
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await _viewModel.RefreshAsync(_pageLifetime.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (_pageLifetime.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            LoggingService.Instance.LogError("Lifecycle control page failed to load.", ex);
        }
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        if (!_pageLifetime.IsCancellationRequested)
            _pageLifetime.Cancel();
    }
}
