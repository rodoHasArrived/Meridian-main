using Meridian.Ui.Services;

namespace Meridian.Wpf.ViewModels;

/// <summary>
/// ViewModel for the Schedule Manager page.
/// Owns schedule loading, refresh actions, empty/error state, and cron validation presentation.
/// </summary>
public sealed class ScheduleManagerViewModel : BindableBase
{
    private readonly IScheduleManagerClient _scheduleClient;
    private readonly AsyncRelayCommand _refreshBackfillSchedulesCommand;
    private readonly AsyncRelayCommand _refreshMaintenanceSchedulesCommand;
    private readonly AsyncRelayCommand _refreshTemplatesCommand;
    private readonly AsyncRelayCommand _validateCronCommand;
    private bool _isBackfillSchedulesLoading;
    private bool _isMaintenanceSchedulesLoading;
    private bool _isTemplatesLoading;
    private bool _isValidatingCron;
    private string _backfillSchedulesStatus = "Loading schedules...";
    private string _maintenanceSchedulesStatus = "Loading schedules...";
    private string _templatesStatus = "Loading templates...";
    private string _cronExpression = "0 0 * * 1-5";
    private string _cronValidationText = "Enter a cron expression to preview schedule validity and upcoming UTC runs.";
    private string _cronValidationTone = "Neutral";

    public ScheduleManagerViewModel(IScheduleManagerClient scheduleClient)
    {
        _scheduleClient = scheduleClient;
        _refreshBackfillSchedulesCommand = new AsyncRelayCommand(RefreshBackfillSchedulesAsync, () => !IsBackfillSchedulesLoading);
        _refreshMaintenanceSchedulesCommand = new AsyncRelayCommand(RefreshMaintenanceSchedulesAsync, () => !IsMaintenanceSchedulesLoading);
        _refreshTemplatesCommand = new AsyncRelayCommand(RefreshTemplatesAsync, () => !IsTemplatesLoading);
        _validateCronCommand = new AsyncRelayCommand(ValidateCronAsync, () => !IsValidatingCron);
    }

    public ObservableCollection<BackfillSchedule> BackfillSchedules { get; } = new();

    public ObservableCollection<MaintenanceSchedule> MaintenanceSchedules { get; } = new();

    public ObservableCollection<ScheduleTemplate> ScheduleTemplates { get; } = new();

    public IAsyncRelayCommand RefreshBackfillSchedulesCommand => _refreshBackfillSchedulesCommand;

    public IAsyncRelayCommand RefreshMaintenanceSchedulesCommand => _refreshMaintenanceSchedulesCommand;

    public IAsyncRelayCommand RefreshTemplatesCommand => _refreshTemplatesCommand;

    public IAsyncRelayCommand ValidateCronCommand => _validateCronCommand;

    public bool IsBackfillSchedulesLoading
    {
        get => _isBackfillSchedulesLoading;
        private set
        {
            if (SetProperty(ref _isBackfillSchedulesLoading, value))
            {
                _refreshBackfillSchedulesCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsMaintenanceSchedulesLoading
    {
        get => _isMaintenanceSchedulesLoading;
        private set
        {
            if (SetProperty(ref _isMaintenanceSchedulesLoading, value))
            {
                _refreshMaintenanceSchedulesCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsTemplatesLoading
    {
        get => _isTemplatesLoading;
        private set
        {
            if (SetProperty(ref _isTemplatesLoading, value))
            {
                _refreshTemplatesCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsValidatingCron
    {
        get => _isValidatingCron;
        private set
        {
            if (SetProperty(ref _isValidatingCron, value))
            {
                _validateCronCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string BackfillSchedulesStatus
    {
        get => _backfillSchedulesStatus;
        private set => SetProperty(ref _backfillSchedulesStatus, value);
    }

    public string MaintenanceSchedulesStatus
    {
        get => _maintenanceSchedulesStatus;
        private set => SetProperty(ref _maintenanceSchedulesStatus, value);
    }

    public string TemplatesStatus
    {
        get => _templatesStatus;
        private set => SetProperty(ref _templatesStatus, value);
    }

    public string CronExpression
    {
        get => _cronExpression;
        set => SetProperty(ref _cronExpression, value);
    }

    public string CronValidationText
    {
        get => _cronValidationText;
        private set => SetProperty(ref _cronValidationText, value);
    }

    public string CronValidationTone
    {
        get => _cronValidationTone;
        private set => SetProperty(ref _cronValidationTone, value);
    }

    public async Task InitializeAsync()
    {
        await RefreshBackfillSchedulesAsync();
        await RefreshMaintenanceSchedulesAsync();
        await RefreshTemplatesAsync();
    }

    public async Task RefreshBackfillSchedulesAsync()
    {
        IsBackfillSchedulesLoading = true;
        BackfillSchedulesStatus = "Loading backfill schedules...";

        try
        {
            var schedules = await _scheduleClient.GetBackfillSchedulesAsync();
            BackfillSchedules.Clear();

            if (schedules is null || schedules.Count == 0)
            {
                BackfillSchedulesStatus = "No backfill schedules configured.";
                return;
            }

            foreach (var schedule in schedules)
            {
                BackfillSchedules.Add(schedule);
            }

            BackfillSchedulesStatus = FormatCount(schedules.Count, "backfill schedule");
        }
        catch (Exception ex)
        {
            BackfillSchedulesStatus = $"Failed to load backfill schedules: {ex.Message}";
        }
        finally
        {
            IsBackfillSchedulesLoading = false;
        }
    }

    public async Task RefreshMaintenanceSchedulesAsync()
    {
        IsMaintenanceSchedulesLoading = true;
        MaintenanceSchedulesStatus = "Loading maintenance schedules...";

        try
        {
            var schedules = await _scheduleClient.GetMaintenanceSchedulesAsync();
            MaintenanceSchedules.Clear();

            if (schedules is null || schedules.Count == 0)
            {
                MaintenanceSchedulesStatus = "No maintenance schedules configured.";
                return;
            }

            foreach (var schedule in schedules)
            {
                MaintenanceSchedules.Add(schedule);
            }

            MaintenanceSchedulesStatus = FormatCount(schedules.Count, "maintenance schedule");
        }
        catch (Exception ex)
        {
            MaintenanceSchedulesStatus = $"Failed to load maintenance schedules: {ex.Message}";
        }
        finally
        {
            IsMaintenanceSchedulesLoading = false;
        }
    }

    public async Task RefreshTemplatesAsync()
    {
        IsTemplatesLoading = true;
        TemplatesStatus = "Loading schedule templates...";

        try
        {
            var templates = await _scheduleClient.GetBackfillTemplatesAsync();
            ScheduleTemplates.Clear();

            if (templates is null || templates.Count == 0)
            {
                TemplatesStatus = "No schedule templates available.";
                return;
            }

            foreach (var template in templates)
            {
                ScheduleTemplates.Add(template);
            }

            TemplatesStatus = FormatCount(templates.Count, "schedule template");
        }
        catch (Exception ex)
        {
            TemplatesStatus = $"Failed to load schedule templates: {ex.Message}";
        }
        finally
        {
            IsTemplatesLoading = false;
        }
    }

    public async Task ValidateCronAsync()
    {
        var expression = CronExpression.Trim();
        if (string.IsNullOrEmpty(expression))
        {
            CronValidationTone = "Warning";
            CronValidationText = "Enter a cron expression before validating.";
            return;
        }

        IsValidatingCron = true;
        CronValidationTone = "Neutral";
        CronValidationText = "Validating cron expression...";

        try
        {
            var result = await _scheduleClient.ValidateCronExpressionAsync(expression);
            if (result is null)
            {
                CronValidationTone = "Warning";
                CronValidationText = "Could not validate expression. Backend may be unavailable.";
                return;
            }

            if (result.IsValid)
            {
                CronValidationTone = "Success";
                CronValidationText = BuildValidCronText(result);
            }
            else
            {
                CronValidationTone = "Error";
                CronValidationText = $"Invalid: {result.ErrorMessage ?? "The expression was rejected."}";
            }
        }
        catch (Exception ex)
        {
            CronValidationTone = "Error";
            CronValidationText = $"Validation failed: {ex.Message}";
        }
        finally
        {
            IsValidatingCron = false;
        }
    }

    private static string BuildValidCronText(CronValidationResult result)
    {
        var description = string.IsNullOrWhiteSpace(result.Description)
            ? "Expression is valid."
            : result.Description.Trim();

        if (result.NextRuns.Count == 0)
        {
            return $"Valid: {description}";
        }

        var runs = string.Join(Environment.NewLine, result.NextRuns.Select(run => $"  {run:yyyy-MM-dd HH:mm:ss} UTC"));
        return $"Valid: {description}{Environment.NewLine}Next runs:{Environment.NewLine}{runs}";
    }

    private static string FormatCount(int count, string noun)
    {
        var suffix = count == 1 ? string.Empty : "s";
        return $"{count} {noun}{suffix} found";
    }
}

public interface IScheduleManagerClient
{
    Task<List<BackfillSchedule>?> GetBackfillSchedulesAsync(CancellationToken ct = default);

    Task<List<MaintenanceSchedule>?> GetMaintenanceSchedulesAsync(CancellationToken ct = default);

    Task<List<ScheduleTemplate>?> GetBackfillTemplatesAsync(CancellationToken ct = default);

    Task<CronValidationResult?> ValidateCronExpressionAsync(string cronExpression, CancellationToken ct = default);
}

public sealed class ScheduleManagerClient : IScheduleManagerClient
{
    private readonly ScheduleManagerService _scheduleService;

    public ScheduleManagerClient(ScheduleManagerService scheduleService)
    {
        _scheduleService = scheduleService;
    }

    public Task<List<BackfillSchedule>?> GetBackfillSchedulesAsync(CancellationToken ct = default) =>
        _scheduleService.GetBackfillSchedulesAsync(ct);

    public Task<List<MaintenanceSchedule>?> GetMaintenanceSchedulesAsync(CancellationToken ct = default) =>
        _scheduleService.GetMaintenanceSchedulesAsync(ct);

    public Task<List<ScheduleTemplate>?> GetBackfillTemplatesAsync(CancellationToken ct = default) =>
        _scheduleService.GetBackfillTemplatesAsync(ct);

    public Task<CronValidationResult?> ValidateCronExpressionAsync(string cronExpression, CancellationToken ct = default) =>
        _scheduleService.ValidateCronExpressionAsync(cronExpression, ct);
}
