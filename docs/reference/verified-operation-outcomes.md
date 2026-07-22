---
title: Verified Operation Outcomes
status: active
owner: core-team
reviewed: 2026-07-19
audience: developers, operators
---

# Verified Operation Outcomes

`VerifiedOperationOutcome` is Meridian's terminal receipt for admitted operations. Producers use
the shared contract in `src/Meridian.Contracts/Operations`; callers validate the receipt instead of
inferring success from a message, HTTP status, process exit code, filename, or partially updated
domain object.

## Terminal states

| State | Required meaning | Caller action |
| --- | --- | --- |
| `Succeeded` | Every required postcondition is satisfied and there are no outstanding issues. | Continue and retain the receipt. |
| `CompletedWithWarnings` | Every required postcondition is satisfied, but warning evidence or follow-up remains. | Continue only where policy allows; surface warnings and recovery guidance. |
| `Failed` | Work was attempted and at least one required postcondition is unsatisfied because execution failed. | Stop dependent work; use the receipt's evidence and recovery action. |
| `Blocked` | Work was withheld because a prerequisite, policy, authority, or implementation dependency is unavailable. | Do not retry blindly; satisfy or explicitly resolve the named blocker. |

There is no terminal `Accepted`, `Running`, `Cancelled`, or message-only success state. A request
cancelled before admission may use normal cancellation semantics. Once admitted, interruption is a
terminal `Failed` or `Blocked` receipt with the unsatisfied postconditions recorded.

## Required receipt identity

Every receipt binds:

- operation ID, kind, attempt, correlation ID, start time, and completion time;
- a canonical SHA-256 input hash;
- each required and optional postcondition and whether it was satisfied;
- structured issues and actionable recovery guidance when the state is not `Succeeded`;
- evidence references with stable IDs and locations; and
- generated artifact IDs, media types, non-zero byte lengths, SHA-256 hashes, and retrieval URIs.

`VerifiedOperationOutcomeValidator` is the authoritative invariant check. Producers must validate
before returning or persisting a receipt. Consumers must fail closed when validation, identity,
hash, or evidence retrieval fails.

## Data provenance

Every receipt carries a `DataProvenance` signal (`Real`, `Simulated`, `Seeded`, or `Sample`) defined
in `src/Meridian.Contracts/Operations/DataProvenance.cs`. It defaults to `Real`; any operation whose
inputs are simulated, seeded, or sample data must downgrade it so the signal travels with the receipt
and downstream evidence gates fail closed on it. No client may present non-real figures as real:

- `DataProvenanceBadge.TryCreate` builds a persistent, non-dismissable badge (`Dismissable` is always
  `false`) that both the browser workstation and the WPF desktop shell render identically. `Real`
  produces no badge.
- The accounting append boundary (`AccountingPostingCommandValidator`) refuses to persist a figure
  whose evidence declares a simulated origin unless the posting carries the retained provenance mark;
  when marked, the mark is written onto the journal so it can never be silently lost.
- The demo-mode API always emits the `Seeded` label, and
  `ProductionServiceRegistrationPolicy.ResolveComposedDataProvenance` forces the `Simulated` label
  whenever the supported local-workstation posture binds an in-memory money-path store rather than a
  durable one.

## Persistence and replay

Operator-controlled or long-running work appends `OperationalCaseHistoryRecord` entries. History
retains transitions, reasons, actors, assignments, retries, exceptions, approvals, artifacts,
recovery attempts, and terminal receipts. Records use monotonic sequencing and predecessor hashes;
corrupt or discontinuous history is not a usable latest state.

Idempotent replay returns the original receipt for the same canonical input. Reusing an operation
or request ID with different canonical input is a conflict, not a new success. A retry after an
external effect must resume from retained evidence or verify the effect; it must not execute the
effect again merely because the caller timed out.

## Artifact and reporting rules

A declaration, filename, or media type is not an artifact. Artifact postconditions require retained
non-empty bytes, a verified content hash, and a stable retrieval URI. PDF, XLSX, CSV, and preview
outputs are individually hashed members of the governed artifact package. Reporting readiness
binds to the exact reconciliation break IDs, measures, dispositions, approvals, and evidence hashes
used to certify the run. Legacy reconciliation receipts that predate item-level break evidence fail
closed with a migration/re-close recovery action; migration must not manufacture evidence that the
original receipt did not retain.

## HTTP and UI handling

APIs may preserve compatibility fields, but those fields derive from the verified receipt. Browser
and WPF clients display the terminal state, issues, evidence, artifact links, and recovery guidance;
they do not convert `Failed` or `Blocked` into success based on an HTTP 2xx response. Commands that
return `CompletedWithWarnings` require an explicit policy decision before dependent operations
continue.

See [ADR-021](../adr/021-verified-operation-outcomes-and-case-history.md) for the decision and
[Verified Outcome Recovery](../operators/verified-outcome-recovery.md) for operator procedure.
