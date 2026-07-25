using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Meridian.Core.Config;
using Meridian.Contracts.Configuration;

namespace Meridian.Wpf.Services;

/// <summary>
/// Service for detecting and handling first-run scenarios.
/// Implements singleton pattern for application-wide first-run detection.
/// </summary>
public sealed class FirstRunService
{
    private static readonly Lazy<FirstRunService> _instance = new(() => new FirstRunService());

    private bool? _isFirstRun;
    private bool _isInitialized;
    private readonly object _lock = new();
    private readonly DesktopConfigurationRecoveryService _configurationRecoveryService;
    private ConfigurationProvisioningResult _lastConfigurationProvisioningResult =
        ConfigurationProvisioningResult.AlreadyValid;
    private DesktopConfigurationRecoveryResult? _lastConfigurationRecoveryResult;

    /// <summary>
    /// Gets the singleton instance of the FirstRunService.
    /// </summary>
    public static FirstRunService Instance => _instance.Value;

    /// <summary>
    /// Gets the path to the application data directory.
    /// </summary>
    public string AppDataPath { get; }

    /// <summary>
    /// Gets the path to the configuration file.
    /// </summary>
    public string ConfigFilePath { get; }

    /// <summary>
    /// Gets the path to the first-run marker file.
    /// </summary>
    public string FirstRunMarkerPath { get; }

    /// <summary>
    /// Occurs when initialization is complete.
    /// </summary>
    public event EventHandler<FirstRunInitializedEventArgs>? Initialized;

    private FirstRunService()
    {
        AppDataPath = MeridianPathDefaults.GetLocalApplicationDataRoot();
        ConfigFilePath = MeridianPathDefaults.GetDesktopConfigPath();
        FirstRunMarkerPath = MeridianPathDefaults.GetFirstRunMarkerPath();
        _configurationRecoveryService = new DesktopConfigurationRecoveryService(ConfigFilePath);
    }

    /// <summary>
    /// Determines whether this is the first run of the application.
    /// </summary>
    /// <returns>True if this is the first run; otherwise, false.</returns>
    public Task<bool> IsFirstRunAsync()
    {
        if (_isFirstRun.HasValue)
        {
            return Task.FromResult(_isFirstRun.Value);
        }

        lock (_lock)
        {
            if (_isFirstRun.HasValue)
            {
                return Task.FromResult(_isFirstRun.Value);
            }

            // Check for presence of config file or first-run marker
            var configExists = File.Exists(ConfigFilePath);
            var markerExists = File.Exists(FirstRunMarkerPath);

            _isFirstRun = !configExists && !markerExists;

            LoggingService.Instance.LogInfo(
                "First run detection completed",
                ("IsFirstRun", _isFirstRun.Value.ToString()),
                ("ConfigExists", configExists.ToString()),
                ("MarkerExists", markerExists.ToString()));

            return Task.FromResult(_isFirstRun.Value);
        }
    }

    /// <summary>
    /// Initializes the application for first-run scenarios.
    /// Creates necessary directories and default configuration.
    /// </summary>
    /// <returns>A task representing the async operation.</returns>
    public Task InitializeAsync()
    {
        if (_isInitialized)
        {
            LoggingService.Instance.LogWarning("FirstRunService is already initialized");
            return Task.CompletedTask;
        }

        lock (_lock)
        {
            if (_isInitialized)
            {
                return Task.CompletedTask;
            }

            try
            {
                StatusService.Instance.SetBusy("Initializing application");

                // Create application data directory if it doesn't exist
                if (!Directory.Exists(AppDataPath))
                {
                    Directory.CreateDirectory(AppDataPath);
                    LoggingService.Instance.LogInfo(
                        "Created application data directory",
                        ("Path", AppDataPath));
                }

                // Create default configuration if it doesn't exist
                if (!File.Exists(ConfigFilePath))
                {
                    CreateDefaultConfiguration();
                }

                // Create first-run marker to indicate initialization is complete
                CreateFirstRunMarker();

                _isInitialized = true;
                _isFirstRun = false;

                StatusService.Instance.SetReady();

                LoggingService.Instance.LogInfo("First run initialization completed successfully");
            }
            catch (Exception ex)
            {
                LoggingService.Instance.LogError("First run initialization failed", ex);
                StatusService.Instance.SetError("Initialization failed");
                throw;
            }
        }

        OnInitialized(new FirstRunInitializedEventArgs(_isFirstRun ?? false));
        return Task.CompletedTask;
    }

    /// <summary>
    /// Resets the first-run state, useful for testing or reconfiguration.
    /// </summary>
    /// <returns>A task representing the async operation.</returns>
    public Task ResetAsync()
    {
        lock (_lock)
        {
            try
            {
                if (File.Exists(FirstRunMarkerPath))
                {
                    File.Delete(FirstRunMarkerPath);
                }

                if (File.Exists(ConfigFilePath))
                {
                    File.Delete(ConfigFilePath);
                }

                _isFirstRun = null;
                _isInitialized = false;

                LoggingService.Instance.LogInfo("First run state reset");
            }
            catch (Exception ex)
            {
                LoggingService.Instance.LogError("Failed to reset first run state", ex);
                throw;
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Gets whether the application has been initialized.
    /// </summary>
    public bool IsInitialized => _isInitialized;

    public ConfigurationProvisioningResult LastConfigurationProvisioningResult => _lastConfigurationProvisioningResult;

    public DesktopConfigurationRecoveryResult? LastConfigurationRecoveryResult => _lastConfigurationRecoveryResult;

    public Task<ConfigurationProvisioningResult> EnsureConfigurationExistsAsync()
    {
        lock (_lock)
        {
            _lastConfigurationRecoveryResult =
                _configurationRecoveryService.EnsureReadableConfiguration();
            _lastConfigurationProvisioningResult = _lastConfigurationRecoveryResult.Outcome;

            var log = _lastConfigurationRecoveryResult.Recovered
                ? (Action<string, (string Key, string Value)[]>)LoggingService.Instance.LogWarning
                : LoggingService.Instance.LogInfo;
            log(
                "Configuration preflight completed",
                [
                    ("Outcome", _lastConfigurationProvisioningResult.ToString()),
                    ("Path", ConfigFilePath),
                    ("InvalidArtifact", _lastConfigurationRecoveryResult.InvalidConfigurationPath ?? string.Empty),
                    ("RecoveryReceipt", _lastConfigurationRecoveryResult.RecoveryReceiptPath ?? string.Empty)
                ]);
            return Task.FromResult(_lastConfigurationProvisioningResult);
        }
    }

    public async Task PersistConfigurationAsync(string json, CancellationToken ct = default)
    {
        await _configurationRecoveryService
            .PersistValidConfigurationAsync(json, ct)
            .ConfigureAwait(false);
        _lastConfigurationProvisioningResult = ConfigurationProvisioningResult.AlreadyValid;
        _lastConfigurationRecoveryResult = new DesktopConfigurationRecoveryResult(
            ConfigurationProvisioningResult.AlreadyValid,
            ConfigFilePath,
            RestoredFromPath: null,
            InvalidConfigurationPath: null,
            RecoveryReceiptPath: null,
            FailureReason: null);
    }

    private void CreateDefaultConfiguration()
    {
        // CreateDefaultConfiguration: first-run bootstrap must use the same canonical defaults as Settings reset.
        var defaultConfig = AppConfigDefaults.CreateDefaultAppConfig();
        var defaultConfigJson = JsonSerializer.Serialize(defaultConfig, Meridian.Ui.Services.DesktopJsonOptions.PrettyPrint);

        File.WriteAllText(ConfigFilePath, defaultConfigJson);

        LoggingService.Instance.LogInfo(
            "Created default configuration file",
            ("Path", ConfigFilePath));
    }

    private void CreateFirstRunMarker()
    {
        var markerContent = $$"""
            {
              "InitializedAt": "{{DateTime.UtcNow:O}}",
              "Version": "1.0.0"
            }
            """;

        File.WriteAllText(FirstRunMarkerPath, markerContent);

        LoggingService.Instance.LogInfo(
            "Created first run marker",
            ("Path", FirstRunMarkerPath));
    }

    /// <summary>
    /// Raises the Initialized event.
    /// </summary>
    /// <param name="e">The event arguments.</param>
    private void OnInitialized(FirstRunInitializedEventArgs e)
    {
        Initialized?.Invoke(this, e);
    }

}

public enum ConfigurationProvisioningResult
{
    CreatedDefault,
    RestoredLastKnownGood,
    RepairedInvalid,
    AlreadyValid
}

/// <summary>
/// Event arguments for first-run initialization events.
/// </summary>
public sealed class FirstRunInitializedEventArgs : EventArgs
{
    /// <summary>
    /// Gets whether this was the first run.
    /// </summary>
    public bool WasFirstRun { get; }

    /// <summary>
    /// Gets the timestamp of initialization.
    /// </summary>
    public DateTime Timestamp { get; }

    /// <summary>
    /// Initializes a new instance of the FirstRunInitializedEventArgs class.
    /// </summary>
    /// <param name="wasFirstRun">Whether this was the first run.</param>
    public FirstRunInitializedEventArgs(bool wasFirstRun)
    {
        WasFirstRun = wasFirstRun;
        Timestamp = DateTime.UtcNow;
    }
}
