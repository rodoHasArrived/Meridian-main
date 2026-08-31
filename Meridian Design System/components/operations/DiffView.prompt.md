Before → after field changes for review/audit rails (governance approvals, config diffs, alert-rule edits). Struck red-washed before, green-washed after; rows where the values match render unwashed as context.

```jsx
<DiffView changes={[
  { field: "Threshold", before: "-5.0%", after: "-8.0%" },
  { field: "Channels", before: "email", after: "email · pager" },
  { field: "Owner", before: "r.alvarez", after: "r.alvarez" },  // unchanged → context row
]} />
```

Values are mono. Keep `field` labels short — they're small-caps. Pair with `SeverityBadge status="ReviewRequired"` and a `Timestamp` for the full audit-rail treatment.
