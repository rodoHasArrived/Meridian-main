using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Meridian.Ui.Services;
using UiServices = Meridian.Ui.Services;
using WpfServices = Meridian.Wpf.Services;

namespace Meridian.Wpf.Views;

/// <summary>
/// Workspace management page for creating, editing, activating, and
/// importing/exporting workspace templates with session restore.
/// </summary>
public partial class WorkspacePage : Page
{
    private readonly WpfServices.WorkspaceService _workspaceService;
    private readonly WpfServices.LoggingService _loggingService;
    private readonly ObservableCollection<WorkspaceListItem> _workspaces = new();

    public WorkspacePage()
    {
        InitializeComponent();

        _workspaceService = WpfServices.WorkspaceService.Instance;
        _loggingService = WpfServices.LoggingService.Instance;

        WorkspacesList.ItemsSource = _workspaces;
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        LoadWorkspaces();
        LoadActiveWorkspace();
        LoadLastSession();
    }

    private void LoadWorkspaces()
    {
        _workspaces.Clear();

        foreach (var workspace in _workspaceService.Workspaces)
        {
            _workspaces.Add(new WorkspaceListItem
            {
                Id = workspace.Id,
                Name = workspace.Name,
                Description = workspace.Description,
                BadgeText = workspace.IsBuiltIn ? "BUILT-IN" : workspace.Category.ToDisplayName().ToUpperInvariant(),
                PagesText = $"{workspace.Pages.Count} pages",
                DeleteVisibility = workspace.IsBuiltIn ? Visibility.Collapsed : Visibility.Visible
            });
        }

        WorkspaceCountText.Text = $"{_workspaces.Count} workspace{(_workspaces.Count == 1 ? "" : "s")}";
    }

    private void LoadActiveWorkspace()
    {
        var active = _workspaceService.ActiveWorkspace;
        if (active != null)
        {
            ActiveWorkspaceName.Text = active.Name;
            ActiveWorkspaceDescription.Text = active.Description;
            ActiveWorkspacePages.Text = active.Pages.Count > 0
                ? $"Pages: {string.Join(", ", active.Pages.Select(p => p.Title))}"
                : "";
            ActiveCategoryText.Text = active.Category.ToDisplayName();
        }
        else
        {
            ActiveWorkspaceName.Text = "None";
            ActiveWorkspaceDescription.Text = "No workspace active. Select a workspace to activate.";
            ActiveWorkspacePages.Text = "";
            ActiveCategoryText.Text = "";
        }
    }

    private void LoadLastSession()
    {
        var session = _workspaceService.GetLastSessionState();
        if (session != null)
        {
            LastSessionText.Text = $"Saved {FormatTimestamp(session.SavedAt)} - " +
                                   $"Page: {session.ActivePageTag}, " +
                                   $"{session.OpenPages.Count} open pages";
            RestoreSessionButton.IsEnabled = true;
        }
        else
        {
            LastSessionText.Text = "No saved session found.";
            RestoreSessionButton.IsEnabled = false;
        }
    }

    private async void CreateWorkspace_Click(object sender, RoutedEventArgs e)
    {
        var name = NewWorkspaceNameBox.Text?.Trim();
        var description = NewWorkspaceDescBox.Text?.Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show("Please enter a workspace name.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var categoryIndex = NewCategoryCombo.SelectedIndex;
        var category = categoryIndex switch
        {
            0 => UiServices.WorkspaceCategory.Research,
            1 => UiServices.WorkspaceCategory.Trading,
            2 => UiServices.WorkspaceCategory.DataOperations,
            3 => UiServices.WorkspaceCategory.Governance,
            _ => UiServices.WorkspaceCategory.Custom
        };

        try
        {
            await _workspaceService.CreateWorkspaceAsync(name, description ?? "", category);
            NewWorkspaceNameBox.Text = "";
            NewWorkspaceDescBox.Text = "";
            LoadWorkspaces();
        }
        catch (Exception ex)
        {
            _loggingService.LogError("Failed to create workspace", ex);
            MessageBox.Show($"Failed to create workspace: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void ActivateWorkspace_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string workspaceId)
        {
            try
            {
                await _workspaceService.ActivateWorkspaceAsync(workspaceId);
                LoadActiveWorkspace();
            }
            catch (Exception ex)
            {
                _loggingService.LogError("Failed to activate workspace", ex);
            }
        }
    }

    private async void DeleteWorkspace_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string workspaceId)
        {
            var workspace = _workspaceService.Workspaces.FirstOrDefault(w => w.Id == workspaceId);
            if (workspace == null || workspace.IsBuiltIn) return;

            var confirm = MessageBox.Show(
                $"Delete workspace '{workspace.Name}'?",
                "Confirm Delete",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirm == MessageBoxResult.Yes)
            {
                try
                {
                    await _workspaceService.DeleteWorkspaceAsync(workspaceId);
                    LoadWorkspaces();
                    LoadActiveWorkspace();
                }
                catch (Exception ex)
                {
                    _loggingService.LogError("Failed to delete workspace", ex);
                }
            }
        }
    }

    private async void ExportWorkspace_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string workspaceId)
        {
            try
            {
                var json = await _workspaceService.ExportWorkspaceAsync(workspaceId);
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
                        await System.IO.File.WriteAllTextAsync(dialog.FileName, json);
                        MessageBox.Show("Workspace exported successfully.", "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogError("Failed to export workspace", ex);
                MessageBox.Show($"Export failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
                var json = await System.IO.File.ReadAllTextAsync(dialog.FileName);
                var imported = await _workspaceService.ImportWorkspaceAsync(json);
                if (imported != null)
                {
                    MessageBox.Show($"Imported workspace '{imported.Name}'.", "Import Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                    LoadWorkspaces();
                }
                else
                {
                    MessageBox.Show("Failed to parse workspace file.", "Import Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogError("Failed to import workspace", ex);
                MessageBox.Show($"Import failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private async void CaptureWorkspace_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var captured = await _workspaceService.CaptureCurrentStateAsync(
                $"Captured {DateTime.Now:MMM dd HH:mm}",
                "Workspace captured from current session state");

            MessageBox.Show($"Workspace '{captured.Name}' captured.", "Capture Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            LoadWorkspaces();
        }
        catch (Exception ex)
        {
            _loggingService.LogError("Failed to capture workspace", ex);
            MessageBox.Show($"Capture failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void RestoreSession_Click(object sender, RoutedEventArgs e)
    {
        var session = _workspaceService.GetLastSessionState();
        if (session != null)
        {
            WpfServices.MessagingService.Instance.SendNamed(
                WpfServices.MessageTypes.NavigationRequested,
                new { PageTag = session.ActivePageTag });
        }
    }

    private static string FormatTimestamp(DateTime timestamp)
    {
        var elapsed = DateTime.UtcNow - timestamp;
        return elapsed.TotalSeconds switch
        {
            < 60 => "just now",
            < 3600 => $"{(int)elapsed.TotalMinutes}m ago",
            < 86400 => $"{(int)elapsed.TotalHours}h ago",
            _ => timestamp.ToString("MMM dd, HH:mm")
        };
    }

    public sealed class WorkspaceListItem
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string BadgeText { get; set; } = string.Empty;
        public string PagesText { get; set; } = string.Empty;
        public Visibility DeleteVisibility { get; set; } = Visibility.Visible;
    }
}
