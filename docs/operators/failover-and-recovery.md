# Failover and Recovery

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-10-05

This page is the canonical operator guide for Meridian recovery posture and failover response.

## Prerequisites and execution context

- Run the recovery scripts in PowerShell 7 from the repository root on the Windows recovery host.
  The commands below use example backup/restore paths; replace them with approved locations and
  use a new receipt path for each attempt.
- Have `pg_dump`, `pg_restore`, and `psql` available, or supply their full paths with `-PgDumpPath`,
  `-PgRestorePath`, and `-PsqlPath`. Use PostgreSQL client tools compatible with the database being
  backed up and restored.
- Load the encryption key and source/target connection strings through the deployment's secret
  process. The script parses **keyword connection strings** containing `Database` and `Username`
  (or `User ID`), such as `Host=...;Database=...;Username=...;Password=...`. It does not accept the
  application host's `postgres://...` shorthand. Installed-host environment values are not
  automatically present in a separately opened PowerShell session.
- Resolve the actual data root from lifecycle configuration, verify access to both recovery stores,
  and prepare a clean target database/root. Record the committed-work boundary and retain the
  existing evidence before changing any writer or restore target.
- Use the [lifecycle supervisor](../reference/lifecycle-control-plane.md) to stop/drain the owned
  workload. Verification reads use the [operator session](preflight-checklist.md#authenticated-evidence-collection)
  with the restored company's permissions and scope.

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
hashes, authenticates the exact manifest bytes with a separately derived HMAC key, publishes the
backup atomically, applies retention only after success, and emits a JSON receipt. Keep the detached
`manifest.hmac` alongside `manifest.json`; metadata authentication is checked before any manifest
evidence is trusted or either store is restored. The encryption key must be a 32-byte random value supplied through
`MDC_RECOVERY_ENCRYPTION_KEY_BASE64`; store it in the approved secret manager, never in the backup
location or repository.

Receipts are created exclusively before backup or restore work begins. An existing `-ReceiptPath`
is rejected without changing it or starting recovery work. Omit that option for a unique per-run
receipt, or choose a new explicit path for every attempt, including failed attempts.
Both explicit and default receipt paths must be outside `DataRoot` and `RestoreDataRoot`, and
cannot be an ancestor of either root. This includes Restore's fallback to `DataRoot` when no
`RestoreDataRoot` is supplied. Conflicting paths are rejected before creating receipt directories
or files, taking a backup, quarantining a target, or invoking PostgreSQL tools; directory symlinks
and normalized path segments do not bypass this check.
Use ordinary drive or UNC paths on Windows; device namespace paths are rejected by preflight.

## Backup

Quiesce write-producing workflows or place the lifecycle supervisor in its controlled drain state,
then identify the last committed-work boundary verified recoverable across both stores. Set
`$lastVerifiedRecoverablePointAtUtc` to its actual UTC timestamp and `$recoverablePointEvidence` to
the retained verification reference, then run:

```powershell
if (-not $env:MDC_RECOVERY_ENCRYPTION_KEY_BASE64) { throw 'Load the approved recovery key first.' }
pwsh ./build/scripts/recovery/invoke-production-recovery.ps1 `
  -Mode Backup `
  -ConnectionString $env:MERIDIAN_LEDGER_CONNECTION_STRING `
  -DataRoot $env:MDC_DATA_ROOT `
  -BackupRoot 'E:\MeridianBackups' `
  -RetentionDays 35 `
  -LastVerifiedRecoverablePointAtUtc $lastVerifiedRecoverablePointAtUtc `
  -RecoverablePointEvidence $recoverablePointEvidence
```

Copy the completed `backup-<UTC timestamp>` directory to the approved off-host backup target. Never
copy a staging directory whose name starts with `.backup-`. The optional recoverable-point assertion
is retained in the manifest; archive hashing does not infer the latest recoverable committed work.
Omitting the assertion or its evidence still permits an archive backup but leaves recovery objectives
unproven.

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
PostgreSQL source/target databases. It validates a retained
database business row and an encrypted-vault file after restore. Before backup, it records a
committed checkpoint for that isolated fixture, verifies both stores, and retains checkpoint JSON
inside the data root with a SHA-256-bound evidence reference. The restored checkpoint and state
probes must agree. It uses RPO 3600s/RTO 7200s and uploads the dated backup, authenticated manifest,
checkpoint, and receipt for 90 days. This proves archive operations and those fixture probes; it
does not perform business reconciliation or operator acceptance.

Drill a retained backup to expose its age at simulated loss. The backup's manifest carries its
recoverable-point assertion and supporting reference; the drill verifies the archive before
recording `simulatedLossAtUtc` and `lossDeclaredAtUtc` and beginning the clean restore. The drill
records a simulated boundary without destroying source data. `sourceCommit` identifies the
authenticated backup-creation commit; `drillSourceCommit` separately identifies the code executing
the drill. Drilling a backup from commit A on commit B does not relabel that backup as B. An
unauthenticated schema-2 manifest is rejected; schema-1 backups remain archive-only and cannot
supply trusted checkpoint metadata.

All producer modes validate the execution `SourceCommit`, including drills of retained backups.
When `-SourceCommit` is omitted, `GITHUB_SHA` supplies the value or the script falls back to the
checkout's `HEAD`; an explicitly supplied blank value is rejected. Commit identifiers must be
exactly 40 or 64 hexadecimal characters, without whitespace or a trailing newline. The authenticated
manifest and standalone receipt validator use the same rule for backup provenance; the validator
also checks `drillSourceCommit`. Matching
completion evidence cannot make a malformed commit identifier valid.

```powershell
pwsh ./build/scripts/recovery/invoke-production-recovery.ps1 `
  -Mode Drill `
  -ConnectionString $env:MERIDIAN_LEDGER_CONNECTION_STRING `
  -DataRoot $env:MDC_DATA_ROOT `
  -BackupRoot 'E:\MeridianBackups\drills' `
  -BackupPath $retainedBackupPath `
  -RestoreConnectionString $env:MERIDIAN_RECOVERY_CONNECTION_STRING `
  -RestoreDataRoot 'D:\MeridianRecovery\drill-data' `
  -AllowDatabaseOverwrite `
  -MaximumRpoSeconds 3600 `
  -MaximumRtoSeconds 7200 `
  -ReceiptPath 'E:\MeridianBackups\drills\recovery-drill-receipt.json'
```

For a fresh-backup archive regression drill, omit `-BackupPath`. Supply
`-LastVerifiedRecoverablePointAtUtc` and `-RecoverablePointEvidence` if a verified committed-work
boundary is available. Neither the new archive nor a short backup duration demonstrates the age
of a retained recovery point. A retained backup's assertion cannot be replaced using these flags.

Schema-version-2 receipts preserve archive durations as `backupDurationSeconds` and
`restoreDurationSeconds`. `status: passed` means the archive operation succeeded. Recovery
`objectiveStatus` remains `unproven` until all required evidence exists, even when the archive
operation passes. RPO measures simulated loss minus the last verified recoverable point; RTO
measures operator acceptance minus declared loss. See
[Recovery Objectives](./service-level-objectives.md#recovery-objectives) for the required fields
and the one-hour/two-hour policy.

### Complete and validate the recovery evidence

1. Preserve the original drill receipt, manifest, and detached manifest authentication. Confirm
   `manifestAuthenticated: true`, its `manifestSha256`, `backupId`, `sourceCommit`, and
   `drillSourceCommit` identify the authenticated backup and drill code. Both commits must match
   the frozen release commit for same-release certification; a different retained-backup commit
   remains visible and requires its own explicit provenance review. Confirm the recoverable
   point, pre-loss verification, and loss milestones. A restore-only receipt lacks loss milestones and proves no
   drill objective.
2. Start the restored host, perform replay/reconciliation, verify the business state described
   under [Clean Restore](#clean-restore), and retain the outcome evidence. Record the actual
   reconciliation completion timestamp and reference.
3. The operations owner records actual acceptance after reconciliation, with their name, UTC
   acceptance timestamp, and retained acceptance reference. Do not substitute the archive return
   time or the time a reviewer later fills in this file.
4. Write the completion-evidence JSON using the drill's exact bindings and the recorded operator
   values. In the example below, populate the five completion variables from the completed
   reconciliation and acceptance records before writing the file. `Read-RecoveryJson` preserves
   the exact timestamp strings required by the evidence bindings:

```powershell
. ./build/scripts/recovery/recovery-evidence.ps1
$drillReceipt = Read-RecoveryJson 'E:\MeridianBackups\drills\recovery-drill-receipt.json'
[ordered]@{
  backupId = $drillReceipt.backupId
  sourceCommit = $drillReceipt.sourceCommit
  drillSourceCommit = $drillReceipt.drillSourceCommit
  manifestSha256 = $drillReceipt.manifestSha256
  simulatedLossAtUtc = $drillReceipt.simulatedLossAtUtc
  lossDeclaredAtUtc = $drillReceipt.lossDeclaredAtUtc
  reconciliationCompletedAtUtc = $reconciliationCompletedAtUtc
  reconciliationEvidence = $reconciliationEvidence
  operatorAcceptedAtUtc = $operatorAcceptedAtUtc
  operatorAcceptedBy = $operatorAcceptedBy
  operatorAcceptanceEvidence = $operatorAcceptanceEvidence
} | ConvertTo-Json | Set-Content 'E:\MeridianBackups\drills\recovery-completion-evidence.json' -Encoding utf8NoBOM

pwsh ./build/scripts/recovery/validate-recovery-receipt.ps1 `
  -ReceiptPath 'E:\MeridianBackups\drills\recovery-drill-receipt.json' `
  -RecoveryEvidencePath 'E:\MeridianBackups\drills\recovery-completion-evidence.json' `
  -OutputPath 'E:\MeridianBackups\drills\recovery-drill-evaluated-receipt.json'
```

The standalone validator binds the completion evidence to the same backup, backup and drill
commits, authenticated manifest digest, and loss milestones and writes a separate evaluated receipt. It independently enforces RPO 3600s and
RTO 7200s by default; explicit `-MaximumRpoSeconds`/`-MaximumRtoSeconds` overrides may tighten those
budgets. It recomputes both measurements and fails for absent, malformed, future, or out-of-order
milestones, missing evidence references or operator attribution, or a budget breach. Incomplete
or invalid evidence produces `objectiveStatus: unproven`; complete valid evidence over a budget
produces `breached`. Only complete valid evidence within both budgets produces `proven` and exit 0.
Automation does not supply operator acceptance or reconciliation on the operator's behalf. The
validator consumes the preserved producer receipt; it does not independently re-authenticate
archives without their key. Protect the original receipt and its retained artifact provenance.
Earlier schema-2 receipts without authenticated-manifest evidence cannot be upgraded by adding
those fields in completion JSON; run a new authenticated drill.

A workflow definition or green archive job is not accepted recovery-objective evidence. Retain
the release-commit run URL, its `production-recovery-drill-*` artifact, original receipt,
completion-evidence JSON, evaluated receipt with `objectiveStatus: proven`, referenced verification/
reconciliation/acceptance records, and the operations-owner review with the release packet. Record
the verdict in the [production-certification evidence ledger](../engineering/production-certification-evidence-chain.md#prd-015-recovery-drill-operator-review).
Historical receipts that call archive timings RPO/RTO prove archive round trips only; they cannot
close `PRD-015` or the recovery portion of `PRD-111`.

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
- Values are compared at the precision the store keeps, not the precision .NET carries.
  Timestamps resolve to microseconds and amounts, transaction amounts, and FX rates to ten
  decimal places, so a submitted value comes back rounded; comparing raw values would reject a
  retry that resubmitted the identical figure.
- The replay path applies the same ledger-book scope validation the append path applies. A
  request that could never have been posted — a line carrying no book dimension, for instance —
  is refused rather than acknowledged as posted because some other path already retains a
  journal under that identity.
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

The durable append seam resolves its own posting-identity collisions and now applies the same two
rules about what a retained value is:

- Timing and amounts are compared at the precision the store keeps. A retry that resubmitted the
  identical write used to be refused as a conflicting posting whenever its timestamp carried
  sub-microsecond ticks — which anything derived from the current clock does — or an amount
  carried more than ten decimal places. That failure was permanent, not transient: the retained
  journal already holds the identity, so no later attempt could have succeeded either.
- A leg's transaction-currency detail participates. Debit and credit are the functional amounts,
  so two legs can agree on every one of them while booking a different transaction currency,
  amount, or FX rate; that is now a conflict rather than an acknowledged replay.
- A *retained* leg carrying the identity translation of its functional amount — same currency on
  both sides, transaction amounts equal to the functional ones, rate 1 — is a replay of a posting
  that declares no currency detail at all. The `V_ledger_029` repair stamps exactly that shape
  onto legs written before the append path carried currency through, and most posting paths still
  build legs without it, so comparing presence rather than content would make exactly the legacy
  postings that repair exists to heal permanently unreplayable.
- This does **not** read in reverse. An identity translation names a currency, so a posting
  declaring one against a retained leg that records no denomination is asserting what the books
  say. Nothing on either replay path checks a leg's functional currency against its book's base
  currency, so that claim cannot be corroborated at the comparison and is refused rather than
  acknowledged.
- A detail that is not an identity translation is a claim either way, and remains a difference. A
  posting declaring a foreign denomination and rate against a leg that records no conversion is a
  different posting, not a missing label.
- The two instants stored as `infinity` and `-infinity` are compared exactly rather than reduced
  to a microsecond, so the largest finite timestamp the store can hold is not read as a replay of
  an infinite one.
- Timing is compared against what the store returns, which is a *signed* microsecond delta from
  2000-01-01 truncated toward that epoch. Journals dated before 2000 truncate upward, not
  downward, and the comparison mirrors that rather than flooring.

## ETL Source Retention Semantics

An ETL source's archive and error locations are single directories shared by every run of that
source, and sources are enumerated by file pattern with no cross-run name dedupe. A scheduled drop
that always lands the same well-known name resolves to the same retention path forever.

- Retention never overwrites. A free destination name is used as-is, so ordinary runs keep the
  original file name.
- A destination already holding **identical** content means the move completed on an earlier
  attempt. The source is consumed and no second copy is written, so a retried or resumed run
  converges rather than accumulating duplicates.
- A destination already holding **different** content is never replaced. The incoming source is
  retained beside it under a deterministic content-addressed name,
  `<name>.sha256-<first 16 hex of the content hash><extension>`. Both sources survive, and the
  same content always resolves to the same name, so replay stays idempotent.
- If that content-addressed path is somehow occupied by different content, post-processing fails
  closed and leaves the source in place rather than overwriting. Resolve the retained file before
  retrying.
- SFTP verifies destination contents before removing anything, and only pays for the transfer when
  a name actually collides; the ordinary path costs one existence check.

Expect `positions.sha256-….csv` style names in a retention directory to mean two genuinely
different sources arrived under one name — normally a re-sent or corrected file. Reconcile which
one the books should reflect rather than deleting either.

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

Start the restored source host in terminal 1 using [preflight](preflight-checklist.md#mandatory-command-set),
or start the installed host through its supervisor, with the intended restored database and data
root. In terminal 2, complete [operator sign-in](preflight-checklist.md#authenticated-evidence-collection)
to define `$meridianBaseUrl` and `$operatorSession`, then inspect:

```powershell
Invoke-RestMethod "$meridianBaseUrl/api/workstation/operator/inbox" -WebSession $operatorSession
Invoke-RestMethod "$meridianBaseUrl/api/workstation/reconciliation/queue-status" -WebSession $operatorSession
Invoke-RestMethod "$meridianBaseUrl/api/config/effective" -WebSession $operatorSession
```

Before declaring recovery complete, retain the actual reconciliation and operator acceptance
records and [validate their bound completion evidence](#complete-and-validate-the-recovery-evidence):

```powershell
pwsh ./build/scripts/recovery/validate-recovery-receipt.ps1 `
  -ReceiptPath 'E:\MeridianBackups\drills\recovery-drill-receipt.json' `
  -RecoveryEvidencePath 'E:\MeridianBackups\drills\recovery-completion-evidence.json'
if ($LASTEXITCODE -ne 0) { throw 'Recovery objectives remain unproven or breached.' }
```

If the host remains unstable, keep write-producing workflows stopped, retain the failing receipt
and logs, and repair the named dependency before retrying. A successful HTTP read is not completed
recovery: compare restored business totals, exact scope, and the accepted recovery boundary.

## Failure and recovery

| Failure | Diagnostic and next action |
| --- | --- |
| PostgreSQL tool missing or connection parse failure | Check the tool path and keyword connection-string fields from the prerequisites before retrying. |
| Receipt already exists or conflicts with a data root | Preserve the existing receipt and choose a unique path outside both roots and their ancestors. |
| Manifest/HMAC/hash verification fails | Stop the restore. Preserve the original archive and receipt; verify the selected key and complete archive through the backup owner. |
| Restore target is non-empty | Use the intended clean target or the documented, deliberate quarantine path; do not remove data merely to bypass the guard. |
| Archive passed but objective status is `unproven` | Complete the actual reconciliation and operator acceptance, then evaluate their bound evidence. Do not substitute archive timings for the missing milestones. |
| Objective status is `breached` or business state disagrees | Keep recovery unaccepted and escalate with the evaluated receipt, reconciliation result, and remaining gap. |


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

- Legacy source: [archive/docs/operations/failover-and-recovery-runbook.md](https://github.com/rodoHasArrived/Meridian-main/blob/8a420730765d99de02c2ac4e9ba6cea062987f9b/archive/docs/operations/failover-and-recovery-runbook.md)
- Archive copy: [archive/docs/operations/failover-and-recovery-runbook.md](https://github.com/rodoHasArrived/Meridian-main/blob/8a420730765d99de02c2ac4e9ba6cea062987f9b/archive/docs/operations/failover-and-recovery-runbook.md)
