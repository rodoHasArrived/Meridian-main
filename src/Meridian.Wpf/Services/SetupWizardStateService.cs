using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Meridian.Core.Config;
using Meridian.Contracts.Configuration;
using Meridian.Ui.Services;
using Meridian.Ui.Services.Services;
using Meridian.Wpf.Contracts;
using Microsoft.Extensions.Logging;
using WpfServices = Meridian.Wpf.Services;

namespace Meridian.Wpf.Services;

/// <summary>
/// Defines setup configuration, provider presets, and backend startup and health operations.
/// </summary>
public interface ISetupWizardStateService
{
    IReadOnlyList<string> ProviderOptions { get; }

    string ConfigFilePath { get; }

    IReadOnlyList<SetupPreset> GetSetupPresets();

    Task<SetupWizardConfigurationState> LoadConfigurationAsync(CancellationToken ct = default);

    Task ApplyPresetAsync(SetupPreset preset, string provider, CancellationToken ct = default);

    Task<BackendServiceOperationResult> StartBackendAsync(CancellationToken ct = default);

    Task<SetupWizardBackendCheckResult> CheckBackendAsync(CancellationToken ct = default);

    Task<SetupWizardSaveResult> SaveConfigurationAsync(
        string provider,
        string storageLocation,
        CancellationToken ct = default);
}

public sealed record SetupWizardConfigurationState(
    IReadOnlyList<string> ProviderOptions,
    string SelectedProvider,
    string StorageLocation,
    string StatusText);

public sealed record SetupWizardBackendCheckResult(
    bool IsHealthy,
    string Message,
    int LatencyMs,
    string ServiceStatusMessage,
    string ServiceUrl);

public sealed record SetupWizardSaveResult(
    bool Success,
    string Message);

/// <summary>
/// Abstracts success, warning, information, and error notifications shown during setup.
/// </summary>
public interface ISetupWizardNotificationSink
{
    void NotifySuccess(string title, string message);

    void NotifyWarning(string title, string message);

    void NotifyInfo(string title, string message);

    Task NotifyErrorAsync(string title, string message);
}

/// <summary>
/// Abstracts page-tag navigation for the setup wizard.
/// </summary>
public interface ISetupWizardNavigator
{
    void NavigateTo(string pageTag);
}

/// <summary>
/// Abstracts opening external links from the setup wizard.
/// </summary>
public interface ISetupWizardLinkLauncher
{
    void Open(string url);
}

/// <summary>
/// Coordinates setup presets, local configuration, and backend startup and health checks.
/// </summary>
public sealed class SetupWizardStateService : ISetupWizardStateService
{
    private static readonly string[] DefaultProviderOptions =
    {
        "NoOp",
        "Stooq",
        "NasdaqDataLink",
        "Polygon",
        "Alpaca",
        "Robinhood"
    };

    private readonly ConnectionService _connectionService;
    private readonly FirstRunService _firstRunService;
    private readonly BackendServiceManager _backendServiceManager;
    private readonly SetupWizardService _setupWizardService;
    private readonly IRemoteWorkstationClient _remoteClient;

    public SetupWizardStateService(
        ConnectionService connectionService,
        FirstRunService firstRunService,
        BackendServiceManager backendServiceManager,
        SetupWizardService setupWizardService,
        IRemoteWorkstationClient remoteClient)
    {
        _connectionService = connectionService;
        _firstRunService = firstRunService;
        _backendServiceManager = backendServiceManager;
        _setupWizardService = setupWizardService;
        _remoteClient = remoteClient ?? throw new ArgumentNullException(nameof(remoteClient));
    }

    public IReadOnlyList<string> ProviderOptions => DefaultProviderOptions;

    public string ConfigFilePath => _firstRunService.ConfigFilePath;

    public IReadOnlyList<SetupPreset> GetSetupPresets()
        => _setupWizardService.GetSetupPresets();

    public async Task<SetupWizardConfigurationState> LoadConfigurationAsync(CancellationToken ct = default)
    {
        var provisioningResult = _firstRunService.LastConfigurationProvisioningResult;
        var providerOptions = DefaultProviderOptions;

        try
        {
            if (!File.Exists(_firstRunService.ConfigFilePath))
            {
                return new SetupWizardConfigurationState(
                    providerOptions,
                    providerOptions[0],
                    "data",
                    "No configuration file found yet. A default will be created.");
            }

            var json = await File.ReadAllTextAsync(_firstRunService.ConfigFilePath, ct).ConfigureAwait(false);
            var root = JsonNode.Parse(json) ?? new JsonObject();

            var dataSource = root["DataSource"]?.GetValue<string>() ?? providerOptions[0];
            var effectiveProviderOptions = providerOptions.Contains(dataSource, StringComparer.OrdinalIgnoreCase)
                ? providerOptions
                : new[] { dataSource }.Concat(providerOptions).ToArray();

            var storageBase = MeridianPathDefaults.ResolveConfiguredDataRootFromJson(json);

            var statusText = provisioningResult switch
            {
                ConfigurationProvisioningResult.CreatedDefault => "Created a new default configuration at startup.",
                ConfigurationProvisioningResult.RestoredLastKnownGood => "Recovered configuration from the last-known-good copy at startup.",
                ConfigurationProvisioningResult.RepairedInvalid => "Repaired an invalid configuration at startup and restored defaults.",
                _ => "Loaded existing valid configuration."
            };

            return new SetupWizardConfigurationState(
                effectiveProviderOptions,
                dataSource,
                storageBase,
                statusText);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new SetupWizardConfigurationState(
                providerOptions,
                providerOptions[0],
                "data",
                $"Unable to read configuration. {ex.Message}");
        }
    }

    public Task ApplyPresetAsync(SetupPreset preset, string provider, CancellationToken ct = default)
        => _setupWizardService.ApplyPresetAsync(preset, provider, ct);

    public Task<BackendServiceOperationResult> StartBackendAsync(CancellationToken ct = default)
        => _backendServiceManager.StartAsync(ct);

    public async Task<SetupWizardBackendCheckResult> CheckBackendAsync(CancellationToken ct = default)
    {
        var serviceStatus = await _backendServiceManager.GetStatusAsync(ct).ConfigureAwait(false);
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var isHealthy = await _remoteClient.CheckHealthEndpointAsync(ct).ConfigureAwait(false);
            stopwatch.Stop();
            if (isHealthy)
            {
                return new SetupWizardBackendCheckResult(
                    true,
                    "Healthy",
                    (int)stopwatch.Elapsed.TotalMilliseconds,
                    serviceStatus.StatusMessage,
                    _connectionService.ServiceUrl);
            }

            return new SetupWizardBackendCheckResult(
                false,
                "Health check failed",
                0,
                serviceStatus.StatusMessage,
                _connectionService.ServiceUrl);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            stopwatch.Stop();
            return new SetupWizardBackendCheckResult(
                false,
                ex.Message,
                0,
                serviceStatus.StatusMessage,
                _connectionService.ServiceUrl);
        }
    }

    public async Task<SetupWizardSaveResult> SaveConfigurationAsync(
        string provider,
        string storageLocation,
        CancellationToken ct = default)
    {
        try
        {
            if (Path.IsPathRooted(storageLocation))
            {
                Directory.CreateDirectory(storageLocation);
            }

            await WriteConfigurationUnderLeaseAsync(_firstRunService.ConfigFilePath, provider, storageLocation, ct).ConfigureAwait(false);

            return new SetupWizardSaveResult(true, "Configuration saved.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new SetupWizardSaveResult(false, $"Failed to save configuration: {ex.Message}");
        }
    }

    /// <summary>
    /// Applies the wizard's provider and storage selection to the shared configuration file as one
    /// read-modify-write under the host's cross-process configuration lock (the <c>.lock</c> sidecar
    /// <c>ConfigStore.UpdateAsync</c> and the shell's <c>ConfigService</c> hold), written atomically so a
    /// browser-host mutation and a wizard save never interleave or expose a truncated document
    /// (PRD-002 concurrent configuration writes).
    /// </summary>
    internal static async Task WriteConfigurationUnderLeaseAsync(string configFilePath, string provider, string storageLocation, CancellationToken ct)
    {
        var configDirectory = Path.GetDirectoryName(configFilePath);
        if (!string.IsNullOrWhiteSpace(configDirectory))
        {
            Directory.CreateDirectory(configDirectory);
        }

        using var lease = await new Meridian.Application.UI.ConfigStore(configFilePath).AcquireWriteLeaseAsync(ct).ConfigureAwait(false);

        JsonNode rootNode;
        if (File.Exists(configFilePath))
        {
            var json = await File.ReadAllTextAsync(configFilePath, ct).ConfigureAwait(false);
            rootNode = JsonNode.Parse(json) ?? new JsonObject();
        }
        else
        {
            rootNode = new JsonObject();
        }

        rootNode["DataSource"] = provider;
        rootNode["DataRoot"] = storageLocation;

        var storageNode = rootNode["Storage"] as JsonObject ?? new JsonObject();
        storageNode.Remove("BaseDirectory");
        storageNode.Remove("baseDirectory");
        rootNode["Storage"] = storageNode;

        var backfillNode = rootNode["Backfill"] as JsonObject ?? new JsonObject();
        backfillNode["DefaultProvider"] = provider;
        rootNode["Backfill"] = backfillNode;

        var output = rootNode.ToJsonString(AppConfigJsonOptions.Write);
        await Meridian.Storage.Archival.AtomicFileWriter.WriteAsync(configFilePath, output, ct).ConfigureAwait(false);
    }
}

/// <summary>
/// Routes setup notifications to the desktop notification service.
/// </summary>
public sealed class SetupWizardNotificationSink : ISetupWizardNotificationSink
{
    private readonly NotificationService _notificationService;

    public SetupWizardNotificationSink(NotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    public void NotifySuccess(string title, string message)
        => _notificationService.NotifySuccess(title, message);

    public void NotifyWarning(string title, string message)
        => _notificationService.NotifyWarning(title, message);

    public void NotifyInfo(string title, string message)
        => _notificationService.NotifyInfo(title, message);

    public Task NotifyErrorAsync(string title, string message)
        => _notificationService.NotifyErrorAsync(title, message);
}

/// <summary>
/// Routes setup-wizard navigation through the desktop navigation service.
/// </summary>
public sealed class SetupWizardNavigator : ISetupWizardNavigator
{
    private readonly NavigationService _navigationService;

    public SetupWizardNavigator(NavigationService navigationService)
    {
        _navigationService = navigationService;
    }

    public void NavigateTo(string pageTag)
        => _navigationService.NavigateTo(pageTag);
}

/// <summary>
/// Opens setup-wizard links through the operating system shell.
/// </summary>
public sealed class SetupWizardLinkLauncher : ISetupWizardLinkLauncher
{
    private readonly ISetupWizardNotificationSink _notificationSink;
    private readonly ILogger<SetupWizardLinkLauncher>? _logger;

    public SetupWizardLinkLauncher(
        ISetupWizardNotificationSink notificationSink,
        ILogger<SetupWizardLinkLauncher>? logger = null)
    {
        _notificationSink = notificationSink;
        _logger = logger;
    }

    public void Open(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _logger?.LogWarning(ex, "Could not open setup wizard documentation link.");
            _notificationSink.NotifyWarning("Error", "Could not open the link. Please try again.");
        }
    }
}
