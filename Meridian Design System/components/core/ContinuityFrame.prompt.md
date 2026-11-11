Wrap any consequential chart, table, or analytical region that must keep the last-known-good evidence visible during delayed, stale, partial, offline, recovery, or error states.

```jsx
<ContinuityFrame state="offline" subject="Portfolio analytics"
  snapshotAt="10:42 UTC" coverage="96 of 96 positions">
  <TraceChart {...lastConfirmedView} />
</ContinuityFrame>
```

The frame names the degraded condition and marks unsafe states read-only; it does not blank the evidence. The child surface should avoid writes when `data-readonly="true"` is present.

**3.2 —** Pass `since` (ISO string, Date, or epoch ms) instead of a pre-formatted `age` and the frame runs its own clock — 1s cadence under a minute, 15s under an hour, 60s beyond. `dim` defaults to `"auto"`, fading and corner-marking retained content on stale, offline, and error — the same states already locked read-only. Supply `onRetry` wherever recovery is an operator action rather than a wait.

```jsx
<ContinuityFrame state="stale" subject="Book yield" since={lastGoodAt}
  snapshotAt="11:42 UTC" onRetry={refetch} retrying={isRefetching}>
  <TraceChart {...lastConfirmedView} />
</ContinuityFrame>
```
