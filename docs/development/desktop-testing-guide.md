# Desktop Development Testing Guide

This guide helps contributors set up and test Meridian's active WPF desktop operator surface.

> Migration note: desktop workflow orchestration commands are PowerShell-first as of April 2026. See [desktop-command-surface-migration.md](./desktop-command-surface-migration.md) for deprecated-to-supported command mappings.

## Quick Commands Reference

```bash
# Environment validation
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/dev/desktop-dev.ps1

# Fast script/profile check without restore or build
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/dev/desktop-dev.ps1 -SkipRestore -SkipBuild -SkipTestBuild -EmitJson

# Inner-loop WPF build plus focused tests
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/dev/test-wpf-dev.ps1

# Launch the fixture-backed desktop shell
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/dev/run-desktop.ps1 -Fixture

# Build desktop application
make desktop-build                # Build WPF desktop app

# Run tests
make desktop-test                 # Run all desktop-focused tests
make desktop-test-dev             # Run serialized WPF dev build plus focused tests
dotnet test tests/Meridian.Wpf.Tests        # WPF service tests (Windows only)
dotnet test tests/Meridian.Ui.Tests         # Shared UI service tests (Windows only)
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/dev/validate-position-blotter-route.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/dev/validate-operator-inbox-route.ps1
```

## Quick Start

### 1. Validate Your Development Environment

Run the desktop development bootstrap script to validate your environment:

```bash
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/dev/desktop-dev.ps1
```

This script validates:

- .NET 10 SDK selection and installed SDK inventory
- Windows SDK and Visual Studio Build Tools presence on Windows
- the selected desktop workflow profile, defaulting to `debug-startup`
- the WPF desktop project, WPF test project, and shared UI-services project paths
- isolated restore/build output under `artifacts/bin/<desktop-dev-*>` and `artifacts/obj/<desktop-dev-*>`
- the WPF desktop shell build and WPF desktop test-project build

Use `-Configuration Release` to match release build behavior, `-Profile <workflow-name>` to validate a different workflow profile, `-NoIsolation` only when you intentionally want standard `bin/` and `obj/` output, and `-EmitJson` when automation needs machine-readable step results. The script keeps workflow orchestration PowerShell-first; use `run-desktop.ps1` or `run-desktop-workflow.ps1` to launch or drive the shell after bootstrap succeeds.

**Actionable Fix Messages**: The script provides specific instructions for any missing components.

### 2. Run The WPF Development Loop

Use the development validation wrapper when you need the Release WPF build command to be repeatable during active desktop work:

```bash
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/dev/test-wpf-dev.ps1
```

This wrapper encodes the serialized WPF build lane that avoids common shared-output and compiler-server contention:

```bash
dotnet build src/Meridian.Wpf/Meridian.Wpf.csproj -c Release --no-restore /m:1 /nr:false /p:BuildInParallel=false /p:UseSharedCompilation=false /p:EnableWindowsTargeting=true /p:EnableFullWpfBuild=true /p:WindowsPackageType=None -v:minimal
```

The default run builds `src/Meridian.Wpf/Meridian.Wpf.csproj`, builds `tests/Meridian.Wpf.Tests/Meridian.Wpf.Tests.csproj`, and runs the focused `DesktopWorkflowScriptTests` slice with `--no-build`. It writes logs plus JSON and Markdown summaries under `artifacts/wpf-validation/dev-loop/<timestamp>/`, uses the existing restored `obj/` graph by default to match the no-restore inner-loop command, and retries once after stopping only stale repo-owned `testhost.exe` processes if a build step fails while one is still running. Use `-Restore` when packages, generated assets, or intermediate output changed; that path restores first and uses isolated output under `artifacts/bin/<wpf-dev-test-*>` and `artifacts/obj/<wpf-dev-test-*>` unless `-NoIsolation` is also supplied.

Common variants:

```bash
# Build only, matching the serialized Release shell command
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/dev/test-wpf-dev.ps1 -BuildOnly

# Restore first when packages or generated assets changed
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/dev/test-wpf-dev.ps1 -Restore

# Run a different focused WPF slice after the build
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/dev/test-wpf-dev.ps1 -Filter "FullyQualifiedName~TradingWorkspaceShellPageTests"

# Run the broader non-integration WPF test set through the same serialized build path
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/dev/test-wpf-dev.ps1 -Filter "Category!=Integration&FullyQualifiedName!~Integration"
```

Use `make desktop-test-dev` for the default wrapper. Keep `desktop-dev.ps1` as the environment/bootstrap check and use `test-wpf-dev.ps1` as the faster inner-loop validation after restore has already succeeded.

### 3. Run Desktop Tests

```bash
# Run all desktop-focused tests (platform-aware)
make desktop-test

# Or run specific test projects:
dotnet test tests/Meridian.Wpf.Tests  # Windows only
dotnet test tests/Meridian.Ui.Tests   # Shared UI services (Windows target; intentionally limited scope)
dotnet test tests/Meridian.Tests      # Cross-platform startup, composition, contracts, and endpoint-shape coverage
```

### Desktop persistence locations

The desktop app is now wired for update-safe persistence:

- Config: `%LocalAppData%\Meridian\appsettings.json`
- Relative data root: `%LocalAppData%\Meridian\data` when `DataRoot` is left at the default `data`
- Desktop catalog and archive-health metadata: `%LocalAppData%\Meridian\_catalog\`
- Activity log and collection session history: under the resolved data root, for example `%LocalAppData%\Meridian\data\_logs\activity_log.json` and `%LocalAppData%\Meridian\data\_sessions\sessions.json`
- Symbol mapping overrides: `DataSources:SymbolMappings:PersistencePath` when configured, otherwise under the resolved data root at `_config\symbol-mappings.json`
- Generated schema dictionary artifacts: `%LocalAppData%\Meridian\_catalog\schemas\`

When testing upgrades or publish/install flows, verify that:

1. `appsettings.json` remains under `%LocalAppData%\Meridian`
2. The setup wizard writes `DataRoot` at the config root level
3. The launched backend process receives the same config path via `--config` and `MDC_CONFIG_PATH`
4. Existing configs that only contain `Storage.BaseDirectory` still load into the same effective data directory
5. Legacy app-folder session, activity-log, symbol-mapping, and schema files are copied forward into the external desktop locations on first upgraded launch

### Launch/session restore seeding

Desktop launch restore now accepts both legacy raw fund profile keys such as `alpha-credit` and operating-context keys such as `Fund:alpha-credit` when reading `workspace-data.json`.

- New workstation session seeds should prefer the operating-context key shape in `lastSelectedFundProfileId` and `sessionsByFundProfileId`
- For backward compatibility, smoke helpers may still write the raw fund profile alias during transition periods
- If you are targeting a fund-scoped workstation page like `Options` or `AddProviderWizard`, make sure the seeded session points at the `data-operations` workspace so launch restore does not legitimately fall back to the fund profile's default governance route

The Robinhood/options smoke harness is now self-contained:

- Run `pwsh -File scripts/dev/robinhood-options-smoke.ps1` from the repo root for the default push-button flow
- The harness seeds from `scripts/dev/fixtures/robinhood-options-smoke.seed.json` and restores the user's `%LocalAppData%\Meridian` files afterward
- Failure runs capture both a screenshot and a UI automation name dump under `artifacts/desktop-workflows/robinhood-options-smoke/`

## Test Projects

### Meridian.Tests (cross-platform backend + host topology)

`tests/Meridian.Tests/` is the default home for repository-wide tests that must stay runnable without Windows desktop support.

**Keep these suites here:**

- startup and host wiring checks
- DI composition / composition-root tests
- provider and endpoint contract tests
- endpoint response-shape and schema snapshot tests
- cross-platform application, domain, infrastructure, and storage logic

**Examples already in this project:**

- `Integration/EndpointTests/*`
- `Application/Composition/*`
- `Infrastructure/Providers/*ContractTests.cs`
- `Integration/EndpointTests/ResponseSchema*Tests.cs`

### Meridian.Ui.Tests (shared desktop service logic under the existing Windows target)

Tests for shared desktop-facing services in `src/Meridian.Ui.Services/`.
Although the project keeps its existing Windows-aware target behavior, its scope should stay focused on platform-compatible shared service logic rather than backend host topology.

**Test Suites:**

1. **Collections Tests** (19 tests)
   - `BoundedObservableCollection` (8 tests) - Capacity-limited observable collection
   - `CircularBuffer` (11 tests) - Circular buffer operations and extension methods

2. **Service Tests** (52 tests)
   - `FormValidationRules` (4 tests) - Input validation rules
   - `ApiClientService` (7 tests) - HTTP client configuration and interaction
   - `BackfillService` (9 tests) - Historical data backfill coordination
   - `WatchlistService` (9 tests) - Symbol watchlist management
   - `SystemHealthService` (10 tests) - System health monitoring and metrics
   - `FixtureDataService` (13 tests) - Mock data generation for offline development

**Running Ui.Tests:**

```bash
# Windows only
dotnet test tests/Meridian.Ui.Tests/Meridian.Ui.Tests.csproj
```

These tests validate shared services used by the WPF desktop application.

**Keep these suites here:**

- shared service abstractions and base classes
- collection helpers and model-shaping helpers
- shared refresh/polling coordinators whose scheduling is abstracted behind an interface
- service logic that is independent of WPF page navigation, binding, or desktop host wiring

### Meridian.Wpf.Tests (Windows only)

Tests for WPF-specific behavior that genuinely depends on WPF types (`System.Windows.Controls.Frame`, bindings, navigation wiring, resource dictionaries, and desktop DI registration).

**Test Suites:**

1. **NavigationServiceTests** (14 tests)
   - Singleton pattern validation
   - Frame initialization
   - Page navigation and registration
   - Navigation history and breadcrumbs
   - Event handling

2. **ConfigServiceTests** (13 tests)
   - Singleton pattern validation
   - Configuration initialization
   - Configuration validation
   - Data source management
   - Symbol management
   - Configuration reload

3. **StatusServiceTests** (13 tests)
   - Singleton pattern validation
   - Status updates and events
   - HTTP client interaction (with mocked unreachable endpoints)
   - Cancellation token support
   - Thread safety

4. **ConnectionServiceTests** (18 tests)
   - Singleton pattern validation
   - Connection state management
   - Auto-reconnect logic
   - Connection monitoring
   - Settings management
   - Event handling
   - HTTP client interaction

5. **MainPageUiWorkflowTests** (3 tests)
   - Command palette filtering and page-open workflow
   - Workspace launch-tile automation and command wiring across the four workstation homes
   - Fixture banner dismissal and ticker-strip toggle behavior
   - Uses an isolated `workspace-data.json` override so mixed-suite shell runs do not inherit persisted workstation state from neighboring tests

6. **Retained shell workstation regressions** (focused slices)
   - `AppServiceRegistrationTests` verifies DI coverage for the shell pages and shell-linked deep pages
   - `WorkspaceShellPageSmokeTests`, `DataOperationsWorkspaceShellSmokeTests`, and `GovernanceWorkspaceShellSmokeTests` verify retained compatibility workspace home pages construct from DI, including the feature-owned Data shell page
   - `WorkstationPageSmokeTests` and `RunMatUiSmokeTests` verify that deep-page navigation now lands inside `WorkspaceDeepPageHostPage` and still exposes the expected hosted inner page
   - `NavigationPageSmokeTests` verifies the dock host wraps WPF `Page` content inside `Frame` containers and can replace shell fallback content on retry
   - `WorkspaceDeepPageChromeTests` verifies the host toggles embedded-shell state on hosted pages and that representative legacy pages, including action-heavy surfaces such as `MessagingHub`, `NotificationCenter`, `SecurityMaster`, `ServiceManager`, and `PositionBlotter`, opt into the shared compact-host styles without losing page-specific command bands
   - The same chrome tests also assert the new workflow-native inspector surfaces for `SecurityMaster`, `ServiceManager`, and `PositionBlotter` through automation IDs such as `SecurityMasterRuntimeInspector`, `ServiceManagerRuntimeInspector`, and `PositionBlotterSelectionInspector`
   - `MainPageSmokeTests`, `MainPageUiWorkflowTests`, `RunMatUiSmokeTests`, `NavigationPageSmokeTests`, `WorkstationPageSmokeTests`, `NavigationServiceTests`, and `FullNavigationSweepTests` share `NavigationServiceSerialCollection` so mixed-suite runs keep singleton navigation state deterministic instead of racing across frame-hosted tests
   - `FullNavigationSweepTests` verifies every registered page tag remains reachable after shell-catalog or route changes

**Running WPF Tests:**

```bash
# Windows only
dotnet test tests/Meridian.Wpf.Tests/Meridian.Wpf.Tests.csproj

# Focused shell-first regression slice
dotnet test tests/Meridian.Wpf.Tests/Meridian.Wpf.Tests.csproj --filter "FullyQualifiedName~AppServiceRegistrationTests|FullyQualifiedName~WorkspaceShellPageSmokeTests|FullyQualifiedName~GovernanceWorkspaceShellSmokeTests|FullyQualifiedName~DataOperationsWorkspaceShellSmokeTests|FullyQualifiedName~MainPageUiWorkflowTests|FullyQualifiedName~RunMatUiSmokeTests|FullyQualifiedName~MainShellViewModelTests|FullyQualifiedName~NavigationPageSmokeTests|FullyQualifiedName~WorkspaceDeepPageChromeTests|FullyQualifiedName~WorkstationPageSmokeTests|FullyQualifiedName~FullNavigationSweepTests|FullyQualifiedName~NavigationServiceTests"

# Broader mixed shell-workflow bundle
dotnet test tests/Meridian.Wpf.Tests/Meridian.Wpf.Tests.csproj --filter "FullyQualifiedName~AppServiceRegistrationTests|FullyQualifiedName~WorkspaceShellPageSmokeTests|FullyQualifiedName~GovernanceWorkspaceShellSmokeTests|FullyQualifiedName~DataOperationsWorkspaceShellSmokeTests|FullyQualifiedName~MainPageSmokeTests|FullyQualifiedName~MainPageUiWorkflowTests|FullyQualifiedName~RunMatUiSmokeTests|FullyQualifiedName~MainShellViewModelTests|FullyQualifiedName~NavigationPageSmokeTests|FullyQualifiedName~WorkspaceDeepPageChromeTests|FullyQualifiedName~WorkstationPageSmokeTests|FullyQualifiedName~FullNavigationSweepTests|FullyQualifiedName~NavigationServiceTests"

# Position blotter route slice
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/dev/validate-position-blotter-route.ps1

# Operator inbox route slice
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/dev/validate-operator-inbox-route.ps1
```

The route validation wrappers exist for the recurring WPF `testhost` lock case around `Meridian.Desktop.dll`.
They use a per-run `MeridianBuildIsolationKey`, build the WPF test project once into an isolated artifact root, and then run the focused route slice with `--no-build` so the validation path is repeatable.

The position blotter route slice covers:

- `PositionBlotterViewModelTests`
- `ShellNavigationCatalogTests`
- `WorkspaceDeepPageChromeTests`
- `TradingWorkspaceShellPageTests`

The operator inbox route slice covers:

- `MainPageUiWorkflowTests`
- `TradingWorkspaceShellPageTests`
- `WorkspaceShellContextStripControlTests`

Validation artifacts land under `artifacts/wpf-validation/<slice-name>/<timestamp>/` with build/test logs plus JSON and Markdown summaries.

If a stale repo-owned `testhost.exe` is still hanging around from an earlier run and the first build fails, the script stops only those repo-scoped processes and retries the build once.

On non-Windows platforms, these tests will be skipped automatically by the Makefile target.

**Keep these suites here:**

- binding-specific behavior
- navigation/page registration behavior
- WPF host wiring and desktop-only service registration
- in-process user workflow smoke tests that exercise real WPF controls and automation IDs

Do **not** move mapping, filtering, or refresh-state logic into this project unless the logic truly requires WPF types. Prefer shared services or plain viewmodel logic with an injected scheduler abstraction.

### Combined Test Coverage Summary

| Project | Tests | Platform | Coverage Areas |
| --- | --- | --- | --- |
| **Meridian.Tests** | Cross-platform | Any OS with .NET 10 | Startup, composition, contracts, endpoint shape, and core/backend logic |
| **Meridian.Ui.Tests** | Varies by slice | Windows target | Shared UI services, collections, form validation, scheduler-backed shared refresh logic |
| **Meridian.Wpf.Tests** | Varies by slice | Windows | WPF-specific binding, navigation, and host wiring |
| **Desktop-specific test projects** | Varies by slice | Windows | Shared desktop services plus WPF-only integration points |

**Coverage breakdown:**

- Navigation: 14 tests (page routing, history, breadcrumbs)
- Configuration: 13 tests (validation, data source management)
- Status Tracking: 13 tests (real-time updates, HTTP interaction)
- Connection Management: 18 tests (state management, auto-reconnect)
- Shell UI Workflows: command palette, workspace switching, fixture banner, ticker strip
- Collections: 19 tests (bounded/circular buffer operations)
- Business Services: 52 tests (validation, health, backfill, fixtures)

## UI Fixture Mode for Offline Development

The UI fixture mode enables desktop developers to work without a running backend service, significantly improving development velocity.

### Using Fixture Mode

**Enable via environment variable:**

```bash
# Windows PowerShell
$env:MDC_FIXTURE_MODE = "1"
dotnet run --project src/Meridian.Wpf

# Windows Command Prompt
set MDC_FIXTURE_MODE=1
dotnet run --project src/Meridian.Wpf
```

**What Fixture Mode Provides:**

- ✅ **Offline development** - No network connectivity required
- ✅ **Deterministic data** - Same mock data every time
- ✅ **Faster iteration** - No backend startup wait time
- ✅ **Demo capabilities** - Show UI features without live data

**Fixture Data Available:**

- Mock status responses (provider health, connection states)
- Sample market data (trades, quotes, order book snapshots)
- Configuration templates
- Historical backfill progress
- Data quality metrics

**Test Coverage for Fixtures:**

The `FixtureDataService` has 13 dedicated tests validating:

- Mock data generation for all major API endpoints
- Consistent data structure matching real API contracts
- Randomized but realistic values (prices, volumes, timestamps)
- Edge cases (empty states, error conditions)

See [UI Fixture Mode Guide](./ui-fixture-mode-guide.md) for complete documentation.

## Building Desktop Applications

### WPF Application (Recommended)

```bash
make desktop-build

# Or directly:
dotnet build src/Meridian.Wpf/Meridian.Wpf.csproj -c Release -r win-x64
```

## Robinhood/Options Smoke Harness

For repeatable desktop smoke coverage of the Robinhood provider wizard, the options page, and the trading position blotter, use:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/dev/robinhood-options-smoke.ps1
```

The harness is intended to be push-button and deterministic:

- It relaunches itself under `pwsh` 7 automatically when needed.
- It defaults to fixture mode so the UI can be exercised without a live backend session.
- It seeds `%LocalAppData%\\Meridian\\workspace-data.json` and `%LocalAppData%\\Meridian\\workstation-operating-context.json` with a workspace/session snapshot for each smoke case.
- It always restores the user's original local files after the run finishes.
- It writes screenshots, seeded session files, per-case post-run workspace snapshots, and `robinhood-options-smoke-results.json` under `output/manual-captures/`.

Older notes may still mention `output/manual-captures/robinhood-options-smoke.ps1`, but the canonical entry point is `scripts/dev/robinhood-options-smoke.ps1`.

## Common Issues and Solutions

### Missing .NET 10 SDK

**Symptom**: Bootstrap script reports .NET SDK not found or wrong version.

**Fix**: Install .NET 10 SDK from https://dotnet.microsoft.com/download/dotnet/9.0

### Missing Visual Studio Build Tools

**Symptom**: XAML compilation fails, build tools not detected.

**Fix**: Install Visual Studio Build Tools with the "Desktop development with C#" workload from https://visualstudio.microsoft.com/downloads/

### XAML Compiler Errors

**Symptom**: WPF build fails with XAML syntax errors.

**Fix**:

1. Check XAML syntax in the Views/ directory
2. Ensure all referenced resources exist
3. See [Desktop App XAML Compiler Errors](https://github.com/rodoHasArrived/Meridian/blob/main/archive/docs/migrations/desktop-app-xaml-compiler-errors.md) for historical diagnostics

### Tests Not Running on Non-Windows

**Expected Behavior**: WPF tests require Windows and will be skipped on Linux/macOS. This is by design.

**What Runs on Non-Windows**:

- Core tests in `Meridian.Tests`
- F# tests in `Meridian.FSharp.Tests`
- Configuration and CLI tests

## Test Coverage

Current test coverage for desktop services:

- **NavigationService**: Page navigation, history tracking, event handling
- **ConfigService**: Configuration validation, data source management
- **StatusService**: Status updates, HTTP interaction, thread safety
- **ConnectionService**: Connection management, auto-reconnect, monitoring

**Areas Not Yet Covered** (future work):

- Integration tests with actual backend service
- Visual regression tests
- Performance tests for singleton access patterns

## Contributing Desktop Tests

When adding new desktop tests:

1. **Follow existing patterns**: Use xUnit, FluentAssertions, Moq/NSubstitute
2. **Test singleton behavior**: Verify instance creation, thread safety
3. **Mock external dependencies**: Use test doubles for HTTP clients, file systems
4. **Test error paths**: Verify exception handling, cancellation support
5. **Keep tests fast**: Avoid actual network calls, use mocked endpoints
6. **Document test purpose**: Clear test names and XML comments
7. **Choose the project by topology**:
   - `Meridian.Tests` for startup/composition/contracts/endpoint shape and any logic that must run cross-platform
   - `Meridian.Ui.Tests` for shared UI-service logic with platform-neutral cores
   - `Meridian.Wpf.Tests` only for WPF-specific binding, navigation, and host wiring
8. **Prefer in-process UI automation for shell workflows**: Add or reuse `AutomationProperties.AutomationId` hooks and exercise the page through `tests/Meridian.Wpf.Tests/Support/*UiAutomationFacade.cs` helpers before reaching for an external UI automation framework

Example test structure:

```csharp
[Fact]
public void ServiceName_Scenario_ExpectedBehavior()
{
    // Arrange
    var service = ServiceName.Instance;
    var input = CreateTestInput();

    // Act
    var result = service.MethodUnderTest(input);

    // Assert
    result.Should().NotBeNull();
    result.SomeProperty.Should().Be(expectedValue);
}
```

## Continuous Integration

Desktop tests run in CI via GitHub Actions:

- **Windows runners**: Run full WPF test suite
- **Linux runners**: Build the WPF project as a CI-compatible stub during solution validation
- **Desktop lane filter**: Use `Category!=Integration&FullyQualifiedName!~Integration`
  so untagged WPF xUnit tests still run while known integration suites stay excluded.
  New integration suites should use both `[Trait("Category", "Integration")]` and an
  `Integration` test class name.

See `.github/workflows/windows-desktop-build.yml` for CI configuration.

## Additional Resources

- [WPF Implementation Notes](./wpf-implementation-notes.md) - WPF architecture and service patterns
- [UI Fixture Mode Guide](./ui-fixture-mode-guide.md) - Complete offline development setup
- [Desktop Support Policy](./policies/desktop-support-policy.md) - Contribution requirements
- [Desktop Architecture](../architecture/desktop-layers.md) - Layer boundaries and design
- [Workstation Delivery Kernel Roadmap](../status/ROADMAP.md#wave-dk-program-focused-migration-wrapper-for-waves-2-4) - Future shell-routing and operator workflow plans
- [GitHub Actions Summary](./github-actions-summary.md) - CI/CD workflows

## Related Documentation

- **Desktop Development:**
  - [Desktop Platform Improvements - Implementation Guide](../evaluations/desktop-platform-improvements-implementation-guide.md) - Canonical roadmap, implementation details, and executive-level summary
  - [Desktop Modernization Summary](../status/EVALUATIONS_AND_AUDITS.md#desktop-platform-improvements-guide) - Consolidated status context across the evaluation set

- **Testing and Quality:**
  - [Test Project README](https://github.com/rodoHasArrived/Meridian/blob/main/tests/Meridian.Ui.Tests/README.md) - Ui.Tests project details

- **Architecture and Policies:**
  - [Repository Organization Guide](./repository-organization-guide.md) - Code structure conventions
  - [Desktop Support Policy](./policies/desktop-support-policy.md) - Required validation checks
