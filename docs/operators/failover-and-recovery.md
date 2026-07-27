# Failover and Recovery

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-07-19

This page is the canonical operator guide for Meridian recovery posture and failover response.

## Supported Recovery Unit

The supported local-workstation topology is recovered as one unit:

- the dedicated PostgreSQL database named by the production connection string; and
- the configured Meridian data root, including encrypted credentials, workflow evidence,
  execution controls, strategy state, WAL files, catalogs, and lifecycle receipts.

Do not restore only one side of this unit. A database-only or file-only restore can create an
apparently healthy host whose evidence and command state disagree.

`build/scripts/recovery/invoke-production-recovery.ps1` is the canonical automation. It creates a
custom-format PostgreSQL dump and a data-root ZIP, encrypts each with independently derived
AES-256 encryption and HMAC-SHA256 authentication keys, verifies plaintext and ciphertext SHA-256
hashes, publishes the backup atomically, applies retention only after success, and emits a JSON
receipt. The encryption key must be a 32-byte random value supplied through
`MDC_RECOVERY_ENCRYPTION_KEY_BASE64`; store it in the approved secret manager, never in the backup
location or repository.

## Backup

Quiesce write-producing workflows or place the lifecycle supervisor in its controlled drain state,
then run:

```powershell
$env:MDC_RECOVERY_ENCRYPTION_KEY_BASE64 = '<secret-manager-value>'
pwsh ./build/scripts/recovery/invoke-production-recovery.ps1 `
  -Mode Backup `
  -ConnectionString $env:MERIDIAN_LEDGER_CONNECTION_STRING `
  -DataRoot $env:MDC_DATA_ROOT `
  -BackupRoot 'E:\MeridianBackups' `
  -RetentionDays 35 `
  -MaximumRpoSeconds 3600
```

Copy the completed `backup-<UTC timestamp>` directory to the approved off-host backup target. Never
copy a staging directory whose name starts with `.backup-`.

## Clean Restore

Restore into a clean, dedicated database and an empty data root first. The database overwrite switch
is deliberately mandatory. A non-empty data root is rejected unless `-AllowDataOverwrite` is
explicit; when allowed, the prior root is moved to a timestamped quarantine sibling rather than
deleted.

```powershell
pwsh ./build/scripts/recovery/invoke-production-recovery.ps1 `
  -Mode Restore `
  -ConnectionString $env:MERIDIAN_LEDGER_CONNECTION_STRING `
  -DataRoot $env:MDC_DATA_ROOT `
  -BackupRoot 'E:\MeridianBackups' `
  -BackupPath 'E:\MeridianBackups\backup-20260719T031700Z' `
  -RestoreConnectionString $env:MERIDIAN_RECOVERY_CONNECTION_STRING `
  -RestoreDataRoot 'D:\MeridianRecovery\data' `
  -AllowDatabaseOverwrite
```

After restore, start the host against the recovery database/root and verify `/startupz`, audit-chain
integrity, ledger totals, open reconciliation cases, report-pack hashes, strategy/promotion lineage,
and the operator inbox before approving traffic.

## Recovery Drill And Objectives

`Production Certification` runs the same encrypted backup and clean restore path against disposable
PostgreSQL source/target databases on every scheduled and release-tag run. It validates a retained
database business row and an encrypted-vault file after restore and uploads the dated backup,
manifest, and receipt for 90 days.

```powershell
pwsh ./build/scripts/recovery/invoke-production-recovery.ps1 `
  -Mode Drill `
  -ConnectionString $env:MERIDIAN_LEDGER_CONNECTION_STRING `
  -DataRoot $env:MDC_DATA_ROOT `
  -BackupRoot 'E:\MeridianBackups\drills' `
  -RestoreConnectionString $env:MERIDIAN_RECOVERY_CONNECTION_STRING `
  -RestoreDataRoot 'D:\MeridianRecovery\drill-data' `
  -AllowDatabaseOverwrite `
  -MaximumRpoSeconds 3600 `
  -MaximumRtoSeconds 7200
```

The receipt fails the drill when the measured backup window exceeds the declared RPO or the clean
restore exceeds the declared RTO. A workflow definition is not drill evidence: retain the successful
run URL and its `production-recovery-drill-*` artifact with the release packet.

## Migration Rollback

Before applying a migration classified as destructive, create and verify a recovery-unit backup.
Apply the migration only after the backup receipt is `passed`. If validation fails, stop the host,
restore the pre-migration recovery unit into a clean target, replay only commands whose immutable
idempotency keys are later than the backup boundary, reconcile ledger/evidence totals, and switch the
lifecycle configuration to the restored target. Do not attempt an ad-hoc reverse migration when it
would discard data.

## Recovery Posture

- Escalate only when evidence confirms impact on operator-facing production workflows.
- Keep evidence-first actions first; preserve immutable event stream and command trace for every intervention.
- Prefer source-owned recovery controls in runtime/services over local ad-hoc toggles.

## Ingest WAL Replay Semantics

Market-data ingest recovery is at-least-once: prefer a detectable, idempotent replay over silent
loss.

- On startup the pipeline replays uncommitted WAL records to the primary sink. A crash that
  happened after the sink flush but before the dedup commit can produce duplicate rows in the
  sink; that is expected and safe. Missing events are not expected — treat any gap as an incident.
- Deduplication entries are versioned. Version-2 entries mean "sink durability confirmed" and
  suppress replay; legacy version-1 entries (written before this versioning existed) only
  suppress live ingress and are deliberately replayed during recovery, then upgraded. After
  upgrading a legacy install, one recovery pass may therefore write duplicates for events that
  were already persisted — reconcile downstream rather than deleting WAL files.
- Recovery fails closed. If the sink or the dedup ledger is unavailable, startup recovery
  surfaces the failure instead of acknowledging records it could not replay; fix the store and
  restart rather than truncating the WAL. Checksum-valid records whose payload cannot be
  deserialized follow `WalOptions.CorruptionMode` (`Halt` blocks startup for operator review).
- Producer acceptance (`TryPublish`/`PublishAsync`) is admission into the in-memory queue only —
  never treat it as a durable acknowledgement when reasoning about loss windows.

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

Also validate the latest receipt before declaring recovery complete:

```powershell
Get-Content 'E:\MeridianBackups\recovery-drill-receipt.json' | ConvertFrom-Json |
  Format-List status, backupId, measuredRpoSeconds, measuredRtoSeconds, completedAtUtc
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

- Legacy source: [archive/docs/operations/failover-and-recovery-runbook.md](../../archive/docs/operations/failover-and-recovery-runbook.md)
- Archive copy: [archive/docs/operations/failover-and-recovery-runbook.md](../../archive/docs/operations/failover-and-recovery-runbook.md)
