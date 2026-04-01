using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Meridian.Wpf.ViewModels;
using DataCalendarService = Meridian.Ui.Services.DataCalendarService;

namespace Meridian.Wpf.Views;

/// <summary>
/// Data Calendar page — thin code-behind.
/// All state, data loading, and year navigation live in <see cref="DataCalendarViewModel"/>.
/// </summary>
public partial class DataCalendarPage : Page
{
    private readonly DataCalendarViewModel _viewModel;

    public DataCalendarPage(DataCalendarService calendarService)
    {
        InitializeComponent();

        _viewModel = new DataCalendarViewModel(
            calendarService,
            successBrush: (Brush)FindResource("SuccessColorBrush"),
            infoBrush:    (Brush)FindResource("InfoColorBrush"),
            warningBrush: (Brush)FindResource("WarningColorBrush"),
            errorBrush:   (Brush)FindResource("ErrorColorBrush"),
            mutedBrush:   (Brush)FindResource("ConsoleTextMutedBrush"));

        DataContext = _viewModel;
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e) =>
        await _viewModel.LoadAsync();

    private async void PreviousYear_Click(object sender, RoutedEventArgs e) =>
        await _viewModel.PreviousYearAsync();

    private async void NextYear_Click(object sender, RoutedEventArgs e) =>
        await _viewModel.NextYearAsync();
}
