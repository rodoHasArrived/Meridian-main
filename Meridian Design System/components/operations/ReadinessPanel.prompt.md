A readiness verdict block — `SeverityBadge` + title + detail + optional score, with a right-aligned action footer. Use to explain *why* something is blocked/ready, not just flag that it is.

```jsx
<ReadinessPanel
  state="ReviewRequired"
  score="86 / 100"
  title="Reconciliation"
  detail="3 open exceptions above tolerance in the custody cash lane."
  actions={<>
    <Button variant="ghost">Investigate</Button>
    <Button variant="primary">Approve</Button>
  </>}
/>
```

One `ReadinessPanel` per verdict; for a list of many small issues use `ValidationIssueList` inside its `children` instead of stacking several panels.
