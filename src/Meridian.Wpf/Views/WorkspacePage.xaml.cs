using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Meridian.Ui.Services;
using Meridian.Wpf.ViewModels;
using WpfServices = Meridian.Wpf.Services;

namespace Meridian.Wpf.Views;

/// <summary>
/// Workspace management page for creating, editing, activating, and
/// importing/exporting workspace templates with session restore.
/// </summary>
public partial class WorkspacePage : Page
{
    private readonly WorkspacePageViewModel _viewModel;

    public WorkspacePage()
    {
        InitializeComponent();
        _viewModel = new WorkspacePageViewModel(
            WpfServices.WorkspaceService.Instance,
            WpfServices.NotificationService.Instance,
            WpfServices.LoggingService.Instance);
        DataContext = _viewModel;
        Loaded += OnPageLoaded;
        Unloaded += OnPageUnloaded;
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        _ = _viewModel.LoadAsync();
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        _viewModel.Dispose();
    }

    private async void ExportWorkspace_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string workspaceId)
        {
            try
            {
                var json = await _viewModel.GetExportJsonAsync(workspaceId);
                if (!string.IsNullOrEmpty(json))
                {
                    var dialog = new Microsoft.Win32.SaveFileDialog
                    {
                        Filter = "JSON files (*.json)|*.json",
                        Title = "Export Workspace",
                        FileName = $"workspace-{workspaceId}.json"
                    };

                    if (dialog.ShowDialog() == true)
                    {
                        await File.WriteAllTextAsync(dialog.FileName, json);
                        WpfServices.NotificationService.Instance.ShowNotification(
                            "Workspace Exported",
                            $"Exported to {Path.GetFileName(dialog.FileName)}.",
                            NotificationType.Success);
                    }
                }
            }
            catch (Exception ex)
            {
                WpfServices.LoggingService.Instance.LogError("Failed to export workspace", ex);
                WpfServices.NotificationService.Instance.ShowNotification(
                    "Workspace Export Failed",
                    ex.Message,
                    NotificationType.Error);
            }
        }
    }

    private async void ImportWorkspace_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            Title = "Import Workspace"
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                var json = await File.ReadAllTextAsync(dialog.FileName);
                await _viewModel.ImportFromJsonAsync(json);
            }
            catch (Exception ex)
            {
                WpfServices.LoggingService.Instance.LogError("Failed to import workspace", ex);
                WpfServices.NotificationService.Instance.ShowNotification(
                    "Workspace Import Failed",
                    ex.Message,
                    NotificationType.Error);
            }
        }
    }
}
