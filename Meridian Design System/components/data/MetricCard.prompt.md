KPI tile mirroring the desktop `MetricCardStyle` — raised off-white surface with a **3px left-accent border** in the tone color, a small-caps label, a 24px mono value, and a signed delta (green up / red down).

```jsx
<MetricCard label="Net liquidation" value="$1,284,002.18" delta="+1.84% today" tone="success" />
<MetricCard label="Day P&L" value="-$4,118.22" delta="-0.32%" tone="danger" />
<MetricCard label="Open positions" value="24" tone="neutral" />
```

`tone` sets the left bar: `neutral | info | success | warning | danger`. Delta color is inferred from its leading sign (override with `trend`). Lay out 4–5 in a row.
