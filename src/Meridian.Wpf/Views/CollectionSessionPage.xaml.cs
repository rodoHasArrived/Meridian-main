using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Meridian.Wpf.Services;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Views;

public partial class CollectionSessionPage : Page
{
    private readonly CollectionSessionViewModel _viewModel;

    public CollectionSessionPage(CollectionSessionViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await _viewModel.LoadSessionsAsync();
        }
        catch (System.OperationCanceledException)
        {
        }
        catch (System.Exception ex)
        {
            global::Meridian.Wpf.Services.LoggingService.Instance.LogError("Collection Session page failed to load.", ex);
        }
    }
}
