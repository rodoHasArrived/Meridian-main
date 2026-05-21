# Broker Order Routing Phased Enablement Runbook

## Purpose

This runbook defines broker-by-broker rollout controls for:
- read-only market and account data flows,
- paper-trading order flows, and
- production order routing.

It also defines mandatory gating evidence so order placement endpoints remain disabled until validation and signoff artifacts are present.

Current implementation note: `BrokerageOrderPlacementGate` enforces this runbook at the order
management boundary. When order-placement validation is required, submit paths fail closed unless
the configured validation and sign-off artifacts are present for the enabled broker flow.

## Configuration Flags

Configure `BrokerageConfiguration` with per-broker feature flags:

- `BrokerFlows[<brokerId>].ReadOnlyDataEnabled`
- `BrokerFlows[<brokerId>].PaperOrderFlowEnabled`
- `BrokerFlows[<brokerId>].ProductionOrderRoutingEnabled`

Use `ValidationGates` for endpoint gating inputs:

- `RequireValidationArtifactsForOrderPlacement`
- `ValidationArtifactPath`
- `SignoffArtifactPath`

## Phase 0 — Read-Only Validation

1. Enable read-only flows for the target broker.
2. Keep paper and production order routing disabled.
3. Run provider-health checks and account sync pull-only checks.
4. Capture evidence in the validation summary artifact.

### Minimum monitoring checks

- Provider connectivity and staleness checks are green.
- Symbol and quote ingest continuity is stable for the agreed observation window.
- Brokerage sync has no unresolved credential or schema failures.

### Rollback / disable

- Set `ReadOnlyDataEnabled=false` for the broker.
- Keep `PaperOrderFlowEnabled=false` and `ProductionOrderRoutingEnabled=false`.
- Open execution circuit breaker while triaging feed/provider instability.

## Phase 1 — Paper Order Flow

1. Keep production order routing disabled.
2. Enable paper order flow for the broker (`PaperOrderFlowEnabled=true`).
3. Validate order lifecycle: submit, partial fill, cancel, replay evidence continuity.
4. Confirm execution reconciliation between OMS state and paper ledger projections.

### Minimum monitoring checks

- Paper order submit/ack latency within desk threshold.
- Replay verification remains consistent after restart.
- Reconciliation queue has no unresolved critical breaks linked to paper fills/orders.

### Rollback / disable

- Set `PaperOrderFlowEnabled=false`.
- Close active paper sessions if the session state is inconsistent.
- Re-run replay verification and reconciliation workflows before re-enabling.

## Phase 2 — Production Routing Readiness Gate

1. Keep production routing disabled until both artifacts exist:
   - validation summary artifact (`ValidationArtifactPath`)
   - operator signoff artifact (`SignoffArtifactPath`)
2. Verify artifacts are current for the broker and account scope being enabled.
3. Enable `ProductionOrderRoutingEnabled=true` only after governance signoff.
4. Keep `RequireValidationArtifactsForOrderPlacement=true` so endpoints fail closed if artifacts disappear.

### Minimum monitoring checks

- Provider health and brokerage connection are green.
- Execution reconciliation backlog has no critical unresolved items.
- Circuit breaker is closed only after operator signoff confirmation.

### Rollback / disable

- Immediately set `ProductionOrderRoutingEnabled=false` for the broker.
- Optionally set `RequireValidationArtifactsForOrderPlacement=true` and rotate artifact paths to block all submit operations until re-certified.
- Open circuit breaker and annotate incident context in execution audit trail and operator inbox workflow.

## Operational Notes

- Endpoint behavior is fail-closed for order placement when required artifacts are missing.
- Apply the narrowest scope changes first (single broker, single account context).
- Prefer staged re-enable: read-only -> paper -> production.
