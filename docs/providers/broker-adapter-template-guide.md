# Broker Adapter Template Guide

Use `src/Meridian.Infrastructure/Adapters/Templates/BrokerAdapterTemplate.cs` as the provider-integration scaffold for new brokerage adapters.

## What to implement

1. **Auth/session service** (`TemplateBrokerSessionService`)
   - Implement broker login, session renewal, and token invalidation.
2. **HTTP wrapper** (`TemplateBrokerHttpClient`)
   - Add broker-specific endpoints, retry/rate-limit policy, and authentication headers.
3. **Optional streaming client** (`ITemplateBrokerStreamingClient`)
   - Replace `TemplateBrokerNoopStreamingClient` for WebSocket/SSE feeds.
4. **Canonical mapper layer** (`TemplateBrokerMapper`)
   - Map broker payloads to `OrderRequest` / `ExecutionReport` without exposing broker enums.
5. **Health/degradation reporter** (`TemplateBrokerHealthReporter`)
   - Translate broker health telemetry into `BrokerHealthStatus`.

## Extension points

- **Order placement:** `TemplateBrokerOrderExtensions.PlaceOrderAsync`
- **Order status polling:** `TemplateBrokerOrderExtensions.PollOrderStatusAsync`
- **Execution reconciliation:** `TemplateBrokerExecutionExtensions.ReconcileExecutionsAsync`
- **Market data ingestion:** `TemplateBrokerMarketDataExtensions.IngestMarketDataAsync`

## Boundary rule (important)

- Keep broker-specific request/response DTOs and enums inside the adapter boundary (`TemplateBroker*` types).
- Do **not** leak provider enums or raw payload types into shared contracts (`Meridian.Execution.Sdk`, shared DTOs, workstation API contracts).
- Shared surfaces should only consume canonical Meridian models (`OrderRequest`, `ExecutionReport`, `BrokerHealthStatus`, etc.).
