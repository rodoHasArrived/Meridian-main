# Reconciliation Operations

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-05-31

This lane is the canonical operator procedure page for reconciliation exception response, recovery prioritization, and evidence capture.

## Canonical Scope

- Use this page for active reconciliation posture, break handling flow, and escalation routing.
- Use [reference lookup tables](../reference/reconciliation-break-taxonomy.md) for taxonomy, severity, and reason code definitions.
- Use [provider status artifacts](../reference/provider-integration-status.md) for provider-linked reconciliation impact.

## Reconciliation Triage Sequence

1. Identify break class and severity from latest operator inbox.
2. Confirm whether the break is:
   - ingest-only,
   - pricing/valuation mismatch,
   - workflow assignment mismatch,
   - provider outage/fallback drift.
3. Apply the fastest safe containment action:
   - pause affected non-verified routes,
   - disable auto-escalation for the queue when required,
   - isolate affected accounts/entities if blast radius is high.
4. Capture evidence:
   - break identifiers,
   - impacted scope and value,
   - root-cause hypothesis and confidence,
   - command history and operator actions.
5. Escalate for approval/reopen only when automated recovery would alter authoritative records.

## Mandatory Checks

- Confirm provider/provider-connection state is current.
- Validate queue age and source-of-failure for each impacted asset class.
- Ensure break evidence is attached before status transitions from `active` to `resolved`.
- Validate any manual correction has traceability in the promotion packet.

## Command references

- Readiness and diagnostics entry points:

```powershell
dotnet run --project src/Meridian/Meridian.csproj -- --mode workstation --http-port 8080
curl http://localhost:8080/api/workstation/operator/inbox
curl http://localhost:8080/api/workstation/reconciliation/queue
```

- Support recovery commands follow per-provider policy in
  [provider validation evidence schema](../reference/provider-validation-evidence-schema.md).

## Cross-Runbook Links

- [Operator Preflight Checklist](preflight-checklist.md)
- [Failover and Recovery](failover-and-recovery.md)
- [Provider Credential Operations](provider-credentials.md)
- [Fund Ops Persistence Cutover](fund-ops-persistence-cutover.md) for approval ownership,
  cutover evidence, and recovery rollback gates.

## Migration source

- Legacy source: [archive/docs/operations/reconciliation-operations.md](../../archive/docs/operations/reconciliation-operations.md)  
- Archive copy: [archive/docs/operations/reconciliation-operations.md](../../archive/docs/operations/reconciliation-operations.md)
