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
  sink; that is expected and safe. The no-loss guarantee begins at the WAL flush durability
  boundary: once an event's WAL record is flushed, a missing event is not expected — treat any
  gap in WAL-flushed data as an incident. Events a crash catches in the in-memory queue,
  accepted but not yet WAL-flushed, are lost by design (see the producer-acceptance bullet
  below) and are not a replay defect.
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

## Executed Fill Delivery Semantics

Executed fills reach double-entry accounting through a separate durable handoff. Unlike ingest
admission, fill acceptance *is* a durability boundary.

- Accepting a fill is durable before it is queued. `LedgerPostingConsumer.PublishAsync` returns
  only after the posting store retained the fill on at least one of its two independent paths
  (atomic snapshot, WAL). If both fail the publisher raises and the order path fails closed.
  A returned acceptance therefore means the fill replays after a restart even if it was never
  posted.
- The ledger is posted before the fill is acknowledged. A crash between the authoritative
  journal write and the acknowledgement leaves the fill pending, and replay detects the existing
  journals — expect a re-examined fill, never a lost one.
- A stopped posting consumer refuses new fills rather than blocking. If its loop stops outside
  shutdown, publishers fail fast with a `ChannelClosedException` naming the posting scope instead
  of waiting on a channel with no reader. Fills already accepted stay durable and replay on
  restart; the process needs restarting to resume posting. Treat the critical log line naming the
  scope as the signal — a silently blocked publisher would otherwise look like a quiet desk.
- Fills the accounting publisher rejected are retained separately and replayed at startup. If
  that retained-failure store cannot be loaded, the load retries with backoff (1s to 30s) rather
  than giving up: those fills exist nowhere else, so replay is delayed, never cancelled. Repeated
  critical log lines about loading retained handoffs mean the backlog is still undelivered.

## Accounting Posting Replay Semantics

A generated posting candidate is posted against a `(ledger book, source event)` pair that is
uniquely indexed in the journal store, so that pair can hold exactly one journal.

- Re-posting the same candidate is a replay and returns the retained journal unchanged. This is
  the normal, safe response to a timeout or a retried operator action.
- A replay is verified, not assumed. The request is rebuilt into its complete posting command,
  normalized the same way the durable store normalizes an append, and compared against the
  retained journal on period, policy, rule, lineage, timing, idempotency, the full accounting
  scope and provenance carried in journal metadata (fund event, capital account, investor,
  payment intent, settlement reference, project, strategy, institution, symbol, and the rest),
  the correlation and governance approval attached to the posting, and the ordered lines with
  their accounts, amounts, dimensions, and transaction-currency detail (currency pair, both
  transaction-side amounts, and FX rate). Booking the same amounts against a different investor, capital
  account, or approval is a different posting, not a replay. Accounts are matched on ledger
  identity, so a line whose account name differs only in casing targets a different balance and
  is a conflict. Policy, policy version, rule, and rule version are retained verbatim and are
  matched the same ordinal way the governed posting target resolves its own collisions.
- Accounting timestamps are compared at the precision the store keeps. PostgreSQL resolves to
  microseconds while .NET carries finer ticks, so a submitted timestamp comes back truncated;
  comparing raw ticks would reject a retry that resubmitted the identical value.
- Normalization before comparison is deliberate. A posting with no treasury context drafts no
  idempotency key and is retained carrying the posting command's key, so an un-normalized
  comparison would reject an ordinary retry over a field the rebuild had not been given yet.
- Generated journal and line identities are excluded because a rebuild legitimately mints new
  ones. Journal tags and evidence references are also excluded: both carry approval-time state —
  approval id, approval state, a fingerprint over the approved command, and evidence merged with
  a clock stamp at append — that no rebuild can reproduce. Their durable content is largely
  mirrored by the metadata fields that are compared.
- A posting that disagrees with the retained journal is refused with a conflict naming the
  retained journal and the field that differed. It is *not* reported as a replay. Because the
  identity is already held, such a posting can never be appended, so acknowledging it would
  confirm accounting content that the books will never contain. Post a correction against the
  retained journal, or resubmit under the posting's own source event.
- A request that cannot be rebuilt into a posting — a blocked candidate, or a policy that no
  longer resolves — is also refused rather than replayed. The retained journal may well be that
  posting, but nothing at that point can establish it, and an unverifiable replay must not be
  reported as a completed one. Resolve the candidate, then retry.

Treat a conflict here as a reconciliation signal, not a transient error: two different postings
have been approved against one source event, and an operator has to decide which one the books
should carry.

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
