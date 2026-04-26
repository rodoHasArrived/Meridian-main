using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Meridian.Contracts.Configuration;
using Meridian.Ui.Services;
using Meridian.Wpf.Services;
using WpfServices = Meridian.Wpf.Services;

namespace Meridian.Wpf.Views;

public partial class SetupWizardPage : Page
{
    private static readonly string[] ProviderOptions =
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
    private readonly WpfServices.NotificationService _notificationService;
    private readonly WpfServices.NavigationService _navigationService;
    private readonly SetupWizardService _setupWizardService;
    private readonly HttpClient _httpClient;

    public SetupWizardPage(
        ConnectionService connectionService,
        FirstRunService firstRunService,
        BackendServiceManager backendServiceManager,
        WpfServices.NotificationService notificationService,
        WpfServices.NavigationService navigationService)
    {
        InitializeComponent();
        _connectionService = connectionService;
        _firstRunService = firstRunService;
        _backendServiceManager = backendServiceManager;
        _notificationService = notificationService;
        _navigationService = navigationService;
        _setupWizardService = new SetupWizardService();
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        ProviderCombo.ItemsSource = ProviderOptions;
        ConfigPathText.Text = _firstRunService.ConfigFilePath;

        // Populate role-based presets
        PresetsList.ItemsSource = _setupWizardService.GetSetupPresets();

        LoadExistingConfiguration();
        LoadStoredApiKeys();

        await EnsureBackendAvailableAsync();
    }

    private async void PresetCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string presetId)
            return;

        var presets = _setupWizardService.GetSetupPresets();
        var preset = presets.FirstOrDefault(p => p.Id == presetId);
        if (preset == null)
            return;

        PresetStatusText.Text = $"Applying \"{preset.Name}\" preset...";

        // Set the recommended provider
        var recommendedProvider = preset.RecommendedProviders.FirstOrDefault(p =>
            ProviderOptions.Contains(p, StringComparer.OrdinalIgnoreCase))
            ?? ProviderOptions[0];

        ProviderCombo.SelectedItem = recommendedProvider;

        // Apply preset configuration
        try
        {
            await _setupWizardService.ApplyPresetAsync(preset, recommendedProvider);
            PresetStatusText.Text = $"Applied \"{preset.Name}\" preset: {preset.DefaultSymbols.Length} symbols, " +
                                    $"{(preset.SubscribeDepth ? "L2 depth" : "L1 only")}, " +
                                    $"{(preset.EnableBackfill ? "backfill enabled" : "no backfill")}.";
            _notificationService.NotifySuccess("Setup Preset", $"\"{preset.Name}\" preset applied successfully.");
        }
        catch (Exception ex)
        {
            PresetStatusText.Text = $"Failed to apply preset: {ex.Message}";
            _ = _notificationService.NotifyErrorAsync("Setup Preset", $"Failed to apply preset: {ex.Message}");
        }
    }

    private void LoadExistingConfiguration()
    {
        try
        {
            if (!File.Exists(_firstRunService.ConfigFilePath))
            {
                ProviderCombo.SelectedItem = ProviderOptions[0];
                StorageLocationTextBox.Text = "data";
                ConfigStatusText.Text = "No configuration file found yet. A default will be created.";
                return;
            }

            var json = File.ReadAllText(_firstRunService.ConfigFilePath);
            var root = JsonNode.Parse(json) ?? new JsonObject();

            var dataSource = root["DataSource"]?.GetValue<string>() ?? ProviderOptions[0];
            if (Array.IndexOf(ProviderOptions, dataSource) < 0)
            {
                ProviderCombo.ItemsSource = new[] { dataSource }.Concat(ProviderOptions);
            }

            ProviderCombo.SelectedItem = dataSource;

            var storageBase = MeridianPathDefaults.ResolveConfiguredDataRootFromJson(json);
            StorageLocationTextBox.Text = storageBase;

            ConfigStatusText.Text = "Loaded existing configuration.";
        }
        catch (Exception ex)
        {
            ConfigStatusText.Text = $"Unable to read configuration. {ex.Message}";
            ProviderCombo.SelectedItem = ProviderOptions[0];
            StorageLocationTextBox.Text = "data";
        }
    }

    private void LoadStoredApiKeys()
    {
        var nasdaqKey = Environment.GetEnvironmentVariable("NASDAQDATALINK__APIKEY", EnvironmentVariableTarget.User);
        if (!string.IsNullOrWhiteSpace(nasdaqKey))
        {
            NasdaqApiKeyTextBox.Text = nasdaqKey;
        }

        var openFigiKey = Environment.GetEnvironmentVariable("OPENFIGI__APIKEY", EnvironmentVariableTarget.User);
        if (!string.IsNullOrWhiteSpace(openFigiKey))
        {
            OpenFigiApiKeyTextBox.Text = openFigiKey;
        }
    }

    private async void TestConnection_Click(object sender, RoutedEventArgs e)
    {
        await RefreshBackendStatusAsync();
    }

    private async Task RefreshBackendStatusAsync(CancellationToken ct = default)
    {
        var serviceStatus = await _backendServiceManager.GetStatusAsync();

        BackendStatusText.Text = "Checking backend status...";
        BackendStatusDetailText.Text = $"Testing {_connectionService.ServiceUrl}/healthz";
        BackendStatusDot.Fill = (System.Windows.Media.Brush)FindResource("WarningColorBrush");

        var result = await CheckBackendAsync();

        if (result.isHealthy)
        {
            BackendStatusDot.Fill = (System.Windows.Media.Brush)FindResource("SuccessColorBrush");
            BackendStatusText.Text = "Backend is running.";
            BackendStatusDetailText.Text = $"Latency: {result.latencyMs} ms · {serviceStatus.StatusMessage}";
        }
        else
        {
            BackendStatusDot.Fill = (System.Windows.Media.Brush)FindResource("ErrorColorBrush");
            BackendStatusText.Text = "Backend not reachable.";
            BackendStatusDetailText.Text = $"{result.message} · {serviceStatus.StatusMessage}";
        }
    }

    private async Task EnsureBackendAvailableAsync(CancellationToken ct = default)
    {
        var status = await _backendServiceManager.GetStatusAsync();
        if (!status.IsRunning)
        {
            BackendStatusText.Text = "Starting backend service...";
            BackendStatusDetailText.Text = "First run now auto-starts the backend service.";

            var startResult = await _backendServiceManager.StartAsync();
            if (!startResult.Success)
            {
                _notificationService.NotifyWarning("Backend", startResult.Message);
            }
        }

        await RefreshBackendStatusAsync();
    }

    private async Task<(bool isHealthy, string message, int latencyMs)> CheckBackendAsync(CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(8));
            var response = await _httpClient.GetAsync($"{_connectionService.ServiceUrl}/healthz", cts.Token);
            stopwatch.Stop();
            if (response.IsSuccessStatusCode)
            {
                return (true, "Healthy", (int)stopwatch.Elapsed.TotalMilliseconds);
            }

            return (false, $"Health check returned {response.StatusCode}", 0);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return (false, ex.Message, 0);
        }
    }

    private async void StartBackend_Click(object sender, RoutedEventArgs e)
    {
        var result = await _backendServiceManager.StartAsync();
        if (result.Success)
        {
            _notificationService.NotifyInfo("Backend", result.Message);
        }
        else
        {
            _notificationService.NotifyWarning("Backend", result.Message);
        }

        await RefreshBackendStatusAsync();
    }

    private void OpenInstructions_Click(object sender, RoutedEventArgs e)
    {
        OpenUrl("https://github.com/example/meridian/blob/main/docs/guides/configuration.md");
    }

    private void UseDefaultStorage_Click(object sender, RoutedEventArgs e)
    {
        StorageLocationTextBox.Text = "data";
    }

    private async void SaveAndContinue_Click(object sender, RoutedEventArgs e)
    {
        ValidationStatusText.Text = "Saving configuration...";

        if (!TryGetWizardInputs(out var provider, out var storageLocation))
        {
            return;
        }

        var saveResult = await SaveConfigurationAsync(provider, storageLocation);
        if (!saveResult)
        {
            return;
        }

        SaveApiKeys();

        var startResult = await _backendServiceManager.StartAsync();
        if (!startResult.Success)
        {
            ValidationStatusText.Text = $"Saved config, but backend start failed: {startResult.Message}";
            _notificationService.NotifyWarning("Setup Wizard", "Configuration saved, but backend is offline.");
            return;
        }

        var backendResult = await CheckBackendAsync();
        if (!backendResult.isHealthy)
        {
            ValidationStatusText.Text = $"Saved config, but backend is unreachable: {backendResult.message}";
            _notificationService.NotifyWarning("Setup Wizard", "Configuration saved, but backend is offline.");
            return;
        }

        ValidationStatusText.Text = "Configuration saved and backend verified. Opening Research Workspace...";
        _notificationService.NotifySuccess("Setup Wizard", "Setup complete. Welcome!");

        _navigationService.NavigateTo("ResearchShell");
    }

    private bool TryGetWizardInputs(out string provider, out string storageLocation)
    {
        provider = ProviderCombo.SelectedItem as string ?? string.Empty;
        storageLocation = StorageLocationTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(provider))
        {
            ValidationStatusText.Text = "Select a default provider before continuing.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(storageLocation))
        {
            ValidationStatusText.Text = "Enter a storage location before continuing.";
            return false;
        }

        return true;
    }

    private async Task<bool> SaveConfigurationAsync(string provider, string storageLocation, CancellationToken ct = default)
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
                var json = await File.ReadAllTextAsync(_firstRunService.ConfigFilePath);
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

            var output = rootNode.ToJsonString(new JsonSerializerOptions
            {
                WriteIndented = true
            });

            await File.WriteAllTextAsync(_firstRunService.ConfigFilePath, output);
            ConfigStatusText.Text = "Configuration saved.";

            return true;
        }
        catch (Exception ex)
        {
            ValidationStatusText.Text = $"Failed to save configuration: {ex.Message}";
            _ = _notificationService.NotifyErrorAsync("Setup Wizard", "Failed to save configuration.");
            return false;
        }
    }

    private void SaveApiKeys()
    {
        SaveApiKey("NASDAQDATALINK__APIKEY", NasdaqApiKeyTextBox.Text);
        SaveApiKey("OPENFIGI__APIKEY", OpenFigiApiKeyTextBox.Text);
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

    private void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch
        {
            _notificationService.ShowNotification(
                "Error",
                "Could not open the link. Please try again.",
                NotificationType.Error);
        }
    }
}
