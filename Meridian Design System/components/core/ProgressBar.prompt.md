The system's one progress bar — batch operations, reconciliation runs, sync status, and any bounded ratio (utilisation, coverage, funded share). Squared track with a semantic fill; no radial dial and no second linear variant exist. Pass `value` (plus `max` when the scale isn't 0–100) for determinate, omit (or `null`) for an indeterminate sweep (respects `prefers-reduced-motion`). `target` draws a dotted datum at the reference ratio so the value can be read against it.

```jsx
<ProgressBar label="Backfill" value={412008} max={480000} showValue />
<ProgressBar label="Coverage" value={82} target={95} variant="warning" />
```
