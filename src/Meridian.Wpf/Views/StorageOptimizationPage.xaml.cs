using System.Windows;
using System.Windows.Controls;
using Meridian.Wpf.Services;
using Meridian.Wpf.ViewModels;
using WpfNotificationService = Meridian.Wpf.Services.NotificationService;

namespace Meridian.Wpf.Views;

public partial class StorageOptimizationPage : Page
{
    private readonly StorageOptimizationViewModel _viewModel;

    public StorageOptimizationPage()
        : this(new StorageOptimizationViewModel())
    {
    }

    public StorageOptimizationPage(
        NavigationService navigationService,
        WpfNotificationService notificationService)
        : this(new StorageOptimizationViewModel())
    {
        _ = navigationService;
        _ = notificationService;
    }

    internal StorageOptimizationPage(StorageOptimizationViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel ?? throw new System.ArgumentNullException(nameof(viewModel));
        DataContext = _viewModel;
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.LoadAsync();
    }
}
