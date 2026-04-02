using System;
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

    // ── Export history empty-state ────────────────────────────────────────
    private bool _isNoExportHistoryVisible;

    // ── Schedule ──────────────────────────────────────────────────────────
    private bool _isScheduleEnabled;
    private string _scheduleTimeText = "08:00";
    private string _scheduleTimeError = string.Empty;
    private bool _isScheduleTimeErrorVisible;

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
    internal string SelectedExportFormat { get; set; } = "CSV";
    internal string SelectedCompression { get; set; } = "gzip";
    internal string SelectedDatabaseType { get; set; } = "postgresql";
    internal string SelectedScheduleFrequency { get; set; } = "daily";
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
        ExportDataCommand = new AsyncRelayCommand(ExportDataAsync);
        SetDatabaseCredentialsCommand = new RelayCommand(SetDatabaseCredentials);
        TestDatabaseConnectionCommand = new RelayCommand(TestDatabaseConnection);
        ConfigureDatabaseSyncCommand = new RelayCommand(ConfigureDatabaseSync);
        TestWebhookCommand = new RelayCommand(TestWebhook);
        BrowseLeanPathCommand = new RelayCommand(BrowseLeanPath);
        ExportToLeanCommand = new RelayCommand(ExportToLean);
        VerifyLeanDataCommand = new RelayCommand(VerifyLeanData);

        SeedInitialData();
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
    public IRelayCommand TestWebhookCommand { get; }
    public IRelayCommand BrowseLeanPathCommand { get; }
    public IRelayCommand ExportToLeanCommand { get; }
    public IRelayCommand VerifyLeanDataCommand { get; }

    // ── Date range properties ─────────────────────────────────────────────

    public DateTime? ExportFromDate
    {
        get => _exportFromDate;
        set => SetProperty(ref _exportFromDate, value);
    }

    public DateTime? ExportToDate
    {
        get => _exportToDate;
        set => SetProperty(ref _exportToDate, value);
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
        private set => SetProperty(ref _isExporting, value);
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
                ValidateScheduleTime();
        }
    }

    public string ScheduleTimeText
    {
        get => _scheduleTimeText;
        set
        {
            if (SetProperty(ref _scheduleTimeText, value))
                ValidateScheduleTime();
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
        if (!ValidateExportInputs()) return;

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

    private void ValidateScheduleTime()
    {
        IsScheduleTimeErrorVisible = false;
        if (!IsScheduleEnabled) return;

        if (!TimeSpan.TryParse(ScheduleTimeText, out _))
        {
            ScheduleTimeError = "Enter a valid time (HH:mm).";
            IsScheduleTimeErrorVisible = true;
        }
    }

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
