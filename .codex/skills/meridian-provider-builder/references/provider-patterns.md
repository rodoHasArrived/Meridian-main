# Meridian Provider Patterns

Use this reference when implementing or extending provider adapters.

## Typical File Set

```text
src/Meridian.Infrastructure/Adapters/{ProviderName}/
  {ProviderName}MarketDataClient.cs
  {ProviderName}HistoricalDataProvider.cs
  {ProviderName}Options.cs
  {ProviderName}Models.cs
  {ProviderName}ProviderModule.cs
```

Not every provider needs every file, but this is the default shape.

## Core Checks

- Start from the `_Template` adapter when one exists.
- Use `IOptionsMonitor<T>`.
- Use repository `HttpClient` or WebSocket infrastructure rather than ad hoc networking.
- Keep `CancellationToken` threaded all the way through.
- Prefer source-generated JSON contexts.
- Add registration changes where the repository expects them.

## Historical Provider Reminders

- Base yourself on the existing historical provider base classes and rate limiting behavior.
- Validate empty responses, HTTP failures, and cancellation.
- Map vendor DTOs into Meridian contracts at the boundary.

## Streaming Provider Reminders

- Handle connect, disconnect, reconnect, and resubscribe.
- Keep receive loops cancellation-aware.
- Publish normalized Meridian events, not vendor-specific message objects.

## Test Expectations

- Happy path
- Error path
- Cancellation path
- Reconnect or retry path where applicable
- Disposal/cleanup path
