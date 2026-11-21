# AdapterDiagnostics

The outbound half of connection semantics. Track F of `docs/UX_DEPTH_4_1.md`; law in
`guidelines/ADAPTER_SEMANTICS.md`.

```jsx
<AdapterDiagnostics adapters={rows} asOf="14:32:08Z" scope="LIVE"
  budgetNote="ack budget 250ms p99"
  onRetryNow={guarded(retry)} onSuspend={guarded(suspend)} onResume={guarded(resume)}
  onRotateKey={guarded(rotate)} />
```

## Reach for it when

An instruction leaves Meridian and something must confirm it landed: OMS/order adapters, custodian
submissions, report delivery, webhook sinks, posting exports. Not for inbound feeds — that is
`ConnectionState` plus `FreshnessIndicator`, and the two vocabularies are deliberately different
(a stale read makes a figure old; a failed adapter makes an instruction *unlanded*).

## What the row must carry

`posture` · `lastRoundTrip` · `latency.p99` against `latency.budget` · `outbound.queued` **with**
`outbound.oldest` · `retry.nextAt` while retrying · `lastError` verbatim with code and clock ·
`key.rotatesAt`. Anything less and the operator cannot triage without leaving the screen.

## Rules the component enforces for you

- A `retrying` row with no `retry.nextAt` renders a flag: *retrying with no next attempt is a
  suspension in fact*. Do not work around it by hiding the row — fix the state or resume it.
- Worst posture sorts first (`sortByPosture`, default on): retrying → degraded → suspended → linked →
  unconfigured.
- `unconfigured` is `info`, never a failure colour. It is an absence of intent.
- `versionStale` disables every mutating control and states why (`OmsOutboundContract.Version`).
- Controls disappear in print; the evidence stays. A printed panel is an incident record.

## Composition

Pair with `LogTail` (what the adapter said) and `EventTimeline` (what the operator did about it).
`AdapterPostureChip` is the cell-sized form for a blotter, a `StatusBar` item, or an adapter picker.
Wrap the four callbacks in `ActionGuard` — suspend and resume are `compensatable`, not free: a
suspension is undone by a resume, but instructions that did not land during it are not.

Reference screen: `templates/adapter-operations`.
