The masthead readiness band — a row of label/value items tinted by state, for an always-visible evidence posture (readiness score, open breaks, provider count, approval state).

```jsx
<TrustStrip items={[
  { label: "Readiness", value: "86 / 100", state: "review" },
  { label: "Recon", value: "3 breaks", state: "blocked" },
  { label: "Providers", value: "4 live", state: "ready" },
  { label: "Approval", value: "Pending", state: "pending" },
]} />
```

Note `state="review"` reads **amber** here (attention), not the blue `SeverityBadge` uses for review — this component mirrors the app's masthead band, which prioritizes "needs a look" over the severity taxonomy's blue.
