Vertical audit trail — who did what, when, with what evidence. Where `DiffView` shows one change, the timeline shows the sequence: severity-dotted rail, mono UTC time-of-day, optional day grouping. Events render in the order given (newest first by convention).

```jsx
<EventTimeline events={[
  { ts: t2, action: "Rule threshold raised", actor: "r.alvarez", severity: "warning",
    detail: "Drawdown breach -5.0% → -8.0% · window 15m",
    evidence: { label: "change-4118.json", status: "Ready", onOpen: open } },
  { ts: t1, action: "Backfill complete", actor: "scheduler", severity: "success",
    detail: "412,008 bars · 0 gaps" },
]} />
```

Keep `action` terse and declarative ("Backfill complete", not "The backfill finished!"). Put the proof in `evidence` (label + status + onOpen/href/route) and the numbers in `detail`. Use `dense` for rail placements inside an inspector. Pair with `DiffView` inside a detail render for field-level before → after.
