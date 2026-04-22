using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.Input;
using Meridian.Ui.Services;
using WpfServices = Meridian.Wpf.Services;

namespace Meridian.Wpf.ViewModels;

public sealed class WorkspaceListItemViewModel : BindableBase
{
    private bool _isActive;

    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public string BadgeText { get; init; } = string.Empty;

    public string PagesText { get; init; } = string.Empty;

    public bool CanDelete { get; init; }

    public bool IsActive
    {
        get => _isActive;
        set => SetProperty(ref _isActive, value);
    }
}

public sealed class WorkspacePageViewModel : BindableBase, IDisposable
{
    private const string SuccessColor = "#3FB950";
    private const string ErrorColor = "#F85149";
    private const string WarningColor = "#D29922";

    private readonly WpfServices.WorkspaceService _workspaceService;
    private readonly WpfServices.NotificationService _notificationService;
    private readonly WpfServices.LoggingService _loggingService;

    private string _newName = string.Empty;
    private string _newDescription = string.Empty;
    private int _newCategoryIndex;
    private string _newNameError = string.Empty;
    private bool _hasNewNameError;
    private string _activeWorkspaceName = "None";
    private string _activeWorkspaceDescription = "No workspace active. Select a workspace to activate.";
    private string _activeWorkspacePagesText = string.Empty;
    private string _activeWorkspaceCategoryText = string.Empty;
    private bool _hasActiveWorkspace;
    private string _lastSessionText = "No saved session found.";
    private bool _canRestoreSession;
    private bool _isBusy;
    private string _statusMessage = string.Empty;
    private bool _hasStatusMessage;
    private string _statusMessageColor = SuccessColor;
    private bool _hasWorkspaces;
    private string _workspaceCountText = "0 workspaces";
    private bool _disposed;

    public WorkspacePageViewModel(
        WpfServices.WorkspaceService workspaceService,
        WpfServices.NotificationService notificationService,
        WpfServices.LoggingService loggingService)
    {
        _workspaceService = workspaceService;
        _notificationService = notificationService;
        _loggingService = loggingService;

        LoadCommand = new AsyncRelayCommand(LoadAsync, () => !IsBusy);
        CreateWorkspaceCommand = new AsyncRelayCommand(CreateWorkspaceAsync, () => !IsBusy);
        ActivateWorkspaceCommand = new AsyncRelayCommand<string>(ActivateWorkspaceAsync, CanInteractWithWorkspace);
        DeleteWorkspaceCommand = new AsyncRelayCommand<string>(DeleteWorkspaceAsync, CanDeleteWorkspace);
        ExportWorkspaceCommand = new AsyncRelayCommand<string>(ExportWorkspaceAsync, CanInteractWithWorkspace);
        CaptureWorkspaceCommand = new AsyncRelayCommand(CaptureWorkspaceAsync, () => !IsBusy);
        RestoreSessionCommand = new RelayCommand(RestoreSession, () => !IsBusy && CanRestoreSession);
        ImportWorkspaceFromJsonCommand = new AsyncRelayCommand<string>(ImportWorkspaceAsync, CanImportWorkspaceJson);

        _workspaceService.WorkspaceCreated += OnWorkspaceChanged;
        _workspaceService.WorkspaceActivated += OnWorkspaceChanged;
        _workspaceService.WorkspaceDeleted += OnWorkspaceChanged;
        _workspaceService.WorkspaceUpdated += OnWorkspaceChanged;
    }

    public ObservableCollection<WorkspaceListItemViewModel> Workspaces { get; } = new();

    public IAsyncRelayCommand LoadCommand { get; }

    public IAsyncRelayCommand CreateWorkspaceCommand { get; }

    public IAsyncRelayCommand<string> ActivateWorkspaceCommand { get; }

    public IAsyncRelayCommand<string> DeleteWorkspaceCommand { get; }

    public IAsyncRelayCommand<string> ExportWorkspaceCommand { get; }

    public IAsyncRelayCommand CaptureWorkspaceCommand { get; }

    public IRelayCommand RestoreSessionCommand { get; }

    public IAsyncRelayCommand<string> ImportWorkspaceFromJsonCommand { get; }

    public string NewName
    {
        get => _newName;
        set => SetProperty(ref _newName, value);
    }

    public string NewDescription
    {
        get => _newDescription;
        set => SetProperty(ref _newDescription, value);
    }

    public int NewCategoryIndex
    {
        get => _newCategoryIndex;
        set => SetProperty(ref _newCategoryIndex, value);
    }

    public string NewNameError
    {
        get => _newNameError;
        private set => SetProperty(ref _newNameError, value);
    }

    public bool HasNewNameError
    {
        get => _hasNewNameError;
        private set => SetProperty(ref _hasNewNameError, value);
    }

    public string ActiveWorkspaceName
    {
        get => _activeWorkspaceName;
        private set => SetProperty(ref _activeWorkspaceName, value);
    }

    public string ActiveWorkspaceDescription
    {
        get => _activeWorkspaceDescription;
        private set => SetProperty(ref _activeWorkspaceDescription, value);
    }

    public string ActiveWorkspacePagesText
    {
        get => _activeWorkspacePagesText;
        private set => SetProperty(ref _activeWorkspacePagesText, value);
    }

    public string ActiveWorkspaceCategoryText
    {
        get => _activeWorkspaceCategoryText;
        private set => SetProperty(ref _activeWorkspaceCategoryText, value);
    }

    public bool HasActiveWorkspace
    {
        get => _hasActiveWorkspace;
        private set => SetProperty(ref _hasActiveWorkspace, value);
    }

    public string LastSessionText
    {
        get => _lastSessionText;
        private set => SetProperty(ref _lastSessionText, value);
    }

    public bool CanRestoreSession
    {
        get => _canRestoreSession;
        private set => SetProperty(ref _canRestoreSession, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                NotifyCommandStateChanged();
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public bool HasStatusMessage
    {
        get => _hasStatusMessage;
        private set => SetProperty(ref _hasStatusMessage, value);
    }

    public string StatusMessageColor
    {
        get => _statusMessageColor;
        private set => SetProperty(ref _statusMessageColor, value);
    }

    public bool HasWorkspaces
    {
        get => _hasWorkspaces;
        private set => SetProperty(ref _hasWorkspaces, value);
    }

    public string WorkspaceCountText
    {
        get => _workspaceCountText;
        private set => SetProperty(ref _workspaceCountText, value);
    }

    public async Task LoadAsync()
    {
        try
        {
            await _workspaceService.LoadWorkspacesAsync().ConfigureAwait(false);
            await RefreshFromServiceAsync().ConfigureAwait(false);
            ClearStatus();
        }
        catch (Exception ex)
        {
            _loggingService.LogError("Failed to load workspace page", ex);
            SetErrorStatus("Failed to load workspaces.");
            _notificationService.ShowNotification("Workspace Load Failed", ex.Message, NotificationType.Error);
        }
    }

    public async Task<string> GetExportJsonAsync(string workspaceId)
    {
        if (string.IsNullOrWhiteSpace(workspaceId))
        {
            SetWarningStatus("Select a workspace to export.");
            return string.Empty;
        }

        await ExecuteBusyAsync(
            async () =>
            {
                var json = await _workspaceService.ExportWorkspaceAsync(workspaceId).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(json))
                {
                    SetWarningStatus("The selected workspace could not be exported.");
                    return string.Empty;
                }

                SetSuccessStatus("Workspace export is ready.");
                return json;
            },
            "Failed to export workspace",
            "Workspace Export Failed").ConfigureAwait(false);

        return _lastOperationResult as string ?? string.Empty;
    }

    public Task ImportFromJsonAsync(string? json)
        => ImportWorkspaceCoreAsync(json ?? string.Empty);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _workspaceService.WorkspaceCreated -= OnWorkspaceChanged;
        _workspaceService.WorkspaceActivated -= OnWorkspaceChanged;
        _workspaceService.WorkspaceDeleted -= OnWorkspaceChanged;
        _workspaceService.WorkspaceUpdated -= OnWorkspaceChanged;
    }

    private object? _lastOperationResult;

    private async Task CreateWorkspaceAsync()
    {
        if (string.IsNullOrWhiteSpace(NewName))
        {
            NewNameError = "Name is required";
            HasNewNameError = true;
            SetErrorStatus("Workspace name is required.");
            return;
        }

        ClearValidationErrors();

        await ExecuteBusyAsync(
            async () =>
            {
                var workspace = await _workspaceService.CreateWorkspaceAsync(
                    NewName.Trim(),
                    NewDescription.Trim(),
                    MapCategory(NewCategoryIndex),
                    CancellationToken.None).ConfigureAwait(false);

                NewName = string.Empty;
                NewDescription = string.Empty;
                NewCategoryIndex = 0;

                await RefreshFromServiceAsync().ConfigureAwait(false);

                var message = $"Created workspace '{workspace.Name}'.";
                SetSuccessStatus(message);
                _notificationService.ShowNotification("Workspace Created", message, NotificationType.Success);
            },
            "Failed to create workspace",
            "Workspace Create Failed").ConfigureAwait(false);
    }

    private async Task ActivateWorkspaceAsync(string? workspaceId)
    {
        if (!CanInteractWithWorkspace(workspaceId))
        {
            return;
        }

        await ExecuteBusyAsync(
            async () =>
            {
                await _workspaceService.ActivateWorkspaceAsync(workspaceId!, CancellationToken.None).ConfigureAwait(false);
                await RefreshFromServiceAsync().ConfigureAwait(false);

                var activeName = _workspaceService.ActiveWorkspace?.Name ?? "workspace";
                var message = $"Activated '{activeName}'.";
                SetSuccessStatus(message);
                _notificationService.ShowNotification("Workspace Activated", message, NotificationType.Success);
            },
            "Failed to activate workspace",
            "Workspace Activation Failed").ConfigureAwait(false);
    }

    private async Task DeleteWorkspaceAsync(string? workspaceId)
    {
        if (!CanDeleteWorkspace(workspaceId))
        {
            return;
        }

        var workspaceName = _workspaceService.Workspaces.FirstOrDefault(w => w.Id == workspaceId)?.Name ?? "workspace";

        await ExecuteBusyAsync(
            async () =>
            {
                await _workspaceService.DeleteWorkspaceAsync(workspaceId!, CancellationToken.None).ConfigureAwait(false);
                await RefreshFromServiceAsync().ConfigureAwait(false);

                var message = $"Deleted workspace '{workspaceName}'.";
                SetSuccessStatus(message);
                _notificationService.ShowNotification("Workspace Deleted", message, NotificationType.Success);
            },
            "Failed to delete workspace",
            "Workspace Delete Failed").ConfigureAwait(false);
    }

    private async Task ExportWorkspaceAsync(string? workspaceId)
    {
        _ = await GetExportJsonAsync(workspaceId ?? string.Empty).ConfigureAwait(false);
    }

    private async Task CaptureWorkspaceAsync()
    {
        await ExecuteBusyAsync(
            async () =>
            {
                var captured = await _workspaceService.CaptureCurrentStateAsync(
                    $"Captured {DateTime.Now:MMM dd HH:mm}",
                    "Workspace captured from current session state",
                    CancellationToken.None).ConfigureAwait(false);

                await RefreshFromServiceAsync().ConfigureAwait(false);

                var message = $"Captured workspace '{captured.Name}'.";
                SetSuccessStatus(message);
                _notificationService.ShowNotification("Workspace Captured", message, NotificationType.Success);
            },
            "Failed to capture workspace",
            "Workspace Capture Failed").ConfigureAwait(false);
    }

    private void RestoreSession()
    {
        var session = _workspaceService.GetLastSessionState();
        if (session is null || string.IsNullOrWhiteSpace(session.ActivePageTag))
        {
            SetWarningStatus("No saved session is available to restore.");
            _notificationService.ShowNotification("Restore Session", "No saved session is available.", NotificationType.Warning);
            return;
        }

        WpfServices.MessagingService.Instance.SendNamed(
            WpfServices.MessageTypes.NavigationRequested,
            new { PageTag = session.ActivePageTag });

        var message = $"Restored session to '{session.ActivePageTag}'.";
        SetSuccessStatus(message);
        _notificationService.ShowNotification("Session Restored", message, NotificationType.Success);
    }

    private Task ImportWorkspaceAsync(string? json)
        => ImportWorkspaceCoreAsync(json ?? string.Empty);

    private async Task ImportWorkspaceCoreAsync(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            SetWarningStatus("The selected file did not contain a workspace.");
            _notificationService.ShowNotification("Import Failed", "The selected file was empty.", NotificationType.Warning);
            return;
        }

        await ExecuteBusyAsync(
            async () =>
            {
                var imported = await _workspaceService.ImportWorkspaceAsync(json, CancellationToken.None).ConfigureAwait(false);
                await RefreshFromServiceAsync().ConfigureAwait(false);

                if (imported is null)
                {
                    SetWarningStatus("Failed to parse workspace file.");
                    _notificationService.ShowNotification("Import Failed", "The file does not contain a valid workspace.", NotificationType.Warning);
                    return;
                }

                var message = $"Imported workspace '{imported.Name}'.";
                SetSuccessStatus(message);
                _notificationService.ShowNotification("Workspace Imported", message, NotificationType.Success);
            },
            "Failed to import workspace",
            "Workspace Import Failed").ConfigureAwait(false);
    }

    private void OnWorkspaceChanged(object? sender, Meridian.Ui.Services.WorkspaceEventArgs e)
    {
        _ = HandleWorkspaceChangedAsync();
    }

    private async Task HandleWorkspaceChangedAsync()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            await RefreshFromServiceAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _loggingService.LogError("Failed to refresh workspace page after workspace change", ex);
        }
    }

    private async Task RefreshFromServiceAsync()
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            RefreshFromService();
            return;
        }

        await dispatcher.InvokeAsync(RefreshFromService).Task.ConfigureAwait(false);
    }

    private void RefreshFromService()
    {
        var activeWorkspaceId = _workspaceService.ActiveWorkspace?.Id;

        Workspaces.Clear();
        foreach (var workspace in _workspaceService.Workspaces)
        {
            Workspaces.Add(new WorkspaceListItemViewModel
            {
                Id = workspace.Id,
                Name = workspace.Name,
                Description = workspace.Description,
                BadgeText = workspace.IsBuiltIn
                    ? "BUILT-IN"
                    : workspace.Category.ToDisplayName().ToUpperInvariant(),
                PagesText = $"{workspace.Pages.Count} page{(workspace.Pages.Count == 1 ? string.Empty : "s")}",
                CanDelete = !workspace.IsBuiltIn,
                IsActive = string.Equals(workspace.Id, activeWorkspaceId, StringComparison.Ordinal)
            });
        }

        HasWorkspaces = Workspaces.Count > 0;
        WorkspaceCountText = $"{Workspaces.Count} workspace{(Workspaces.Count == 1 ? string.Empty : "s")}";

        RefreshActiveWorkspace();
        RefreshLastSession();
        NotifyCommandStateChanged();
    }

    private void RefreshActiveWorkspace()
    {
        var active = _workspaceService.ActiveWorkspace;
        if (active is null)
        {
            HasActiveWorkspace = false;
            ActiveWorkspaceName = "None";
            ActiveWorkspaceDescription = "No workspace active. Select a workspace to activate.";
            ActiveWorkspacePagesText = string.Empty;
            ActiveWorkspaceCategoryText = string.Empty;
            return;
        }

        HasActiveWorkspace = true;
        ActiveWorkspaceName = active.Name;
        ActiveWorkspaceDescription = active.Description;
        ActiveWorkspacePagesText = active.Pages.Count > 0
            ? $"Pages: {string.Join(", ", active.Pages.Select(page => page.Title))}"
            : string.Empty;
        ActiveWorkspaceCategoryText = active.Category.ToDisplayName();
    }

    private void RefreshLastSession()
    {
        var session = _workspaceService.GetLastSessionState();
        if (session is null)
        {
            LastSessionText = "No saved session found.";
            CanRestoreSession = false;
            return;
        }

        LastSessionText = $"Saved {FormatTimestamp(session.SavedAt)} - Page: {session.ActivePageTag}, {session.OpenPages.Count} open pages";
        CanRestoreSession = !string.IsNullOrWhiteSpace(session.ActivePageTag);
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

    private static WorkspaceCategory MapCategory(int categoryIndex)
    {
        return categoryIndex switch
        {
            0 => WorkspaceCategory.Research,
            1 => WorkspaceCategory.Trading,
            2 => WorkspaceCategory.DataOperations,
            3 => WorkspaceCategory.Governance,
            _ => WorkspaceCategory.Custom
        };
    }

    private bool CanInteractWithWorkspace(string? workspaceId)
        => !IsBusy && !string.IsNullOrWhiteSpace(workspaceId);

    private bool CanDeleteWorkspace(string? workspaceId)
        => !IsBusy
           && !string.IsNullOrWhiteSpace(workspaceId)
           && Workspaces.FirstOrDefault(item => item.Id == workspaceId)?.CanDelete == true;

    private bool CanImportWorkspaceJson(string? json)
        => !IsBusy && !string.IsNullOrWhiteSpace(json);

    private async Task ExecuteBusyAsync(Func<Task> action, string logMessage, string notificationTitle)
    {
        try
        {
            IsBusy = true;
            _lastOperationResult = null;
            await action().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _loggingService.LogError(logMessage, ex);
            SetErrorStatus(ex.Message);
            _notificationService.ShowNotification(notificationTitle, ex.Message, NotificationType.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ExecuteBusyAsync<T>(Func<Task<T>> action, string logMessage, string notificationTitle)
    {
        try
        {
            IsBusy = true;
            _lastOperationResult = await action().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _loggingService.LogError(logMessage, ex);
            SetErrorStatus(ex.Message);
            _notificationService.ShowNotification(notificationTitle, ex.Message, NotificationType.Error);
            _lastOperationResult = default(T);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ClearValidationErrors()
    {
        NewNameError = string.Empty;
        HasNewNameError = false;
    }

    private void ClearStatus()
    {
        StatusMessage = string.Empty;
        HasStatusMessage = false;
        StatusMessageColor = SuccessColor;
    }

    private void SetSuccessStatus(string message)
        => SetStatus(message, SuccessColor);

    private void SetErrorStatus(string message)
        => SetStatus(message, ErrorColor);

    private void SetWarningStatus(string message)
        => SetStatus(message, WarningColor);

    private void SetStatus(string message, string color)
    {
        StatusMessage = message;
        StatusMessageColor = color;
        HasStatusMessage = !string.IsNullOrWhiteSpace(message);
    }

    private void NotifyCommandStateChanged()
    {
        LoadCommand.NotifyCanExecuteChanged();
        CreateWorkspaceCommand.NotifyCanExecuteChanged();
        ActivateWorkspaceCommand.NotifyCanExecuteChanged();
        DeleteWorkspaceCommand.NotifyCanExecuteChanged();
        ExportWorkspaceCommand.NotifyCanExecuteChanged();
        CaptureWorkspaceCommand.NotifyCanExecuteChanged();
        RestoreSessionCommand.NotifyCanExecuteChanged();
        ImportWorkspaceFromJsonCommand.NotifyCanExecuteChanged();
    }
}
