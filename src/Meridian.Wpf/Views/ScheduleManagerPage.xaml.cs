using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using Meridian.Wpf.Services;
using ScheduleManagerService = Meridian.Ui.Services.ScheduleManagerService;

namespace Meridian.Wpf.Views;

public partial class ScheduleManagerPage : Page
{
    private readonly NavigationService _navigationService;
    private readonly NotificationService _notificationService;
    private readonly ScheduleManagerService _scheduleService;
    private System.Windows.Media.Brush _warningBrush = null!;
    private System.Windows.Media.Brush _successBrush = null!;
    private System.Windows.Media.Brush _errorBrush = null!;

    public ScheduleManagerPage(
        NavigationService navigationService,
        NotificationService notificationService)
    {
        InitializeComponent();

        _navigationService = navigationService;
        _notificationService = notificationService;
        _scheduleService = ScheduleManagerService.Instance;
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        // Cache brushes once to avoid resource dictionary walks on every validation keystroke
        _warningBrush = (System.Windows.Media.Brush)FindResource("WarningColorBrush");
        _successBrush = (System.Windows.Media.Brush)FindResource("SuccessColorBrush");
        _errorBrush = (System.Windows.Media.Brush)FindResource("ErrorColorBrush");

        await LoadBackfillSchedulesAsync();
        await LoadMaintenanceSchedulesAsync();
        await LoadTemplatesAsync();
    }

    private async void RefreshBackfillSchedules_Click(object sender, RoutedEventArgs e)
    {
        await LoadBackfillSchedulesAsync();
    }

    private async void RefreshMaintenanceSchedules_Click(object sender, RoutedEventArgs e)
    {
        await LoadMaintenanceSchedulesAsync();
    }

    private async void ValidateCron_Click(object sender, RoutedEventArgs e)
    {
        var expression = CronExpressionInput.Text?.Trim();
        if (string.IsNullOrEmpty(expression))
        {
            CronValidationResult.Text = "Please enter a cron expression.";
            CronValidationResult.Foreground = _warningBrush;
            return;
        }

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var result = await _scheduleService.ValidateCronExpressionAsync(expression, cts.Token);

            if (result == null)
            {
                CronValidationResult.Text = "Could not validate expression. Backend may be unavailable.";
                CronValidationResult.Foreground = _warningBrush;
                return;
            }

            if (result.IsValid)
            {
                var text = $"Valid: {result.Description}";
                if (result.NextRuns.Count > 0)
                {
                    text += "\nNext runs:";
                    foreach (var run in result.NextRuns)
                    {
                        text += $"\n  {run:yyyy-MM-dd HH:mm:ss} UTC";
                    }
                }
                CronValidationResult.Text = text;
                CronValidationResult.Foreground = _successBrush;
            }
            else
            {
                CronValidationResult.Text = $"Invalid: {result.ErrorMessage}";
                CronValidationResult.Foreground = _errorBrush;
            }
        }
        catch (Exception ex)
        {
            CronValidationResult.Text = $"Validation failed: {ex.Message}";
            CronValidationResult.Foreground = _errorBrush;
        }
    }

    private async System.Threading.Tasks.Task LoadBackfillSchedulesAsync()
    {
        try
        {
            BackfillSchedulesStatus.Text = "Loading...";
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var schedules = await _scheduleService.GetBackfillSchedulesAsync(cts.Token);

            if (schedules == null || schedules.Count == 0)
            {
                BackfillSchedulesStatus.Text = "No backfill schedules configured.";
                BackfillSchedulesList.ItemsSource = null;
                return;
            }

            BackfillSchedulesStatus.Text = $"{schedules.Count} schedule(s) found";
            BackfillSchedulesList.ItemsSource = schedules;
        }
        catch (Exception ex)
        {
            BackfillSchedulesStatus.Text = $"Failed to load: {ex.Message}";
            BackfillSchedulesStatus.Foreground = _errorBrush;
        }
    }

    private async System.Threading.Tasks.Task LoadMaintenanceSchedulesAsync()
    {
        try
        {
            MaintenanceSchedulesStatus.Text = "Loading...";
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var schedules = await _scheduleService.GetMaintenanceSchedulesAsync(cts.Token);

            if (schedules == null || schedules.Count == 0)
            {
                MaintenanceSchedulesStatus.Text = "No maintenance schedules configured.";
                MaintenanceSchedulesList.ItemsSource = null;
                return;
            }

            MaintenanceSchedulesStatus.Text = $"{schedules.Count} schedule(s) found";
            MaintenanceSchedulesList.ItemsSource = schedules;
        }
        catch (Exception ex)
        {
            MaintenanceSchedulesStatus.Text = $"Failed to load: {ex.Message}";
            MaintenanceSchedulesStatus.Foreground = _errorBrush;
        }
    }

    private async System.Threading.Tasks.Task LoadTemplatesAsync()
    {
        try
        {
            TemplatesStatus.Text = "Loading...";
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var templates = await _scheduleService.GetBackfillTemplatesAsync(cts.Token);

            if (templates == null || templates.Count == 0)
            {
                TemplatesStatus.Text = "No templates available.";
                TemplatesList.ItemsSource = null;
                return;
            }

            TemplatesStatus.Text = $"{templates.Count} template(s) available";
            TemplatesList.ItemsSource = templates;
        }
        catch (Exception ex)
        {
            TemplatesStatus.Text = $"Failed to load: {ex.Message}";
            TemplatesStatus.Foreground = _errorBrush;
        }
    }
}
