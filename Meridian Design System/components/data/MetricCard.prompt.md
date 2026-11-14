KPI tile mirroring the desktop `MetricCardStyle` — raised off-white surface with a **3px left-accent border** in the tone color, a small-caps label, a 24px mono value, and a signed delta (green up / red down).

```jsx
<MetricCard label="Net liquidation" value="$1,284,002.18" delta="+1.84% today" tone="success" />
<MetricCard label="Day P&L" value="-$4,118.22" delta="-0.32%" tone="danger" />
<MetricCard label="Open positions" value="24" tone="neutral" />
```

`tone` sets the left bar: `neutral | info | success | warning | danger`. Delta color is inferred from its leading sign (override with `trend`). Lay out 4–5 in a row.

4.0: a **stated figure** (P&L, total, tie-out difference) passes `envelope` — the facets minus value/label — and the tile's value renders as a `ValueEnvelope` with the standard marks and hover disclosure. Counts and rates keep the plain string.

```jsx
<MetricCard label="Difference" value="+$108.00" tone="warning"
  envelope={{ condition: "exception", authority: "derived", detail: "Statement − Ledger",
    evidence: [{ label: "Run SRUN-1044", ref: "evidence://recon/SRUN-1044" }],
    clocks: { asOf: "2026-06-30" } }} />
```

A11y (3.5): the group's accessible name carries value, trend + delta, context, non-neutral tone, and the optional `sparklineLabel` description.
