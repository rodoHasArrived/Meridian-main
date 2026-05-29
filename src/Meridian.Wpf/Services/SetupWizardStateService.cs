using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Meridian.Application.Config;
using Meridian.Contracts.Configuration;
using Meridian.Ui.Services;
using Meridian.Ui.Services.Services;
using Microsoft.Extensions.Logging;
using WpfServices = Meridian.Wpf.Services;

namespace Meridian.Wpf.Services;

public interface ISetupWizardStateService
{
    IReadOnlyList<string> ProviderOptions { get; }

    string ConfigFilePath { get; }

    IReadOnlyList<SetupPreset> GetSetupPresets();

    Task<SetupWizardConfigurationState> LoadConfigurationAsync(CancellationToken ct = default);

    SetupWizardApiKeyState LoadStoredApiKeys();

    Task ApplyPresetAsync(SetupPreset preset, string provider, CancellationToken ct = default);

    Task<BackendServiceOperationResult> StartBackendAsync(CancellationToken ct = default);

    Task<SetupWizardBackendCheckResult> CheckBackendAsync(CancellationToken ct = default);

    Task<SetupWizardSaveResult> SaveConfigurationAsync(
        string provider,
        string storageLocation,
        CancellationToken ct = default);

    void SaveApiKeys(string nasdaqDataLinkApiKey, string openFigiApiKey);
}

public sealed record SetupWizardConfigurationState(
    IReadOnlyList<string> ProviderOptions,
    string SelectedProvider,
    string StorageLocation,
    string StatusText);

public sealed record SetupWizardApiKeyState(
    string NasdaqDataLinkApiKey,
    string OpenFigiApiKey);

public sealed record SetupWizardBackendCheckResult(
    bool IsHealthy,
    string Message,
    int LatencyMs,
    string ServiceStatusMessage,
    string ServiceUrl);

public sealed record SetupWizardSaveResult(
    bool Success,
    string Message);

public interface ISetupWizardNotificationSink
{
    void NotifySuccess(string title, string message);

    void NotifyWarning(string title, string message);

    void NotifyInfo(string title, string message);

    Task NotifyErrorAsync(string title, string message);
}

public interface ISetupWizardNavigator
{
    void NavigateTo(string pageTag);
}

public interface ISetupWizardLinkLauncher
{
    void Open(string url);
}

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
    private readonly HttpClient _httpClient;

    public SetupWizardStateService(
        ConnectionService connectionService,
        FirstRunService firstRunService,
        BackendServiceManager backendServiceManager,
        SetupWizardService setupWizardService,
        IHttpClientFactory httpClientFactory)
    {
        _connectionService = connectionService;
        _firstRunService = firstRunService;
        _backendServiceManager = backendServiceManager;
        _setupWizardService = setupWizardService;
        _httpClient = httpClientFactory.CreateClient(HttpClientNames.SetupWizard);
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

    public SetupWizardApiKeyState LoadStoredApiKeys()
        => new(
            Environment.GetEnvironmentVariable("NASDAQDATALINK__APIKEY", EnvironmentVariableTarget.User) ?? string.Empty,
            Environment.GetEnvironmentVariable("OPENFIGI__APIKEY", EnvironmentVariableTarget.User) ?? string.Empty);

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
            using var response = await _httpClient.GetAsync($"{_connectionService.ServiceUrl}/healthz", ct).ConfigureAwait(false);
            stopwatch.Stop();
            if (response.IsSuccessStatusCode)
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
                $"Health check returned {response.StatusCode}",
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

            var configDirectory = Path.GetDirectoryName(_firstRunService.ConfigFilePath);
            if (!string.IsNullOrWhiteSpace(configDirectory))
            {
                Directory.CreateDirectory(configDirectory);
            }

            JsonNode rootNode;
            if (File.Exists(_firstRunService.ConfigFilePath))
            {
                var json = await File.ReadAllTextAsync(_firstRunService.ConfigFilePath, ct).ConfigureAwait(false);
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
            await File.WriteAllTextAsync(_firstRunService.ConfigFilePath, output, ct).ConfigureAwait(false);

            return new SetupWizardSaveResult(true, "Configuration saved.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new SetupWizardSaveResult(false, $"Failed to save configuration: {ex.Message}");
        }
    }

    public void SaveApiKeys(string nasdaqDataLinkApiKey, string openFigiApiKey)
    {
        SaveApiKey("NASDAQDATALINK__APIKEY", nasdaqDataLinkApiKey);
        SaveApiKey("OPENFIGI__APIKEY", openFigiApiKey);
    }

    private static void SaveApiKey(string variableName, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            Environment.SetEnvironmentVariable(variableName, null, EnvironmentVariableTarget.User);
            return;
        }

        Environment.SetEnvironmentVariable(variableName, value.Trim(), EnvironmentVariableTarget.User);
    }
}

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
