# Tradier Endpoint Catalog and Meridian Capability Mapping

**Last updated:** 2026-05-19  
**Status:** Planning reference for a Tradier provider integration (not yet implemented in Meridian)

This catalog lists Tradier Brokerage API endpoints needed to support Meridian workflows for:

- authentication and account identity
- balances, positions, orders, and executions
- option discovery (chains/quotes)
- historical market data retrieval

It also maps each endpoint group to Meridian capability lanes and calls out where impacts would surface in workstation readiness and operator inbox projections.

## Scope assumptions

- Tradier production host: `https://api.tradier.com/v1`
- Tradier sandbox host (paper): `https://sandbox.tradier.com/v1`
- OAuth exchange endpoint: `POST /oauth/accesstoken`
- API tokens can be used for direct account access in single-user flows.

## Endpoint catalog mapped to Meridian capabilities

| Capability lane | Tradier endpoint(s) | Meridian provider capability mapping | Support status in Meridian | Notes for readiness / controls |
| --- | --- | --- | --- | --- |
| Auth bootstrap | `POST /oauth/accesstoken` | Credential acquisition/refresh for provider setup and connection verification | **Unsupported** | Needed before any production control claim; no Tradier credential adapter is registered today. |
| Account identity/profile | `GET /user/profile` | Account selection + external-account linking metadata | **Unsupported** | Required to bind Tradier account IDs into fund-account brokerage sync posture. |
| Account balances | `GET /accounts/{account_id}/balances` | Read-only brokerage state (cash, buying power, equity/margin posture) | **Unsupported** | Would feed account-sync health and readiness blockers for missing or stale balances. |
| Account positions | `GET /accounts/{account_id}/positions` | Read-only position state projection | **Unsupported** | Would back portfolio/account operating-context checks and reconciliation preconditions. |
| Account history (fills/activity) | `GET /accounts/{account_id}/history` | Execution and cash activity evidence for reconciliation views | **Unsupported** | Needed for durable execution evidence and accounting break investigation. |
| Orders list / detail | `GET /accounts/{account_id}/orders`, `GET /accounts/{account_id}/orders/{order_id}` | Read-only order lifecycle projection | **Unsupported** | Needed for order-status verification and operator triage detail. |
| Place order | `POST /accounts/{account_id}/orders` | Paper order flow (submit order) | **Unsupported** | No Tradier execution gateway seam exists; cannot claim paper-trading flow support. |
| Modify / cancel order | `PUT /accounts/{account_id}/orders/{order_id}`, `DELETE /accounts/{account_id}/orders/{order_id}` | Paper order flow controls (replace/cancel) | **Unsupported** | Required for operator intervention controls in cockpit workflows. |
| Quotes | `GET /markets/quotes` | Market context data (quote snapshots for order/readiness context) | **Unsupported** | Needed for quote-aware readiness and execution staging context. |
| Option chains | `GET /markets/options/chains` (+ expirations/strikes helpers) | Options discovery for chain-driven workflows | **Unsupported** | Required for options strategy surfacing and chain-based validation. |
| Time & sales | `GET /markets/timesales` | Intraday tape context | **Unsupported** | Optional for minimal viability; useful for execution explainability. |
| Historical bars | `GET /markets/history` | Historical retrieval/backfill provider capability | **Unsupported** | Needed for research continuity and historical evidence paths. |

## Explicit unsupported / partial feature classification

- **Tradier provider registration:** **Unsupported** (no `Tradier` provider implementation, routing entry, or credential catalog row).
- **Read-only account state (profile/balances/positions/orders/history):** **Unsupported**.
- **Paper order flow (submit/replace/cancel):** **Unsupported**.
- **Production controls and live-readiness gating:** **Unsupported** for Tradier; no brokerage validation path is specific to Tradier.
- **Market-data options/quotes/historical retrieval:** **Unsupported**.
- **Partial status:** **None currently** — integration should be treated as zero-coverage until provider and tests are added.

## Where this would surface in workstation readiness and operator inbox

When a Tradier adapter is added, the capability posture should project through existing shared readiness/inbox seams:

1. **Trading readiness endpoint** (`GET /api/workstation/trading/readiness`) for aggregate readiness status and work-item projection.
2. **Trading workstation aggregate** (`GET /api/workstation/trading`) which embeds readiness.
3. **Operator inbox endpoint** (`GET /api/workstation/operator/inbox`) for actionable cross-workspace blockers, including brokerage-sync/readiness items.

### Meridian code surfaces already responsible for those projections

- `src/Meridian.Ui.Shared/Endpoints/WorkstationEndpoints.cs`
  - maps `/trading/readiness`
  - maps `/trading` with embedded readiness
  - maps `/operator/inbox` aggregation
- `src/Meridian.Ui.Shared/Services/TradingOperatorReadinessService.cs`
  - builds acceptance-gate status, brokerage-sync gate, and readiness work items
- `src/Meridian.Application/FundAccounts/IFundAccountService.cs`
  - exposes account sync history/readiness seams used by inbox/readiness projections

## Implementation notes for future Tradier onboarding

To move from **Unsupported** to **Partial/Supported**, the minimum sequence should be:

1. Add Tradier credential + provider registration (routing + capability metadata).
2. Implement read-only account sync (profile, balances, positions, orders/history).
3. Project sync status into trading readiness work items and operator inbox routing.
4. Add paper order execution seam (submit/cancel/replace) and tests.
5. Add options + historical market data coverage where required by current web workstation routes.

Until those steps land with executable evidence, Tradier should remain explicitly marked unsupported in readiness narratives and provider matrices.
