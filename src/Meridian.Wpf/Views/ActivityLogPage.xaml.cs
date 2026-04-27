using System.IO;
using System.Windows;
using System.Windows.Controls;
using Meridian.Wpf.ViewModels;
using Microsoft.Win32;
using WpfServices = Meridian.Wpf.Services;

namespace Meridian.Wpf.Views;

/// <summary>
/// Activity log page — thin code-behind.
/// All state, HTTP loading, filtering, and timer management live in <see cref="ActivityLogViewModel"/>.
/// The only UI-level action kept here is <c>LogList.ScrollIntoView</c> which requires
/// a direct reference to the list control.
/// </summary>
public partial class ActivityLogPage : Page
{
    private readonly ActivityLogViewModel _viewModel;

    public ActivityLogPage(
        WpfServices.StatusService statusService,
        WpfServices.LoggingService loggingService,
        WpfServices.NotificationService notificationService)
    {
        _viewModel = new ActivityLogViewModel(statusService, loggingService, notificationService);

        InitializeComponent();
        DataContext = _viewModel;

        // Relay the VM's scroll-request to the actual ListBox control.
        _viewModel.ScrollToTopRequested += (_, _) =>
        {
            if (LogList.Items.Count > 0)
                LogList.ScrollIntoView(LogList.Items[0]);
        };

        Unloaded += OnPageUnloaded;
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e) =>
        await _viewModel.StartAsync();

    private void OnPageUnloaded(object sender, RoutedEventArgs e) =>
        _viewModel.Stop();

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.CanExportVisibleLogs)
            return;

        var dialog = new SaveFileDialog
        {
            Title = "Export Activity Log",
            Filter = "CSV Files (*.csv)|*.csv|Text Files (*.txt)|*.txt|All Files (*.*)|*.*",
            DefaultExt = ".csv",
            FileName = $"activity_log_{DateTime.Now:yyyyMMdd_HHmmss}"
        };

        if (dialog.ShowDialog() != true)
            return;

        try
        {
            using var stream = File.OpenWrite(dialog.FileName);
            if (dialog.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                _viewModel.ExportAsCsv(stream);
            else
                _viewModel.ExportAsText(stream);

            _viewModel.ShowExportNotification(dialog.FileName);
        }
        catch (Exception ex)
        {
            _viewModel.ShowExportError(ex);
        }
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.ClearCommand.CanExecute(null))
            return;

        var result = MessageBox.Show(
            "Are you sure you want to clear all log entries?",
            "Clear Activity Log",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
            _viewModel.ClearCommand.Execute(null);
    }
}
