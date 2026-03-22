# TODO Tracking

> Auto-generated TODO documentation. Do not edit manually.
> Last updated: 2026-03-22T03:10:14.239283+00:00

## Summary

| Metric | Count |
|--------|-------|
| **Total Items** | 50 |
| **Linked to Issues** | 0 |
| **Untracked** | 50 |

### By Type

| Type | Count | Description |
|------|-------|-------------|
| `TODO` | 36 | General tasks to complete |
| `NOTE` | 14 | Important notes and documentation |

### By Directory

| Directory | Count |
|-----------|-------|
| `docs/` | 36 |
| `tests/` | 9 |
| `src/` | 3 |
| `.github/` | 2 |

## Unassigned & Untracked

50 items have no assignee and no issue tracking:

Consider assigning ownership or creating tracking issues for these items.

## All Items

### TODO (36)

- [ ] `docs\examples\provider-template\TemplateFactory.cs:157`
  > Register a named HttpClient for this provider: services.AddHttpClient(HttpClientNames.TemplateHistorical, client => { client.BaseAddress = new Uri(TemplateEndpoints.BaseUrl); client.DefaultRequestHeaders.Add("Accept", "application/json"); });

- [ ] `docs\examples\provider-template\TemplateFactory.cs:164`
  > Bind streaming options from configuration: services.AddOptions<TemplateStreamingOptions>() .BindConfiguration("Template");

- [ ] `docs\examples\provider-template\TemplateFactory.cs:168`
  > Register data sources in the DataSourceRegistry if needed. registry.Register(new DataSourceConfiguration("template", ...));

- [ ] `docs\examples\provider-template\TemplateHistoricalDataProvider.cs:29`
  > Replace "template" with the provider ID, display name, type, and category.

- [ ] `docs\examples\provider-template\TemplateHistoricalDataProvider.cs:34`
  > Replace with the actual API key environment variable name.

- [ ] `docs\examples\provider-template\TemplateHistoricalDataProvider.cs:44`
  > Set to your provider's unique ID (lowercase, e.g., "tiingo").

- [ ] `docs\examples\provider-template\TemplateHistoricalDataProvider.cs:48`
  > Set a human-readable display name.

- [ ] `docs\examples\provider-template\TemplateHistoricalDataProvider.cs:52`
  > Describe the provider's capabilities and data coverage.

- [ ] `docs\examples\provider-template\TemplateHistoricalDataProvider.cs:56`
  > Set the named HTTP client registered in the DI container. Add a constant to HttpClientNames and register the client in the composition root.

- [ ] `docs\examples\provider-template\TemplateHistoricalDataProvider.cs:65`
  > Set an appropriate priority (lower = tried first in failover chains).

- [ ] `docs\examples\provider-template\TemplateHistoricalDataProvider.cs:69`
  > Set based on the provider's rate limit (e.g., 60 req/min → 1 s delay).

- [ ] `docs\examples\provider-template\TemplateHistoricalDataProvider.cs:79`
  > Adjust to match what the provider actually supports.

- [ ] `docs\examples\provider-template\TemplateHistoricalDataProvider.cs:103`
  > Add required HTTP headers for this provider. Example: Http.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");

- [ ] `docs\examples\provider-template\TemplateHistoricalDataProvider.cs:126`
  > Build the request URL using TemplateEndpoints constants.

- [ ] `docs\examples\provider-template\TemplateHistoricalDataProvider.cs:127`
  > Apply rate limiting before each request: await WaitForRateLimitSlotAsync(ct).ConfigureAwait(false);

- [ ] `docs\examples\provider-template\TemplateHistoricalDataProvider.cs:129`
  > Call Http.GetAsync(url, ct) with the resilience pipeline.

- [ ] `docs\examples\provider-template\TemplateHistoricalDataProvider.cs:130`
  > Deserialize the response and map to List<HistoricalBar>.

- [ ] `docs\examples\provider-template\TemplateHistoricalDataProvider.cs:131`
  > Normalize symbol, convert timestamps to UTC.

- [ ] `docs\examples\provider-template\TemplateHistoricalDataProvider.cs:132`
  > Log success: Log.Debug("Fetched {Count} bars for {Symbol}", bars.Count, symbol);

- [ ] `docs\examples\provider-template\TemplateMarketDataClient.cs:29`
  > Replace "template" with the provider ID, display name, type, and category.

- [ ] `docs\examples\provider-template\TemplateMarketDataClient.cs:40`
  > Replace TemplateOptions with the provider's configuration type. private readonly TemplateOptions _options;

- [ ] `docs\examples\provider-template\TemplateSymbolSearchProvider.cs:28`
  > Replace "template" with the provider ID, display name, type, and category.

- [ ] `docs\examples\provider-template\TemplateSymbolSearchProvider.cs:38`
  > Set to your provider's unique ID (lowercase, e.g., "finnhub").

- [ ] `docs\examples\provider-template\TemplateSymbolSearchProvider.cs:42`
  > Set a human-readable display name.

- [ ] `docs\examples\provider-template\TemplateSymbolSearchProvider.cs:46`
  > Set the named HTTP client registered in the DI container.

- [ ] `docs\examples\provider-template\TemplateSymbolSearchProvider.cs:50`
  > Set the base URL for this provider's REST API.

- [ ] `docs\examples\provider-template\TemplateSymbolSearchProvider.cs:54`
  > Set the environment variable name used to load the API key.

- [ ] `docs\examples\provider-template\TemplateSymbolSearchProvider.cs:70`
  > If the provider supports asset-type filtering, override SupportedAssetTypes. public override IReadOnlyList<string> SupportedAssetTypes => ["stock", "etf", "crypto"];

- [ ] `docs\examples\provider-template\TemplateSymbolSearchProvider.cs:73`
  > If the provider supports exchange filtering, override SupportedExchanges. public override IReadOnlyList<string> SupportedExchanges => ["NYSE", "NASDAQ"];

- [ ] `docs\examples\provider-template\TemplateSymbolSearchProvider.cs:98`
  > Add provider-specific authentication headers. Example: if (!string.IsNullOrEmpty(ApiKey)) Http.DefaultRequestHeaders.Add("Authorization", $"Bearer {ApiKey}");

- [ ] `docs\examples\provider-template\TemplateSymbolSearchProvider.cs:123`
  > Build the request URL from BaseUrl and query parameters.

- [ ] `docs\examples\provider-template\TemplateSymbolSearchProvider.cs:124`
  > Send the HTTP request and handle errors.

- [ ] `docs\examples\provider-template\TemplateSymbolSearchProvider.cs:125`
  > Deserialize the JSON response.

- [ ] `docs\examples\provider-template\TemplateSymbolSearchProvider.cs:126`
  > Map provider-specific DTOs to SymbolSearchResult.

- [ ] `docs\examples\provider-template\TemplateSymbolSearchProvider.cs:127`
  > Return the mapped results (up to maxResults).

- [ ] `docs\examples\provider-template\TemplateSymbolSearchProvider.cs:147`
  > If the provider supports server-side filtering, implement it here. Otherwise, keep this call to the base class which filters client-side.

### NOTE (14)

- [ ] `.github\workflows\desktop-builds.yml:9`
  > UWP/WinUI 3 application has been removed. WPF is the sole desktop client.

- [ ] `.github\workflows\test-matrix.yml:5`
  > This workflow intentionally does NOT use reusable-dotnet-build.yml because it needs separate C# / F# test runs with per-language arguments, a Category!=Integration filter, platform-conditional jobs, and per-platform Codecov flags. The reusable template targets simpler "build + test entire solution" scenarios.

- [ ] `src\Meridian.Ui.Services\Services\AdminMaintenanceModels.cs:411`
  > SelfTest*, ErrorCodes*, ShowConfig*, QuickCheck* models are defined in DiagnosticsService.cs to avoid duplication and maintain single source of truth

- [ ] `src\Meridian.Ui.Services\Services\ProviderHealthService.cs:516`
  > ProviderComparison is defined in AdvancedAnalyticsModels.cs for cross-provider comparison ProviderHealthComparison below is for overall provider ranking

- [ ] `src\Meridian.Wpf\GlobalUsings.cs:7`
  > Type aliases and Contracts namespaces are NOT re-defined here because they are already provided by the referenced Meridian.Ui.Services project (via its GlobalUsings.cs). Re-defining them would cause CS0101 duplicate type definition errors.

- [ ] `tests\Meridian.Tests\Application\Backfill\BackfillWorkerServiceTests.cs:28`
  > Using null! because validation throws before dependencies are accessed

- [ ] `tests\Meridian.Tests\Application\Backfill\BackfillWorkerServiceTests.cs:55`
  > Using null! because validation throws before dependencies are accessed

- [ ] `tests\Meridian.Tests\Application\Backfill\BackfillWorkerServiceTests.cs:84`
  > Using null! dependencies - we only verify that ArgumentOutOfRangeException is not thrown The constructor may throw other exceptions (e.g., NullReferenceException) when accessing null dependencies

- [ ] `tests\Meridian.Tests\Application\Monitoring\DataQuality\DataFreshnessSlaMonitorTests.cs:525`
  > Actual result depends on current time, so we check the logic is working

- [ ] `tests\Meridian.Tests\Application\Pipeline\FSharpEventValidatorTests.cs:72`
  > Trade.ctor only checks Price > 0, so $2,000,000 is constructible.

- [ ] `tests\Meridian.Tests\Storage\StorageChecksumServiceTests.cs:121`
  > File.WriteAllTextAsync uses UTF-8 with BOM by default on some platforms, so we compute expected from the actual file bytes

- [ ] `tests\Meridian.Ui.Tests\Services\ScheduledMaintenanceServiceTests.cs:85`
  > since this is a singleton shared across tests, if StartScheduler was previously called, we stop it first to ensure test isolation.

- [ ] `tests\Meridian.Wpf.Tests\Services\OfflineTrackingPersistenceServiceTests.cs:27`
  > Singleton state may persist across tests. We explicitly shut down first to verify the default state transition.

- [ ] `tests\Meridian.Wpf.Tests\Services\PendingOperationsQueueServiceTests.cs:30`
  > This may not be false if other tests have run InitializeAsync. We test the lifecycle explicitly below.

---

## Contributing

When adding TODO comments, please follow these guidelines:

1. **Link to GitHub Issues**: Use `// TODO: Track with issue #123` format
2. **Be Descriptive**: Explain what needs to be done and why
3. **Use Correct Type**:
   - `TODO` - General tasks
   - `FIXME` - Bugs that need fixing
   - `HACK` - Temporary workarounds
   - `NOTE` - Important information

Example:
```csharp
// TODO: Track with issue #123 - Implement retry logic for transient failures
// This is needed because the API occasionally returns 503 errors during peak load.
```
