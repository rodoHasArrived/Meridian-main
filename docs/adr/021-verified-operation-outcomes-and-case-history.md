# ADR-021: Verified Operation Outcomes and Operational Case History

**Status:** Proposed
**Date:** 2026-07-19
**Owner:** core-team
**Reviewed:** 2026-07-19
**Deciders:** core-team
**Supersedes:** —
**Superseded by:** —

## Context

Meridian has several kinds of operational ambiguity that cannot be corrected by better status
messages alone. A command can narrate work without executing it, a multi-stage job can report
completion after a required export fails, a maintenance task can return success for a no-op, and a
generated report declaration can be mistaken for retained output bytes. Process-local workflow and
strategy state also loses assignments, retries, approvals, exceptions, and recovery attempts at
restart.

These behaviors are unsafe for an operational-finance system because callers cannot distinguish a
satisfied postcondition from an accepted request, a partial result, or a missing implementation.
Boolean `Success` flags and free-form messages do not carry enough evidence to make that
distinction.

## Decision

### One terminal vocabulary

Every admitted command, job, workflow transition, artifact-producing run, or lifecycle operation
must terminate with `VerifiedOperationOutcome`. Queries that only read state are not operations for
this decision. Compatibility result types may compose the receipt and derive a boolean from it; they
must not maintain an independent success claim.

The only terminal states are:

| State | Meaning |
| --- | --- |
| `Succeeded` | Every required postcondition is satisfied and no issue remains. |
| `CompletedWithWarnings` | Every required postcondition is satisfied, but retained warning evidence requires review or recovery guidance. |
| `Failed` | Work was attempted and at least one required postcondition is unsatisfied because execution failed. |
| `Blocked` | Work was withheld because a prerequisite, policy, authority, or implementation dependency is unavailable. |

An accepted operation that is interrupted after work begins must retain a terminal receipt; it
cannot invent a fifth `Cancelled` success-adjacent state. A request cancelled before admission may
still use the normal cancellation-token contract because no operation was created.

Every terminal receipt contains:

- operation, attempt, correlation, timing, and canonical input-hash identity;
- evaluated required and optional postconditions;
- retained evidence references and real artifact identities, byte lengths, hashes, and URIs;
- structured warning or error issues; and
- actionable recovery or review guidance whenever the state is not `Succeeded`.

`VerifiedOperationOutcomeValidator` enforces the state/postcondition/issue matrix, unique and
resolvable evidence/artifact links, valid hashes and artifact identities, and recovery requirements.
A message-only execution, swallowed required-stage failure, filename-only artifact, or unlinked
evidence record therefore cannot validate as success.

### Durable operational case history

Long-running or operator-controlled work appends `OperationalCaseHistoryRecord` events through
`IOperationalCaseHistoryStore`. Events retain state transitions, reasons, actors, assignments,
attempts, exceptions, approvals, evidence, artifacts, recovery attempts, terminal receipts, and a
bounded domain snapshot/metadata map. The file-backed implementation assigns one monotonic
sequence and a SHA-256 predecessor chain, serializes cross-process appends, writes atomically, and
fails closed on malformed, duplicated, or tampered history.

The case event and any attached terminal outcome must have matching correlation and input-hash
identity. Domain services replay the case history instead of maintaining a second authoritative
snapshot. If action and evidence cannot be committed atomically in the owning transactional store,
the remaining gap must stay visible in the production-readiness tracker until an outbox or
transactional boundary closes it.

### Artifact and continuity rules

Artifact postconditions are satisfied only by retained non-empty bytes with a verified content hash
and a stable retrieval URI. Previews are artifacts in the same immutable package, not transient UI
projections. Reconciliation evidence passed into close and reporting names exact break IDs,
value/quantity/cost-basis measures, blocked outputs, dispositions, approvals, and evidence hashes.
An unresolved break remains visible as a report-readiness blocker.

### Recovery and startup rules

The installed workstation may open only after `/readyz` returns success with exact `Ready` state and
the startup outcome has been retained. `Degraded` is observable but does not satisfy the browser
launch gate. Preflight failures are `Blocked`; timeout, early process exit, and failed readiness are
`Failed`; inability to ask the operating system to open an otherwise ready URL is
`CompletedWithWarnings` with a safe manual URL.

## Implementation Links

| Component | Location | Purpose |
| --- | --- | --- |
| Outcome contract | `src/Meridian.Contracts/Operations/VerifiedOperationOutcome.cs` | Terminal vocabulary and fail-closed invariant validator |
| Case-history contract | `src/Meridian.Contracts/Operations/OperationalCaseHistoryContracts.cs` | Durable event, assignment, retry, approval, exception, and recovery schema |
| Case-history port | `src/Meridian.Contracts/Operations/IOperationalCaseHistoryStore.cs` | Shared append/replay boundary |
| File history | `src/Meridian.Storage/Operations/FileOperationalCaseHistoryStore.cs` | Atomic, cross-process, tamper-evident persistence |
| Contract tests | `tests/Meridian.Tests/Contracts/VerifiedOperationOutcomeTests.cs` | Terminal-state and serialization invariants |
| Persistence tests | `tests/Meridian.Tests/Storage/Operations/FileOperationalCaseHistoryStoreTests.cs` | Restart, duplicate, concurrency, and corruption behavior |

## Rationale

One small shared receipt makes success semantics composable across modules without moving domain
policy into Contracts. A strict validator catches contradictory receipts at the producing boundary.
The append-only case spine preserves the operational narrative needed for restart, audit, support,
and recovery while allowing each bounded context to retain its own structured snapshot data.

## Alternatives Considered

### Keep booleans and improve messages

**Pros:** Minimal compatibility impact.

**Cons:** Messages remain non-machine-verifiable; callers cannot prove postconditions, retained
bytes, or recovery identity.

**Why rejected:** It preserves the ambiguity this decision is intended to remove.

### Add a terminal enum independently in each module

**Pros:** Domain teams can evolve without a shared contract.

**Cons:** State meanings, JSON values, evidence rules, and UI handling drift immediately.

**Why rejected:** Meridian's browser and WPF workstations and shared operations services need one
terminal vocabulary.

### Store only the latest workflow snapshot

**Pros:** Simple reads and small storage footprint.

**Cons:** Loses reasons, assignments, retries, exceptions, approvals, and recovery lineage; an
overwrite can erase the evidence needed to explain the current state.

**Why rejected:** Operational case history is part of the product record, not a disposable cache.

## Consequences

### Positive

- Callers can distinguish success, warning, execution failure, and prerequisite blocking without
  parsing prose.
- Restart and support workflows can replay who did what, why, with which inputs and evidence.
- Report and startup claims become tied to verifiable postconditions and retained artifacts.

### Negative

- Producers must calculate hashes, retain evidence, and model recovery instead of returning early
  booleans.
- Compatibility result types and clients need additive migration work.
- File-backed case replay is intentionally conservative and can cost more I/O than a latest-state
  snapshot until a transactional store is introduced.

### Neutral

- This ADR establishes the program contract and first adoption slices; it does not by itself close
  every broader production-readiness item such as ETL ownership leases, atomic action-plus-evidence
  outboxes, or multi-process disaster-recovery certification.

## Compliance

### Code Contracts

```csharp
public enum OperationTerminalState : byte
{
    Succeeded,
    CompletedWithWarnings,
    Failed,
    Blocked
}

public interface IOperationalCaseHistoryStore
{
    ValueTask<OperationalCaseHistoryRecord> AppendAsync(
        OperationalCaseHistoryAppendRequest request,
        CancellationToken cancellationToken = default);
}
```

New operational result types must compose `VerifiedOperationOutcome`. Tests must validate returned
receipts with `VerifiedOperationOutcomeValidator` and exercise failure or blocking postconditions,
not only the happy path.

### Runtime Verification

- `VerifiedOperationOutcomeValidator.ValidateAndThrow` rejects contradictory terminal receipts at
  operation boundaries.
- `OperationalCaseHistoryHashing` binds every retained case record to its predecessor and bounded
  data payload.
- Focused restart, corruption, failure, warning, blocking, artifact, and readiness tests enforce the
  first migration slices.

## References

- [ADR-017: Modular Operational Monolith](017-modular-operational-monolith.md)
- [ADR-020: Local Lifecycle Control Plane](020-lifecycle-control-plane.md)
- [Production-readiness tracker](../product/implementation-todo-list.md)

---

*Last Updated: 2026-07-19*
