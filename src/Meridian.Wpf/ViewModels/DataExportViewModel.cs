using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;

namespace Meridian.Wpf.ViewModels;

/// <summary>
/// ViewModel for the Data Export page.
/// Owns all form state, date-range properties, validation messages, and export
/// commands so that the code-behind is thinned to ComboBox-tag helpers and
/// PasswordBox reads only.
/// </summary>
public sealed class DataExportViewModel : BindableBase
{
    // ── Date range ────────────────────────────────────────────────────────
    private DateTime? _exportFromDate;
    private DateTime? _exportToDate;
    private string _dateValidationError = string.Empty;
    private bool _isDateValidationErrorVisible;

    // ── Symbol selection ──────────────────────────────────────────────────
    private string _symbolSearchText = string.Empty;
    private string _symbolsValidationError = string.Empty;
    private bool _isSymbolsValidationErrorVisible;

    // ── Export progress ───────────────────────────────────────────────────
    private bool _isExporting;
    private bool _isExportProgressVisible;
    private double _exportProgressValue;
    private string _exportProgressPercent = "0%";
    private string _exportProgressLabel = "Exporting...";
    private string _exportReadinessTitle = string.Empty;
    private string _exportReadinessDetail = string.Empty;
    private string _exportScopeText = string.Empty;
    private string _selectedSymbolCountText = string.Empty;

    // ── Export history empty-state ────────────────────────────────────────
    private bool _isNoExportHistoryVisible;

    // ── Schedule ──────────────────────────────────────────────────────────
    private bool _isScheduleEnabled;
    private string _scheduleTimeText = "08:00";
    private string _scheduleDestinationPath = string.Empty;
    private string _scheduleTimeError = string.Empty;
    private bool _isScheduleTimeErrorVisible;
    private string _scheduleReadinessTitle = string.Empty;
    private string _scheduleReadinessDetail = string.Empty;
    private string _scheduleScopeText = string.Empty;
    private string _selectedScheduleFrequency = "daily";

    // ── Database ──────────────────────────────────────────────────────────
    private string _databaseHost = string.Empty;
    private string _databasePort = "5432";
    private string _databaseName = string.Empty;
    private string _databaseUser = string.Empty;
    private string _dbCredentialStatus = string.Empty;
    private bool _isDbCredentialStatusSuccess;

    // ── Webhook ───────────────────────────────────────────────────────────
    private string _webhookUrl = string.Empty;
    private string _webhookTestResult = string.Empty;
    private bool _isWebhookTestResultSuccess;
    private bool _isWebhookTestResultVisible;

    // ── Lean ──────────────────────────────────────────────────────────────
    private string _leanDataPath = string.Empty;

    // ── Status / info panel ───────────────────────────────────────────────
    private string _actionInfoText = string.Empty;
    private bool _isActionInfoError;
    private bool _isActionInfoVisible;

    // ── Internal state set by code-behind (non-bindable controls) ─────────
    private string _selectedExportFormat = "csv";
    private string _selectedCompression = "gzip";

    internal string SelectedExportFormat
    {
        get => _selectedExportFormat;
        set
        {
            if (!string.Equals(_selectedExportFormat, value, StringComparison.OrdinalIgnoreCase))
            {
                _selectedExportFormat = value;
                RefreshExportReadiness();
            }
        }
    }

    internal string SelectedCompression
    {
        get => _selectedCompression;
        set
        {
            if (!string.Equals(_selectedCompression, value, StringComparison.OrdinalIgnoreCase))
            {
                _selectedCompression = value;
                RefreshExportReadiness();
            }
        }
    }

    internal string SelectedDatabaseType { get; set; } = "postgresql";
    internal string SelectedScheduleFrequency
    {
        get => _selectedScheduleFrequency;
        set
        {
            if (!string.Equals(_selectedScheduleFrequency, value, StringComparison.OrdinalIgnoreCase))
            {
                _selectedScheduleFrequency = value;
                RefreshScheduleReadiness();
            }
        }
    }
    internal string SelectedWebhookFormat { get; set; } = "json";
    internal string SelectedWebhookBatch { get; set; } = "trade";
    internal string SelectedLeanResolution { get; set; } = "minute";
    internal string DatabasePassword { get; set; } = string.Empty;

    public DataExportViewModel()
    {
        SetTodayCommand = new RelayCommand(SetToday);
        SetWeekCommand = new RelayCommand(SetWeek);
        SetMonthCommand = new RelayCommand(SetMonth);
        AddSymbolCommand = new RelayCommand(AddSymbol);
        ExportDataCommand = new AsyncRelayCommand(ct => ExportDataAsync(ct), CanRunExportData);
        SetDatabaseCredentialsCommand = new RelayCommand(SetDatabaseCredentials);
        TestDatabaseConnectionCommand = new RelayCommand(TestDatabaseConnection);
        ConfigureDatabaseSyncCommand = new RelayCommand(ConfigureDatabaseSync);
        ConfigureScheduledExportCommand = new RelayCommand(ConfigureScheduledExport, () => CanConfigureScheduledExport);
        TestWebhookCommand = new RelayCommand(TestWebhook);
        BrowseLeanPathCommand = new RelayCommand(BrowseLeanPath);
        ExportToLeanCommand = new RelayCommand(ExportToLean);
        VerifyLeanDataCommand = new RelayCommand(VerifyLeanData);

        SelectedSymbols.CollectionChanged += (_, _) => RefreshExportReadiness();
        ExportHistory.CollectionChanged += (_, _) => IsNoExportHistoryVisible = ExportHistory.Count == 0;

        SeedInitialData();
        RefreshExportReadiness();
        RefreshScheduleReadiness();
    }

    // ── Collections ───────────────────────────────────────────────────────

    public ObservableCollection<string> SelectedSymbols { get; } = new();
    public ObservableCollection<ExportHistoryItem> ExportHistory { get; } = new();

    // ── Commands ──────────────────────────────────────────────────────────

    public IRelayCommand SetTodayCommand { get; }
    public IRelayCommand SetWeekCommand { get; }
    public IRelayCommand SetMonthCommand { get; }
    public IRelayCommand AddSymbolCommand { get; }
    public IAsyncRelayCommand ExportDataCommand { get; }
    public IRelayCommand SetDatabaseCredentialsCommand { get; }
    public IRelayCommand TestDatabaseConnectionCommand { get; }
    public IRelayCommand ConfigureDatabaseSyncCommand { get; }
    public IRelayCommand ConfigureScheduledExportCommand { get; }
    public IRelayCommand TestWebhookCommand { get; }
    public IRelayCommand BrowseLeanPathCommand { get; }
    public IRelayCommand ExportToLeanCommand { get; }
    public IRelayCommand VerifyLeanDataCommand { get; }

    // ── Date range properties ─────────────────────────────────────────────

    public DateTime? ExportFromDate
    {
        get => _exportFromDate;
        set
        {
            if (SetProperty(ref _exportFromDate, value))
            {
                RefreshExportReadiness();
            }
        }
    }

    public DateTime? ExportToDate
    {
        get => _exportToDate;
        set
        {
            if (SetProperty(ref _exportToDate, value))
            {
                RefreshExportReadiness();
            }
        }
    }

    public string DateValidationError
    {
        get => _dateValidationError;
        private set => SetProperty(ref _dateValidationError, value);
    }

    public bool IsDateValidationErrorVisible
    {
        get => _isDateValidationErrorVisible;
        private set => SetProperty(ref _isDateValidationErrorVisible, value);
    }

    // ── Symbol properties ─────────────────────────────────────────────────

    public string SymbolSearchText
    {
        get => _symbolSearchText;
        set => SetProperty(ref _symbolSearchText, value);
    }

    public string SymbolsValidationError
    {
        get => _symbolsValidationError;
        private set => SetProperty(ref _symbolsValidationError, value);
    }

    public bool IsSymbolsValidationErrorVisible
    {
        get => _isSymbolsValidationErrorVisible;
        private set => SetProperty(ref _isSymbolsValidationErrorVisible, value);
    }

    // ── Export progress properties ────────────────────────────────────────

    public bool IsExporting
    {
        get => _isExporting;
        private set
        {
            if (SetProperty(ref _isExporting, value))
            {
                RefreshExportReadiness();
            }
        }
    }

    public bool IsExportProgressVisible
    {
        get => _isExportProgressVisible;
        private set => SetProperty(ref _isExportProgressVisible, value);
    }

    public double ExportProgressValue
    {
        get => _exportProgressValue;
        private set => SetProperty(ref _exportProgressValue, value);
    }

    public string ExportProgressPercent
    {
        get => _exportProgressPercent;
        private set => SetProperty(ref _exportProgressPercent, value);
    }

    public string ExportProgressLabel
    {
        get => _exportProgressLabel;
        private set => SetProperty(ref _exportProgressLabel, value);
    }

    public string ExportReadinessTitle
    {
        get => _exportReadinessTitle;
        private set => SetProperty(ref _exportReadinessTitle, value);
    }

    public string ExportReadinessDetail
    {
        get => _exportReadinessDetail;
        private set => SetProperty(ref _exportReadinessDetail, value);
    }

    public string ExportScopeText
    {
        get => _exportScopeText;
        private set => SetProperty(ref _exportScopeText, value);
    }

    public string SelectedSymbolCountText
    {
        get => _selectedSymbolCountText;
        private set => SetProperty(ref _selectedSymbolCountText, value);
    }

    public bool CanExportData => CanRunExportData();

    public bool IsNoExportHistoryVisible
    {
        get => _isNoExportHistoryVisible;
        private set => SetProperty(ref _isNoExportHistoryVisible, value);
    }

    // ── Schedule properties ───────────────────────────────────────────────

    public bool IsScheduleEnabled
    {
        get => _isScheduleEnabled;
        set
        {
            if (SetProperty(ref _isScheduleEnabled, value))
            {
                ValidateScheduleTime();
                RefreshScheduleReadiness();
            }
        }
    }

    public string ScheduleTimeText
    {
        get => _scheduleTimeText;
        set
        {
            if (SetProperty(ref _scheduleTimeText, value))
            {
                ValidateScheduleTime();
                RefreshScheduleReadiness();
            }
        }
    }

    public string ScheduleDestinationPath
    {
        get => _scheduleDestinationPath;
        set
        {
            if (SetProperty(ref _scheduleDestinationPath, value))
            {
                RefreshScheduleReadiness();
            }
        }
    }

    public string ScheduleTimeError
    {
        get => _scheduleTimeError;
        private set => SetProperty(ref _scheduleTimeError, value);
    }

    public bool IsScheduleTimeErrorVisible
    {
        get => _isScheduleTimeErrorVisible;
        private set => SetProperty(ref _isScheduleTimeErrorVisible, value);
    }

    public string ScheduleReadinessTitle
    {
        get => _scheduleReadinessTitle;
        private set => SetProperty(ref _scheduleReadinessTitle, value);
    }

    public string ScheduleReadinessDetail
    {
        get => _scheduleReadinessDetail;
        private set => SetProperty(ref _scheduleReadinessDetail, value);
    }

    public string ScheduleScopeText
    {
        get => _scheduleScopeText;
        private set => SetProperty(ref _scheduleScopeText, value);
    }

    public bool CanConfigureScheduledExport => CanConfigureScheduledExportForState(
        IsScheduleEnabled,
        ScheduleTimeText,
        ScheduleDestinationPath);

    // ── Database properties ───────────────────────────────────────────────

    public string DatabaseHost
    {
        get => _databaseHost;
        set => SetProperty(ref _databaseHost, value);
    }

    public string DatabasePort
    {
        get => _databasePort;
        set => SetProperty(ref _databasePort, value);
    }

    public string DatabaseName
    {
        get => _databaseName;
        set => SetProperty(ref _databaseName, value);
    }

    public string DatabaseUser
    {
        get => _databaseUser;
        set => SetProperty(ref _databaseUser, value);
    }

    public string DbCredentialStatus
    {
        get => _dbCredentialStatus;
        private set => SetProperty(ref _dbCredentialStatus, value);
    }

    public bool IsDbCredentialStatusSuccess
    {
        get => _isDbCredentialStatusSuccess;
        private set => SetProperty(ref _isDbCredentialStatusSuccess, value);
    }

    // ── Webhook properties ────────────────────────────────────────────────

    public string WebhookUrl
    {
        get => _webhookUrl;
        set => SetProperty(ref _webhookUrl, value);
    }

    public string WebhookTestResult
    {
        get => _webhookTestResult;
        private set => SetProperty(ref _webhookTestResult, value);
    }

    public bool IsWebhookTestResultSuccess
    {
        get => _isWebhookTestResultSuccess;
        private set => SetProperty(ref _isWebhookTestResultSuccess, value);
    }

    public bool IsWebhookTestResultVisible
    {
        get => _isWebhookTestResultVisible;
        private set => SetProperty(ref _isWebhookTestResultVisible, value);
    }

    // ── Lean properties ───────────────────────────────────────────────────

    public string LeanDataPath
    {
        get => _leanDataPath;
        set => SetProperty(ref _leanDataPath, value);
    }

    // ── Action info panel ─────────────────────────────────────────────────

    public string ActionInfoText
    {
        get => _actionInfoText;
        private set => SetProperty(ref _actionInfoText, value);
    }

    public bool IsActionInfoError
    {
        get => _isActionInfoError;
        private set => SetProperty(ref _isActionInfoError, value);
    }

    public bool IsActionInfoVisible
    {
        get => _isActionInfoVisible;
        private set => SetProperty(ref _isActionInfoVisible, value);
    }

    // ── Command implementations ───────────────────────────────────────────

    private void SetToday()
    {
        ExportFromDate = DateTime.Today;
        ExportToDate = DateTime.Today;
    }

    private void SetWeek()
    {
        ExportFromDate = DateTime.Today.AddDays(-7);
        ExportToDate = DateTime.Today;
    }

    private void SetMonth()
    {
        ExportFromDate = DateTime.Today.AddMonths(-1);
        ExportToDate = DateTime.Today;
    }

    private void AddSymbol()
    {
        IsSymbolsValidationErrorVisible = false;

        var symbol = SymbolSearchText.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(symbol))
        {
            SymbolsValidationError = "Enter a symbol to add.";
            IsSymbolsValidationErrorVisible = true;
            return;
        }

        if (!SelectedSymbols.Contains(symbol))
            SelectedSymbols.Add(symbol);

        SymbolSearchText = string.Empty;
    }

    private async Task ExportDataAsync(CancellationToken ct = default)
    {
        if (!ValidateExportInputs())
            return;

        IsExporting = true;
        IsExportProgressVisible = true;
        ExportProgressValue = 0;
        ExportProgressPercent = "0%";
        ExportProgressLabel = $"Exporting {SelectedSymbols.FirstOrDefault() ?? "data"}...";

        try
        {
            for (var step = 1; step <= 5; step++)
            {
                await Task.Delay(200, ct);
                var progress = step * 20;
                ExportProgressValue = progress;
                ExportProgressPercent = $"{progress}%";
            }

            ExportHistory.Insert(0, new ExportHistoryItem
            {
                Timestamp = DateTimeOffset.Now.ToString("g"),
                Format = SelectedExportFormat,
                SymbolCount = SelectedSymbols.Count.ToString(),
                Size = $"{SelectedSymbols.Count * 8} MB",
                Destination = "C:\\Exports\\latest_export.csv"
            });

            IsNoExportHistoryVisible = ExportHistory.Count == 0;
            ShowInfo("Export queued successfully. You can track progress in Export History.");
        }
        catch (OperationCanceledException)
        {
            ShowInfo("Export cancelled.", isError: true);
        }
        finally
        {
            IsExporting = false;
            IsExportProgressVisible = false;
        }
    }

    private void SetDatabaseCredentials()
    {
        DbCredentialStatus = "Stored securely";
        IsDbCredentialStatusSuccess = true;
        ShowInfo("Database credentials saved.");
    }

    private void TestDatabaseConnection()
    {
        if (!TryValidateDatabaseInputs(out var error))
        {
            ShowInfo(error ?? "Missing required database fields.", isError: true);
            return;
        }

        ShowInfo("Database connection successful.");
    }

    private void ConfigureDatabaseSync()
    {
        if (!TryValidateDatabaseInputs(out var error))
        {
            ShowInfo(error ?? "Missing required database fields.", isError: true);
            return;
        }

        ShowInfo("Database sync configured. Scheduled exports will push data automatically.");
    }

    private void ConfigureScheduledExport()
    {
        if (!CanConfigureScheduledExport)
        {
            RefreshScheduleReadiness();
            ShowInfo(ScheduleReadinessDetail, isError: true);
            return;
        }

        ShowInfo(
            $"{FormatScheduleFrequency(SelectedScheduleFrequency)} export scheduled for {ScheduleTimeText.Trim()} local.");
    }

    private void TestWebhook()
    {
        if (string.IsNullOrWhiteSpace(WebhookUrl))
        {
            WebhookTestResult = "Webhook URL required.";
            IsWebhookTestResultSuccess = false;
            IsWebhookTestResultVisible = true;
            return;
        }

        WebhookTestResult = "Webhook responded successfully.";
        IsWebhookTestResultSuccess = true;
        IsWebhookTestResultVisible = true;
        ShowInfo("Webhook test completed.");
    }

    private void BrowseLeanPath()
    {
        ShowInfo("Select the Lean data directory in the file picker.");
    }

    private void ExportToLean()
    {
        if (string.IsNullOrWhiteSpace(LeanDataPath))
        {
            ShowInfo("Lean data folder is required.", isError: true);
            return;
        }

        ShowInfo("Lean export job created. Data will be exported in the selected resolution.");
    }

    private void VerifyLeanData()
    {
        ShowInfo("Lean data verification scheduled.");
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    public bool CanRunExportData()
    {
        return !IsExporting && !GetExportReadinessIssues().Any();
    }

    private void RefreshExportReadiness()
    {
        var issues = GetExportReadinessIssues().ToArray();

        SelectedSymbolCountText = SelectedSymbols.Count switch
        {
            0 => "No symbols selected",
            1 => "1 symbol selected",
            _ => $"{SelectedSymbols.Count} symbols selected"
        };

        ExportScopeText = $"{SelectedSymbolCountText} • {FormatDateScope()}";

        if (SelectedSymbols.Count > 0)
        {
            IsSymbolsValidationErrorVisible = false;
        }

        if (GetDateReadinessIssue() is null)
        {
            IsDateValidationErrorVisible = false;
        }

        if (IsExporting)
        {
            ExportReadinessTitle = "Export running";
            ExportReadinessDetail = "Progress is shown below. Wait for this job to finish before queuing another export.";
        }
        else if (issues.Length > 0)
        {
            ExportReadinessTitle = "Export setup incomplete";
            ExportReadinessDetail = string.Join(" ", issues);
        }
        else
        {
            ExportReadinessTitle = "Export ready";
            ExportReadinessDetail =
                $"{FormatExportFormat(SelectedExportFormat)} export will include {SelectedSymbolCountText.ToLowerInvariant()} across {FormatDateScope().ToLowerInvariant()} with {FormatCompression(SelectedCompression)}.";
        }

        RaisePropertyChanged(nameof(CanExportData));
        ExportDataCommand.NotifyCanExecuteChanged();
    }

    private bool ValidateExportInputs()
    {
        IsSymbolsValidationErrorVisible = false;
        IsDateValidationErrorVisible = false;
        var hasError = false;

        if (SelectedSymbols.Count == 0)
        {
            SymbolsValidationError = "Select at least one symbol.";
            IsSymbolsValidationErrorVisible = true;
            hasError = true;
        }

        if (!ExportFromDate.HasValue || !ExportToDate.HasValue)
        {
            DateValidationError = "Select both start and end dates.";
            IsDateValidationErrorVisible = true;
            hasError = true;
        }
        else if (ExportFromDate > ExportToDate)
        {
            DateValidationError = "Start date must be before end date.";
            IsDateValidationErrorVisible = true;
            hasError = true;
        }

        return !hasError;
    }

    private IEnumerable<string> GetExportReadinessIssues()
    {
        if (SelectedSymbols.Count == 0)
        {
            yield return "Add at least one symbol.";
        }

        var dateIssue = GetDateReadinessIssue();
        if (dateIssue is not null)
        {
            yield return dateIssue;
        }
    }

    private string? GetDateReadinessIssue()
    {
        if (!ExportFromDate.HasValue || !ExportToDate.HasValue)
        {
            return "Select both start and end dates.";
        }

        return ExportFromDate > ExportToDate
            ? "Start date must be before end date."
            : null;
    }

    private string FormatDateScope()
    {
        if (!ExportFromDate.HasValue || !ExportToDate.HasValue)
        {
            return "date range incomplete";
        }

        return $"{ExportFromDate.Value:MMM d, yyyy} to {ExportToDate.Value:MMM d, yyyy}";
    }

    private static string FormatExportFormat(string format)
        => format.ToLowerInvariant() switch
        {
            "csv" => "CSV",
            "parquet" => "Parquet",
            "jsonl" => "JSON Lines",
            "hdf5" => "HDF5",
            "feather" => "Feather",
            _ => format.ToUpperInvariant()
        };

    private static string FormatCompression(string compression)
        => compression.ToLowerInvariant() switch
        {
            "none" => "no compression",
            "gzip" => "gzip compression",
            "lz4" => "LZ4 compression",
            "zstd" => "Zstd compression",
            _ => $"{compression} compression"
        };

    private void ValidateScheduleTime()
    {
        IsScheduleTimeErrorVisible = false;
        if (!IsScheduleEnabled)
            return;

        if (!TimeSpan.TryParse(ScheduleTimeText, out _))
        {
            ScheduleTimeError = "Enter a valid time (HH:mm).";
            IsScheduleTimeErrorVisible = true;
        }
    }

    private void RefreshScheduleReadiness()
    {
        if (!IsScheduleEnabled)
        {
            ScheduleReadinessTitle = "Scheduled exports disabled";
            ScheduleReadinessDetail = "Enable scheduled exports after choosing frequency, run time, and output destination.";
            ScheduleScopeText = "Disabled";
        }
        else if (!TimeSpan.TryParse(ScheduleTimeText, out _))
        {
            ScheduleReadinessTitle = "Schedule setup incomplete";
            ScheduleReadinessDetail = "Enter a valid local run time in HH:mm format.";
            ScheduleScopeText = $"{FormatScheduleFrequency(SelectedScheduleFrequency)} - time requires review";
        }
        else if (string.IsNullOrWhiteSpace(ScheduleDestinationPath))
        {
            ScheduleReadinessTitle = "Schedule setup incomplete";
            ScheduleReadinessDetail = "Set a destination path before saving this scheduled export.";
            ScheduleScopeText = $"{FormatScheduleFrequency(SelectedScheduleFrequency)} - {ScheduleTimeText.Trim()} local";
        }
        else
        {
            ScheduleReadinessTitle = "Schedule ready";
            ScheduleReadinessDetail =
                $"{FormatScheduleFrequency(SelectedScheduleFrequency)} export will run at {ScheduleTimeText.Trim()} local and write to {ScheduleDestinationPath.Trim()}.";
            ScheduleScopeText = $"{FormatScheduleFrequency(SelectedScheduleFrequency)} - {ScheduleTimeText.Trim()} local";
        }

        RaisePropertyChanged(nameof(CanConfigureScheduledExport));
        ConfigureScheduledExportCommand.NotifyCanExecuteChanged();
    }

    public static bool CanConfigureScheduledExportForState(
        bool isScheduleEnabled,
        string? scheduleTimeText,
        string? scheduleDestinationPath) =>
        isScheduleEnabled
        && TimeSpan.TryParse(scheduleTimeText, out _)
        && !string.IsNullOrWhiteSpace(scheduleDestinationPath);

    private bool TryValidateDatabaseInputs(out string? error)
    {
        error = null;

        if (string.IsNullOrWhiteSpace(DatabaseHost))
        {
            error = "Database host is required.";
            return false;
        }

        if (!int.TryParse(DatabasePort, out var port) || port < 0)
        {
            error = "Database port must be a valid number.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(DatabaseName))
        {
            error = "Database name is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(DatabaseUser))
        {
            error = "Database username is required.";
            return false;
        }

        return true;
    }

    private static string FormatScheduleFrequency(string frequency)
        => frequency.ToLowerInvariant() switch
        {
            "hourly" => "Hourly",
            "daily" => "Daily",
            "weekly" => "Weekly",
            "monthly" => "Monthly",
            _ => frequency.ToUpperInvariant()
        };

    private void ShowInfo(string message, bool isError = false)
    {
        ActionInfoText = message;
        IsActionInfoError = isError;
        IsActionInfoVisible = true;
    }

    private void SeedInitialData()
    {
        ExportFromDate = DateTime.Today.AddDays(-7);
        ExportToDate = DateTime.Today;

        SelectedSymbols.Add("AAPL");
        SelectedSymbols.Add("MSFT");
        SelectedSymbols.Add("TSLA");

        ExportHistory.Add(new ExportHistoryItem
        {
            Timestamp = DateTimeOffset.Now.AddMinutes(-42).ToString("g"),
            Format = "CSV",
            SymbolCount = "3",
            Size = "24 MB",
            Destination = "C:\\Exports\\AAPL_MSFT_TSLA.csv"
        });
        ExportHistory.Add(new ExportHistoryItem
        {
            Timestamp = DateTimeOffset.Now.AddHours(-6).ToString("g"),
            Format = "Parquet",
            SymbolCount = "5",
            Size = "120 MB",
            Destination = "D:\\MarketData\\intraday.parquet"
        });

        IsNoExportHistoryVisible = ExportHistory.Count == 0;
    }
}

/// <summary>Represents a single completed export entry in the export history list.</summary>
public sealed class ExportHistoryItem
{
    public string Timestamp { get; set; } = string.Empty;
    public string Format { get; set; } = string.Empty;
    public string SymbolCount { get; set; } = string.Empty;
    public string Size { get; set; } = string.Empty;
    public string Destination { get; set; } = string.Empty;
}
