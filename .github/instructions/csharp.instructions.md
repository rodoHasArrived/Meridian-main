---
applyTo: "src/**/*.cs"
---
# C# Source File Instructions

When editing C# source files in this repository:

1. Mark classes `sealed` unless explicitly designed for inheritance.
2. Use `CancellationToken ct` as the last parameter on every async method; never omit it on public API methods.
3. Use structured logging with semantic parameters — never string interpolation: `_logger.LogInformation("Received {Count} bars for {Symbol}", count, symbol)`.
4. Use `IOptionsMonitor<T>` (not `IOptions<T>`) for any setting that may change at runtime; reserve `IOptions<T>` for static config only.
5. All JSON serialization must use ADR-014 source generators: call `JsonSerializer.Serialize(value, MyJsonContext.Default.MyType)` — never the reflection overload.
6. Use `EventPipelinePolicy.Default.CreateChannel<T>()` (or another preset) for producer-consumer queues — this wraps `Channel.CreateBounded` with consistent `BoundedChannelOptions` including `FullMode = BoundedChannelFullMode.DropOldest`. Never create raw unbounded channels.
7. Prefer `Span<T>` and `Memory<T>` for buffer operations in hot paths; avoid LINQ `.ToList()` / `.Select()` allocations per-tick.
8. All domain exceptions must derive from `MeridianException` (in `src/Meridian.Core/Exceptions/`); never throw bare `Exception` or `ApplicationException`.
9. Private fields use the `_` prefix; interfaces use the `I` prefix; async methods end with `Async`.
10. Register all new serializable DTOs in the project's `JsonSerializerContext` partial class.
