---
title: Verified Outcome Recovery
status: active
owner: core-team
reviewed: 2026-07-19
audience: operators
---

# Verified Outcome Recovery

Use the terminal receipt as the recovery authority. Do not rely on the last console message, UI
toast, process exit code, declared filename, or an incomplete domain snapshot.

## Prerequisites and execution context

Identify the owning workflow, deployed version, tenant/company, and affected operation before
retrying. Retrieve its receipt through the authenticated workstation or the service-owned receipt
location named in [Lifecycle Control Plane](../reference/lifecycle-control-plane.md); this runbook
does not define a generic recovery endpoint. Use [Preflight](preflight-checklist.md#authenticated-evidence-collection)
for a scoped operator session. Restoring storage or restarting an installed host requires its
deployment owner and the domain's coordinated recovery procedure.

Keep the original receipt and referenced evidence in a restricted incident packet. Preserve
artifact bytes without altering them, and omit login bodies, cookies, API keys, and delivery
bearers from support notes. Pause dependent release/delivery actions while authority is uncertain.

## Triage sequence

1. Capture the operation ID, correlation ID, attempt, input hash, terminal state, and completion
   time from the receipt.
2. Validate every required postcondition. Treat an invalid receipt, missing evidence, broken hash
   chain, or unreadable artifact as a failure requiring escalation.
3. Retrieve each referenced evidence record and artifact. When retained content includes SHA-256
   and byte-length fields, verify both before using it downstream. URI-only operational evidence
   identifies the authoritative log or endpoint and must not be treated as a content snapshot.
4. Follow the structured recovery actions in order. Record the actor, reason, assignment, retry,
   exception, approval, or waiver in the same operational case history.
5. Retry with the same request identity only when the guidance declares the operation retry-safe.
   A changed canonical input requires a new operation identity.
6. Confirm the recovery attempt itself reaches a validated terminal receipt. Do not close a case on
   an intermediate transition or message.

Receipt validation includes the current schema version, positive attempt number, UTC timestamps,
correlation and input hash, evaluated postconditions, and resolvable evidence/artifact references.
`isSuccessful` alone is insufficient: the shared contract computes it from terminal state, while
the validator checks the receipt's invariants. Check `provenance` as well; seeded or simulated
evidence cannot establish a real production outcome.

## State-specific response

| State | Response |
| --- | --- |
| `Succeeded` | Confirm artifacts/evidence are retrievable, then continue. |
| `CompletedWithWarnings` | Review every warning and policy gate; continue only if the downstream operation permits it. |
| `Failed` | Stop dependent work, preserve exception/log evidence, apply the named recovery action, and retry only when safe. |
| `Blocked` | Resolve the named prerequisite, authority, continuity, or implementation blocker before resubmission. |
| Missing, invalid, or `Unknown` state | Preserve the response and owning service logs; establish the authoritative outcome before retrying or declaring rollback. |

## Startup recovery

The lifecycle supervisor opens the workstation only after exact `/readyz` success and a readiness
receipt bound to the current request. Its terminal receipt records the browser attempt. Request
retries append numbered attempt files and do not overwrite prior evidence. On malformed configuration, timeout,
process-start failure, or early process exit, preserve the supervisor or launcher failure receipt
and logs, use the reported repair action, and retry. A zero process exit without the bound receipt
is still a failed startup. `Degraded` health never satisfies the launch gate.

## Reconciliation and reporting recovery

- Keep unresolved value, quantity, and cost-basis breaks visible until assigned, resolved, waived,
  or superseded with actor, reason, approval, and evidence.
- Treat continuity failure or unreadable case history as a reporting blocker.
- Treat a verified legacy v1 reconciliation receipt as `Blocked`, not corrupt and not current:
  perform a governed reconciliation/close again so item-level break evidence is captured. Never
  synthesize the missing break evidence during migration. The first governed v2 retention preserves
  the exact v1 bytes beside the configured snapshot as
  `<snapshot>.legacy-v1.<content-hash-prefix>.json`, resets the active path to v2, and then retains
  the new authoritative receipt; keep that deterministic backup with the case evidence.
- Verify PDF, XLSX, CSV, and preview bytes against retained hashes before delivery.
- Never replace a failed required artifact with a declaration or omit it while reporting success.
- If the reconciliation queue changes during or immediately after final release, quarantine the
  package, preserve both queue-head receipts, and use governed restatement. A cross-store atomic
  queue/release lease remains required before concurrent release and casework can be certified.

## Completion and handoff

Record the original and recovery operation/attempt IDs, unchanged or newly approved input identity,
terminal receipt, verified artifact hashes/lengths, remaining warnings, and accountable owner.
Resume dependent work only when the required postconditions are satisfied and its policy permits
the remaining warnings. A timeout, missing output file, or failed client request never establishes
that a mutation rolled back; use the domain's receipt recovery before creating a replacement.

For domain-specific continuation, use [Governed Reporting Operations](governed-reporting-operations.md)
for hard-close/delivery recovery and [Statement Reconciliation Report Operations](statement-reconciliation-report-operations.md)
for retained intake/resume. Committed financial corrections follow those governed workflows;
editing receipts or clearing persisted state is not a recovery action.

For the contract shape and invariants, see
[Verified Operation Outcomes](../reference/verified-operation-outcomes.md). For lifecycle state and
receipt locations, see [Lifecycle Control Plane](../reference/lifecycle-control-plane.md).
