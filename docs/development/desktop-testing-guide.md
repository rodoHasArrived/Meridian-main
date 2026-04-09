# Desktop Development Testing Guide

This guide helps contributors set up and test the WPF desktop application for Meridian.

## Quick Commands Reference

```bash
# Environment validation
make desktop-dev-bootstrap

# Build desktop application
make build-wpf                    # Build WPF desktop app

# Run tests
make test-desktop-services        # Run all desktop-focused tests
dotnet test tests/Meridian.Wpf.Tests        # WPF service tests (Windows only)
dotnet test tests/Meridian.Ui.Tests         # Shared UI service tests (Windows only)
```

## Quick Start

### 1. Validate Your Development Environment

Run the desktop development bootstrap script to validate your environment:

```bash
make desktop-dev-bootstrap
```

Or directly with PowerShell:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/dev/desktop-dev.ps1
```

This script validates:
- ✅ .NET 9 SDK installation
- ✅ Windows SDK presence (Windows only)
- ✅ Visual Studio Build Tools
- ✅ XAML tooling support
- ✅ Desktop project restore and smoke build

**Actionable Fix Messages**: The script provides specific instructions for any missing components.

### 2. Run Desktop Tests

```bash
# Run all desktop-focused tests (platform-aware)
make test-desktop-services

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
   - Workspace tile navigation across workstation home surfaces
   - Fixture banner dismissal and ticker-strip toggle behavior

**Running WPF Tests:**

```bash
# Windows only
dotnet test tests/Meridian.Wpf.Tests/Meridian.Wpf.Tests.csproj
```

On non-Windows platforms, these tests will be skipped automatically by the Makefile target.

**Keep these suites here:**

- binding-specific behavior
- navigation/page registration behavior
- WPF host wiring and desktop-only service registration
- in-process user workflow smoke tests that exercise real WPF controls and automation IDs

Do **not** move mapping, filtering, or refresh-state logic into this project unless the logic truly requires WPF types. Prefer shared services or plain viewmodel logic with an injected scheduler abstraction.

### Combined Test Coverage Summary

| Project | Tests | Platform | Coverage Areas |
|---------|-------|----------|----------------|
| **Meridian.Tests** | Cross-platform | Any OS with .NET 9 | Startup, composition, contracts, endpoint shape, and core/backend logic |
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
make build-wpf

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

For compatibility with older notes and artifacts, `output/manual-captures/robinhood-options-smoke.ps1` is a thin wrapper that forwards to the canonical script in `scripts/dev/`.

## Common Issues and Solutions

### Missing .NET 9 SDK

**Symptom**: Bootstrap script reports .NET SDK not found or wrong version.

**Fix**: Install .NET 9 SDK from https://dotnet.microsoft.com/download/dotnet/9.0

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
- **Linux/macOS runners**: Skip WPF tests, run integration tests

See `.github/workflows/desktop-builds.yml` for CI configuration.

## Additional Resources

- [WPF Implementation Notes](./wpf-implementation-notes.md) - WPF architecture and service patterns
- [UI Fixture Mode Guide](./ui-fixture-mode-guide.md) - Complete offline development setup
- [Desktop Support Policy](./policies/desktop-support-policy.md) - Contribution requirements
- [Desktop Architecture](../architecture/desktop-layers.md) - Layer boundaries and design
- [Desktop Improvements Roadmap](../status/ROADMAP.md#desktop-improvements) - Future plans
- [GitHub Actions Summary](./github-actions-summary.md) - CI/CD workflows

## Related Documentation

- **Desktop Development:**
  - [Desktop Platform Improvements - Implementation Guide](../evaluations/desktop-platform-improvements-implementation-guide.md) - Complete improvement roadmap
  - [Desktop Improvements - Executive Summary](../evaluations/desktop-improvements-executive-summary.md) - Impact analysis, priorities, and quick reference

- **Testing and Quality:**
  - [Test Project README](https://github.com/rodoHasArrived/Meridian/blob/main/tests/Meridian.Ui.Tests/README.md) - Ui.Tests project details

- **Architecture and Policies:**
  - [Repository Organization Guide](./repository-organization-guide.md) - Code structure conventions
  - [Desktop Support Policy](./policies/desktop-support-policy.md) - Required validation checks
