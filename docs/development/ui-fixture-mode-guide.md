# UI Fixture Mode for Offline Development

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-10-05

Use fixture mode to review supported WPF screens with sample responses. The desktop shell runs on
Windows; shared fixture-service tests can run on other supported .NET hosts. Fixture coverage is
service-specific, so a screen without a fixture branch may still need the local backend.

## Prerequisites

Run from the repository root on Windows with PowerShell 7 and the .NET SDK selected by
`global.json`. Restore dependencies before attempting offline work. Use the
[desktop testing guide](desktop-testing-guide.md) for build prerequisites and Windows validation.

## Start the desktop with its local backend

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/dev/run-desktop.ps1 -LaunchMode Development -Fixture
```

The launcher starts the backend with the synthetic provider and enables fixture mode for the WPF
shell. Development mode sets the Development environment and explicitly opts into local in-memory
governance. The launcher restores its environment overrides afterward and stops its owned backend
when the desktop closes. This is a local development setup; follow
[provider setup](../operators/provider-credentials.md) and the
[environment reference](../reference/environment-variables.md) for real provider and persistence settings.

Expected result: the desktop opens with sample-data provenance visible, without requiring live
provider credentials. Launch logs are written to `artifacts/desktop-launcher*.log`. If startup fails,
inspect those logs and rerun without `-NoBuild` after resolving missing SDK/assets or backend startup
errors. See [process lifecycle diagnostics](process-lifecycle-diagnostics.md) for stale processes.

## Set fixture mode directly

The application reads `MDC_FIXTURE_MODE=1` or the `--fixture` argument. Use these direct launches when
you are testing the desktop startup path itself; they do not perform the launcher's backend setup.

Windows PowerShell, from the repository root:

```powershell
$env:MDC_FIXTURE_MODE = "1"
dotnet run --project src/Meridian.Wpf/Meridian.Wpf.csproj
# Clear the setting when finished, or restore its previous value if one was set.
Remove-Item Env:MDC_FIXTURE_MODE
```

Alternatively, pass the application argument:

```powershell
dotnet run --project src/Meridian.Wpf/Meridian.Wpf.csproj -- --fixture
```

WPF cannot run on Linux/macOS. Its non-Windows project target is an empty compatibility library;
adding Windows targeting flags does not provide a desktop runtime.

## Fixture responses and synthetic ingestion

| Need | Use |
| --- | --- |
| Sample responses in supported desktop services | `MDC_FIXTURE_MODE=1`, `--fixture`, or the launcher `-Fixture` switch |
| Exercise the actual ingestion/backfill pipeline without live provider credentials | The Synthetic provider, configured through `DataSource: Synthetic` with `Synthetic.Enabled: true`, or `MDC_SYNTHETIC_MODE=1` |
| Reproducible unit tests | Explicit inputs and the test project's fixture/time-provider patterns |

Fixture mode does not prove provider connectivity, production persistence, or real backend integration.
Several fixture methods use current timestamps, symbol hash codes, and randomized simulated delays;
do not assume responses are byte-for-byte identical across processes or that every screen is mocked.

## Source and fixture API

The implementation is owned by
[FixtureDataService](../../src/Meridian.Ui.Services/Services/FixtureDataService.cs) and
[FixtureModeDetector](../../src/Meridian.Ui.Services/Services/FixtureModeDetector.cs).

| Method | Response |
| --- | --- |
| `GetMockStatusResponse()` | Connected status, pipeline counters, and metrics |
| `GetMockDisconnectedStatus()` | Disconnected status with no metrics or pipeline data |
| `GetMockTradeData(symbol)` | Sample trade for one symbol |
| `GetMockQuoteData(symbol)` | Sample bid/ask quote |
| `GetMockTradesResponse(symbol, count)` | A collection of sample trades |
| `GetMockBackfillHealth()` | Sample provider health |
| `GetMockSymbols()` | Sample symbols |
| `SetScenario(scenario)` | Selects Connected, Disconnected, Degraded, Error, or Loading |

For a bounded test setup, use the actual detector API and restore shared state:

```csharp
using Meridian.Ui.Services.Services;

var detector = FixtureModeDetector.Instance;
var wasEnabled = detector.IsFixtureMode;
var previousScenario = FixtureDataService.Instance.ActiveScenario;
try
{
    detector.SetFixtureMode(true);
    FixtureDataService.Instance.SetScenario(FixtureScenario.Connected);
    var status = FixtureDataService.Instance.GetMockStatusResponse();
    // Assert the fields required by this test.
}
finally
{
    FixtureDataService.Instance.SetScenario(previousScenario);
    detector.SetFixtureMode(wasEnabled);
}
```

These are shared singletons. Follow the owning test project's isolation conventions and avoid
parallel tests that mutate the same detector/scenario. Selecting a scenario does not make every
`GetMock*` method scenario-aware; verify the service consuming the scenario.

## Extend and validate

Use the existing
[fixture service tests](../../tests/Meridian.Ui.Tests/Services/FixtureDataServiceTests.cs) and
[detector tests](../../tests/Meridian.Ui.Tests/Services/FixtureModeDetectorTests.cs) as examples.
When adding fixture behavior, update the shared API-contract response and its tests, then connect
it through the existing service and WPF startup wiring. Do not replace the application's startup
method or introduce another fixture-mode singleton. Preserve visible sample-data provenance.

Run the focused shared-service tests from the repository root:

```powershell
python build/python/cli/buildctl.py test --project tests/Meridian.Ui.Tests/Meridian.Ui.Tests.csproj --filter "FullyQualifiedName~FixtureDataServiceTests|FullyQualifiedName~FixtureModeDetectorTests" --queue
```

Expected result: the selected tests pass. If a fixture test fails, compare the response with current
contracts and check for singleton state leaked from another test before changing expected values.
For WPF integration, run the Windows desktop validation lane and inspect the actual screen; shared
fixture tests alone do not verify desktop rendering.

## Related guidance

- [Desktop testing guide](desktop-testing-guide.md)
- [WPF implementation notes](wpf-implementation-notes.md)
- [Desktop support policy](policies/desktop-support-policy.md)
- [Desktop workflow automation](desktop-workflow-automation.md)
