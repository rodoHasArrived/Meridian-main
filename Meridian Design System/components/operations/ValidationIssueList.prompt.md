The recurring `{ code, severity, message, gate }` issue shape, rendered as a compact list — severity dot, mono code, message, optional gate tag. Use for validation/lint-style output, not for a single readiness verdict (that's `ReadinessPanel`).

```jsx
<ValidationIssueList issues={[
  { code: "RECON-204", severity: "Critical", message: "Custody cash break exceeds tolerance.", gate: "Reconciliation" },
  { code: "MAP-118", severity: "Warning", message: "Provider field 'cusip' unmapped for 12 rows.", gate: "SecurityMaster" },
]} />
```

`severity` accepts the plain `Info | Warning | Critical` vocabulary in addition to full readiness strings — both normalize onto the same five-severity palette as `SeverityBadge`.
