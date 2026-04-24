# Live Execution Controls

**Last Updated:** 2026-04-20

This guide covers the operator-facing controls that gate live execution in Meridian while preserving `src/Meridian.Ui.Shared/Endpoints/ExecutionEndpoints.cs` as the stable backend seam.

## What Is Covered

- durable execution audit trail for order flow, operator actions, control changes, and promotion approvals
- runtime operator controls for circuit breakers and manual overrides
- governed `Paper -> Live` promotion with explicit human approval and override requirements
- live/paper gateway selection through the existing execution REST surface

## Required Environment

For the embedded UI host (`src/Meridian/Meridian.csproj`), live routing is enabled with environment variables:

```powershell
$env:MERIDIAN_EXECUTION_GATEWAY = "alpaca"
$env:MERIDIAN_EXECUTION_LIVE_ENABLED = "true"
$env:ALPACA_KEY_ID = "<key>"
$env:ALPACA_SECRET_KEY = "<secret>"
```

Optional guardrail variables:

```powershell
$env:MERIDIAN_EXECUTION_MAX_POSITION_SIZE = "100"
$env:MERIDIAN_EXECUTION_MAX_ORDER_NOTIONAL = "25000"
$env:MERIDIAN_EXECUTION_MAX_OPEN_ORDERS = "10"
```

If `MERIDIAN_EXECUTION_LIVE_ENABLED` is missing or `MERIDIAN_EXECUTION_GATEWAY` resolves to `paper`, `Paper -> Live` promotion remains blocked.

## Execution Endpoints

Existing order/account seams remain in `/api/execution/*`.

Current execution SDK order types available through `/api/execution/orders/submit`:

- `Market`
- `Limit`
- `StopMarket`
- `StopLimit`
- `MarketOnOpen`
- `MarketOnClose`
- `LimitOnOpen`
- `LimitOnClose`

Gateway capabilities are authoritative. A live gateway must list `MarketOnOpen`, `MarketOnClose`,
`LimitOnOpen`, or `LimitOnClose` only when the adapter preserves the open/close timing qualifier all
the way to the broker. If a gateway does not list one of those order types, the OMS rejects the order
before it can be routed. Adapters must not fall back from `*OnOpen`/`*OnClose` to plain `Market` or
`Limit` because that can turn a session-scoped instruction into an immediate order.

Current provider behavior:

| Gateway | `*OnOpen`/`*OnClose` behavior |
|---|---|
| Alpaca | Not advertised; rejected because the adapter does not preserve the qualifier. |
| Interactive Brokers | Not advertised; rejected until Meridian maps native IB open/close order semantics end to end. |
| Robinhood | Not advertised; rejected because the unofficial API mapping only preserves base order types. |
| Paper | Not advertised by the `IOrderGateway` adapter; rejected because the scaffold fills immediately instead of preserving session timing. |

New live-readiness endpoints:

- `GET /api/execution/audit`
- `GET /api/execution/controls`
- `POST /api/execution/controls/circuit-breaker`
- `POST /api/execution/controls/manual-overrides`
- `POST /api/execution/controls/manual-overrides/{overrideId}/clear`

Operator identity is taken from `X-Meridian-Actor` when present, otherwise the authenticated user name is used.
Circuit-breaker and manual-override mutations also accept a caller-supplied `correlationId` in the JSON body so the resulting audit entries can be traced end to end.

Cockpit write conventions:

- order submits include `metadata.actor` and `metadata.correlationId`
- order submits should also include `metadata.sessionId` for paper-session continuity
- order submits should include `metadata.runId` when the order is tied to a promoted run
- promotion approvals use the full `PromotionApprovalRequest` payload: `runId`, `reviewNotes`, `approvedBy`, `approvalReason`, and `manualOverrideId`

## Audit Categories

Audit records are written under the execution data root and surfaced through `GET /api/execution/audit`.
The audit trail WAL is initialised lazily on first read or write so host construction does not sync-block on recovery during startup.
When live routing is enabled, `AddBrokerageExecution(...)` now passes operator controls, audit trail, security-master gate, and shared portfolio state into `OrderManagementSystem`, and the OMS connects the selected live gateway on demand before the first order-side operation.

Expected categories:

- `Order`: OMS submit/cancel/modify and gateway-connect outcomes
- `OperatorAction`: REST-initiated submit/cancel/close actions
- `Control`: circuit-breaker and manual-override changes
- `Promotion`: approval and rejection decisions for mode promotion

## Standard Operator Flow

1. Check health with `GET /api/execution/health`.
2. Review current controls with `GET /api/execution/controls`.
3. Open the circuit breaker immediately if routing must stop:

```http
POST /api/execution/controls/circuit-breaker
{
  "isOpen": true,
  "reason": "Manual halt",
  "correlationId": "corr-breaker-open-001"
}
```

1. Create a scoped live-promotion override before approving `Paper -> Live`:

```http
POST /api/execution/controls/manual-overrides
{
  "kind": "AllowLivePromotion",
  "reason": "Risk review completed",
  "strategyId": "strategy-123",
  "runId": "run-456",
  "expiresAt": "2026-04-20T22:00:00Z",
  "correlationId": "corr-live-override-001"
}
```

1. Approve the promotion:

```http
POST /api/promotion/approve
{
  "runId": "run-456",
  "reviewNotes": "Replay verified and controls green.",
  "approvedBy": "ops",
  "approvalReason": "Risk review completed",
  "manualOverrideId": "ovr-..."
}
```

1. Clear the override once the decision window is complete:

```http
POST /api/execution/controls/manual-overrides/ovr-.../clear
{
  "reason": "Promotion window complete",
  "correlationId": "corr-live-override-clear-001"
}
```

1. Confirm the resulting `auditReference` from the approval response and review recent audit entries.

## Notes

- `Paper -> Live` promotion is blocked while the execution circuit breaker is open.
- Live promotion requires both a non-paper live gateway configuration and an active `AllowLivePromotion` manual override.
- `BypassOrderControls` is for tightly scoped order exceptions and should be short-lived.
- `ForceBlockOrders` can be used to stop routing for a symbol or strategy without opening the global breaker.
- Replay verification responses now include evidence counts, last-persisted timestamps, and a `verificationAuditId` that ties the cockpit evidence back to the execution audit trail.

## Validation Evidence

The repo includes executable coverage for the stable seam and Alpaca gateway path:

- `Meridian.Tests.Ui.ExecutionGovernanceEndpointsTests.ControlsEndpoints_UpdateCircuitBreakerAndExposeAuditTrail`
- `Meridian.Tests.Ui.ExecutionGovernanceEndpointsTests.AlpacaExecutionPath_SubmitsOrderThroughStableExecutionSeam`
- `Meridian.Tests.Ui.ExecutionGovernanceEndpointsTests.RobinhoodExecutionPath_SubmitsOrderThroughStableExecutionSeam`
- `Meridian.Tests.Execution.OrderManagementSystemTests.PlaceOrderAsync_WhenGatewayStartsDisconnected_ConnectsAndAuditsSelectedGateway`
- `Meridian.Tests.Strategies.PromotionServiceLiveGovernanceTests`
- `Meridian.Tests.Execution.OrderManagementSystemGovernanceTests`
