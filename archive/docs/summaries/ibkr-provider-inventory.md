# IBKR Provider Endpoint Inventory

## Purpose

This document inventories the minimum Interactive Brokers (IBKR) endpoint surface Meridian needs to support production-grade brokerage and market-data workflows. It maps each endpoint capability to explicit provider integration responsibilities and defines fallback posture when IBKR-hosted services are degraded or unavailable.

Scope covers:

- Authentication/session bootstrap
- Account summaries
- Balances
- Positions
- Orders
- Executions/fills
- Symbols/contracts
- Historical bars/trades

## Integration Layer Responsibilities

Use the following roles consistently across the provider stack:

- **Auth client**: Owns IBKR gateway/session bootstrap and re-auth handling.
- **REST client**: Owns request/response workflows for snapshot and back-office style API calls.
- **Streaming/polling client**: Owns realtime feeds (streaming where available) and adaptive polling fallback.
- **Mapper**: Converts IBKR payloads to Meridian canonical contracts (`Quote`, `Bar`, `Position`, `Order`, `ExecutionFill`, account/balance projections).
- **Health reporter**: Emits endpoint-level health state, stale-data markers, throttling posture, and operator-facing incidents.

## Required Endpoint Inventory

| Capability | Endpoint family (IBKR Web API / TWS-Gateway equivalent) | Primary adapter owner | Shared support owners | Fallback when unavailable |
| --- | --- | --- | --- | --- |
| Authentication / session bootstrap | `POST /iserver/auth/status`, `POST /iserver/reauthenticate`, `GET /tickle` (or TWS/Gateway connection/session probes) | **Auth client** | Health reporter | Freeze order entry and account mutation actions; retain last known market/account snapshots as read-only with explicit stale badge; retry with exponential backoff and open operator inbox work item. |
| Account summaries | `GET /portfolio/{accountId}/summary` (+ linked account list discovery) | **REST client** | Mapper, health reporter | Serve last successful summary snapshot with timestamp; annotate fund-account readiness as degraded; continue non-account-critical market-data workflows. |
| Balances / cash / margin | `GET /portfolio/{accountId}/ledger` and margin-related account windows | **REST client** | Mapper, health reporter | Mark cash/margin posture unknown, block pre-trade controls that require margin certainty, and require manual override workflow before paper/live promotion. |
| Positions | `GET /portfolio/{accountId}/positions/{pageId}` and incremental position refresh windows | **REST client** with polling assist | Mapper, streaming/polling client, health reporter | Maintain last reconciled positions, suspend automated rebalance/flatten commands, and enqueue reconciliation task for operator review. |
| Orders (open + recent) | `GET /iserver/account/orders`, `POST /iserver/account/{accountId}/orders`, `DELETE`/cancel routes | **REST client** | Mapper, health reporter | Put order actions in protective hold, allow cancel-only mode if endpoint subset still healthy, and preserve queued intents for explicit operator replay. |
| Executions / fills | `GET /iserver/account/trades` or execution stream equivalents | **Streaming/polling client** | REST client, mapper, health reporter | Switch to periodic trade polling if stream drops; if both fail, stop PnL-realization dependent automation and flag replay/readiness evidence as stale. |
| Symbols / contracts / lookup | `GET /iserver/secdef/search`, `GET /iserver/secdef/info`, contract detail/qualification routes | **REST client** | Mapper, health reporter | Fall back to local symbol cache and previously qualified contracts; disallow opening new instruments not already resolved in cache. |
| Historical bars / historical trades | `GET /iserver/marketdata/history` (or IBKR historical data service equivalents) | **REST client** with pacing-aware scheduler | Mapper, health reporter, streaming/polling client | Defer backfill jobs, keep existing local archive read-only, and surface partial coverage gaps in data-quality projections and runbook outputs. |

## Adapter-Level Fallback Contract

When any required endpoint is unavailable, adapters must enforce all of the following:

1. **Canonical degradation state**: emit a structured provider health event with endpoint key, failure class (auth, transport, rate-limit, schema), started-at timestamp, and retry policy.
2. **Operator visibility**: route degradation to workstation operator inbox/work-item surfaces so trading readiness and reconciliation views show concrete impact.
3. **Safety-first execution policy**: default to deny new risk-increasing actions when balances, positions, orders, or fills are uncertain.
4. **Read-only continuity**: continue serving last-known-good projections with explicit staleness metadata instead of returning empty payloads when feasible.
5. **Recovery proof**: on restoration, emit recovery event and trigger reconciliation checks for positions/orders/fills before clearing degraded posture.

## Ownership Notes: CLI Command Entry Points

Command-layer ownership for IBKR-related invocation and operational probes is centered in `src/Meridian.Application/Commands/`:

- **`CommandDispatcher`** is the primary routing owner for CLI flags and should remain the single arbitration point for provider-related command precedence.
- **`DiagnosticsCommands`** owns quick connectivity/configuration probes used to confirm IBKR availability posture before broader runs.
- **`ConfigCommands` / `ValidateConfigCommand` / `ConfigPresetCommand`** own bootstrap-time correctness of provider credentials, endpoint selection, and environment profile wiring.
- **`DryRunCommand` / `SelfTestCommand`** own non-destructive readiness validation and should be the first stop for IBKR smoke verification.
- **`SymbolCommands`, `Backfill`-related flows via dispatcher, and `Query/Catalog` command handlers** own operator entry points that depend on symbols/contracts and historical endpoint availability.
- **`StatementCommands` and `StatementImportCommands`** own brokerage statement import/reconciliation entry points and should remain explicitly segregated from live endpoint polling concerns.
- **`ProviderCalibrationCommand`** owns provider degradation calibration workflows and should ingest incidents from IBKR endpoint failures for kernel tuning.

Implementation note: keep provider-specific execution logic out of command classes; commands should orchestrate and defer to provider/application services so IBKR adapter behavior remains testable and reusable.

## Ownership Notes: Shared Projection Surfaces

Shared surfaces for IBKR-backed projections span `src/Meridian.Ui.Services/` and `src/Meridian.Ui.Shared/`:

- **`src/Meridian.Ui.Services/` ownership**
  - Service-layer orchestration of API calls, retries, local caching, and model shaping for dashboard/desktop consumers.
  - `ProviderManagementService`, `ProviderHealthService`, backfill/data-quality services, and related API-client abstractions should translate endpoint degradation into coherent UI service states.
  - This layer should not embed transport-specific IBKR protocol behavior; it consumes canonical upstream endpoints and composes view-ready state.

- **`src/Meridian.Ui.Shared/` ownership**
  - Endpoint mapping and shared DTO/read-model projection surfaces for host APIs (for example provider connection, execution, symbol, backfill, and workstation endpoint groups).
  - `IBEndpoints` and adjacent endpoint modules own HTTP surface contracts exposed to UI clients; they should emit explicit stale/degraded metadata when IBKR dependencies fail.
  - Workstation/readiness projections should include provider-health-derived gating so operator workflows reflect auth, balance, position, and execution uncertainty.

## Operational Promotion Gate (IBKR-Specific)

Before promoting an IBKR integration change to broader usage:

1. Validate auth bootstrap + reauth behavior under session expiration.
2. Validate account summary/balance/position coherence against a known fixture account.
3. Validate order lifecycle (submit, acknowledge, cancel) including partial endpoint outages.
4. Validate execution/fill reconciliation and replay-readiness evidence refresh.
5. Validate symbol qualification and historical data fallback to cache/archived data.
6. Confirm provider health and workstation projections surface degraded state with actionable operator guidance.

This inventory should be treated as a baseline contract for any new IBKR adapter, refactor, or incident-retrospective hardening pass.
