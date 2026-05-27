using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Meridian.Ui.Services;

namespace Meridian.Wpf.ViewModels;

/// <summary>
/// View model for the Retention Assurance cleanup flow. The page remains responsible
/// for WPF-only list wiring and file dialogs while cleanup readiness, validation,
/// busy state, and destructive-action confirmation stay testable here.
/// </summary>
public sealed class RetentionAssuranceViewModel : BindableBase
{
    private static readonly SolidColorBrush ErrorBrush = CreateBrush(Color.FromRgb(248, 81, 73));
    private static readonly SolidColorBrush WarningBrush = CreateBrush(Color.FromRgb(210, 153, 34));
    private static readonly SolidColorBrush SuccessBrush = CreateBrush(Color.FromRgb(63, 185, 80));

    private readonly IRetentionAssuranceClient _client;
    private readonly AsyncRelayCommand _saveGuardrailsCommand;
    private readonly RelayCommand _validatePolicyCommand;
    private readonly AsyncRelayCommand _dryRunCommand;
    private readonly RelayCommand _requestCleanupCommand;
    private readonly AsyncRelayCommand _confirmCleanupCommand;
    private readonly RelayCommand _cancelCleanupCommand;

    private RetentionDryRunResult? _lastDryRun;

    private string _minTickDataDaysText = "7";
    private string _minBarDataDaysText = "30";
    private string _minQuoteDataDaysText = "7";
    private string _maxDailyDeletesText = "1000";
    private bool _requireChecksumVerification = true;
    private bool _requireDryRunPreview = true;
    private bool _allowDeleteDuringTradingHours;

    private string _tickDataDaysText = "30";
    private string _barDataDaysText = "365";
    private string _quoteDataDaysText = "30";
    private string _filesPerRunText = "100";
    private bool _compressBeforeDelete = true;

    private bool _isStatusVisible;
    private string _statusText = string.Empty;
    private bool _isStatusProgressVisible;
    private bool _isResultsVisible;
    private string _resultsHeader = "Results";
    private string _resultsText = string.Empty;
    private bool _isDryRunRunning;
    private bool _isCleanupRunning;
    private bool _isCleanupConfirmationVisible;
    private bool _canExecuteCleanup;
    private string _cleanupReadinessTitle = "Dry run required";
    private string _cleanupReadinessDetail = "Validate the retention policy, then run a dry run before any cleanup can execute.";
    private string _cleanupReadinessScope = "No cleanup preview loaded.";
    private string _cleanupConfirmationTitle = "Confirm cleanup execution";
    private string _cleanupConfirmationDetail = "Review the dry-run results before permanently deleting files.";

    public RetentionAssuranceViewModel(IRetentionAssuranceClient client)
    {
        _client = client;
        _saveGuardrailsCommand = new AsyncRelayCommand(ct => SaveGuardrailsAsync(ct), CanRunPolicyAction);
        _validatePolicyCommand = new RelayCommand(ValidatePolicy, CanRunPolicyAction);
        _dryRunCommand = new AsyncRelayCommand(ct => DryRunAsync(ct), CanRunPolicyAction);
        _requestCleanupCommand = new RelayCommand(RequestCleanup, () => CanExecuteCleanup && !IsCleanupBusy);
        _confirmCleanupCommand = new AsyncRelayCommand(ct => ConfirmCleanupAsync(ct), () =>
            CanExecuteCleanup && IsCleanupConfirmationVisible && !IsCleanupBusy);
        _cancelCleanupCommand = new RelayCommand(CancelCleanup, () => IsCleanupConfirmationVisible && !IsCleanupBusy);
    }

    public event EventHandler? CleanupCompleted;

    public ObservableCollection<RetentionResultItem> Results { get; } = new();

    public IAsyncRelayCommand SaveGuardrailsCommand => _saveGuardrailsCommand;

    public IRelayCommand ValidatePolicyCommand => _validatePolicyCommand;

    public IAsyncRelayCommand DryRunCommand => _dryRunCommand;

    public IRelayCommand RequestCleanupCommand => _requestCleanupCommand;

    public IAsyncRelayCommand ConfirmCleanupCommand => _confirmCleanupCommand;

    public IRelayCommand CancelCleanupCommand => _cancelCleanupCommand;

    public string MinTickDataDaysText
    {
        get => _minTickDataDaysText;
        set => SetProperty(ref _minTickDataDaysText, value);
    }

    public string MinBarDataDaysText
    {
        get => _minBarDataDaysText;
        set => SetProperty(ref _minBarDataDaysText, value);
    }

    public string MinQuoteDataDaysText
    {
        get => _minQuoteDataDaysText;
        set => SetProperty(ref _minQuoteDataDaysText, value);
    }

    public string MaxDailyDeletesText
    {
        get => _maxDailyDeletesText;
        set => SetProperty(ref _maxDailyDeletesText, value);
    }

    public bool RequireChecksumVerification
    {
        get => _requireChecksumVerification;
        set => SetProperty(ref _requireChecksumVerification, value);
    }

    public bool RequireDryRunPreview
    {
        get => _requireDryRunPreview;
        set => SetProperty(ref _requireDryRunPreview, value);
    }

    public bool AllowDeleteDuringTradingHours
    {
        get => _allowDeleteDuringTradingHours;
        set => SetProperty(ref _allowDeleteDuringTradingHours, value);
    }

    public string TickDataDaysText
    {
        get => _tickDataDaysText;
        set
        {
            if (SetProperty(ref _tickDataDaysText, value))
                InvalidateDryRunPreview();
        }
    }

    public string BarDataDaysText
    {
        get => _barDataDaysText;
        set
        {
            if (SetProperty(ref _barDataDaysText, value))
                InvalidateDryRunPreview();
        }
    }

    public string QuoteDataDaysText
    {
        get => _quoteDataDaysText;
        set
        {
            if (SetProperty(ref _quoteDataDaysText, value))
                InvalidateDryRunPreview();
        }
    }

    public string FilesPerRunText
    {
        get => _filesPerRunText;
        set
        {
            if (SetProperty(ref _filesPerRunText, value))
                InvalidateDryRunPreview();
        }
    }

    public bool CompressBeforeDelete
    {
        get => _compressBeforeDelete;
        set
        {
            if (SetProperty(ref _compressBeforeDelete, value))
                InvalidateDryRunPreview();
        }
    }

    public bool IsStatusVisible
    {
        get => _isStatusVisible;
        private set
        {
            if (SetProperty(ref _isStatusVisible, value))
                RaisePropertyChanged(nameof(StatusVisibility));
        }
    }

    public Visibility StatusVisibility => IsStatusVisible ? Visibility.Visible : Visibility.Collapsed;

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public bool IsStatusProgressVisible
    {
        get => _isStatusProgressVisible;
        private set
        {
            if (SetProperty(ref _isStatusProgressVisible, value))
                RaisePropertyChanged(nameof(StatusProgressVisibility));
        }
    }

    public Visibility StatusProgressVisibility => IsStatusProgressVisible ? Visibility.Visible : Visibility.Collapsed;

    public bool IsResultsVisible
    {
        get => _isResultsVisible;
        private set
        {
            if (SetProperty(ref _isResultsVisible, value))
                RaisePropertyChanged(nameof(ResultsVisibility));
        }
    }

    public Visibility ResultsVisibility => IsResultsVisible ? Visibility.Visible : Visibility.Collapsed;

    public string ResultsHeader
    {
        get => _resultsHeader;
        private set => SetProperty(ref _resultsHeader, value);
    }

    public string ResultsText
    {
        get => _resultsText;
        private set => SetProperty(ref _resultsText, value);
    }

    public Visibility ResultsListVisibility => Results.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

    public bool IsDryRunRunning
    {
        get => _isDryRunRunning;
        private set
        {
            if (SetProperty(ref _isDryRunRunning, value))
                RefreshCommandStates();
        }
    }

    public bool IsCleanupBusy
    {
        get => _isCleanupRunning;
        private set
        {
            if (SetProperty(ref _isCleanupRunning, value))
                RefreshCommandStates();
        }
    }

    public bool IsCleanupConfirmationVisible
    {
        get => _isCleanupConfirmationVisible;
        private set
        {
            if (SetProperty(ref _isCleanupConfirmationVisible, value))
            {
                RaisePropertyChanged(nameof(CleanupConfirmationVisibility));
                RefreshCommandStates();
            }
        }
    }

    public Visibility CleanupConfirmationVisibility =>
        IsCleanupConfirmationVisible ? Visibility.Visible : Visibility.Collapsed;

    public bool CanExecuteCleanup
    {
        get => _canExecuteCleanup;
        private set
        {
            if (SetProperty(ref _canExecuteCleanup, value))
                RefreshCommandStates();
        }
    }

    public string CleanupReadinessTitle
    {
        get => _cleanupReadinessTitle;
        private set => SetProperty(ref _cleanupReadinessTitle, value);
    }

    public string CleanupReadinessDetail
    {
        get => _cleanupReadinessDetail;
        private set => SetProperty(ref _cleanupReadinessDetail, value);
    }

    public string CleanupReadinessScope
    {
        get => _cleanupReadinessScope;
        private set => SetProperty(ref _cleanupReadinessScope, value);
    }

    public string CleanupConfirmationTitle
    {
        get => _cleanupConfirmationTitle;
        private set => SetProperty(ref _cleanupConfirmationTitle, value);
    }

    public string CleanupConfirmationDetail
    {
        get => _cleanupConfirmationDetail;
        private set => SetProperty(ref _cleanupConfirmationDetail, value);
    }

    public void LoadConfiguration(RetentionConfiguration configuration)
    {
        var guardrails = configuration.Guardrails ?? new RetentionGuardrails();
        MinTickDataDaysText = guardrails.MinTickDataDays.ToString(CultureInfo.InvariantCulture);
        MinBarDataDaysText = guardrails.MinBarDataDays.ToString(CultureInfo.InvariantCulture);
        MinQuoteDataDaysText = guardrails.MinQuoteDataDays.ToString(CultureInfo.InvariantCulture);
        MaxDailyDeletesText = guardrails.MaxDailyDeletedFiles.ToString(CultureInfo.InvariantCulture);
        RequireChecksumVerification = guardrails.RequireChecksumVerification;
        RequireDryRunPreview = guardrails.RequireDryRunPreview;
        AllowDeleteDuringTradingHours = guardrails.AllowDeleteDuringTradingHours;
    }

    public async Task SaveGuardrailsAsync(CancellationToken ct = default)
    {
        if (!TryBuildGuardrails(out var guardrails, out var error))
        {
            ShowStatus($"Guardrails not saved. {error}", showProgress: false);
            return;
        }

        try
        {
            var configuration = _client.Configuration;
            configuration.Guardrails = guardrails;
            await _client.SaveConfigurationAsync(ct);
            ShowStatus("Guardrails saved successfully.", showProgress: false);
        }
        catch (Exception ex)
        {
            ShowStatus($"Failed to save guardrails: {ex.Message}", showProgress: false);
        }
    }

    public void ValidatePolicy()
    {
        if (!TryBuildPolicy(out var policy, out var error))
        {
            ApplyInputError(error);
            return;
        }

        try
        {
            var result = _client.ValidateRetentionPolicy(policy);
            ApplyValidationResult(result);
        }
        catch (Exception ex)
        {
            ApplyInputError($"Policy validation failed: {ex.Message}");
        }
    }

    public async Task DryRunAsync(CancellationToken ct = default)
    {
        if (!TryBuildPolicy(out var policy, out var error))
        {
            ApplyInputError(error);
            return;
        }

        IsDryRunRunning = true;
        IsCleanupConfirmationVisible = false;
        ShowStatus("Running retention dry run...", showProgress: true);
        CleanupReadinessTitle = "Dry run in progress";
        CleanupReadinessDetail = "Scanning retention candidates while preserving legal holds and guardrails.";
        CleanupReadinessScope = BuildPolicyScope(policy);

        try
        {
            var dryRun = await _client.PerformDryRunAsync(policy, ct);
            ApplyDryRunResult(dryRun);
            IsStatusVisible = false;
        }
        catch (Exception ex)
        {
            _lastDryRun = null;
            CanExecuteCleanup = false;
            ResultsHeader = "Dry Run Results";
            ResultsText = $"Dry run failed: {ex.Message}";
            ClearResults();
            AddResult("ERROR", ex.Message, ErrorBrush);
            IsResultsVisible = true;
            CleanupReadinessTitle = "Dry run failed";
            CleanupReadinessDetail = ex.Message;
            CleanupReadinessScope = "Cleanup remains blocked until a dry run succeeds.";
            ShowStatus($"Dry run failed: {ex.Message}", showProgress: false);
        }
        finally
        {
            IsDryRunRunning = false;
            IsStatusProgressVisible = false;
        }
    }

    private void RequestCleanup()
    {
        if (_lastDryRun is null || _lastDryRun.FilesToDelete.Count == 0)
        {
            CleanupReadinessTitle = "Dry run required";
            CleanupReadinessDetail = "Run a dry run with deletion candidates before cleanup can execute.";
            CleanupReadinessScope = "Execution blocked.";
            return;
        }

        IsCleanupConfirmationVisible = true;
        CleanupReadinessTitle = "Confirm cleanup execution";
        CleanupReadinessDetail = "Permanent deletion is staged. Confirm only after reviewing the dry-run results.";
        CleanupReadinessScope = FormatDryRunScope(_lastDryRun);
        CleanupConfirmationTitle = "Confirm permanent deletion";
        CleanupConfirmationDetail =
            $"{_lastDryRun.FilesToDelete.Count:N0} file(s) totaling {FormatHelpers.FormatBytes(_lastDryRun.TotalBytesToDelete)} will be deleted.";
    }

    public async Task ConfirmCleanupAsync(CancellationToken ct = default)
    {
        if (_lastDryRun is null || _lastDryRun.FilesToDelete.Count == 0)
            return;

        IsCleanupBusy = true;
        IsCleanupConfirmationVisible = false;
        ShowStatus("Executing retention cleanup...", showProgress: true);
        CleanupReadinessTitle = "Executing cleanup";
        CleanupReadinessDetail = "Deleting the staged files and writing the retention audit report.";
        CleanupReadinessScope = FormatDryRunScope(_lastDryRun);

        try
        {
            var report = await _client.ExecuteRetentionCleanupAsync(_lastDryRun, RequireChecksumVerification, ct);
            ApplyCleanupReport(report);
            CleanupCompleted?.Invoke(this, EventArgs.Empty);
            IsStatusVisible = false;
        }
        catch (Exception ex)
        {
            CleanupReadinessTitle = "Cleanup failed";
            CleanupReadinessDetail = ex.Message;
            CleanupReadinessScope = _lastDryRun is null ? "No dry run staged." : FormatDryRunScope(_lastDryRun);
            ShowStatus($"Cleanup failed: {ex.Message}", showProgress: false);
        }
        finally
        {
            IsCleanupBusy = false;
            IsStatusProgressVisible = false;
        }
    }

    private void CancelCleanup()
    {
        IsCleanupConfirmationVisible = false;
        CleanupReadinessTitle = "Cleanup preview ready";
        CleanupReadinessDetail = "Execution was cancelled. Review the staged files or run a fresh dry run.";
        CleanupReadinessScope = _lastDryRun is null ? "No cleanup preview loaded." : FormatDryRunScope(_lastDryRun);
    }

    private void ApplyValidationResult(RetentionValidationResult result)
    {
        _lastDryRun = null;
        CanExecuteCleanup = false;
        IsCleanupConfirmationVisible = false;

        ClearResults();
        foreach (var violation in result.Violations)
            AddResult("ERROR", violation.Message, ErrorBrush);
        foreach (var warning in result.Warnings)
            AddResult("WARN", warning.Message, WarningBrush);

        ResultsHeader = "Validation Results";
        IsResultsVisible = true;

        if (result.IsValid && Results.Count == 0)
        {
            ResultsText = "Policy is valid. Run a dry run to stage cleanup candidates before deletion.";
            CleanupReadinessTitle = "Policy valid";
            CleanupReadinessDetail = "No guardrail issues were found. Run a dry run to inspect candidate files.";
            CleanupReadinessScope = "Cleanup still blocked until dry run.";
        }
        else if (result.IsValid)
        {
            ResultsText = $"Policy is valid with {Results.Count:N0} warning(s).";
            CleanupReadinessTitle = "Policy valid with warnings";
            CleanupReadinessDetail = "Review warnings, then run a dry run to stage cleanup candidates.";
            CleanupReadinessScope = "Cleanup still blocked until dry run.";
        }
        else
        {
            ResultsText = $"Policy has {result.Violations.Count:N0} violation(s) and {result.Warnings.Count:N0} warning(s).";
            CleanupReadinessTitle = "Policy blocked by guardrails";
            CleanupReadinessDetail = "Fix guardrail violations before running a cleanup dry run.";
            CleanupReadinessScope = $"{result.Violations.Count:N0} violation(s) blocking cleanup.";
        }

        RaisePropertyChanged(nameof(ResultsListVisibility));
    }

    private void ApplyDryRunResult(RetentionDryRunResult dryRun)
    {
        ClearResults();
        ResultsHeader = "Dry Run Results";
        IsResultsVisible = true;

        foreach (var error in dryRun.Errors)
            AddResult("ERROR", error, ErrorBrush);

        if (dryRun.SkippedFiles.Count > 0)
        {
            AddResult(
                "WARN",
                $"{dryRun.SkippedFiles.Count:N0} file(s) skipped because legal holds or guardrails protected them.",
                WarningBrush);
        }

        if (dryRun.Errors.Count > 0)
        {
            _lastDryRun = null;
            CanExecuteCleanup = false;
            ResultsText = $"Dry run completed with {dryRun.Errors.Count:N0} error(s). Cleanup remains blocked.";
            CleanupReadinessTitle = "Dry run needs attention";
            CleanupReadinessDetail = "Resolve dry-run errors before cleanup can be staged.";
            CleanupReadinessScope = "Cleanup blocked.";
        }
        else if (dryRun.FilesToDelete.Count == 0)
        {
            _lastDryRun = null;
            CanExecuteCleanup = false;
            ResultsText = $"No files matched the retention policy. Skipped: {dryRun.SkippedFiles.Count:N0} file(s).";
            CleanupReadinessTitle = "No cleanup candidates";
            CleanupReadinessDetail = "The dry run completed and found no files eligible for deletion.";
            CleanupReadinessScope = "0 files staged.";
        }
        else
        {
            _lastDryRun = dryRun;
            CanExecuteCleanup = true;
            ResultsText =
                $"Found {dryRun.FilesToDelete.Count:N0} file(s) to delete ({FormatHelpers.FormatBytes(dryRun.TotalBytesToDelete)}).\n" +
                $"Skipped: {dryRun.SkippedFiles.Count:N0} file(s).\n" +
                $"Symbols affected: {dryRun.BySymbol.Count:N0}.";
            CleanupReadinessTitle = "Cleanup preview ready";
            CleanupReadinessDetail = "Review dry-run results, then confirm cleanup execution explicitly.";
            CleanupReadinessScope = FormatDryRunScope(dryRun);
        }

        RaisePropertyChanged(nameof(ResultsListVisibility));
    }

    private void ApplyCleanupReport(RetentionAuditReport report)
    {
        ClearResults();
        ResultsHeader = "Cleanup Results";
        IsResultsVisible = true;

        foreach (var error in report.Errors)
            AddResult("ERROR", error, ErrorBrush);
        foreach (var note in report.Notes)
            AddResult("NOTE", note, SuccessBrush);

        ResultsText =
            $"Status: {report.Status}\n" +
            $"Files deleted: {report.DeletedFiles.Count:N0}\n" +
            $"Bytes freed: {FormatHelpers.FormatBytes(report.ActualBytesDeleted)}";

        _lastDryRun = null;
        CanExecuteCleanup = false;
        IsCleanupConfirmationVisible = false;
        CleanupReadinessTitle = report.Status is CleanupStatus.Success or CleanupStatus.PartialSuccess
            ? "Cleanup audit recorded"
            : "Cleanup finished with attention needed";
        CleanupReadinessDetail =
            $"{report.DeletedFiles.Count:N0} file(s) deleted and {FormatHelpers.FormatBytes(report.ActualBytesDeleted)} freed.";
        CleanupReadinessScope = "Run a new dry run before the next cleanup.";
        RaisePropertyChanged(nameof(ResultsListVisibility));
    }

    private bool TryBuildGuardrails(out RetentionGuardrails guardrails, out string error)
    {
        guardrails = new RetentionGuardrails();

        if (!TryParsePositiveInt(MinTickDataDaysText, "minimum tick retention", out var minTick, out error) ||
            !TryParsePositiveInt(MinBarDataDaysText, "minimum bar retention", out var minBar, out error) ||
            !TryParsePositiveInt(MinQuoteDataDaysText, "minimum quote retention", out var minQuote, out error) ||
            !TryParsePositiveInt(MaxDailyDeletesText, "maximum daily deletions", out var maxDeletes, out error))
        {
            return false;
        }

        guardrails.MinTickDataDays = minTick;
        guardrails.MinBarDataDays = minBar;
        guardrails.MinQuoteDataDays = minQuote;
        guardrails.MaxDailyDeletedFiles = maxDeletes;
        guardrails.RequireChecksumVerification = RequireChecksumVerification;
        guardrails.RequireDryRunPreview = RequireDryRunPreview;
        guardrails.AllowDeleteDuringTradingHours = AllowDeleteDuringTradingHours;
        return true;
    }

    private bool TryBuildPolicy(out RetentionPolicy policy, out string error)
    {
        policy = new RetentionPolicy();

        if (!TryParsePositiveInt(TickDataDaysText, "tick data retention", out var tick, out error) ||
            !TryParsePositiveInt(BarDataDaysText, "bar data retention", out var bar, out error) ||
            !TryParsePositiveInt(QuoteDataDaysText, "quote data retention", out var quote, out error) ||
            !TryParsePositiveInt(FilesPerRunText, "files per cleanup run", out var files, out error))
        {
            return false;
        }

        policy.TickDataDays = tick;
        policy.BarDataDays = bar;
        policy.QuoteDataDays = quote;
        policy.DeletedFilesPerRun = files;
        policy.CompressBeforeDelete = CompressBeforeDelete;
        return true;
    }

    private static bool TryParsePositiveInt(string value, string label, out int parsed, out string error)
    {
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed) || parsed <= 0)
        {
            error = $"Enter a positive whole number for {label}.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private void ApplyInputError(string error)
    {
        _lastDryRun = null;
        CanExecuteCleanup = false;
        IsCleanupConfirmationVisible = false;
        ResultsHeader = "Policy Input Required";
        ResultsText = error;
        ClearResults();
        AddResult("ERROR", error, ErrorBrush);
        IsResultsVisible = true;
        CleanupReadinessTitle = "Fix policy inputs";
        CleanupReadinessDetail = error;
        CleanupReadinessScope = "Cleanup blocked until inputs are valid.";
        RaisePropertyChanged(nameof(ResultsListVisibility));
    }

    private void InvalidateDryRunPreview()
    {
        if (_lastDryRun is null && !CanExecuteCleanup && !IsCleanupConfirmationVisible)
            return;

        _lastDryRun = null;
        CanExecuteCleanup = false;
        IsCleanupConfirmationVisible = false;
        CleanupReadinessTitle = "Dry run required";
        CleanupReadinessDetail = "Policy inputs changed after the last preview. Run a fresh dry run before cleanup.";
        CleanupReadinessScope = "Previous cleanup preview cleared.";
    }

    private void ClearResults()
    {
        Results.Clear();
        RaisePropertyChanged(nameof(ResultsListVisibility));
    }

    private void AddResult(string severity, string message, Brush brush)
    {
        Results.Add(new RetentionResultItem
        {
            Severity = severity,
            Message = message,
            SeverityColor = brush
        });
    }

    private void ShowStatus(string message, bool showProgress)
    {
        StatusText = message;
        IsStatusProgressVisible = showProgress;
        IsStatusVisible = true;
    }

    private bool CanRunPolicyAction() => !IsDryRunRunning && !IsCleanupBusy;

    private void RefreshCommandStates()
    {
        _saveGuardrailsCommand.NotifyCanExecuteChanged();
        _validatePolicyCommand.NotifyCanExecuteChanged();
        _dryRunCommand.NotifyCanExecuteChanged();
        _requestCleanupCommand.NotifyCanExecuteChanged();
        _confirmCleanupCommand.NotifyCanExecuteChanged();
        _cancelCleanupCommand.NotifyCanExecuteChanged();
    }

    private static string BuildPolicyScope(RetentionPolicy policy) =>
        $"Tick {policy.TickDataDays}d | Bar {policy.BarDataDays}d | Quote {policy.QuoteDataDays}d | {policy.DeletedFilesPerRun:N0} file cap";

    private static string FormatDryRunScope(RetentionDryRunResult dryRun) =>
        $"{dryRun.FilesToDelete.Count:N0} file(s) staged | {FormatHelpers.FormatBytes(dryRun.TotalBytesToDelete)} | {dryRun.BySymbol.Count:N0} symbol(s)";

    private static SolidColorBrush CreateBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}

public interface IRetentionAssuranceClient
{
    RetentionConfiguration Configuration { get; }

    Task SaveConfigurationAsync(CancellationToken ct = default);

    RetentionValidationResult ValidateRetentionPolicy(RetentionPolicy policy);

    Task<RetentionDryRunResult> PerformDryRunAsync(RetentionPolicy policy, CancellationToken ct = default);

    Task<RetentionAuditReport> ExecuteRetentionCleanupAsync(
        RetentionDryRunResult dryRun,
        bool verifyChecksums,
        CancellationToken ct = default);
}

public sealed class RetentionResultItem
{
    public string Severity { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public Brush SeverityColor { get; set; } = Brushes.Gray;
}
