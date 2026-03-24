using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Meridian.Wpf.Services;
using DataCalendarService = Meridian.Ui.Services.DataCalendarService;

namespace Meridian.Wpf.Views;

public partial class DataCalendarPage : Page
{
    private readonly NavigationService _navigationService;
    private readonly NotificationService _notificationService;
    private readonly DataCalendarService _calendarService;
    private int _currentYear;

    public DataCalendarPage(
        NavigationService navigationService,
        NotificationService notificationService)
    {
        InitializeComponent();

        _navigationService = navigationService;
        _notificationService = notificationService;
        _calendarService = new DataCalendarService();
        _currentYear = DateTime.UtcNow.Year;
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        CurrentYearText.Text = _currentYear.ToString();
        await LoadCalendarDataAsync();
    }

    private async void PreviousYear_Click(object sender, RoutedEventArgs e)
    {
        _currentYear--;
        CurrentYearText.Text = _currentYear.ToString();
        await LoadCalendarDataAsync();
    }

    private async void NextYear_Click(object sender, RoutedEventArgs e)
    {
        _currentYear++;
        CurrentYearText.Text = _currentYear.ToString();
        await LoadCalendarDataAsync();
    }

    private async System.Threading.Tasks.Task LoadCalendarDataAsync()
    {
        try
        {
            MonthlyStatus.Text = "Loading calendar data...";
            MonthlyStatus.Foreground = (Brush)FindResource("ConsoleTextMutedBrush");

            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            var yearData = await _calendarService.GetYearCalendarAsync(_currentYear, ct: cts.Token);

            // Update summary
            TradingDaysText.Text = yearData.TotalTradingDays.ToString();
            DaysWithDataText.Text = yearData.DaysWithData.ToString();
            TotalGapsText.Text = yearData.TotalGaps.ToString();
            CompletenessText.Text = $"{yearData.OverallCompleteness:F1}%";

            // Color completeness based on value
            if (yearData.OverallCompleteness >= 95)
                CompletenessText.Foreground = (Brush)FindResource("SuccessColorBrush");
            else if (yearData.OverallCompleteness >= 80)
                CompletenessText.Foreground = (Brush)FindResource("InfoColorBrush");
            else if (yearData.OverallCompleteness >= 50)
                CompletenessText.Foreground = (Brush)FindResource("WarningColorBrush");
            else
                CompletenessText.Foreground = (Brush)FindResource("ErrorColorBrush");

            // Monthly breakdown
            if (yearData.Months.Count > 0)
            {
                MonthlyStatus.Text = $"Showing {yearData.Months.Count} months";
                MonthlyBreakdownList.ItemsSource = yearData.Months;
            }
            else
            {
                MonthlyStatus.Text = "No calendar data available.";
                MonthlyBreakdownList.ItemsSource = null;
            }
        }
        catch (Exception ex)
        {
            MonthlyStatus.Text = $"Failed to load calendar data: {ex.Message}";
            MonthlyStatus.Foreground = (Brush)FindResource("ErrorColorBrush");

            TradingDaysText.Text = "--";
            DaysWithDataText.Text = "--";
            TotalGapsText.Text = "--";
            CompletenessText.Text = "--";
        }
    }
}
