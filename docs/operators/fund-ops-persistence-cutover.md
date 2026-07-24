# Fund Operations Persistence Cutover

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-05-31

This is the canonical operator lane for controlled persistence cutover and fallback sequencing.

## Scope

- persistence cutover sequencing for fund-ops operational data
- controlled role and permission boundary checks around persistence switches
- pre-cutover and post-cutover evidence requirements
- rollback requirements for partial cutovers

## Canonical Cutover Principles

- Do not switch persistence pathways during active rollout windows.
- Treat persistence cutover as a release gate with evidence artifacts attached.
- Preserve operational integrity by keeping queue/route visibility and reconciliation posture stable during transition.
- Keep provider and module fallback rules explicit in the linked runbook commands and evidence packets.

## Pre-Cutover Checks

Before initiating persistence cutover:

- Confirm launch/build posture is stable via the canonical preflight command set in [Operator Preflight Checklist](./preflight-checklist.md).
- Confirm provider state and queue health in operator inbox/reconciliation signals.
- Confirm checkpoint, import-state, and routing posture are captured in readiness evidence.
- Confirm ownership/approval has been recorded for the cutover window.

### Required evidence

- `wave1-validation-summary` or equivalent operator packet for the active batch
- support packet entries that explicitly show pre-cutover state and ownership
- packet consistency between readiness, operator inbox, and post-run verification

## Execution Sequence (Canonical)

1. Freeze non-essential route activity for affected fund-ops surfaces.
2. Capture checkpoint snapshot (state, queue depth, unresolved exceptions).
3. Switch cutover posture in the controlled sequence defined by owning code/config path.
4. Validate persistence read/write continuity with focused diagnostics.
5. Monitor operator inbox for regression signals for at least one readiness window.
6. Run a scoped reconciliation sweep and confirm no new critical breaks are introduced.

## Post-Cutover Validation

- Confirm critical queues remain healthy and no unresolved critical breaks are newly introduced.
- Reconcile evidence: if packet artifacts diverge, treat as rollback condition.
- Confirm operator handoff artifact includes:
  - timeboxed transition window
  - command sequence executed
  - verification outputs and failure signals
  - approver and fallback owner

## Rollback Criteria

- Any blocking mismatch between evidence artifacts and live operator signals.
- New high-severity reconciliations introduced by persistence switch.
- Missing required packet fields for approval trail or ownership traceability.

Rollback should restore last known-good persistence posture and re-run pre-cutover validation.

## Runbook Links

- [Failover and Recovery](./failover-and-recovery.md)
- [Reconciliation Operations](./reconciliation-operations.md)
- [Operator Preflight Checklist](./preflight-checklist.md)
- [Provider validation and evidence schema](../reference/provider-validation-evidence-schema.md)

## Source-Material Source and Archive

- Legacy source: [archive/docs/operations/fund-ops-persistence-cutover-runbook.md](../../archive/docs/operations/fund-ops-persistence-cutover-runbook.md)
- Archive copy: [archive/docs/operations/fund-ops-persistence-cutover-runbook.md](../../archive/docs/operations/fund-ops-persistence-cutover-runbook.md)
