# Provider Capability Inventory (Current vs Target)

## Purpose

This inventory maps broker/provider capabilities to their owning command, shared API/service, and shared DTO/endpoint contract surfaces so gap closure can be planned with explicit module ownership.

Status definitions used below:

- **Supported**: capability is implemented and currently wired into the active operator lane.
- **Partial**: capability exists with runtime/build gating, reduced fidelity, or incomplete endpoint/contract coverage.
- **Missing**: capability is not implemented in the current codepath.

## Owning implementation touchpoints

### `src/Meridian.Application/Commands/`

- `CommandDispatcher.cs` (CLI wiring for top-level flags and command routing)
- `StatementCommands.cs` and `StatementImportCommands.cs` (broker statement import/reconcile entry points)
- `ConfigCommands.cs` and `ValidateConfigCommand.cs` (provider config validation surfaces)
- `DiagnosticsCommands.cs` and `DryRunCommand.cs` (operator verification probes)

### `src/Meridian.Ui.Services/`

- `Services/ProviderManagementService.cs` (provider catalog and management abstraction)
- `Services/BackfillProviderConfigService.cs` (provider backfill configuration)
- `Services/ProviderHealthService.cs` (provider health read model)
- `Services/CredentialService.cs` + `Services/OAuthRefreshService.cs` (credential + OAuth token lifecycle)
- `Services/ConfigService.cs` + `Services/ConfigServiceBase.cs` (persist/read provider options)

### `src/Meridian.Ui.Shared/`

- `Endpoints/ProviderEndpoints.cs` (provider options contracts)
- `Endpoints/ProviderConnectionEndpoints.cs` + `Services/ProviderConnectionLifecycleService.cs` (credential verification lifecycle; currently Alpaca-focused)
- `Endpoints/BrokerageConnectionEndpoints.cs` + `Services/BrokerageConnectionService.cs` (Robinhood read-only OAuth flow)
- `Services/BrokeragePortfolioSyncService.cs` (broker account/position/order/fill sync projection)
- `Services/AlpacaBrokerageConnectionService.cs` (Alpaca paper/live connection service)
- `Endpoints/ExecutionEndpoints.cs` (paper execution + brokerage gateway read/write seam)

## Current broker/provider inventory

| Broker / Provider | Authentication model | Account endpoints | Position model | Order lifecycle events | Execution / fill fidelity | Historical data availability | Rate limits | Sandbox / paper support | Key blockers requiring shared-contract updates |
|---|---|---|---|---|---|---|---|---|---|
| **Alpaca** | **Supported** (API key/secret with environment normalization and verification) | **Supported** (`/account` verification + workstation sync/account projections) | **Supported** (brokerage sync + paper portfolio projection) | **Supported** (execution endpoints + paper/readiness flow) | **Supported** (paper-first flow with readiness/replay guardrails) | **Partial** (available, but depth/coverage assumptions vary by lane) | **Partial** (operational handling exists, but no cross-provider normalized limit contract) | **Supported** (explicit paper endpoint + paper-first warnings) | Introduce shared `ProviderCapabilityDescriptor` DTO for explicit per-provider order/fill guarantees and history granularity metadata in workstation payloads. |
| **Robinhood** | **Partial** (read-only OAuth handoff and token exchange path; unofficial API constraints) | **Partial** (account sync available through brokerage projection path) | **Partial** (read-side projection supported; some lifecycle semantics adapter-specific) | **Partial** (gateway coverage exists but cancellation semantics and unofficial behavior remain bounded) | **Partial** (usable for paper/readiness evidence with bounded runtime assumptions) | **Partial** (daily/history support present but bounded by unofficial API and token state) | **Missing** (no shared normalized per-provider rate-limit contract exposed to UI/shared endpoints) | **Partial** (read-only aggregation flow; not equivalent to full official broker paper environment) | Add shared brokerage capability DTO fields that differentiate `readOnlyAggregation`, `orderSubmit`, `cancelSemantics`, and `officialPaperEnvironment` so readiness and Settings can avoid over-claiming parity. |
| **Interactive Brokers (IB)** | **Partial** (available behind `IBAPI` build/runtime gate) | **Partial** (real runtime available when IBAPI path is enabled) | **Partial** (model exists but operator-ready parity depends on gated runtime path) | **Partial** (implemented via gateway seam, with build/runtime gating) | **Partial** (fidelity is adapter-capable but not always available in default builds) | **Partial** (implementation exists but depends on IB runtime availability) | **Missing** (shared rate-limit posture contract not standardized at workstation/API layer) | **Partial** (paper usage exists conceptually, but delivery is gated by IB runtime path) | Promote IB runtime-gating state into shared contracts (`ProviderConnectionStatus`/workstation readiness) so UI can distinguish "implemented but gated" from "healthy and active." |
| **Polygon** | **Supported** (API-key provider configuration path) | **Missing** (market-data provider, not brokerage account provider) | **Missing** (no brokerage positions) | **Missing** (no brokerage order lifecycle surface) | **Missing** (not an execution broker in current architecture) | **Supported** (quotes/trades/aggregates/history lanes in provider/data workflows) | **Partial** (provider-specific throttling exists, but no normalized cross-provider contract) | **Missing** (no brokerage paper account concept; data-provider only) | Add shared contract split between `BrokerageCapabilities` and `MarketDataCapabilities` so workstation/settings can render non-broker providers without forcing brokerage fields. |

## Target broker inventory (planned/expansion lane)

| Target broker | Authentication model | Account endpoints | Position model | Order lifecycle events | Execution / fill fidelity | Historical data availability | Rate limits | Sandbox / paper support | Contract blockers to resolve before delivery |
|---|---|---|---|---|---|---|---|---|---|
| **StockSharp** (connector-dependent brokerage target) | **Partial** (framework hook exists; concrete auth contract not standardized) | **Missing** | **Missing** | **Missing** | **Missing** | **Partial** (connector-dependent potential) | **Missing** | **Partial** (depends on connector/exchange simulation mode) | Need provider-agnostic brokerage onboarding contract (`auth scheme`, `capability flags`, `environment modes`) shared across settings, readiness, and execution endpoints. |
| **Additional broker adapters (future)** | **Missing** | **Missing** | **Missing** | **Missing** | **Missing** | **Missing** | **Missing** | **Missing** | Define canonical shared enums/DTOs for auth model, account-sync scope, order-event guarantees, fill provenance, and paper/live environment support so new adapters can integrate without ad-hoc endpoint growth. |

## Shared-contract update backlog (cross-cutting)

1. **Capability descriptor contract**: add a shared provider capability DTO exposed by shared endpoints (provider, brokerage connection, workstation readiness) with explicit dimensions for auth mode, account scope, order/fill coverage, history granularity, and paper/live modes.
2. **Normalized rate-limit contract**: introduce shared fields for observed/specified limits (`requestsPerMinute`, burst policy, websocket/session caps, throttle state) and surface them in provider health/readiness responses.
3. **Runtime gating contract**: standardize "implemented vs enabled vs healthy" provider state (especially for build-flag adapters like IB) so UI does not conflate unavailable with failing.
4. **Brokerage vs market-data split**: separate contracts so data-only providers (e.g., Polygon) are first-class citizens without empty brokerage placeholders.
5. **Order/fill fidelity contract**: define shared fidelity metadata (partial-fill semantics, cancel semantics, venue/order-id provenance, reconciliation confidence) to support operator acceptance and report-pack evidence.

## Module ownership summary

- **CLI and operator command ownership**: `src/Meridian.Application/Commands/`
- **Client/service orchestration ownership**: `src/Meridian.Ui.Services/Services/`
- **Shared API + DTO + readiness/execution contract ownership**: `src/Meridian.Ui.Shared/Endpoints/` and `src/Meridian.Ui.Shared/Services/`

This document should be updated whenever a broker/provider adapter, shared workstation contract, or provider connection endpoint changes status.
