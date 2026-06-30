# Provider Backfill Operations

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-06-30

This is the canonical operator lane for historical data backfill operations and recovery in Meridian.

## Scope

- backfill job submission and monitoring,
- provider priority and fallback behavior,
- gap remediation and quality checks,
- dry-run and evidence capture posture.

## Backfill operator workflow

Use this sequence for controlled backfill execution:

1. Configure provider credentials and priorities for all intended providers.
2. Run preview before execution and capture the preview evidence.
3. Run scoped backfill jobs (single symbol or small batches first).
4. Monitor status/progress APIs until completion or bounded retries.
5. Run quality check and gap report before archival or downstream promotion.

## Key endpoints

- `GET /api/backfill/providers`
- `GET /api/backfill/status`
- `GET /api/backfill/progress`
- `POST /api/backfill/run/preview`
- `POST /api/backfill/run`
- `GET /api/backfill/executions`
- `GET /api/backfill/statistics`

## Provider configuration posture

Use explicit provider priority and fallback policy matching current run intent:

- Configure provider enablement and `Priority` in backfill settings.
- Enable fallback only where temporary provider failures are acceptable.
- Validate credentials for key-backed providers before production backfill.

Keep provider lookup details in Reference lane files:
- [provider-capability-matrix.md](../reference/provider-capability-matrix.md)
- [provider-validation-matrix.md](../reference/provider-validation-matrix.md)

## Multi-symbol ordering and resume semantics

Current `HistoricalBackfillService` behavior is deterministic only within the configured execution
mode:

- `MaxConcurrentSymbols` caps concurrent symbol processing. If unset, the service uses the
  configured backfill job concurrency.
- When `SymbolPriorities` is supplied, lower numeric priority values process first. Priority keys are
  matched case-insensitively.
- When no symbol priorities are supplied and concurrency is `1`, input symbol order is preserved.
- When concurrency is greater than `1`, completion order is not a stable ordering contract. Use
  per-symbol validation signals and checkpoints rather than task completion order as evidence.
- `ResumeFromCheckpoint=true` skips fully covered symbols, advances partial symbols to the day after
  the recorded checkpoint, and emits warning signals when checkpoint bar-count evidence is missing.
- Fresh runs without the resume flag clear matching-granularity checkpoints before writing new
  checkpoint evidence; they do not clear checkpoints recorded for another granularity.

## Execution commands

```bash
# Full symbol history
Dotnet run -- --backfill --backfill-symbols AAPL --backfill-from 2000-01-01 --backfill-to 2026-01-01

# Incremental/scheduled catch-up
Dotnet run -- --backfill --backfill-symbols AAPL --backfill-from 2025-01-01

# Scoped date range
Dotnet run -- --backfill --backfill-symbols AAPL,MSFT --backfill-from 2025-06-01 --backfill-to 2025-12-31

# Dry-run validation
Dotnet run -- --backfill --backfill-symbols AAPL --backfill-from 2025-01-01 --dry-run
```

> Note: in examples above, use lowercase `dotnet` command.

## Quality and gap checks

Before accepting outputs, run:

- backfill gap report (`dotnet run -- --gap-report ...`)
- quality report (`dotnet run -- --quality-report <symbol>`)
- review execution history/lineage before promoting archive/parquet transitions.

## Gap-remediation SLA posture

The current automation provides guardrails for gap remediation, but it is not a complete
cross-provider SLA engine. Treat these as the active operator semantics until a provider-governance
workflow enforces timers end to end:

- Detect: a gap report, data-quality gap, or quality alert must identify symbol, provider when known,
  date range, granularity, severity, and affected downstream workflow.
- Triage: critical gaps that block paper promotion, reconciliation, accounting, or governed reporting
  require same-business-day owner assignment and a retained runbook entry.
- Attempt: auto-remediation may run when the gap exceeds the configured minimum duration/size and is
  not under symbol/provider cooldown. Duplicate triggers are suppressed by idempotency; transient
  provider failures may retry with the same remediation key.
- Fallback: cross-provider repair must use the configured provider priority/fallback order and must
  preserve the original source/provider evidence for audit comparison.
- Prove: a gap is not closed until the rerun has execution history, per-symbol validation signals,
  and a follow-up gap/quality check showing the affected interval is acceptable for the downstream
  workflow.
- Escalate: if provider fallback cannot close the interval, attach the failed execution evidence and
  route the exception to provider readiness, reconciliation, or reporting owners according to the
  blocked workflow.

## Backfill controls for operators

### Provider mode semantics

- Run with the minimal required provider set for validation.
- If a single provider becomes unavailable, use the configured fallback order explicitly.
- Review error and rotation signals before escalating.

### Recovery and throttling controls

- Honor rate-limit pressure by widening intervals rather than forcing retries.
- Use incremental backfill windows when a full range is high risk.
- Escalate unresolved critical breaks only with evidence attached.

## Mandatory evidence

Backfill operations entering support/handover should include:

- preview output reference,
- run request/command capture,
- status/progress endpoint observations,
- quality/gap output for one representative symbol or batch,
- operator action log (start, stop, retry, completion).

## Related operator runbooks

- [Operator Preflight Checklist](./preflight-checklist.md)
- [Reconciliation Operations](./reconciliation-operations.md)
- [Failover and Recovery](./failover-and-recovery.md)

## Source and archive

- Legacy source archived at [archive/docs/providers/backfill-guide.md](../../archive/docs/providers/backfill-guide.md)
