using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Meridian.Wpf.ViewModels;
using WpfServices = Meridian.Wpf.Services;

namespace Meridian.Wpf.Views;

/// <summary>
/// Messaging Hub page — thin code-behind.
/// All state, timer management, and statistics live in <see cref="MessagingHubViewModel"/>.
/// </summary>
public partial class MessagingHubPage : Page
{
    private readonly MessagingHubViewModel _viewModel;

    public MessagingHubPage(WpfServices.MessagingService messagingService)
    {
        InitializeComponent();

        _viewModel = new MessagingHubViewModel(
            messagingService,
            infoBrush: (Brush)FindResource("InfoColorBrush"),
            successBrush: (Brush)FindResource("SuccessColorBrush"),
            errorBrush: (Brush)FindResource("ErrorColorBrush"));

        DataContext = _viewModel;
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e) => _viewModel.Start();
    private void OnPageUnloaded(object sender, RoutedEventArgs e) => _viewModel.Stop();
    private void Refresh_Click(object sender, RoutedEventArgs e) => _viewModel.Refresh();
    private void ClearActivity_Click(object sender, RoutedEventArgs e) => _viewModel.ClearActivity();
}
