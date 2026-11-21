# Adapter semantics

Status: **law, additive.** Introduced 4.1 (track F of `docs/UX_DEPTH_4_1.md`). No existing API changes
behaviour.

Meridian had a complete vocabulary for **reading** a feed — `ConnectionState`'s
`live · delayed · partial · offline · error`, `FreshnessIndicator`, `SourceCoverageSummary` — and none
for the **outbound** integrations that carry the operator's instructions away from the workstation:
order adapters, custodian submissions, report delivery, webhook sinks. `CONTRACT_AUDIT.md` §A-5 and
§D-2 have carried "OMS adapter diagnostics (retry/next-retry/last-error per adapter)" as unbuilt since
July. This is the law for that surface; `AdapterDiagnostics` is its component.

The distinction that justifies a second vocabulary: a read feed's failure makes a figure **old**, and
the recovery is to wait or refetch. An outbound adapter's failure makes an instruction **unlanded** —
the operator believes something is in force that is not. Staleness and unlanded intent are different
harms and cannot share one word.

| | Inbound | Outbound |
|---|---|---|
| Surface | `ConnectionState`, `FreshnessIndicator` | `AdapterDiagnostics` |
| Vocabulary | `live · delayed · partial · offline · error` | `linked · degraded · retrying · suspended · unconfigured` |
| The question | "How old is what I am reading?" | "Did what I sent arrive, and if not, when is the next attempt?" |
| Recovery | wait, refetch, accept the age | retry now, suspend, rotate the key, escalate |

## The five postures

| Posture | Means | Must also state |
|---|---|---|
| `linked` | Round trips are succeeding inside the acknowledgement budget. | last round trip, p99 against budget |
| `degraded` | Succeeding, but outside budget, or partially (a subset of routes). | which budget, by how much, which routes |
| `retrying` | Failing, and a schedule is in force. | attempt *n* of *m*, backoff, **next attempt time** |
| `suspended` | Will not retry until an operator acts — breaker open, or stopped by hand. | who suspended it, when, what resumes it |
| `unconfigured` | No route or credential. Never rendered as a failure. | what is missing to configure it |

Nothing else is a posture. "Unknown" is not a posture: an adapter whose state cannot be read is
`degraded` with the reason stated, because the operator's exposure is the same as a partial failure.

## Rules

1. **A failure without a next attempt is a suspension.** `retrying` without a next-attempt time is a
   contradiction, and the component renders it as one rather than hiding it. This is the single rule
   most often broken by a spinner: it implies a schedule that may not exist.
2. **The last error is quoted, with its code and its clock.** Never normalized to "connection failed",
   never truncated to the first clause. An operator diagnoses from the adapter's own words; a summary
   sends them to a log they may not have.
3. **Retry is a schedule, not a spinner.** Attempt, backoff interval, next attempt. A spinner states
   only that someone is hoping.
4. **Queue depth carries the age of its oldest item.** "412 queued" is not a severity; "412 queued,
   oldest 18m" is. Depth without age cannot be triaged.
5. **Key rotation is a clock, not a checkbox.** Key id, age, expiry, and whether rotation is overdue —
   an expired signing key is the most common cause of a silent outbound failure.
6. **The adapter never reports the venue's health as its own.** Adapter up and venue down is
   `degraded` **with the venue named**. Collapsing the two makes an operator restart the wrong thing.
7. **Suspend, resume, and retry-now are consequential actions.** They route through `ActionGuard` and
   declare a reversibility class (`compensatable` — a suspension is undone by a resume, but the
   instructions that did not land during it are not). Never bare buttons.
8. **Diagnostics are per adapter, per environment.** A `PAPER` adapter and its `LIVE` twin are
   different rows, never one row with an environment toggle.
9. **Optimistic concurrency is visible.** `OmsOutboundContract` carries a `Version`; a mutating control
   on a row whose version has moved is disabled with the reason, per `VersionConflictNotice`.
10. **Print drops the controls, keeps the evidence.** A printed diagnostics panel is an incident record.

## Composition

```jsx
<AdapterDiagnostics
  adapters={rows} asOf="14:32:08Z" budgetNote="acknowledgement budget 250ms p99"
  selectedId={id} onSelect={setId}
  onRetryNow={guarded(retry)} onSuspend={guarded(suspend)} onResume={guarded(resume)}
  onRotateKey={guarded(rotate)} />
```

`AdapterPostureChip` is the cell-sized form for a blotter or a status bar. Pair the panel with
`LogTail` (what the adapter said) and `EventTimeline` (what the operator did about it) — the panel
states posture, the log states evidence, the timeline states intervention.

## Anti-patterns

- **A spinner in place of a schedule.** See rule 1.
- **A green dot for an adapter with 400 items queued.** Round trips succeeding is not the same as the
  queue draining; `linked` requires both, or the posture is `degraded`.
- **One row for both environments.** See rule 8.
- **A "reconnect" button with no statement of what happens to the queue.** Resuming replays; say so.
- **Rendering `unconfigured` in a failure colour.** It is an absence of intent, not a fault, and
  colouring it red trains operators to ignore red.
