AR/AP aging — counterparty rows spread across age buckets, row totals, and a footer with bucket totals plus each bucket's share of the whole. Late buckets escalate: amber wash from `warnFrom` (default bucket 2), red on the last bucket — zero cells stay unwashed dashes.

```jsx
const { AgingTable, Money } = window.MeridianDesignSystem_4f61be;

<AgingTable
  currency="USD"
  rows={[
    { name: "Helios Capital",  ref: "AR-1102", amounts: [48200, 12800, 0, 0, 0] },
    { name: "Northgate Fund",  ref: "AR-1088", amounts: [0, 0, 22400, 0, 9120] },
  ]}
  onRowClick={(row) => openStatement(row)}
/>

// Bucketing raw invoices:
const idx = Money.agingBucketIndex(Money.daysBetween(inv.dueDate, asOf)); // 0..4
```

Non-obvious: `amounts` aligns positionally with `buckets` — pass custom labels (e.g. ["Current","1–15","16–45","45+"]) and the wash thresholds follow (`warnFrom` index, last bucket always red). Use `Money.agingBucketIndex` + `daysBetween` so bucket math matches the rendered schedule.
