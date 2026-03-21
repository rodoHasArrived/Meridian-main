# Readability Refactor Technical Design Pack

**Date:** 2026-03-20  
**Status:** Draft target design  
**Companion to:** `docs/plans/readability-refactor-roadmap.md`

---

## Overview

This design pack turns the readability roadmap into a concrete target architecture that fits the repository's existing conventions:

- thin hosts,
- shared UI services,
- contracts-first DTOs,
- and enforced layer boundaries.

The key constraints remain unchanged:

- hosts stay thin,
- `Ui.Services` remains platform-neutral,
- shared types belong in `Contracts` or `Core`,
- composition stays centralized even when implemented through smaller modules.

---

## 1. Proposed Target Folder and File Structure

### 1.1 Host startup refactor

`Program.cs` should move toward a thin bootstrapper that delegates to explicit startup workflows.

```text
src/Meridian/
  Program.cs
  Startup/
    StartupOrchestrator.cs
    StartupBootstrap.cs
    StartupContext.cs
    StartupPlan.cs
    StartupValidationResult.cs
    HostMode.cs
    ModeRunners/
      CommandModeRunner.cs
      WebModeRunner.cs
      DesktopModeRunner.cs
      CollectorModeRunner.cs
      BackfillModeRunner.cs
    Phases/
      ParseArgumentsPhase.cs
      LoadConfigurationPhase.cs
      ValidateEnvironmentPhase.cs
      BuildHostPhase.cs
      StartRuntimePhase.cs
```

**Why this fits the repo**

The architecture already frames startup around `Program.cs`, `HostStartup`, and `ServiceCompositionRoot`. This structure makes those concepts more explicit without replacing them.

### 1.2 Composition root refactor

`ServiceCompositionRoot` should remain the top-level entry point, but feature registration should move into smaller files aligned to capability areas.

```text
src/Meridian.Application/Composition/
  ServiceCompositionRoot.cs
  HostStartup.cs
  HostStartupFactory.cs
  CompositionProfiles/
    HostCompositionProfile.cs
    WebHostProfile.cs
    DesktopHostProfile.cs
    CollectorHostProfile.cs
    BackfillHostProfile.cs
  Features/
    ConfigurationFeatureRegistration.cs
    StorageFeatureRegistration.cs
    CredentialFeatureRegistration.cs
    ProviderFeatureRegistration.cs
    SymbolManagementFeatureRegistration.cs
    BackfillFeatureRegistration.cs
    MaintenanceFeatureRegistration.cs
    DiagnosticsFeatureRegistration.cs
    PipelineFeatureRegistration.cs
    CollectorFeatureRegistration.cs
    CanonicalizationFeatureRegistration.cs
    HttpClientFeatureRegistration.cs
```

**Why this fits the repo**

This preserves the single-source-of-truth composition story while making the structure line up with the architecture's named application modules.

### 1.3 Contracts for data quality and shared UI APIs

`Contracts` should own typed request/response shapes for the data-quality experience.

```text
src/Meridian.Contracts/
  Api/
    Quality/
      DataQualityDashboardResponse.cs
      DataQualityRealtimeMetrics.cs
      SymbolHealthDto.cs
      QualityCompletenessStatsDto.cs
      QualityGapStatsDto.cs
      QualitySequenceStatsDto.cs
      QualityLatencyDistributionDto.cs
      QualityAlertsResponse.cs
      QualityAnomaliesResponse.cs
      AcknowledgeAlertRequest.cs
      AcknowledgeAlertResponse.cs
```

**Why this fits the repo**

This prevents WPF or shared UI code from owning backend contract shape and aligns with the existing contracts-first pattern.

### 1.4 Shared UI service layer for data quality

The cleanest migration is to build on the existing `Ui.Services` model rather than invent a new host-specific pattern.

```text
src/Meridian.Ui.Services/
  Contracts/
    IDataQualityApiClient.cs
    IDataQualityRefreshScheduler.cs
    IDataQualityPresentationService.cs
  Services/
    DataQuality/
      DataQualityApiClient.cs
      DataQualityRefreshScheduler.cs
      DataQualityPresentationService.cs
      DataQualityPresentationModels.cs
      DataQualityFilterState.cs
      DataQualityMappers.cs
```

**Why this fits the repo**

`Ui.Services` is explicitly intended to hold shared service logic and platform-neutral behavior. That is where HTTP orchestration and presentation mapping belong.

### 1.5 WPF page and viewmodel layout

The WPF layer should retain page/viewmodel ownership while shedding shared orchestration concerns.

```text
src/Meridian.Wpf/
  ViewModels/
    Pages/
      DataQualityPageViewModel.cs
  Views/
    DataQualityPage.xaml
    DataQualityPage.xaml.cs
  Converters/
    ScoreToBrushConverter.cs
    SeverityToBrushConverter.cs
    RelativeTimeConverter.cs
```

**Why this fits the repo**

This follows the current WPF organization while moving platform-neutral retrieval and shaping logic out of WPF and into `Ui.Services`.

---

## 2. Startup Orchestration Types

### `StartupContext`

Responsibility: immutable startup state passed between phases.

```csharp
public sealed record StartupContext(
    CliArguments CliArgs,
    string ConfigPath,
    AppConfig? Config,
    DeploymentContext Deployment,
    ILogger Log);
```

### `StartupPlan`

Responsibility: a concise description of what startup will do.

```csharp
public sealed record StartupPlan(
    HostMode Mode,
    bool RequiresConfigValidation,
    bool RequiresSchemaValidation,
    bool RequiresHostStartup,
    bool RequiresUiServer);
```

### `IStartupPhase`

```csharp
public interface IStartupPhase
{
    Task<StartupContext> ExecuteAsync(StartupContext context, CancellationToken ct = default);
}
```

### `IHostModeRunner`

```csharp
public interface IHostModeRunner
{
    Task<int> RunAsync(StartupContext context, CancellationToken ct = default);
}
```

### `StartupOrchestrator`

Responsibility: build the startup plan, run phases, and dispatch to the correct runner.

---

## 3. Composition Layer Types

### `HostCompositionProfile`

```csharp
public sealed record HostCompositionProfile(
    string Name,
    bool EnableCredentialServices,
    bool EnableProviderServices,
    bool EnableSymbolManagement,
    bool EnableBackfillServices,
    bool EnableMaintenanceServices,
    bool EnableDiagnosticServices,
    bool EnablePipelineServices,
    bool EnableCollectorServices,
    bool EnableCanonicalizationServices,
    bool EnableHttpClientFactory);
```

### `IServiceFeatureRegistration`

```csharp
public interface IServiceFeatureRegistration
{
    IServiceCollection Register(IServiceCollection services, CompositionOptions options);
}
```

### Example responsibility boundaries

- `ConfigurationFeatureRegistration`: config store, config service, templates, dry-run services.
- `StorageFeatureRegistration`: storage options, source registry, maintenance/search/export services.
- `PipelineFeatureRegistration`: event pipeline, validators, and pipeline policy wiring.
- `HostProfileFactory`: map runtime host types to composition profiles.

---

## 4. Shared UI Service Contracts

The new data-quality services should follow the same testable contract style already used elsewhere in `Ui.Services`.

### `IDataQualityApiClient`

```csharp
public interface IDataQualityApiClient
{
    Task<DataQualityDashboardResponse?> GetDashboardAsync(CancellationToken ct = default);
    Task<QualityAlertsResponse?> GetAlertsAsync(CancellationToken ct = default);
    Task<QualityAnomaliesResponse?> GetAnomaliesAsync(CancellationToken ct = default);
    Task<QualityLatencyDistributionDto?> GetLatencyDistributionAsync(CancellationToken ct = default);
    Task<AcknowledgeAlertResponse> AcknowledgeAlertAsync(string alertId, CancellationToken ct = default);
}
```

### `IDataQualityPresentationService`

```csharp
public interface IDataQualityPresentationService
{
    DataQualityScreenState BuildScreenState(
        DataQualityDashboardResponse dashboard,
        QualityAlertsResponse? alerts,
        QualityAnomaliesResponse? anomalies,
        QualityLatencyDistributionDto? latency,
        DataQualityFilterState filter);
}
```

### `IDataQualityRefreshScheduler`

```csharp
public interface IDataQualityRefreshScheduler : IDisposable
{
    void Start(Func<CancellationToken, Task> refreshAction, TimeSpan interval);
    void Stop();
}
```

---

## 5. Shared Presentation Models

The target is to centralize UI shaping into immutable models so WPF pages and viewmodels stop owning backend interpretation details.

### `DataQualityScreenState`

```csharp
public sealed record DataQualityScreenState(
    QualityHeaderState Header,
    IReadOnlyList<SymbolQualityRow> Symbols,
    IReadOnlyList<GapRow> Gaps,
    IReadOnlyList<AlertRow> Alerts,
    IReadOnlyList<AnomalyRow> Anomalies,
    LatencyChartState Latency,
    TrendSummaryState Trend);
```

### `QualityHeaderState`

```csharp
public sealed record QualityHeaderState(
    string OverallScoreText,
    string GradeText,
    string StatusText,
    double OverallScore,
    QualitySeverity Severity,
    string LastUpdateText,
    string CompletenessText,
    string HealthyFilesText,
    string WarningFilesText,
    string CriticalFilesText);
```

### `DataQualityFilterState`

```csharp
public sealed record DataQualityFilterState(
    string SymbolQuery,
    string SeverityFilter,
    string TimeRange);
```

---

## 6. WPF-Facing Types

### `DataQualityPageViewModel`

Responsibility: bindable page state plus commands only.

**Dependencies:**

- `IDataQualityApiClient`
- `IDataQualityPresentationService`
- `IDataQualityRefreshScheduler`
- `ILoggingService`
- `INotificationService`

If needed, add a reusable async command implementation for refresh, alert acknowledgment, and filter changes.

---

## 7. Sample API Contracts

These examples illustrate the target contract shape that should replace manual JSON walking in WPF code.

### Dashboard response

```csharp
namespace Meridian.Contracts.Api.Quality;

public sealed record DataQualityDashboardResponse(
    DataQualityRealtimeMetrics RealtimeMetrics,
    QualityCompletenessStatsDto CompletenessStats,
    QualityGapStatsDto GapStats,
    QualitySequenceStatsDto SequenceStats,
    DateTimeOffset GeneratedAt);
```

### Realtime metrics

```csharp
public sealed record DataQualityRealtimeMetrics(
    double OverallHealthScore,
    double AverageLatencyMs,
    IReadOnlyList<SymbolHealthDto> SymbolHealth);
```

### Symbol health

```csharp
public sealed record SymbolHealthDto(
    string Symbol,
    double Score,
    string State,
    DateTimeOffset LastEvent,
    IReadOnlyList<string> ActiveIssues);
```

### Completeness stats

```csharp
public sealed record QualityCompletenessStatsDto(
    double AverageScore,
    IReadOnlyDictionary<string, int> GradeDistribution,
    DateTimeOffset CalculatedAt);
```

### Alerts

```csharp
public sealed record QualityAlertsResponse(
    IReadOnlyList<QualityAlertDto> Items,
    int UnacknowledgedCount,
    int TotalCount);

public sealed record QualityAlertDto(
    string Id,
    string Severity,
    string Symbol,
    string Message,
    string Details,
    DateTimeOffset CreatedAt,
    bool IsAcknowledged);
```

### Anomalies

```csharp
public sealed record QualityAnomaliesResponse(
    IReadOnlyList<QualityAnomalyDto> Items);

public sealed record QualityAnomalyDto(
    string Id,
    string Type,
    string Severity,
    string Symbol,
    string Description,
    DateTimeOffset DetectedAt);
```

---

## 8. Migration Example — `Program.cs`

### Current problem

`Program.cs` currently mixes bootstrap, config loading, command dispatch, host selection, validation, schema checks, host startup, backfill behavior, UI startup, and pipeline recovery.

### Target end state

```csharp
public partial class Program
{
    public static async Task<int> Main(string[] args)
    {
        var bootstrap = StartupBootstrap.Create(args);
        return await bootstrap.Orchestrator.RunAsync(bootstrap.Context);
    }
}
```

### `StartupBootstrap`

```csharp
public static class StartupBootstrap
{
    public static StartupBootstrapResult Create(string[] args)
    {
        var cliArgs = CliArguments.Parse(args);
        var configPath = ConfigPathResolver.Resolve(cliArgs);
        var initialConfig = MinimalConfigLoader.Load(configPath);

        LoggingSetup.Initialize(dataRoot: initialConfig.DataRoot);
        var log = LoggingSetup.ForContext("Program");

        var deployment = DeploymentContext.FromArgs(args, configPath);

        var context = new StartupContext(
            CliArgs: cliArgs,
            ConfigPath: configPath,
            Config: null,
            Deployment: deployment,
            Log: log);

        return new StartupBootstrapResult(
            Context: context,
            Orchestrator: StartupOrchestrator.CreateDefault());
    }
}
```

### `StartupOrchestrator`

```csharp
public sealed class StartupOrchestrator
{
    public async Task<int> RunAsync(StartupContext context, CancellationToken ct = default)
    {
        context = await _loadConfiguration.ExecuteAsync(context, ct);
        var plan = _planner.Build(context);

        if (plan.Mode == HostMode.Command)
            return await _commandRunner.RunAsync(context, ct);

        context = await _validateEnvironment.ExecuteAsync(context, ct);

        return plan.Mode switch
        {
            HostMode.Web => await _webRunner.RunAsync(context, ct),
            HostMode.Desktop => await _desktopRunner.RunAsync(context, ct),
            HostMode.Collector => await _collectorRunner.RunAsync(context, ct),
            HostMode.Backfill => await _backfillRunner.RunAsync(context, ct),
            _ => 1
        };
    }
}
```

### Benefits

- `Program.cs` becomes readable in seconds.
- Startup ordering becomes explicit.
- Each mode becomes unit-testable.
- Failure points become isolated.

---

## 9. Migration Example — `ServiceCompositionRoot`

### Current problem

The current composition root is the right concept, but too much registration detail and ordering logic lives in one file.

### Target end state

```csharp
public static class ServiceCompositionRoot
{
    public static IServiceCollection AddMarketDataServices(
        this IServiceCollection services,
        CompositionOptions? options = null)
    {
        options ??= CompositionOptions.Default;

        services.RegisterFeature(new ConfigurationFeatureRegistration(), options);
        services.RegisterFeature(new StorageFeatureRegistration(), options);
        services.RegisterFeature(new CredentialFeatureRegistration(), options);
        services.RegisterFeature(new ProviderFeatureRegistration(), options);
        services.RegisterFeature(new SymbolManagementFeatureRegistration(), options);
        services.RegisterFeature(new BackfillFeatureRegistration(), options);
        services.RegisterFeature(new MaintenanceFeatureRegistration(), options);
        services.RegisterFeature(new DiagnosticsFeatureRegistration(), options);
        services.RegisterFeature(new PipelineFeatureRegistration(), options);
        services.RegisterFeature(new CollectorFeatureRegistration(), options);
        services.RegisterFeature(new CanonicalizationFeatureRegistration(), options);
        services.RegisterFeature(new HttpClientFeatureRegistration(), options);

        return services;
    }

    public static void InitializeCircuitBreakerCallbackRouter(IServiceProvider sp)
    {
        var cbService = sp.GetService<CircuitBreakerStatusService>();
        if (cbService != null)
            CircuitBreakerCallbackRouter.Initialize(cbService);
    }

    private static IServiceCollection RegisterFeature(
        this IServiceCollection services,
        IServiceFeatureRegistration feature,
        CompositionOptions options)
        => feature.Register(services, options);
}
```

### Example feature registration

```csharp
internal sealed class StorageFeatureRegistration : IServiceFeatureRegistration
{
    public IServiceCollection Register(IServiceCollection services, CompositionOptions options)
    {
        services.AddSingleton<StorageOptions>(sp =>
        {
            var configStore = sp.GetRequiredService<ConfigStore>();
            var config = configStore.Load();
            var compressionEnabled = config.Compress ?? false;

            return config.Storage?.ToStorageOptions(config.DataRoot, compressionEnabled)
                ?? StorageProfilePresets.CreateFromProfile(null, config.DataRoot, compressionEnabled);
        });

        services.AddSingleton<IFileMaintenanceService, FileMaintenanceService>();
        services.AddSingleton<IDataQualityService, DataQualityService>();
        services.AddSingleton<IStorageSearchService, StorageSearchService>();
        services.AddSingleton<ITierMigrationService, TierMigrationService>();

        return services;
    }
}
```

### Optional profile layer

```csharp
public static class HostCompositionProfiles
{
    public static CompositionOptions ForDesktop(string configPath) => new()
    {
        ConfigPath = configPath,
        EnableCredentialServices = true,
        EnableProviderServices = true,
        EnableSymbolManagement = true,
        EnableBackfillServices = true,
        EnableMaintenanceServices = true,
        EnableDiagnosticServices = true,
        EnablePipelineServices = true,
        EnableCollectorServices = true,
        EnableCanonicalizationServices = true,
        EnableHttpClientFactory = true
    };
}
```

### Benefits

- The file becomes a manifest instead of a mega-file.
- Feature modules align with the architecture docs and team mental model.
- New registrations become additive instead of more nesting.

---

## 10. Migration Example — `DataQualityPage`

### Current problem

`DataQualityPage.xaml.cs` performs direct HTTP requests and manual `JsonElement` parsing.

The current viewmodel improves separation somewhat, but shared orchestration still remains trapped in WPF because the page/viewmodel own HTTP clients, timers, cancellation, refresh logic, and formatting concerns.

### Target end state

#### `IDataQualityApiClient`

```csharp
public interface IDataQualityApiClient
{
    Task<DataQualityDashboardResponse?> GetDashboardAsync(CancellationToken ct = default);
    Task<QualityAlertsResponse?> GetAlertsAsync(CancellationToken ct = default);
    Task<QualityAnomaliesResponse?> GetAnomaliesAsync(CancellationToken ct = default);
    Task<QualityLatencyDistributionDto?> GetLatencyDistributionAsync(CancellationToken ct = default);
}
```

#### `DataQualityApiClient`

```csharp
public sealed class DataQualityApiClient : IDataQualityApiClient
{
    private readonly ApiClientService _apiClient;

    public DataQualityApiClient(ApiClientService apiClient)
    {
        _apiClient = apiClient;
    }

    public Task<DataQualityDashboardResponse?> GetDashboardAsync(CancellationToken ct = default)
        => _apiClient.GetAsync<DataQualityDashboardResponse>("/api/quality/dashboard", ct);

    public Task<QualityAlertsResponse?> GetAlertsAsync(CancellationToken ct = default)
        => _apiClient.GetAsync<QualityAlertsResponse>("/api/quality/alerts", ct);

    public Task<QualityAnomaliesResponse?> GetAnomaliesAsync(CancellationToken ct = default)
        => _apiClient.GetAsync<QualityAnomaliesResponse>("/api/quality/anomalies", ct);

    public Task<QualityLatencyDistributionDto?> GetLatencyDistributionAsync(CancellationToken ct = default)
        => _apiClient.GetAsync<QualityLatencyDistributionDto>("/api/quality/latency", ct);
}
```

#### `DataQualityPresentationService`

```csharp
public sealed class DataQualityPresentationService : IDataQualityPresentationService
{
    public DataQualityScreenState BuildScreenState(
        DataQualityDashboardResponse dashboard,
        QualityAlertsResponse? alerts,
        QualityAnomaliesResponse? anomalies,
        QualityLatencyDistributionDto? latency,
        DataQualityFilterState filter)
    {
        // Maps DTOs to screen-friendly immutable models.
        // Applies grade/status/severity formatting rules centrally.
        // Applies filters.
    }
}
```

#### Target `DataQualityPageViewModel`

```csharp
public sealed class DataQualityPageViewModel : BindableBase, IDisposable
{
    private readonly IDataQualityApiClient _client;
    private readonly IDataQualityPresentationService _presentation;
    private readonly IDataQualityRefreshScheduler _scheduler;
    private readonly ILoggingService _logging;

    private DataQualityScreenState _screen = DataQualityScreenState.Empty;
    public DataQualityScreenState Screen
    {
        get => _screen;
        private set => SetProperty(ref _screen, value);
    }

    public IAsyncCommand RefreshCommand { get; }
    public IAsyncCommand<string> AcknowledgeAlertCommand { get; }

    public async Task InitializeAsync()
    {
        _scheduler.Start(RefreshAsync, TimeSpan.FromSeconds(30));
        await RefreshAsync(CancellationToken.None);
    }

    public Task ShutdownAsync()
    {
        _scheduler.Stop();
        return Task.CompletedTask;
    }

    private async Task RefreshAsync(CancellationToken ct)
    {
        var dashboard = await _client.GetDashboardAsync(ct);
        if (dashboard == null) return;

        var alerts = await _client.GetAlertsAsync(ct);
        var anomalies = await _client.GetAnomaliesAsync(ct);
        var latency = await _client.GetLatencyDistributionAsync(ct);

        Screen = _presentation.BuildScreenState(
            dashboard, alerts, anomalies, latency, CurrentFilter);
    }

    public void Dispose() => _scheduler.Dispose();
}
```

#### Target `DataQualityPage.xaml.cs`

```csharp
public partial class DataQualityPage : Page
{
    public DataQualityPage(DataQualityPageViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        Loaded += async (_, _) => await vm.InitializeAsync();
        Unloaded += async (_, _) => await vm.ShutdownAsync();
    }
}
```

### Benefits

- The WPF page becomes thin.
- DTO contracts become typed and reusable.
- Shared UI logic becomes testable in `Ui.Services`.
- Refresh/presentation logic can be reused across WPF and future host flows.

---

## 11. Recommended Dependency Direction for the New Design

### Startup

- `Meridian` host depends on `Meridian.Application` startup/orchestration abstractions.
- Host runners may stay in `Meridian` if they are host-specific.

### Data quality

- `Contracts` owns DTOs.
- `Ui.Services` owns shared API clients and presentation mappers.
- `Wpf` owns only viewmodels, lifecycle glue, bindings, and converters.

This keeps the dependency direction aligned with the documented rules:

- `Wpf -> Ui.Services`
- `Ui.Services -> Contracts`
- `Ui.Shared -> Application + Contracts`
- no host-to-host references

---

## 12. Suggested Implementation Order

1. Introduce DTOs and `IDataQualityApiClient` without changing the page.
2. Refactor the data quality viewmodel to consume the new client and remove direct `HttpClient` ownership.
3. Move formatting, filtering, and state-shaping into `DataQualityPresentationService`.
4. Reduce `DataQualityPage.xaml.cs` to lifecycle wiring only.
5. Apply the same pattern to other large pages.
6. Refactor `Program.cs`.
7. Refactor `ServiceCompositionRoot`.

This ordering gives visible benefit early while keeping the host stable.

---

## 13. First PR-Sized Slices

### PR 1

Add:

- `Meridian.Contracts/Api/Quality/*`
- `Ui.Services/Contracts/IDataQualityApiClient.cs`
- `Ui.Services/Services/DataQuality/DataQualityApiClient.cs`
- viewmodel updates to use the client

### PR 2

Add:

- `DataQualityPresentationService`
- immutable presentation models
- removal of formatting/mapping logic from the viewmodel

### PR 3

Slim `DataQualityPage.xaml.cs` to lifecycle wiring only.

### PR 4

Introduce `StartupOrchestrator` and extract the command-mode runner from `Program.cs`.

### PR 5

Start splitting `ServiceCompositionRoot` into feature registration files.

---

## 14. Adoption Guidance

Use this design pack as a target, not as a mandate to implement everything at once.

A good migration slice should:

- preserve runtime behavior,
- reduce file size or responsibility count in one hotspot,
- improve testability,
- and keep dependencies moving in the documented direction.
