# Desktop UI Services Tests

This test project validates the shared UI services used by the repository's desktop stack.

## Platform Requirements

**Windows Target**: This project keeps its current Windows-aware target behavior so it can build alongside the desktop stack, but its intended scope is **shared UI-service logic**, not backend host topology or WPF-only wiring.

On non-Windows platforms, the project compiles as an empty library.

## Running Tests

### On Windows

```bash
# Run all desktop UI service tests
dotnet test tests/Meridian.Ui.Tests

# Or use Makefile
make test-desktop-services
```

### On Linux/macOS

Tests are automatically skipped (project compiles empty).

## Test Coverage

**Current coverage:** shared collections and service-level logic for the desktop stack.

- **Collections**: 
  - `BoundedObservableCollection` (8 tests)
  - `CircularBuffer` (11 tests)
- **Services**: 
  - `FormValidationRules` (4 tests)
  - `ApiClientService` (7 tests)
  - `BackfillService` (9 tests)
  - `WatchlistService` (9 tests)
  - `SystemHealthService` (10 tests)
  - `FixtureDataService` (13 tests)

More tests to be added...

## Intended Scope

Keep tests here when they validate:

- shared `Meridian.Ui.Services` service/base-class behavior
- platform-compatible collection and helper logic
- refresh/polling coordinators where scheduling is hidden behind an interface
- mapping/filtering/state transitions that do **not** require WPF pages, bindings, or desktop DI wiring

Do **not** put these suites here:

- startup or DI composition-root tests
- endpoint-shape/schema snapshot tests
- provider or HTTP contract tests
- WPF-only binding/navigation/host-wiring tests

Those belong in:

- `tests/Meridian.Tests` for startup, composition, contracts, and endpoint shape
- `tests/Meridian.Wpf.Tests` for WPF-specific binding, navigation, and host wiring

## Adding New Tests

1. Create test file in appropriate subdirectory
2. Follow existing test patterns (Arrange-Act-Assert)
3. Use FluentAssertions for readable assertions
4. Run tests on Windows before submitting PR

## Test Structure

```
Collections/
  BoundedObservableCollectionTests.cs (8 tests)
  CircularBufferTests.cs (11 tests)
Services/
  FormValidationServiceTests.cs (4 tests)
  ApiClientServiceTests.cs (7 tests)
  BackfillServiceTests.cs (9 tests)
  WatchlistServiceTests.cs (9 tests)
  SystemHealthServiceTests.cs (10 tests)
  FixtureDataServiceTests.cs (13 tests)
```
