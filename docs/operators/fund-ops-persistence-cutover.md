# Fund Operations Persistence Cutover

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-05-31

This is the canonical operator lane for controlled persistence cutover and fallback sequencing.
It coordinates domain-owned migrations and recovery; it is not a data-copy command. Changing a
connection setting selects a store and does not by itself migrate retained records into it.

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

- Inventory every affected store, its current and target database/schema, the deployed binary,
  service account, resolved data root, and the owner of each retained dataset. Resolve unified
  database settings and per-domain overrides using [Database Persistence](../reference/environment-variables.md#database-persistence).
  Preserve configuration provenance without copying connection-string secrets into the packet.
- Confirm launch/build posture is stable via the canonical preflight command set in [Operator Preflight Checklist](./preflight-checklist.md).
- Confirm provider state and queue health in operator inbox/reconciliation signals.
- Confirm checkpoint, import-state, and routing posture are captured in readiness evidence.
- Confirm ownership/approval has been recorded for the cutover window.
- Take one coordinated recovery point for the participating PostgreSQL stores and service-owned
  local state. For Reporting, use its [backup and recovery set](governed-reporting-operations.md#backup-and-recovery),
  which distinguishes durable reconciliation files from disposable statement runtime cache.
- Review [Fund Structure Tenant Backfill](fund-structure-tenant-backfill.md) for retained attribution
  and quarantine. Strict startup can refuse service after migrations; a connection change does
  not establish ownership of legacy records.

### Execution context and permissions

For an installed Windows workstation, use the lifecycle supervisor to stop/start the existing
host. For a source checkout, use PowerShell 7 and the repository-root commands in
[Preflight](preflight-checklist.md). Do not start a second writer against the same data root.
Configuration changes take effect in the restarted process. The deployment identity needs the
database migration privileges, and the validating operator needs the intended tenant/company and
workflow permissions through a signed-in workstation session. An API key alone does not supply
that scope.

Normal startup requires durable governance connections. The explicit non-production
`MERIDIAN_USE_INMEMORY_GOVERNANCE` profile is not a production rollback target; its governance
fallback is file-backed and does not make the ledger or Reporting production-ready.

### Required evidence

- `wave1-validation-summary` or equivalent operator packet for the active batch
- support packet entries that explicitly show pre-cutover state and ownership
- packet consistency between readiness, operator inbox, and post-run verification

## Execution Sequence (Canonical)

1. Drain affected writes and workers to the reviewed boundary, then stop all affected hosts.
   Record any unresolved delivery, close, or migration outcome before proceeding.
2. Capture the coordinated recovery point and checkpoint inventory: retained IDs, scope, queue
   depth, unresolved exceptions, and hashes or balances appropriate to each domain.
3. Apply the reviewed target configuration and the owning domain's migration/import procedure.
   Use the same version for the cutover and verification; retain migration receipts. Follow
   [Reporting's application-version barrier](governed-reporting-operations.md#migration-012-application-version-barrier)
   when that schema is included.
4. Start the intended host and complete authenticated preflight. Require successful migrations and
   strict tenant inspection, then compare scoped retained IDs, counts, balances, and receipt hashes
   with the checkpoint. A healthy HTTP listener alone does not establish read/write continuity.
5. Complete the approved bounded write/restart/read check for each affected workflow and retain its
   receipt. Monitor the operator inbox for the duration recorded in the cutover review; this
   runbook does not define a universal readiness-window length.
6. Run a scoped reconciliation sweep, confirm no new critical breaks, and record the accountable
   owner's acceptance before reopening normal writes and workers.

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

Keep affected writes stopped while the owner determines whether the target accepted changes.
If no target write or incompatible migration occurred, the reviewed prior configuration may be
restored. If changes committed or their outcome is uncertain, reconcile retained receipts first
and use a coordinated restore or forward repair; pointing the host back to an older store can
hide committed work. Do not restart an older binary against a schema that rejects its write
contract. Re-run migration, tenant, continuity, and reconciliation checks before resuming service.

Missing local output is not proof of rollback. Recover an uncertain tenant-backfill run using the
same retained run identity, and recover pending hard-close evidence using the
[governed Reporting procedure](governed-reporting-operations.md#recover-a-pending-hard-close-evidence-handoff).

## Runbook Links

- [Failover and Recovery](./failover-and-recovery.md)
- [Reconciliation Operations](./reconciliation-operations.md)
- [Operator Preflight Checklist](./preflight-checklist.md)
- [Fund Structure Tenant Backfill](./fund-structure-tenant-backfill.md)
- [Governed Reporting Operations](./governed-reporting-operations.md)
- [Provider validation and evidence schema](../reference/provider-validation-evidence-schema.md)

## Source-Material Source and Archive

- Historical source: [archived cutover runbook at commit 8a42073](https://github.com/rodoHasArrived/Meridian-main/blob/8a420730765d99de02c2ac4e9ba6cea062987f9b/archive/docs/operations/fund-ops-persistence-cutover-runbook.md)
