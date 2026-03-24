# UI Services Tests

This test project validates the shared UI services library (`Meridian.Ui.Services`).

## Running Tests

```bash
dotnet test tests/Meridian.Ui.Tests
```

## Test Coverage

**Current coverage:** shared collections and service-level logic.

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
- mapping/filtering/state transitions that do **not** require UI pages, bindings, or DI wiring

Do **not** put these suites here:

- startup or DI composition-root tests
- endpoint-shape/schema snapshot tests
- provider or HTTP contract tests

Those belong in:

- `tests/Meridian.Tests` for startup, composition, contracts, and endpoint shape

## Adding New Tests

1. Create test file in appropriate subdirectory
2. Follow existing test patterns (Arrange-Act-Assert)
3. Use FluentAssertions for readable assertions

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
