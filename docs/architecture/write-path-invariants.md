# Write-Path Invariants (Operator-Critical Domains)

This document captures domain write invariants that must hold at persistence boundaries for operator/readiness workflows.

## Fund Structure Updates
- Every mutation is versioned and must advance aggregate version by exactly `+1`.
- Stale writes are rejected as concurrency conflicts.
- Transition evidence is append-only (`loan_event` lineage style) and never rewritten.

## Account Lifecycle Changes
- Account status transitions must be valid state-machine transitions (no implicit skips).
- Activation/closure events require effective dates and audit metadata.
- Duplicate externally triggered lifecycle commands must be idempotent by command key.

## Lending Workflow Transitions
- All servicing mutations are transactionally atomic with their event append and normalized projections.
- Duplicate command retries (same `command_id`) must not append duplicate events.
- Aggregate version mismatches are rejected with explicit `ConcurrencyConflict` domain errors.

## Banking Movements
- Cash movement rows must remain externally deduplicable by stable external references.
- Posting + workflow evidence append must share one transaction boundary.
- Reconciliation-facing state changes must produce append-only evidence records.

## MMF Cash Operations
- Cash subscription/redemption writes are append-only evented transitions.
- Operator-readiness transitions must persist auditable state/gate changes.
- External ingest/import commands must be idempotent under retry.
