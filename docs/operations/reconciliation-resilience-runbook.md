# Reconciliation Resilience Runbook

## Purpose

This runbook defines production operations for resilient reconciliation workflows: incident triage, safe reprocessing, controlled replay/backfill, and emergency rollback.

## Contract and versioning strategy

All inbound/outbound reconciliation payloads must use `ReconciliationPayloadEnvelope<TPayload>` with:

- `schema` (`contractName`, semantic `major/minor/patch`, content type).
- `correlation` (`traceId`, `spanId`, `parentSpanId`, `workflowId`, `jobId`).
- envelope-level `payloadId`, `createdAt`, `producer`, `direction`, and `idempotencyKey`.

Versioning policy:

1. **Patch**: non-structural changes only.
2. **Minor**: additive-only fields/enums.
3. **Major**: breaking changes; dual-read period is required.
4. Producers advertise newest supported version; consumers must tolerate one lower minor version during rollout.

## Job orchestration resilience

Every reconciliation job must persist a `ReconciliationJobControl` projection with:

- retry attempt counters (`attempt`, `maxAttempts`, `nextAttemptAt`),
- dead-letter state (`deadLettered`, `deadLetterReason`),
- deterministic `idempotencyKey`,
- backpressure partition (`backpressureBucket`) for queue controls.

Operational defaults:

- Retry: exponential backoff with jitter.
- Dead-letter: send exhausted jobs to quarantine for operator review.
- Idempotency: reject duplicate keys already marked successful.
- Backpressure: throttle by `backpressureBucket` when SLA latency exceeds thresholds.

## Observability and SLO probes

Track `ReconciliationProcessingTelemetry` for every window:

- `matchRate`
- `breakRate`
- `slaMissCount`
- `p50LatencyMs`, `p95LatencyMs`, `p99LatencyMs`

Required logging shape:

- structured fields: `traceId`, `workflowId`, `jobId`, `payloadId`, `idempotencyKey`, `attempt`, `deadLettered`.
- transitions: queued, started, retried, succeeded, dead-lettered, replayed, backfilled.

## Feature-flag rollout

Use `ReconciliationRolloutFlags` to phase deployment by:

- client (`clientIds`)
- team (`teamIds`)
- counterparty (`counterpartyIds`)

Rollout sequence:

1. Enable for internal team and one pilot counterparty.
2. Expand to all teams in the pilot client.
3. Expand to additional clients.
4. Enable replay/backfill lanes (`allowReplay`, `allowBackfill`) after stability gates pass.

## Regression suites

Minimum automated suites per deployment:

1. Scenario packs: happy path, data drift, out-of-order events, missing source coverage.
2. Break lifecycle: Opened -> Triaged -> Calibrated -> Approved -> Closed, including escalation/supersede branches.
3. Approval policy: role-based sign-off, invalidated sign-off on upstream cursor drift, and reroute gates.

## Backfill and replay operations

For cutover rehearsal:

1. Select historical windows and seed envelopes with fixed `idempotencyKey` namespace.
2. Run replay in dry-run mode and verify telemetry deltas.
3. Run backfill in controlled batches by `backpressureBucket`.
4. Compare match/break distributions against baseline windows before promotion.

## Incident triage checklist

1. Confirm scope via `traceId` and affected `jobId`s.
2. Measure current SLA miss trend.
3. Quarantine toxic payloads (dead-letter) without stopping healthy buckets.
4. Reprocess eligible jobs with preserved idempotency keys.
5. Escalate if break-rate materially diverges from baseline.

## Emergency rollback

1. Disable `enabled` in `ReconciliationRolloutFlags` for affected client/team/counterparty slices.
2. Pause replay/backfill lanes.
3. Drain in-flight retries to dead-letter for forensic review.
4. Restore last known stable schema producer version.
5. Publish incident summary with correlation references and remediation owner.
