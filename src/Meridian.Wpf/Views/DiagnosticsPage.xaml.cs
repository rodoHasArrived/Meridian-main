using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Shapes;
using System.Windows.Controls;
using System.Windows.Media;
using Meridian.Application.Monitoring;
using Meridian.Application.Services;
using Meridian.Ui.Services;
using Meridian.Wpf.ViewModels;
using WpfServices = Meridian.Wpf.Services;
using Path = System.IO.Path;

namespace Meridian.Wpf.Views;

/// <summary>
/// Diagnostics page providing system information, configuration checks,
/// and diagnostic actions for troubleshooting Meridian.
/// </summary>
public partial class DiagnosticsPage : Page
{
    private const string AppVersion = "1.6.1";
    private const string ConfigFileName = "appsettings.json";
    private const string DefaultStoragePath = "data";
    private const string DefaultLogsPath = "logs";

    private readonly WpfServices.NavigationService _navigationService;
    private readonly WpfServices.NotificationService _notificationService;
    private readonly DiagnosticsPageViewModel _viewModel;

    public DiagnosticsPage(
        WpfServices.NavigationService navigationService,
        WpfServices.NotificationService notificationService,
        IConnectivityProbeService? connectivityProbe = null,
        ICoLocationProfileActivator? coLocationProfileActivator = null,
        ProviderLatencyService? latencyService = null)
    {
        InitializeComponent();

        _navigationService = navigationService;
        _notificationService = notificationService;
        _viewModel = new DiagnosticsPageViewModel(connectivityProbe, coLocationProfileActivator, latencyService);
        
        DataContext = _viewModel;
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        PopulateSystemInfo();
        UpdateConnectivityStatusDot();
        
        // Subscribe to ViewModel connectivity changes
        if (_viewModel != null)
        {
            _viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(DiagnosticsPageViewModel.IsOnline))
                {
                    UpdateConnectivityStatusDot();
                }
            };
        }
    }

    private void UpdateConnectivityStatusDot()
    {
        if (_viewModel?.IsOnline == true)
        {
            ConnectivityStatusDot.Fill = (Brush)FindResource("SuccessColorBrush");
        }
        else if (_viewModel?.IsOnline == false)
        {
            ConnectivityStatusDot.Fill = (Brush)FindResource("ErrorColorBrush");
        }
        else
        {
            ConnectivityStatusDot.Fill = (Brush)FindResource("WarningColorBrush");
        }
    }

    private void PopulateSystemInfo()
    {
        RuntimeVersionText.Text = $".NET {Environment.Version}";
        OsVersionText.Text = Environment.OSVersion.ToString();
        WorkingDirectoryText.Text = Environment.CurrentDirectory;
        ProcessIdText.Text = Environment.ProcessId.ToString();

        var memoryBytes = GC.GetTotalMemory(forceFullCollection: false);
        MemoryUsageText.Text = FormatHelpers.FormatBytes(memoryBytes);
    }

    private void RunQuickCheck_Click(object sender, RoutedEventArgs e)
    {
        var output = new StringBuilder();
        output.AppendLine($"=== Quick Diagnostic Check ===");
        output.AppendLine($"Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        output.AppendLine();

        // Check configuration file
        var configPaths = new[]
        {
            Path.Combine(Environment.CurrentDirectory, "config", ConfigFileName),
            Path.Combine(Environment.CurrentDirectory, ConfigFileName)
        };

        var configFound = false;
        string? configPath = null;
        foreach (var path in configPaths)
        {
            if (File.Exists(path))
            {
                configFound = true;
                configPath = path;
                break;
            }
        }

        if (configFound)
        {
            output.AppendLine($"[PASS] Configuration file found: {configPath}");
            SetStatusIndicator(ConfigStatusDot, ConfigStatusText, true, $"Found at {configPath}");
        }
        else
        {
            output.AppendLine($"[WARN] Configuration file not found in expected locations.");
            output.AppendLine($"       Searched: {string.Join(", ", configPaths)}");
            SetStatusIndicator(ConfigStatusDot, ConfigStatusText, false, "File not found");
        }

        // Check storage directory
        var storagePath = Path.Combine(Environment.CurrentDirectory, DefaultStoragePath);
        try
        {
            if (Directory.Exists(storagePath))
            {
                output.AppendLine($"[PASS] Storage directory accessible: {storagePath}");
                SetStatusIndicator(StorageStatusDot, StorageStatusText, true, $"Accessible at {storagePath}");
            }
            else
            {
                output.AppendLine($"[INFO] Storage directory does not exist yet: {storagePath}");
                SetStatusIndicator(StorageStatusDot, StorageStatusText, null, "Directory not created yet");
            }
        }
        catch (Exception ex)
        {
            output.AppendLine($"[FAIL] Storage directory check failed: {ex.Message}");
            SetStatusIndicator(StorageStatusDot, StorageStatusText, false, $"Error: {ex.Message}");
        }

        // Check API connectivity (basic environment variable check)
        var hasAlpacaKey = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ALPACA__KEYID"));
        var hasPolygonKey = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("POLYGON__APIKEY"));
        var hasAnyKey = hasAlpacaKey || hasPolygonKey;

        if (hasAnyKey)
        {
            var providers = new StringBuilder();
            if (hasAlpacaKey) providers.Append("Alpaca ");
            if (hasPolygonKey) providers.Append("Polygon ");
            output.AppendLine($"[PASS] API credentials detected: {providers.ToString().Trim()}");
            SetStatusIndicator(ApiStatusDot, ApiStatusText, true, $"Credentials found: {providers.ToString().Trim()}");
        }
        else
        {
            output.AppendLine("[WARN] No API credentials found in environment variables.");
            output.AppendLine("       Set ALPACA__KEYID, POLYGON__APIKEY, or other provider keys.");
            SetStatusIndicator(ApiStatusDot, ApiStatusText, null, "No credentials in environment");
        }

        // Check provider configuration
        if (configFound && configPath != null)
        {
            try
            {
                var configContent = File.ReadAllText(configPath);
                var hasDataSource = configContent.Contains("\"DataSource\"", StringComparison.OrdinalIgnoreCase);
                if (hasDataSource)
                {
                    output.AppendLine("[PASS] Provider configuration section found in config file.");
                    SetStatusIndicator(ProviderStatusDot, ProviderStatusText, true, "Configuration section present");
                }
                else
                {
                    output.AppendLine("[WARN] No DataSource section found in configuration file.");
                    SetStatusIndicator(ProviderStatusDot, ProviderStatusText, null, "DataSource section missing");
                }
            }
            catch (Exception ex)
            {
                output.AppendLine($"[FAIL] Could not read config file: {ex.Message}");
                SetStatusIndicator(ProviderStatusDot, ProviderStatusText, false, $"Read error: {ex.Message}");
            }
        }
        else
        {
            output.AppendLine("[SKIP] Provider check skipped (no config file found).");
            SetStatusIndicator(ProviderStatusDot, ProviderStatusText, null, "Skipped (no config file)");
        }

        output.AppendLine();
        output.AppendLine("Quick check complete.");

        DiagnosticOutputText.Text = output.ToString();
    }

    private void RunFullDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        var output = new StringBuilder();
        output.AppendLine("=== Full Diagnostic Report ===");
        output.AppendLine($"Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        output.AppendLine($"Application: Meridian v{AppVersion}");
        output.AppendLine();

        // System Information
        output.AppendLine("--- System Information ---");
        output.AppendLine($"  .NET Runtime:      {Environment.Version}");
        output.AppendLine($"  OS:                {Environment.OSVersion}");
        output.AppendLine($"  64-bit OS:         {Environment.Is64BitOperatingSystem}");
        output.AppendLine($"  64-bit Process:    {Environment.Is64BitProcess}");
        output.AppendLine($"  Processor Count:   {Environment.ProcessorCount}");
        output.AppendLine($"  Machine Name:      {Environment.MachineName}");
        output.AppendLine($"  User:              {Environment.UserName}");
        output.AppendLine($"  Working Directory: {Environment.CurrentDirectory}");
        output.AppendLine($"  Process ID:        {Environment.ProcessId}");
        output.AppendLine();

        // Memory Information
        output.AppendLine("--- Memory ---");
        var memoryBytes = GC.GetTotalMemory(forceFullCollection: false);
        output.AppendLine($"  Managed Heap:      {FormatHelpers.FormatBytes(memoryBytes)}");
        output.AppendLine($"  GC Gen0 Collections: {GC.CollectionCount(0)}");
        output.AppendLine($"  GC Gen1 Collections: {GC.CollectionCount(1)}");
        output.AppendLine($"  GC Gen2 Collections: {GC.CollectionCount(2)}");

        try
        {
            using var currentProcess = Process.GetCurrentProcess();
            output.AppendLine($"  Working Set:       {FormatHelpers.FormatBytes(currentProcess.WorkingSet64)}");
            output.AppendLine($"  Private Memory:    {FormatHelpers.FormatBytes(currentProcess.PrivateMemorySize64)}");
            output.AppendLine($"  Thread Count:      {currentProcess.Threads.Count}");
            output.AppendLine($"  Handle Count:      {currentProcess.HandleCount}");
            output.AppendLine($"  Start Time:        {currentProcess.StartTime:yyyy-MM-dd HH:mm:ss}");
            var uptime = DateTime.Now - currentProcess.StartTime;
            output.AppendLine($"  Process Uptime:    {uptime.Hours}h {uptime.Minutes}m {uptime.Seconds}s");
        }
        catch (Exception ex)
        {
            output.AppendLine($"  [Process info unavailable: {ex.Message}]");
        }
        output.AppendLine();

        // Configuration Check
        output.AppendLine("--- Configuration ---");
        var configPaths = new[]
        {
            Path.Combine(Environment.CurrentDirectory, "config", ConfigFileName),
            Path.Combine(Environment.CurrentDirectory, ConfigFileName)
        };

        var configFound = false;
        foreach (var path in configPaths)
        {
            var exists = File.Exists(path);
            output.AppendLine($"  {path}: {(exists ? "FOUND" : "not found")}");
            if (exists)
            {
                configFound = true;
                try
                {
                    var info = new FileInfo(path);
                    output.AppendLine($"    Size: {FormatHelpers.FormatBytes(info.Length)}, Modified: {info.LastWriteTime:yyyy-MM-dd HH:mm:ss}");
                }
                catch (Exception ex)
                {
                    output.AppendLine($"    [Could not read file info: {ex.Message}]");
                }
            }
        }

        if (!configFound)
        {
            output.AppendLine("  WARNING: No configuration file found. Run --wizard or --auto-config to create one.");
        }
        output.AppendLine();

        // Storage Check
        output.AppendLine("--- Storage ---");
        var storageDirs = new[] { "data", "data/live", "data/historical", "data/_wal", "data/_archive" };
        foreach (var dir in storageDirs)
        {
            var fullPath = Path.Combine(Environment.CurrentDirectory, dir);
            var exists = Directory.Exists(fullPath);
            output.Append($"  {dir}: {(exists ? "EXISTS" : "not found")}");

            if (exists)
            {
                try
                {
                    var dirInfo = new DirectoryInfo(fullPath);
                    var fileCount = dirInfo.GetFiles("*", SearchOption.TopDirectoryOnly).Length;
                    output.Append($" ({fileCount} files)");
                }
                catch
                {
                    // Ignore directory access errors for file counting
                }
            }

            output.AppendLine();
        }
        output.AppendLine();

        // Environment Variables
        output.AppendLine("--- API Credentials (presence check only) ---");
        var credentialVars = new[]
        {
            "ALPACA__KEYID",
            "ALPACA__SECRETKEY",
            "POLYGON__APIKEY",
            "NYSE__APIKEY",
            "TIINGO__TOKEN",
            "FINNHUB__TOKEN",
            "ALPHAVANTAGE__APIKEY"
        };

        foreach (var varName in credentialVars)
        {
            var value = Environment.GetEnvironmentVariable(varName);
            var status = string.IsNullOrEmpty(value) ? "NOT SET" : "SET";
            output.AppendLine($"  {varName}: {status}");
        }
        output.AppendLine();

        // Logs Directory
        output.AppendLine("--- Logs ---");
        var logsPath = Path.Combine(Environment.CurrentDirectory, DefaultLogsPath);
        if (Directory.Exists(logsPath))
        {
            output.AppendLine($"  Logs directory: {logsPath}");
            try
            {
                var logFiles = Directory.GetFiles(logsPath, "*.log", SearchOption.TopDirectoryOnly);
                output.AppendLine($"  Log files: {logFiles.Length}");
                if (logFiles.Length > 0)
                {
                    var latestLog = new FileInfo(logFiles[^1]);
                    output.AppendLine($"  Latest: {latestLog.Name} ({FormatHelpers.FormatBytes(latestLog.Length)}, {latestLog.LastWriteTime:yyyy-MM-dd HH:mm:ss})");
                }
            }
            catch (Exception ex)
            {
                output.AppendLine($"  [Could not enumerate logs: {ex.Message}]");
            }
        }
        else
        {
            output.AppendLine($"  Logs directory not found at: {logsPath}");
        }
        output.AppendLine();

        output.AppendLine("=== End of Diagnostic Report ===");

        DiagnosticOutputText.Text = output.ToString();

        // Also run the quick check to update status indicators
        RunQuickCheckStatusIndicators();
    }

    private void ExportBundle_Click(object sender, RoutedEventArgs e)
    {
        _notificationService.NotifyInfo(
            "Export Diagnostic Bundle",
            "Diagnostic bundle export is not yet implemented. This feature will create a zip archive containing logs, configuration, and system information for support purposes.");
    }

    private void OpenLogsFolder_Click(object sender, RoutedEventArgs e)
    {
        var logsPath = Path.Combine(Environment.CurrentDirectory, DefaultLogsPath);

        if (!Directory.Exists(logsPath))
        {
            try
            {
                Directory.CreateDirectory(logsPath);
            }
            catch (Exception ex)
            {
                _notificationService.ShowNotification(
                    "Error",
                    $"Could not create logs directory: {ex.Message}",
                    NotificationType.Error);
                return;
            }
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = logsPath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _notificationService.ShowNotification(
                "Error",
                $"Could not open logs folder: {ex.Message}",
                NotificationType.Error);
        }
    }

    /// <summary>
    /// Runs status indicator updates without writing to the output textbox.
    /// Used by RunFullDiagnostics to update the colored dots after the full report.
    /// </summary>
    private void RunQuickCheckStatusIndicators()
    {
        // Config file
        var configPaths = new[]
        {
            Path.Combine(Environment.CurrentDirectory, "config", ConfigFileName),
            Path.Combine(Environment.CurrentDirectory, ConfigFileName)
        };

        var configFound = false;
        string? configPath = null;
        foreach (var path in configPaths)
        {
            if (File.Exists(path))
            {
                configFound = true;
                configPath = path;
                break;
            }
        }

        SetStatusIndicator(ConfigStatusDot, ConfigStatusText,
            configFound, configFound ? $"Found at {configPath}" : "File not found");

        // Storage
        var storagePath = Path.Combine(Environment.CurrentDirectory, DefaultStoragePath);
        try
        {
            if (Directory.Exists(storagePath))
            {
                SetStatusIndicator(StorageStatusDot, StorageStatusText, true, $"Accessible at {storagePath}");
            }
            else
            {
                SetStatusIndicator(StorageStatusDot, StorageStatusText, null, "Directory not created yet");
            }
        }
        catch (Exception ex)
        {
            SetStatusIndicator(StorageStatusDot, StorageStatusText, false, $"Error: {ex.Message}");
        }

        // API credentials
        var hasAlpacaKey = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ALPACA__KEYID"));
        var hasPolygonKey = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("POLYGON__APIKEY"));
        var hasAnyKey = hasAlpacaKey || hasPolygonKey;

        if (hasAnyKey)
        {
            var providers = new StringBuilder();
            if (hasAlpacaKey) providers.Append("Alpaca ");
            if (hasPolygonKey) providers.Append("Polygon ");
            SetStatusIndicator(ApiStatusDot, ApiStatusText, true, $"Credentials found: {providers.ToString().Trim()}");
        }
        else
        {
            SetStatusIndicator(ApiStatusDot, ApiStatusText, null, "No credentials in environment");
        }

        // Provider configuration
        if (configFound && configPath != null)
        {
            try
            {
                var configContent = File.ReadAllText(configPath);
                var hasDataSource = configContent.Contains("\"DataSource\"", StringComparison.OrdinalIgnoreCase);
                SetStatusIndicator(ProviderStatusDot, ProviderStatusText,
                    hasDataSource, hasDataSource ? "Configuration section present" : "DataSource section missing");
            }
            catch (Exception ex)
            {
                SetStatusIndicator(ProviderStatusDot, ProviderStatusText, false, $"Read error: {ex.Message}");
            }
        }
        else
        {
            SetStatusIndicator(ProviderStatusDot, ProviderStatusText, null, "Skipped (no config file)");
        }
    }

    /// <summary>
    /// Sets a status indicator dot and text. Pass true for success (green),
    /// false for failure (red), or null for warning/unknown (orange).
    /// </summary>
    private void SetStatusIndicator(Ellipse dot, TextBlock text, bool? success, string message)
    {
        if (success == true)
        {
            dot.Fill = (Brush)FindResource("SuccessColorBrush");
            text.Foreground = (Brush)FindResource("SuccessColorBrush");
        }
        else if (success == false)
        {
            dot.Fill = (Brush)FindResource("ErrorColorBrush");
            text.Foreground = (Brush)FindResource("ErrorColorBrush");
        }
        else
        {
            dot.Fill = (Brush)FindResource("WarningColorBrush");
            text.Foreground = (Brush)FindResource("WarningColorBrush");
        }

        text.Text = message;
    }

}
