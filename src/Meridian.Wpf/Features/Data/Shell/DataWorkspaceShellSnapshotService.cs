using Meridian.Contracts.Api;
using Meridian.Contracts.Session;
using Meridian.Ui.Services;
using Meridian.Ui.Services.Services;
using Meridian.Wpf.Copy;
using Meridian.Wpf.Models;
using Meridian.Wpf.Services;
using WpfNotificationService = Meridian.Wpf.Services.NotificationService;
using WpfLoggingService = Meridian.Wpf.Services.LoggingService;
using ProviderInfoModel = Meridian.Ui.Services.Services.ProviderInfo;
using StatusProviderInfoModel = Meridian.Ui.Services.Services.StatusProviderInfo;

namespace Meridian.Wpf.Features.Data.Shell;

public interface IDataWorkspaceShellSnapshotService
{
    Task<DataOperationsWorkspaceData> LoadAsync(CancellationToken cancellationToken = default);
}

public sealed class DataWorkspaceShellSnapshotService : IDataWorkspaceShellSnapshotService, IWorkspaceScopedService
{
    private readonly WorkspaceShellContextService _shellContextService;
    private readonly WpfNotificationService _notificationService;
    private readonly WorkstationOperatingContextService? _operatingContextService;
    private readonly StatusService _statusService;
    private readonly BackfillApiService _backfillApiService;
    private readonly BackfillCheckpointService _backfillCheckpointService;
    private readonly StorageService _storageService;
    private readonly CollectionSessionService _collectionSessionService;
    private readonly ScheduleManagerService _scheduleManagerService;
    private readonly BatchExportSchedulerService _exportSchedulerService;
    private readonly FixtureModeDetector _fixtureModeDetector;

    public DataWorkspaceShellSnapshotService(
        WorkspaceShellContextService shellContextService,
        WpfNotificationService notificationService,
        StatusService statusService,
        BackfillApiService backfillApiService,
        BackfillCheckpointService backfillCheckpointService,
        StorageService storageService,
        CollectionSessionService collectionSessionService,
        ScheduleManagerService scheduleManagerService,
        BatchExportSchedulerService exportSchedulerService,
        FixtureModeDetector fixtureModeDetector,
        WorkstationOperatingContextService? operatingContextService = null)
    {
        _shellContextService = shellContextService ?? throw new ArgumentNullException(nameof(shellContextService));
        _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
        _statusService = statusService ?? throw new ArgumentNullException(nameof(statusService));
        _backfillApiService = backfillApiService ?? throw new ArgumentNullException(nameof(backfillApiService));
        _backfillCheckpointService = backfillCheckpointService ?? throw new ArgumentNullException(nameof(backfillCheckpointService));
        _storageService = storageService ?? throw new ArgumentNullException(nameof(storageService));
        _collectionSessionService = collectionSessionService ?? throw new ArgumentNullException(nameof(collectionSessionService));
        _scheduleManagerService = scheduleManagerService ?? throw new ArgumentNullException(nameof(scheduleManagerService));
        _exportSchedulerService = exportSchedulerService ?? throw new ArgumentNullException(nameof(exportSchedulerService));
        _fixtureModeDetector = fixtureModeDetector ?? throw new ArgumentNullException(nameof(fixtureModeDetector));
        _operatingContextService = operatingContextService;
    }

    public async Task<DataOperationsWorkspaceData> LoadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var unreadAlerts = _shellContextService.GetUnreadAlertCount();
        var notifications = _notificationService.GetHistory().Take(4).ToArray();
        var operatingContext = _operatingContextService?.CurrentContext;
        var scopeLabel = operatingContext is null
            ? WorkspaceCopyCatalog.DataOperations.DefaultScopeLabel
            : $"{operatingContext.ScopeKind.ToDisplayName()} · {operatingContext.DisplayName}";
        var scopeSummary = operatingContext is null
            ? WorkspaceCopyCatalog.DataOperations.DefaultScopeSummary
            : $"Route providers, backfills, storage, and export jobs for {operatingContext.DisplayName} without leaving the shell.";

        var providersTask = LoadSafeAsync("provider catalog", async () => (await _statusService.GetAvailableProvidersAsync()).ToArray(), Array.Empty<ProviderInfoModel>());
        var providerStatusTask = LoadSafeAsync("provider status", () => _statusService.GetProviderStatusAsync(), default(StatusProviderInfoModel));
        var backfillHealthTask = LoadSafeAsync("backfill health", () => _backfillApiService.CheckProviderHealthAsync(), default(BackfillHealthResponse));
        var lastBackfillTask = LoadSafeAsync("backfill status", () => _backfillApiService.GetLastStatusAsync(), default(BackfillResultDto));
        var backfillExecutionsTask = LoadSafeAsync("backfill executions", async () => (await _backfillApiService.GetExecutionHistoryAsync(limit: 6)).ToArray(), Array.Empty<BackfillExecution>());
        var resumableJobsTask = LoadSafeAsync("backfill checkpoints", async () => (await _backfillCheckpointService.GetResumableJobsAsync()).ToArray(), Array.Empty<BackfillCheckpoint>());
        var backfillSchedulesTask = LoadSafeAsync("backfill schedules", async () => (await _scheduleManagerService.GetBackfillSchedulesAsync())?.ToArray() ?? Array.Empty<BackfillSchedule>(), Array.Empty<BackfillSchedule>());
        var storageStatsTask = LoadSafeAsync("storage stats", () => _storageService.GetStorageStatsAsync(), default(StorageStatsSummary));
        var storageHealthTask = LoadSafeAsync("storage health", () => _storageService.GetStorageHealthAsync(), default(StorageHealthReport));
        var activeSessionTask = LoadSafeAsync("active session", () => _collectionSessionService.GetActiveSessionAsync(), default(CollectionSession));
        var sessionsTask = LoadSafeAsync("session history", async () => (await _collectionSessionService.GetSessionsAsync()).ToArray(), Array.Empty<CollectionSession>());
        var exportJobsTask = LoadSafeAsync("export jobs", async () => (await _exportSchedulerService.ReadPersistedJobsAsync()).ToArray(), Array.Empty<ExportJob>());

        await Task.WhenAll(
            providersTask,
            providerStatusTask,
            backfillHealthTask,
            lastBackfillTask,
            backfillExecutionsTask,
            resumableJobsTask,
            backfillSchedulesTask,
            storageStatsTask,
            storageHealthTask,
            activeSessionTask,
            sessionsTask,
            exportJobsTask);

        cancellationToken.ThrowIfCancellationRequested();

        return new DataOperationsWorkspaceData
        {
            EnvironmentMode = _fixtureModeDetector.ModeKind,
            ScopeLabel = scopeLabel,
            ScopeSummary = scopeSummary,
            RetrievedAt = DateTimeOffset.Now,
            UnreadAlerts = unreadAlerts,
            Notifications = notifications,
            Providers = await providersTask,
            ProviderStatus = await providerStatusTask,
            BackfillHealth = await backfillHealthTask,
            LastBackfillStatus = await lastBackfillTask,
            BackfillExecutions = await backfillExecutionsTask,
            ResumableJobs = await resumableJobsTask,
            BackfillSchedules = await backfillSchedulesTask,
            StorageStats = await storageStatsTask,
            StorageHealth = await storageHealthTask,
            ActiveSession = await activeSessionTask,
            Sessions = await sessionsTask,
            ExportJobs = await exportJobsTask
        };
    }

    private static async Task<T> LoadSafeAsync<T>(string operationName, Func<Task<T>> loader, T fallback)
    {
        try
        {
            var result = await loader();
            return result is null ? fallback : result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            WpfLoggingService.Instance.LogWarning($"[DataWorkspaceShell] {operationName} unavailable: {ex.Message}");
            return fallback;
        }
    }
}
