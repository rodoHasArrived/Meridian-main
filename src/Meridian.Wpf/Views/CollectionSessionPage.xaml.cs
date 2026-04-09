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
        await _viewModel.LoadSessionsAsync();
    }

    private async void RefreshSessions_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.LoadSessionsAsync();
    }

    private async void CreateDailySession_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.CreateDailySessionAsync();
    }

    private async void PauseSession_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.PauseSessionAsync();
    }

    private async void StopSession_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.StopSessionAsync();
    }
}
