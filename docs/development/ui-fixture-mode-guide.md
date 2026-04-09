# UI Fixture Mode for Offline Development

## Overview

The UI Fixture Mode enables desktop developers to work on the WPF application without requiring a running backend service. This significantly improves the development experience by:

- **Enabling offline development** - No need for network connectivity
- **Deterministic testing** - Same data every time for reproducible debugging
- **Faster iteration** - No waiting for backend startup or network requests
- **Demo capabilities** - Show UI features without live data sources
- **Runtime scenario switching** - Cycle through all visual states in a single session (no restart required)

## Architecture

The fixture mode is built around the `FixtureDataService` singleton that provides mock data matching the actual API contracts defined in `Meridian.Contracts.Api`.

```
┌─────────────────────────────────────────────┐
│          Desktop Application                │
│              (WPF)                           │
└─────────────────┬───────────────────────────┘
                  │
                  │ Fixture Mode: ON
                  ├──────────────┐
                  │              │
         ┌────────▼─────┐  ┌────▼────────────────────────┐
         │ Real Service │  │ FixtureDataService          │
         │   (HTTP)     │  │ (In-Memory, scenario-aware) │
         └──────────────┘  └─────────────────────────────┘
```

## Fixture Scenarios

Instead of a single fixed "fixture" state, the service now exposes five named scenarios you can switch between at runtime. This lets you exercise every visual state in a single debugging session.

| Scenario | Description | `IsConnected` | Metrics |
|----------|-------------|---------------|---------|
| `Connected` | Happy-path — providers active, throughput nominal | ✅ | Full |
| `Disconnected` | All providers offline — empty-state/reconnection UI | ❌ | None |
| `Degraded` | Partial connectivity — some providers rate-limited | ✅ | Reduced |
| `Error` | Catastrophic failure — high drop rate, queue overflowing | ❌ | Error-level |
| `Loading` | Initializing — no data available yet | ❌ | None |

### Switching Scenarios at Runtime

**Keyboard shortcut (fastest):**

Press `Ctrl+Shift+N` while the app is running in fixture mode to advance to the next scenario. The fixture banner at the top of the window shows the current scenario name and updates immediately.

**Via the fixture banner:**

Click the **"Next scenario Ctrl+⇧+N"** button in the amber fixture banner at the top of the main window.

**In code:**

```csharp
// Advance to the next scenario
FixtureModeDetector.Instance.CycleScenario();

// Jump directly to a specific scenario
FixtureDataService.Instance.SetScenario(FixtureScenario.Degraded);

// Get the current scenario
var label = FixtureModeDetector.Instance.ScenarioLabel; // e.g. "Degraded (partial)"
```

## Synthetic Provider vs. UI Fixture Mode

The desktop fixture mode is still useful when only the UI needs canned responses. For end-to-end offline development, the repository now also supports a **synthetic provider mode** that runs the actual ingestion/backfill pipeline against deterministic historical/reference data.

Use the synthetic provider when you want:

- realistic historical bars with split and dividend adjustments;
- historical trades, quotes, and auction prints for provider/backtest testing;
- real-time synthetic trade, BBO, and level-2 order book events without live credentials;
- symbol/reference metadata for offline development flows.

Enable it by either:

- setting `"DataSource": "Synthetic"` plus `"Synthetic": { "Enabled": true }` in `appsettings.json`; or
- setting `MDC_SYNTHETIC_MODE=1` to force the synthetic provider on in development environments.

## Using Fixture Mode

### Environment Variable (Recommended)

Set the `MDC_FIXTURE_MODE` environment variable before starting the application:

**Windows (PowerShell):**
```powershell
$env:MDC_FIXTURE_MODE = "1"
dotnet run --project src/Meridian.Wpf
```

**Windows (Command Prompt):**
```cmd
set MDC_FIXTURE_MODE=1
dotnet run --project src/Meridian.Wpf
```

**Linux/macOS:**
```bash
export MDC_FIXTURE_MODE=1
dotnet run --project src/Meridian.Wpf
```

### Command-Line Argument

Pass `--fixture` flag when starting the application:

```bash
dotnet run --project src/Meridian.Wpf -- --fixture
```

### Programmatic (For Testing)

In your test setup or initialization code:

```csharp
// Enable fixture mode
FixtureModeDetector.Instance.SetFixtureMode(true);

// Switch to a specific scenario
FixtureDataService.Instance.SetScenario(FixtureScenario.Degraded);

// Get status for the current scenario
var status = FixtureDataService.Instance.GetStatusForActiveScenario();

// Cycle to the next scenario
FixtureModeDetector.Instance.CycleScenario();
```

## Available Mock Data

### Status Response (scenario-aware)

```csharp
// Returns data for the currently active scenario
var status = FixtureDataService.Instance.GetStatusForActiveScenario();

// Or call individual methods directly:
var connected    = FixtureDataService.Instance.GetMockStatusResponse();       // Connected
var disconnected = FixtureDataService.Instance.GetMockDisconnectedStatus();   // Disconnected
var degraded     = FixtureDataService.Instance.GetMockDegradedStatus();       // Degraded
var error        = FixtureDataService.Instance.GetMockErrorStatus();          // Error state
var loading      = FixtureDataService.Instance.GetMockLoadingStatus();        // Loading/init
```

### Trade Data

```csharp
var trade = FixtureDataService.Instance.GetMockTradeData("SPY");
// Returns: Single trade for symbol
// - Realistic price based on symbol hash
// - Current timestamp
// - Venue: NASDAQ
```

### Quote Data

```csharp
var quote = FixtureDataService.Instance.GetMockQuoteData("AAPL");
// Returns: BBO quote with spread
// - Bid/Ask prices
// - Bid/Ask sizes
// - Calculated mid-price and spread
```

### Trade History

```csharp
var trades = FixtureDataService.Instance.GetMockTradesResponse("MSFT", 20);
// Returns: Collection of sequential trades
// - Chronological timestamps
// - Ascending prices
// - Alternating buy/sell aggressor
```

### Backfill Health

```csharp
var health = FixtureDataService.Instance.GetMockBackfillHealth();
// Returns: Provider health status
// - Alpaca: Available (45ms latency)
// - Polygon: Available (68ms latency)
// - Tiingo: Unavailable (rate limited)
```

### Symbol List

```csharp
var symbols = FixtureDataService.Instance.GetMockSymbols();
// Returns: ["SPY", "AAPL", "MSFT", "TSLA", "GOOGL", ...]
```

## Integration Patterns

### Service Integration

Services should check for fixture mode and return mock data appropriate to the active scenario:

```csharp
public sealed class StatusService
{
    private static readonly Lazy<StatusService> _instance = new(() => new());
    public static StatusService Instance => _instance.Value;

    public bool UseFixtureMode { get; set; }

    public async Task<StatusResponse> GetStatusAsync()
    {
        if (UseFixtureMode)
        {
            // Simulate network delay for realism
            await FixtureDataService.Instance.SimulateNetworkDelayAsync();
            // Return data for the currently active scenario
            return FixtureDataService.Instance.GetStatusForActiveScenario();
        }

        // Real API call
        return await ApiClientService.Instance.GetAsync<StatusResponse>("/api/status");
    }
}
```

## Testing with Fixture Data

### Unit Tests

```csharp
[Fact]
public void GetMockDegradedStatus_IsConnectedWithHighDropRate()
{
    var status = FixtureDataService.Instance.GetMockDegradedStatus();
    status.IsConnected.Should().BeTrue();
    status.Metrics!.DropRate.Should().BeGreaterThan(0.02f);
}

[Theory]
[InlineData(FixtureScenario.Connected, FixtureScenario.Disconnected)]
[InlineData(FixtureScenario.Loading, FixtureScenario.Connected)]
public void CycleToNextScenario_AdvancesToExpectedNextScenario(
    FixtureScenario start, FixtureScenario expectedNext)
{
    FixtureDataService.Instance.SetScenario(start);
    var next = FixtureDataService.Instance.CycleToNextScenario();
    next.Should().Be(expectedNext);
    FixtureDataService.Instance.SetScenario(FixtureScenario.Connected); // cleanup
}
```

## Keyboard Shortcut Reference

| Shortcut | Action |
|----------|--------|
| `Ctrl+Shift+N` | Cycle to next fixture scenario (only active when fixture mode is on) |

The scenario cycles in this order:  
**Connected → Disconnected → Degraded → Error → Loading → Connected → …**

## Realistic Network Simulation

The `SimulateNetworkDelayAsync()` method adds random delays (50-150ms) to make fixture data feel more realistic:

```csharp
public async Task<StatusResponse> GetStatusAsync()
{
    if (UseFixtureMode)
    {
        await FixtureDataService.Instance.SimulateNetworkDelayAsync();
        return FixtureDataService.Instance.GetStatusForActiveScenario();
    }

    return await RealApiCall();
}
```

## Visual Indicator

When fixture mode is active, the main window shows an amber banner at the top. The banner displays:

1. **Mode text** – "FIXTURE MODE — Showing sample data, not connected to live backend"
2. **Scenario label** – A pill badge showing the current scenario (e.g. `Connected (healthy)`)
3. **Next scenario button** – Click to advance to the next scenario; also shows the keyboard shortcut

## Advantages

### For Developers

✅ **Work offline** - No internet required  
✅ **Faster startup** - No backend initialization wait  
✅ **Deterministic** - Same data every time  
✅ **No rate limits** - Mock data has no API quotas  
✅ **Runtime scenario switching** - No restart needed to test different states  
✅ **Keyboard shortcut** - Cycle scenarios without touching the mouse  

### For Demos

✅ **Reliable** - No network issues during presentations  
✅ **Controllable** - Show exact scenarios you want  
✅ **Portable** - Works anywhere, anytime  

### For Testing

✅ **Reproducible** - Tests get consistent data  
✅ **Fast** - No network latency  
✅ **Isolated** - No backend dependencies  

## Limitations

⚠️ **Not a replacement for integration tests** - Real backend testing still needed  
⚠️ **Mock data may differ** - Contracts can change; keep fixtures updated  

## Extending Fixture Data

To add new mock data:

1. **Add method to FixtureDataService:**

```csharp
public MyNewDataType GetMockMyNewData() => new()
{
    // Mock data properties
};
```

2. **Add tests:**

```csharp
[Fact]
public void GetMockMyNewData_ReturnsValidData()
{
    var data = FixtureDataService.Instance.GetMockMyNewData();
    data.Should().NotBeNull();
}
```

3. **Integrate with services using `GetStatusForActiveScenario()` pattern.**

## Best Practices

1. **Keep fixtures updated** - When API contracts change, update mock data
2. **Test both modes** - Verify application works with fixtures AND real data
3. **Use realistic values** - Mock data should resemble production
4. **Simulate delays** - Use `SimulateNetworkDelayAsync()` for realism
5. **Show indicator** - Always indicate when fixture mode is active
6. **Use `SetScenario()` in tests** - Explicitly set and reset scenario state

## Troubleshooting

### Fixture mode not working

Check:
- ✓ Environment variable is set correctly (`MDC_FIXTURE_MODE=1`)
- ✓ Service checks `FixtureModeDetector.Instance.IsFixtureMode` before calling fixture data
- ✓ App startup calls `FixtureModeDetector.Instance.SetFixtureMode(true)` if using programmatic enable

### Scenario button doesn't do anything

- The "Next scenario" button and `Ctrl+Shift+N` shortcut are **disabled when fixture mode is off**.
- Make sure the app was launched with `--fixture` or `MDC_FIXTURE_MODE=1`.

### Data looks wrong

Verify:
- ✓ `FixtureDataService` returns expected types
- ✓ Mock data matches current API contracts
- ✓ Your service calls `GetStatusForActiveScenario()` (not a hardcoded method)
- ✓ Timestamps are reasonable

## Related Files

- **Scenarios enum**: `src/Meridian.Ui.Services/Services/FixtureScenario.cs`
- **Service**: `src/Meridian.Ui.Services/Services/FixtureDataService.cs`
- **Mode detector**: `src/Meridian.Ui.Services/Services/FixtureModeDetector.cs`
- **ViewModel**: `src/Meridian.Wpf/ViewModels/MainWindowViewModel.cs`
- **Tests**: `tests/Meridian.Ui.Tests/Services/FixtureDataServiceTests.cs`
- **Tests**: `tests/Meridian.Ui.Tests/Services/FixtureModeDetectorTests.cs`
- **Contracts**: `src/Meridian.Contracts/Api/`

---

**Status**: ✅ Implemented  
**Version**: 1.1  
**Last Updated**: 2026-04-03  

---

## Related Documentation

- **Desktop Development:**
  - [Desktop Testing Guide](./desktop-testing-guide.md) - Complete testing procedures and fixture usage
  - [WPF Implementation Notes](./wpf-implementation-notes.md) - WPF architecture and patterns
  - [Desktop Platform Improvements](../evaluations/desktop-platform-improvements-implementation-guide.md) - Improvement roadmap

- **Testing and Quality:**
  - [Test Project README](https://github.com/rodoHasArrived/Meridian/blob/main/tests/Meridian.Ui.Tests/README.md) - Test coverage details
  - [Desktop Support Policy](./policies/desktop-support-policy.md) - Required validation checks

- **Architecture:**
  - [Desktop Architecture Layers](../architecture/desktop-layers.md) - Layer boundaries and dependencies
  - [Repository Organization Guide](./repository-organization-guide.md) - Code structure conventions


## Overview

The UI Fixture Mode enables desktop developers to work on the WPF application without requiring a running backend service. This significantly improves the development experience by:

- **Enabling offline development** - No need for network connectivity
- **Deterministic testing** - Same data every time for reproducible debugging
- **Faster iteration** - No waiting for backend startup or network requests
- **Demo capabilities** - Show UI features without live data sources

## Architecture

The fixture mode is built around the `FixtureDataService` singleton that provides mock data matching the actual API contracts defined in `Meridian.Contracts.Api`.

```
┌─────────────────────────────────────────────┐
│          Desktop Application                │
│              (WPF)                           │
└─────────────────┬───────────────────────────┘
                  │
                  │ Fixture Mode: ON
                  ├──────────────┐
                  │              │
         ┌────────▼─────┐  ┌────▼────────────┐
         │ Real Service │  │ FixtureService  │
         │   (HTTP)     │  │   (In-Memory)   │
         └──────────────┘  └─────────────────┘
```

## Synthetic Provider vs. UI Fixture Mode

The desktop fixture mode is still useful when only the UI needs canned responses. For end-to-end offline development, the repository now also supports a **synthetic provider mode** that runs the actual ingestion/backfill pipeline against deterministic historical/reference data.

Use the synthetic provider when you want:

- realistic historical bars with split and dividend adjustments;
- historical trades, quotes, and auction prints for provider/backtest testing;
- real-time synthetic trade, BBO, and level-2 order book events without live credentials;
- symbol/reference metadata for offline development flows.

Enable it by either:

- setting `"DataSource": "Synthetic"` plus `"Synthetic": { "Enabled": true }` in `appsettings.json`; or
- setting `MDC_SYNTHETIC_MODE=1` to force the synthetic provider on in development environments.

## Using Fixture Mode

### Environment Variable (Recommended)

Set the `MDC_FIXTURE_MODE` environment variable before starting the application:

**Windows (PowerShell):**
```powershell
$env:MDC_FIXTURE_MODE = "1"
dotnet run --project src/Meridian.Wpf
```

**Windows (Command Prompt):**
```cmd
set MDC_FIXTURE_MODE=1
dotnet run --project src/Meridian.Wpf
```

**Linux/macOS:**
```bash
export MDC_FIXTURE_MODE=1
dotnet run --project src/Meridian.Wpf
```

### Command-Line Argument

Pass `--fixture` flag when starting the application:

```bash
dotnet run --project src/Meridian.Wpf -- --fixture
```

### Programmatic (For Testing)

In your test setup or initialization code:

```csharp
// Enable fixture mode
FixtureModeManager.Instance.IsEnabled = true;

// Use fixture data
var status = FixtureDataService.Instance.GetMockStatusResponse();
```

## Available Mock Data

### Status Response

```csharp
var status = FixtureDataService.Instance.GetMockStatusResponse();
// Returns: Connected system with realistic metrics
// - Uptime: ~2 hours
// - Events processed: ~45,000
// - Pipeline status: Active with queue data
```

### Disconnected Status

```csharp
var status = FixtureDataService.Instance.GetMockDisconnectedStatus();
// Returns: Disconnected system state
// - IsConnected: false
// - Metrics: null
// - Pipeline: null
```

### Trade Data

```csharp
var trade = FixtureDataService.Instance.GetMockTradeData("SPY");
// Returns: Single trade for symbol
// - Realistic price based on symbol hash
// - Current timestamp
// - Venue: NASDAQ
```

### Quote Data

```csharp
var quote = FixtureDataService.Instance.GetMockQuoteData("AAPL");
// Returns: BBO quote with spread
// - Bid/Ask prices
// - Bid/Ask sizes
// - Calculated mid-price and spread
```

### Trade History

```csharp
var trades = FixtureDataService.Instance.GetMockTradesResponse("MSFT", 20);
// Returns: Collection of sequential trades
// - Chronological timestamps
// - Ascending prices
// - Alternating buy/sell aggressor
```

### Backfill Health

```csharp
var health = FixtureDataService.Instance.GetMockBackfillHealth();
// Returns: Provider health status
// - Alpaca: Available (45ms latency)
// - Polygon: Available (68ms latency)
// - Tiingo: Unavailable (rate limited)
```

### Symbol List

```csharp
var symbols = FixtureDataService.Instance.GetMockSymbols();
// Returns: ["SPY", "AAPL", "MSFT", "TSLA", "GOOGL", ...]
```

## Integration Patterns

### Service Integration

Services should check for fixture mode and return mock data:

```csharp
public sealed class StatusService
{
    private static readonly Lazy<StatusService> _instance = new(() => new());
    public static StatusService Instance => _instance.Value;

    public bool UseFixtureMode { get; set; }

    public async Task<StatusResponse> GetStatusAsync()
    {
        if (UseFixtureMode)
        {
            // Simulate network delay for realism
            await FixtureDataService.Instance.SimulateNetworkDelayAsync();
            return FixtureDataService.Instance.GetMockStatusResponse();
        }

        // Real API call
        return await ApiClientService.Instance.GetAsync<StatusResponse>("/api/status");
    }
}
```

### App Startup Configuration

**WPF (App.xaml.cs):**

```csharp
protected override void OnStartup(StartupEventArgs e)
{
    base.OnStartup(e);

    // Check for fixture mode
    var useFixture = e.Args.Contains("--fixture") ||
                     Environment.GetEnvironmentVariable("MDC_FIXTURE_MODE") == "1";

    if (useFixture)
    {
        EnableFixtureMode();
    }

    var mainWindow = new MainWindow();
    mainWindow.Show();
}

private void EnableFixtureMode()
{
    // Enable fixture mode for all services
    StatusService.Instance.UseFixtureMode = true;
    BackfillService.Instance.UseFixtureMode = true;
    LiveDataService.Instance.UseFixtureMode = true;

    // Show notification
    NotificationService.Instance.ShowInfo(
        "Fixture Mode",
        "Running with mock data (offline mode)"
    );
}
```

## Testing with Fixture Data

### Unit Tests

```csharp
[Fact]
public async Task Dashboard_WithFixtureData_DisplaysStatus()
{
    // Arrange
    StatusService.Instance.UseFixtureMode = true;
    var viewModel = new DashboardViewModel();

    // Act
    await viewModel.LoadStatusAsync();

    // Assert
    viewModel.Status.Should().NotBeNull();
    viewModel.Status.IsConnected.Should().BeTrue();
    viewModel.EventsPerSecond.Should().BeGreaterThan(0);
}
```

### Integration Tests

```csharp
[Fact]
public async Task LiveDataViewer_WithFixtureMode_ShowsTrades()
{
    // Arrange
    LiveDataService.Instance.UseFixtureMode = true;

    // Act
    var trades = await LiveDataService.Instance.GetRecentTradesAsync("SPY");

    // Assert
    trades.Should().NotBeEmpty();
    trades.First().Symbol.Should().Be("SPY");
}
```

## Realistic Network Simulation

The `SimulateNetworkDelayAsync()` method adds random delays (50-150ms) to make fixture data feel more realistic:

```csharp
public async Task<StatusResponse> GetStatusAsync()
{
    if (UseFixtureMode)
    {
        await FixtureDataService.Instance.SimulateNetworkDelayAsync();
        return FixtureDataService.Instance.GetMockStatusResponse();
    }

    return await RealApiCall();
}
```

## Visual Indicator

It's recommended to show a visual indicator when fixture mode is active:

**Status Bar:**
```xml
<StatusBar>
    <StatusBarItem x:Name="FixtureModeIndicator"
                   Visibility="{Binding IsFixtureMode, Converter={StaticResource BoolToVisibility}}">
        <TextBlock Text="⚠ FIXTURE MODE - Offline Data"
                   Foreground="Orange"
                   FontWeight="Bold"/>
    </StatusBarItem>
</StatusBar>
```

**Notification:**
```csharp
if (UseFixtureMode)
{
    NotificationService.Instance.ShowWarning(
        "Fixture Mode Active",
        "Application is using mock data for offline development"
    );
}
```

## Advantages

### For Developers

✅ **Work offline** - No internet required
✅ **Faster startup** - No backend initialization wait
✅ **Deterministic** - Same data every time
✅ **No rate limits** - Mock data has no API quotas
✅ **Test edge cases** - Easy to simulate errors/disconnections

### For Demos

✅ **Reliable** - No network issues during presentations
✅ **Controllable** - Show exact scenarios you want
✅ **Portable** - Works anywhere, anytime

### For Testing

✅ **Reproducible** - Tests get consistent data
✅ **Fast** - No network latency
✅ **Isolated** - No backend dependencies

## Limitations

⚠️ **Not a replacement for integration tests** - Real backend testing still needed
⚠️ **Mock data may differ** - Contracts can change; keep fixtures updated
⚠️ **Limited scenarios** - Only common cases are mocked

## Extending Fixture Data

To add new mock data:

1. **Add method to FixtureDataService:**

```csharp
public MyNewDataType GetMockMyNewData() => new()
{
    // Mock data properties
};
```

2. **Add tests:**

```csharp
[Fact]
public void GetMockMyNewData_ReturnsValidData()
{
    var data = FixtureDataService.Instance.GetMockMyNewData();
    data.Should().NotBeNull();
}
```

3. **Integrate with services:**

```csharp
if (UseFixtureMode)
{
    return FixtureDataService.Instance.GetMockMyNewData();
}
```

## Best Practices

1. **Keep fixtures updated** - When API contracts change, update mock data
2. **Test both modes** - Verify application works with fixtures AND real data
3. **Use realistic values** - Mock data should resemble production
4. **Simulate delays** - Use `SimulateNetworkDelayAsync()` for realism
5. **Show indicator** - Always indicate when fixture mode is active
6. **Document edge cases** - Add methods for error scenarios

## Troubleshooting

### Fixture mode not working

Check:
- ✓ Environment variable is set correctly
- ✓ Service has `UseFixtureMode` property
- ✓ Service checks fixture mode before API calls
- ✓ App startup enables fixture mode

### Data looks wrong

Verify:
- ✓ FixtureDataService returns expected types
- ✓ Mock data matches current API contracts
- ✓ JSON property names match API
- ✓ Timestamps are reasonable

### Tests fail in fixture mode

Ensure:
- ✓ Test setup enables fixture mode
- ✓ Services support fixture mode flag
- ✓ Mock data meets test expectations
- ✓ No accidental real API calls

## Related Files

- **Service**: `src/Meridian.Ui.Services/Services/FixtureDataService.cs`
- **Tests**: `tests/Meridian.Ui.Tests/Services/FixtureDataServiceTests.cs`
- **Contracts**: `src/Meridian.Contracts/Api/`
- **Implementation Guide**: `docs/evaluations/desktop-platform-improvements-implementation-guide.md`

## Next Steps

After implementing basic fixture mode:

1. **Week 4**: Add architecture diagram
2. **Months 2-3**: Service consolidation to reduce duplication
3. **Future**: Fixture mode for more complex scenarios (streaming, backfill progress)

---

**Status**: ✅ Implemented
**Version**: 1.0
**Last Updated**: 2026-02-13

---

## Related Documentation

- **Desktop Development:**
  - [Desktop Testing Guide](./desktop-testing-guide.md) - Complete testing procedures and fixture usage
  - [WPF Implementation Notes](./wpf-implementation-notes.md) - WPF architecture and patterns
  - [Desktop Platform Improvements](../evaluations/desktop-platform-improvements-implementation-guide.md) - Improvement roadmap

- **Testing and Quality:**
  - [Test Project README](https://github.com/rodoHasArrived/Meridian/blob/main/tests/Meridian.Ui.Tests/README.md) - Test coverage details
  - [Desktop Support Policy](./policies/desktop-support-policy.md) - Required validation checks

- **Architecture:**
  - [Desktop Architecture Layers](../architecture/desktop-layers.md) - Layer boundaries and dependencies
  - [Repository Organization Guide](./repository-organization-guide.md) - Code structure conventions
