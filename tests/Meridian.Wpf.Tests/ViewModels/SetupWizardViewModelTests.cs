using Meridian.Ui.Services;
using Meridian.Ui.Services.Services;
using Meridian.Wpf.Services;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed class SetupWizardViewModelTests
{
    [Fact]
    public async Task LoadAsync_WhenBackendIsOffline_StartsBackendAndProjectsHealthyState()
    {
        var state = new FakeSetupWizardStateService();
        state.BackendChecks.Enqueue(new SetupWizardBackendCheckResult(
            false,
            "connection refused",
            0,
            "Backend is stopped.",
            "http://localhost:8080"));
        state.BackendChecks.Enqueue(new SetupWizardBackendCheckResult(
            true,
            "Healthy",
            12,
            "Backend is running and healthy.",
            "http://localhost:8080"));

        var viewModel = CreateViewModel(state);

        await viewModel.LoadAsync();

        state.StartBackendCalls.Should().Be(1);
        viewModel.ConfigPath.Should().Be(state.ConfigFilePath);
        viewModel.SelectedProvider.Should().Be("Synthetic");
        viewModel.StorageLocation.Should().Be("data");
        viewModel.ConfigStatusText.Should().Be("Loaded existing valid configuration.");
        viewModel.BackendStatusTone.Should().Be("Success");
        viewModel.BackendStatusText.Should().Be("Backend is running.");
        viewModel.BackendStatusDetailText.Should().Contain("12 ms");
    }

    [Fact]
    public async Task SaveAndContinueCommand_WhenProviderIsMissing_StopsBeforeSaving()
    {
        var state = new FakeSetupWizardStateService();
        var viewModel = CreateViewModel(state);

        viewModel.SelectedProvider = "";
        viewModel.StorageLocation = "data";

        await viewModel.SaveAndContinueCommand.ExecuteAsync(null);

        state.SaveConfigurationCalls.Should().Be(0);
        viewModel.ValidationStatusText.Should().Be("Select a default provider before continuing.");
    }

    [Fact]
    public async Task SaveAndContinueCommand_WhenBackendVerifies_SavesKeysAndNavigates()
    {
        var state = new FakeSetupWizardStateService
        {
            SaveResult = new SetupWizardSaveResult(true, "Configuration saved.")
        };
        state.BackendChecks.Enqueue(new SetupWizardBackendCheckResult(
            true,
            "Healthy",
            9,
            "Backend is running and healthy.",
            "http://localhost:8080"));
        var navigator = new FakeSetupWizardNavigator();
        var notification = new FakeSetupWizardNotificationSink();
        var viewModel = CreateViewModel(state, notification, navigator);

        viewModel.SelectedProvider = "Alpaca";
        viewModel.StorageLocation = "D:\\MeridianData";
        viewModel.NasdaqApiKey = " nasdaq-key ";
        viewModel.OpenFigiApiKey = "figi-key";

        await viewModel.SaveAndContinueCommand.ExecuteAsync(null);

        state.SavedProvider.Should().Be("Alpaca");
        state.SavedStorageLocation.Should().Be("D:\\MeridianData");
        state.SavedNasdaqKey.Should().Be(" nasdaq-key ");
        state.SavedOpenFigiKey.Should().Be("figi-key");
        navigator.LastPageTag.Should().Be("ResearchShell");
        notification.SuccessMessages.Should().Contain(message => message.Contains("Setup complete"));
        viewModel.ValidationStatusText.Should().Be("Configuration saved and backend verified. Opening Research Workspace...");
    }

    [Fact]
    public async Task ApplyPresetCommand_UsesFirstSupportedRecommendedProvider()
    {
        var state = new FakeSetupWizardStateService();
        var notification = new FakeSetupWizardNotificationSink();
        var viewModel = CreateViewModel(state, notification);
        await viewModel.LoadAsync();

        await viewModel.ApplyPresetCommand.ExecuteAsync("minimal");

        state.AppliedPresetId.Should().Be("minimal");
        state.AppliedProvider.Should().Be("Alpaca");
        viewModel.SelectedProvider.Should().Be("Alpaca");
        viewModel.PresetStatusText.Should().Contain("Applied \"Minimal Setup\" preset");
        notification.SuccessMessages.Should().Contain(message => message.Contains("Minimal Setup"));
    }

    private static SetupWizardViewModel CreateViewModel(
        FakeSetupWizardStateService state,
        FakeSetupWizardNotificationSink? notification = null,
        FakeSetupWizardNavigator? navigator = null)
        => new(
            state,
            notification ?? new FakeSetupWizardNotificationSink(),
            navigator ?? new FakeSetupWizardNavigator(),
            new FakeSetupWizardLinkLauncher());

    private sealed class FakeSetupWizardStateService : ISetupWizardStateService
    {
        public Queue<SetupWizardBackendCheckResult> BackendChecks { get; } = new();

        public IReadOnlyList<string> ProviderOptions { get; } =
            ["Synthetic", "Alpaca", "Polygon", "NasdaqDataLink"];

        public string ConfigFilePath { get; } = "D:\\Meridian-main\\config\\appsettings.json";

        public int StartBackendCalls { get; private set; }

        public int SaveConfigurationCalls { get; private set; }

        public string? SavedProvider { get; private set; }

        public string? SavedStorageLocation { get; private set; }

        public string? SavedNasdaqKey { get; private set; }

        public string? SavedOpenFigiKey { get; private set; }

        public string? AppliedPresetId { get; private set; }

        public string? AppliedProvider { get; private set; }

        public SetupWizardSaveResult SaveResult { get; init; } =
            new(true, "Configuration saved.");

        public IReadOnlyList<SetupPreset> GetSetupPresets()
            =>
            [
                new()
                {
                    Id = "minimal",
                    Name = "Minimal Setup",
                    Description = "Basic configuration for testing and evaluation",
                    RecommendedProviders = ["Alpaca", "Polygon"],
                    DefaultSymbols = ["SPY"],
                    SubscribeTrades = true,
                    SubscribeDepth = false,
                    EnableBackfill = false
                }
            ];

        public Task<SetupWizardConfigurationState> LoadConfigurationAsync(CancellationToken ct = default)
            => Task.FromResult(new SetupWizardConfigurationState(
                ProviderOptions,
                "Synthetic",
                "data",
                "Loaded existing valid configuration."));

        public SetupWizardApiKeyState LoadStoredApiKeys()
            => new("nasdaq-key", "figi-key");

        public Task ApplyPresetAsync(SetupPreset preset, string provider, CancellationToken ct = default)
        {
            AppliedPresetId = preset.Id;
            AppliedProvider = provider;
            return Task.CompletedTask;
        }

        public Task<BackendServiceOperationResult> StartBackendAsync(CancellationToken ct = default)
        {
            StartBackendCalls++;
            return Task.FromResult(BackendServiceOperationResult.SuccessResult("Backend service started."));
        }

        public Task<SetupWizardBackendCheckResult> CheckBackendAsync(CancellationToken ct = default)
            => Task.FromResult(BackendChecks.Count > 0
                ? BackendChecks.Dequeue()
                : new SetupWizardBackendCheckResult(
                    true,
                    "Healthy",
                    5,
                    "Backend is running and healthy.",
                    "http://localhost:8080"));

        public Task<SetupWizardSaveResult> SaveConfigurationAsync(
            string provider,
            string storageLocation,
            CancellationToken ct = default)
        {
            SaveConfigurationCalls++;
            SavedProvider = provider;
            SavedStorageLocation = storageLocation;
            return Task.FromResult(SaveResult);
        }

        public void SaveApiKeys(string nasdaqDataLinkApiKey, string openFigiApiKey)
        {
            SavedNasdaqKey = nasdaqDataLinkApiKey;
            SavedOpenFigiKey = openFigiApiKey;
        }
    }

    private sealed class FakeSetupWizardNotificationSink : ISetupWizardNotificationSink
    {
        public List<string> SuccessMessages { get; } = new();

        public void NotifySuccess(string title, string message)
            => SuccessMessages.Add($"{title}: {message}");

        public void NotifyWarning(string title, string message)
        {
        }

        public void NotifyInfo(string title, string message)
        {
        }

        public Task NotifyErrorAsync(string title, string message)
            => Task.CompletedTask;
    }

    private sealed class FakeSetupWizardNavigator : ISetupWizardNavigator
    {
        public string? LastPageTag { get; private set; }

        public void NavigateTo(string pageTag)
            => LastPageTag = pageTag;
    }

    private sealed class FakeSetupWizardLinkLauncher : ISetupWizardLinkLauncher
    {
        public void Open(string url)
        {
        }
    }
}
