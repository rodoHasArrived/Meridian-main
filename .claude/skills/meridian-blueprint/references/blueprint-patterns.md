# Meridian Blueprint Patterns Reference

> **Authoritative stats and file paths:** [`../_shared/project-context.md`](../../_shared/project-context.md)

This file provides ready-to-use patterns, naming conventions, and ADR contract reference for
Blueprint Mode. Every pattern here is grounded in the actual Meridian codebase.

---

## Naming Conventions

### Namespaces

| Layer | Namespace Pattern | Example |
|-------|-------------------|---------|
| Domain | `Meridian.Domain.[Area]` | `Meridian.Domain.Collectors` |
| Application | `Meridian.Application.[Area]` | `Meridian.Application.Services` |
| Infrastructure | `Meridian.Infrastructure.Adapters.[Provider]` | `Meridian.Infrastructure.Adapters.Alpaca` |
| Storage | `Meridian.Storage.[Area]` | `Meridian.Storage.Sinks` |
| Contracts | `Meridian.Contracts.[Area]` | `Meridian.Contracts.Domain.Events` |
| WPF | `Meridian.Wpf.[Layer]` | `Meridian.Wpf.ViewModels` |
| ProviderSdk | `Meridian.ProviderSdk` | (flat) |

### Interface Names

- Prefix `I`, PascalCase: `ISymbolWatchlistService`, `IBackfillProvider`, `IStorageSink`
- Service interfaces: `IXxxService`
- Provider interfaces: `IXxxProvider`, `IXxxClient`
- Data interfaces: `IXxxStore`, `IXxxRepository`
- Observer/event interfaces: `IXxxMonitor`, `IXxxObserver`

### Class Names

- Concrete services: `XxxService` (sealed)
- ViewModels: `XxxViewModel : BindableBase` (sealed)
- Views: `XxxPage.xaml` / `XxxView.xaml`
- Options: `XxxOptions` (sealed record or sealed class with `init` properties)
- Commands: `XxxCommand` (for `Channel<T>` discriminated unions)
- Event args: `XxxChangedEventArgs`, `XxxEventArgs`

### Method Names

- Async methods end with `Async`: `GetDataAsync`, `AddSymbolAsync`
- `CancellationToken` parameter named `ct`: `async ValueTask DoWorkAsync(CancellationToken ct = default)`
- Private fields prefixed `_`: `_entries`, `_commandChannel`, `_logger`

---

## ADR Contract Reference

Every blueprint that touches a governed pattern must cite the relevant ADR.

| ADR | Title | Key Contract |
|-----|-------|-------------|
| ADR-001 | Provider Abstraction | `IMarketDataClient`, `IHistoricalDataProvider` — never change without ADR amendment |
| ADR-004 | Async Streaming Patterns | All async methods accept `CancellationToken ct = default`. Prefer `IAsyncEnumerable<T>` for streaming |
| ADR-005 | Attribute-Based Discovery | `[DataSource("name")]`, `[ImplementsAdr("ADR-XXX", "reason")]`, `[StorageSink]` on concrete impls |
| ADR-007 | WAL + Event Pipeline Durability | All storage writes route through `WriteAheadLog` or `AtomicFileWriter`, never `File.WriteAllText` |
| ADR-008 | Multi-Format Composite Storage | `CompositeSink` writes JSONL + Parquet simultaneously; never bypass |
| ADR-009 | F# Type-Safe Domain | F# discriminated unions for domain state; C# interop via generated `.g.cs` |
| ADR-010 | HttpClient Factory | Inject `HttpClient` via `IHttpClientFactory`; never `new HttpClient()` |
| ADR-011 | Centralized Configuration | All config via `IOptionsMonitor<T>` with hot-reload support |
| ADR-013 | Bounded Channel Pipeline Policy | Use `EventPipelinePolicy.Default.CreateChannel<T>()` — never `Channel.CreateUnbounded<T>()` |
| ADR-014 | JSON Source Generators | Use source-generated contexts: `JsonSerializer.Serialize(obj, MyJsonContext.Default.T)` |

---

## DI Registration Patterns

### Singleton service with IHostedService

```csharp
// In IProviderModule.RegisterServices(IServiceCollection services, IConfiguration config)
services.AddSingleton<IXxxService, XxxService>();
services.AddHostedService(sp => (XxxService)sp.GetRequiredService<IXxxService>());
```

### Options registration

```csharp
services.Configure<XxxOptions>(config.GetSection(XxxOptions.SectionName));
```

### HttpClient with resilience

```csharp
services.AddHttpClient<IXxxClient, XxxClient>()
    .AddStandardResilienceHandler();  // via Polly / shared policies
```

---

## Options Class Pattern

```csharp
// Options follow the sealed record pattern for immutability + init-only properties
public sealed class XxxOptions
{
    public const string SectionName = "Xxx";  // matches appsettings.json key

    public List<string> Symbols { get; init; } = [];
    public int MaxItems { get; init; } = 500;
    public TimeSpan ReconnectDelay { get; init; } = TimeSpan.FromSeconds(5);
    public bool EnableFeature { get; init; } = true;
}
```

Use `IOptionsMonitor<XxxOptions>` (not `IOptions<T>`) for runtime/provider settings so they
hot-reload when `appsettings.json` changes. Use `IOptions<T>` only for startup-time config that
never changes after host build.

---

## Channel / Pipeline Pattern

```csharp
// ADR-013: Always use EventPipelinePolicy, never Channel.CreateBounded/Unbounded directly
private readonly Channel<XxxCommand> _commandChannel =
    EventPipelinePolicy.Default.CreateChannel<XxxCommand>();

// Background processing loop (in IHostedService.StartAsync)
private async Task ProcessCommandsAsync(CancellationToken ct)
{
    await foreach (var command in _commandChannel.Reader.ReadAllAsync(ct))
    {
        await HandleCommandAsync(command, ct);
    }
}
```

---

## WPF / MVVM Patterns

### ViewModel skeleton

```csharp
public sealed partial class XxxViewModel : BindableBase, IDisposable
{
    private readonly IXxxService _service;

    public XxxViewModel(IXxxService service)
    {
        _service = service;
        _service.ItemChanged += OnItemChanged;

        AddItemCommand = new AsyncRelayCommand(AddItemAsync, CanAddItem);
        RemoveItemCommand = new RelayCommand<string>(RemoveItem);
    }

    public ObservableCollection<XxxItemViewModel> Items { get; } = [];
    public IAsyncRelayCommand AddItemCommand { get; }
    public IRelayCommand<string> RemoveItemCommand { get; }

    private string _newItemInput = string.Empty;
    public string NewItemInput
    {
        get => _newItemInput;
        set
        {
            if (SetProperty(ref _newItemInput, value.ToUpperInvariant()))
                AddItemCommand.NotifyCanExecuteChanged();
        }
    }

    private void OnItemChanged(object? sender, XxxChangedEventArgs e)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            var item = Items.FirstOrDefault(i => i.Key == e.Key);
            if (item is not null) item.Status = e.NewStatus;
        });
    }

    public void Dispose() => _service.ItemChanged -= OnItemChanged;
}
```

### View code-behind (minimal — logic lives in ViewModel)

```csharp
public partial class XxxPage : Page
{
    public XxxPage(XxxViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
```

### Status DataTrigger pattern (in XAML)

```xml
<Style TargetType="Ellipse" x:Key="StatusDot">
    <Setter Property="Fill" Value="#95A5A6" />  <!-- Default: grey -->
    <Style.Triggers>
        <DataTrigger Binding="{Binding Status}" Value="Active">
            <Setter Property="Fill" Value="#2ECC71" />
        </DataTrigger>
        <DataTrigger Binding="{Binding Status}" Value="Connecting">
            <Setter Property="Fill" Value="#F39C12" />
        </DataTrigger>
        <DataTrigger Binding="{Binding Status}" Value="Faulted">
            <Setter Property="Fill" Value="#E74C3C" />
        </DataTrigger>
    </Style.Triggers>
</Style>
```

---

## F# Domain Type Patterns

```fsharp
// Discriminated union for status (ADR-009)
type ConnectionStatus =
    | Disconnected
    | Connecting
    | Connected
    | Faulted of message: string

// Record type for domain entity
type SymbolEntry =
    { Symbol      : string
      ProviderId  : string
      ConnectedAt : DateTimeOffset
      Status      : ConnectionStatus }

// Module with pure functions (no mutable state)
module SymbolEntryOps =
    let isHealthy entry =
        match entry.Status with
        | Connected -> true
        | _ -> false
```

C# interop is generated into `src/Meridian.FSharp/Generated/Meridian.FSharp.Interop.g.cs`.
New F# types that need C# consumers must be added to `src/Meridian.FSharp/Interop.fs`
and the generator re-run.

---

## Error Handling Pattern

```csharp
// Use the correct exception type from Core/Exceptions/
throw new ConnectionException($"Failed to connect to {_providerName}: {ex.Message}", ex);
throw new RateLimitException($"Rate limit exceeded for {_providerName}", retryAfter: TimeSpan.FromSeconds(60));
throw new ConfigurationException($"Missing required configuration: {nameof(XxxOptions.ApiKey)}");

// CancellationToken swallowing guard — always rethrow OCE
catch (Exception ex) when (ex is not OperationCanceledException)
{
    _logger.LogError(ex, "Failed to {Operation} for {Symbol}", operation, symbol);
    throw new DataProviderException($"Operation failed: {operation}", ex);
}
```

---

## Structured Logging Pattern

```csharp
// ADR requirement: never use string interpolation in log calls
_logger.LogInformation("Subscribed to {Symbol} via {Provider}", symbol, _providerName);
_logger.LogWarning("Reconnecting to {Provider} after {Delay}ms delay", _providerName, delay.TotalMilliseconds);
_logger.LogError(ex, "Failed to process {EventType} for {Symbol}", eventType, symbol);
```

---

## Storage Sink Pattern

```csharp
// ADR-005: attribute decoration on concrete storage sinks
[StorageSink("xxx-sink")]
[ImplementsAdr("ADR-007", "WAL durability")]
[ImplementsAdr("ADR-008", "Multi-format composite storage")]
public sealed class XxxStorageSink : IStorageSink
{
    public async ValueTask WriteAsync(MarketEvent @event, CancellationToken ct = default)
    {
        // Route through AtomicFileWriter (ADR-007) — never File.WriteAllText
        await _atomicWriter.WriteAsync(@event, ct);
    }

    public async ValueTask FlushAsync(CancellationToken ct = default) { ... }
    public async ValueTask DisposeAsync() { ... }
}
```

---

## Historical Provider Pattern

```csharp
// ADR-001, ADR-004, ADR-005 required on all historical providers
[DataSource("xxx-historical")]
[ImplementsAdr("ADR-001", "Historical data provider contract")]
[ImplementsAdr("ADR-004", "CancellationToken on all async methods")]
public sealed class XxxHistoricalDataProvider : BaseHistoricalDataProvider
{
    public override string Name => "xxx";
    public override string DisplayName => "Xxx Data";
    public override string Description => "Historical OHLCV bars from Xxx";
    public override HistoricalDataCapabilities Capabilities => ...;
    public override int Priority => 50;  // Lower = higher priority in fallback chain

    public override async Task<IReadOnlyList<HistoricalBar>> GetDailyBarsAsync(
        string symbol, DateOnly? from, DateOnly? to, CancellationToken ct = default)
    {
        // Rate limiting is handled by BaseHistoricalDataProvider
        ...
    }
}
```

---

## Breaking Change Checklist

When a blueprint requires modifying an existing public interface, always verify:

1. **Consumers in `src/`** — Search for all implementations of the interface
2. **Consumers in `tests/`** — Search for all test doubles (mocks, fakes, stubs)
3. **Consumers in `benchmarks/`** — Check if benchmarks use the interface
4. **Migration strategy** — Default parameter values, optional extension methods, or adapter
   wrappers to preserve backward compatibility if needed
5. **ADR amendment** — If the contract change affects an ADR-governed interface, document the
   decision in the relevant ADR or create a new ADR

---

*Last Updated: 2026-03-17*
