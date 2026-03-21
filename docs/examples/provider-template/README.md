# Provider Implementation Template

This directory contains skeleton files for implementing a new market data provider.
Copy the relevant files to a new directory under `Adapters/{ProviderName}/` and replace
every `Template` / `TEMPLATE` placeholder with your provider's actual name and values.

## Files

| File | Purpose |
|------|---------|
| `TemplateConstants.cs` | Provider-specific constants (endpoints, rate limits, message types) |
| `TemplateConfig.cs` | Configuration records (backfill config + streaming options) |
| `TemplateMarketDataClient.cs` | Real-time streaming client (`IMarketDataClient`) |
| `TemplateHistoricalDataProvider.cs` | Historical data backfill provider (`BaseHistoricalDataProvider`) |
| `TemplateSymbolSearchProvider.cs` | Symbol search/lookup provider (`BaseSymbolSearchProvider`) |
| `TemplateFactory.cs` | Factory helpers and optional DI module (`IProviderModule`) |

Implement only the file(s) relevant to your provider — not every provider needs all six.

## Quick-start steps

1. **Create your provider directory**
   ```
   src/Meridian.Infrastructure/Adapters/{YourProvider}/
   ```

2. **Copy and rename the template files you need**
   ```
   cp docs/examples/provider-template/TemplateConstants.cs              src/Meridian.Infrastructure/Adapters/{YourProvider}/{YourProvider}Constants.cs
   cp docs/examples/provider-template/TemplateConfig.cs                 src/Meridian.Infrastructure/Adapters/{YourProvider}/{YourProvider}Config.cs
   cp docs/examples/provider-template/TemplateMarketDataClient.cs       src/Meridian.Infrastructure/Adapters/{YourProvider}/{YourProvider}MarketDataClient.cs
   cp docs/examples/provider-template/TemplateHistoricalDataProvider.cs src/Meridian.Infrastructure/Adapters/{YourProvider}/{YourProvider}HistoricalDataProvider.cs
   cp docs/examples/provider-template/TemplateSymbolSearchProvider.cs   src/Meridian.Infrastructure/Adapters/{YourProvider}/{YourProvider}SymbolSearchProvider.cs
   cp docs/examples/provider-template/TemplateFactory.cs                src/Meridian.Infrastructure/Adapters/{YourProvider}/{YourProvider}Factory.cs
   ```

3. **Replace all `Template` placeholders** with your provider name (case-sensitive).

4. **Fill in the `TODO` comments** in each file — they mark every section that needs
   provider-specific implementation.

5. **Integrate the configuration** — follow the instructions at the top of `TemplateConfig.cs`:
   - Add the backfill config record to `BackfillConfig.cs` (in `src/Meridian.Core/Config/`).
   - Add the streaming options record to a new `{YourProvider}Options.cs` file there (if streaming).
   - Reference the new config properties from `AppConfig.cs` (streaming) and
     `BackfillProvidersConfig` (backfill).

6. **Register the provider** — follow the integration checklist at the top of `TemplateFactory.cs`:
   - **Backfill / search**: add the Create* methods to `ProviderFactory` and call them
     from `CreateBackfillProviders()` / `CreateSymbolSearchProviders()`.
   - **Streaming**: register the factory lambda in `ServiceCompositionRoot.RegisterStreamingFactories()`.

7. **Write tests** — place them in `tests/Meridian.Tests/Infrastructure/Providers/`.
   Use `MarketDataClientContractTests<T>` as the base class for streaming-client tests.

## Architecture rules

- `*Constants.cs` — keep all types `internal`; no public API surface.
- `*Config.cs` — records live in `Meridian.Application.Config` after integration;
  the template uses the `Adapters.Template` namespace as a staging area only.
- `*MarketDataClient.cs` — implement `IMarketDataClient`; prefer `WebSocketProviderBase`
  for WebSocket-based providers.
- `*HistoricalDataProvider.cs` — extend `BaseHistoricalDataProvider`; use
  `WaitForRateLimitSlotAsync(ct)` (built into the base class) for rate limiting.
- `*SymbolSearchProvider.cs` — extend `BaseSymbolSearchProvider`.
- `*Factory.cs` — static creation helpers and optional `IProviderModule` for DI registration.
- Apply `[DataSource]` and `[ImplementsAdr]` attributes as shown in the templates.
- Use structured logging: `_log.LogInformation("Fetched {Count} bars for {Symbol}", n, sym)`.
- Every async method must accept and forward `CancellationToken ct`.

## Reference providers

Consult these production implementations for additional context:

| Type | Example |
|------|---------|
| Streaming (WebSocket) | `Adapters/Alpaca/AlpacaMarketDataClient.cs` |
| Streaming (base class) | `Adapters/Core/WebSocketProviderBase.cs` |
| Historical backfill | `Adapters/Finnhub/FinnhubHistoricalDataProvider.cs` |
| Symbol search | `Adapters/Alpaca/AlpacaSymbolSearchProviderRefactored.cs` |
| Constants pattern | `Adapters/InteractiveBrokers/IBApiLimits.cs` |
| Config / options pattern | `src/Meridian.Core/Config/BackfillConfig.cs` |
| Factory / DI wiring | `Adapters/Core/ProviderFactory.cs` |

See also `docs/development/provider-implementation.md` for the full implementation guide.
