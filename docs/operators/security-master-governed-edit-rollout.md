# Security Master Governed Edit Rollout

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-10-15

## Purpose and Scope

Use this procedure to accept the durable generation fence introduced by
[#3130](https://github.com/rodoHasArrived/Meridian-main/pull/3130). Acceptance requires a coordinated
writer cutover, verified migration history, independent human schema/Storage review, and retained
operator evidence for the deployed artifact. A merged pull request and passing CI establish source
integration evidence; they do not establish production acceptance.

The fence covers the Security Master overlay, audit, revision and generation writes participating
in one PostgreSQL mutation. External approval-workflow persistence, downstream publish handlers,
and period-aware propagation retain their separate transactions and reconciliation contracts.

## Evidence State on 2026-10-15

| Gate | Retrieved evidence | Acceptance state |
| --- | --- | --- |
| Merged implementation | #3130 merged as `91b8314ff0f7670bbd2f3e78eeb7b04f22674985` on 2026-10-13; reviewed source head `3b1b64fa150ad040e910c5d9f5c8160932e36bed` | Recorded for that source revision |
| Hosted integration | [Meridian CI 38422621093](https://github.com/rodoHasArrived/Meridian-main/actions/runs/38422621093) succeeded, including `quality-gate` and `integration-gate`; service-backed logs report 1,198 Meridian and 12 Direct Lending tests passed with zero skips | Recorded for that source revision; rerun required for a changed release artifact |
| Schema control | [PostgreSQL Schema Control 38422620271](https://github.com/rodoHasArrived/Meridian-main/actions/runs/38422620271) succeeded | Recorded for that source revision |
| Independent human schema/Storage approval | Retrieved submissions contain a Codex automated `COMMENTED` review and a Copilot quota-failure `COMMENTED` submission. The final-head Codex completion summary is automated. No independent human approval receipt was retrieved | **Pending** |
| Old writer drain and migration | No target-environment writer inventory, drain receipt, migration log or deployed ledger result was retrieved | **Pending** |
| Production workflow and operator acceptance | No deployed-artifact workflow evidence or accountable operator decision was retrieved | **Pending** |

Two bot review threads remain unresolved in GitHub's retrieved #3130 thread metadata, including
the outdated schema-casing thread. The merged source includes savepoint recovery and schema-case
regressions; thread resolution metadata and independent human acceptance remain separate evidence.
This procedure records no production action or approval.
The current acceptance work is limited to code and database rehearsal. Production draining,
migration, deployment and operator acceptance remain pending under a separate production change.

## Prerequisites

- Name the release owner, independent human schema/Storage reviewer, and accepting operator in
  the deployment change record. Retain their identities, decisions, timestamps and artifact links.
- Obtain the independent review of migration 036, transaction composition, first-edit fencing,
  schema case folding, optional-step savepoint recovery, and the separate workflow/publication
  boundaries. Record its exact source revision and outstanding conditions.
- Identify the target database and effective
  [Security Master persistence configuration](../reference/environment-variables.md#database-persistence).
  All participating PostgreSQL stores must share the same configured connection string and schema;
  mixed durable/in-memory composition fails closed. Do not retain connection-string secrets.
- Inventory every Security Master writer, including workstation/API hosts, background jobs,
  scheduled/import workers and recovery processes. Identify the controller that can restart each
  writer, including autoscaling, failover and rollback automation.
- Retain a coordinated, tested backup and restore reference, release checksum, deployed migration
  inventory and rollback decision owner. Rehearse the cutover on a restored non-production copy.
- Retain passing GitHub Actions evidence for the release revision, including the service-backed
  PostgreSQL concurrency/recovery tests. Skipped database tests do not satisfy this gate.

## Execution Context

- Operator host: supported deployment workstation with PowerShell 7 and PostgreSQL `psql`.
- Source commands: repository root at the exact release revision, using its required .NET SDK.
- Database commands: an authenticated `psql` session against the verified target database, with
  deployment-approved credentials. The migration role and read-only verification role may differ.
- Installed release: use its existing [lifecycle supervisor](../reference/lifecycle-control-plane.md)
  and deployment controller. The source launch commands in [Operator Preflight](preflight-checklist.md)
  are a rehearsal path, not a second production host.

The controller-specific stop/start commands belong in the deployment change record. Record the
actual commands and receipts for the target deployment; no universal command here can establish
that its writers are drained.

## Procedure

1. Attach the independent review and current release CI receipts. Confirm the target, backup,
   artifact digest, complete writer inventory and approved maintenance window.
2. Close writer admission and pause all scheduled/import writers through their controllers.
   Drain in-flight work, then stop every old writer. Disable automated restarts of old artifacts.
   Retain instance/job identities, binary revisions, controller state, drain completion timestamps
   and confirmation that no old writer remains or can rejoin. An empty request queue or database
   connection list alone does not prove this. Keep admission closed if any writer is unaccounted for.
3. With old writers stopped, start only the reviewed release through the normal deployment path,
   keeping general mutation admission closed. Its `SecurityMasterMigrationRunner` applies pending
   migrations transactionally under the migration advisory lock before readiness succeeds. Retain
   startup/migration logs. Preserve the merged `036_security_master_workbench_generations.sql`;
   never renumber it, execute it as ad hoc SQL, or rewrite an applied ledger checksum.
4. In the verified target's `psql` session, run the read-only checks below. Set `sm_schema` to the
   effective PostgreSQL schema. Security Master uses unquoted migration identifiers, so configured
   `Ops` resolves to `ops`; use that effective spelling here.

   ```sql
   \set ON_ERROR_STOP on
   \set sm_schema security_master

   select current_database() as database_name, current_timestamp as observed_at;

   select ordinal, filename, checksum, applied_at
   from :"sm_schema".schema_migrations
   where ordinal = 36
      or filename = '036_security_master_workbench_generations.sql'
   order by ordinal, filename;

   select column_name, data_type, is_nullable
   from information_schema.columns
   where table_schema = :'sm_schema'
     and table_name = 'security_workbench_generations'
   order by ordinal_position;

   select c.conname, pg_get_constraintdef(c.oid) as definition
   from pg_constraint c
   join pg_class t on t.oid = c.conrelid
   join pg_namespace n on n.oid = t.relnamespace
   where n.nspname = :'sm_schema'
     and t.relname = 'security_workbench_generations'
   order by c.conname;
   ```

   Expect exactly one ledger row for ordinal 36, with the generation migration filename and checksum
   `7557305b13360bfd1c7a0ca28be1c8ce43ccd4e67dae4f540713348b466cc398`.
   Expect `security_id uuid NOT NULL`, `generation bigint NOT NULL`, a primary key on `security_id`
   and a check requiring `generation >= 0`. A newly created fence table may be empty: rows are
   created on first mutation. This is not evidence that writer admission can reopen.
5. Start the remaining updated instances with admission still controlled. Verify every instance's
   artifact identity, database/schema and readiness through the normal deployment and
   [authenticated preflight](preflight-checklist.md). Confirm old-artifact restart exclusion remains
   in effect.
6. Retain the non-production concurrency/recovery results for this artifact, then execute the
   designated production acceptance change through the governed workbench with the authorized
   maker/reviewer. Capture instance identity, security/revision/workflow/correlation IDs, predecessor
   and resulting states, overlay/audit evidence, and the publication outcome. Reconcile the external
   approval and downstream outcomes as described below. Use an approved ordinary change for the
   production check; the failure-injection suite belongs on the isolated test database.
7. Have the accepting operator record the deployed artifact, target environment, gate results,
   remaining conditions and explicit accept/hold decision. Reopen writer admission only after all
   required receipts are present and the decision is accept. Retain the admission/controller receipt.

## Concurrency and Recovery Rehearsal

On an isolated non-production database, load `MERIDIAN_SECURITY_MASTER_CONNECTION_STRING` through
the approved credential source and ensure `MERIDIAN_DISABLE_DOCKER_TESTS` is not `true`. From the
release checkout in PowerShell 7, run:

```powershell
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj `
    --filter 'FullyQualifiedName~SecurityMasterGovernedConcurrencyPostgresTests' `
    --logger 'trx;LogFileName=security-master-governed-concurrency.trx' `
    --results-directory artifacts/security-master-rollout
if ($LASTEXITCODE -ne 0) { throw 'Governed concurrency/recovery validation failed.' }
```

Retain the TRX and verify all cases executed with zero failures and zero skips. The current suite
has ten cases covering two independent instances, stale-edit revalidation, approval exclusion,
first-edit SQL/cancellation rollback, shared-connection reads, mixed-durability rejection,
mixed-case schemas, failed discard provenance, failed decision convergence and optional-write
savepoint rollback. A process exit of zero with skipped tests is insufficient. The required hosted
service-backed result remains authoritative; this rehearsal adds deployment-specific evidence.

## Separate Transaction Outcomes

| Boundary or interruption | Expected state and recovery evidence |
| --- | --- |
| Concurrent governed mutations read the same generation | One predecessor is invalidated; the losing command reports a generation conflict (legacy override HTTP route: `409`). Reload and explicitly retry so validation sees the winner. Do not automatically replay stale review assumptions. |
| Draft insert, cancellation or mandatory mutation fails | Overlay, participating review audit, draft and generation roll back together. Retain before/after evidence; do not repair by editing the generation row. |
| External workflow approved but revision remains Submitted | Retain the bound workflow's approved decision and original reviewer. The governed approval retry can reconcile that revision transition; a successful external approval does not prove the revision committed. |
| Approval committed but overlay decision convergence failed | Retain the logged convergence failure and revision/workflow IDs. Publish must converge the decision before its final transition; other staged revisions can defer it. |
| Downstream handler or period-aware propagation fails | Prior side effects may already have committed. The revision remains Approved; retain each handler outcome and retry the same revision through the governed publish path after correcting the failure. The ordered handlers rerun under their idempotency contracts. |
| Publish response is lost | Read the revision and downstream receipts before retrying. A Published revision does not re-enter the Approved-state publish path; verify already-committed outcomes rather than creating a replacement revision. |
| Optional discard SQL fails | Savepoints preserve the core withdrawal/rejection while optional convergence/provenance may need reconciliation. Retain the warning and unresolved state; do not equate a completed discard with all optional evidence being present. |

The final overlay-decision/Approved-to-Published transition is fenced in PostgreSQL. That local
transaction does not undo an already-persisted workflow decision or downstream side effect.

## Failure and Recovery

| Symptom | Safe next action |
| --- | --- |
| An old writer is active or restarts | Keep mutation admission closed, stop it through its controller, reconcile writes made during the overlap, and repeat the drain and acceptance checks. The new fence cannot protect writes by old binaries. |
| Migration/checksum/ordinal or table-shape mismatch | Stop the rollout with writes closed. Retain the artifact and ledger output; reconcile with the schema/Storage owner. Never delete ledger rows, change recorded checksums or overwrite migration 036. |
| Participant database/schema mismatch or in-memory store | Correct deployment composition and restart under the reviewed persistence configuration; repeat readiness and acceptance checks. |
| Savepoint creation, rollback or release fails | Treat the command as failed and verify rollback. Do not report optional-step success; investigate the database error before retrying. |
| Operator or independent review evidence is absent | Record hold with the missing receipt and accountable owner. Passing CI or merged status cannot substitute for the missing decision. |

For binary rollback, first close admission and drain the updated writers before starting old
binaries. The additive generation table may remain. Old binaries restore the former concurrency
limitation; retain an explicit rollback decision and a separately reviewed writer-serialization plan.
A rollback is not acceptance of the cross-instance guarantee. Do not drop the table as a binary
rollback step.

## Evidence and Handoff

The release owner retains a sanitized packet containing the independent human review, release and
CI identities, tested backup reference, target database/schema, complete writer inventory and drain
receipts, controller restart exclusions, migration logs and ledger checks, rehearsal TRX, deployed
workflow and downstream receipts, rollback posture, and accountable operator decision. Keep
credentials, session cookies and production secrets out of the packet.

Each receipt needs its environment, exact artifact/revision, timestamp, responsible person and
durable link. Mark unperformed checks and missing decisions pending. The evidence table above is
the retrieved source baseline, not a completed production packet. Shared typed-schedule validation
(#3126), explicit payload-family discrimination (#3127), durable historical visibility evidence and
shared economics remain separately scoped work; this rollout does not certify them. The identifier
lookup contract in #3131 uses stored timestamps for eligibility and explicitly excludes historical
commit visibility; repeated queries can change when an earlier-timestamped transaction commits later.

## Related References

- [Governed edit concurrency](../domain/security.md#governed-edit-concurrency)
- [Migration 036](../../src/Meridian.Storage/SecurityMaster/Migrations/036_security_master_workbench_generations.sql)
- [Security Master migration runner](../../src/Meridian.Storage/SecurityMaster/SecurityMasterMigrationRunner.cs)
- [Shared migration runner](../../src/Meridian.Storage/Migrations/PostgresMigrationRunner.cs)
- [Generation-fenced mutation boundary](../../src/Meridian.Storage/SecurityMaster/PostgresSecurityMasterMutation.cs)
- [Governed concurrency tests](../../tests/Meridian.Tests/SecurityMaster/SecurityMasterGovernedConcurrencyPostgresTests.cs)
- [Governed recovery tests](../../tests/Meridian.Tests/SecurityMaster/SecurityMasterGovernedConcurrencyPostgresTests.Recovery.cs)
- [Deployment and packaging](deployment-packaging.md)
