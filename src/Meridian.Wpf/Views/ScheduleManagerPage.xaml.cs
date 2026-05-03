using System.Windows;
using System.Windows.Controls;
using Meridian.Wpf.Services;
using Meridian.Wpf.ViewModels;
using ScheduleManagerService = Meridian.Ui.Services.ScheduleManagerService;

namespace Meridian.Wpf.Views;

public partial class ScheduleManagerPage : Page
{
    private readonly ScheduleManagerViewModel _viewModel;

    public ScheduleManagerPage(
        NavigationService navigationService,
        NotificationService notificationService)
    {
        InitializeComponent();
        _ = navigationService;
        _ = notificationService;
        _viewModel = new ScheduleManagerViewModel(new ScheduleManagerClient(ScheduleManagerService.Instance));
        DataContext = _viewModel;
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.InitializeAsync();
    }
}
