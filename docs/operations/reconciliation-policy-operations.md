# Reconciliation Policy Operations

## Configuration

`ReconciliationGovernanceService` evaluates reconciliation runs against policy thresholds:

- `MaxOpenBreakCount`
- `MaxCriticalOpenBreakCount`
- `MaxAbsoluteVariance`
- `RequireSecondaryApprovalForWaivers`

The service writes durable audit entries to `artifacts/reconciliation/governance-audit.jsonl`.

## Escalation and Sign-off

If thresholds are breached:

1. Gate becomes `Blocked` unless a waiver is requested.
2. If waiver requires secondary sign-off, gate remains blocked until secondary approval is recorded.
3. With required sign-off complete, gate transitions to `ReviewRequired` for operator approval workflow.

## Evidence Export

Use `ReconciliationGovernanceService.ExportEvidenceAsync(...)` to emit:

- JSON payload (machine-readable)
- Markdown summary (operator-readable)

Store outputs under `artifacts/reconciliation/` following run-scoped naming.
