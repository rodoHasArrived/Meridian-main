# Failover and Recovery

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-05-31

This page is the canonical operator guide for Meridian recovery posture and failover response.

## Recovery Posture

- Escalate only when evidence confirms impact on operator-facing production workflows.
- Keep evidence-first actions first; preserve immutable event stream and command trace for every intervention.
- Prefer source-owned recovery controls in runtime/services over local ad-hoc toggles.

## Recovery Decision Matrix

1. Detect symptom and scope (single provider, module, or full workflow surface).
2. Contain blast radius (disable affected route, reroute if policy allows).
3. Confirm state snapshots and checkpoint continuity.
4. Validate fallback behavior with a controlled command sequence.
5. Resume slowly only when evidence indicates no data-loss risk.

## Immediate Containment Checklist

- Stop non-essential route activity for affected flows.
- Disable automatic promotions for unresolved workflow rows.
- Verify provider failback/fallback policy remains explicit in the active runbook.
- Capture pre/post snapshots for reconciliation and readiness signals.

## Verification Commands

Use the same host mode as affected service surface (desktop or workstation host):

```powershell
dotnet run --project src/Meridian/Meridian.csproj -- --mode workstation --http-port 8080
curl http://localhost:8080/api/workstation/operator/inbox
curl http://localhost:8080/api/workstation/reconciliation/queue
curl http://localhost:8080/api/config/effective
```

If a service is unstable, switch to service-safe mode, re-run provider validation, and recheck operator inbox for recovery state changes before promoting.

## Evidence and Handoff

Recovery handoffs should include:

- Fault window, affected assets/providers, and blast radius.
- Recovery actions and exact command sequence.
- Packeted outcome and remaining risks with owners.
- Re-open checks and next validation gate.

## Related Canonical Pages

- [Provider Credential Operations](provider-credentials.md)
- [Preflight Checklist](preflight-checklist.md)
- [Reconciliation Operations](reconciliation-operations.md)

## Migration source

- Legacy source: [docs/operations/failover-and-recovery-runbook.md](../operations/failover-and-recovery-runbook.md)
- Archive copy: [archive/docs/operations/failover-and-recovery-runbook.md](../../archive/docs/operations/failover-and-recovery-runbook.md)

